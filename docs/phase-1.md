# Phase 1 — Ballistics sandbox: task list

> **Status: proposed. Waiting for go-ahead.** Design: [architecture.md](architecture.md). Requirements: [spec.md](spec.md) §1.1, §1.4, §1.5 (basic movement only), §11.

## Goal

A playable first-person range where you:

- fire physically simulated paintballs,
- see the arc and drop,
- watch balls break or bounce,
- push 1,000+ live balls,

with the ballistics proven by automated tests.

## Scope

**In:**

- Scaffolding and CI.
- Data layer.
- `Pb.Sim`: ballistics, ball collision, break/bounce, dispersion, marker, loader, pods, air.
- Basic controller: walk/run/sprint/crouch, look, FOV.
- Range with targets at 10/20/30/40 m.
- Ball rendering and splats.
- Minimal HUD, perf overlay, debug tools, stress mode.
- Tests and benchmark.

**Out (Phase 2+):** lean, slide/dive, shoulder-swap, bunker field loader, rounds, bots, spectator, networking, final art and audio.

## M1.0 Scaffolding

- [ ] Godot 4.7.2 .NET project in `game/`: Forward+, Jolt, 120 Hz physics tick. `Pb.sln` containing `Pb.Game`, `Pb.Sim`, `Pb.Sim.Tests`, `Pb.Bench`.
- [ ] `.gitignore`, `.gitattributes` (LFS patterns for future binaries), `.editorconfig`, pinned engine-version file.
- [ ] Input map for KB/M and Xbox/PlayStation pads: move, look, fire, sprint, crouch, refill, fire mode, crosshair toggle, debug keys.
- [ ] CI (GitHub Actions): build → `dotnet test` → Godot headless import + smoke run.
- [ ] `README.md` (setup, run, test) and `CLAUDE.md` (units, data rule, original-IP rule, test commands).

*Done when:* a clean clone builds, the test suite runs green, and the empty scene launches.

## M1.1 Data layer

- [ ] JSONC loader (comments, trailing commas). Unit-suffixed keys are converted to SI. Validation errors name the file and field.
- [ ] Default files:
  - `projectiles/paintball_68`
  - `markers/standard`
  - `air/standard_1100ml_310bar`
  - `loaders/standard` (200) with pods (3 × 140, refill 2.5 s)
  - `break_model`, `movement`, `ranges/phase1`, `debug`, `stress`
- [ ] Dev hot-reload key: re-reads and re-applies data without a restart.

*Done when:* every §1 default from the spec lives in a data file and no gameplay number is hard-coded.

## M1.2 Simulation core (`Pb.Sim`)

- [ ] PCG32 RNG and per-shot seed hashing.
- [ ] Ball pool: struct-of-arrays, capacity from data (default 4,096), free list, zero allocations per tick.
- [ ] RK2 integrator: quadratic drag + gravity at 120 Hz.
- [ ] Sweep tests against primitives inflated by ball radius: plane, box, cylinder, capsule, sphere, wedge. Uniform-grid broadphase, surface IDs on every collider.
- [ ] Break/bounce model (per-surface logistic on normal speed) and bounce response. Bounced balls are never lethal.
- [ ] Dispersion cone, velocity variance, moving spread. Muzzle origin with crosshair convergence.
- [ ] Fire control: semi, ramping, 10.5 bps cap with sub-tick spawn offsets. Blocked by sprint, refill, empty loader, empty air.
- [ ] Loader (200) and pods (3 × 140). Refill takes 2.5 s, blocks firing, and fire or sprint cancels it.
- [ ] Air tank: 1.1 L at 310 bar, per-shot draw, velocity drop below threshold, about 1,000 full-velocity shots.
- [ ] Hit receivers behind a tick-indexed hitbox provider. Range targets use it now. Players and server rewind plug into the same interface later.
- [ ] Event queue: `ShotFired`, `BallBroke`, `BallBounced`, `BallDespawned`, `TargetHit`, `RefillStarted/Completed/Cancelled`, `AirLow`.
- [ ] Despawn on field bounds, at rest, or at max lifetime.

## M1.3 Tests (`Pb.Sim.Tests`)

