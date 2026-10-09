# Notes for AI agents working on this repo

Read [docs/spec.md](docs/spec.md) (requirements, verbatim from the owner) and
[docs/architecture.md](docs/architecture.md) before changing anything. Each phase has a plan
(`docs/phase-N.md`) and ends with a report in `docs/reports/`.

## Ground rules (from the owner, non-negotiable)

- **Original IP only.** No real brand names, logos, licensed equipment or names of existing
  paintball games. Invent all gear, brands, currency and map names.
- **Metric units** in code and UI (m, m/s, g, bar, L). Data keys carry the unit as a suffix
  (`mass_g`, `muzzleVelocity_mps`). `Pb.Sim` converts to SI once on load; code never mixes units.
- **Every gameplay value lives in `game/data/*.jsonc`**, never hard-coded. Add a key (with a
  comment explaining it) rather than a constant.
- **Work phase by phase.** Plan first, wait for the owner's OK, and end each phase with a
  report: what was built, how to run it, known issues, what's next.
- **Tests for ballistics and match rules.**

## Architecture in one paragraph

`src/Pb.Sim` is plain C#, with no Godot references allowed, and decides every outcome:
ballistics, ball collision, gear, hitboxes, match rules. It's deterministic for a given seed
and input stream. Hosts call `SimWorld.Step()` once per fixed tick with one `InputCommand` per
player, then read and clear `SimWorld.Events`. The Godot project in `game/` samples input,
does the collide-and-slide for walking, and renders. Presentation systems implement
`ISimEventListener` and never change outcomes. Bots (Phase 2) and network clients (Phase 4)
produce the same `InputCommand`s as humans. Since Phase 2 the game is explorable compound levels
(see `docs/architecture.md` §14): levels are built from a data kit of analytic primitives, so paint
collision never needs engine meshes.

## Commands

```bash
dotnet build Pb.sln                                   # everything, including the Godot project
dotnet test                                           # Pb.Sim tests (fast, no engine)
dotnet run -c Release --project tools/Pb.Bench        # ballistics report + tick cost
tools/ci/import.sh godot && tools/ci/smoke-test.sh godot   # headless end-to-end check
tools/package/windows-build.sh godot                  # ready-to-run Windows build (CI artifact "Pb-windows"), art in art/*.pck
tools/package/server-build.sh godot                   # then the dedicated servers: Pb-server-linux.tar.gz, Pb-server-windows.zip
godot --headless --path game -- --server              # a dedicated server from the source (game/data/server.jsonc; docs/hosting.md)
tools/ci/net-round.sh godot                           # CI's networked rounds: a dedicated server and five players over UDP, two of them cheating
tools/ci/wine-join.sh builds/server/linux/Pb-server builds/windows/Pb   # the Windows build plays a round on the Linux server under Wine
tools/package/godot-project.sh                        # self-contained project zip for Godot's Import
python3 tools/levels/hospital_wings.py game/data/kit/buildings   # regenerate the hospital wings (edit the script, not the files)
```

The menu's picture of each place (`game/ui/places/LEVEL_PLACE.jpg`) is taken in the game, from the viewpoint the level
file's place names in `"still"`; retake a level's after changing it (under lavapipe, below), then import:
`xvfb-run -a -s "-screen 0 1600x900x24" godot --path game --rendering-driver vulkan res://scenes/Level.tscn -- --level=ID
--place-stills=res://ui/places` and `godot --headless --path game --import`.

