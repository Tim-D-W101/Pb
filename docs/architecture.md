# Architecture plan

> **Status: approved (defaults accepted 2026-09-30); Phases 1–4 built (Phases 3 and 4 but for the owner's checks).** On 2026-09-30 the owner changed direction to explorable compound levels; [§14](#14-direction-change-2026-09-30-the-compound) and [phase-2.md](phase-2.md) were approved on 2026-10-01, and [§15](#15-phase-3-open-areas) with [phase-3.md](phase-3.md) on 2026-10-05 (revised 2026-10-06: open areas, each with places). [§16](#16-phase-4-multiplayer) with [phase-4.md](phase-4.md) (multiplayer) on 2026-10-09 (defaults taken). [§17](#17-phase-5-locker-and-extras) with [phase-5.md](phase-5.md) (the locker, brands, paint, the speedball field, capture the flag and arcade) on 2026-10-10 (defaults taken). Requirements are in [spec.md](spec.md); this file explains how they're met. Phase results: [reports/](reports/).

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
- **Viewmodel and FOV.** The viewmodel (marker, loader, tank) is drawn with its own FOV so it never clips into the bunker you're hugging. The FOV setting means horizontal degrees at 16:9. Godot's camera FOV is vertical by default, so it's converted, and wider screens get more horizontal view. The support hand is a separate part turning about the top of the foregrip, and a second left hand holding a pod (posed for pouring, its arm running back towards the camera) turns about its grip; `PlayerController` hands the viewmodel the sim's refill progress, and the viewmodel keys the cant, both hands and five falling balls off it (the phase runs on to the end when a refill stops early).
- **Splats.**
  - Pooled `Decal`s, capped at 2,000, oldest fading first.
  - Player splats clear at round end.
  - A mask-spray overlay appears when a ball breaks within 0.5 m of a face (Phase 2).
  - If decals prove expensive, Phase 5 moves to splat shaders.
- **HUD.** The HUD reads a presentation model (loader/pods, air in bar, timer, alive icons…) that is updated from events, never from sim internals.
- **Audio.** `AudioDirector` maps events and surface IDs to pooled `AudioStreamPlayer3D`s. Buses are Master, SFX, UI and Ambience.

## 6. Networking (Phase 3; the earlier phases already respect these constraints)

> Networking is Phase 4 since the roadmap's revision ([§14.8](#148-revised-roadmap)); its detailed design is
> [§16](#16-phase-4-multiplayer), which builds on what follows.

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
  - **As built (2026-10-02):** a prop draws its generated `model` if it loads, else a `shape` the game builds in code from its colliders, else the colliders as greybox.
    - `PropShapes` has 21 recipes (pallets, crate, tyres, drum, sandbags, barrier, car wreck, water tower, racking, forklift and so on).
    - They're built with `ShapeMesh`: boxes, lathed solids, extruded outlines (ear-clipped, so a car's side can have wheel arches), rounded bags, rods and rings.
    - They follow the kit shader's mesh contract (UV in metres, UV2 for the weathering ramp) and are merged per 24 m chunk and material.
    - Shapes are presentation only: the colliders stay the gameplay shape, and the sim drops a prop's greybox flag when it has either a model or a shape.
- **Cover points** are generated from the same data at load: wall ends, opening edges and prop sides, tagged with peek side and cover height (standing or crouched).
- **Named areas** (boxes in the level file) carry a callout name, an indoor flag and a light level, which bots use for callouts, searching and sight, and optionally a tone (what the place sounds like inside: presentation only, for the ambience).
- **Broadphase.** The XZ grid stays as it is. Multi-storey columns simply hold more candidates. If the benchmark shows a cost, the grid gains Y bands.
- **The Phase 1 range** stays as the training level, with its own loader unchanged.

### 14.3 Rendering for realism

- **Material library.** Each entry gives albedo, normal and roughness textures, tile size in metres, a surface ID (for paint physics now and footsteps later), and a weathering amount.
- **`weathered.gdshader`** is one shader for all kit surfaces: base PBR plus grime by height above the floor, noise-driven stains and moss, and per-material strength.
  - **As built:** textures are laid on in metres (`tile_m` per repeat), the right way up on walls (the kit's UVs run up a wall while a picture's rows run down it, so the shader flips v and the normal map's green). A material without a regular pattern can set `breakUpRepeat`: the shader then takes two looks at each texture, moved by an offset that changes in patches a few metres across, and blends them where the patches meet (after Inigo Quilez's "texture repetition"), so a 3 m photo doesn't show as a grid over a yard. It costs three more texture reads on those materials (3–6 % of a frame on ground-heavy views under software rendering, less on a GPU).
- **Presets** (`presentation.jsonc` → `graphics`; as built, the presets live there rather than in a `graphics.jsonc`). After the owner's first play-test lagged on anything above Low, the presets were rebalanced by measuring each effect's cost over the level's viewpoints (`-- --shots --preset=NAME` prints per-view frame times). Ambient occlusion turned out to be the biggest single cost:
  - **Low:** no GI, no SSAO, no glow, a third of the weeds to 28 m, no sunbeams, nothing lying on the ground, no old paint.
  - **Medium** (about 1.2× Low): glow, half the weeds to 35 m, shadows to 75 m; no SSAO, sunbeams or fill lights.
  - **High** (about 2.3× Low, the old Medium): SSAO, per-room ambient probes, unshadowed fill lights at windows, sunbeams with dust, three quarters of the weeds to 45 m.
  - **Ultra** (the old High): adds SSIL, SDFGI, volumetric fog and SSR; all the weeds to 70 m; softer fake beams beside the fog's real ones; no fill lights, since SDFGI bounces light.
  - **Render scale** (a setting, any preset): the 3D view is drawn at 50–100 % of the screen's resolution and upscaled with FSR 1.0.
  
  Shadow sizes and every toggle are data. Antialiasing is screen-space (FXAA on Low and Medium, SMAA on High and Ultra): with MSAA on, the depth buffer isn't available to shaders, and the sunbeams need it.
- **Apertures** (as built in M2.6). The level kit records every window, door, gap and roof hole as a rectangle in world space (`LevelLayout.Apertures`). At load the game classifies each one by probing the paint geometry: open sky on one side and a roof on the other makes it an exterior opening, and clear lines to the sun across it make it sunlit.
- **Sunbeams** (`LightShafts`, `light_shaft.gdshader`). Each sunlit opening into a roofed space gets a box skewed along the light, long enough to reach whatever the light lands on. The shader intersects each pixel's view ray with the box, cuts it off at the depth buffer and at the building's bounds (so a beam never shows outside), and ray-marches soft edges, a fade with distance and drifting dust swirls. GPU particles drift inside each beam as dust motes. Exterior openings also get an unshadowed spotlight angled in and down (neighbours on a wall share one), and a warm bounce light sits where each beam lands.
- **Weeds** (`WeedField`, `weeds.gdshader`; the place's ground comes through `WeedField.IWeedGround`, a level's from its survey and primitives, the training ground's from its lane and dressing). Crossed cards cut from a procedurally painted four-variant atlas, about 26,000 of them in Oxbarrow Works, placed at load from a seed of the level id. They grow by ground material (thick on scrubland, in a crack network on asphalt and concrete), along the foot of walls and props, and only where a small sphere dropped from the sky lands on the ground, so nothing grows indoors except under a hole in the roof. One MultiMesh per 16 m square for culling; they sway in rolling gusts and shrink into the ground with distance.
- **Window and door frames** (`OpeningFrames`, as built). A building template names its frame material (`"frames"`); every window and door aperture that building owns gets a frame (windows divided into panes of about 55 × 65 cm, jagged glass shards left in about half of them), drawn in the opening's plane. The perimeter wall's breaks are owned by `wall#n`, not a building, so they stay bare.
- **Building details** (`BuildingDetails`, as built). A building template can name a material for `"gutters"`: half-round gutters along the roof's long eaves, with a downpipe from each end (a swan neck back to the wall, collars bracketed to it every 1.8 m, a shoe at the foot), moved along the wall past any door or window. A parapet roof gets rainwater heads on the parapet and downpipes instead. `"fittings"` (a material) puts a dead lamp on a bracket over each door and loading bay and, along the outside walls clear of the openings, junction boxes with conduits, louvred vents and pipes turning down into the ground. `"ceilingLights"` (a material) hangs fluorescent fittings under every storey's ceiling, a row down each strip between walls running the long way, where the slab above or the roof is whole; most fixed, some hanging from one end, some gone. `"trusses"` (material, spacing, depth, and pendant lamps per truss) puts steel Warren trusses across a big roof's short span with purlins along it; over a hole in the roof each truss is broken off, and what's left near the edge hangs down into it at a random angle. The level records where each building stands (`LevelLayout.Buildings`), so the game builds these from the templates. They're presentation only, merged into the props' meshes.
- **Things on the ground** (`GroundDetail`, as built). `StainPainter` paints a 4 × 4 atlas at load (oil, puddle, damp, rust, tyre tracks, leaves, litter, chips; two variants each) plus a roughness/wetness map. `presentation.jsonc` → `groundDetail` lists, per kind, how many, their size, the ground materials they lie on, open sky or covered, the props they gather round and the share along walls. They're drawn as flat cards in one MultiMesh with `ground_detail.gdshader`. `GroundSurvey` (material and surface height at a point, and the sphere drop for open sky) is shared with the weeds.
- **Wall dressing** (`WallDressing`, as built). A level's free-standing wall run can carry a `"dressing"`: piers at a spacing (at the corners and either side of each break too), a coping course, barbed wire on brackets leaning away from the middle of a closed run (loose ends hanging at the breaks), broken blocks either side of the breaks, and a gate leaf lying outside each gap listed in `fallenGates` (the sim checks each index names a gap). It reports where its piers stand (`LevelBuilder.Piers`) for things painted on the wall to keep clear of, and where the wire's strands run (`LevelBuilder.Strands`) for the bags caught on them. The level records its wall runs (`LevelLayout.Walls`), so the game builds these from their definitions; presentation only, merged into the props' meshes.
- **Scenery** (`Scenery`, as built). A level's `"scenery"` lists lines of power pylons and of telegraph poles out beyond its bounds, each along plan points with a spacing, height and materials; the game builds steel lattice pylons (with crossarms, insulators and an earth-wire peak) or wooden poles, and the wires sagging between them. The level passes the definition through (`LevelLayout.Scenery`); the sim only checks its materials.
- **Creepers** (`Creepers`, as built). Ivy cards (`CreeperPainter`, four variants) stand on the ground against the faces of ground-level wall boxes of the listed materials, under the open sky; sweeps at six points over each card check there's wall behind it all the way. Alpha-cut, one MultiMesh (`creepers.gdshader`), seeded by the level id.
- **Menu backdrop** (`MenuBackdrop`, as built). The main menu builds a level's looks (no sim) behind its panels, a piece a frame after the menu shows, at the saved preset, with a camera drifting between two points (`presentation.jsonc` → `menuBackdrop`), then fades it in under a shade.
- **Old paint** (`OldPaint`, as built). Faded splats from past games where people would have shot from cover: at load, shots are cast from the bots' cover points (`CoverSet`) at others facing them across their cover, at random heights round where the other would peek, and where one lands on a wall, a column, the ground or a prop listed in `presentation.jsonc` → `oldPaint.props` (each with how far its shape sits inside its colliders), a card is laid on the surface if all four of its corners land on the same plane. `SplatPainter` paints the 4 × 4 atlas (eight shapes with drips for walls, eight without for the ground); the cards are one MultiMesh with `old_paint.gdshader`, coloured per card. Seeded by the level id.
- **Material tint.** A material can multiply its photo by a colour (`"tint"`), so one photo serves several materials (the manhole covers' cast iron from the rusted-steel photo). `MaterialLibrary` makes each material the first time it's asked for, and can recolour one (`Recoloured`: the procedural base colour, or the photo's tint), which the training ground uses for each bunker's colour.
- **Markings** (`Markings`, `MarkingPainter`, as built). A building template's `"markings"` (in its plan) or a level's (in the world) list worn painted lines, hatched areas, stencilled digits on a floor or wall, and column stripes. The sim validates and passes them through; the game lays cards from a painted atlas (bands, stripes, the digits, A–Z and a dash in a stencil hand; luminance and alpha only), one MultiMesh with the old paint's shader, faded with distance (`presentation.jsonc` → `markings`).
- **Horizon** (`Horizon`, as built): the tree-line rings and a few far landmarks (`horizon.landmarks`: chimneys, sawtooth sheds, a gasholder's frame, flats, by compass bearing and distance), flat-lit silhouettes in one mesh round any place's middle.
- **Skirting** (`Skirting`, as built): a building template's `"skirting"` material puts a board along the foot of each face of its upright wall primitives that stands on a floor and looks into an indoor area, merged into the props' meshes.
- **Cobwebs** (`Cobwebs`, `cobwebs.gdshader`, as built): cards anchored in the corners of the level's door and window apertures (not the free-standing walls' breaks), from a four-web atlas painted at load; two-sided, faded with distance.
- **Wall hangings** (`WallHangings`, `HangingPainter`, `wall_hangings.gdshader`, as built): one card at most per side of each full-height wall piece of a building with a `skirting`, where that side faces an indoor area and stands on a floor; each card reads its own rectangle of the atlas from its custom data (painted in the item's proportions), alpha-cut, so the splat decals land on them.
- **Contact shadows** (`ContactShadows`, `contact_shadows.gdshader`, as built): a flat card round the footprint of each upright wall, column and prop primitive whose foot is on the ground or a floor's top (collinear wall pieces merged into runs), multiplying the floor by a rounded-rectangle falloff; shown only on presets without SSAO.
- **Run-off streaks** (`RunOff`, `run_off.gdshader`, as built): cards on outdoor faces under each window aperture's sill (the face found by a ray from outside) and under the `Drip` sources that `BuildingDetails` and `WallDressing` report while they build (a point with the face's normal, or a line along a gutter or coping); each is cut short at the ground and above any aperture below it in the same face, and coloured by the kit material it runs off.
- **Floor debris** (`FloorDebris`, `DebrisPainter`, `floor_debris.gdshader`, as built): flat cards scattered per indoor area (by its floor area, a share along the foot of its walls; a ground-floor area's floor is the ground), each kept only if rays down from head height meet one flat floor under its middle and corners; glass goes under the window apertures on both sides. Tinted per kind, glossy for glass; shown with the preset's ground detail.
- **Cloud deck** (`CloudDeck`, `clouds.gdshader`, as built): a sphere that follows the camera inside the far clip (a whole one, its lower half discarded: Godot closes a hemisphere with a flat disc through its centre, and whenever the camera dipped after the dome had followed it, that disc lay just above the eye and drew the clouds in front of everything), unshaded and unfogged, sorted behind every other transparent thing; its shader intersects the view ray with a cloud plane and scrolls two seamless noise tiles with `TIME`, so only the background moves and the sky's radiance (from the procedural sky's still cover) is computed once. That cover is painted in code (`Atmosphere.HighLayer`, once per run, about 0.1 s): noise thresholded by the cloud cover and faded out between `clouds.highLayerFade_deg`, since a panorama's top rows squeeze into the point overhead. It wraps round by blending in a strip generated past its right-hand end, scaled to keep the noise's contrast (Godot's seamless noise averages two fields across the middle, which read as a hazy band). `Atmosphere.ApplyLighting` adds it, so every scene that applies the lighting has it.
- **Woods** (`Woods`, `leaves.gdshader`, as built): copses placed round a plan rectangle (the level's bounds, or the training ground's lane and firing point) beyond a clearance, kept off the place's scenery lines; trunks and branches are tapered lathes merged into one mesh with generated LODs, leaf clumps are crossed cards in one MultiMesh whose custom data carries the crown's outward normal (used as the lighting normal) and atlas cell. `LevelBuilder.Build` and `RangeBuilder` add them beside the horizon.
- **Yard fittings** (`YardFittings`, as built): manholes and drains placed with the `GroundSurvey` (listed ground material, flat, open sky) and a sphere sweep that keeps them clear of anything standing; drains against a wall skip the sweep and check their footprint instead. Built in one mesh with the kit's `cast_iron` and a dark material for the holes, after the level (it needs the paint collision), using `LevelBuilder.Materials`.
- **Cracks** (`Cracks`, `CrackNetwork`, `cracks.gdshader`, as built): the weeds' crack network (the edges of a jittered grid's Voronoi diagram, `weeds.crackSpacing_m`, seeded by the level id) is shared by `WeedField`, which grows tufts within reach of it, and `Cracks`, which cuts each cell from a box by its neighbours' bisectors to list every edge once, keeps the stretches over the weeds' `crackMaterials` (a stretch ends where the ground or a slab's height changes, tapering), and draws them as wandering ribbons with spurs and a few patches of a finer network (alligator cracking). The ribbons are multiplied over the ground (and the paint on it, drawn after it), in squares of ground that cull with distance; the shader draws a jagged, antialiased line with round ends, so cracks meeting at a corner join.
- **Worn paths** (`WornPaths`, `worn_paths.gdshader`, as built): nodes either side of every ground-level door and gap aperture (gaps up to 1.2 m high too) and at the middle of each square of `hubSpacing_m` holding three or more of the bots' cover points on soft ground; a spanning tree (Prim's) over them with edges no longer than `longest_m` whose straight line misses every standing footprint, plus one extra edge per hub; each edge wanders (pinned at its ends) and is kept only over the listed ground materials. `Plan` runs before the weeds and rasterises the trodden middles into a grid that `WeedField` consults (`KeepClear`); `Draw` lays ribbons that taper at their ends.
- **Graffiti** (`Graffiti`, `GraffitiPainter`, `graffiti.gdshader`, as built): a 4 × 4 atlas painted at load (throw-ups from a distance field to the stencil letters' strokes, `MarkingPainter.LetterStrokes`: shadow, outline, fill, shine and runs; tags as thin sheared strokes), cards on the outside faces of ground-standing wall boxes (outer faces of free-standing walls skipped), each on one face, clear of the piers `WallDressing` reports (`LevelBuilder.Piers`) and of the other pieces; one MultiMesh, render priority below the run-off.
- **Paint runs** (`PaintDrips`, as built): an `ISimEventListener` beside `SplatSystem`: a `BallBroke` on the world (no receiver) whose normal leans back less than `steepest` may start runs. Each is a `Decal` (the splats' fades and culling) on a basis whose Y is the face's normal and Z runs down the face; `_Process` grows its length with an ease-out over its run time, keeping its top fixed, then leaves it. Decals come from a pool capped at `cap`, the oldest reused first; the range clears them with its splats.
- **Footprints** (`Footprints`, `footprints.gdshader`, `paint_prints.gdshader`, as built): an `ISimEventListener` on `Footstep` (steps and landings) on the listed surfaces. It also remembers the last 96 `BallBroke`s on the ground (no receiver, normal up); a foot coming down within `paintReach_m` of a fresh one loads that player's boots with its colour for `paintSteps` prints, laid in a second ring-buffer MultiMesh on any ground (a mix blend, glossy, the tread's bars carrying more paint). Each print is a flat card in a ring-buffer MultiMesh, turned along the walker's velocity (or facing, standing still), stamped with its time on the node's clock; the shader mirrors the painted left sole for right feet, darkens the ground under it with a multiply (so the soil's grain shows through), and fades the print over the last 40% of `fade_s`. A third ring holds wet prints: a foot coming down in water (`PuddleRipples.InWater`, so the drips' pools count) wets that player's boots for `wetSteps` steps, which dry a step at a time on any ground but print only on the `wetOn` surfaces; `wet_prints.gdshader` multiplies the ground darker, shrinking the wet area towards the sole's deepest parts and the tread's bars as it dries.
- **Roof tatters** (`RoofTatters`, as built): strips along the four edges of every `RoofHole` aperture, hung from a few centimetres outside the edge, with the bags' shader (`snagged_bags.gdshader`) and a strip mesh that narrows to a torn end.
- **Ripples** (`PuddleRipples`, `ripples.gdshader`, as built): `GroundDetail` reports each puddle card's water (its middle, turn and half-sizes: half the card, where `StainPainter.Puddle` paints water). An `ISimEventListener` tests `Footstep`, and `BallBroke`/`BallBounced` on upward faces, against those ellipses; inside one, it stamps rings onto flat cards in a ring-buffer MultiMesh, each with its start time on the node's clock, sized to the event but no wider than twice the distance to the water's edge. The shader draws the spreading crest and trough and collapses finished cards.
- **Roof drips** (`RoofDrips`, `roof_drips.gdshader`, `drip_pools.gdshader`, as built): places along the four edges of every `RoofHole` aperture (about every `every_m`, `share` of them), each kept if a small sphere swept down from just inside the edge meets a floor facing up. Each gets a pool card (one MultiMesh) whose water it hands to `PuddleRipples.AddWater`, so steps and paint ripple it like a puddle. Each place lets go a drop every `interval_s`: a fixed set of `max` camera-facing streaks in one MultiMesh (a zero-scale basis hides an idle one), falling under the sim's gravity (`SimConfig.Projectile.Gravity`); a landing calls `PuddleRipples.Splash` for one ring.
- **Weeds pushed aside** (`WeedField.Follow`, `weeds.gdshader`, as built): each frame the weed field reads the sim's players and keeps two points trailing each one, easing after them (time constant a third of `pushLinger_s`), so a body leaves a short wake. The players near enough the camera for their weeds to be drawn go to the shader as up to 32 pushers (feet position, strength 1, 0.6 and 0.3), and each tuft within `pushReach_m` of one, on the same level, leans its upper part away (up to `pushLean_m` at the tip) and lowers it a little.
- **Blowing litter** (`BlowingLitter`, `blowing_litter.gdshader`, as built): pieces rest, move or are pinned. A rolling gust field (`0.5 + 0.3 sin(0.45 t − 0.06 · along the wind) + 0.2 sin(1.3 t + phase)`) lifts a piece above its own threshold; moving pieces ease towards their share of the ground wind, weaving, and each frame sweep a small sphere along their step against the sim's collision (a hit pins them) and check the step is still open ground (`GroundSurvey.OpenGround`; under a roof pins them). Pinned pieces are moved back onto open ground after `recycle_s`, only when neither spot is within `hidden_m` in front of the camera. One MultiMesh of flat cards; tumbling pieces are lifted clear of the ground by half their size times the sine of their tilt.
- **Dust underfoot** (`FootDust`, `foot_dust.gdshader`, as built): an `ISimEventListener` on `Footstep` events, and on `BallBroke` and `BallBounced` on upward-facing dusty surfaces. A step's effort is its hearing radius over the surface's loudness, placed between the walk's and the sprint's radius (crouched steps fall below and raise nothing); slides are followed each frame from the sim's players until they end. Puffs are a ring buffer of camera-facing quads in one MultiMesh, stamped with their birth time on the node's own clock (the shader's `now`), so the shader animates them and dead ones collapse to a point; none are raised beyond `reach_m` from the camera. The weeds, the bags and the dust share `groundWind_mps`.
- **Bags on the wire** (`SnaggedBags`, `snagged_bags.gdshader`, as built): `WallDressing` reports every strand between two brackets (`WireStrand`: its ends, how far it sags and the height of the wall top under it); each bag takes a strand of its own and a point along it, its length capped so that, held out at the lull's angle, it stays above the wall top. One shared crumpled mesh (gathered at the top, a notch between the handles) in one MultiMesh; the shader swings each bag about the point it's caught on, across the wind, between the lull's and the gust's angles, twists it and runs ripples down it.
- **Fallen roof** (`BuildingDetails.FallenRoof`, as built): for each of a roof's `holes_m`, corrugated sheets (an extruded corrugated section) and purlin bars on the highest floor below the roof at that point, each kept only if every corner lies on that floor and clear of the bounds of anything standing on it (walls, columns, props, stairs) and of the other sheets; its own random, so the building's other details don't move.
- **Damp** (`Damp`, `DampPainter`, `damp.gdshader`, as built): cards on the faces of upright wall boxes. A face that looks out under the sky and stands on the ground gets a splash-back band; one that looks into an indoor area and stands on a floor gets rising damp (on the ground floor) and mould in its top corners (walls as tall as a room); ceilings found by sweeps up from head height take water stains where all of one fits clear of the walls. The bands repeat a painted strip along the card (custom data: repeats and start), the rest pick a cell of a 2 × 2 atlas; three MultiMeshes under the old paint.
- **Chimney smoke** (`Horizon`, `smoke.gdshader`, as built): a landmark marked `"smoke"` gets a `CpuParticles3D` plume at its top (CPU particles, as a few dozen puffs cost nothing and their pre-roll is reliable): puffs launched along the wind plus the rise, turning, growing and fading by curves, drawn as camera-facing billboards in a shader that neither lights nor fogs them (their colour is the haze's view of them).
- **Birds** (`Birds`, as built). Flocks from `presentation.jsonc` → `birds` circle round points from a place's middle; the C# moves each bird along its circle every frame (banked, bobbing, a flapping state), and `birds.gdshader` beats the wings about their roots. One MultiMesh, unshaded silhouettes. On a level, perched birds sit on uncovered wall tops (a small 3D crow, a second MultiMesh) until the camera comes within `flushDistance_m`, then climb onto a circle of their own. It's also an `ISimEventListener`: a `ShotFired` within `shotStartle_m`, a `BallBroke` within `breakStartle_m` or a `Footstep` within `stepStartle` times its hearing radius puts up the sitting birds there.
- **Training ground** (`RangeBuilder`, `RangeShapes`, as built). The Phase 1 range is dressed with the same kit, under the same `lighting` and `horizon`, from `presentation.jsonc` → `trainingGround`:
  - kit materials for the lane, the firing point, nets, backstop, boards and shelter;
  - each inflatable bunker built over its `PropSpec` collider and the dummy built round each target kind's hitbox parts;
  - old paint cast from the firing line (`OldPaint` takes any shot source; each dummy carries its own, cast in its frame);
  - scenery as a level's.

  The nets, backstop, boards and shelter get walking collision only; the sim's range is unchanged.
- **Culling:**
  - occluders generated from walls and slabs;
  - `visibility_range` on small props and weed squares;
  - automatic mesh LODs from GLB import, and for the meshes built in code (props, frames, building details: indexed, merged per 24 m square and material, LODs generated at load);
  - MultiMesh for weeds and rubble.
- **Budget** (GTX 1070, 1080p, Medium): scene ≤ 10 ms, 10 characters ≤ 1.5 ms, sim ≤ 1 ms, AI ≤ 1 ms.

### 14.4 Characters

- **Source.** GLB models from Higgsfield: Meshy image-to-3D with humanoid auto-rig and PBR, about 25k triangles.
- **As built (M2.6).** The first opponent has 26k triangles, the generator's 24-bone biped rig and an idle clip.
  - **The generator's rig, not Godot's humanoid profile.** `CharacterPoser` binds the generator's bone names directly; a model with any other rig is drawn as its hitbox boxes, with a warning.
  - **Movement clips, shared by every model** (`presentation.jsonc` → `characters.clips`: walk, run and crouched walk, each a GLB holding one in-place clip). `MoveClipBaker` fits a clip to each model's build once, at load: every bone, matched by name, turns in skeleton space as the clip turns it from the clip rig's rest pose, applied to the model's own rest pose, and the hips' sway is scaled by leg length. So clips generated on one character play on the others. The baked clip's feet then give its ground speed (the median speed at which a planted foot moves back past the hips) and its phase (where the left foot is mid-stance), and the cycle is cut to one exact loop.
  - **No `AnimationTree`.** The idle clip loops, and `Gait` decides the rest from the ground covered each frame: clips play at the pace that keeps their feet planted, walk blends into run between the two clips' own speeds and into the crouched walk as the body crouches, all on one phase so the feet stay in step; the legs turn towards the travel (up to `maxLegYaw_deg`, the chest staying on the aim), and backing off plays the cycle backwards. Sliding and jumping hold the legs. `CharacterPoser`, a `SkeletonModifier3D`, applies it every frame over the idle, then poses the model to match the sim:
    - the hips drop for a crouch, less what the clips already lower them;
    - the spine rolls with the lean, a third per bone, as the hitboxes do;
    - the chest and head share the aim pitch;
    - the feet go where the clips put them (two-bone IK, keeping the clip's knee direction), so they stay on the ground however low the hips go; without clips they stay planted or step in a procedural cycle along the travel;
    - both hands go to grips on the marker (two-bone IK).

    `-- --gait-demo` has one opponent stand, walk, run, sprint, strafe, back off and walk crouched along a clear lane, seen from the side, to check it by eye.
  - **Gear and look.** Opponents hold the generated marker (`MarkerModel`, `presentation.jsonc` → `markerModel`), the same model as the first-person one: one GLB with its barrel along −X, turned to point down −Z and hung in the marker hitbox's frame with its muzzle at the box's front (`muzzleAbove_m` over its centre). The data gives points on the model as imported: the muzzle, the tops of the pistol grip and foregrip (where the first-person hands go), and the trigger and support wrists (where an opponent's hand IK goes). Without its art they hold the marker built in code (`MarkerShape`, also the first-person fallback), split into three groups, each fitted into its hitbox (`GearShapes`): receiver and barrel, the loader with the team's paint in it, the bottle. In first person the hands are built in code either way (`HandShape`, mirrored with the marker on the left shoulder), and the generated marker is drawn `viewScale` times its size with its own maps through `viewmodel.gdshader`. A team-colour armband sits on each upper arm. Each copy of a model gets the next tint in a list, so copies differ.
  - **Data.** `presentation.jsonc` → `characters` holds the models, tints, armband size, movement clips, blend time and leg turn, stride and lift for the steps without clips, pitch shares and grip positions.
  - **The clips themselves** come from the generator (an in-place walk, run and crouched walk made on the first opponent's rig). Without the art, the procedural steps stand in.
- **Attachments.** Armbands and paint splats attach to bones (`BoneAttachment3D`), so splats move with the character. Each armband is fitted once per model and arm (`CharacterModel.Sleeve`): the mesh's vertices skinned mostly to the upper arm, in a slice halfway along it, are put into the bone's frame with the skin's bind poses; outliers past 1.5 times the median distance are dropped, the reach in 16 directions round the bone is taken (85th percentile each), and a circle is fitted to those reaches (its offset from their first harmonic), plus `armbandGap_m`.
  - A splat sticks to the nearest bone of the part it hit.
  - The hit point lies on the hitbox, which the model doesn't fill and in places bulges past. So the splat's projection box reaches as far as the bone in both directions, and normal fade keeps the paint off the far side.
- **Hitboxes** come from the sim's `HitboxRig`, never from the mesh, so animation can't change outcomes.
- **Your own body** (`PlayerController.Body`): the local player gets a `CharacterVisual` of their own state, captured each tick like an opponent's. In first person it's `ShadowOnly`: every mesh casts but isn't drawn, the gear is hidden and the hands leave the marker for the clips' carry, since the viewmodel is lit where it really is and an invisible marker's shadow would fall across it. On elimination it's drawn whole for the spectator camera, and splats and flinches on the player go to it. No model (no art), no body.

### 14.5 Match rules

- **`Pb.Sim/Match`** holds `MatchState` (Briefing → Live → Ended(outcome), with a settle window for balls still in the air) and `IMatchMode`. The summary screen is presentation.
- **Modes (as built, M2.10).** `rules.jsonc` → `modes` lists what the menu offers: solo (you against a squad), free-for-all (everyone a team of their own) and teams (you and bot teammates against a bot team), each with the sizes on offer and, for the open modes, the behaviours bots are dealt. One rule decides every round, `LastTeamStandingMode`:
  - the round goes on while your team and another are both in, or, once yours is out, while two others still are;
  - `Cleared` means your team is the last one standing, `Eliminated` that another is, `Traded` that nobody is.

  `MatchState` records the tick each player went out, so free-for-all placings fall out of it, and only opponents count as eliminations (friendly fire still puts you out). The difficulty tier never sets the number of players.
- **Pickups** (`PickupSet`) are part of the sim, so bots and remote players take them by the same rules.
- **Random starts (as built).** `SimWorld` takes a match seed per round: the host deals a new one each round, and tests and scripted runs keep the data's. `SpawnPlanner` uses the seed to deal the starts:
  - your entry point;
  - opponents at a mix of the level's opponent spawns and cover points inside its spawn area, a fair distance from you, out of your sight and spread out;
  - in free-for-all, everyone further apart and out of each other's sight where the level allows;
  - in teams, your teammates near you and the other team grouped on the far side, out of sight of your whole team;
  - a random role for each (`rules.jsonc` → `spawning`, or the mode's `roles`).

  The same seed always deals the same starts, so a round can be replayed with `--seed`.
- **Areas and difficulty live in `levels/areas.jsonc`:** the areas in menu order (every one open), each with its tiers (bot difficulty, time limit, starting gear, pickups) and the roster scripted solo runs fill. Where in an area to play is its level file's `places` (§15.8). Until 2026-10-06 this was a ladder (`levels/ladder.jsonc`) that opened one level after another.

### 14.6 AI

- **Brain.** The state machine, target selection, aim and difficulty are plain C# in `Pb.Sim/AI` (`BotBrain`). A brain emits `InputCommand`s, exactly like a human, and its body is the same `PawnBody` the player uses.
- **Squad.** `BotSquad` holds what the bots share: the navigation grid, the cover points (with claims, so two bots don't take one spot), the last step's events, and a budget of path searches per tick. The host feeds it each step's events after stepping.
- **Navigation (as built).** `NavGrid` is built in `Pb.Sim` at level load from the walkable primitives (plus stair steps): columns of 0.25 m, a "span" wherever a bot can stand with headroom and clear of walls by the agent radius, joined to neighbours within a step. A* (weighted) finds paths, which are then straightened. Since M3.5 the search's estimate of the distance left also uses landmarks (`navigation.jsonc` → `landmarks`): walking distances from a few spans spread over the grid, worked out on load, so a search for a place upstairs heads for the stairs instead of searching the floor below; a goal in another connected piece of the grid fails without a search. It replaced the planned Godot `NavigationRegion3D`: an engine-free grid is deterministic and lets CI check paths on the real level and run whole fights in sim tests. `IBotNavigation` is the interface the brain uses.
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
- **As built (M2.6).** `tools/art/import.sh texture|model` downloads one file by its URL, then runs `game/tools/ArtImport.tscn` headless.
  - **Textures** are cropped, evened out and blended across their seams, then saved as JPEG at 1K with normal and roughness maps and their import settings, in `game/art/textures/`.
    - **Texture sheets.** A generation is a scarce thing (five a day), so one picture holds four materials in a 2 × 2 grid (or six in a 3 × 2 grid on a 3:2 picture), and each is cut out with `--region`.
    - **Prop sheets.** Likewise one product picture holds six props on white; each is cut out and uploaded, and lifted into 3D on its own (Tripo H3.1, detailed geometry with PBR maps).
    - **Regular patterns.** Blending a seam mixes in a copy of the picture shifted by half its size, which doubles the joints of bricks, planks or corrugations unless the shift is a whole number of repeats. So a patterned material is cut to whole repeats (measured by autocorrelation), and `--repeats=across,down` makes the shift the whole number of repeats nearest half, with a plain blend where the copies line up (the contrast-keeping blend would strengthen the joints there); `--stretch` squares a cut that isn't square. The importer's self-test checks a repeating pattern comes through unchanged.
  - **Models** are tidied (`GlbTidy`) and measured, in `game/art/models/`:
    - pictures shrunk to JPEG, and the generator's ids dropped from their names (Godot names the textures it extracts after them);
    - the baked-in glow and specular boost removed;
    - optionally scaled to a real height.
  - **Clips** (`clip`): a rigged character's GLB holding a movement clip is cut down to its rig and animation (`GlbTidy.ClipOnly`: no meshes, materials or pictures), so a clip costs tens of kilobytes, not megabytes.
  - **Provenance.** `assets.jsonc` records the job, generator, prompt, source URL, files and import settings. A sim test fails if any art the kit or `presentation.jsonc` names has no record.
  - **Fallbacks.** The game loads art through `ArtFiles`, and anything missing falls back: materials to the procedural look, props to their shapes built in code (then greybox), opponents to their hitbox boxes. `-- --no-art` ignores all the art, and CI's bot match runs that way.
- **Shipping (as built).** CI exports the Windows build on every run and publishes it as the rolling "test-build" release (`tools/package/windows-build.sh`):
  - **Art in packs of its own, one per asset.** The game's pack leaves `art/` out (`export_presets.cfg`), and `tools/package/art-packs.sh` exports each asset in `assets.jsonc` (a material's three maps, or a model with its pictures) into `art/Pb-art-<id>.pck`, which `ArtFiles` mounts the first time anything asks for art. A code or data change then costs under 1 MB, and new art costs just its own packs, not all 65 MB of it. (Until 2026-10-02 the art was one `Pb-art.pck`; the launcher removes it, and any pack the release no longer lists.)
    - **Mounting.** The pack is mounted without replacing files, so its own copies of the project settings and the UID list don't override the game's. `ArtFiles` then registers each art file's UID, read from its `.import` file, so models find their textures by UID.
    - **Reuse.** A fresh import doesn't produce byte-identical files (the textures come out the same, but the imported models don't), and CI imports from scratch on every run. So the manifest records a stamp for each pack (a hash of its files with their import settings, the project settings and the Godot version), and while a pack's stamp matches the published release, the build reuses the published pack byte for byte, after checking it against the published SHA-256. CI also deletes release assets that no build publishes any more.
  - **Per-file updates.** The release carries each game file under its own name, the whole game as `Pb-windows.zip`, and `manifest.txt`: version, date, an engine stamp, and each game file's SHA-256 and size. `Play.bat` runs `update.ps1`, which downloads the files whose hash differs from the local copy, checks each against its hash, then swaps them in; a new engine stamp means the whole game. CI uploads the manifest last, so a launcher never reads a manifest whose files aren't up yet. `Pb-update.zip` (all the game's own files) keeps launchers from before per-file updates working: it brings the new launcher with it.
  - **The runtime's JIT switches.** `game/Pb.csproj` turns off tiered PGO and quick JIT for loops, and the export writes both into `Pb.runtimeconfig.json`. With both on, the .NET 8 JIT on Windows x64 miscompiles big methods with loops whose local functions capture locals: a garbage collection in the middle of such a method loses track of the captured objects. That crashed the menu's backdrop (`Cracks.Build`) on the owner's laptop, and it never happens on Linux, so CI can't catch it. It was reproduced with a copy of the crack builder on the Windows runtime under Wine, collecting every 64 KB, and either switch stops it; the Windows build itself, run the same way (with `WINEDLLOVERRIDES=dinput8=d`, as Wine's DirectInput crashes Godot), failed 1 level load in 5 before the switches and none in 24 after. The switches cost the sim about 12% (0.34 ms a tick with ten players, against 0.31). `windows-build.sh` fails if an export loses the switches, and the game logs them at start (`.NET 8.0.31: TieredPGO=false, QuickJitForLoops=false`).
- **Storage.** Binaries go in Git LFS, using the patterns already in `.gitattributes`. CI checks out without LFS, to spare the bandwidth quota, and so it exercises the greybox fallback every run.
  - **As built:** the imported art (71 MB by 2026-10-05) is committed as plain files marked binary. The container has no Git LFS, and CI tests the fallback with `--no-art` instead.
- **Original IP.** Prompts never name real brands, products, fields or games. Generated images are checked for logos and legible text before use.

### 14.8 Revised roadmap

| Phase | Adds |
|---|---|
| 2 The compound | Level kit and loader, Level 1, realism pass (materials, lighting, props, characters), hitbox rig, lean/slide/shoulder-swap/jump, `EliminationRules`, bots (Sentry, Patroller, Rusher; three tiers), menus, match HUD |
| 3 Open areas | Three more areas (four in all), every one open, each with places to play in; local save and records; Marksman and Flanker; objectives; audio pass with voiced callouts; doors; full settings and rebinding; planted steps for the opponents |
| 4 Multiplayer | `Pb.Net`, dedicated and listen server, lobby, lag compensation, co-op vs bots and PvP for up to 10 players |
| 5 Locker and extras | Gear locker, fictional brands, gear models, splat shaders; speedball field, CTF and Arcade as optional modes |
| 6 Progression (optional) | As before |

## 15. Phase 3: open areas

> **Status: approved 2026-10-05 with the [Phase 3 plan](phase-3.md) (defaults taken) as the level ladder; revised
> 2026-10-06 at the owner's request: every area open, each with places to play in (§15.8), and the opponents' planted
> steps (§15.9).** This section is the technical design; each part gains "as built" notes as its milestone lands.

### 15.1 Profile and records

> **Revised 2026-10-06.** The unlocks are gone. `Pb.Sim/Match/RecordBook.cs` (was `Ladder.cs`) keeps your records per
> area, place, mode, objective and difficulty, and the last choices (the place among them); `ProfileData` version 2
> drops `opened`. A ladder save still loads: its records read as the whole area's (place `whole`), and `opened` is
> ignored. Settings → "Open every level", `--unlock-all` and the summary's "new level open" are gone. As first built:

- **The profile** (`game/core/Profile.cs`, `user://profile.json`) is presentation-side state, like the settings: the
  sim never reads it. It holds the levels opened, per level × mode × difficulty the rounds played and won with the best
  clear time, accuracy and eliminations, and the last menu choices.
- **The rule is data** (`levels/ladder.jsonc` → `unlock`) and engine-free (`Pb.Sim/Match/Ladder.cs`), so it's unit-tested:
  a level opens when a round on the level before it is won at the rule's lowest difficulty or above. The first level is
  always open; `-- --unlock-all` and the settings switch open them all without writing it into the profile.
- **As built (M3.1).** `LadderProgress` holds the rules and the records; `ProfileData` is the file (System.Text.Json,
  camelCase). A level is open if it's first, listed in `opened` (a win wrote it there), or the records show a win that
  counts on the level before it, so a profile from before a rule change keeps what it had. `LevelMain` adds each real
  round when it ends (not scripted runs or tours) and the summary names a level it opened. Level select is rebuilt each
  time it's shown, since what's open can change.

### 15.2 Doors

- **Data.** A door opening may carry a `leaf`: hinged (hinge at the opening's start or end, swinging left, right or
  both ways) or sliding, single or double, a material and a start state (shut, open, ajar, random by the match seed).
- **Sim.** `DoorSet` (`Pb.Sim/Level`) holds each leaf's pose and target, steps them every tick (`SimWorld.Step`, before
  the balls fly), and resolves `Interact` presses: the nearest leaf whose box the eye's reach ray meets, or whose
  doorway it crosses. A tap toggles; holding eases the leaf open. A leaf stops short of a player's capsule rather than
  pushing through. Opening and closing emit `DoorMoved` events (bots hear them like footsteps; the audio plays them).
- **Collision.** Each leaf is a `DoorShape` in the paint `CollisionWorld`: its grid bounds cover the whole swing, so
  the broadphase stays static and only nearby leaves are tested; its box follows the leaf's pose. Sight and aim use the
  same world, so shut doors block both. Splats on a leaf are parented to its node.
- **Navigation.** The grid is built with doorways open; a bot whose next path segment crosses a shut leaf's doorway
  stops, faces it and presses `Interact`, then goes on once it's open enough.
- **Game.** Each leaf is an `AnimatableBody3D` (walking) and a mesh built in code (`DoorShapes`), posed from the sim.
- **As built (M3.2).** `DoorSet` keeps each leaf's openness (0 shut to 1), target and rate; `Interact` starts a leaf
  moving at the ease rate on the press and, if the press is let go within the hold time, carries it on at full speed
  (a tap), else stops it where it's let go. A leaf steps only if it doesn't cut deeper into anyone's capsule. Swing
  doors open away from whoever pushes them; a pair opens together. The paint `CollisionWorld` gained dynamic colliders
  and `SkipDynamic`, which `LevelMain` sets while it builds what's made once from the level (cover, starts, weeds, old
  paint, light), so nothing sticks to a door that will move; light shafts and cobwebs leave hung doorways alone.
  `NavGridMover` (headless) stops at leaves as Godot's collision does in the game. Door meshes sit on render layer 5:
  world splats leave them out, door splats paint only them (`SplatAnchor.CullMask`), and `weathered.gdshader` draws
  their pattern in their own frame (`local_pattern`, `local_origin` instance uniforms), so it doesn't slide as they
  swing.

### 15.3 Marksman, Flanker and shared contacts

- **Shared contacts.** A bot's `Spotted` callout carries the target's position; teammates whose ears it reaches
  (`brain.jsonc` → `calloutRange_m`, muffled by walls) get it as a lead with a little error, through the same path as a
  heard shot.
- **Vantage points** (`Pb.Sim/AI/VantageSet.cs`): computed once per level from the cover points and spawns: rays at
  eye height round each candidate measure how far it sees and over what arc, plus a bonus for height.
- **Marksman**: idles at the best vantage within reach of its start (`idle: "overwatch"`), engages beyond its
  preferred range only with its aim settled, and relocates to another vantage after `relocateAfterShots` or when shot
  at. **Flanker**: on a teammate's contact or when it loses you, scores a few flank spots round your last known
  position by the share of the path to each that you could see, takes the least exposed, and approaches quietly.
- **As built (M3.3).** `BotSquad.Share` queues a `Contact` (who called, from where, about whom, where they are); it
  reaches teammates the next tick through `ContactsFor`, like sounds. `BotSenses` hears it within `calloutRange_m`
  (muffled through walls), and `Awareness.SinceContact` keeps `FromContact` true for `contactMemory_s`, so a sound or a
  glimpse that follows doesn't cancel the call. `VantageSet` scores cover points only (spawns aren't scored: a bot
  watches from cover), clips its rays at the level's bounds (open ground outside the play area isn't a view), and keeps
  each point's most open direction (the middle of the best three neighbouring rays), which overwatch sweeps round,
  never past the cover's own arc. The Flanker judges a route by the straight line to each candidate spot (six samples
  in the enemy's sight or not), a cheap stand-in for the path; the test checks the real path is still less exposed.
  The relocation distance and the flanking and "noticed" angles are `brain.jsonc` keys; the new archetype keys are
  optional, so older behaviours read as before.
- **Changed in M3.11.** At its spot a Flanker looks out from it (the cover's peek, for `flankLook_s`) before it
  searches, since the spot was picked for its view of where you were: a flank that worked now ends in a shot from the
  side rather than a walk towards you. The spot search is a public query, `BotBrain.FlankSpot(enemy, caller)`, which the
  role demo uses to find where to stand; asked from outside it leaves out the random tie-break, so it doesn't touch the
  brain's random numbers.

### 15.4 Objectives

- **Data.** A level's `objectives`: case spots (each with the area it's in), ways out (boxes, by default round the
  player spawns) and rooms to hold (named areas). `rules.jsonc` → `objectives` holds the timings.
- **Sim.** `ObjectiveState` (`Pb.Sim/Match`) is part of the round: where the case is or who carries it, the hold
  clock. Two `IMatchMode`s, `RetrieveMode` and `HoldMode`, decide the outcome, each also won by the last team standing.
  New events: `CaseTaken`, `CaseDropped`, `CaseExtracted`, `HoldChanged`.
- **Bots.** The squad knows the objective. Defenders start near it (`SpawnPlanner`), hold it while nothing's going on,
  and get an alarm (the carrier's position every few seconds, or the room being entered) that sends them after it.
  Attackers (your teammates) head for it, carry, escort and hold from cover.
- **As built (M3.4).** Case spots are points (the factory finds the biggest indoor area round each: its building or that
  storey, which the HUD marks); ways out are points with the rules' radius (default: the player spawns). The case spot
  or room is dealt from the match seed (`ObjectiveState.PickIndex`), and `ObjectiveFocus.For` works it out before
  anyone starts, so `SpawnPlanner` gathers the defenders round it (their guards play `objectives.guardRole`) and picks
  your entry clear of it (`spawning.objectiveClearance_m`). `ObjectiveState.Update` runs in `MatchState.Update` after the
  balls have flown, so a carrier hit this tick drops the case this tick; it sets `PlayerState.SprintBlocked`, which
  `MovementModel` honours. The time limit doesn't end a round whose objective was done on its last tick. Bots: the squad
  runs the alarms once a tick from its sync (`BotBrain.Alarm` gives a lead through `BotSenses.Alarm`, like a teammate's
  call; `BotBrain.Guard` gives an objective post that replaces post, patrol and hunt); attackers play the objective from
  `ActIdle` and go back to it after a fight, and a carrier or someone in the room (`OnTask`) drops everything short of
  an enemy in sight. Records gained an `objective` key (old records read as eliminate). `NavGrid.Blocked` lets path
  searches walk round moving things: the squad points it at `DoorSet.OpenLeafAt`, which says where a leaf standing
  at least `doors.botRouteRound` open is. Game: `ObjectiveViews` (world), `ObjectiveHud` (marker, status line, hold
  bar), `LevelMap.MarkObjective` (briefing), `ObjectiveDemo` (`-- --objective-demo` for screenshots).

### 15.5 New kit

- **Tracks**: a level's `tracks` (plan polylines) become rails (paint only; feet step over them) and sleepers drawn on
  the ground (presentation).
- **Props and buildings** follow the existing rules (§14.2): colliders are the gameplay shape, detail models built in
  code from them, generated models where they arrive.
- **As built (M3.5).** `LevelFactory` lays each track segment's two rails as `PrimitiveRole.Rail` boxes (paint only) at
  the track's gauge; `TrackViews` draws the rails and the sleepers under them. A prop collider can be `"paint": false`
  (it blocks walking, not paint): a wagon's underframe is a walking-only box with its wheelsets as paint-only
  cylinders, so you can't crawl under a wagon but you can shoot under it, between the wheels, at someone's legs. The
  wagons, shunter, buffer stops and stacks are recipes in `PropShapes.Railway.cs`. The Rail Yard
  (`levels/rail_yard.jsonc`) is built from six new buildings (engine shed with its gantry, goods shed with its platform
  and canopy, signal box, yard office, footbridge, lamp hut), five tracks (one outside the wall, for the scenery) and
  rakes of wagons. Its first search tests showed a gap in the navigation: a goal up on the gantry, reached by stairs at
  the far end of the shed, took up to 166,000 expanded spans (60 ms and more), and an unreachable one cost the whole
  search budget every time a bot asked. Landmarks and the connected-piece check (above) bring those to under a
  thousand, and the straightening now looks ahead in doubling strides from each corner instead of testing every span,
  which had made long straight paths cost milliseconds (the sim tests, with fourteen more of them, went from 54 s to
  40 s). `LadderLevelTests` holds every playable level to the same checks: the grid reaches every
  spawn, patrol, pickup, case spot, way out and room from every way in; every mode and objective deals starts at every
  size; and bot rounds in each mode and objective play out.
- **As built (M3.6).** Prop colliders can be tilted boxes that feet walk on (`rotation_deg` on a box): a trailer's
  loading ramp. The Cold Store's raised floor is a building slab 1.2 m thick (slabs take up to 2 m), its interior walls
  stand on it (`baseElevation_m` 1.2), and its outer wall is two runs: a concrete plinth to the floor and cladding
  above with the doors at floor level. Indoor areas' light (0.08 to 0.4 here) drives both the bots' sight and the
  interior ambient light (`LevelBuilder.BuildAmbientProbes`), so the chambers are dark to both. New props and their
  detail models (`PropShapes.ColdStore.cs`): `lorry_trailer`, `pallet_racking` and `pallet_racking_full`,
  `roll_cage`, `compressor`, `ammonia_tank`, `lorry_cab`; materials tinted from the existing photos until M3.10.
- **As built (M3.7).** The Hospital Wing's two three-storey wings come from a generator, `tools/levels/hospital_wings.py`,
  which writes their building files (stair flights side by side, one per storey, the slab holes over each and the
  railings round them, the doors and windows of every floor, the beds and curtains). A building with no walls is
  allowed (the ambulance canopy is columns and a roof); a wing whose end has fallen in is just shorter slabs and roof
  than walls, and open wall runs upstairs. A prop collider with `"walk": false` and paint on is a curtain: it stops
  balls and sight but nobody's feet. New props (`PropShapes.Hospital.cs`): `hospital_bed`, `curtain_screen`,
  `locker_bank`, `medical_trolley`, `wheelchair`, `bench`, `operating_table`, `ambulance_wreck`, `fountain`, `boiler`,
  `chimney`.

### 15.6 Audio

- **Buses**: Master → Effects, Voices, Ambience, Menus (volumes in settings). Effects and Voices pass through a reverb
  whose mix follows the listener's area (indoor, size).
- **Sound bank** (`game/audio/SoundBank.cs`): every effect synthesised at load from a recipe, several variations each,
  by surface and kind; the `AudioDirector` maps sim events to them with 3D players (air absorption by distance, a
  low-pass when the sim's collision says a wall is in the way).
- **Voices**: generated lines imported as art (`tools/art/import.sh voice`, provenance in `assets.jsonc`), cast per
  character model in `presentation.jsonc`; a callout plays the caller's voice from its position. Missing lines fall back
  to subtitles.

- **As built (M3.8).** `SoundBank` renders 87 recipes (225 variations) on worker threads when the first scene starts
  and keeps their samples for the whole run; each scene's `SoundSet` turns them into Godot streams as they're first played
  and frees them on exit. `AudioDirector` maps sim events to sounds (families of surfaces from `presentation.jsonc`
  "audio" → "surfaces"), keeps a per-frame budget, culls by each kind's range, and casts one ray through the sim's
  collision per 3D sound for occlusion. `Ambience` finds the smallest area round the listener each frame, crossfades its
  tone, eases the Outside bus's low-pass and the World reverb, follows the weeds' gust formula for the wind, and calls
  crows from `Birds.TryFlying` (and wings where `Birds.Flushed` says birds were put up). `RefereeCalls` queues the
  referee's lines so they don't talk over each other. Voice files are `art/voices/<voice>_<slug>.ogg` (named for their
  voice so `tools/package/art-packs.sh` packs them per voice); `tools/art/import.sh voice` cuts one take into its lines
  with `VoiceSplitter` (dynamic programming over the take's pauses against the lines' expected lengths; the silence
  kept either side of a line never reaches past the cuts) and encodes them with ffmpeg. A line the take says more in
  (twice, or with another line's words again) keeps its first or last part with `--keep-first`/`--keep-last`
  (`VoiceSplitter.KeepPart`, at the longest pause inside it), and `tools/art/voice-check.py` checks every cut line's
  words with a local speech recogniser. Under Godot's dummy driver (headless) nothing is started, so CI exercises
  everything but the mixer.

### 15.7 Settings and bindings

- **Bindings**: `input.jsonc` stays the defaults; the player's changes are overrides (per action: its keyboard/mouse
  and pad events) saved with the settings and applied over the defaults by `InputSetup`.
- **Settings menu** (`game/ui/SettingsMenu.cs`): tabs built from the shared `UiKit`, the same in the main and pause menus.
- **As built (M3.9).** Bindings are text (`key:W`, `mouse:Left`, `pad:A`, `axis:TriggerRight+`); `BindingSet` holds
  each action's two keyboard-and-mouse slots and pad slot, the defaults' shared bindings (allowed) and clashes, and
  saves only the differences. `GameSettings` is split: `GameSettings.cs` (the values, migration from older files and
  clamping, no engine) and `GameSettings.Godot.cs` (the file, the defaults from `presentation.jsonc`, the window). The
  test project compiles both engine-free files. Startup order in every scene: `InputSetup.Apply(inputDef)` (checks and
  applies the defaults, records the actions), `GameSettings.Load`, `InputSetup.Apply(settings.Bindings)`,
  `view.UseTeamColors(settings.TeamColors)`, then the rest. `GraphicsDef.Effective(preset, parts)` lays the settings'
  graphics parts over a preset for every `ApplyGraphics`.

### 15.8 Places to play (as built, M3.12)

- **Data.** Each level file lists its `places`: the whole level first, called `whole` (the records keep it by that
  name; validation insists), then parts, each a rectangle on the plan with optional ways in (`playerSpawns`), a walk-off
  spot (`deadZone_m`) and a `spawnScale`. `LevelFactory` checks each part's entries and walk-off spot are inside it.
- **`LevelLayout.ForPlace`** makes the level for a round in a part. It adds four `Boundary` boxes on the edge that block
  walking only (paint and sight pass over), and keeps what lies inside: opponent spawns, patrols (all their points),
  pickups, viewpoints, case spots, rooms to hold (every box of the room) and ways out (else the entries, "where you came
  in"). Doors and tracks stay. Random starts go to the level's spawn ground inside the place (a place that reaches past
  the perimeter for its way in doesn't start anyone out there), or the whole place if none of it is spawn ground.
  `CoverSet` keeps only cover inside; `SpawnRules.Scaled` shrinks the distances; the planner falls back to cover points
  when the spawns inside run short. A place offers the objectives its layout has room for (`LevelObjectives.Offers`).
- **`PlaceBoundary`** (presentation only) surveys the edge every 0.2 m against a walking-only collision world (the
  ground, walking primitives and stairs, not the boundary itself): a gap is a floor (ground, floor, stairs; never a roof
  or a container) clear to head height, so tape never crosses a window. Open stretches are strung with tape at 0.95 m on
  posts at corners, loose ends and every 2.6 m, tied off where a wall closes them; one mesh and one MultiMesh.
- **The menu** (`MainMenu`): the areas in a row; the chosen one's card has the places down the left and the round's
  choices on the right; your record is per place. **The briefing** frames its map on the place (`LevelMap`).

### 15.9 Planted steps and gear off the walls (as built, M3.13)

- **`StepGait`** (presentation only) drives the legs by default (`characters.legs` = `"steps"`): each foot is planted in
  the world until its next step. A gait table by speed (`characters.steps.gaits`: cadence, swing share, lift, landing
  point, bob) blends between rows; a swing moves on a minimum-jerk curve in the body's frame and lands on the ground under
  it (a drop onto the paint geometry: a stair tread, a kerb). Heel and toe roll about the ball of the foot and the heel;
  the hips drop at once (eased back up) as far as a leg needs to reach, and sit lower on bent knees running; a foot that
  can't reach behind is drawn along until it lifts. Standing, a shooter's stance (bladed, staggered); past
  `settleTwist_deg` the feet step round, one at a time. `CharacterPoser` takes the idle clip's hip turn back out and
  turns the spine to keep the chest on the aim.
- **The tuck** is in the sim: `SimWorld.UpdateTuck` sweeps the barrel line (`HitboxRig.MarkerLine`) and, when it's blocked,
  finds the least pitch up about the back of the marker that clears it (in steps, then refined), reached at
  `tuckRate_degps`, at once before the round goes live. `HitboxPose` records it, so lag compensation sees it too.
- **Stairs** get walking-only blocks under the ramp (`KitGeometry.Stairs`), so nobody walks into a flight.
- **Bots** stroll (`brain.jsonc` → `strollPace`) on patrol, back to a post and walking off; to an objective's duty post
  they walk.

### 15.10 More life in the opponents (as built, M3.14)

- **The head** is in the sim, so what you see is still what you can hit: `InputCommand.HeadYaw` → `PlayerState.HeadYaw`
  (clamped to `movement.jsonc` → `maxHeadTurn_deg`, 0 once out) → `HitboxPose.HeadYaw`, and `HitboxRig.Pose` turns the
  head box in place and swings the mask round it about the vertical; the marker, arms and body stay on the aim.
  `BotSenses` looks along `Yaw + HeadYaw`. `BotBrain.TurnHead` turns it at `headTurnSpeed_degps` towards
  `Wrap(wantYaw − yaw)` (the head leads every turn) plus a glance when calm (idle or going back, nothing on the go:
  `glance*`), from a second `Pcg32` per bot so the brain's own random numbers are unchanged. `BotBrain.Sweep` replaces the
  sine scan of a post, a held room and a patrol pause with held looks (`scanHold*`); the Marksman's vantage keeps its slow
  sine. People send 0 (their view is the aim).
- **`StepGait`** adds three things, all drawing: the stop (on the frame the smoothed speed drops below 0.3 m/s from
  above `stopFrom_mps`, the steps into the stance shorten to `stopStep_s` with a tighter tolerance for `stopWindow_s`, and
  `Settling()` dips the hips `stopDip_m` and tips the upper body `stopLean_deg`, scaled by how fast it was going); the
  weight shift (standing settled, a target of −1, 0 or +1 every `weightEvery*`, eased in over `weightShift_s`, moving the
  hips `weightSway_m` and rolling them `weightRoll_deg`; `Vary(seed)` gives each character its own timing); and
  `KneesOut`, which narrows the stagger, sets both feet back by up to `kneeBack_m` and turns the knee bend out to the side.
- **`CharacterVisual`** probes the paint geometry ahead of the body each frame at knee height (crouched only) and at
  elbow height (`Closeness`), easing `KneesOut` and `CharacterPoser.ElbowsOut` (elbows in and back along the body); it
  breathes (`Breathe`: rate and depth from how hard the body has been going, `breath*`) into `CharacterPoser.Breath`
  (chest up, neck back); passes the head's turn to `CharacterPoser.HeadYaw` (40% neck, 60% head); and when the player
  goes out, blends the gear and arms from their last pose in to the sim's out pose over `outRaise_s` (nothing counts on an
  out player's hitboxes) while `SupportRaise` takes the support hand off the marker to over the head, its elbow out.
- **Place stills**: `PlaceSpec.Still` is the viewpoint the level file's `"still"` names (inside the place), else the first
  inside it, checked at load. `PlaceStills` (`-- --place-stills=DIR --level=ID`) puts a camera at each, hides everyone,
  holds 30 frames and saves the frame's middle band at 720 × 320 as JPEG; the area card shows `res://ui/places/LEVEL_PLACE.jpg`
  under the places when it's there. The menu's smoke test picks every place of every area and checks the laid-out page
  fits the project's 1600 × 900 less the screen's margins (headless windows are taller, so the room is worked out).

### 15.11 Ladders (as built, M3.15)

- **Data.** A prop type lists its `ladders` (`kit/props.jsonc`) in its own frame: the foot (middle of the bottom, in the
  rungs' plane), the height to the floor at the top, the facing of a climber on it, width, handholds above the top
  (`rails_m`), how far past the rungs stepping off goes (`exit_m`) and how far back the brackets reach (`bracket_m`).
  `KitCatalog` turns them into `LadderTemplate`s, `LevelFactory` places them as `LadderSpec`s in world space
  (`LevelLayout.Ladders`, kept by `ForPlace`), and `PropShapes.Ladders` draws them from the template, so a drawn ladder
  is always a climbable one and the reverse.
- **Rules.** `SimWorld.Ladders` (a `LadderSet`) holds the level's ladders and `FindGrab`: in front of one within
  `climbing.reach_m`, facing it to within `grabAngle_deg`, from its foot to a metre under its top; or behind its top within
  reach and its exit, facing out over it. Climbing is a branch of `MovementModel.Step` (`Climb`), so the game's
  `PawnBody`, the headless `NavGridMover` and a future network client all climb by the same rules. `PlayerState.Ladder`
  and `LadderPhase` (climbing, stepping off at the top, getting on at the top) are the state. Interact gets on; forward
  and back are up and down at `speed_mps` (the dead climb down); the body is pulled onto the climbing line
  (`standoff_m` out from the rungs) at up to 3 m/s; at the top it steps on over the edge at `stepOffSpeed_mps` until past
  the rungs by the ladder's exit, rising 5 cm to clear it; at the bottom it's off once grounded; jump lets go, pushed back
  at `letGoSpeed_mps`. Getting on or off at the top gives up after 1.5 s (someone in the way). `MovementResult.Climbing`
  and `ClimbVelocity` tell the host there's no gravity this tick.
- **In the sim's tick** a climber's body faces the ladder (`PlayerState.Yaw` = its facing) and the head turns to the
  view up to `maxHeadTurn_deg`; fire and refill are blocked, doors ignore interact, the tuck is off, and a footstep on the
  rungs' surface comes every `footsteps.climbStride_m` climbed. `HitboxRig.Pose` with `HitboxPose.Climbing`: the arms
  pitched up `climbArmsPitch_deg` in front, the marker slung from `slungBelowEye_m`/`slungBehind_m` with its barrel
  pitched `slungPitch_deg` and rolled `slungRoll_deg`, the loader and tank where they sit on it.
- **Bots.** `NavGrid` joins each ladder's foot (the span in front of it) to its top (the span stepped off onto), both ways,
  at `ladderCostPerMetre` × the climb; components, searches and landmark distances cross them, but landmarks are placed
  by walking alone, so a ladder to a dead end leaves ground searches as they were. A path keeps both ends as waypoints;
  `LadderBetween` tells `BotBrain.AtLadder` it's a climb: stand in front of the foot (or at the top's exit), face it (or
  out), tap interact, and climb to the end the next waypoint is at; while on a ladder, `Think` keeps it climbing whatever
  the mode, and `WalkOff` lets the dead climb down first.
- **Drawing.** `CharacterVisual` hands `CharacterPoser.Ladder` the hands' and feet's places from `LadderLimbs` (rungs
  every 0.3 m; feet at or below the body's feet, hands from shoulder height up; left foot with right hand, then right foot
  with left hand, two rungs at a time over `ladderLimbMove_s`, swung `ladderLimbSwing_m` out). `LadderBody` sets the hips
  `ladderHipsIn_m` in and `ladderHipsDown_m` down, square to the ladder, the feet by `StepLeg` with the knees forward and
  the arms by two-bone IK. The first-person `ViewModel.Lowered` drops the marker out of view. The HUD prompt says what
  interact will do, and on a ladder how to climb and let go.
- **Checks.** `LevelSmokeTest` climbs every ladder up and down through the real scene; `-- --ladder-demo` films one.

## 16. Phase 4: multiplayer

> **Status: approved 2026-10-09 with the [Phase 4 plan](phase-4.md) (defaults taken).** This is the
> technical design. It fills in §6, which still holds, and each part gains "as built" notes as its milestone lands.

### 16.1 Processes and roles

- **One project, run three ways:**
  - the game, offline or **hosting** (a listen server);
  - the game joined to a host (a **client**);
  - the same export started with `-- --server` (**dedicated**, headless).

  Offline play is a host nobody joins: the same authority path, with no transport.
- **The authority** is the loop that exists now. `SimDriver` steps every `IPlayerDriver` (each moving its `PawnBody`),
  then `SimWorld.Step`. Each player's driver depends on who they are:
  - A remote player gets a `RemotePlayerDriver`, whose pawn takes its commands from that player's queue (§16.6).
  - Bots keep `BotPilot`.
  - The host's own player keeps `PlayerController`, with no prediction and no rewind.

  After each step, the server's `Replicator` (an `ISimEventListener`) files the tick's events for each player.
- **A client** builds the level as the game does now: presentation, walking collision, and a `SimWorld` for the level.
  It steps that world in client mode (§16.4): its own player predicted, the others as puppets posed from snapshots, the
  balls cosmetic.
- **The dedicated server** is `scenes/Server.tscn` (`ServerMain`): no window, audio, dressing or art (as `--no-art`).
  `LevelBuilder` builds only the walking collision, so the server has the same physics and the same movement code as
  the game.

### 16.2 Rules for several players (`Pb.Sim/Match`)

- **Setup.** `MatchSetup.HeroId` gives way to the round's sides: each side's team number and the people on it (player
  ids), and `Attackers`, the side that attacks an objective. Starting gear follows: `StartPods` for people, `BotPods`
  for bots.
- **Result.** The round's result becomes side-neutral: `MatchResult { WinnerTeam (−1 for none), Reason }`, where the
  reason is cleared, extracted, held, traded or time up. `RoundOutcome For(team)` maps it to the outcomes there are now,
  so the HUD, summary, referee and records keep their code.
- **When a round ends.** `LastTeamStandingMode` already reads "the round goes on while at least two teams are in", which
  holds for any number of people. Online, a round also ends once no person is still in it, since nobody is left to
  play it out for. Offline, free-for-all bots still play on behind your summary, as now.
- **Starts.** `SpawnPlanner` takes the sides and their people, and the same seed still deals the same starts:
  - co-op: the people at the area's player spawns, together within `teammatesWithin_m`;
  - teams: each side's people and bots round one spot, the two spots on opposite sides, out of each other's sight;
  - free-for-all: everyone apart.
- **Objectives.** The objective uses `Attackers`. With people on both sides, the session alternates the attackers.
- **Records** take only rounds with one person (§16.10).

### 16.3 Lag compensation in the sim

- Each command carries `ViewTick`: the server tick (with a fraction) that the client's puppets were drawn at. When the
  server fires a shot from that command, its rewind is `clamp(Tick − ViewTick, 0, HistoryTicks − 1)`, at most 23
  ticks (192 ms, inside the spec's 200 ms cap). Bots and the host's own player fire with rewind 0.
- The ball keeps its rewind for its whole flight. `BallisticsWorld` sweeps it against `PlayerHitboxes` at
  `tick − rewind`; the 24-tick history is already kept. `SolveShot`'s aim point uses the same tick, so the shot
  converges on what the shooter saw under the crosshair.
- The static world and the doors are tested where they are now (a door is where it is).
- Within that cap, everything RTT + interpolation delay + queue (§16.6) covers is compensated: a ping up to about 130 ms.
  Beyond it, the shooter leads by the remainder.

### 16.4 Client mode in the sim

`SimWorld.Role = Client` changes what a step does:

- **Markers.** Only the local player's marker runs, to predict its shots. The others' markers don't run, and their
  shots come from the server's `ShotFired`.
- **Balls.** A client's balls never eliminate anyone, and the match state isn't run (it's the server's, from the
  snapshot):
  - Others' balls are tested against the world and doors only; the server's events say where they end (§16.8).
  - The local player's own balls are also tested against the puppets, for a provisional splat.
- **Footsteps.** The puppets' footsteps are worked out locally from their interpolated movement (`UpdateFootsteps`), so
  they cost no traffic.
- **Server-driven state.** Door motion, pickups and objectives come from the server.

### 16.5 `Pb.Net`: messages, channels, budget

- **Projects.** `src/Pb.Net` references `Pb.Sim` and no Godot; its tests are `tests/Pb.Net.Tests`. Like the sim, its
  per-tick paths use pooled buffers and allocate nothing.
- **Packing.** `BitWriter` and `BitReader` work over pooled byte buffers, with quantisers for each kind of value:

  | Value | Bits | Precision |
  |---|---|---|
  | Position (per axis, over the level's bounds plus a margin) | 16 | ≤ 2.5 mm over 160 m |
  | Velocity (per axis, ±16 m/s) | 12 | 8 mm/s |
  | Yaw | 16 | 0.006° |
  | Pitch, head yaw | 12 each | |
  | Lean, shoulder, tuck | 8 each | |
- **Channels** (`ITransport`): unreliable sequenced (commands, snapshots), and reliable ordered (handshake, lobby, round
  setup, chat).
- **Client → server**, 60 packets a second:
  - the last 4 commands, each with its sequence (the client's tick), `Move`, `Yaw`, `Pitch`, `HeadYaw`, `Buttons` and
    `ViewTick`;
  - the newest snapshot tick received (the ack).
- **Server → client**, 60 packets a second (every other tick): a snapshot, delta-compressed against the client's last
  acked one, then the events that client hasn't acked yet. The snapshot holds:
  - the tick, and the client's last command processed;
  - the client's own player at full precision: its whole movement state, and its marker (loader, pods, air, fire mode,
    ramping, refill, shot sequence);
  - the other players, quantised: position, velocity, angles, stance, lean, shoulder, tuck, eye height, and flags
    (alive, present, grounded, sprinting, refilling, ladder and phase);
  - the doors that moved, and the match: phase, clock, objective, pickups.
- **Events** ride with the snapshots and are resent until a snapshot carrying them is acked, so a lost packet never
  holds the others up:
  - `ShotFired`: shooter, sequence, tick, origin, velocity;
  - `BallBounced` (each bounce, with the outgoing velocity), `BallBroke` and `BallDespawned`: shooter, sequence, tick,
    point and normal, and for a player the hit part and the point in that part's frame;
  - `PlayerEliminated`, `MaskSprayed`, `DoorMoved`, `PickupTaken`, `CaseTaken`, `CaseDropped`, `CaseExtracted`,
    `HoldChanged`, `MatchPhaseChanged`;
  - `RoundEnded`, with the final stats;
  - callouts: speaker, kind, line and contact position.
- **Budget** for one client in a full round of ten, everyone firing:

  | Traffic | Rate |
  |---|---|
  | Down: snapshots | ≈ 8–10 KB/s |
  | Down: events | ≈ 2 KB/s |
  | Down: UDP and ENet headers | ≈ 2.3 KB/s |
  | **Down in all** | **≈ 13 KB/s (0.1 Mbit/s)** |
  | Up: commands | ≈ 5 KB/s |
  | Host's upload, with nine others | ≈ 120 KB/s (1 Mbit/s) |

  CI fails a run over 25 KB/s down per client.

### 16.6 Time, commands and the server's queue

- **No shared clock.** A command's sequence is the client's tick. The server keeps a queue per player, aiming for 2
  ticks deep (`net.jsonc`), and runs one command a tick, in order:
  - If the queue is over its limit, it runs two in a tick.
  - It never runs more commands than ticks since the player joined, plus a small allowance, so a client can't bank
    time to move or fire faster.
  - If the queue is empty, it repeats the last command; the redundancy in the next packet usually brings the real one
    anyway.

  The queue absorbs drift between the two machines' clocks.
- **Round trip.** It's measured from the acks: each snapshot echoes the newest command sequence, and the client knows
  when it sent that command. The client uses it for its interpolation delay and for the ping on the HUD.
- **Interpolation.** The client draws the others at the newest snapshot tick minus a delay of 2 snapshot intervals
  (33 ms) plus the measured jitter, normally at most 3 intervals (50 ms). Across a gap it extrapolates for at most
  100 ms, then holds.

### 16.7 Your own player: prediction and reconciliation

- **Each tick** the client samples its command, moves its `PawnBody` (`MovementModel` and `MoveAndSlide`) and steps its
  marker, as the game does now, and keeps the command with the predicted state by sequence.
- **On each snapshot** it compares the server's state for the last command processed with its prediction:
  - Within tolerance (1 mm of position, discrete state exact), it drops the history up to there.
  - Otherwise it sets the body and player state to the server's and replays the stored commands after it through the
    same `PawnBody`. Godot's `MoveAndSlide` can run several times in one physics frame; the first replayed step takes the
    server's `Grounded` rather than the body's last floor contact. The camera eases out a small correction over about
    100 ms and snaps a large one (over 0.5 m).
- **The marker** is corrected likewise: loader, pods, air and the shot sequence come from the server. Balls are keyed by
  shooter and sequence, so a replayed shot doesn't spawn a second ball.
- **Not predicted:** doors (a door you open starts moving one ping later), pickups and objectives.
- **The check first (M4.3).** Replaying a scripted run's commands from a stored state must land within 1 mm of the first
  run on one machine. Between a Linux server and a Windows client, it must stay within tolerance on almost every tick.
  If either fails, `Pb.Sim` gets its own walking mover: a capsule sweep with slide and floor snap against the level's
  primitives (stairs are already ramps, doors and ladders already shapes). The server, the clients and the headless
  tests would all use it (it would replace `NavGridMover`), and Godot's `CharacterBody3D` would only be drawn. §4.7 and
  §12 named this fallback from the start.

### 16.8 Other players and balls on a client

- **Puppets.** Each remote player is a `RemotePawn`: an `OpponentPawn` without a pilot.
  - Every tick its `PlayerState` is written from the interpolation buffer: position, velocity, angles, stance, lean,
    shoulder, tuck, ladder.
  - So `CharacterVisual` and its poser animate it exactly as they animate the bots now: planted steps from the
    movement, leaning, climbing, the walk-off.
  - Its hitboxes are recorded each tick, for the client's own shots.
- **Others' balls.**
  - On `ShotFired` for tick S, the client spawns a cosmetic ball when its display reaches S, so the ball leaves the
    puppet's muzzle as the puppet fires.
  - It flies with the shot's own random stream (`SeedHash.Shot`), against the world and the doors.
  - Each event for that ball is applied when the display reaches its tick. It arrives about one interpolation delay
    before that, and a loss is covered by the resend.
  - A bounce takes the server's outgoing velocity.
  - A break ends the ball with its splat. A break on a player lands on the hit part at the part-frame point, so it sits
    on the puppet even though the server tested the rewound pose.
- **Your balls** are predicted entirely:
  - They fly at once.
  - They're tested against the world and against the puppets as you see them, which is what the server checks with
    your rewind.
  - A splat on a puppet stays provisional until the server's result for that ball. If the result differs, the splat is
    removed and the server's own is drawn.
- **When events play.** Events about others (eliminations, door motion, pickups, callouts) play when the display reaches
  their tick, so sound and sight agree with the puppets. Events about you (you're out, mask spray, your pickups) play
  at once; the ball that put you out ends on you there and then.
- **Presentation.** The existing presentation (`AudioDirector`, `SplatSystem`, the HUD, the kill feed, subtitles, voices)
  takes all of these as `SimEvent`s through `ISimEventListener`, unchanged.

### 16.9 Validation (server)

- **Commands:**
  - sequences only go forward;
  - at most one a tick on average, with a small burst allowance: a queue that stays too long runs two to a tick until
    it's back to its depth (as built). Two seconds of that, which is a clock running fast, not a hiccup's catch-up, is
    logged, and again every ten seconds while it goes on;
  - a command further ahead of its turn than the queue reaches (`commandAheadTicks`) means the copy's clock has run on:
    the host stalled, or the copy runs fast and the host can't keep up even two to a tick. The queue skips on to it
    and never runs the commands in between, so a stall costs a player a moment rather than the rest of the round, and
    running fast still gains nothing. Each skip is logged, at most every ten seconds for a player;
  - `Move` is clamped to the unit circle;
  - angles must be finite, and are clamped (pitch to `maxPitch`, head yaw to `maxHeadTurn`);
  - unknown buttons are masked off;
  - `ViewTick` must lie within the history window.
- **Fire rate, loader, pods and air** are the server's own marker, run from the commands. A trigger toggled every tick
  is still held to `rateCap_bps`, and an empty loader or tank dry-fires.
- **Speed** is the server's own movement, run from the commands. No client position is ever accepted.
- **Every violation** is counted per player and logged, rate-limited. Nothing kicks automatically; the host can remove
  a player.

### 16.10 Lobby and session

- **States.** A server-side state machine (`Pb.Net/Lobby`):
  1. `Lobby`: joining, sides, the host's choices, ready-up.
  2. `Countdown`: 5 s (`net.jsonc`).
  3. `Loading`: everyone loads the level, for at most 30 s.
  4. `Round`: briefing, live, ended.
  5. `Summary`.
  6. Back to `Lobby`, or to the vote first: 3 choices for 15 s.
- **Sides.**
  - A newcomer goes on the smaller side.
  - While balance is on, a switch that would leave the sides more than one person apart is refused.
  - Bots fill each side up to the size, at the host's difficulty.
  - Free-for-all has no sides.
- **The session's score:** rounds won per side (per player in free-for-all) and eliminations, kept across rounds until
  the host closes the lobby.
- **The round setup** (reliable) holds the level, place, mode, size, objective, tier and match seed, and the roster:
  each player's id, name, team, person or bot, look and start. Clients don't re-run the spawn planner. Door starts
  follow from the seed, as now.
- **Records.** A round with more than one person isn't recorded.
- **Chat** is reliable messages, to everyone or to a side, at most 200 characters and rate-limited.

### 16.11 Transport and discovery (`game/net`)

- **ENet.** `EnetTransport : ITransport` runs over Godot's `ENetConnection` and `ENetPacketPeer`: channel 0 unreliable
  sequenced, channel 1 reliable. Its UDP port comes from `net.jsonc` (47820), and it takes up to nine peers besides the
  host.
- **Handshake.** It checks the protocol version, the build stamp and a SHA-256 of the data files (`game/data/**`, hashed
  as they load), plus the password if there is one. A mismatch is refused, naming both versions. The hello also says
  who's joining: the platform identity's id (§16.13).
- **LAN discovery.** Hosts answer a broadcast query on port 47821 (`PacketPeerUdp`) with their name, area, mode, how
  many people they have and the most they take.
- **Timeouts and the lag simulator** live in `net.jsonc`: connect within 10 s, dropped after 5 s of silence. The lag
  simulator wraps any transport: `--net-lag=MS` (round trip), `--net-jitter=MS`, `--net-loss=PCT`.

### 16.12 Dedicated server builds

- **Linux.** `tools/package/server-build.sh` exports the Linux server (Godot's `linux_release.x86_64` template, fetched
  like the Windows ones by `fetch-templates.py`), stamped with the Windows build's version. It packages
  `Pb-server-linux.tar.gz`:
  - the binary, the pack, the .NET assemblies and `server.jsonc`;
  - `run-server.sh` (updates, then starts it headless; the game's .NET needs no ICU, as `Directory.Build.props` sets
    `InvariantGlobalization`);
  - `update-server.sh`;
  - `pb-server.service` (systemd).

  Before packaging, it starts the exported server once to check that it serves.
- **Windows.** `Pb-server-windows.zip` is made from the Windows build: `Pb.exe`, its pack and its runtime, without the
  art. It adds `Pb.console.exe` (Godot's console template, the program a console-wrapper export makes) and `Server.bat`,
  which updates, then runs `Pb.console.exe --headless -- --server`.
- **Updates.** `update-server.sh` and `update-server.ps1` compare `version.txt` with the release manifest's version. When
  they differ, they fetch the package and put its files in place, all except `server.jsonc`. The Linux script renames
  each file into place, so nothing is half written.
- **Release.** Both go on the test-build release, uploaded before the manifest.
- **`server.jsonc`** holds: name, port, password, the most people, a rotation of area, place, mode, size, objective and
  difficulty, bots on or off, the vote, and the time between rounds. Only `res://data` is hashed for the handshake, so
  editing the copy beside the program doesn't change which players can join.
- **Logs.** The server logs settings, joins, leaves, refusals, rounds and violations to stdout (flushed on each line)
  and to `user://logs/server.log`. Past 10 MB, the next start sets the log aside as `server.old.log`.
- **Guide.** `docs/hosting.md`.

### 16.13 Platform services (`Pb.Net/Platform`, `game/platform`)

- **The interface.** `IPlatformServices` (engine-free, in `Pb.Net/Platform`) gathers:
  - `IIdentity`: your name, a stable id, and whether the game asks for your name (offline) or takes the platform's;
  - `ISessionBrowser`: the games found to join, searched while a screen lists them (a typed address needs no finding);
  - `IFriends`: friends and invites;
  - `IStore`: null while there's none.

  It's polled once a frame (`PlatformPump`), and `Platforms.Current` picks the one in use. The game asks only these
  interfaces who you are and what games there are: Play with others, and `--join` from the command line.
- **Offline first.** `OfflinePlatform`:
  - takes its identity from the profile: the name you typed, and an id (`GameSettings.ProfileId`, 32 hexadecimal
    digits) made the first time the game reads its settings and saved at once;
  - finds sessions by LAN discovery (`LanSessions` over `LanBrowser`);
  - has no friends or store.
- **The id on the wire.** The hello carries the identity's id (protocol 3). A version-2 hello is still read, so its copy
  is told to update. The lobby keeps the session score of anyone who leaves and gives it back when the same id joins
  again. It doesn't if another copy with that id is still in: two copies on one computer share a profile.
- **Later.** A Steam implementation can be added without touching the game: identity, lobbies as found games,
  invites, and relay networking as another `ITransport`.

### 16.14 Testing

- **`tests/Pb.Net.Tests`:**
  - packing, quantisation, deltas, the event resend, the command queue and interpolation;
  - prediction against a scripted mover;
  - whole networked rounds without the engine: a server `SimWorld` and client `SimWorld`s joined by the loopback
    transport through the lag simulator, with bodies moved by `NavGridMover` (or the sim's own mover, if it comes) and
    bots included. The fairness test, the validation tests and the desync check run here.
- **CI's `net-round` job** (`tools/ci/net-round.sh`, as built):
  - a headless dedicated server (`--server-config=tools/ci/net-round.jsonc`: one round on every area, in all three modes)
    and five headless players (`--join=127.0.0.1 --bot-match --rounds=4 --net-lag=100 --net-jitter=10 --net-loss=1`),
    each with a bot at its controls sending commands through the network like a person, and bots filling to ten;
  - two of the five cheat: `--net-cheat=fire` flips the trigger every tick (and refills when dry), `--net-cheat=fast`
    runs the copy's ticks twice as fast;
  - every copy prints its `NET RESULT` line for each round (result, end tick, and every player's shots, hits,
    eliminations, pickups and out tick), and the server logs each player's traffic and shot rate;
  - the job fails on a line that differs from the server's, an error, more than 25 KB/s to a player while a round is
    live, a shot rate over the cap, a trigger flipper that never reaches half the cap, or no log of the fast clock.
- **Wine.** In the Windows build job, `tools/ci/wine-join.sh` has the exported Windows build play a round on the packaged
  Linux server under Wine, since the Windows .NET runtime has faulted where Linux didn't (§14.7).
- **Bench.** `tools/Pb.Bench` "Network":
  - the server's tick with ten players (nine joined over the in-memory network) and 1,000 balls: its step, then the
    packing for nine;
  - the traffic of a full round with everyone firing at the cap;
  - a 12-tick replay.

  Joining copies print their own replays' cost, with what differed first in each correction.

### 16.15 Risks

| Risk | Mitigation |
|---|---|
| Replays through Godot's movement diverge | Checked first (§16.7); the fallback is the sim's own mover. |
| Head-of-line blocking under loss | Events ride with snapshots and are resent until acked; only the lobby, round setup and chat use the reliable channel. |
| Bandwidth with 1,000 balls in the air | Balls are never sent per tick; a shot is one event, and where it ends is another. |
| The two machines' clocks drift | No shared clock: the server's queue absorbs it. |
| A cheat predicting the spread | Accepted (§6); server-only seeds can come later. |
| Faults only Windows shows | The Windows build joins the Linux server under Wine in CI. |
| Routers in the way | Port forwarding (in the hosting guide), a rented server, or later a platform relay. |

## 17. Phase 5: locker and extras

> **Status: approved 2026-10-10 with the [Phase 5 plan](phase-5.md) (defaults taken).** This is the
> technical design; each part gains "as built" notes as its milestone lands. Nothing here changes how a compound round
> plays: gear is looks only, and the field and the new modes are extra choices.

### 17.1 Gear catalogue and loadouts

- **Data** (`game/data/gear/`):
  - `brands.jsonc`: each brand's id, display name, a line about it, its mark (a wordmark in the stencil hand of
    `MarkingPainter` and an emblem from a few shapes) and the palette its items default to;
  - one file per slot: `markers`, `loaders`, `tanks`, `masks`, `jerseys`, `pants`.
- **An item** has an id, a brand and a display name, and its build: a `shape` recipe with parameters, plus an optional
  generated `model`, as props have. It also has its colour zones (what the primary, secondary and accent colours paint)
  and its default colours. A marker also gives its grip points (pistol grip, foregrip, trigger and support wrists), as
  `markerModel` does now. A jersey or pants item is a pattern; a mask is a shell recipe.
- **No item has a gameplay key**, and the validator refuses one (looks only, the plan's question 1).
- **`Pb.Sim/Gear/GearCatalog`** (engine-free) loads and checks them: every item's brand exists, and every slot has an
  item of every brand.
- **`Loadout`:** the character (the look, 0–2), then for each slot an item id and three colours (sRGB bytes).
- **`LoadoutDealer`** deals a bot's loadout from the match seed and the bot's id, with a `Pcg32` of its own, so every
  copy deals the same and no other random stream moves.
- **The profile.**
  - `ProfileData` version 3 gains `loadout`, and the look (`GameSettings.PlayerLook`) moves into it.
  - A version 2 profile loads with the default kit on the saved look.
  - An item id the catalogue no longer has reads as its slot's default.
- **The sim never reads a loadout.** Every player keeps `SimConfig`'s one marker, loader and air, and `HitboxRig`'s
  boxes.
- **As built (M5.1):**
  - The catalogue is one file, `gear/catalog.jsonc` (brands, items, defaults), loaded with the sim's data (`sim.jsonc`
    → `files.gear`, `GameData.Gear`) so the game, the locker and the network share one list. An item's three colours
    are its defaults; what each paints, and a marker's grip points, are its recipe's (in code). Checked at load: unique
    ids, every brand makes every slot, defaults of the right slot, `#rrggbb` colours, marks in the stencil's letters,
    `includes` only on a generated model.
  - `GearCatalog.Deal` is the dealer (its own `Pcg32` from the match seed and the player's id); `Normalised` puts a
    slot's default in for a pick the catalogue no longer has.
  - People wore the field's own kit on their character until the locker (M5.2); now you wear what you saved there
    (`--kit=BRAND` still wears one brand's whole range). Bots wear their dealt kit in their side's colour, on every
    copy.

### 17.2 Drawing gear and clothes

- **Gear models** (`game/player/GearModels`).
  - Each item's mesh is built once from its recipe with `ShapeMesh` (as `PropShapes` and `MarkerShape` are) and shared
    by every wearer.
  - A vertex channel carries each vertex's zone: primary, secondary, accent, or bare (metal, rubber).
  - `gear.gdshader` colours the zones from instance uniforms, sets roughness by zone, and adds the brand's marks from an
    atlas painted at load.
  - Each item is fitted into its hitbox's box, as `GearShapes` fits the coded marker now, so the boxes stay the gameplay
    shape.
- **Where gear is drawn:**
  - **First person** (`ViewModel`): your marker, loader and tank at `viewScale`, through `viewmodel.gdshader` (which
    gains the zones), with the hands at the marker's grip points.
  - **Third person** (`CharacterVisual`): each piece hung in its hitbox's frame, as the generated marker is now.
- **Clothes** (`CharacterModel`).
  - Once per model at load, each vertex gets a zone by its strongest bone: hips and legs are pants, spine and arms the
    jersey, neck and head the head, hands the gloves, feet the boots.
  - `character.gdshader` takes over the model's materials. It keeps the picture's albedo, recolours the jersey and pants
    zones by luminance (so the folds and seams stay), and draws the item's pattern (bands, panels, camouflage)
    procedurally in the bind pose's frame, so it doesn't swim as the body moves.
- **Masks** (`MaskShapes`).
  - A shell is fitted to each model's head as the armbands are to the arms (`CharacterModel.Sleeve`): the head zone's
    vertices, in the head bone's frame, give its reach in each direction, and the recipe is scaled to it plus a gap.
  - Under a brand's mask the head zone is discarded in the shader, so the generated mask never shows through.
- **The generated marker** is split at load by its mesh's connected pieces into receiver, loader and tank if they're
  separate. If not, it's one item that fills all three slots.
- **Sides.** In a round with sides, the side's colour is the jersey's and pants' primary (the plan's question 3); the
  armbands stay.
- **Cost.** Meshes are per item and colours are instance uniforms, so ten loadouts are ten material instances, not ten
  sets of meshes.
- **As built (M5.1):**
  - **Recipes** (`MarkerRecipes`, `LoaderRecipes`, `TankRecipes`) build into `ShapeMesh`es whose material ids are zones:
    a finish (anodised, matt, polished, plastic, gloss, fabric, rubber, knurl, metal, steel, carbon, lens, shell, mark)
    times what paints it (main, second, accent, its own colour, the paint). Every marker keeps the field marker's
    ergonomics (the pistol grip, trigger and guard about the grip's top, the feed neck's top, the tank's mount), so the
    hands, a loader and a bottle fit every one; the field's own is `MarkerShape` absorbed with its parts mapped to
    zones. `GearModels` fits each item into its hitbox once, with levels of detail, and shares it; `gear.gdshader` and
    `gear_clear.gdshader` are the zones' shared materials, the wearer's colours instance uniforms. The knurl, weave and
    twill are pressed in by `gear_patterns.gdshaderinc` (bump from the height's screen-space slope, faded where too
    fine), which the view model's shader includes too. The marks are an atlas painted at start (`BrandMarks`), each
    printed as a cut-out quad.
  - **First person:** each slot is its own mesh in its own colours (the loader on the marker's feed, the bottle at its
    back), the trigger hand moved onto the recipe's grip and the support hand turning about its foregrip; a see-through
    loader shows the paint as full as it is. The field's marker is the generated model when its art loads (its loader
    and bottle are part of it, so the slots it `includes` aren't drawn). Your sleeves take your jersey's colour.
  - **Clothes:** `ClothesZones` builds each model's mesh again once (about 50 ms), its zones as vertex colours and its
    rest positions in `CUSTOM0`, keeping the levels of detail the import made. `character.gdshader` draws the model's
    own maps and repaints the jersey and pants by their brightness over the zone's mean to the power `shading` (0.5).
    The patterns (`ClothesPatterns`: own, yoke, panels, camo, patches, stripe) are laid out from the rest pose and the
    joints (the arm's line, the leg's middle by height), mirrored across. A model whose exporter left glTF's default
    full metalness without a map is drawn matt (cloth isn't metal).
  - **Masks** (`MaskShapes`): the face is the head's front `faceDepth_m` back from its headfront joint, below
    `faceBelow_m`, moved by the Head bone rather than the neck. The shell's surface is a grid of radii (12 heights by 40
    angles about an upright axis 4 cm behind the cut) from the face's vertices, hair strands well out from the rest
    left out, then swollen a cell and smoothed; each brand's parts are slabs laid on it (goggles standing upright over
    their height), and the strap runs from the shell's sides round the back with the mark wrapped on it.
    `--gear-demo=masks` shows every mask on every character from eight directions.
  - **Sides:** in a Teams round people's jerseys and pants take the side's colour as their main; bots' always do (in
    free-for-all, each its own paint colour).
  - **The generated marker** is one connected piece, so it's Norrel's marker with its loader and bottle `includes`d.

### 17.3 The locker

- **Scene** `scenes/Locker.tscn` (`LockerMain`):
  - a room from a small level file (`levels/locker_room.jsonc`), built by `LevelBuilder` with the kit's materials and the
    dressing that suits a room (damp, cobwebs, floor debris, skirting);
  - a turntable under a lamp;
  - one `CharacterVisual`, standing, in the loadout.
- **`LockerCamera`** orbits as you drag and closes in as you scroll. Each slot has a framing (the marker in the hands,
  the mask at the head, the pants at the legs), and the camera eases between them.
- **`LockerUi`** (from `UiKit`):
  - the character, the six slots, and the chosen slot's items by brand;
  - three swatches and a colour picker;
  - Done saves through `Profile`, and Back leaves the loadout as it was.
- **Opened** from:
  - the main menu, as a new screen in `MainMenu`'s screen lists, the menu tour and the smoke test;
  - Play with others and the lobby, in place of their character pickers. The lobby's `CharacterPreview` shows the
    loadout.

- **As built (M5.2):**
  - **An overlay, not a scene.** `LockerView` (game/ui) is a full-screen control the main menu or the lobby adds over
    itself, so the lobby carries on behind it (its chat, the countdown; the countdown's line shows in the locker) and a
    round that starts while it's open finds what you've picked saved. The room is drawn in a `SubViewport` with a
    world of its own (`LockerStage`), at your preset's antialiasing and your render scale; the menu stops drawing its
    level while the locker is open.
  - **The room** is `levels/locker_room.jsonc`, the kit's `changing_room` building (8 × 6 m, lockers down both long
    walls, benches, high windows, a door left ajar, strip lights), built by `LevelBuilder` with its doors, floor
    debris, markings, cobwebs, damp, things on the walls, contact shadows and the window light. A `SimWorld` of its own
    loads it (it never steps): your feet stand on the turntable's collider, and the camera keeps out of the room by
    sweeping a sphere to where it wants to be. The turntable (a drum with a rubber top) and the lamp (an enamel shade on
    its flex, a bulb, a spot down on you) are built with `ShapeMesh` in the kit's materials; a soft fill rides with the
    camera. You face the spawn's way (down the room's length), 0.75 m off the middle towards the windows so the lamp
    hangs clear of the strip lights (one hangs askew, and its shadow put the lamp out), and a sim test keeps 1.4 m
    clear round you. You hold the marker low (`holdPitch_deg`) with your head up, the idle clip held at its start so it
    doesn't look away, and your head turns to follow the camera (`headTurn_deg`), so a close-up of the mask shows its
    front.
  - **`LockerCamera`** orbits about you (yaw from your front, pitch, distance, field of view, the point looked at),
    easing to each slot's framing over `ease_s` and drawing you in the middle of the room the panels leave (it slides
    across rather than turning). A framing looks at a hitbox part's middle (the marker, loader, tank, torso, legs; the
    head as drawn), so the close-ups follow the pose; the whole of you is looked at over the turntable. Drag turns it,
    the wheel brings it closer, a pad's right stick turns it too.
  - **Picking:** the item buttons are each slot's items by brand, with the brand's line; the colour chips, the palette,
    a `ColorPickerButton` (its presets the palette) and the item's own colours. What can't change says why: a generated
    marker's own finish and the loader and tank it `includes`, the character's own mask, and the second and accent of
    the clothes the characters came with. Colours repaint what's drawn (`CharacterVisual.Recolour`); a new item or
    character builds you again. Colours you give an item come back if you return to it. The whole kit's panel puts a
    brand's range on you, or the field's own kit; the clothes' panels show them in a side's colour.
  - **Saved:** Done writes the loadout into the profile (`ProfileData.Loadout`, format 3: each slot's item id and
    `#rrggbb` colours, read back by `GearCatalog.Read`) and the character into the settings (`PlayerLook`, as the
    lobby and the network still take it). Rounds, the training ground and the lobby's preview wear
    `Kit.Saved(catalogue, profile's loadout, your character)`. In a game with others only your own copy shows your kit
    until M5.3 sends it.
  - **Checks:** the menu's smoke test opens it, finds every slot's items, picks a brand's marker in a colour of its own
    and a brand's mask, presses Done, and reads the same back from the profile (then puts the profile back);
    `-- --locker-tour` (with `--locker-hold=N`) shows each slot, a brand's range and the clothes in a side's colour.

### 17.4 Paint

- **The look.** `SplatPainter` gains a wet splat atlas: albedo, normal and ORM for each shape (a raised middle, drops,
  shell flecks). A fresh world decal uses the glossy ORM and swaps to a satin one after `drying_s`.
- **Paint in the surface shaders.**
  - Characters and their gear keep their last `paintSlots` splats (16) in their own frame (a character's bind pose, an
    item's frame) as shader parameters: centre, normal, radius, colour, shape and age. The oldest is replaced first.
  - What stands still (walls, floors, bunkers, which take a lot of paint in a match) keeps the pooled decals.
  - Their shaders draw each splat from the atlas, projected along its normal and faded by the surface's angle to it,
    so paint wraps round an arm or a barrel and moves with it.
  - This replaces the bone-parented decals on players.
- **First person.** A break within `spatterReach_m` of your eye (the mask spray's event, widened) also adds splats to
  your view model's marker and gloves, mapped from the marker hitbox's frame into the view model's.
- **The provisional splat** (§16.8 promised it; it isn't built). `ClientSession` remembers its predicted breaks on
  players. When the server's word on that shot differs, it raises `SplatWithdrawn`, and the splat systems, which gain
  per-splat removal, take the splat off.
- **The fallback.** If the stress mode shows decals over budget, world splats become cards in a ring-buffer MultiMesh
  drawn like the old paint, on flat surfaces only (a splat across an edge is skipped).
- **Data:** `presentation.jsonc` → `splats` (`drying_s`, `paintSlots`, `spatterReach_m`).

### 17.5 The field

- **Data.** `levels/sports_ground.jsonc` is a level: the ground, the nets as walk-only bounds, the pits and the banners.
  It also has a `field`:
  - its size, the start boxes and the buzzer stations;
  - `layouts`, each a list of the bunkers on one half (type, position, turn, tags), with a `symmetry`: `mirror` across
    the halfway line, or `rotate` 180°;
  - `lanes`: the segments runners cross.

  The factory places each layout's bunkers and their twins as kit props.
- **Bunkers** (`kit/bunkers.jsonc`): each type is a list of primitives in the `nylon` material, whose `inflatable`
  surface is already in `break_model.jsonc` (and has its own break sound).
  - The level's pieces gain a `Wedge` and a lying `Capsule` (the sim's collision already has `ConvexShape.Wedge`).
    Walking gets the matching Godot shapes, and `NavGrid` and `CoverSet` learn their tops and sides.
  - Snakes and cakes are 1.2 m tall (the plan's question 6). `CoverSet`'s half-cover rule (a top 1.15 m above the floor)
    then gives every bunker peek points.
- **The look.** `inflatable.gdshader`: a coated-nylon sheen, seam lines and panels from each bunker's UVs, the club's
  colours, and a wobble where it's hit (a vertex offset decaying from the last few hits; presentation only).

### 17.6 A match of rounds, and the breakout

- **`MatchSeries`** (`Pb.Sim/Match`, engine-free) holds the target (`raceTo`), each side's points, the next point's
  setup, and the match's end.
  - **Offline**, `GameSession` holds it across `LevelMain`'s reloads.
  - **Online**, `LobbyHost`'s session score gains the target (`LobbyState.RaceTo`) and ends the match, and then the
    lobby comes back.
- **The countdown.** `MatchPhase.Countdown` comes between `Briefing` and `Live` (`breakout.countdown_s`, 3). Nobody
  moves, and `RefereeCalls` plays the beeps and then the horn at go. Online, it replaces `briefing_s`'s status line for
  these modes.

### 17.7 Speedball rules

- **`SpeedballMode`** (`IMatchMode`):
  - the last side standing wins the point;
  - **the buzzer** (`BuzzerSet`, two stations): a player of the other side holds Interact within `hangReach_m` for
    `hangTime_s`. The hang resets if they let go, move off or go out. Events: `BuzzerHanging`, `BuzzerHung`;
  - at time up, the side with more players in wins the point, and nobody does on a tie (new `RoundEnd` values `Hung` and
    `MoreIn`).
- The movement and marker rules are the same as everywhere: sprinting blocks firing, and slides end crouched.

### 17.8 Speedball bots

- **A `speedball` idle kind**, with archetypes by the layout's tags: `front`, `mid` and `back`.
- **Breakout.** At go, `BotSquad` deals each bot a bunker by its role and side (claimed, as cover is). The bot sprints to
  it, on a new `Sprint` gait (bots have never sprinted), firing down a lane on the way if someone is crossing it.
- **Hold.** The cover cycle as now, with the lanes as directions to watch between targets.
- **Advance.** When its side is ahead by `advanceMargin` players, a bot claims the next bunker forward on its side.
- **Flank.** As now, along the field's sides.
- **Hang.** When no opponent can see the buzzer (by the squad's sight lines), the nearest bot goes and hangs it.
- **Tests** run whole bot-against-bot matches headless on `NavGridMover`, as the compound's bot rounds do.

### 17.9 Capture the flag

- **`FlagState`** generalises `ObjectiveState`'s case to one flag (centre) or two (one per side). Each flag records where
  it is, or who carries it.
- **Scoring points:** on the field, the opposing buzzer station; in a compound area, the side's own starts, within
  `objectives.flagBase_m`.
- **Carrying** follows the case's rules: taking, carrying (`SprintBlocked`), dropping where the carrier goes out, and
  scoring. Either side can pick up the centre flag; with two bases, nobody carries their own.
- **`CaptureMode`:** a capture or the last side standing wins the point. At time up, the side with more players in
  wins it.
- **Events:** `FlagTaken`, `FlagDropped`, `FlagCaptured`.
- **Bots:** attackers take, carry and escort; defenders guard their flag with the objectives' posts and alarms; anyone
  near a dropped flag goes for it.
- **Levels:** a compound area offers it in the places both sides' starts fit (`LevelObjectives.Offers`).

### 17.10 Arcade

- **Respawn in the sim.** `SimWorld.Respawn(id, start)` brings `Alive` and `Present` back, refills the gear
  (`FillWith`), clears the hitbox history, resets the stance, and raises `PlayerRespawned`.
- **`ArcadeMode`** is timed and scored by eliminations, per side or per player. `respawn_s` (3) after someone goes out,
  it asks `SpawnPlanner` for a start out of their opponents' current sight.
- **The shield.** `PlayerState.ShieldUntil` (`respawnShield_s`, 2): a ball breaking on a shielded player leaves paint
  but doesn't put them out. It ends early if they fire.
- **Bots** re-enter through `BotSquad`, with a fresh brain state at the new start.
- **Presentation:** the spectator view until the respawn; your splats cleared when you come back; the kill feed and
  scoreboard run all match.

### 17.11 Network

- **Protocol 4.** Loadouts (about 40 bytes each) travel in:
  - the hello;
  - the lobby's members;
  - each `RosterEntry` for a person. Bots' loadouts are dealt on every copy from the seed, so they aren't sent.

  A copy of protocol 3 is told to update.
- **Snapshots** (`WorldFields`): the round's fields gain:
  - two flags (state, carrier, position);
  - each buzzer station's hang progress;
  - the match's score and target;
  - the arcade scores, from the per-player stats already sent.

  `RoundEnd` takes 4 bits.
- **Events:**
  - `PlayerRespawned`: a joining copy resets its prediction and the puppet's interpolation at the new start, so it's a
    jump, not a glide;
  - the flag and buzzer events;
  - `SplatWithdrawn`, raised on the joining copy itself, never sent.
- **Dedicated server:** `server.jsonc` rotation entries may name the Sports Ground and the new modes, and `RotationDef`
  checks them as it checks the others.
- **As built (M5.3):**
  - `KitCodec` packs a loadout as a flag, the character and, per slot, the item's catalogue index (a var-int) and its
    three colours (24 bits each): about 60 bytes. It goes in the hello (`HelloMessage.Kit`, read only from protocol 4,
    so a protocol 3 copy is still read and told to update), in each `LobbyMember` of the lobby's state, and in each
    person's `RosterEntry`. A change in the locker is a `Kit` message to the host, at any time; it's worn from the next
    round.
  - The host's `LobbyHost` checks every kit against the catalogue (`GearCatalog.Normalised`: an item that isn't one of
    its slot's is the slot's default) and makes the member's character the kit's (and choosing a character moves the
    kit to it). The in-game host and the dedicated server both pass it the catalogue.
  - People are cast in their kit (`Person.Kit`); every copy draws each person in the roster's kit on the roster's
    character (yours too, so you see yourself as the others do), and the lobby's preview shows yours as the host has
    it. Bots' kit is dealt from the seed, as before.
  - Each item's mesh is built once (`GearModels`) and shared by everyone wearing it, colours being instance uniforms, so
    ten players in ten loadouts cost what ten in one did.
  - The `NET round` log line names each person's marker and mask, and CI's networked rounds put Ada, Bo and Cy in
    Vellis's, Quarrow's and Kilnmark's ranges (`--kit=BRAND`) and check every copy's roster has them so.

### 17.12 Testing

- **Sim tests:**
  - the catalogue and loadouts;
  - the field's mirroring and its bunkers' shapes;
  - the new rules: speedball, the match series, capture the flag, arcade;
  - bot-against-bot matches on both layouts.
- **Net tests:** loadouts through the protocol; each new mode's rounds through the in-memory network, ending with the
  server's result; respawns.
- **Smoke tests:** the locker opens and saves; a bot match on the field in each new mode; `MainMenu`'s tour shows the
  locker.
- **CI's networked rounds** (`tools/ci/net-round.jsonc`) gain a speedball match, a flag round and an arcade match.
