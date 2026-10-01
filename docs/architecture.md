# Architecture plan

> **Status: approved (defaults accepted 2026-09-30); Phase 1 implemented.** On 2026-09-30 the owner changed direction to explorable compound levels; [§14](#14-direction-change-2026-09-30-the-compound) and [phase-2.md](phase-2.md) were approved on 2026-10-01. Requirements are in [spec.md](spec.md); this file explains how they're met. Phase results: [reports/](reports/).

## 0. Open questions, decisions and assumptions

### Questions from planning (owner accepted all defaults)

| # | Question | Default |
|---|---|---|
| Q1 | Coding experience, and C# or GDScript? | **C#.** The simulation core is a plain .NET library tested with `dotnet test`, with no engine needed, and it has plenty of headroom for 1,000+ balls and server-side lag compensation. GDScript suits a newcomer who doesn't want the .NET setup: the layering stays the same, `src/Pb.Sim` becomes `game/sim/`, and tests run through gdUnit4 headless. |
| Q2 | Dev machine (OS/GPU) and first ship platforms? | Windows build first, plus a Linux dedicated server. You run the fps checks because the cloud container has no GPU. |
| Q3 | Online platform and server hosting? | Platform-neutral for Phase 3: ENet over UDP, direct connect plus LAN discovery, behind `IPlatformServices`. A Steam adapter comes later. You host on a small Linux VPS. |
| Q4 | Who makes art/audio after greybox? | I generate greybox and inflatables procedurally from data. Gear and character models come from you or an artist in Blender, with CC0 packs allowed as placeholders. Binaries go in Git LFS. |
| Q5 | Hours/week and budget? | About 5–10 h/week and $0 until Phase 3. This sets how finely each phase is split into reviewable milestones. |

### Decisions

| Topic | Decision |
|---|---|
| Engine | Godot **4.7.2** (latest stable), .NET build, Forward+ renderer, Jolt physics. Exact version pinned in the repo and CI. |
| Language | C# on .NET 8 (what Godot 4.7.2's C# layer targets). |
| Names | Code prefix `Pb`: `Pb.Sim` (engine-free core), `Pb.Game` namespaces in the Godot project (assembly `Pb`, `game/Pb.csproj`). The display title is a project setting, so the game can be renamed any time. |
| Tick rates | 120 Hz simulation (= Godot physics tick), 60 Hz network send, rendering interpolated between ticks. |
| Integrator | RK2 (midpoint). Numbers are in §4.2. |
| Data format | JSON with comments (`.jsonc`), with units in key names (§8). |
| Tests | xUnit for `Pb.Sim`, a Godot headless smoke test, and a headless ball benchmark, all in GitHub Actions. |

### Assumptions where the spec is silent

| Topic | Assumption | Where it's tunable |
|---|---|---|
| Speedball round timeout | The team with more players alive wins the round; a tie means no point | `timeoutRule` in `modes/speedball.jsonc` |
| Interrupted pod refill | Transfers nothing; leftover balls stay in the pod | `refill.partialOnInterrupt` |
| Ball vs ball | No collision (genre standard) | Could be added later |
| Bounced ball | Can still break later and leave paint, but never eliminates (per spec) | – |
| Field symmetry | Reflection across the halfway line, as in standard speedball where both teams share the snake side. 180° rotation is also supported. | `symmetry` per layout |
| Bunker names | Display names come from data. "Dorito" is standard paintball slang, but it echoes a snack trademark, so renaming it is a one-line change. | `bunkers.jsonc` |

## 1. Principles

1. **One simulation, many hosts.** The code that decides outcomes runs identically offline, on a listen server, on a dedicated server, and on clients for prediction. Offline play in Phases 1–2 already goes through the same authority path, so Phase 3 adds a transport instead of rewriting gameplay.
2. **Engine-free core.** Ballistics, ball collision, gear, hitboxes, match rules and the trajectory solver live in `Pb.Sim`, a plain C# library with no Godot references. Given a seed and an input stream it is deterministic, and it's unit-tested without the engine.
3. **Commands in, events out.** Every participant (human, bot, remote client) drives the sim with one `InputCommand` per tick. The sim emits struct events (`ShotFired`, `BallBroke`, `BallBounced`, `PlayerEliminated`, `RoundEnded`, …). Splats, audio, HUD, kill feed and replication subscribe to them. No system calls another directly.
4. **Every gameplay number in data.** Values live in `game/data/*.jsonc`, are validated at load and can be hot-reloaded in dev builds.
5. **Bots are players.** Bots produce `InputCommand`s and go through the same marker, ballistics and rules as humans.
6. **Presentation never decides.** Renderers, audio and UI read state and events. They never change outcomes.

## 2. Layers

```
            game/data/*.jsonc — every tunable, unit-suffixed keys
                        │ load · validate · convert to SI · hot-reload
┌───────────────────────▼─────────────────────────────────────────┐
│ Pb.Sim  (plain C#, no Godot refs, unit-tested)                  │
│ ballistics · ball collision · marker/loader/air · hitboxes +    │
│ 200 ms history · match rules/modes · trajectory solver · seeds  │
└───────▲───────────────────────────────────────────────┬─────────┘
        │ InputCommand per tick                         │ SimEvent queue
┌───────┴──────────────────────────┐     ┌──────────────▼──────────┐
│ Command sources                  │     │ Consumers               │
│ • local player (Godot input)     │     │ • ball renderer, splats │
│ • bots (P2)                      │     │ • audio, HUD, kill feed │
│ • remote clients via Pb.Net (P3) │     │ • replication (P3)      │
└──────────────────────────────────┘     └─────────────────────────┘
```

## 3. Modules

The spec asks for decoupled systems: ballistics, weapons, player controller, match rules, networking, AI, UI and audio. Each has its own module.

| Module | Location | Responsibility | Phase |
|---|---|---|---|
| Core | `Pb.Sim/Core` | `System.Numerics` helpers, unit conversion, PCG32 RNG, seed hashing, view angles | 1 |
| Data | `Pb.Sim/Data` | JSONC → typed definitions, validation (errors name file + field), SI conversion | 1 |
| Ballistics | `Pb.Sim/Ballistics` | Ball pool, RK2 integrator, sweep and resolve, break/bounce, dispersion | 1 |
| Collision | `Pb.Sim/Collision` | Sweep tests vs primitives, grid broadphase, `ICollisionWorld` | 1 |
| Weapons (gear) | `Pb.Sim/Gear` | Fire control, loader, pods, air tank. Gear definitions are cosmetic, with small data-driven stat deltas. | 1 |
| Players | `Pb.Sim/Players` | `InputCommand`, player state, movement model, stance/lean/shoulder, hitbox rig and history | 1–2 |
| Match rules | `Pb.Sim/Match` | Round state machine, `IGameModeRules` (Speedball, CTF, Arcade), scoring, objectives | 2, 4 |
| AI core | `Pb.Sim/AI` | Trajectory solver, cover-point generation from bunker data, difficulty parameters | 2 |
| Net core | `src/Pb.Net` | Protocol, bit-packed serialization, snapshots and deltas, prediction/interpolation buffers, `ITransport` with loopback and lag simulator | 3 |
| Engine glue | `game/core` | Bootstrap, `SimDriver`, data hot-reload, Sim↔Godot conversion, engine-physics collision adapter | 1 |
| Player controller | `game/player` | `CharacterBody3D`, camera, input sampling, viewmodel | 1–2 |
| Ballistics view | `game/ballistics` | Ball MultiMesh and shader, splat decals, impact FX, arc debug | 1 |
| World | `game/world` | `FieldBuilder`, procedural bunker meshes, range, targets, boundary net | 1–2 |
| Bots | `game/ai` | Brain → commands, perception, navmesh (runtime bake) | 2 |
| Networking | `game/net` | ENet transport, lobby flow, headless dedicated-server entry point | 3 |
| UI | `game/ui` | HUD, perf overlay, menus, settings, locker UI | 1–5 |
| Audio | `game/audio` | Event-driven `AudioDirector`, pooled 3D players, per-surface sound sets | 1 (hooks), 5 |
| Platform | `game/platform` | `IPlatformServices` (identity, friends, lobbies, store), offline implementation first | 3+ |

## 4. Simulation

### 4.1 Tick order (120 Hz, fixed dt = 1/120 s, integer tick counter)

1. Collect one `InputCommand` per participant: local input, bots, network.
2. Movement. `MovementModel` (pure) turns command + state into a desired velocity and stance change. The player's `CharacterBody3D` then collides and slides, and the result is written back to the sim's player state. Movement never reads `Input` directly, so the server and client prediction can replay it.
3. `Sim.Tick()` runs: fire control → spawn shots → integrate and sweep balls → resolve hits (break / bounce / eliminate) → match rules → record hitbox history → emit events.
4. Every render frame, presentation drains events and draws state interpolated between the last two ticks.

### 4.2 Ballistics

- `a = −k·|v|·v + g`, where `k = ½·ρ·Cd·A/m`. The defaults give k = 0.02115 m⁻¹ and a terminal velocity of 21.5 m/s.
- The spec's targets check out against its own defaults. This was verified with an RK4 reference (dt = 0.1 ms) before planning:

| Target (level shot, 1.5 m, 88 m/s) | Spec | Defaults give |
|---|---|---|
| Drop at 20 m | ≈ 0.35 m | 0.343 m |
| Speed at 20 m | ≈ 58 m/s | 57.7 m/s |
| Drop at 30 m | ≈ 0.92 m | 0.912 m |
| Max range | ≈ 93 m at ~30° | 93.1 m at 30.0° |

- **Integrator choice.** Semi-implicit Euler at 120 Hz overshoots the 30 m drop by 4.5%, which is half the ±10% test budget (+9.1% at 60 Hz). RK2 at 120 Hz is within 0.01% and costs one extra acceleration evaluation, so RK2 it is.
- A level shot from 1.5 m lands at about 36 m. The 40 m target needs holdover, which is the realistic behaviour the range should show.
- **Pool.** Struct-of-arrays with a fixed capacity from data (default 4,096) and a free list. The tick makes zero allocations, and a test enforces that.
- **Despawn.** A ball despawns when it breaks, leaves the field bounds or net, comes to rest, or exceeds `maxLifetime_s`.

### 4.3 Collision and continuous detection

- Every tick, each live ball sweeps from its previous to its new position. That's 0.73 m per tick at 88 m/s, so a ball skips right past a thin target without the sweep. Sphere sweeps are done as ray tests against primitives inflated by the ball radius (8.65 mm).
- **Primitives:** plane, oriented box, cylinder, capsule, sphere, wedge/prism. That covers every inflatable in the spec. A bunker type is a list of primitives in `bunkers.jsonc`. That one definition generates the visible mesh, the Godot collision shapes for walking and the sim colliders for balls, so the three can never drift apart.
- **Broadphase.** A uniform grid (2 m cells) holds static colliders. Players are checked against a bounding sphere first, then their hitbox primitives.
- **Arbitrary meshes** (scenario map, Phase 4) go through an engine adapter that implements the same `ICollisionWorld`. It is only queried when the grid says a segment is near such geometry.
- **Why not engine raycasts for everything?** 1,000 balls at 120 Hz is 120,000 queries/s, and each engine query from C# crosses the interop boundary and allocates a result dictionary. The analytic tests allocate nothing, run in unit tests and give the server a self-contained hit world.

### 4.4 Break vs bounce

- On impact, compute the normal speed `vₙ = |v·n|`. Then `P(break) = 1 / (1 + e^(−(vₙ − v50)/w))`, with `v50` and `w` set per surface (inflatable, turf, wood, player gear, target…) in `break_model.jsonc`.
- With inflatable defaults of v50 = 22 m/s and w = 5 m/s:
  - A head-on hit at 20 m (58 m/s) breaks about 100% of the time.
  - A 15° glancing hit at the same range (vₙ ≈ 15 m/s) breaks about 20% of the time.
  - A head-on hit at 22 m/s breaks 50% of the time.
- The roll uses the ball's own seeded RNG stream, so every host gets the same result.
- **Bounce.** The ball reflects using per-surface normal restitution and tangential retention, and is flagged `bounced`. After that it can never eliminate.

### 4.5 Shots

- **Origin.** The authoritative origin is a muzzle point computed from player state (eye + shoulder-side offset + lean), not from the viewmodel, so shoulder-swapping genuinely changes exposure. If the eye→muzzle segment is blocked because you're hugging a bunker, the ball breaks on that bunker: you paint your own cover, as in real life. This arrives in Phase 2 behind a data toggle.
- **Aim.** The direction runs from the muzzle to the point under the crosshair: the camera ray hit, or a convergence distance if the ray hits nothing.
- **Dispersion.** Directions are uniform over the solid angle of a 0.6° half-angle cone. Velocity varies uniformly by ±1.5 m/s. Moving adds extra cone per m/s of shooter speed. All of these are in data.
- **Seeds.** `seed = hash(matchSeed, playerId, shotSequence)`. A per-shot PCG32 stream drives dispersion, velocity and break rolls. It uses integer arithmetic only, so it's identical on every platform, and clients can reproduce server shots without waiting.
- **Timing.** Fire control schedules shots with sub-tick offsets, and each ball is advanced by its offset at spawn. That keeps 10.5 bps exact and evenly spaced even though 120 / 10.5 = 11.43 ticks.

### 4.6 Marker, loader, air

- **Fire control.**
  - Semi: one shot per pull.
  - Ramping: after N shots with pulls at X Hz or faster, fire at the cap (N and X in data).
  - The 10.5 bps cap is enforced with a time accumulator.
  - Sprinting, refilling, an empty loader or empty air all block firing.
- **Loader and pods.** The loader holds 200. Three pods each hold 140 and track their own count. A refill takes 2.5 s from the next non-empty pod. It blocks firing, and pressing fire or sprint cancels it.
- **Air.** Pressure bookkeeping: each shot draws a fixed gas amount (bar·L). Once tank pressure falls below the regulator threshold, muzzle velocity scales down along a data curve. Defaults are tuned so a 1.1 L / 310 bar fill gives about 1,000 full-velocity shots, and a test enforces this. The shot sound follows tank pressure (Phase 5).

### 4.7 Players, movement, hitboxes

- **`InputCommand`** holds: tick, move axes, yaw/pitch, and buttons (fire, sprint, crouch, slide, lean L/R, swap shoulder, refill, interact, fire mode). Mouse look is applied to the camera every render frame for responsiveness, and the command carries the angles at tick time.
- **Movement.** All of these are data: walk 3 / run 5.5 / sprint 7 / crouch 2 m/s, slide/dive momentum and friction (ends crouched), lean angle 20° with lean-in/out times tuned for snap-shooting, and head-bob amplitude. If replaying engine movement for prediction proves unreliable in Phase 3, the fallback is a kinematic sweep against the analytic world, since speedball fields are all primitives.
- **Hitboxes.** `HitboxRig` maps player state (stance, lean, shoulder side, yaw, pitch) to primitives tagged body, mask, marker, loader or tank. All parts are lethal by default (`lethalParts` in data). The last 200 ms of hitboxes are kept (24 ticks) for server rewind.
- **Eliminated** players raise their marker, lose input and switch to a spectator camera (Phase 2).

### 4.8 Match rules and modes

- **`MatchState`:** Setup → Countdown (horn) → Live → RoundEnd(reason) → Intermission → … → MatchEnd. It is pure C#, driven by sim events and a tick timer, and rules plug in through `IGameModeRules`. It's tested exhaustively from Phase 2.
- **Speedball:**
  - 5v5.
  - A round is won by eliminating all opponents or hanging the buzzer (hold interact at the opposing start station for `hangTime_s`).
  - 180 s round timer, with the timeout rule from §0.
  - The match is a race to 4.
- **CTF, Arcade (3 s respawn, timed, score by eliminations) and co-op vs AI** (bots fill empty slots) are added rule classes plus data files in Phase 4.

## 5. Presentation (Godot)

- **Ball readability.** At 1080p with a 90° FOV, a 17.3 mm ball is about 1.7 px wide at 10 m and under 1 px beyond about 17 m. Balls render through one MultiMesh whose shader enforces a minimum on-screen size (default 3 px) with an optional faint motion streak. This is cosmetic only: collision stays at true size, and the setting applies to everyone equally.
- **Visible vs true path.** The visible ball starts at the drawn barrel and blends into the true path over about 0.1 s.
- **Viewmodel and FOV.** The viewmodel (marker, loader, tank) is drawn with its own FOV so it never clips into the bunker you're hugging. The FOV setting means horizontal degrees at 16:9. Godot's camera FOV is vertical by default, so it's converted, and wider screens get more horizontal view.
- **Splats.**
  - Pooled `Decal`s, capped at 2,000, oldest fading first.
  - Player splats clear at round end.
  - A mask-spray overlay appears when a ball breaks within 0.5 m of a face (Phase 2).
  - If decals prove expensive, Phase 5 moves to splat shaders.
- **HUD.** The HUD reads a presentation model (loader/pods, air in bar, timer, alive icons…) that is updated from events, never from sim internals.
- **Audio.** `AudioDirector` maps events and surface IDs to pooled `AudioStreamPlayer3D`s. Buses are Master, SFX, UI and Ambience.

## 6. Networking (Phase 3; the earlier phases already respect these constraints)

- **Topology.** Server-authoritative. The dedicated server is a headless export of the same project. A listen server runs the server and a client in one process.
- **Client → server.** Input commands are sent redundantly (last N), batched at 60 Hz. The server derives shots from commands. Clients predict their own shots with the same seed.
- **Server → client.**
  - Snapshots at 60 Hz: quantized player state and match state, delta-compressed against the last acknowledged snapshot.
  - A reliable event stream: `ShotFired` (so others simulate the ball cosmetically), `BallResolved` (authoritative outcome and point), eliminations.
  - Balls are never replicated per tick.
- **Own player.** Prediction plus reconciliation: unacknowledged commands are replayed on correction.
- **Other players.** An interpolation buffer of about 2–3 snapshot intervals.
- **Lag compensation.** Each ball stores its shooter's rewind (latency + interpolation delay, capped at 200 ms). Hit tests against players use the hitbox history at `tick − rewind`.
- **Validation.** The server enforces the rate-of-fire cap, loader/pod counts, air, max speed per stance and command rate. It rejects and logs violations.
- **Transport.** `ITransport` provides unreliable and reliable-ordered channels. ENet comes first, and a loopback transport serves tests. A wrapper injects latency, jitter and loss for the "fair at 100 ms" acceptance check.
- **Lobby.** Create/join, team slots with auto-balance, loadout preview, ready-up, map vote and countdown, as a server-driven state machine.
- **Known trade-off.** Client-known seeds let a cheater predict spread. That's acceptable at this scale, and server-only seeds can come later if needed.

## 7. AI (Phase 2)

- **A bot is a brain that emits `InputCommand`s.** Its perception is fair:
  - Line-of-sight tests through the sim collision world.
  - Hearing shot events within range.
  - Decaying memory of last-seen positions.
- **Cover points** are generated automatically from bunker data: left/right/top peek positions per enemy direction, with exposure scores. Lanes are the known running gaps.
- **Behaviour.** A state machine runs Breakout (sprint to the assigned bunker, shooting lanes on the way) → Hold (peek, snap-shoot, shoot lanes) → Advance (on numbers advantage) → Flank → Hang flag.
- **Aim.** The shared trajectory solver leads the target, accounting for drag and drop. Difficulty (Easy/Normal/Hard in `bots.jsonc`) adds reaction delay, aim error, tracking lag and a slower decision interval.
- **Navigation.** A navmesh is baked at field load from the generated geometry.
- **Tooling.** Debug overlays show cover points, lanes and aim error. Headless bot-vs-bot matches serve as regression tests.

## 8. Data conventions

- JSON with comments (`.jsonc`), one concern per file, with `id` fields for cross-references.
- **Units in key names:** `mass_g`, `diameter_mm`, `muzzleVelocity_mps`, `fillPressure_bar`, `tankVolume_L`, `refillTime_s`, `leanAngle_deg`. The loader converts to SI (m, kg, s, Pa, rad) once, so code never sees mixed units. The UI shows metric units (m, m/s, bar).
- **Validation.** Range and required-field checks name the file and field. The game refuses to start with a clear message rather than run with a bad value.
- **Hot reload** (dev builds): a key re-reads all data and re-applies it in place.

```jsonc
// game/data/markers/standard.jsonc
{
  "id": "marker.standard",
  "muzzleVelocity_mps": 88.0,
  "velocityVariance_mps": 1.5,        // uniform ±
  "dispersionHalfAngle_deg": 0.6,
  "movingDispersion_degPerMps": 0.1,  // extra cone per m/s of shooter speed
  "rateCap_bps": 10.5,
  "fireModes": ["semi", "ramping"],
  "ramping": { "afterShots": 3, "minPullRate_hz": 5.0 }
}
```

## 9. Testing and CI

- **`tests/Pb.Sim.Tests` (xUnit):**
  - Ballistics validation: the spec targets at ±10%, plus ≤1% against a high-precision reference so real regressions can't hide inside the ±10% band.
  - Continuous collision detection (CCD), break/bounce statistics, dispersion bounds, determinism.
  - Fire control, loader/pods, air, zero-allocation tick, data validation.
  - Match rules from Phase 2 and net serialization/prediction from Phase 3.
- **Godot headless smoke test:** import, load each scene, run N ticks, fail on any error.
- **`tools/Pb.Bench`:** a headless benchmark of 1k/2k/5k live balls, reported in ms per tick. It's reported in CI but doesn't gate.
- **GitHub Actions** on every push and PR: build, tests, smoke test, with the engine version pinned.
- **What needs you:** GPU frame rate and game feel, because the cloud container has no GPU. Every phase ships a perf overlay and a stress scene so checking takes about a minute.

## 10. Performance budget (GTX 1070-class, 1080p, 16.6 ms frame)

| Item | Target |
|---|---|
| Sim tick, 1,000 live balls + 10 players | ≤ 0.5 ms per tick (2 ticks per frame at 60 fps) |
| Ball rendering | 1 draw call (MultiMesh), one buffer upload per frame |
| Splats | ≤ 2,000 decals (cap in data; lowered if the stress test says so) |
| Scene rendering (greybox field, 10 players) | ≤ 10 ms |
| UI, audio, AI, net combined | ≤ 3 ms |

The RTX 3080 target is 144 fps. Install size target is under 10 GB, which is easy with low-poly art.

## 11. Folder structure

```
Pb/
├─ Pb.sln
├─ README.md
├─ CLAUDE.md                     conventions for AI agents
├─ docs/
│  ├─ spec.md                    the build prompt (source of truth)
│  ├─ architecture.md            this file
│  ├─ phase-1.md                 current phase task list
│  └─ reports/                   end-of-phase reports
├─ game/                         Godot project: open game/project.godot
│  ├─ project.godot
│  ├─ Pb.csproj                  assembly "Pb", namespaces Pb.Game.*; references src/Pb.Sim (+ Pb.Net in P3)
│  ├─ data/                      every tunable (.jsonc)
│  │  ├─ projectiles/ markers/ air/ loaders/
│  │  ├─ break_model.jsonc movement.jsonc stress.jsonc debug.jsonc
│  │  ├─ bunkers.jsonc fields/ ranges/
│  │  └─ modes/ bots/ gear/ brands/ progression/    (later phases)
│  ├─ core/                      RangeMain (composition root), SimDriver, data/settings/input, autopilot, smoke test
│  ├─ player/                    controller, camera, input → InputCommand, viewmodel
│  ├─ ballistics/                ball renderer + shader, splats, impact FX, arc debug
│  ├─ world/                     FieldBuilder, procedural bunkers, range, targets
│  ├─ ui/                        HUD, perf overlay, menus, settings
│  ├─ audio/                     AudioDirector, bus layout
│  ├─ ai/                        (P2) bot controller, perception, navigation
│  ├─ net/                       (P3) ENet transport, lobby, dedicated entry
│  ├─ platform/                  (P3) IPlatformServices implementations
│  ├─ scenes/                    Main, Range, StressTest, Field, Locker, …
│  └─ assets/                    models, textures, audio (Git LFS)
├─ src/
│  ├─ Pb.Sim/                    engine-free simulation core
│  │  └─ Core/ Data/ Ballistics/ Collision/ Gear/ Players/ Range/ Events/  (+ Match/ AI/ later)
│  └─ Pb.Net/                    (P3) protocol, snapshots, prediction, transport API
├─ tests/
│  ├─ Pb.Sim.Tests/              xUnit
│  └─ Pb.Net.Tests/              (P3)
├─ tools/
│  ├─ Pb.Bench/                  ballistics report + headless ball-count benchmark
│  └─ ci/                        Godot install, import and smoke-test scripts
└─ .github/workflows/ci.yml
```

The Godot project sits in `game/` rather than at the repo root for two reasons: Godot's C# project would otherwise compile `src/` and `tests/` a second time, and the editor would scan `bin/` and `obj/` folders.

## 12. Risks

| Risk | Mitigation |
|---|---|
| 2,000 decals too heavy on 1070-class GPUs | Measure in the Phase 1 stress scene. Lower the default cap, or switch to a splat-texture shader in Phase 5. |
| C#↔engine interop cost in hot paths | Hot loops stay in `Pb.Sim`. Engine calls are batched (MultiMesh buffer upload, pooled decals). |
| Replaying `CharacterBody3D` moves for prediction | Movement is stepped only from commands. Fallback is an analytic kinematic sweep. |
| Balls unreadable at distance | Minimum-pixel-size shader and streaks, both tunable. |
| Bot quality in a tactical game | Data-driven difficulty, debug overlays, headless bot-vs-bot regression matches. |
| Client-predictable spread | Accepted for now; server-only seeds later if needed. |
| Scope: six phases | Each phase lands as small milestones, each with a playable build and a report. |

## 13. Roadmap: what each phase adds to this architecture

> Superseded by the roadmap in [§14.8](#148-revised-roadmap) (approved 2026-10-01).

| Phase | Adds |
|---|---|
| 1 Ballistics sandbox | `Pb.Sim` ballistics, collision, gear, data layer, tests. Range scene, ball rendering, splats, basic controller, perf tools. |
| 2 Offline vertical slice | Hitbox rig, lean/slide/shoulder-swap, bunker catalogue + field loader with mirroring, match rules, bots, HUD, spectator. |
| 3 Multiplayer | `Pb.Net`, dedicated and listen server, lobby, lag compensation, server validation, `IPlatformServices`. |
| 4 Modes and maps | CTF/Arcade/co-op rule classes, second layout, scenario map (engine-collision adapter, terrain, props). |
| 5 Customisation and art | Gear locker, fictional brands, gear models, splat shaders, audio pass, full settings and rebinding UI. |
| 6 Progression (optional) | XP/levels, seasonal track, soft and premium currency (premium name is yours to choose), mock store behind `IStoreService`. |

## 14. Direction change (2026-09-30): the compound

> **Status: approved 2026-10-01 with the [Phase 2 plan](phase-2.md) (all defaults accepted).** The owner's direction, verbatim: "…use Higgsfield to design the graphics and make it more realistic. I want the setting to be like old abandoned buildings in a compound. We can go around exploring and then you fight different people. You may have up to 10 people in one level, in one round, and then you have different levels depending on the difficulty."

### 14.1 What changes, what stays

- **Stays:**
  - the principles (§1);
  - everything built in Phase 1: ballistics, collision, gear, data layer, tests, CI;
  - the data conventions (§8);
  - the netcode design (§6), now Phase 4.
- **Changes:**
  - The main game becomes solo elimination in explorable compound levels, which grows out of the spec's scenario map (§2.2).
  - Speedball (§4.8) and its breakout/hang bot behaviours (§7) become an optional Phase 5 mode.
  - The roadmap (§13) is replaced by §14.8.

### 14.2 Levels from a kit

- **Files.** `levels/*.jsonc` places buildings, props, pickups, spawns, patrol routes and named areas. `kit/*.jsonc` defines building templates, prop types and the material library.
- **Buildings.** A building is:
  - floors (height, slab thickness);
  - wall runs (a polyline in plan, thickness, material, door and window openings);
  - floor holes, stairs, roof (flat or pitched, with holes) and columns.
- **`Pb.Sim/Level` turns a building into primitives the collision world already supports:**
  - a wall with openings becomes boxes (piers, lintels, sills);
  - a slab becomes boxes around its holes;
  - stairs become stepped boxes for paint plus one ramp for walking;
  - a pitched roof becomes rotated boxes.
  
  No mesh colliders are needed, so paint collision stays analytic, allocation-free and testable.
- **One primitive list feeds everything:**
  - render meshes (merged per material per building, with world-scale UVs);
  - `StaticBody3D` shapes for walking;
  - the sim `CollisionWorld` for paint;
  - `BoxOccluder3D` occluders;
  - navigation-mesh source geometry.
- **Props.** Each prop is a GLB model plus a list of proxy colliders in data (an oil drum is a cylinder of 0.58 × 0.88 m; a car wreck is three boxes). Proxies are fitted to the model's measured bounds at import.
- **Cover points** are generated from the same data at load: wall ends, opening edges and prop sides, tagged with peek side and cover height (standing or crouched).
- **Named areas** (boxes in the level file) carry a callout name, an indoor flag and a light level, which bots use for callouts, searching and sight.
- **Broadphase.** The XZ grid stays as it is. Multi-storey columns simply hold more candidates. If the benchmark shows a cost, the grid gains Y bands.
- **The Phase 1 range** stays as the training level, with its own loader unchanged.

### 14.3 Rendering for realism

- **Material library.** Each entry gives albedo, normal and roughness textures, tile size in metres, a surface ID (for paint physics now and footsteps later), and a weathering amount.
- **`weathered.gdshader`** is one shader for all kit surfaces: base PBR plus grime by height above the floor, noise-driven stains and moss, and per-material strength.
- **Presets in `graphics.jsonc`:**
  - **Low:** no GI, no SSAO.
  - **Medium:** SSAO, per-room ambient probes and unshadowed fill lights at windows.
  - **High:** adds SSIL, SDFGI, volumetric fog and SSR.
  
  Shadow sizes and every toggle are data.
- **Culling:**
  - occluders generated from walls and slabs;
  - `visibility_range` on small props and weeds;
  - automatic mesh LODs from GLB import;
  - MultiMesh for weeds and rubble.
- **Budget** (GTX 1070, 1080p, Medium): scene ≤ 10 ms, 10 characters ≤ 1.5 ms, sim ≤ 1 ms, AI ≤ 1 ms.

### 14.4 Characters

- **Source.** GLB models from Higgsfield: Meshy image-to-3D with humanoid auto-rig and PBR, about 25k triangles.
- **Import.** Characters are imported against Godot's humanoid skeleton profile, so animation clips retarget between characters.
- **Animation.** An `AnimationTree` blends locomotion by speed and stance. The upper body is posed procedurally:
  - hands on the marker grips by IK;
  - spine turned to the aim pitch;
  - lean as a spine bend that matches the hitbox rig.
- **Attachments.** The marker, armband and paint splats attach to bones (`BoneAttachment3D`), so splats move with the character.
- **Hitboxes** come from the sim's `HitboxRig`, never from the mesh, so animation can't change outcomes.

### 14.5 Match rules

- **`Pb.Sim/Match`** holds `MatchState` (Briefing → Live → Ended(outcome), with a settle window for balls still in the air) and `IMatchMode`. The summary screen is presentation.
- **Phase 2 ships `SoloMode`** (`rules.jsonc`: `"mode": "solo"`): you against every opponent; the level is cleared when every opponent is out. Players already carry a team, so free-for-all and squads are mode variants.
- **Pickups** (`PickupSet`) are part of the sim, so bots and remote players take them by the same rules.
- **Level flow and difficulty live in `levels/ladder.jsonc`:** order, tiers, roster per tier, time limit, starting gear and pickups.

### 14.6 AI

- **Brain.** The state machine, target selection, aim and difficulty are plain C# in `Pb.Sim/AI` (`BotBrain`). A brain emits `InputCommand`s, exactly like a human, and its body is the same `PawnBody` the player uses.
- **Squad.** `BotSquad` holds what the bots share: the navigation grid, the cover points (with claims, so two bots don't take one spot), the last step's events, and a budget of path searches per tick. The host feeds it each step's events after stepping.
- **Navigation (as built).** `NavGrid` is built in `Pb.Sim` at level load from the walkable primitives (plus stair steps): columns of 0.25 m, a "span" wherever a bot can stand with headroom and clear of walls by the agent radius, joined to neighbours within a step. A* (weighted) finds paths, which are then straightened. It replaced the planned Godot `NavigationRegion3D`: an engine-free grid is deterministic and lets CI check paths on the real level and run whole fights in sim tests. `IBotNavigation` is the interface the brain uses.
- **Cover.** `CoverSet` generates points behind cover-flagged primitives (wall ends, door and window frames, props), full height if they hide a standing head and half if they hide a crouched one, each with the edge to peek round.
- **Sight.** Rays run from the eyes to the head, chest and hips of each enemy. A detection meter fills according to distance, the area's light level, the target's stance and speed, where in the view they are, and difficulty.
- **Hearing.** Shots, breaks and footsteps are heard within data ranges, halved when a wall is in the way.
- **Data.** Behaviours (Sentry, Patroller, Rusher; later Marksman, Flanker) live in `bots/archetypes.jsonc`, tiers in `bots/difficulty.jsonc`, senses in `bots/senses.jsonc`, shared timings in `bots/brain.jsonc` and the grid in `bots/navigation.jsonc`.
- **`game/ai`.** `BotPilot` drives an `OpponentPawn` with a brain, and `BotDebugOverlay` draws what bots think (F3).
- **Determinism and tests.** Brains draw randomness from per-bot PCG32 streams seeded from the match seed. Headless tests move bodies over the grid (`NavGridMover`) instead of Godot's collide-and-slide, so a whole round with bots runs in a unit test.

### 14.7 Asset pipeline

- **Flow.** A finished Higgsfield job becomes a project asset in three steps:
  1. `tools/art` downloads the files by job ID.
  2. A headless Godot script uses the `Image` API to make textures tileable, derive normal maps from height, derive roughness, and resize to at most 2K.
  3. Outputs are written to `game/assets/…`, and `game/data/assets.jsonc` records each asset's file, source job ID, prompt and date.
- **Storage.** Binaries go in Git LFS, using the patterns already in `.gitattributes`. CI checks out without LFS, to spare the bandwidth quota, and so it exercises the greybox fallback every run.
- **Original IP.** Prompts never name real brands, products, fields or games. Generated images are checked for logos and legible text before use.

### 14.8 Revised roadmap

| Phase | Adds |
|---|---|
| 2 The compound | Level kit and loader, Level 1, realism pass (materials, lighting, props, characters), hitbox rig, lean/slide/shoulder-swap/jump, `EliminationRules`, bots (Sentry, Patroller, Rusher; three tiers), menus, match HUD |
| 3 Level ladder | 3–4 more levels, unlocks and local save, Marksman and Flanker, objectives, audio pass with voiced callouts, doors, full settings and rebinding |
| 4 Multiplayer | `Pb.Net`, dedicated and listen server, lobby, lag compensation, co-op vs bots and PvP for up to 10 players |
| 5 Locker and extras | Gear locker, fictional brands, gear models, splat shaders; speedball field, CTF and Arcade as optional modes |
| 6 Progression (optional) | As before |
