namespace AVAPengo.Engine;

public sealed partial class GameEngine
{
    public const double PenguinSpeed = 5.0, BlockSpeed = 13;
    public const double StartSeconds = 2.6, ReadySeconds = 1.6, DyingSeconds = 2.2, ClearSeconds = 3.6;
    public const double HatchSeconds = 1.2, PushSeconds = 0.16, StarStunSeconds = 8, HurrySeconds = 60, FleeSeconds = 2;
    public const int FirstExtraLife = 30000, ExtraLifeEvery = 50000;
    public const int IcePoints = 30, EggPoints = 500, StampPoints = 100;

    /// <summary>What crushing mites with one block is worth: one, two, three, four or more at once.</summary>
    public static readonly int[] CrushPoints = [400, 1600, 3200, 6400];

    /// <summary>The bonus for a round cleared quickly: (under so many seconds, points).</summary>
    public static readonly (int Seconds, int Points)[] TimeBonus = [(20, 5000), (30, 2000), (40, 1000), (50, 500), (60, 10)];

    public const int StarPoints = 10000, StarPointsAtWall = 5000;

    public int Round { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Board Board { get; private set; } = new();
    public Penguin Penguin { get; } = new();
    public List<Mite> Mites { get; } = [];
    public List<Block> Sliding { get; } = [];
    public PenguinPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double RoundTime { get; private set; }
    public bool Hurried { get; private set; }
    /// <summary>Whether the star bonus has been had this round (only once).</summary>
    public bool StarsDone { get; private set; }
    /// <summary>How much longer each wall (indexed by the way it lies from the field) shakes.</summary>
    public double[] Shaking { get; } = new double[5];
    /// <summary>For tests: no eggs hatch, nothing flees, and a round never ends.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra;
    private double _hatchIn, _fleeIn, _pushLatch;
    private bool _firePrev;
    private int[,] _toPenguin = new int[Field.Cols, Field.Rows];

    /// <summary>How many mites a round holds, all of them hatching from eggs in the ice.</summary>
    public static int MitesIn(int round) => Math.Min(10, 6 + (round - 1) / 2);

    /// <summary>How many mites are about at once (the rest wait in their eggs).</summary>
    public static int Awake(int round) => Math.Min(4, 3 + (round - 1) / 3);

    public double MiteSpeed => Math.Min(4.3, 2.5 + 0.13 * (Round - 1)) * (Hurried ? 1.3 : 1);
    public double StunSeconds => Math.Max(2.5, 5 - 0.15 * (Round - 1));
    public double EatSeconds => Math.Max(0.35, 0.9 - 0.04 * (Round - 1));
    /// <summary>How often a mite chews through the ice on its way to the penguin rather than going round.</summary>
    private double Appetite => Math.Min(0.4, 0.06 + 0.025 * (Round - 1)) + (Hurried ? 0.15 : 0);
    private double Keenness => Math.Min(0.85, 0.4 + 0.035 * (Round - 1)) + (Hurried ? 0.1 : 0);

    private static readonly string[] Hints =
        ["CRUSH THE MITES WITH THE ICE", "KICK A WALL TO DAZE THEM", "LINE UP THE STAR BLOCKS", "BREAK THE EGGS BEFORE THEY HATCH", "THE QUICKER, THE BIGGER THE BONUS"];

    public static int TimeBonusFor(double seconds) => TimeBonus.FirstOrDefault(t => seconds < t.Seconds).Points;

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Round = 1;
        _nextExtra = FirstExtraLife;
        LayOut();
        SetPhase(Phase.Start);
    }

    private void BeginRound(int round)
    {
        Round = round;
        LayOut();
        SetPhase(Phase.Start);
        Say($"ROUND {round}", Hints[(round - 1) % Hints.Length], StartSeconds);
        Play(Sfx.RoundStart);
    }

