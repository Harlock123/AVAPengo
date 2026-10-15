namespace AVAPengo.Engine;

/// <summary>
/// The computer penguin, for the demonstration (and the tests). At each cell it weighs up everything it
/// could do from every cell it can safely reach: send a block sliding down a corridor with mites in it,
/// break an egg, kick a wall with mites against it, stamp on a dazed mite, line up the star blocks, and
/// goes for the best, less the walk. With a mite close by and nothing to hit it with, it backs away.
/// </summary>
public sealed class PenguinPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    private static Controls Press(Dir d, bool fire = false)
    {
        var (dx, dy) = Dirs.Vec(d);
        return new Controls(dx, dy, fire);
    }

    private static bool Dangerous(Mite m) => !m.Dead && m.State is MiteState.Roaming or MiteState.Eating;

    public Controls Decide(GameEngine g)
    {
        var p = g.Penguin;
        if (g.Phase != Phase.Play || !p.Alive || p.PushTime > 0) return default;
        var danger = g.Mites.Where(Dangerous).ToList();
        if (!p.AtCentre)
        {
            // Turn back rather than walk into a mite.
            var (dx, dy) = Dirs.Vec(p.Dir);
            if (danger.Any(m => Math.Abs(m.X - p.Tx - dx * 0.5) + Math.Abs(m.Y - p.Ty - dy * 0.5) < 1.2 && (m.X - p.X) * dx + (m.Y - p.Y) * dy > 0))
            {
                Doing = "retreat";
                return Press(Dirs.Opposite(p.Dir));
            }
            return Press(p.Dir);
        }

        var b = g.Board;
        int pc = p.Cx, pr = p.Cy;
        bool Busy(int c, int r) => g.Sliding.Any(s => (s.Cx == c && s.Cy == r) || (s.Tx == c && s.Ty == r));
        bool Open(int c, int r) => b.Free(c, r) && !Busy(c, r);

        // Cells next to a mite on the loose are no place to walk.
        var unsafeCells = new HashSet<(int, int)>();
        foreach (var m in danger)
            foreach (var (c, r) in new[] { (m.Cx, m.Cy), (m.Tx, m.Ty) })
            {
                unsafeCells.Add((c, r));
                foreach (var w in Dirs.All) { var (dx, dy) = Dirs.Vec(w); unsafeCells.Add((c + dx, r + dy)); }
            }
        unsafeCells.Remove((pc, pr));

        // Steps to every cell it can safely reach, and the first step of the way.
        var dist = new int[Field.Cols, Field.Rows];
        var first = new Dir[Field.Cols, Field.Rows];
        for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
                dist[c, r] = -1;
        var q = new Queue<(int, int)>();
        dist[pc, pr] = 0;
        q.Enqueue((pc, pr));
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var w in Dirs.All)
            {
                var (dx, dy) = Dirs.Vec(w);
                int nx = x + dx, ny = y + dy;
                if (!Field.Inside(nx, ny) || dist[nx, ny] >= 0 || !Open(nx, ny) || unsafeCells.Contains((nx, ny))) continue;
                dist[nx, ny] = dist[x, y] + 1;
                first[nx, ny] = dist[x, y] == 0 ? w : first[x, y];
                q.Enqueue((nx, ny));
            }
        }

        var targets = g.Mites.Where(m => !m.Dead && m.State != MiteState.Caught).ToList();
        var stars = b.Stars;

        double Value(int c, int r, Dir d, out string what)
        {
            what = "";
            var (dx, dy) = Dirs.Vec(d);
            int bx = c + dx, by = r + dy;
            if (!Field.Inside(bx, by))
            {
                // Kick the wall: worth it with mites on the loose against it.
                int against = danger.Count(m => d switch
                {
                    Dir.Left => m.X < 0.5, Dir.Right => m.X > Field.Cols - 1.5, Dir.Up => m.Y < 0.5, _ => m.Y > Field.Rows - 1.5,
                } && m.State == MiteState.Roaming);
                what = "kick";
                return 70 * against;
            }
            var kind = b.T[bx, by];
            if (kind == Tile.Empty || Busy(bx, by)) return 0;
            int nx = bx + dx, ny = by + dy;
            if (Open(nx, ny))
            {
                // Where it would slide, and who it would take with it.
                var path = new List<(int C, int R)>();
                for (int x = nx, y = ny; Open(x, y) && !(x == c && y == r); x += dx, y += dy) path.Add((x, y));
                int caught = targets.Count(m => path.Contains((m.Cx, m.Cy)) || path.Contains((m.Tx, m.Ty)));
                double v = caught * 130 + (caught > 1 ? 60 * caught : 0);
                if (kind == Tile.Star && !g.StarsDone && path.Count > 0)
                {
                    var rest = path[^1];
                    var others = stars.Where(s => s != (bx, by)).Append(rest).ToList();
                    bool row = others.All(s => s.R == others[0].R) && others.Max(s => s.C) - others.Min(s => s.C) == 2;
                    bool col = others.All(s => s.C == others[0].C) && others.Max(s => s.R) - others.Min(s => s.R) == 2;
                    if (row || col) v += 300;
                }
                what = caught > 0 ? $"crush {caught}" : "slide";
                return v > 0 ? v : kind == Tile.Ice ? 1.5 : 0;
            }
            if (kind != Tile.Ice) return 0;
            what = b.Egg[bx, by] ? "egg" : "break";
            return b.Egg[bx, by] ? 90 : 2;
        }

        double best = 0;
        (int C, int R, Dir D, bool Push) plan = (pc, pr, Dir.None, false);
        string doing = "wait";
        for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
            {
                if (dist[c, r] < 0) continue;
                double walk = dist[c, r] * 6;
                // A dazed mite to stamp on.
                if (targets.Any(m => m.State == MiteState.Stunned && m.Cx == c && m.Cy == r && m.Timer > dist[c, r] / GameEngine.PenguinSpeed + 0.2) && 110 - walk > best)
                {
                    best = 110 - walk;
                    plan = (c, r, Dir.None, false);
                    doing = "stamp";
                }
                foreach (var d in Dirs.All)
                {
                    double v = Value(c, r, d, out string what) - walk;
                    if (v > best) { best = v; plan = (c, r, d, true); doing = what; }
                }
            }

        // Nothing worth doing nearby, or a mite too close for comfort: get away from the mites.
        double near = danger.Count == 0 ? 99 : danger.Min(m => Math.Abs(m.X - pc) + Math.Abs(m.Y - pr));
        bool hitNow = plan.C == pc && plan.R == pr && plan.Push && best >= 100;
        if (near < 2.1 && !hitNow)
        {
            Dir away = Dir.None;
            double far = near;
            foreach (var w in Dirs.All)
            {
                var (dx, dy) = Dirs.Vec(w);
                if (!Open(pc + dx, pr + dy)) continue;
                double v = danger.Min(m => Math.Abs(m.X - pc - dx) + Math.Abs(m.Y - pr - dy));
                if (v > far) { far = v; away = w; }
            }
            if (away != Dir.None) { Doing = "flee"; return Press(away); }
            // Cornered: break or send off whatever is between it and the mite.
            var m0 = danger.OrderBy(m => Math.Abs(m.X - pc) + Math.Abs(m.Y - pr)).First();
            foreach (var w in Dirs.All)
            {
                var (dx, dy) = Dirs.Vec(w);
                if (b.At(pc + dx, pr + dy) != Tile.Empty && (m0.X - pc) * dx + (m0.Y - pr) * dy > 0) { Doing = "bash"; return Press(w, true); }
            }
        }

        if (plan.D == Dir.None && !plan.Push)
        {
            if (plan.C == pc && plan.R == pr) { Doing = doing; return default; }
        }
        Doing = doing;
        if (plan.C == pc && plan.R == pr) return Press(plan.D, fire: true);
        return Press(first[plan.C, plan.R]);
    }
}
