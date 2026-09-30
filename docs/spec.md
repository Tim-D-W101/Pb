<!--
  Verbatim build prompt, received 2026-09-30. This is the requirements source of truth.
  Answers to the Context block and all design decisions are tracked in architecture.md §0.
-->

# Build Prompt: First-Person Tactical Paintball Game

> Fill in the Context block, then paste everything below into your AI coding agent (Claude Code, Cursor, etc.).

---

## Context (fill in before sending)

```
Game name:                 [ ]
Engine:                    [ ]   (recommended: Godot 4.x — text-based scenes, easy for an AI agent to edit)
My experience level:       [ ]
Target platforms:          [ ]   (e.g. PC first, controller support)
Multiplayer scope:         [ ]   (e.g. listen server first, dedicated servers later)
Art approach:              [ ]   (e.g. greybox → original low-poly assets)
Timeline / hours per week: [ ]
Budget for assets/tools:   [ ]
```

---

## Role & goal

You are a senior game developer. Build an **original** first-person competitive paintball game with realistic projectile ballistics, one-hit eliminations, and round-based team play on inflatable-bunker fields. Work in phases. Each phase must end with a playable build and pass its acceptance checks before you start the next.

## Ground rules

- **Original IP only.** No real brand names, logos, licensed equipment, or names of existing paintball games. Invent all gear, brands, currency and map names.
- **Metric units** everywhere in code and UI (m, m/s, g, bar, L).
- **All gameplay values in data files** (resources/JSON), never hard-coded. I will tune them.
- **Plan before code.** Give me an architecture plan first and wait for my OK.
- **Decoupled systems:** ballistics, weapons, player controller, match rules, networking, AI, UI, audio.
- **Automated tests** for ballistics and match-rule logic.
- After each phase: what was built, how to run it, known issues, what's next.

---

## 1. Core simulation

### 1.1 Paintball ballistics (the heart of the game)

Paintballs are physical projectiles — never hitscan.

- Defaults: diameter 17.3 mm (.68 cal), mass 3.2 g, muzzle velocity 88 m/s, drag coefficient 0.47, air density 1.225 kg/m³, gravity 9.81 m/s².
- Quadratic drag: `a = −(0.5·ρ·Cd·A / m)·|v|·v + g`. Integrate at a fixed tick (≥ 120 Hz, semi-implicit Euler or RK2).
- Continuous collision: sweep between previous and current position every tick so balls never tunnel through thin inflatables.
- **Validation targets** (fired level from 1.5 m height at 88 m/s). Write a unit test that checks these within ±10%:
  - Drop at 20 m ≈ 0.35 m, speed ≈ 58 m/s
  - Drop at 30 m ≈ 0.92 m
  - Max range ≈ 93 m at ~30° elevation
- Shot dispersion: random cone (default 0.6° half-angle) plus velocity variance of ±1.5 m/s per shot.
- **Break vs bounce:** on impact the ball breaks with a probability based on impact speed and angle (fast and head-on ≈ certain break; slow or glancing = likely bounce). Bounced balls keep reduced velocity and do **not** eliminate. Expose the curve as data.
- Pool projectiles. Support 1,000+ live balls without frame drops.

### 1.2 Hits & elimination

- A ball that **breaks** on any player hitbox (body, mask, marker, loader, tank) eliminates that player for the round.
- Eliminated players raise their marker, switch to spectator camera, and can't interact.
- Hit registration is server-authoritative (see §5).

### 1.3 Paint splatters

- On break: team-coloured splat decal on bunkers, terrain, players, and the first-person marker.
- Decal pool with a cap (default 2,000); oldest fade first. Player splats clear at round end.
- Balls breaking within 0.5 m of a player's face spray paint on their mask, briefly reducing visibility (tunable).

### 1.4 Marker & air system

- Fire modes: semi-auto and ramping, capped at a tunable rate (default 10.5 balls/s).
- Loader holds 200 balls. Player carries 3 pods × 140 balls. Refilling from a pod takes 2.5 s, is interruptible, and blocks firing.
- Air tank: 1.1 L at 310 bar. Each shot consumes pressure; below a threshold, muzzle velocity drops. Default fill ≈ 1,000 shots.
- Gear variants are cosmetic by default. Any stat difference must be small and data-driven.

### 1.5 Movement & stance

Defaults (tunable): walk 3 m/s, run 5.5 m/s, sprint 7 m/s, crouch-walk 2 m/s.

- Sprint, crouch, **slide/dive into bunkers** (keeps momentum, ends in crouch).
- **Lean** left/right (~20°) and **shoulder-swap** the marker to minimise exposure when peeking.
- **Snap-shooting:** fast peek → fire → return must feel responsive.
- Sprinting blocks firing. Firing while moving widens dispersion.
- FOV default 90°, adjustable 70–110°. Minimal head-bob.

---

## 2. Fields & maps

### 2.1 Speedball field (build first)

