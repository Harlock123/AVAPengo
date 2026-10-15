using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAPengo.Engine;
using AVAPengo.Views;

namespace AVAPengo.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set PENGO_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avapengo-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("PENGO_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAPengo-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer penguin play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ICE", 84250, 9);
        g.HighScores.Insert("AVA", 52800, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_first_round()
    {
        var (w, c, g) = Open();
        g.StartGame();
        g.SetLives(9);
        g.Idle(60);
        // The eggs show through the ice, blinking, before the round begins.
        c.Advance(0.1);
        Shot(w, c, "round-start");
        Assert.True(PlayUntil(g, c, g => g.RoundTime > 6 && g.Penguin is { Alive: true, AtCentre: false } && g.Mites.Count(m => !m.Dead && m.State == MiteState.Roaming) >= 3));
        Shot(w, c, "play");
        Assert.True(PlayUntil(g, c, g => g.Sliding.Any(b => b.Caught.Count > 0 && Math.Abs(b.X - b.Tx) + Math.Abs(b.Y - b.Ty) > 0.3) && g.Penguin.Alive));
        Shot(w, c, "crush");
    }

    [AvaloniaFact]
    public void Dazed_mites_and_a_hungry_one()
    {
        var (w, c, g) = Open(4);
        g.Go(2);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Mites.Any(m => !m.Dead && m.State == MiteState.Stunned && m.Timer > 1.5) && g.Penguin.Alive, 240));
        Shot(w, c, "dazed");
        g.Go(6);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Mites.Any(m => !m.Dead && m.State == MiteState.Eating && m.Timer < 0.3) && g.Penguin.Alive && g.RoundTime > 4, 240));
        Shot(w, c, "chew");
    }

    [AvaloniaFact]
    public void The_star_bonus()
    {
        var (w, c, g) = Open(6);
        g.Go(3);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.RoundTime > 5 && g.Penguin.Alive && g.Sliding.Count == 0));
        // Two star blocks side by side, and the third ready to be pushed down into line with them.
        var b = g.Board;
        foreach (var s in b.Stars) b.T[s.C, s.R] = Tile.Ice;
        b.T[3, 10] = b.T[4, 10] = Tile.Star;
        b.T[5, 9] = Tile.Star;
        b.T[5, 10] = Tile.Empty;
        b.T[5, 11] = Tile.Ice;
        b.T[5, 8] = Tile.Empty;
        foreach (var m in g.Mites.Where(m => Math.Abs(m.X - 5) + Math.Abs(m.Y - 8) < 4)) m.Place(11, 1);
        g.PutPenguin(5, 8, Dir.Down);
        g.Run(1, new AVAPengo.Engine.Controls(Fire: true));
        g.Idle(20);
        c.Advance(0.3);
        Shot(w, c, "stars");
    }

    [AvaloniaFact]
    public void A_penguin_lost_and_a_round_cleared()
    {
        var (w, c, g) = Open();
        g.Go(4);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.Penguin is { Alive: true, AtCentre: true } && g.RoundTime > 6));
        g.PutMite(g.Penguin.Cx, g.Penguin.Cy, MiteState.Roaming);
        g.Idle(70);
        c.Advance(0.3);
        Shot(w, c, "lost");
        g.Go(1);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Clear && g.PhaseTime > 0.6, 240));
        Shot(w, c, "clear");
    }
}