| Test | Pass criterion |
|---|---|
| Drop at 20 m (level, 1.5 m, 88 m/s) | 0.35 m ± 10% |
| Speed at 20 m | 58 m/s ± 10% |
| Drop at 30 m | 0.92 m ± 10% |
| Max range and best angle | 93 m ± 10%, best angle within 25–35° |
| Reference regression | All of the above within 1% of an RK4 reference solution |
| No tunnelling | 10,000 randomized shots at a 1 cm panel, straddling tick boundaries: 0 pass-throughs |
| Break/bounce | Head-on at 58 m/s on an inflatable: ≥ 99% break. 15° glancing at the same speed: ≤ 30% break. Bounced balls: 0 lethal hits. |
| Dispersion | Every sample within 0.6°. Velocity within 88 ± 1.5 m/s. Moving widens the cone as configured. |
| Determinism | Same seed and inputs → bit-identical ball states and outcomes |
| Fire rate (fire control alone, unlimited paint and air) | Ramping at 8 pulls/s for 60 s → 630 ± 2 shots. Semi = one shot per pull. Cap never exceeded. |
| Loader and pods | Capacities respected. Refill takes exactly 2.5 s. A cancelled refill transfers nothing. No shots during a refill. |
| Air | Default fill gives 1,000 ± 5% full-velocity shots. Velocity falls monotonically below the threshold. An empty tank can't fire. |
| Pool | 1,000+ live balls with zero managed allocations per tick |
| Data validation | Missing or out-of-range values fail with the file and field in the message |

## M1.4 Playable sandbox (`game/`)

- [ ] **`SimDriver` node:** owns the sim, ticks it at 120 Hz, drains events to presentation.
- [ ] **Player controller:**
  - Walk 3 / run 5.5 / sprint 7 / crouch 2 m/s, all from data.
  - Mouse and gamepad look, sensitivity and invert.
  - FOV 90°, adjustable 70–110° (horizontal at 16:9).
  - Minimal head-bob with a toggle.
  - Commands sampled per tick.
  - Sprint blocks firing; moving widens spread.
- [ ] **Viewmodel:** greybox marker, loader and tank, all visible, drawn with their own FOV. Fire, refill and fire-mode toggle work.
- [ ] **Ball renderer:**
  - One MultiMesh, team colour, interpolated between ticks.
  - Minimum on-screen size plus an optional streak.
  - The visible ball blends from the drawn barrel into the true path.
- [ ] **Splats:** pooled decals, capped at 2,000 with the oldest fading first. A small puff on bounce.
- [ ] **Range:**
  - 20 m × 110 m of flat turf, with distance markers every 10 m to 100 m so max-range lobs (93 m) land in view.
  - Targets at 10/20/30/40 m with hit feedback and hit counters.
  - Two angled inflatables for bounces.
  - A 1 cm panel for the tunnelling check.
- [ ] **HUD:** toggleable crosshair, loader count + pods, air gauge in bar, fire mode. A perf overlay shows fps, frame ms, sim ms per tick, live balls and decals.
- [ ] **Debug:** predicted-arc overlay with drop readouts at 10/20/30/40 m. Stress mode uses ball cannons to sustain N ≥ 1,000 live balls, with N set in data.
- [ ] **Placeholder sounds** for shot, break and bounce, routed through the event → `AudioDirector` path. Real audio comes in Phase 5.

## M1.5 Verify and report

- [ ] CI green. Headless benchmark numbers for 1k/2k/5k live balls.
- [ ] `docs/reports/phase-1.md`: what was built, how to run it, known issues, what's next.
- [ ] You run the stress scene on your PC and confirm ≥ 60 fps at 1,000 live balls (the perf overlay shows it).

## Acceptance mapping

| Spec check | How it's demonstrated |
|---|---|
| Ballistic unit tests pass | M1.3 suite green in CI |
| 1,000 live balls at 60 fps | Stress mode + perf overlay on your hardware. The headless benchmark guards against regressions. |
| Visible arc and drop | Targets at four distances, distance markers, arc overlay, minimum-size ball rendering |
| Break/bounce works | Splats vs bounce puffs on targets and angled inflatables, backed by the statistical tests |

## Planned commands (C# path)

```
dotnet test                            # simulation tests (no engine needed)
dotnet run --project tools/Pb.Bench    # headless ball benchmark
# play: open game/project.godot in Godot 4.7.2 (.NET) and press F5
```
