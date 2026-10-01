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
```

Godot 4.7.2 .NET is expected on PATH as `godot` (CI installs it with `tools/ci/install-godot.sh`).
Build `game/Pb.csproj` before running Godot headless.

## Verifying visuals without a GPU

A cloud container can render with Mesa's software Vulkan (lavapipe) under Xvfb. Godot's Movie
Maker writes frames you can inspect:

```bash
# The compound level (main scene): hold each of the level's viewpoints for 10 frames, then quit.
xvfb-run -a -s "-screen 0 1600x900x24" godot --path game --rendering-driver vulkan \
  --write-movie /tmp/cap/f.png --fixed-fps 30 -- --shots=10
# The Phase 1 range: a scripted tour (game/core/DemoTour.cs).
xvfb-run -a -s "-screen 0 1600x900x24" godot --path game --rendering-driver vulkan \
  --write-movie /tmp/cap/f.png --fixed-fps 30 --quit-after 450 res://scenes/Range.tscn -- --demo
```

Viewpoints live in the level file (`viewpoints`). `-- --posture-demo` instead runs a short scripted
shoulder swap / lean / muzzle-in-cover sequence through the player's own camera. Frame rates under
lavapipe mean nothing; only the owner's hardware can confirm the 60 fps target.

## Conventions

- C# 12, nullable enabled, file-scoped namespaces. `Pb.Sim` builds with warnings as errors.
- Godot scripts: one class per file, file name = class name, `partial`.
- Hot paths (anything per ball or per tick) must not allocate. A test enforces this for `SimWorld.Step`.
- The sim uses System.Numerics. Convert with `Pb.Game.Core.Conv` (`ToGodot()` / `ToSim()`).
- Coordinates match Godot: Y up, yaw 0 faces −Z, positive yaw turns left, positive pitch looks up.
