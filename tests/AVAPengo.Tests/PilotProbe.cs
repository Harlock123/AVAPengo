using AVAPengo.Engine;

namespace AVAPengo.Tests;

/// <summary>How the computer penguin gets on, round by round (set PENGO_PROBE to run it, for tuning; PENGO_PROBE=lazy for one that just wanders and pushes).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("PENGO_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "pengo-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var rounds = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int round = 1, t = 0, stars = 0, escapes = 0;
            double start = 0, still = 0;
            string? said = null;
            (double, double) at = (0, 0);
            var rng = new Random(seed);
            var lazy = Dir.Left;
            for (; t < 60 * 900 && g.Round <= 8; t++)
            {
                Controls c;
                if (mode == "lazy")
                {
                    if (t % 40 == 0) lazy = Dirs.All[rng.Next(4)];
                    var (dx, dy) = Dirs.Vec(lazy);
                    c = new Controls(dx, dy, t % 30 < 2);
                }
                else c = g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"R{g.Round}"] = deaths.GetValueOrDefault($"R{g.Round}") + 1;
                if (g.Message != said && g.Message == "STAR BONUS") stars++;
                if (g.Message != said && g.Message == "IT GOT AWAY") escapes++;
                said = g.Message;
                if (g.Phase == Phase.Play && g.Penguin.Alive)
                {
                    var p = g.Penguin;
                    if ((p.X, p.Y) == at) still += 1 / 60.0; else { still = 0; at = (p.X, p.Y); }
                    if (still > 10)
                    {
                        output.WriteLine($"   still R{g.Round} at ({p.X},{p.Y}) doing {g.Pilot.Doing}: {string.Join(" ", g.Mites.Where(m => !m.Dead).Select(m => $"{m.State}@({m.X:0.#},{m.Y:0.#})"))} eggs {g.Board.Eggs}");
                        still = -999;
                    }
                }
                if (g.Round != round) { rounds.Add($"R{round}:{(t - start) / 60:0}s"); round = g.Round; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: round {g.Round} after {t / 60:0}s score {g.Score} stars {stars} escapes {escapes} | {string.Join(" ", rounds)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
