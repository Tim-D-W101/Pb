# Pb (working title)

An original first-person tactical paintball game: physically simulated paintballs and one-hit
eliminations, played through abandoned industrial compounds you explore and clear, with up to
ten players a round. Built with Godot 4.7 (.NET / C#).

**Status: Phase 3 is finished but for your play-test ([report](docs/reports/phase-3.md), [plan](docs/phase-3.md)):
four open areas, each with places to play in, doors, ladders, objectives, new opponents, sound with voiced callouts, full
settings; after your play-test (2026-10-06) every area is open with nothing to unlock, and the opponents plant their
steps; since then they look round, shift their weight, pull up from a run, keep out of their cover and put a hand up
when hit (M3.14), and you and they can climb every ladder (M3.15).** **Phase 4 (multiplayer: co-op against the bots and player
vs player for up to ten, hosted from the game or on a dedicated server) is finished but for your check
([report](docs/reports/phase-4.md), [plan](docs/phase-4.md), [hosting](docs/hosting.md)), and merged.** **Phase 5 (the gear
locker, four brands of our own, gear models, paint that looks like paint, and the speedball field with capture the flag
and arcade) is under way: the plan is approved with its defaults ([plan](docs/phase-5.md)); M5.1 is built (the four
brands' kit, built in code, worn in first person and by every character, bots dealt theirs), and so is M5.2: the **Gear
locker** in the main menu and the lobby, an old changing room where you stand on a turntable under a lamp and pick your
character, each slot's item by brand and its three colours, saved to your profile and worn in every round; and M5.3:
online, everyone sees everyone's kit.** Phase 2 is built
([report](docs/reports/phase-2.md), [plan](docs/phase-2.md)). Phase 1 built the ballistics sandbox, a
range with real paintball ballistics, break/bounce, splats, a marker with loader, pods and air, and a 1,000-ball stress mode
([report](docs/reports/phase-1.md)). Phase 2 built the first compound, **Oxbarrow Works**: a
walkable derelict works yard with a warehouse, offices, pump house and guardhouse, built from a data
kit. You pick it, a mode, how many play and a difficulty from the menu, read the briefing, and play:
**solo** (clear the compound of a squad against the clock), **free-for-all** (up to ten players,
everyone against everyone) or **teams** (you and bot teammates against a bot team, up to 5 v 5),
picking up paint and air on the way. In solo and teams you can also play for an objective (Phase 3): **retrieve** (find
the case in the marked building and carry it out) or **hold** (keep the marked room for a minute in all). Every area is
open from the start, and in each you pick where to play: the whole of it, or one part (a building, the tracks, a
courtyard), taped off all round, with the round kept inside. Besides Oxbarrow Works there's **the Rail Yard**: a goods yard left to rust, rakes of dead wagons on four tracks (you
can shoot under them, not crawl), an engine shed with a gantry, a goods shed with its platform, a signal box and a
footbridge over the lot, with Marksmen watching from the high places. Then **the Cold Store**: a refrigerated depot gone
dark, its floor a lorry's height above the yard, chambers of racking behind heavy doors off a long aisle, and trailers
backed onto the docks that you run through to get in. And the hardest, **the Hospital Wing**: three storeys of wards
and corridors in two wings round an overgrown courtyard that every window looks down on, an operating theatre, a lift
shaft open through every floor and the end of one wing fallen in. The ladders up the water towers (to a railed catwalk
12 m up, and on up the tank to its roof) and up the tank wagons' sides can be climbed, by you and by the bots, whose
paths go up them. The bots hold posts, patrol, hunt and rush, notice you by sight
and sound, and fight from cover; in free-for-all and teams they fight each other too. They shout where you are, so
their teammates know; Marksmen shoot carefully from far off and move after a few shots, and Flankers work round to
your side when a teammate calls you out. The match HUD
is in, and so is the art pass: photographic textures on every surface; every prop modelled (built in
code from its colliders, and generated models for the oil drum, the burnt-out car, the stacked tyres, the generator
and the sandbag walls); framed windows with broken glass; gutters, roof trusses, lamps and fittings
on the buildings; barbed wire on the perimeter wall and pylons on the skyline; oil, puddles, leaves,
litter, ivy and old paint from past games; worn paint markings in the warehouse and on the yard;
crows wheeling overhead and scattering at gunfire; dust kicked up underfoot, footprints in the dirt
(in paint, or wet, if you've trodden in some), ripples in the puddles, water dripping through the
holes in the roof, and litter, bags and torn roofing blowing in one wind; paint running down the
walls where it lands; the generated marker in your gloved hands, and your own shadow on the ground;
three rigged opponents who plant every step (feet that stay put on the ground whichever way they
move, a shooter's stance standing still, steps round when they turn, short last steps pulling up from a run),
glance round and look at a noise before they turn to it, shift their weight and breathe, keep their knees and elbows
out of the cover they tuck in behind, bring their markers up off a wall instead of pushing them through it, flinch when
a ball hits them, put the marker and a hand up to walk off when they're out, and refill from their pods in plain sight;
the compound itself behind the main menu; and the
training ground dressed to match, with netting, a timber backstop, inflatable bunkers and masked
dummies covered in old paint.

Every sound is synthesised in code: the marker's report thinning as the tank empties, paint breaking and
bouncing off metal, glass, wood, brick, tarp and bodies, footsteps on each floor (splashing in puddles),
doors by kind, refills, the hit marker, the breakout horn and the referee's whistle, all heard from where
they happen, muffled through walls and echoing in the halls; wind that rises with the gusts, crows,
distant traffic and trains outside, and each room's own sound inside (dripping water, pigeons in the
rafters, a draught through broken windows, the dead quiet of a cold store). The bots shout their callouts in six
voices, two for each of the three opponents, and a referee calls the round, all subtitled too.

The settings (in the main menu and the pause menu) come in five tabs: Controls, where every action can be bound to
two keys or mouse buttons and a pad button or stick, with clashes shown and swappable, and crouch and walk can be
toggles; Video (window mode, v-sync, frame cap, the graphics preset and each of its parts, render scale, field of
view); Audio (five volumes, subtitles); Gameplay (crosshair style, colour and size, hit marker, head-bob, camera jolt,
paint on your mask); and Accessibility (two colourblind-safe team colour sets, HUD size).

## Run it

**Just want to play (Windows)?** Download the latest test build,
[Pb-windows.zip](https://github.com/Tim-D-W101/Pb/releases/download/test-build/Pb-windows.zip), unzip it
and double-click `Play.bat`. No Godot or .NET needed. `Play.bat` keeps the game up to date: each time,
it downloads only the files that changed since your version (under 1 MB for a code change; the art
comes in packs of its own, one per texture or model, so only new or changed art comes down), then
starts the game.
Windows may ask you to confirm ("More info" → "Run anyway") because the game isn't signed. Every CI
run publishes the build there (and as the run's "Pb-windows" artifact); `tools/package/windows-build.sh`
makes the same build locally.

**Playing together:** **Play with others** in the menu hosts a game or joins one, on your network or over the internet.
A dedicated server runs on its own, on your PC or a rented Linux machine. The release has both:
`Pb-server-windows.zip` and `Pb-server-linux.tar.gz` (`tools/package/server-build.sh` makes them locally). Port
forwarding, the firewall and the server's settings, step by step: [docs/hosting.md](docs/hosting.md).

To run it from Godot:

1. Install **Godot 4.7.2 – .NET** (the ".NET" download, not the standard one) and the
   **.NET 8 SDK** (or newer).
2. Open `game/project.godot` in Godot. The first open imports the project and builds the C# code.
3. Press **F5** (Run Project) for the main menu. **Play**, then pick an area, where in it to play (the whole of it or
   one of its parts: every area and place is open from the start), a mode, how many play, an objective and a
   difficulty, **Start**, and **Start** again on the briefing card. **Training ground** is the Phase 1 range.

Only want to play it? `tools/package/godot-project.sh` packs the game as one self-contained
project zip (`builds/Pb-godot-project.zip`). In Godot's Project Manager, click **Import**, choose
the zip and an empty folder to install it into, then follow its `HOW-TO-PLAY.txt`. GitHub's own
"Download ZIP" won't import that way: Godot unpacks only the `game` folder, which needs `src` beside it.

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
| Open or shut the door you're facing (hold to ease it open) | F | X / Square, facing a door |
| Get on the ladder you're facing (or, facing out over its top, get on to climb down) | F | X / Square, facing a ladder |
| On a ladder: climb up / down; let go | W / S; Space | Left stick up / down; A / Cross |
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
files. **F12** cycles graphics presets (low / medium / high / ultra); the pause menu also has a
**render scale**, which draws the 3D view at a lower resolution and sharpens it back up, for slower
graphics cards. Besides the usual ambient occlusion,
shadows and fog, a preset sets how many weeds you see and how far, and whether sunbeams with
drifting dust shine through the windows and holes in the roof and daylight spills in around them.

### A round

Clear the compound: eliminate every opponent before the clock runs out. One hit and you're out,
and so are they. When the last opponent or you go out, the round waits a moment (at most 1.5 s)
for balls still in the air, so going out together is a trade. You start with a full loader and a
few spare pods; walk over a pod to put it in an empty pod slot, or over an air tank to refill
yours. The summary shows your time, shots, hits, accuracy, eliminations and pickups.

Played in part of an area, the round stays inside it: red-and-white tape on posts marks the edge
wherever you could walk across it (doorways included), and you can't walk past it, though paint
flies over. The briefing map shows the part you're in, with the rest dimmed.

### The HUD

Top centre, the clock, with an icon for you on the left and one per opponent on the right: filled
while they're in, greyed out with a cross once they're out. The kill feed (top right) names who
got whom, where and from how far. When an opponent shouts close by ("Contact!", "Hit!"), it shows
as a subtitle. Walk near a pod or air tank and a prompt under the crosshair says what it is, or
that you've no room for it. The crosshair flashes when your ball puts someone out. Your gear
(loader, pods, air) is bottom right; the paint count turns amber and says LOW when the loader is
nearly empty, and red with EMPTY when it is. Once you're out, a camera above and behind where you
stood shows you, marker up, and whoever got you.

### The opponents

Each opponent stands at a spawn whose roles give its behaviour: a **sentry** holds its post and
scans it a look at a time, a **patroller** walks a route, a **rusher** comes straight at you firing on the move. They
see where their head looks, and it turns before their body does.
They spot you faster up close, in daylight, standing or moving, and in the middle of their view;
they hear shots, balls breaking near them and your footsteps (crouch-walk to stay quiet), and
remember where you were. Once they've seen you they shoot, then take cover and peek round it,
refilling when they run low. Lost, they flank, push or search. The tier sets how quickly they
react, how well they aim and how bold they are.

Where everyone starts changes every round: you come in at the gate, the west breach or the north-east
collapse, and the opponents start at random places around the compound, out of your sight, each with
a random role, so you can't learn where they are.

They're drawn by one of three rigged character models, posed every frame to match their hitboxes:
crouching, leaning, aiming, walking and running with movement clips, both hands on the marker (the
same generated one you hold), one hand off it while they refill. Each
wears an armband in their team colour and a slightly different tint, and flinches when a ball hits.
Paint splats stick where the ball hit and move with them.

Settings changed in game are saved to `user://settings.json`, your key bindings as changes over the defaults in
[`game/data/input.jsonc`](game/data/input.jsonc); an older file loads with the new settings at their defaults.

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
| `levels/areas.jsonc` | The areas to play in, all open, in menu order, with their difficulty tiers (bot difficulty, time limits, starting pods, pickups), each area's roster for scripted runs, and what the records count. Where in an area to play is the level file's `places` |
| `rules.jsonc` | Round rules: mode, settle window for balls in the air, trades, pickup radius |
| `stress.jsonc` | Stress-mode ball count and cannons |
| `presentation.jsonc` | Team colours, FOV, ball visibility, splats, HUD, audio, lighting, weeds, sunbeams, dust, window light, graphics presets, character models and posing |
| `input.jsonc` | Key and gamepad bindings |
| `assets.jsonc` | Provenance of imported art: the Higgsfield job, prompt and source of each texture and model (written by `tools/art/import.sh`) |

## Test it

```bash
dotnet test                                          # 355 sim tests (ballistics vs spec, collision, gear, level kit, places, movement, hitboxes, rounds, bots…) — no Godot needed
dotnet run -c Release --project tools/Pb.Bench       # ballistics report, sim cost at 1k/2k/5k live balls and with 10 players, bot cost
tools/ci/smoke-test.sh /path/to/godot                # headless end-to-end runs of the menu, the range, the compound, a bot match and the art import (Linux/macOS)
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
  tools/     the art import (run headless; left out of exports)
  audio/ shaders/ scenes/
src/Pb.Sim/  engine-free simulation: ballistics, collision, gear, players, level kit, rounds, bots, data loading (plain C#, no Godot)
tests/       xUnit tests for Pb.Sim
tools/       Pb.Bench benchmark, CI scripts, art import (tools/art/import.sh)
docs/        spec, architecture, phase plans and reports
```

The [architecture](docs/architecture.md) explains the design. The [spec](docs/spec.md) is the
source of truth for requirements.
