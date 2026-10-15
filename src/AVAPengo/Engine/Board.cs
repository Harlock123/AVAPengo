namespace AVAPengo.Engine;

/// <summary>
/// The ice field: which cells hold a block, which blocks hide an egg, and where the three star blocks are.
/// Each round's field is a maze cut through solid ice: the corridors join the cells at even places, so
/// every cell can be reached, with a few more blocks knocked out to make loops.
/// </summary>
public sealed class Board
{
    public const int Cols = Field.Cols, Rows = Field.Rows;

    public readonly Tile[,] T = new Tile[Cols, Rows];
    public readonly bool[,] Egg = new bool[Cols, Rows];

    public static bool Inside(int c, int r) => Field.Inside(c, r);

    public Tile At(int c, int r) => Inside(c, r) ? T[c, r] : Tile.Ice;
    public bool Free(int c, int r) => Inside(c, r) && T[c, r] == Tile.Empty;

    public int Eggs => Egg.Cast<bool>().Count(e => e);

    public IEnumerable<(int C, int R)> Cells()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                yield return (c, r);
    }

    public List<(int C, int R)> Stars => Cells().Where(p => T[p.C, p.R] == Tile.Star).ToList();

    /// <summary>The three star blocks side by side in a row or a column; touching a wall if any of them does.</summary>
    public (bool Lined, bool AtWall) StarLine()
    {
        var s = Stars;
        if (s.Count != 3) return (false, false);
        bool row = s.All(p => p.R == s[0].R) && s.Max(p => p.C) - s.Min(p => p.C) == 2;
        bool col = s.All(p => p.C == s[0].C) && s.Max(p => p.R) - s.Min(p => p.R) == 2;
        bool wall = s.Any(p => p.C == 0 || p.C == Cols - 1 || p.R == 0 || p.R == Rows - 1);
        return (row || col, wall);
    }

    /// <summary>Steps from (c, r) to every cell, through cells <paramref name="pass"/> allows (-1 where it can't get).</summary>
    public int[,] Distances(int c, int r, Func<int, int, bool> pass)
    {
        var d = new int[Cols, Rows];
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                d[x, y] = -1;
        if (!Inside(c, r)) return d;
        var q = new Queue<(int, int)>();
        d[c, r] = 0;
        q.Enqueue((c, r));
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var w in Dirs.All)
            {
                var (dx, dy) = Dirs.Vec(w);
                int nx = x + dx, ny = y + dy;
                if (!Inside(nx, ny) || d[nx, ny] >= 0 || !pass(nx, ny)) continue;
                d[nx, ny] = d[x, y] + 1;
                q.Enqueue((nx, ny));
            }
        }
        return d;
    }

    /// <summary>Distances through the open cells.</summary>
    public int[,] Distances(int c, int r) => Distances(c, r, Free);

    /// <summary>A new field for a round, with <paramref name="eggs"/> eggs hidden in its ice.</summary>
    public static Board Build(Random rng, int eggs)
    {
        var b = new Board();
        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
                b.T[c, r] = Tile.Ice;

        // A maze through the cells at even places, from the middle out.
        var stack = new Stack<(int C, int R)>();
        b.T[Field.StartX, Field.StartY] = Tile.Empty;
        stack.Push((Field.StartX, Field.StartY));
        while (stack.Count > 0)
        {
            var (c, r) = stack.Peek();
            var ways = Dirs.All.Select(Dirs.Vec).Where(v => Inside(c + 2 * v.X, r + 2 * v.Y) && b.T[c + 2 * v.X, r + 2 * v.Y] == Tile.Ice).ToList();
            if (ways.Count == 0) { stack.Pop(); continue; }
            var (dx, dy) = ways[rng.Next(ways.Count)];
            b.T[c + dx, r + dy] = Tile.Empty;
            b.T[c + 2 * dx, r + 2 * dy] = Tile.Empty;
            stack.Push((c + 2 * dx, r + 2 * dy));
        }

        // Some loops, so a mite can't always be cornered (nor the penguin).
        var between = b.Cells().Where(p => b.T[p.C, p.R] == Tile.Ice && (p.C % 2 == 0) != (p.R % 2 == 0)).ToList();
        Shuffle(rng, between);
        foreach (var (c, r) in between.Take(8)) b.T[c, r] = Tile.Empty;

        // The three star blocks: away from the walls and the penguin, and not touching one another.
        var ice = b.Cells().Where(p => b.T[p.C, p.R] == Tile.Ice).ToList();
        Shuffle(rng, ice);
        var stars = new List<(int C, int R)>();
        foreach (var p in ice)
        {
            if (stars.Count == 3) break;
            if (p.C < 1 || p.C > Cols - 2 || p.R < 1 || p.R > Rows - 2) continue;
            if (Math.Abs(p.C - Field.StartX) + Math.Abs(p.R - Field.StartY) < 3) continue;
            if (stars.Any(s => Math.Abs(s.C - p.C) <= 2 && Math.Abs(s.R - p.R) <= 2)) continue;
            stars.Add(p);
        }
        foreach (var (c, r) in stars) b.T[c, r] = Tile.Star;

        // The eggs: in ice blocks well away from the penguin, spread about.
        var far = ice.Where(p => b.T[p.C, p.R] == Tile.Ice && Math.Abs(p.C - Field.StartX) + Math.Abs(p.R - Field.StartY) >= 5).ToList();
        var placed = new List<(int C, int R)>();
        foreach (int spread in new[] { 4, 2, 0 })
            foreach (var p in far)
            {
                if (placed.Count == eggs) break;
                if (b.Egg[p.C, p.R] || placed.Any(e => Math.Abs(e.C - p.C) + Math.Abs(e.R - p.R) < spread)) continue;
                b.Egg[p.C, p.R] = true;
                placed.Add(p);
            }
        return b;
    }

    /// <summary>A field drawn as text, for tests: '#' ice, 'e' ice with an egg, '*' a star block, '.' open.</summary>
    public static Board Parse(params string[] rows)
    {
        var b = new Board();
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                char ch = r < rows.Length && c < rows[r].Length ? rows[r][c] : '.';
                b.T[c, r] = ch switch { '#' or 'e' => Tile.Ice, '*' => Tile.Star, _ => Tile.Empty };
                b.Egg[c, r] = ch == 'e';
            }
        return b;
    }

    private static void Shuffle<T>(Random rng, List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
