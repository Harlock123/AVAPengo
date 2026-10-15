using AVAPengo.Engine;

namespace AVAPengo.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avapengo-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in play on an open field with nothing on it unless a test puts it there, and no eggs hatching.</summary>
    private static GameEngine Game(params string[] rows)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(1);
        g.SetBoard(rows);
        return g;
    }

    /// <summary>A real game in play (eggs hatching), with plenty of penguins.</summary>
    private static GameEngine Real(int round = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(round);
        g.SetLives(99);
        return g;
    }

    private static Controls Press(Dir d, bool fire = false)
    {
        var (dx, dy) = Dirs.Vec(d);
        return new Controls(dx, dy, fire);
    }

    private static readonly Controls Push = new(Fire: true);

    /// <summary>One row of a field: '.' everywhere but the given cells.</summary>
    private static string Row(params (int C, char Ch)[] cells)
    {
        var s = new string('.', Field.Cols).ToCharArray();
        foreach (var (c, ch) in cells) s[c] = ch;
        return new string(s);
    }

    /// <summary>A field that's open but for the given cells.</summary>
    private static string[] Rows(params (int C, int R, char Ch)[] cells) =>
        Enumerable.Range(0, Field.Rows).Select(r => Row(cells.Where(x => x.R == r).Select(x => (x.C, x.Ch)).ToArray())).ToArray();

    // ───────────────────────────── the field ─────────────────────────────

    [Fact]
    public void Every_field_is_a_maze_through_the_ice_with_three_stars_and_an_egg_for_every_mite()
    {
        for (int seed = 1; seed <= 30; seed++)
        {
            int eggs = GameEngine.MitesIn(seed % 12 + 1);
            var b = Board.Build(new Random(seed), eggs);
            Assert.True(b.Free(Field.StartX, Field.StartY));
            var dist = b.Distances(Field.StartX, Field.StartY);
            Assert.All(b.Cells().Where(p => b.Free(p.C, p.R)), p => Assert.True(dist[p.C, p.R] >= 0, $"seed {seed}: {p} cut off"));
            var stars = b.Stars;
            Assert.Equal(3, stars.Count);
            Assert.All(stars, s => Assert.True(s.C > 0 && s.C < Field.Cols - 1 && s.R > 0 && s.R < Field.Rows - 1));
            Assert.False(b.StarLine().Lined);
            Assert.Equal(eggs, b.Eggs);
            Assert.All(b.Cells().Where(p => b.Egg[p.C, p.R]), p => Assert.Equal(Tile.Ice, b.T[p.C, p.R]));
            Assert.True(b.Cells().Count(p => b.T[p.C, p.R] != Tile.Empty) > 60, $"seed {seed}: too little ice");
            Assert.Equal(b.T.Cast<Tile>(), Board.Build(new Random(seed), eggs).T.Cast<Tile>());
        }
    }

    // ───────────────────────────── the penguin ─────────────────────────────

    [Fact]
    public void The_penguin_walks_cell_to_cell_and_stops_at_the_next_when_let_go()
    {
        var g = Game();
        g.PutPenguin(2, 6);
        g.Run(30, Press(Dir.Right));
        Assert.True(g.Penguin.X > 4 && g.Penguin.X < 5.5, $"x {g.Penguin.X}");
        g.Idle(20);
        Assert.Equal((5.0, 6.0), (g.Penguin.X, g.Penguin.Y));
        g.Run(6, Press(Dir.Right));
        g.Run(1, Press(Dir.Left));
        g.Idle(20);
        Assert.Equal((5.0, 6.0), (g.Penguin.X, g.Penguin.Y));
    }

    [Fact]
    public void Ice_stops_the_penguin_but_it_turns_to_face_it()
    {
        var g = Game(Rows((5, 6, '#')));
        g.PutPenguin(4, 6, Dir.Down);
        g.Run(30, Press(Dir.Right));
        Assert.Equal((4.0, 6.0), (g.Penguin.X, g.Penguin.Y));
        Assert.Equal(Dir.Right, g.Penguin.Facing);
        g.PutPenguin(0, 0, Dir.Down);
        g.Run(30, Press(Dir.Up));
        Assert.Equal((0.0, 0.0), (g.Penguin.X, g.Penguin.Y));
    }

    // ───────────────────────────── pushing the ice ─────────────────────────────

    [Fact]
    public void A_pushed_block_slides_until_something_stops_it()
    {
        var g = Game(Rows((3, 6, '#'), (3, 8, '#'), (9, 8, '#')));
        g.PutPenguin(2, 6, Dir.Right);
        g.Run(1, Push);
        Assert.Single(g.Sliding);
        Assert.Equal(Tile.Empty, g.Board.T[3, 6]);
        g.Idle(60);
        Assert.Empty(g.Sliding);
        Assert.Equal(Tile.Ice, g.Board.T[Field.Cols - 1, 6]);

        g.PutPenguin(2, 8, Dir.Right);
        g.Run(1, Push);
        g.Idle(60);
        Assert.Equal(Tile.Ice, g.Board.T[8, 8]);
        Assert.Equal(Tile.Ice, g.Board.T[9, 8]);
    }

    [Fact]
    public void A_block_with_no_room_to_slide_breaks_but_a_star_block_never_does()
    {
        var g = Game(Rows((3, 6, '#'), (4, 6, '#'), (3, 8, '*'), (4, 8, '#')));
        g.PutPenguin(2, 6, Dir.Right);
        g.Run(1, Push);
        Assert.Equal(Tile.Empty, g.Board.T[3, 6]);
        Assert.Equal(GameEngine.IcePoints, g.Score);
        g.PutPenguin(2, 8, Dir.Right);
        g.Idle(20);
        g.Run(1, Push);
        g.Idle(20);
        Assert.Equal(Tile.Star, g.Board.T[3, 8]);
        Assert.Equal(GameEngine.IcePoints, g.Score);
    }

    [Fact]
    public void Breaking_an_egg_is_worth_500_and_that_mite_never_hatches()
    {
        var g = Game(Rows((3, 6, 'e'), (4, 6, '#'), (9, 9, 'e')));
        Assert.Equal(2, g.Board.Eggs);
        g.PutPenguin(2, 6, Dir.Right);
        g.Run(1, Push);
        Assert.Equal(GameEngine.EggPoints, g.Score);
        Assert.Equal(1, g.Board.Eggs);
    }

    [Fact]
    public void A_sliding_block_crushes_the_mites_in_its_way_more_for_more_at_once()
    {
        var g = Game(Rows((3, 6, '#'), (3, 9, '#')));
        var one = g.PutMite(8, 6, MiteState.Stunned);
        g.PutPenguin(2, 6, Dir.Right);
        g.Run(1, Push);
        g.Idle(20);
        Assert.Equal(MiteState.Caught, one.State);
        g.Idle(40);
        Assert.True(one.Dead);
        Assert.Equal(400, g.Score);

        var a = g.PutMite(6, 9, MiteState.Stunned);
        var b = g.PutMite(10, 9, MiteState.Stunned);
        g.PutPenguin(2, 9, Dir.Right);
        g.Run(1, Push);
        g.Idle(60);
        Assert.True(a.Dead && b.Dead);
        Assert.Equal(400 + 1600, g.Score);
    }

    [Fact]
    public void A_block_crushes_a_mite_coming_down_the_corridor_at_it()
    {
        // A corridor of ice along row 6, so the mite can only come this way.
        var cells = Enumerable.Range(4, Field.Cols - 4).SelectMany(c => new[] { (c, 5, '#'), (c, 7, '#') }).Append((3, 6, '#')).ToArray();
        var g = Game(Rows(cells));
        var m = g.PutMite(11, 6, MiteState.Roaming, Dir.Left);
        g.PutPenguin(2, 6, Dir.Right);
        g.Idle(10);
        g.Run(1, Push);
        g.Idle(60);
        Assert.True(m.Dead);
        Assert.True(g.Penguin.Alive);
        Assert.Equal(400, g.Score);
    }

    // ───────────────────────────── the walls ─────────────────────────────

    [Fact]
    public void Kicking_a_wall_dazes_the_mites_against_it_and_the_penguin_can_stamp_on_them()
    {
        var g = Game();
        var top = g.PutMite(0, 2);
        var bottom = g.PutMite(0, 12);
        var away = g.PutMite(5, 12);
        g.PutPenguin(0, 6, Dir.Up);
        g.Run(1, Press(Dir.Left, fire: true));
        Assert.Equal(MiteState.Stunned, top.State);
        Assert.Equal(MiteState.Stunned, bottom.State);
        Assert.NotEqual(MiteState.Stunned, away.State);
        Assert.True(g.Shaking[(int)Dir.Left] > 0);
        g.Run(60, Press(Dir.Up));
        Assert.True(top.Dead);
        Assert.Equal(GameEngine.StampPoints, g.Score);
        g.Idle((int)(60 * g.StunSeconds));
        Assert.Equal(MiteState.Roaming, bottom.State);
    }

    // ───────────────────────────── the mites ─────────────────────────────

    [Fact]
    public void A_mite_hunts_the_penguin_down_and_the_next_penguin_starts_in_the_middle()
    {
        var g = Game();
        g.SetLives(3);
        var m = g.PutMite(11, 6, MiteState.Roaming, Dir.Left);
        g.PutPenguin(Field.StartX, Field.StartY);
        for (int t = 0; t < 60 * 30 && g.Phase == Phase.Play; t++) g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.True(g.Penguin.Alive);
        Assert.Equal((Field.StartX, Field.StartY), (g.Penguin.Cx, g.Penguin.Cy));
        Assert.True(Math.Abs(m.X - Field.StartX) + Math.Abs(m.Y - Field.StartY) >= 6);
        g.Idle((int)(60 * GameEngine.ReadySeconds) + 5);
        Assert.Equal(Phase.Play, g.Phase);
    }

    [Fact]
    public void Losing_the_last_penguin_ends_the_game()
    {
        var g = Game();
        g.SetLives(1);
        g.PutMite(Field.StartX, Field.StartY, MiteState.Roaming);
        g.Idle(2);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void A_mite_just_out_of_its_egg_is_harmless_for_a_moment()
    {
        var g = Game();
        var m = g.PutMite(Field.StartX, Field.StartY, MiteState.Hatching);
        g.Idle(30);
        Assert.True(g.Penguin.Alive);
        g.Idle((int)(60 * GameEngine.HatchSeconds));
        Assert.False(g.Penguin.Alive);
        Assert.NotEqual(MiteState.Hatching, m.State);
    }

    [Fact]
    public void A_boxed_in_mite_chews_its_way_out_through_the_ice()
    {
        var g = Game(Rows((2, 1, '#'), (1, 2, '#'), (3, 2, '#'), (2, 3, '#')));
        var m = g.PutMite(2, 2);
        g.Idle(5);
        Assert.Equal(MiteState.Eating, m.State);
        g.Idle(70);
        Assert.Equal(3, new[] { g.Board.T[2, 1], g.Board.T[1, 2], g.Board.T[3, 2], g.Board.T[2, 3] }.Count(t => t == Tile.Ice));
        Assert.NotEqual(MiteState.Eating, m.State);
    }

    [Fact]
    public void Eggs_hatch_to_keep_the_mites_coming_until_there_are_none_left()
    {
        var g = Real();
        int awake = GameEngine.Awake(1);
        Assert.Equal(awake, g.Mites.Count(m => !m.Dead));
        Assert.Equal(GameEngine.MitesIn(1) - awake, g.Board.Eggs);
        Assert.All(g.Mites, m => Assert.Equal(MiteState.Hatching, m.State));
        g.Mites[0].Dead = true;
        g.Idle(80);
        Assert.Equal(awake, g.Mites.Count(m => !m.Dead));
        Assert.Equal(GameEngine.MitesIn(1) - awake - 1, g.Board.Eggs);
    }

    [Fact]
    public void The_last_mite_runs_for_a_corner_and_gets_away_and_the_round_is_over()
    {
        var g = Real();
        // It starts penned in by the ice in the bottom left corner, well away from the penguin.
        g.SetBoard(Rows((0, 11, '#'), (1, 11, '#'), (2, 11, '#'), (3, 11, '#'), (3, 12, '#'), (3, 13, '#'), (3, 14, '#')));
        g.PutPenguin(Field.Cols - 1, 0);
        var m = g.PutMite(1, 13);
        g.Idle((int)(60 * GameEngine.FleeSeconds) + 5);
        Assert.True(m.Fleeing);
        for (int t = 0; t < 900 && !m.Dead; t++) g.Tick(default);
        Assert.True(m.Dead);
        Assert.True(g.Penguin.Alive);
        Assert.Contains(m.Cx, new[] { 0, Field.Cols - 1 });
        Assert.Contains(m.Cy, new[] { 0, Field.Rows - 1 });
        g.Idle(2);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(5000, g.Score);
        g.Idle((int)(60 * GameEngine.ClearSeconds) + 5);
        Assert.Equal(2, g.Round);
    }

    [Fact]
    public void The_quicker_a_round_the_bigger_the_bonus()
    {
        Assert.Equal(5000, GameEngine.TimeBonusFor(15));
        Assert.Equal(2000, GameEngine.TimeBonusFor(25));
        Assert.Equal(1000, GameEngine.TimeBonusFor(35));
        Assert.Equal(500, GameEngine.TimeBonusFor(45));
        Assert.Equal(10, GameEngine.TimeBonusFor(59));
        Assert.Equal(0, GameEngine.TimeBonusFor(61));
    }

    [Fact]
    public void After_a_minute_the_mites_hurry()
    {
        var g = Real();
        double speed = g.MiteSpeed;
        g.SetRoundTime(GameEngine.HurrySeconds - 0.05);
        g.Idle(10);
        Assert.True(g.Hurried);
        Assert.True(g.MiteSpeed > speed);
    }

    // ───────────────────────────── the star blocks ─────────────────────────────

    [Fact]
    public void Lining_up_the_star_blocks_is_worth_10000_and_dazes_every_mite()
    {
        var g = Game(Rows((4, 3, '*'), (5, 3, '*'), (6, 8, '*'), (6, 2, '#')));
        var m = g.PutMite(11, 13);
        g.PutPenguin(6, 9, Dir.Up);
        g.Run(1, Push);
        g.Idle(40);
        Assert.Equal(Tile.Star, g.Board.T[6, 3]);
        Assert.True(g.StarsDone);
        Assert.Equal(GameEngine.StarPoints, g.Score);
        Assert.Equal(MiteState.Stunned, m.State);
    }

    [Fact]
    public void Star_blocks_lined_up_against_a_wall_are_worth_5000()
    {
        var g = Game(Rows((4, 0, '*'), (5, 0, '*'), (6, 8, '*')));
        g.PutPenguin(6, 9, Dir.Up);
        g.Run(1, Push);
        g.Idle(40);
        Assert.Equal(Tile.Star, g.Board.T[6, 0]);
        Assert.True(g.StarsDone);
        Assert.Equal(GameEngine.StarPointsAtWall, g.Score);
    }

    [Fact]
    public void Extra_penguins_come_at_30000_and_every_50000_after()
    {
        var g = Game(Rows((3, 6, '#'), (4, 6, '#'), (3, 8, '#'), (4, 8, '#')));
        int lives = g.Lives;
        g.SetScore(GameEngine.FirstExtraLife - 10);
        g.PutPenguin(2, 6, Dir.Right);
        g.Run(1, Push);
        Assert.Equal(lives + 1, g.Lives);
        g.SetScore(GameEngine.FirstExtraLife + GameEngine.ExtraLifeEvery - 10);
        g.PutPenguin(2, 8, Dir.Right);
        g.Idle(20);
        g.Run(1, Push);
        Assert.Equal(lives + 2, g.Lives);
    }

    // ───────────────────────────── around the game ─────────────────────────────

    [Fact]
    public void The_computer_penguin_clears_the_first_rounds()
    {
        var g = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        g.SetLives(9);
        for (int t = 0; t < 60 * 180 && g.State == GameState.Playing && g.Round < 3; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Round >= 3, $"round {g.Round}");
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_back()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 25);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 25)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        static (int, double, double, int) Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 40; t++) g.Tick(g.Pilot.Decide(g));
            return (g.Score, g.Penguin.X, g.Penguin.Y, g.Round);
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.SetBoard();
        g.SetScore(99999);
        g.SetLives(1);
        g.PutMite(Field.StartX, Field.StartY);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 5)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "PEN") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("PEN", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}