- Flat, symmetrical tournament field, 45 m × 36 m, netted boundary. Balls leaving the field despawn.
- Inflatable bunkers (approximate heights, all in data): Dorito ~1.2 m, Snake ~0.9 m, Can ~1.5 m, Temple ~1.5 m, Cake ~1.1 m, Brick ~1.8 m.
- Layouts defined in a data file (bunker type, position, rotation), auto-mirrored for symmetry, so new fields are trivial to add.
- Start boxes on each back line. Flag/buzzer at the opposing start.

### 2.2 Scenario / woodsball map (later phase)

- Asymmetric, ~150 m × 100 m, trees, wooden structures, urban props, elevation changes.

---

## 3. Game modes

| Mode | Rules |
| --- | --- |
| **Speedball** (primary) | 5v5, round-based. Win a round by eliminating all opponents or hanging the opposing flag/buzzer. 3 min round timer. Match = race to N rounds (default 4). Horn countdown at every breakout. |
| **Capture the Flag** | Centre-flag or two-base variant. Carry flag to scoring point; carrier eliminated → flag drops. |
| **Arcade** | 3 s respawn, timed match, score by eliminations. |
| **Solo / Co-op vs AI** | Any mode with bots filling empty slots. |

---

## 4. AI bots

- Behaviour tree or state machine: **Breakout** (sprint to assigned bunker) → **Hold** (peek, snap-shoot, shoot lanes across known running gaps) → **Advance** (move up on numbers advantage) → **Flank** → **Hang flag**.
- Bots use the same ballistics and rules as players. They lead targets with a predicted trajectory plus difficulty-based error.
- Difficulty tiers (Easy/Normal/Hard) vary reaction time, aim error and decision speed.
- Navmesh on every field. Cover points auto-generated from bunker data (each bunker exposes left/right/top peek positions).

---

## 5. Multiplayer

- Server-authoritative. Dedicated server build plus listen server for testing.
- Client-side prediction and reconciliation for movement; interpolation for remote players.
- Projectiles: clients simulate cosmetically from a shared per-shot seed; the server simulates the authoritative ball and decides hits, with lag compensation (rewind hitboxes to shooter's time, capped at 200 ms).
- 60 Hz network tick, 120 Hz physics tick.
- Lobby: create/join, team slots with auto-balance, loadout preview, ready-up, map vote, countdown.
- Server validates fire rate, ammo, air pressure and movement speed.
- Platform services behind an abstraction layer so crossplay can be added later.

---

## 6. UI / HUD

**In-match**
- First-person view model with marker, loader and tank clearly visible.
- Ammo: loader count + pods remaining. Air gauge in bar.
- Top bar: alive/eliminated icon per player, both teams.
- Round timer, score, objective markers (flag/buzzer).
- Toggleable minimal crosshair. Elimination feed.

**Menus**
- Main menu, settings (graphics, audio, rebindable controls, FOV, sensitivity, colourblind-safe team colours), gear locker, lobby.

---

## 7. Gear locker & customisation

- 3D room where the player can rotate and inspect items.
- Slots: marker, loader, tank, mask, jersey, pants.
- Primary/secondary/accent colour per item.
- All items are original designs from fictional brands you invent.
- Loadout saved to player profile (local file first, server later).

---

## 8. Audio

- Shot sound that changes with tank pressure; distinct break sounds on inflatables, wood, turf and players; bounce sounds; footsteps per surface; breakout horn; flag buzzer.
- Positional 3D audio so distant fire gives directional awareness.

---

## 9. Progression & economy (final phase, optional)

- XP from matches → levels → cosmetic unlocks.
- Seasonal track with free and premium tiers. **Cosmetic only — no gameplay advantage.**
- Soft currency earned in play; premium currency named by me.
- Stub the store with mock purchases. No real payment integration until I ask.

---

## 10. Technical targets

- 60 fps at 1080p on a GTX 1070-class GPU with 16 GB RAM; 144 fps on an RTX 3080-class GPU.
- Mouse & keyboard plus gamepad (Xbox and PlayStation layouts), fully rebindable.
- Install size under 10 GB.

---

## 11. Build phases & acceptance criteria

**Phase 1 — Ballistics sandbox**
- Player controller, marker, flat range with targets at 10/20/30/40 m.
- ✅ Ballistic unit tests pass · 1,000 live balls at 60 fps · visible arc and drop · break/bounce works.

**Phase 2 — Offline speedball vertical slice**
- Greybox field loaded from data, full movement (lean, slide, shoulder-swap), HUD, round rules, 5v5 vs bots.
- ✅ Complete a race-to-4 match vs bots · bots break out, hold cover and eliminate players.

**Phase 3 — Multiplayer**
- Dedicated + listen server, lobby, 10 players.
- ✅ Hits feel fair at 100 ms simulated latency · no score desync · server rejects over-rate fire.

**Phase 4 — Modes & maps**
- CTF, Arcade, co-op vs AI, second speedball layout, scenario map.

**Phase 5 — Customisation & art pass**
- Gear locker, original gear models, splat shaders, audio, full settings menus.

**Phase 6 — Progression & economy (optional)**
- XP, seasonal track, mock store.

---

## Your first response

1. Up to 5 clarifying questions — only where the Context block leaves something ambiguous.
2. Architecture plan and folder structure.
3. Phase 1 task list.

Then wait for my go-ahead.
