# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

An ice-block maze game in the style of Sega's 1982 arcade game Pengo, in C# on .NET 10 and Avalonia 12. One
self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in code,
and the sounds and music are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAPengo.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAPengo.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVAPengo          # play it; --no-audio for silence
PENGO_SHOTS=$PWD/Images dotnet test tests/AVAPengo.Tests --filter ScreenshotTests   # refresh the README's pictures
PENGO_PROBE=1 dotnet test tests/AVAPengo.Tests --filter PilotProbe   # how the computer penguin gets on, for tuning
python3 tools/make_sounds.py               # rebuild src/AVAPengo/Sounds (standard-library Python only)
```

`PENGO_PROBE=lazy` runs a penguin that just wanders and pushes. Without `PENGO_PROBE` the probe test does nothing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAPengo/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `GameEngine.Play.cs` is the rules within a round: the penguin, sliding blocks, mites, eggs, stunned mites,
    the star blocks and the bonuses.
  - `Board.cs` cuts each round's maze through the ice and places the eggs and star blocks.
  - `Entities.cs` has `Controls`, the field measures (`Field`), the movers (`Penguin`, `Mite`), tiles, sliding
    blocks and effects.
  - `PenguinPilot.cs` is the computer player, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAPengo/Views/`: `GameCanvas` (split over two partial files) draws the screen and runs the 60 Hz timer.
- `src/AVAPengo/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAPengo.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay tests, screenshot tests and the pilot
  probe. The tests see the engine's internals (`InternalsVisibleTo`), such as `Calm`, in which no eggs hatch,
  nothing flees and a round never ends.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` runs the game in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate, so a seed always plays the same game. Draw random numbers from the engine's seeded `_rng`
  (`new Random(seed)`), which also builds each board. Never use `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it.
- **Input goes through `InputHandler`.** `Controls` is `dx` and `dy` (four ways) and the push button. `dy` is -1
  for up the screen, on keys and stick alike (`LeftStickY < -0.4` is up), and field cells count down from (0, 0)
  at the top left. Keep keys and stick agreeing when adding controls.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAPengo`); tests load the
  table from a fresh folder under the system temp path and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