The Windows build needs the export templates: `tools/package/fetch-templates.py 4.7.2 windows_release_x86_64.exe
windows_release_x86_64_console.exe` pulls just those from the 1.2 GB release archive. Exported builds always
start on the main menu; `-- --level=ID` (with the level's other options, and `--place=ID` for one of the level's
`places`) skips it once. The game's own pack leaves
`art/` out: `tools/package/art-packs.sh` exports it as one pack per asset (`art/Pb-art-<id>.pck`), which `ArtFiles`
mounts in exported builds, and the release lists every game file's SHA-256 so `Play.bat` downloads only what changed.
The Windows build runs headless under Wine (`apt-get install wine`), the way to catch faults only the Windows .NET
runtime has: `WINEDLLOVERRIDES=dinput8=d wine Pb.exe --headless -- --level=ID --bot-match --no-art` (Wine's DirectInput
crashes Godot; exported builds take no scene path, and the menu's `--smoke-test` comes first).

Playing with others from the command line (Phase 4; the menu's Play with others does the same):
- **Hosting and joining:** `-- --host` (on `--port=N`, with `--password=WORD`) or `-- --join=ADDRESS`, as `--name=NAME`
  playing character `--look=N`.
- **In the lobby:** `--host-wait=N` starts the countdown once N people are in, `--ready` readies you up, `--say=TEXT`
  says something in the chat, and `--rounds=N` has a `--bot-match` copy play N rounds before it quits.
- **A worse connection on purpose:** `--net-lag=MS` (round trip), `--net-jitter=MS`, `--net-loss=PCT`; and
  `--no-lag-compensation` on the host.
- **CI's cheats:** `--net-cheat=fire` flips the trigger every tick; `--net-cheat=fast` runs a joining copy's ticks twice
  as fast.
- **The dedicated server:** `-- --server` (with `--server-config=PATH`).
- **Every networked copy** prints a `NET RESULT` line per round, so CI can compare them. `--snap-every=S` (with
  `--snap-dir`) saves the screen every S seconds of a networked round, and `--show-scoreboard` holds the scoreboard up.

Godot 4.7.2 .NET is expected on PATH as `godot` (CI installs it with `tools/ci/install-godot.sh`).
Build `game/Pb.csproj` before running Godot headless.

## Importing art

Art is generated with Higgsfield, then the finished result is imported:

```bash
tools/art/import.sh texture <material-id> <job-id> <generator> <url> "<prompt>"   # tiling albedo + normal + roughness maps
tools/art/import.sh model <prop-id> <job-id> <generator> <url> "<prompt>"         # GLB; prints its measured size
tools/art/import.sh clip <clip-id> <job-id> <generator> <url> "<prompt>"          # a rigged GLB's movement clip, cut down to rig + animation
tools/art/import.sh voice <voice-id> <job-id> <generator> <url> "<script>"        # one TTS take of a voice's lines, cut into a file per line (ffmpeg)
```

Each import records its provenance (job, generator, prompt, URL, files) in `game/data/assets.jsonc`, and a
sim test fails if the kit or the characters use a texture or model without a record. Then point the
material's `albedo`, `normal` and `roughnessMap` (`kit/materials.jsonc`), the prop's `model`
(`kit/props.jsonc`) or a character model or movement clip (`presentation.jsonc` → `characters`, `characters.clips`) at the files.
A voice's script is the exact lines of `presentation.jsonc` `hud.callouts` (or `hud.referee` for the referee), one per line:
`tools/art/voice-script.py callouts` (or `referee`) prints it, for the generator and for the import's last argument. Its
files are found by their words, so nothing needs pointing at them. Then check the cut lines with
`tools/art/voice-check.py <voice-id>...` (a speech recogniser; `pip install faster-whisper` in a virtual environment): a
take that says a line twice, or runs another line's words into one, is imported again with `--keep-first="LINE"` or
`--keep-last="LINE"` after the script (several lines joined with `|`), which keep that line's first or last part. Characters
must use the generator's biped rig (the bone names `CharacterPoser` binds). One generation can hold four
materials (a 2 × 2 sheet): cut each with `--region=x,y,w,h`. Cut a regular pattern (bricks, planks,
corrugations) to whole repeats and pass `--repeats=across,down` (and `--stretch` for a cut that isn't
square), so its seams blend in step with it; set `tile_m` to the cut's real size. Give materials without a
regular pattern `"breakUpRepeat": true`, so their repeat doesn't show over a yard. The game loads art through
`ArtFiles`: anything missing falls back to the procedural look, greybox or hitbox boxes, and
`-- --no-art` ignores all of it (CI's bot match), so the game and CI never depend on art.

## Sound

Every effect is synthesised in code when the game starts (`game/audio/SoundBank.cs`); there are no recorded sounds
but the voices. To listen to them outside the game: `godot --headless --path game res://tools/ArtImport.tscn --
--sounds=/tmp/sounds` writes every variation as a WAV file. Headless runs build and count every sound but start none.

## Verifying visuals without a GPU

A cloud container can render with Mesa's software Vulkan (lavapipe) under Xvfb (`apt-get install
mesa-vulkan-drivers` if `vulkaninfo` finds no device; without it Godot falls back to OpenGL, which
isn't what players see). Godot's Movie Maker writes frames you can inspect:

```bash
# The compound level: hold each of the level's viewpoints for 10 frames, then quit.
xvfb-run -a -s "-screen 0 1600x900x24" godot --path game --rendering-driver vulkan \
  --write-movie /tmp/cap/f.png --fixed-fps 30 res://scenes/Level.tscn -- --shots=10
# The Phase 1 range (the training ground): a scripted tour (game/core/DemoTour.cs), or its viewpoints with --shots.
xvfb-run -a -s "-screen 0 1600x900x24" godot --path game --rendering-driver vulkan \
  --write-movie /tmp/cap/f.png --fixed-fps 30 --quit-after 450 res://scenes/Range.tscn -- --demo
```

The main scene is the menu (`scenes/Main.tscn`), so name the scene for anything else. Viewpoints
live in the level file (`viewpoints`), and the range's in `ranges/phase1.jsonc`; `--views="x,y,z>tx,ty,tz;…"` (or `x,y,z,yaw,pitch`) replaces them
for close-ups of anything in the level. On `Level.tscn`, through the player's own camera,
`-- --posture-demo` runs a short scripted shoulder swap / lean / muzzle-in-cover sequence,
`-- --duel-demo` an elimination each way (callout, splat on a character, mask spray, spectator
view; add `--duel-distance=2` for a close-up), `-- --round-tour` a round's screens from briefing to summary, and `-- --bot-demo` bots
fighting you from cover with the F3 overlay, and `-- --gait-demo` one opponent standing, walking, running,
sprinting, pulling up, strafing, backing off, walking crouched and looking round, seen from the side (the planted steps,
or the movement clips with `characters.legs` = `"clips"`), printing how far the planted feet slid (`--gait-only=NAME`
plays only the moves starting with NAME). `-- --cover-demo` tucks an opponent in behind low cover, stands it up to shoot
over and tucks it in again, printing how far its knees and elbows got into the cover (`--cover-at=X,Z` picks the cover
nearest that point); it runs headless too, for the numbers alone. `-- --ladder-demo` has an opponent climb the level's
tallest ladder, step off at the top, turn round and climb down, filmed from behind and to the side (`--ladder=N` for another;
Oxbarrow Works and `--level=rail_yard` have ladders).
`-- --role-demo=marksman` (on `--level=rail_yard`) and `-- --role-demo=flanker` (on `--level=hospital_wing`) show a
Marksman or a Flanker at work with the F3 overlay; they run on the sim's clock, so capture them at `--fixed-fps 6` for
fewer frames. `-- --bot-match` (CI) has a bot play your slot until
the round ends (`--fast --show-summary` at `--fixed-fps 1` films it through to its summary, a second of the round a frame). `--mode=solo|ffa|teams`, `--size=N`, `--objective=eliminate|retrieve|hold` and `--place=ID` pick the mode, size,
objective and where in the area (the menu's choices; they're in `rules.jsonc` and the level file's `places`), e.g.
`-- --round-tour --mode=ffa --size=6 --place=warehouse`; `--render-scale=0.25` (with `--preset=low`) makes long lavapipe
runs quicker; and
`-- --objective-demo --objective=retrieve` (or `hold`) shows the objective through your eyes: its marker, the case or
the room, the ways out. `-- --menu-tour` on the
main scene shows each menu screen. Frame rates under lavapipe mean nothing; only the owner's
hardware can confirm the 60 fps target.

## Conventions

- C# 12, nullable enabled, file-scoped namespaces. `Pb.Sim` builds with warnings as errors.
- Godot scripts: one class per file, file name = class name, `partial`.
- Hot paths (anything per ball or per tick) must not allocate. A test enforces this for `SimWorld.Step`. Measure with
  `Allocations.During` (tests' `TestSupport.cs`), not a bare `GC.GetAllocatedBytesForCurrentThread()`: another thread's
  collection can add a few phantom bytes to the count, which fails such tests at random on CI.
- The sim uses System.Numerics. Convert with `Pb.Game.Core.Conv` (`ToGodot()` / `ToSim()`).
- Coordinates match Godot: Y up, yaw 0 faces −Z, positive yaw turns left, positive pitch looks up.
- `game/Pb.csproj` keeps tiered PGO and quick JIT for loops off. With them on, the .NET 8 JIT on Windows x64 crashes
  the game, and Linux runs never show it (architecture §14.7, Shipping).
