namespace AVAPengo.Engine;

/// <summary>The stick (four ways) and the push button.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

public enum Dir { None, Up, Down, Left, Right }

public static class Dirs
{
    public static readonly Dir[] All = [Dir.Up, Dir.Left, Dir.Down, Dir.Right];

    public static (int X, int Y) Vec(Dir d) => d switch { Dir.Up => (0, -1), Dir.Down => (0, 1), Dir.Left => (-1, 0), Dir.Right => (1, 0), _ => (0, 0) };

    public static Dir Opposite(Dir d) => d switch { Dir.Up => Dir.Down, Dir.Down => Dir.Up, Dir.Left => Dir.Right, Dir.Right => Dir.Left, _ => Dir.None };

    public static bool Horizontal(Dir d) => d is Dir.Left or Dir.Right;

    public static Dir Of(int dx, int dy) => dx > 0 ? Dir.Right : dx < 0 ? Dir.Left : dy > 0 ? Dir.Down : dy < 0 ? Dir.Up : Dir.None;
}

/// <summary>
/// The screen and the ice field on it. Positions are in cells: a cell's centre is at whole numbers, (0, 0)
/// top left to (12, 14) bottom right. Walls run all the way round the field.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int Cols = 13, Rows = 15;
    public const double Cell = 42;
    public const double X0 = (Width - Cols * Cell) / 2, Y0 = 92;
    /// <summary>Where the penguin starts each round: the middle of the field.</summary>
    public const int StartX = 6, StartY = 6;

    /// <summary>Where a point in cells is on the screen.</summary>
    public static (double X, double Y) Px(double x, double y) => (X0 + (x + 0.5) * Cell, Y0 + (y + 0.5) * Cell);

    public static bool Inside(int c, int r) => c >= 0 && c < Cols && r >= 0 && r < Rows;
}

/// <summary>Anything that moves about the field, from cell centre to cell centre.</summary>
public class Mover
{
    public double X, Y;
    /// <summary>The cell it is heading for: its own cell while it stands.</summary>
    public int Tx, Ty;
    /// <summary>The way it is going (None while it stands).</summary>
    public Dir Dir;
    public Dir Facing = Dir.Down;
    public int Cx => (int)Math.Round(X);
    public int Cy => (int)Math.Round(Y);
    public bool AtCentre => X == Tx && Y == Ty;

    public void Place(int c, int r)
    {
        (X, Y, Tx, Ty, Dir) = (c, r, c, r, Dir.None);
    }

    /// <summary>Heads one cell the given way.</summary>
    public void Head(Dir d)
    {
        var (dx, dy) = Dirs.Vec(d);
        (Tx, Ty, Dir) = (Cx + dx, Cy + dy, d);
        Facing = d;
    }

    /// <summary>Moves up to <paramref name="dist"/> cells towards its target; returns what is left over if it gets there.</summary>
    public double Glide(double dist)
    {
        double left = Math.Abs(Tx - X) + Math.Abs(Ty - Y);
        if (dist >= left)
        {
            (X, Y, Dir) = (Tx, Ty, Dir.None);
            return dist - left;
        }
        var (dx, dy) = Dirs.Vec(Dir);
        X += dx * dist;
        Y += dy * dist;
        return 0;
    }
}

public sealed class Penguin : Mover
{
    public bool Alive = true;
    /// <summary>A moment after a push, or a kick at the wall, before it can move again.</summary>
    public double PushTime;
}

public enum MiteState
{
    /// <summary>Just out of its egg: a moment before it moves, when it is harmless.</summary>
    Hatching,
    Roaming,
    /// <summary>Dazed by a shaken wall or the star bonus: harmless, and the penguin can stamp on it.</summary>
    Stunned,
    /// <summary>Chewing through an ice block.</summary>
    Eating,
    /// <summary>Carried along in front of a sliding block, to be crushed when it stops.</summary>
    Caught,
}

public sealed class Mite : Mover
{
    public MiteState State = MiteState.Hatching;
    public bool Dead;
    /// <summary>The last of a round's mites runs for a corner of the field, to escape.</summary>
    public bool Fleeing;
    public double Age, Timer;
    /// <summary>The block it is chewing through.</summary>
    public int EatX, EatY;
    public Block? CaughtBy;
    public bool Harmless => State is MiteState.Hatching or MiteState.Stunned or MiteState.Caught;
}

public enum Tile : byte { Empty, Ice, Star }

/// <summary>A block on the move: it slides until something stops it, carrying any mites it meets.</summary>
public sealed class Block : Mover
{
    public Tile Kind;
    public bool Egg;
    public readonly List<Mite> Caught = [];
}

public enum EffectKind { Score, Shards, Word, Stars }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.7, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
