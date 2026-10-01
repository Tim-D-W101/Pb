# Pb (working title)

An original first-person tactical paintball game: physically simulated paintballs and one-hit
eliminations, played through abandoned industrial compounds you explore and clear, with up to
ten players a round. Built with Godot 4.7 (.NET / C#).

**Status: Phase 2 in progress ([plan](docs/phase-2.md)).** Phase 1 built the ballistics
sandbox, a range with real paintball ballistics, break/bounce, splats, a marker with loader,
pods and air, and a 1,000-ball stress mode ([report](docs/reports/phase-1.md)). Phase 2 has
started with the first compound, **Oxbarrow Works**: a walkable greybox of a derelict
works yard with a warehouse, offices, pump house and guardhouse, built from a data kit.
You pick it and a difficulty (4, 6 or 9 opponents) from the menu, read the briefing, and
clear the compound against the clock, picking up paint and air on the way. The opponents are
bots that hold posts, patrol and rush, notice you by sight and sound, and fight from cover.
The match HUD and proper models arrive in later milestones.

## Run it

1. Install **Godot 4.7.2 – .NET** (the ".NET" download, not the standard one) and the
   **.NET 8 SDK** (or newer).
2. Open `game/project.godot` in Godot. The first open imports the project and builds the C# code.
3. Press **F5** (Run Project) for the main menu. **Play** → Oxbarrow Works → a difficulty, then
   **Start** on the briefing card. **Training ground** is the Phase 1 range.

For the most accurate frame rate, export a release build: Project → Export → Windows Desktop.
The export presets already include the data files. The editor's F5 run uses a Debug build.

### Controls

| Action | Keyboard / mouse | Gamepad (Xbox / PlayStation) |
|---|---|---|
| Move / look | WASD / mouse | Left / right stick |
| Fire | Left mouse | RT / R2 |
| Sprint (blocks firing) | Shift | L3 |
| Crouch (stands up only where there's headroom) | Ctrl or C | B / Circle |
| Walk | Alt | – |
| Jump (a small hop) | Space | A / Cross |
| Lean left / right (stops at walls) | Q / E | LB / RB (L1 / R1) |
| Swap shoulder (the muzzle moves to the other side) | X or middle mouse | R3 |
| Slide (from a run; ends crouched) | V, or crouch while sprinting | B while sprinting |
| Refill loader from a pod (2.5 s) | R | X / Square |
| Semi ↔ ramping | B | Y / Triangle |
| Pause menu (settings, restart, quit) | Esc | Start / Options |

A ball fired while your barrel is behind a wall edge breaks on that wall, as it would in real
life: to shoot round a left-hand edge, swap to your left shoulder first. A break on any part of
you (mask, head, body, arms, legs, marker, loader or tank) puts you out; a bounce doesn't.

**Debug keys:**

- F1 help, F2 arc preview (predicted path and drop), F3 stress mode (1,000 live balls), F4 performance overlay.
- F5 head-bob, F6 reset gear and hit counters (range only), F7 crosshair, F8 v-sync.
- F9 reload data files, F10 invert Y, F11 fullscreen.
- `[` `]` field of view (70–110°), `-` `=` mouse sensitivity.

In the compound, F6 doesn't apply: a round has no gear reset, so you find pods and air lying
around instead. **F3** shows the bot debug overlay there (sight cones coloured by how close they
are to spotting you, their state, paths and cover points). F9 restarts the level from its data
files. **F12** cycles graphics presets (low / medium / high).

### A round

Clear the compound: eliminate every opponent before the clock runs out. One hit and you're out,
and so are they. When the last opponent or you go out, the round waits a moment (at most 1.5 s)
for balls still in the air, so going out together is a trade. You start with a full loader and a
few spare pods; walk over a pod to put it in an empty pod slot, or over an air tank to refill
yours. The summary shows your time, shots, hits, accuracy, eliminations and pickups.

### The opponents

Each opponent stands at a spawn whose roles give its behaviour: a **sentry** holds its post and
scans, a **patroller** walks a route, a **rusher** comes straight at you firing on the move.
They spot you faster up close, in daylight, standing or moving, and in the middle of their view;
they hear shots, balls breaking near them and your footsteps (crouch-walk to stay quiet), and
remember where you were. Once they've seen you they shoot, then take cover and peek round it,
refilling when they run low. Lost, they flank, push or search. The tier sets how quickly they
react, how well they aim and how bold they are.

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
| `movement.jsonc` | Walk/run/sprint/crouch speeds, eye heights, lean, slide, jump, footstep noise |
| `hitboxes.jsonc` | Player hitboxes and elimination rules, mask spray radius |
| `bots/archetypes.jsonc` | Bot behaviours: sentry, patroller, rusher |
| `bots/difficulty.jsonc` | Bot difficulty tiers: reaction, aim, tracking, sight, hearing, aggression |
| `bots/senses.jsonc` | What bots notice: field of view, detection meter, hearing, memory |
| `bots/brain.jsonc` | How bots fight: cover, peeking, searching, refilling, aim settling |
| `bots/navigation.jsonc` | The bots' navigation grid: cell size, clearance, headroom, step height |
| `ranges/phase1.jsonc` | Range layout: targets, props, distance markers |
| `kit/*.jsonc` | Level kit: surface materials, props, building templates |
| `levels/*.jsonc` | Compound levels (layout, spawns, patrols, pickups) |
| `levels/ladder.jsonc` | Level order and difficulty tiers: opponent rosters, bot difficulty, time limits, starting pods, pickups |
| `rules.jsonc` | Round rules: mode, settle window for balls in the air, trades, pickup radius |
| `stress.jsonc` | Stress-mode ball count and cannons |
| `presentation.jsonc` | Team colours, FOV, ball visibility, splats, HUD, audio, lighting, graphics presets |
| `input.jsonc` | Key and gamepad bindings |

## Test it

```bash
dotnet test                                          # 121 sim tests (ballistics vs spec, collision, gear, level kit, movement, hitboxes, rounds, bots…) — no Godot needed
dotnet run -c Release --project tools/Pb.Bench       # ballistics report, sim cost at 1k/2k/5k live balls, bot cost
tools/ci/smoke-test.sh /path/to/godot                # headless end-to-end runs of the menu, the range and the compound (Linux/macOS)
```

CI (GitHub Actions) runs all three on every pull request.

## Layout

```
game/        Godot project (open game/project.godot). Scenes, presentation, input, data files
  data/      every tunable (.jsonc)
  core/      composition root, SimDriver (fixed 120 Hz tick), data loading, settings, input map
  player/    first-person controller, viewmodel, opponent bodies
  ai/        bot pilots and the bot debug overlay
  ballistics/ ball renderer, splat decals, impact FX, arc preview
  world/     range builder, targets, compound level builder, materials, lighting, pickups
  ui/        menus, briefing and summary screens, pause menu, HUD
  audio/ shaders/ scenes/
src/Pb.Sim/  engine-free simulation: ballistics, collision, gear, players, level kit, rounds, bots, data loading (plain C#, no Godot)
tests/       xUnit tests for Pb.Sim
tools/       Pb.Bench benchmark, CI scripts
docs/        spec, architecture, phase plans and reports
```

The [architecture](docs/architecture.md) explains the design. The [spec](docs/spec.md) is the
source of truth for requirements.
