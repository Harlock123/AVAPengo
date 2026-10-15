using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPengo.Engine;
using Effect = AVAPengo.Engine.Effect;

namespace AVAPengo.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAPengo/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F6FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A5A, Dim = 0xFF8FA6C8, Ice = 0xFF8FE0F6, Gold = 0xFFFFC83A;
    private const uint MiteColour = 0xFFFF7A2E, Wall = 0xFF3AD0C0;
    private const double WallWidth = 14;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private static Point Px(double x, double y) { var (px, py) = Field.Px(x, y); return new Point(px, py); }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame)
        {
            DrawField(dc);
            DrawPanels(dc);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
        {
            dc.FillRectangle(Brush(0xB0000000), new Rect(Field.X0, 270, Field.Cols * Field.Cell, 92));
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 316), 100, Red, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 18), 26, Yellow, center: true);
    }

    /// <summary>A polar night: deep blue with a scatter of snow.</summary>
    private void DrawBackdrop(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0xFF081430), new Rect(0, 0, Field.Width, Field.Height));
        for (int k = 0; k < 90; k++)
        {
            double x = Hash(k, 1, 7) % 1024, speed = 8 + Hash(k, 2, 7) % 20;
            double y = (Hash(k, 3, 7) % 768 + _clock * speed) % 768;
            x += Math.Sin(_clock * 0.8 + k) * 6;
            double s = 1 + Hash(k, 4, 7) % 3;
            dc.FillRectangle(Brush(Alpha(0xFFDDE8FF, 0.25 + s * 0.12)), new Rect(x, y, s, s));
        }
    }

    // ───────────────────────────── the field ─────────────────────────────

    private void DrawField(DrawingContext dc)
    {
        var g = Engine;
        double x0 = Field.X0, y0 = Field.Y0, w = Field.Cols * Field.Cell, h = Field.Rows * Field.Cell;
        dc.FillRectangle(Brush(0xFF04102A), new Rect(x0, y0, w, h));
        for (int c = 1; c < Field.Cols; c++) dc.DrawLine(Pen(0x14FFFFFF, 1), new Point(x0 + c * Field.Cell, y0), new Point(x0 + c * Field.Cell, y0 + h));
        for (int r = 1; r < Field.Rows; r++) dc.DrawLine(Pen(0x14FFFFFF, 1), new Point(x0, y0 + r * Field.Cell), new Point(x0 + w, y0 + r * Field.Cell));
        DrawWalls(dc);

        bool showEggs = g.Phase == Phase.Start && g.Mites.Count == 0 && (int)(_clock * 5) % 2 == 0;
        for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
            {
                var t = g.Board.T[c, r];
                if (t == Tile.Empty) continue;
                bool hatchSoon = g.Board.Egg[c, r] && showEggs;
                DrawBlock(dc, Px(c, r), t, hatchSoon);
            }
        // Blocks being chewed crack more and more.
        foreach (var m in g.Mites.Where(m => !m.Dead && m.State == MiteState.Eating))
        {
            var at = Px(m.EatX, m.EatY);
            int cracks = Math.Clamp(1 + (int)((1 - m.Timer / g.EatSeconds) * 5), 1, 5);
            for (int k = 0; k < cracks; k++)
            {
                double a = k * 1.3 + Hash(m.EatX, m.EatY, k) % 10 * 0.1, len = 10 + Hash(k, m.EatX, m.EatY) % 8;
                var mid = new Point(at.X + Math.Cos(a) * len * 0.5 + ((int)(Hash(k, 1, m.EatY) % 5) - 2), at.Y + Math.Sin(a) * len * 0.5 + ((int)(Hash(k, 2, m.EatX) % 5) - 2));
                var end = new Point(at.X + Math.Cos(a) * len, at.Y + Math.Sin(a) * len);
                dc.DrawLine(Pen(0xFF1A5A80, 2), at, mid);
                dc.DrawLine(Pen(0xFF1A5A80, 2), mid, end);
            }
            // Crumbs of ice by its jaws.
            var (fx, fy) = Dirs.Vec(m.Facing);
            for (int k = 0; k < 3; k++)
            {
                double j = Math.Sin(_clock * 25 + k * 2);
                dc.FillRectangle(Brush(0xFFDDF8FF), new Rect(at.X - fx * 20 + fy * (k - 1) * 7 + j * 2 - 2, at.Y - fy * 20 + fx * (k - 1) * 7 - Math.Abs(j) * 3 - 2, 4, 4));
            }
        }
        foreach (var b in g.Sliding)
        {
            var (dx, dy) = Dirs.Vec(b.Facing);
            // A streak behind a sliding block.
            var at = Px(b.X, b.Y);
            for (int k = 1; k <= 3; k++)
                dc.FillRectangle(Brush(Alpha(b.Kind == Tile.Star ? Gold : Ice, 0.18 / k)),
                    new Rect(at.X - dx * k * 12 - Field.Cell / 2 + 3, at.Y - dy * k * 12 - Field.Cell / 2 + 3, Field.Cell - 6, Field.Cell - 6));
            DrawBlock(dc, at, b.Kind, false);
        }
        foreach (var m in g.Mites.Where(m => !m.Dead)) DrawMite(dc, m, Px(m.X, m.Y), 1);
        var p = g.Penguin;
        if (p.Alive) DrawPenguin(dc, Px(p.X, p.Y), p.Facing, !p.AtCentre, p.PushTime > 0, 1);
        else DrawDizzy(dc, Px(p.X, p.Y));
    }

    /// <summary>The walls round the field: a kicked one wobbles and flashes.</summary>
    private void DrawWalls(DrawingContext dc)
    {
        var g = Engine;
        double x0 = Field.X0, y0 = Field.Y0, w = Field.Cols * Field.Cell, h = Field.Rows * Field.Cell, ww = WallWidth;
        void Band(Dir side, Rect rect)
        {
            double s = g.Shaking[(int)side];
            double wob = s > 0 ? Math.Sin(_clock * 90) * 4 * s / 0.5 : 0;
            var (dx, dy) = Dirs.Vec(side);
            var rr = rect.Translate(new Vector(dx * wob, dy * wob));
            uint colour = s > 0 && (int)(_clock * 20) % 2 == 0 ? 0xFFFFFFFF : Wall;
            dc.FillRectangle(Brush(Alpha(colour, 0.25)), rr.Inflate(3));
            dc.FillRectangle(Brush(colour), rr);
            // Rivets along it.
            bool across = side is Dir.Up or Dir.Down;
            double len = across ? rr.Width : rr.Height;
            for (double t = 10; t < len - 4; t += 21)
            {
                var at = across ? new Point(rr.X + t, rr.Center.Y) : new Point(rr.Center.X, rr.Y + t);
                dc.DrawEllipse(Brush(0xFF14786E), null, at, 2.4, 2.4);
            }
        }
        Band(Dir.Up, new Rect(x0 - ww, y0 - ww, w + 2 * ww, ww));
        Band(Dir.Down, new Rect(x0 - ww, y0 + h, w + 2 * ww, ww));
        Band(Dir.Left, new Rect(x0 - ww, y0, ww, h));
        Band(Dir.Right, new Rect(x0 + w, y0, ww, h));
    }

    /// <summary>A block of ice (with its egg showing, at the start of a round), or a star block.</summary>
    private static void DrawBlock(DrawingContext dc, Point at, Tile kind, bool egg)
    {
        double s = Field.Cell / 2 - 1.5;
        var r = new Rect(at.X - s, at.Y - s, 2 * s, 2 * s);
        bool star = kind == Tile.Star;
        uint baseC = star ? 0xFFE8A820 : 0xFF6CCFEA, light = star ? 0xFFFFF0A0 : 0xFFDDF8FF, shade = star ? 0xFFA86A10 : 0xFF2E8CB8;
        dc.FillRectangle(Brush(baseC), r, 4);
        Poly(dc, light, null, 0, r.TopLeft, r.TopRight, new Point(r.Right - 6, r.Top + 6), new Point(r.Left + 6, r.Top + 6), new Point(r.Left + 6, r.Bottom - 6), r.BottomLeft);
        Poly(dc, shade, null, 0, r.BottomRight, r.BottomLeft, new Point(r.Left + 6, r.Bottom - 6), new Point(r.Right - 6, r.Bottom - 6), new Point(r.Right - 6, r.Top + 6), r.TopRight);
        dc.FillRectangle(Brush(baseC), new Rect(r.Left + 6, r.Top + 6, r.Width - 12, r.Height - 12));
        if (star)
        {
            var pts = Enumerable.Range(0, 10).Select(k =>
            {
                double a = -Math.PI / 2 + k * Math.PI / 5, rad = k % 2 == 0 ? 11 : 4.6;
                return new Point(at.X + Math.Cos(a) * rad, at.Y + Math.Sin(a) * rad);
            }).ToArray();
            Poly(dc, 0xFFFFFFFF, 0xFFB06A00, 1.2, pts);
        }
        else
        {
            // A glint in the ice.
            dc.DrawLine(Pen(0xC0FFFFFF, 2), new Point(r.Left + 10, r.Top + 16), new Point(r.Left + 16, r.Top + 10));
            if (egg) dc.DrawEllipse(Brush(0xFFFFF4D0), Pen(0xFFC89A50, 1.5), new Point(at.X, at.Y + 1), 8, 10.5);
        }
    }

    // ───────────────────────────── the penguin and the mites ─────────────────────────────

    /// <summary>The penguin: from the front facing down, its back facing up, side-on facing left or right; waddling as it walks.</summary>
    private void DrawPenguin(DrawingContext dc, Point at, Dir facing, bool walking, bool pushing, double scale)
    {
        double k = scale * Field.Cell / 42;
        double wad = walking ? Math.Sin(_clock * 18) : 0;
        var c = new Point(at.X + wad * 1.2 * k, at.Y + 1 * k);
        uint body = 0xFF141C34, belly = 0xFFF4F8FF, beak = 0xFFFFA020;
        // Feet.
        dc.DrawEllipse(Brush(beak), null, new Point(c.X - 7 * k, c.Y + 15 * k + wad * 1.5 * k), 6 * k, 3 * k);
        dc.DrawEllipse(Brush(beak), null, new Point(c.X + 7 * k, c.Y + 15 * k - wad * 1.5 * k), 6 * k, 3 * k);
        dc.DrawEllipse(Brush(body), Pen(0xFF3A4A78, 1.5), c, 13 * k, 16 * k);
        int side = facing == Dir.Left ? -1 : 1;
        double push = pushing ? 4 * k : 0;
        switch (facing)
        {
            case Dir.Down:
                dc.DrawEllipse(Brush(belly), null, new Point(c.X, c.Y + 4 * k), 9 * k, 11 * k);
                dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(c.X - 5 * k, c.Y - 7 * k), 3.6 * k, 3.6 * k);
                dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(c.X + 5 * k, c.Y - 7 * k), 3.6 * k, 3.6 * k);
                dc.DrawEllipse(Brush(0xFF000000), null, new Point(c.X - 5 * k, c.Y - 6.2 * k), 1.8 * k, 1.8 * k);
                dc.DrawEllipse(Brush(0xFF000000), null, new Point(c.X + 5 * k, c.Y - 6.2 * k), 1.8 * k, 1.8 * k);
                Poly(dc, beak, null, 0, new Point(c.X - 4 * k, c.Y - 3 * k), new Point(c.X + 4 * k, c.Y - 3 * k), new Point(c.X, c.Y + 2 * k + push));
                // Flippers out to the sides.
                Poly(dc, body, null, 0, new Point(c.X - 11 * k, c.Y - 2 * k), new Point(c.X - 18 * k, c.Y + 8 * k + push), new Point(c.X - 10 * k, c.Y + 8 * k));
                Poly(dc, body, null, 0, new Point(c.X + 11 * k, c.Y - 2 * k), new Point(c.X + 18 * k, c.Y + 8 * k + push), new Point(c.X + 10 * k, c.Y + 8 * k));
                break;
            case Dir.Up:
                // Its back, flippers raised to push up.
                Poly(dc, body, null, 0, new Point(c.X - 11 * k, c.Y), new Point(c.X - 16 * k, c.Y - 10 * k - push), new Point(c.X - 8 * k, c.Y - 6 * k));
                Poly(dc, body, null, 0, new Point(c.X + 11 * k, c.Y), new Point(c.X + 16 * k, c.Y - 10 * k - push), new Point(c.X + 8 * k, c.Y - 6 * k));
                dc.DrawEllipse(Brush(0xFF26345C), null, new Point(c.X, c.Y - 2 * k), 6 * k, 9 * k);
                break;
            default:
                dc.DrawEllipse(Brush(belly), null, new Point(c.X + side * 4 * k, c.Y + 4 * k), 7.5 * k, 11 * k);
                dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(c.X + side * 5 * k, c.Y - 7 * k), 3.6 * k, 3.6 * k);
                dc.DrawEllipse(Brush(0xFF000000), null, new Point(c.X + side * 6.2 * k, c.Y - 7 * k), 1.8 * k, 1.8 * k);
                Poly(dc, beak, null, 0, new Point(c.X + side * 10 * k, c.Y - 5 * k), new Point(c.X + side * (18 * k + push), c.Y - 2 * k), new Point(c.X + side * 10 * k, c.Y));
                // A flipper, held forward when it pushes.
                Poly(dc, 0xFF0A1024, null, 0, new Point(c.X - side * 2 * k, c.Y - 2 * k),
                    pushing ? new Point(c.X + side * 18 * k, c.Y + 2 * k) : new Point(c.X - side * 8 * k, c.Y + 12 * k), new Point(c.X + side * 4 * k, c.Y + 6 * k));
                break;
        }
    }

    /// <summary>A caught penguin, flat out with stars going round its head.</summary>
    private void DrawDizzy(DrawingContext dc, Point at)
    {
        double t = Engine.PhaseTime;
        double spin = Math.Min(1, t / 0.6);
        if (spin < 1)
        {
            var f = Dirs.All[(int)(t * 16) % 4];
            DrawPenguin(dc, at, f, false, false, 1);
            return;
        }
        dc.DrawEllipse(Brush(0xFF141C34), Pen(0xFF3A4A78, 1.5), new Point(at.X, at.Y + 6), 18, 10);
        dc.DrawEllipse(Brush(0xFFF4F8FF), null, new Point(at.X, at.Y + 7), 12, 6);
        dc.DrawEllipse(Brush(0xFFFFA020), null, new Point(at.X - 20, at.Y + 4), 4, 3);
        for (int k = 0; k < 3; k++)
        {
            double a = _clock * 5 + k * Math.PI * 2 / 3;
            DrawSpark(dc, new Point(at.X + Math.Cos(a) * 16, at.Y - 10 + Math.Sin(a) * 5), 5, Yellow);
        }
    }

    private static void DrawSpark(DrawingContext dc, Point at, double r, uint colour)
    {
        var pts = Enumerable.Range(0, 8).Select(k =>
        {
            double a = k * Math.PI / 4, rad = k % 2 == 0 ? r : r * 0.4;
            return new Point(at.X + Math.Cos(a) * rad, at.Y + Math.Sin(a) * rad);
        }).ToArray();
        Poly(dc, colour, null, 0, pts);
    }

    /// <summary>A mite: a round, many-legged ice bug with feelers. Dazed, it goes pale and sees stars; caught, it's squashed flat against the block.</summary>
    private void DrawMite(DrawingContext dc, Mite m, Point at, double scale)
    {
        double k = scale * Field.Cell / 42;
        uint colour = m.State switch
        {
            MiteState.Stunned => (int)(_clock * 6) % 2 == 0 || m.Timer > 1.2 ? 0xFF9AB8D8 : MiteColour,
            _ when m.Fleeing => (int)(_clock * 10) % 2 == 0 ? 0xFFFFE14A : MiteColour,
            _ => MiteColour,
        };
        double sx = 1, sy = 1;
        if (m.State == MiteState.Caught && m.CaughtBy is { } b)
        {
            if (Dirs.Horizontal(b.Facing)) { sx = 0.45; sy = 1.25; } else { sx = 1.25; sy = 0.45; }
        }
        if (m.State == MiteState.Hatching)
        {
            double grow = Math.Clamp(1 - m.Timer / GameEngine.HatchSeconds, 0.2, 1);
            sx = sy = 0.5 + 0.5 * grow;
            // The shell it came out of.
            dc.DrawEllipse(Brush(0xFFFFF4D0), Pen(0xFFC89A50, 1.2), new Point(at.X - 9 * k, at.Y + 10 * k), 6 * k, 4 * k);
            dc.DrawEllipse(Brush(0xFFFFF4D0), Pen(0xFFC89A50, 1.2), new Point(at.X + 9 * k, at.Y + 10 * k), 6 * k, 4 * k);
        }
        double rx = 13 * k * sx, ry = 12 * k * sy;
        bool still = m.State is MiteState.Stunned or MiteState.Caught or MiteState.Hatching;
        double leg = still ? 0 : Math.Sin(_clock * 22 + m.Age * 3) * 3 * k;
        // Legs, three each side.
        for (int i = -1; i <= 1; i++)
        {
            double y = at.Y + i * 6 * k * sy;
            double l = (i % 2 == 0 ? leg : -leg);
            dc.DrawLine(Pen(0xFF8A2A0A, 2.2 * k), new Point(at.X - rx + 2, y), new Point(at.X - rx - 6 * k, y + 4 * k + l));
            dc.DrawLine(Pen(0xFF8A2A0A, 2.2 * k), new Point(at.X + rx - 2, y), new Point(at.X + rx + 6 * k, y + 4 * k - l));
        }
        dc.DrawEllipse(Brush(colour), Pen(0xFF8A2A0A, 1.6), at, rx, ry);
        dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.3)), null, new Point(at.X - rx * 0.35, at.Y - ry * 0.4), rx * 0.35, ry * 0.25);
        var (fx, fy) = Dirs.Vec(m.Facing);
        // Feelers.
        double wave = Math.Sin(_clock * 6 + m.Age) * 2 * k;
        dc.DrawLine(Pen(0xFF8A2A0A, 1.6), new Point(at.X - 4 * k, at.Y - ry + 2), new Point(at.X - 9 * k + wave, at.Y - ry - 9 * k));
        dc.DrawLine(Pen(0xFF8A2A0A, 1.6), new Point(at.X + 4 * k, at.Y - ry + 2), new Point(at.X + 9 * k + wave, at.Y - ry - 9 * k));
        dc.DrawEllipse(Brush(0xFFFFE14A), null, new Point(at.X - 9 * k + wave, at.Y - ry - 9 * k), 2.2 * k, 2.2 * k);
        dc.DrawEllipse(Brush(0xFFFFE14A), null, new Point(at.X + 9 * k + wave, at.Y - ry - 9 * k), 2.2 * k, 2.2 * k);
        if (m.State == MiteState.Stunned)
        {
            // Crossed-out eyes, and stars.
            foreach (int s in new[] { -1, 1 })
            {
                var e = new Point(at.X + s * 5 * k, at.Y - 2 * k);
                dc.DrawLine(Pen(0xFF202020, 2), new Point(e.X - 3 * k, e.Y - 3 * k), new Point(e.X + 3 * k, e.Y + 3 * k));
                dc.DrawLine(Pen(0xFF202020, 2), new Point(e.X - 3 * k, e.Y + 3 * k), new Point(e.X + 3 * k, e.Y - 3 * k));
            }
            for (int i = 0; i < 3; i++)
            {
                double a = _clock * 6 + i * Math.PI * 2 / 3;
                DrawSpark(dc, new Point(at.X + Math.Cos(a) * 15 * k, at.Y - 16 * k + Math.Sin(a) * 4 * k), 4 * k, Yellow);
            }
            return;
        }
        if (m.State == MiteState.Caught) return;
        foreach (int s in new[] { -1, 1 })
        {
            var e = new Point(at.X + s * 5 * k + fx * 3 * k, at.Y - 2 * k + fy * 2 * k);
            dc.DrawEllipse(Brush(0xFFFFFFFF), null, e, 4.2 * k, 4.6 * k);
            dc.DrawEllipse(Brush(0xFF101010), null, new Point(e.X + fx * 1.8 * k, e.Y + fy * 1.8 * k), 2 * k, 2 * k);
        }
        // Jaws: chomping away at the ice.
        double open = m.State == MiteState.Eating ? 2 + 2 * Math.Sin(_clock * 30) : 1;
        var mouth = new Point(at.X + fx * 6 * k, at.Y + 6 * k + fy * 3 * k);
        dc.DrawLine(Pen(0xFF5A1404, 2.2), new Point(mouth.X - 4 * k, mouth.Y - open * 0.4), new Point(mouth.X + 4 * k, mouth.Y - open * 0.4));
        if (m.State == MiteState.Eating)
            dc.DrawLine(Pen(0xFFFFFFFF, 2), new Point(mouth.X - 3 * k, mouth.Y + open), new Point(mouth.X + 3 * k, mouth.Y + open));
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Shards:
                for (int k = 0; k < 10; k++)
                {
                    double a = k * Math.PI / 5 + Hash(k, (int)e.X, 1) % 10 * 0.06, d = (6 + 34 * t) * e.Size * (0.6 + Hash(k, (int)e.Y, 3) % 10 * 0.06);
                    double s = 7 * (1 - t) * e.Size;
                    var at = new Point(c.X + Math.Cos(a) * d, c.Y + Math.Sin(a) * d + 30 * t * t);
                    Poly(dc, Alpha(e.Colour, 1 - t), null, 0, new Point(at.X, at.Y - s), new Point(at.X + s * 0.7, at.Y + s * 0.5), new Point(at.X - s * 0.6, at.Y + s * 0.4));
                }
                break;
            case EffectKind.Stars:
                for (int k = 0; k < 5; k++)
                {
                    double a = _clock * 3 + k * Math.PI * 2 / 5, d = 20 + 6 * Math.Sin(_clock * 8 + k);
                    DrawSpark(dc, new Point(c.X + Math.Cos(a) * d, c.Y + Math.Sin(a) * d), 6, Alpha((uint)(k % 2 == 0 ? Yellow : White), 1 - t * t));
                }
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - 14 - t * 24), 28, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 34, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, side panels and screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 48), 30, Dim, right: true);
        if (g.State is GameState.Playing or GameState.Attract or GameState.Paused)
            Text(dc, $"ROUND {g.Round}", new Point(Field.Width / 2, 40), 38, White, center: true);
    }

    /// <summary>The eggs still to hatch and the spare penguins on the left; the clock and the bonuses on the right.</summary>
    private void DrawPanels(DrawingContext dc)
    {
        var g = Engine;
        double lx = (Field.X0 - WallWidth) / 2, rx = Field.Width - lx;
        Text(dc, "EGGS", new Point(lx, 120), 30, Dim, center: true);
        int eggs = g.Board.Eggs;
        for (int i = 0; i < eggs; i++)
        {
            double x = lx - 52 + i % 4 * 35, y = 162 + i / 4 * 42;
            dc.DrawEllipse(Brush(0xFFFFF4D0), Pen(0xFFC89A50, 1.5), new Point(x, y), 10, 13);
        }
        Text(dc, "PENGUINS", new Point(lx, 330), 30, Dim, center: true);
        int spare = Math.Max(0, g.Lives - (g.Penguin.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 8); i++)
            DrawPenguin(dc, new Point(lx - 54 + i % 4 * 36, 378 + i / 4 * 46), Dir.Down, false, false, 0.75);

        Text(dc, "TIME", new Point(rx, 120), 30, Dim, center: true);
        int secs = (int)g.RoundTime;
        int bonus = GameEngine.TimeBonusFor(g.RoundTime);
        Text(dc, $"{secs / 60}:{secs % 60:D2}", new Point(rx, 158), 44, g.Hurried ? Red : White, center: true);
        Text(dc, $"BONUS {bonus}", new Point(rx, 196), 26, bonus > 0 ? Yellow : Dim, center: true);
        Text(dc, "STARS", new Point(rx, 280), 30, Dim, center: true);
        DrawBlock(dc, new Point(rx - 44, 326), Tile.Star, false);
        DrawBlock(dc, new Point(rx, 326), Tile.Star, false);
        DrawBlock(dc, new Point(rx + 44, 326), Tile.Star, false);
        Text(dc, g.StarsDone ? "LINED UP!" : "LINE THEM UP", new Point(rx, 372), 26, g.StarsDone ? Yellow : White, center: true);
        Text(dc, "CRUSH", new Point(rx, 460), 30, Dim, center: true);
        for (int i = 0; i < GameEngine.CrushPoints.Length; i++)
            Text(dc, $"{i + 1}{(i == GameEngine.CrushPoints.Length - 1 ? "+" : " ")} {GameEngine.CrushPoints[i],5}", new Point(rx, 496 + i * 28), 26, White, center: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = Field.Y0 + 5 * Field.Cell;
        var (w, h) = (Math.Min(Field.Cols * Field.Cell, Math.Max(msg.Length * 30, (g.SubMessage?.Length ?? 0) * 14) + 60), g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xD0061030), new Rect(cx - w / 2, cy - 48, w, h), 6);
        dc.DrawRectangle(null, Pen(Alpha(Ice, 0.6), 2), new Rect(cx - w / 2, cy - 48, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), 56, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "AVA PENGO", new Point(cx, 120), 120, Ice, center: true);
        Text(dc, "AFTER THE 1982 ARCADE CLASSIC", new Point(cx, 192), 28, Dim, center: true);
        // A penguin about to send a block at a mite.
        DrawPenguin(dc, new Point(cx - 110, 260), Dir.Right, false, true, 1.5);
        DrawBlock(dc, new Point(cx - 48, 262), Tile.Ice, false);
        for (int k = 1; k <= 3; k++) dc.DrawLine(Pen(Alpha(Ice, 0.6 - k * 0.15), 3), new Point(cx - 18 + k * 14, 248 + k * 6), new Point(cx - 4 + k * 14, 248 + k * 6));
        DrawMite(dc, new Mite { State = MiteState.Roaming, Facing = Dir.Left, Age = 1 }, new Point(cx + 70, 262), 1.5);
        DrawBlock(dc, new Point(cx + 140, 262), Tile.Star, false);
        string[] lines =
        [
            "PUSH THE ICE TO CRUSH THE MITES",
            "KICK A WALL TO DAZE THE ONES AGAINST IT, THEN STAMP ON THEM",
            "BREAK THE EGGS BEFORE THEY HATCH - LINE UP THE STAR BLOCKS",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 322 + i * 32), 27, White, center: true);
        (string What, string Points)[] table =
        [
            ("CRUSH ONE", "400"), ("TWO AT ONCE", "1600"), ("THREE", "3200"), ("FOUR", "6400"),
            ("STAMP", "100"), ("EGG", "500"), ("STARS", "10000"), ("AT A WALL", "5000"),
        ];
        for (int i = 0; i < table.Length; i++)
        {
            double x = cx - 300 + i % 4 * 200, y = 438 + i / 4 * 52;
            Text(dc, table[i].What, new Point(x, y), 24, Dim, center: true);
            Text(dc, table[i].Points, new Point(x, y + 24), 28, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 600), 46, Yellow, center: true);
        Text(dc, "ARROWS: WALK    SPACE: PUSH (OR KICK THE WALL)", new Point(cx, 664), 26, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE COOLEST PENGUINS", new Point(cx, 130), 64, Ice, center: true);
        Text(dc, "RANK NAME    SCORE   ROUND", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4} ", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
