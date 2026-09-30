# Pb (working title)

An original first-person tactical paintball game: physically simulated paintballs, one-hit
eliminations, round-based 5v5 on inflatable-bunker fields. Built with Godot 4.7 (.NET / C#).

**Status: Phase 1, the ballistics sandbox.** A first-person range with real paintball ballistics,
break/bounce, splats, a marker with loader, pods and air, and a 1,000-ball stress mode. The
[Phase 1 report](docs/reports/phase-1.md) has results and known issues.

## Run it

1. Install **Godot 4.7.2 – .NET** (the ".NET" download, not the standard one) and the
   **.NET 8 SDK** (or newer).
2. Open `game/project.godot` in Godot. The first open imports the project and builds the C# code.
3. Press **F5** (Run Project). Click the window to capture the mouse.

For the most accurate frame rate, export a release build: Project → Export → Windows Desktop.
The export presets already include the data files. The editor's F5 run uses a Debug build.

### Controls

| Action | Keyboard / mouse | Gamepad (Xbox / PlayStation) |
|---|---|---|
| Move / look | WASD / mouse | Left / right stick |
| Fire | Left mouse | RT / R2 |
| Sprint (blocks firing) | Shift | L3 |
| Crouch | Ctrl or C | B / Circle |
| Walk | Alt | – |
| Refill loader from a pod (2.5 s) | R | X / Square |
| Semi ↔ ramping | B | Y / Triangle |
| Release mouse | Esc | – |

**Sandbox keys:**

- F1 help, F2 arc preview (predicted path and drop), F3 stress mode (1,000 live balls), F4 performance overlay.
- F5 head-bob, F6 reset gear and hit counters, F7 crosshair, F8 v-sync.
- F9 reload data files, F10 invert Y, F11 fullscreen.
- `[` `]` field of view (70–110°), `-` `=` mouse sensitivity.

Settings changed in game are saved to `user://settings.json`.

## Tune it

Every gameplay number lives in [`game/data/`](game/data) as JSON with comments. Keys carry
their unit (`muzzleVelocity_mps`, `fillPressure_bar`, `diameter_mm`…), and every file explains
its values. Edit a file, then press **F9** in game to reload. A typo or out-of-range value is
reported with the file and key; the game won't silently use a wrong value.

| File | What it controls |
|---|---|
| `projectiles/paintball_68.jsonc` | Ball size, mass, drag, gravity |
| `markers/standard.jsonc` | Muzzle velocity, dispersion, rate cap, ramping, muzzle position |
| `loaders/standard.jsonc` | Loader and pod capacity, refill time and rules |
| `air/standard_1100ml_310bar.jsonc` | Tank size, pressure, per-shot use, low-air velocity drop |
| `break_model.jsonc` | Break-vs-bounce curve per surface |
| `movement.jsonc` | Walk/run/sprint/crouch speeds, eye heights |
| `ranges/phase1.jsonc` | Range layout: targets, props, distance markers |
| `stress.jsonc` | Stress-mode ball count and cannons |
| `presentation.jsonc` | Team colours, FOV, ball visibility, splats, HUD, audio |
| `input.jsonc` | Key and gamepad bindings |

## Test it

```bash
dotnet test                                          # 56 sim tests (ballistics vs spec, collision, gear…) — no Godot needed
dotnet run -c Release --project tools/Pb.Bench       # ballistics report + sim cost at 1k/2k/5k live balls
tools/ci/smoke-test.sh /path/to/godot                # headless end-to-end run of the real scene (Linux/macOS)
```

CI (GitHub Actions) runs all three on every pull request.

## Layout

```
game/        Godot project (open game/project.godot). Scenes, presentation, input, data files
  data/      every tunable (.jsonc)
  core/      composition root, SimDriver (fixed 120 Hz tick), data loading, settings, input map
  player/    first-person controller, viewmodel
  ballistics/ ball renderer, splat decals, impact FX, arc preview
  world/     range builder, targets
  ui/ audio/ shaders/ scenes/
src/Pb.Sim/  engine-free simulation: ballistics, collision, gear, data loading (plain C#, no Godot)
tests/       xUnit tests for Pb.Sim
tools/       Pb.Bench benchmark, CI scripts
docs/        spec, architecture, phase plans and reports
```

The [architecture](docs/architecture.md) explains the design. The [spec](docs/spec.md) is the
source of truth for requirements.
