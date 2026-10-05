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
tools/package/godot-project.sh                        # self-contained project zip for Godot's Import
```

The Windows build needs the export templates: `tools/package/fetch-templates.py 4.7.2 windows_release_x86_64.exe
windows_release_x86_64_console.exe` pulls just those from the 1.2 GB release archive. Exported builds always
start on the main menu; `-- --level=ID` (with the level's other options) skips it once. The game's own pack leaves
`art/` out: `tools/package/art-packs.sh` exports it as one pack per asset (`art/Pb-art-<id>.pck`), which `ArtFiles`
mounts in exported builds, and the release lists every game file's SHA-256 so `Play.bat` downloads only what changed.

Godot 4.7.2 .NET is expected on PATH as `godot` (CI installs it with `tools/ci/install-godot.sh`).
Build `game/Pb.csproj` before running Godot headless.

## Importing art

Art is generated with Higgsfield, then the finished result is imported:

```bash
tools/art/import.sh texture <material-id> <job-id> <generator> <url> "<prompt>"   # tiling albedo + normal + roughness maps
tools/art/import.sh model <prop-id> <job-id> <generator> <url> "<prompt>"         # GLB; prints its measured size
tools/art/import.sh clip <clip-id> <job-id> <generator> <url> "<prompt>"          # a rigged GLB's movement clip, cut down to rig + animation
```

Each import records its provenance (job, generator, prompt, URL, files) in `game/data/assets.jsonc`, and a
sim test fails if the kit or the characters use a texture or model without a record. Then point the
material's `albedo`, `normal` and `roughnessMap` (`kit/materials.jsonc`), the prop's `model`
(`kit/props.jsonc`) or a character model or movement clip (`presentation.jsonc` → `characters`, `characters.clips`) at the files. Characters
must use the generator's biped rig (the bone names `CharacterPoser` binds). One generation can hold four
materials (a 2 × 2 sheet): cut each with `--region=x,y,w,h`. Cut a regular pattern (bricks, planks,
corrugations) to whole repeats and pass `--repeats=across,down` (and `--stretch` for a cut that isn't
square), so its seams blend in step with it; set `tile_m` to the cut's real size. Give materials without a
regular pattern `"breakUpRepeat": true`, so their repeat doesn't show over a yard. The game loads art through
`ArtFiles`: anything missing falls back to the procedural look, greybox or hitbox boxes, and
`-- --no-art` ignores all of it (CI's bot match), so the game and CI never depend on art.

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
sprinting, strafing, backing off and walking crouched, seen from the side (movement clips, or the steps without them). `-- --bot-match` (CI) has a bot play your slot until
the round ends. `--mode=solo|ffa|teams` and `--size=N` pick the mode and size (the menu's choices; the
modes are in `rules.jsonc`), e.g. `-- --round-tour --mode=ffa --size=6`. `-- --menu-tour` on the
main scene shows each menu screen. Frame rates under lavapipe mean nothing; only the owner's
hardware can confirm the 60 fps target.

## Conventions

- C# 12, nullable enabled, file-scoped namespaces. `Pb.Sim` builds with warnings as errors.
- Godot scripts: one class per file, file name = class name, `partial`.
- Hot paths (anything per ball or per tick) must not allocate. A test enforces this for `SimWorld.Step`.
- The sim uses System.Numerics. Convert with `Pb.Game.Core.Conv` (`ToGodot()` / `ToSim()`).
- Coordinates match Godot: Y up, yaw 0 faces −Z, positive yaw turns left, positive pitch looks up.