    private void LayOut()
    {
        Board = Board.Build(_rng, MitesIn(Round));
        Mites.Clear();
        Sliding.Clear();
        Effects.Clear();
        Array.Clear(Shaking);
        RoundTime = 0;
        Hurried = StarsDone = false;
        _hatchIn = 0;
        _fleeIn = FleeSeconds;
        _pushLatch = 0;
        Penguin.Place(Field.StartX, Field.StartY);
        Penguin.Facing = Dir.Down;
        Penguin.Alive = true;
        Penguin.PushTime = 0;
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        for (int i = 0; i < Shaking.Length; i++) Shaking[i] = Math.Max(0, Shaking[i] - dt);
        switch (Phase)
        {
            case Phase.Start:
                _firePrev = c.Fire;
                if (PhaseTime > (Mites.Count == 0 ? StartSeconds : ReadySeconds))
                {
                    SetPhase(Phase.Play);
                    if (Mites.Count == 0 && !Calm)
                        for (int k = 0; k < Awake(Round); k++) Hatch();
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                RoundTime += dt;
                if (!Hurried && !Calm && RoundTime > HurrySeconds)
                {
                    Hurried = true;
                    Say("HURRY!", null, 1.5);
                    Play(Sfx.Hurry);
                }
                MovePenguin(c, dt);
                MoveBlocks(dt);
                _toPenguin = Board.Distances(Penguin.Cx, Penguin.Cy, MiteCanEnter);
                MoveMites(dt);
                Contacts();
                if (Phase == Phase.Play) Progress(dt);
                break;
            case Phase.Dying:
                MoveBlocks(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                }
                break;
            case Phase.Clear:
                if (PhaseTime > ClearSeconds) BeginRound(Round + 1);
                break;
        }
    }

    // ───────────────────────────── the penguin ─────────────────────────────

    private bool BlockAt(int c, int r) => Sliding.Any(b => (b.Cx == c && b.Cy == r) || (b.Tx == c && b.Ty == r));

    private bool PenguinCanEnter(int c, int r) => Board.Free(c, r) && !BlockAt(c, r);

    private bool MiteCanEnter(int c, int r) => Board.Free(c, r) && !BlockAt(c, r);

    private void MovePenguin(Controls c, double dt)
    {
        var p = Penguin;
        bool pressed = c.Fire && !_firePrev;
        _firePrev = c.Fire;
        if (pressed) _pushLatch = 0.25;
        else _pushLatch = Math.Max(0, _pushLatch - dt);
        if (p.PushTime > 0) { p.PushTime -= dt; return; }

        // Two ways at once: take the one across the way it's going, so it turns.
        Dir want = c.Dx != 0 && c.Dy != 0
            ? (Dirs.Horizontal(p.Dir == Dir.None ? p.Facing : p.Dir) ? Dirs.Of(0, c.Dy) : Dirs.Of(c.Dx, 0))
            : Dirs.Of(c.Dx, c.Dy);

        double dist = PenguinSpeed * dt;
        for (int guard = 0; guard < 3 && dist > 0; guard++)
        {
            if (p.AtCentre)
            {
                if (want != Dir.None) p.Facing = want;
                if (_pushLatch > 0)
                {
                    _pushLatch = 0;
                    Push(p.Facing);
                    return;
                }
                var (dx, dy) = Dirs.Vec(want);
                if (want == Dir.None || !PenguinCanEnter(p.Cx + dx, p.Cy + dy)) return;
                p.Head(want);
            }
            else if (want != Dir.None && want == Dirs.Opposite(p.Dir))
            {
                // Back the way it came.
                var (dx, dy) = Dirs.Vec(want);
                (p.Tx, p.Ty) = (p.Tx + dx, p.Ty + dy);
                p.Dir = p.Facing = want;
            }
            dist = p.Glide(dist);
        }
    }

    /// <summary>The push button: send the block in front sliding, break it if it can't move, or kick the wall.</summary>
    private void Push(Dir d)
    {
        var p = Penguin;
        p.PushTime = PushSeconds;
        var (dx, dy) = Dirs.Vec(d);
        int bx = p.Cx + dx, by = p.Cy + dy;
        if (!Field.Inside(bx, by))
        {
            ShakeWall(d);
            return;
        }
        var kind = Board.T[bx, by];
        if (kind == Tile.Empty) return;
        int nx = bx + dx, ny = by + dy;
        if (BlockCanEnter(nx, ny))
        {
            var b = new Block { Kind = kind, Egg = Board.Egg[bx, by] };
            b.Place(bx, by);
            b.Head(d);
            Board.T[bx, by] = Tile.Empty;
            Board.Egg[bx, by] = false;
            Sliding.Add(b);
            Play(Sfx.Push);
        }
        else if (kind == Tile.Ice) Shatter(bx, by);
        else Play(Sfx.Thud);
    }

    private bool BlockCanEnter(int c, int r)
    {
        if (!Board.Free(c, r)) return false;
        var p = Penguin;
        if ((p.Cx == c && p.Cy == r) || (p.Tx == c && p.Ty == r)) return false;
        return !BlockAt(c, r);
    }

    private void Shatter(int c, int r)
    {
        var (x, y) = Field.Px(c, r);
        bool egg = Board.Egg[c, r];
        Board.T[c, r] = Tile.Empty;
        Board.Egg[c, r] = false;
        Effects.Add(new Effect { Kind = EffectKind.Shards, X = x, Y = y, Colour = egg ? 0xFFFFE890 : 0xFFB8F0FF, Life = 0.5 });
        if (egg)
        {
            AddScore(EggPoints, x, y);
            Play(Sfx.EggBreak);
        }
        else
        {
            AddScore(IcePoints);
            Play(Sfx.Shatter);
        }
    }

    /// <summary>A kick at a wall shakes it, and dazes every mite against it.</summary>
    private void ShakeWall(Dir side)
    {
        Shaking[(int)side] = 0.5;
        Play(Sfx.Shake);
        foreach (var m in Mites.Where(m => !m.Dead && m.State is MiteState.Roaming or MiteState.Eating or MiteState.Stunned))
        {
            bool against = side switch
            {
                Dir.Left => m.X < 0.5,
                Dir.Right => m.X > Field.Cols - 1.5,
                Dir.Up => m.Y < 0.5,
                _ => m.Y > Field.Rows - 1.5,
            };
            if (against) Stun(m, StunSeconds);
        }
    }

    private void Stun(Mite m, double seconds)
    {
        if (m.State == MiteState.Eating) { m.Tx = m.Cx; m.Ty = m.Cy; }
        m.State = MiteState.Stunned;
        m.Timer = seconds;
    }

    // ───────────────────────────── sliding blocks ─────────────────────────────

    private void MoveBlocks(double dt)
    {
        foreach (var b in Sliding.ToList())
        {
            double dist = BlockSpeed * dt;
            var (dx, dy) = Dirs.Vec(b.Facing);
            for (int guard = 0; guard < 4; guard++)
            {
                Catch(b);
                if (b.AtCentre)
                {
                    if (!BlockCanEnter(b.Cx + dx, b.Cy + dy)) { Stop(b); break; }
                    b.Head(b.Facing);
                }
                if (dist <= 0) break;
                dist = b.Glide(dist);
            }
            foreach (var m in b.Caught) (m.X, m.Y) = (b.X + dx * 0.55, b.Y + dy * 0.55);
        }
    }

    /// <summary>Picks up any mite in front of a sliding block.</summary>
    private void Catch(Block b)
    {
        var (dx, dy) = Dirs.Vec(b.Facing);
        foreach (var m in Mites.Where(m => !m.Dead && m.State != MiteState.Caught))
        {
            double along = (m.X - b.X) * dx + (m.Y - b.Y) * dy;
            double across = Math.Abs((m.X - b.X) * dy) + Math.Abs((m.Y - b.Y) * dx);
            if (across < 0.55 && along > 0 && along < 1.0)
            {
                m.State = MiteState.Caught;
                m.CaughtBy = b;
                b.Caught.Add(m);
            }
        }
    }

    private void Stop(Block b)
    {
        Sliding.Remove(b);
        Board.T[b.Cx, b.Cy] = b.Kind;
        Board.Egg[b.Cx, b.Cy] = b.Egg;
        if (b.Caught.Count > 0)
        {
            foreach (var m in b.Caught) m.Dead = true;
            var (dx, dy) = Dirs.Vec(b.Facing);
            var (mx, my) = Field.Px(b.X + dx * 0.55, b.Y + dy * 0.55);
            AddScore(CrushPoints[Math.Min(b.Caught.Count, CrushPoints.Length) - 1], mx, my);
            Effects.Add(new Effect { Kind = EffectKind.Shards, X = mx, Y = my, Colour = 0xFF7CFF5A, Life = 0.6 });
            Play(Sfx.Crush);
            _hatchIn = Math.Max(_hatchIn, 0.8);
        }
        else Play(Sfx.Thud);
        if (b.Kind == Tile.Star && !StarsDone && Board.StarLine() is (true, var atWall))
        {
            StarsDone = true;
            int bonus = atWall ? StarPointsAtWall : StarPoints;
            AddScore(bonus);
            Say("STAR BONUS", bonus.ToString(), 2.5);
            foreach (var s in Board.Stars)
            {
                var (sx, sy) = Field.Px(s.C, s.R);
                Effects.Add(new Effect { Kind = EffectKind.Stars, X = sx, Y = sy, Life = 2.0 });
            }
            foreach (var m in Mites.Where(m => !m.Dead && m.State is MiteState.Roaming or MiteState.Eating or MiteState.Stunned)) Stun(m, StarStunSeconds);
            Play(Sfx.StarBonus);
        }
    }

    // ───────────────────────────── the mites ─────────────────────────────

    /// <summary>A mite out of one of the eggs left in the ice (not one right by the penguin, if it can help it).</summary>
    private void Hatch()
    {
        var eggs = Board.Cells().Where(p => Board.Egg[p.C, p.R]).ToList();
        if (eggs.Count == 0) return;
        var p = Penguin;
        var far = eggs.Where(e => Math.Abs(e.C - p.Cx) + Math.Abs(e.R - p.Cy) >= 4).ToList();
        var (c, r) = (far.Count > 0 ? far : eggs)[_rng.Next((far.Count > 0 ? far : eggs).Count)];
        Board.T[c, r] = Tile.Empty;
        Board.Egg[c, r] = false;
        var m = new Mite { State = MiteState.Hatching, Timer = HatchSeconds, Facing = Dirs.All[_rng.Next(4)] };
        m.Place(c, r);
        Mites.Add(m);
        var (x, y) = Field.Px(c, r);
        Effects.Add(new Effect { Kind = EffectKind.Shards, X = x, Y = y, Colour = 0xFFB8F0FF, Life = 0.5 });
        Play(Sfx.Hatch);
    }

    private void MoveMites(double dt)
    {
        foreach (var m in Mites.Where(m => !m.Dead))
        {
            m.Age += dt;
            switch (m.State)
            {
                case MiteState.Caught:
                    break;
                case MiteState.Hatching:
                case MiteState.Stunned:
                    m.Timer -= dt;
                    if (m.Timer <= 0) m.State = MiteState.Roaming;
                    break;
                case MiteState.Eating:
                    if (Board.At(m.EatX, m.EatY) != Tile.Ice) { m.State = MiteState.Roaming; break; }
                    m.Timer -= dt;
                    if (m.Timer > 0) break;
                    Board.T[m.EatX, m.EatY] = Tile.Empty;
                    var (x, y) = Field.Px(m.EatX, m.EatY);
                    Effects.Add(new Effect { Kind = EffectKind.Shards, X = x, Y = y, Colour = 0xFFB8F0FF, Life = 0.4, Size = 0.7 });
                    m.State = MiteState.Roaming;
                    m.Head(m.Facing);
                    break;
                case MiteState.Roaming:
                    double dist = MiteSpeed * (m.Fleeing ? 1.25 : 1) * dt;
                    // A block has come to rest where it was going: back the way it came.
                    if (!m.AtCentre && !Board.Free(m.Tx, m.Ty))
                    {
                        var (bx, by) = Dirs.Vec(m.Dir);
                        (m.Tx, m.Ty) = (m.Tx - bx, m.Ty - by);
                        m.Dir = m.Facing = Dirs.Opposite(m.Dir);
                    }
                    for (int guard = 0; guard < 3 && dist > 0; guard++)
                    {
                        if (m.AtCentre)
                        {
                            if (m.Fleeing && Corner(m) == (m.Cx, m.Cy)) { Escape(m); break; }
                            Choose(m);
                            if (m.State != MiteState.Roaming || m.AtCentre) break;
                        }
                        dist = m.Glide(dist);
                    }
                    break;
            }
        }
    }

    private static (int C, int R) Corner(Mite m) =>
        (m.X < Field.Cols / 2.0 ? 0 : Field.Cols - 1, m.Y < Field.Rows / 2.0 ? 0 : Field.Rows - 1);

    /// <summary>Which way a mite goes from a cell centre: after the penguin (or a corner, fleeing), or anywhere; chewing through the ice when it must, or when it fancies.</summary>
    private void Choose(Mite m)
    {
        var opts = Dirs.All.Where(d => { var (dx, dy) = Dirs.Vec(d); return MiteCanEnter(m.Cx + dx, m.Cy + dy); }).ToList();
        if (opts.Count > 1) opts.Remove(Dirs.Opposite(m.Facing));

        int[,]? dist = null;
        (int C, int R) goal = (Penguin.Cx, Penguin.Cy);
        bool hunt = m.Fleeing || _rng.NextDouble() < Keenness;
        if (m.Fleeing)
        {
            goal = Corner(m);
            dist = Board.Distances(goal.C, goal.R, MiteCanEnter);
        }
        else if (hunt) dist = _toPenguin;

        Dir best = Dir.None;
        if (hunt && dist != null)
        {
            int bestD = int.MaxValue;
            foreach (var d in opts)
            {
                var (dx, dy) = Dirs.Vec(d);
                int v = dist[m.Cx + dx, m.Cy + dy];
                if (v >= 0 && v < bestD) { bestD = v; best = d; }
            }
        }
        // Can't get there through the open cells (or doesn't fancy going round): chew a way through.
        if (hunt && (best == Dir.None || _rng.NextDouble() < Appetite) && Chew(m, goal)) return;
        if (best == Dir.None && opts.Count > 0) best = opts[_rng.Next(opts.Count)];
        if (best == Dir.None)
        {
            Chew(m, goal);
            return;
        }
        m.Head(best);
    }

    /// <summary>Starts chewing the ice block between a mite and where it wants to go, if there is one.</summary>
    private bool Chew(Mite m, (int C, int R) goal)
    {
        int ddx = goal.C - m.Cx, ddy = goal.R - m.Cy;
        var ways = new List<Dir>();
        if (Math.Abs(ddx) >= Math.Abs(ddy)) { ways.Add(Dirs.Of(ddx, 0)); ways.Add(Dirs.Of(0, ddy)); }
        else { ways.Add(Dirs.Of(0, ddy)); ways.Add(Dirs.Of(ddx, 0)); }
        foreach (var d in ways.Where(d => d != Dir.None))
        {
            var (dx, dy) = Dirs.Vec(d);
            int c = m.Cx + dx, r = m.Cy + dy;
            if (Board.At(c, r) != Tile.Ice || !Field.Inside(c, r) || Board.Egg[c, r] || BlockAt(c, r)) continue;
            m.State = MiteState.Eating;
            m.Facing = d;
            (m.EatX, m.EatY) = (c, r);
            m.Timer = EatSeconds;
            Play(Sfx.Chew);
            return true;
        }
        return false;
    }

    private void Escape(Mite m)
    {
        m.Dead = true;
        var (x, y) = Field.Px(m.X, m.Y);
        Effects.Add(new Effect { Kind = EffectKind.Word, X = x, Y = y, Text = "BYE!", Colour = 0xFF7CFF5A, Life = 1.2 });
        Say("IT GOT AWAY", null, 1.5);
        Play(Sfx.Escape);
    }

    // ───────────────────────────── meetings, and the round's progress ─────────────────────────────

    private void Contacts()
    {
        var p = Penguin;
        if (!p.Alive) return;
        foreach (var m in Mites.Where(m => !m.Dead && m.State != MiteState.Caught))
        {
            if (Math.Abs(m.X - p.X) + Math.Abs(m.Y - p.Y) > 0.75) continue;
            if (m.State == MiteState.Stunned)
            {
                m.Dead = true;
                var (x, y) = Field.Px(m.X, m.Y);
                AddScore(StampPoints, x, y);
                Play(Sfx.Stamp);
                _hatchIn = Math.Max(_hatchIn, 0.8);
            }
            else if (m.State != MiteState.Hatching)
            {
                Die("CAUGHT");
                return;
            }
        }
    }

    private void Die(string cause)
    {
        Cause = cause;
        Penguin.Alive = false;
        Lives--;
        SetPhase(Phase.Dying);
        Music(false);
        Play(Sfx.Death);
    }

    /// <summary>The next penguin, back in the middle, with the mites moved well away from it.</summary>
    private void Respawn()
    {
        var p = Penguin;
        foreach (var b in Sliding.ToList()) Stop(b);
        var near = Board.Cells().Where(q => Board.Free(q.C, q.R))
            .OrderBy(q => Math.Abs(q.C - Field.StartX) + Math.Abs(q.R - Field.StartY)).First();
        p.Place(near.C, near.R);
        p.Alive = true;
        p.Facing = Dir.Down;
        p.PushTime = 0;
        var dist = Board.Distances(p.Cx, p.Cy);
        var far = Board.Cells().Where(q => dist[q.C, q.R] >= 6 || (dist[q.C, q.R] < 0 && Board.Free(q.C, q.R))).ToList();
        if (far.Count == 0) far = Board.Cells().Where(q => Board.Free(q.C, q.R) && Math.Abs(q.C - p.Cx) + Math.Abs(q.R - p.Cy) >= 3).ToList();
        foreach (var m in Mites.Where(m => !m.Dead))
        {
            var (c, r) = far[_rng.Next(far.Count)];
            m.Place(c, r);
            m.State = MiteState.Roaming;
            m.CaughtBy = null;
        }
        SetPhase(Phase.Start);
        Say("READY!", null, ReadySeconds);
        _firePrev = true;
    }

    private void Progress(double dt)
    {
        if (Calm) return;
        int awake = Mites.Count(m => !m.Dead);
        int eggs = Board.Eggs;
        if (eggs > 0 && awake < Awake(Round))
        {
            _hatchIn -= dt;
            if (_hatchIn <= 0)
            {
                Hatch();
                _hatchIn = 1.0;
            }
        }
        // The last mite runs for it.
        if (eggs == 0 && awake == 1 && Mites.First(m => !m.Dead) is { Fleeing: false } last && last.State != MiteState.Hatching)
        {
            _fleeIn -= dt;
            if (_fleeIn <= 0)
            {
                last.Fleeing = true;
                Play(Sfx.Flee);
            }
        }
        if (eggs == 0 && awake == 0 && Sliding.Count == 0)
        {
            SetPhase(Phase.Clear);
            Music(false);
            int bonus = TimeBonusFor(RoundTime);
            AddScore(bonus);
            Say("ROUND CLEAR", $"TIME {(int)RoundTime} SECONDS - BONUS {bonus}", ClearSeconds);
            Play(Sfx.Clear);
        }
    }

    private void AddScore(int points, double? x = null, double? y = null)
    {
        Score += points;
        if (x is { } px && y is { } py)
            Effects.Add(new Effect { Kind = EffectKind.Score, X = px, Y = py, Text = points.ToString(), Colour = points >= 1600 ? 0xFFFFE14A : 0xFFFFFFFF, Life = 1.0 });
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a round (its first mites hatching, unless calm).</summary>
    internal void Go(int round = 1)
    {
        StartGame();
        BeginRound(round);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
        if (!Calm)
            for (int k = 0; k < Awake(Round); k++) Hatch();
    }

    /// <summary>A field of one's own, with nothing on it.</summary>
    internal void SetBoard(params string[] rows)
    {
        Board = Board.Parse(rows);
        Mites.Clear();
        Sliding.Clear();
    }

    internal void PutPenguin(int c, int r, Dir facing = Dir.Down)
    {
        Penguin.Place(c, r);
        Penguin.Facing = facing;
        Penguin.Alive = true;
        Penguin.PushTime = 0;
    }

    internal Mite PutMite(int c, int r, MiteState state = MiteState.Roaming, Dir facing = Dir.Left)
    {
        var m = new Mite { State = state, Facing = facing, Timer = state == MiteState.Stunned ? StunSeconds : HatchSeconds };
        m.Place(c, r);
        Mites.Add(m);
        return m;
    }

    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
    internal void SetRoundTime(double t) => RoundTime = t;
}
