# Phase 2 — The compound: plan

> **Status: approved 2026-10-01 (all defaults accepted); in progress.** At your request (2026-09-30) this replaces the spec's speedball Phase 2. Technical design: [architecture.md §14](architecture.md#14-direction-change-2026-09-30-the-compound). Everything else in [spec.md](spec.md) still applies: ballistics, paint rules, gear, metric units, original IP, data files, tests.

## Your direction

> "…use Higgsfield to design the graphics and make it more realistic. I want the setting to be like old abandoned buildings in a compound. We can go around exploring and then you fight different people. You may have up to 10 people in one level, in one round, and then you have different levels depending on the difficulty."

How I've read it:

| You said | What it becomes |
|---|---|
| Old abandoned buildings in a compound | Each level is a derelict compound: warehouse, offices, guardhouse, yard. Buildings come from a modular kit, so a level is a data file plus art. |
| Go around exploring, then fight | There are no start boxes or horn. You come in at the gate. Opponents are spread through the buildings, patrolling or holding posts, and they don't know where you are until they see or hear you. Paint pickups reward exploring. |
| Different people | Several distinct opponent characters, each with a callsign and one of several behaviours. |
| Up to 10 people in one level, in one round | You plus up to 9 opponents. One level is one round: eliminate everyone to clear it. |
| Different levels depending on difficulty | A ladder of levels that get harder: more opponents, sharper aim, faster reactions, bigger maps. Each level is playable on Easy, Normal or Hard. |
| Realistic graphics with Higgsfield | Photographic textures, generated 3D props and rigged characters, weathering, real lighting and atmosphere. |

## Goal

One complete level that looks the part: an abandoned industrial compound you explore on foot, against up to 9 opponents who patrol, hear you, take cover, flank and eliminate you. The paintball rules and ballistics proven in Phase 1 stay exactly as they are: one hit eliminates, and a bounce never does.

## What you'll be able to do

- Choose **Level 1** and a difficulty: Easy (4 opponents), Normal (6) or Hard (9, so ten people in the round).
- Start at the main gate and explore at your own pace: guardhouse, a yard with rusted containers and a burnt-out car, a two-storey office block, a warehouse with a mezzanine, a pump house.
- Find paint pods along the way. Opponents carry limited paint too and have to refill.
- Sneak (crouching is quieter), lean around door frames, swap shoulders, slide into cover, climb stairs, hop small obstacles.
- Face opponents who patrol, investigate noises, call contacts (as subtitles for now), hold angles, flank and push.
- One hit eliminates, you or them. A hit opponent calls "Hit!", raises their marker and walks off. If you're hit, you see who got you.
- Clear the level by eliminating everyone. If you're hit or time runs out (15 minutes by default), it's over. A summary shows time, shots, accuracy and eliminations, then offers Retry or Level select.
- Keep practising on the Phase 1 range, which becomes the training ground in the menu.

## Level 1: Oxbarrow Works (working name)

About 110 × 80 m. Every name is a placeholder in data, so rename anything.

```
                                  N
  +------------------------------------------------------------------+
  |  perimeter wall, 3 m high, collapsed in two places               |
  |   +------------ WAREHOUSE 40 x 24 m -------------+   PUMP        |
  |   |  mezzanine on two sides, loading bays,       |   HOUSE       |
  |   |  half the roof fallen in                     |   8 x 6 m     |
  |   +----------------------------------------------+               |
  |                                                                  |
  |   [container] [container]   YARD: pallets, car wreck,  WATER     |
  |   [container]               sandbags, weeds, puddles   TOWER     |
  |                                                        (landmark)|
  |   +---------- OFFICE BLOCK 26 x 12 m, 2 floors ----+             |
  |   |  corridor, offices, stairwell, broken windows  |             |
  |   +------------------------------------------------+             |
  |   GUARD-                                                         |
  |   HOUSE            ====== MAIN GATE (you start) ======           |
  +------------------------------------------------------------------+
```

## How it will look

Realism comes from four things:

1. **Materials.** Photographic, tileable textures (concrete, brick, peeling paint, rusted sheet metal, cracked asphalt, gravel, wood) generated with Higgsfield. Normal and roughness maps derived from them let surfaces catch the light properly. A weathering shader adds grime at the foot of walls, water stains and moss.
2. **Props.** Oil drums, pallet stacks, a burnt-out car, a generator, cable reels, tyres and old office furniture, generated as 3D models with Higgsfield (image to 3D, with PBR materials). Each has a simple invisible collision shape in data, so paint physics stays exact and fast.
3. **People.** Three distinct opponent characters, generated as rigged 3D models with walk, run, crouch and idle animations, plus team armbands as worn at real scenario games. Their arms are posed on the marker procedurally, because the animation library has few weapon clips.
4. **Light and air.** A low sun under an overcast sky, ambient occlusion and bounced light, dim interiors lit through windows and holes in the roof, dust in the air, weeds pushing through cracks. Graphics presets (Low, Medium, High) keep a GTX 1070 at 60 fps on Medium.

The buildings themselves (walls, floors, stairs, roofs) are generated from the level file. That keeps layouts editable and paint collision exact. Big simple shapes such as containers and concrete barriers are generated too.

**Concept art so far** is in your Higgsfield project:

- an establishing shot of the compound;
- a first-person view of the yard;
- an opponent in A-pose, the source for the rigged model;
- a concrete texture;
- an oil drum, the source for a test prop.

**Higgsfield budget** (you have 541 credits):

| Item | Credits |
|---|---|
| 3 opponent characters, rigged (38 each) | 114 |
| About 6 animation clips (8 each), shared by all three characters | 48 |
| 6 props (30 each) | 180 |
| First-person marker, loader and tank | 30 |
| About 12 textures, a sky, concept images | 20 |
| **Total** | **≈ 390** |

Sharing clips depends on the three rigs matching, which the first test will show. If they don't, clips cost about 100 more, and I'd drop to two characters or fewer props rather than go past 400. Spending happens in two steps: one prop and one character first (≈ 75 credits) to prove the whole pipeline, then the rest.

Higgsfield's audio tools only generate speech. They can voice the opponents' callouts ("Hit!", "Contact, office!") in Phase 3. Other sounds stay synthesised for now.

## Scope

**In:**

- Level kit and Level 1.
- Realistic materials, lighting, props and characters.
- Full movement.
- Player hitboxes, eliminations, spectator view and mask spray.
- Opponents with three behaviours and three difficulty tiers.
- Round rules, pickups and stats.
- Main menu, level select, briefing, pause menu and summary.
- Match HUD, tests and report.

**Out (later phases):**

- More levels, plus unlocks and saving for the ladder (Phase 3).
- Voiced callouts and the audio pass (Phase 3).
- Doors that open and close (Phase 3).
- Multiplayer (Phase 4).
- Gear locker, the speedball field and other modes (Phase 5).
- Progression (Phase 6).

## Milestones

Each milestone is pushed as it lands, with screenshots.

### M2.1 Level kit and greybox compound

**Done 2026-10-01.** [Screenshots](reports/phase-2/) (software-rendered in the cloud container, so colours are right but frame rates mean nothing).

- [x] Level file format (`levels/*.jsonc`):
  - buildings made from wall runs with door and window openings, floors with stairwell holes, stairs, roofs and columns;
  - props, pickups, spawn points, patrol routes and named areas.
  - Added along the way: open railings (`"style": "railing"`; paint and sight pass between the rails), roofs with a separate ceiling material, and camera viewpoints used for screenshots.
- [x] One definition generates the visible mesh, walking collision, paint colliders, occluders and navigation geometry, so they can never disagree. The navmesh itself is baked in M2.5, from the same walkable primitives.
- [x] Kit catalogue (`kit/*.jsonc`):
  - building templates (guardhouse, pump house, office block, warehouse);
  - 27 prop types with proxy colliders;
  - a material library giving each material's texture, tile size and paint-physics surface (29 materials). Until M2.6 brings textures, a shader draws each material procedurally: brick, block, peeling plaster, corrugated sheet, rust, cracked asphalt, and so on, weathered with grime, moss and rain streaks.
- [x] New surfaces in `break_model.jsonc`: concrete, brick, plaster, sheet metal, glass, tarp, tyre rubber, dirt, gravel (plus asphalt).
- [x] Greybox Level 1 you can walk through, including both office floors and the mezzanine. The level smoke test in CI walks in through the gate firing, then climbs every flight of stairs to its landing.
- [x] Paint collision checked against multi-storey geometry, staying within the per-tick budget. Long shots are swept in chunks and tested against a brute-force reference. With ~1,000 balls in flight over Oxbarrow Works the sim costs 0.13 ms per tick (0.26 ms per 60 fps frame), against 0.10 ms on the range.

Also: a distant tree line and fog-matched sky so the horizon never shows an edge, overcast late-afternoon lighting, and three graphics presets (F12). F5 now opens the compound; the range is `scenes/Range.tscn`.

### M2.2 Movement

- [ ] Lean left and right, with a wall check.
- [ ] Shoulder swap. The muzzle genuinely moves to the other side.
- [ ] Slide or dive into a crouch.
- [ ] Snap-shooting timings.
- [ ] A small jump, stairs, and a headroom check before standing up from a crouch.
- [ ] Muzzle-in-cover rule: a ball fired while the barrel is behind a wall edge breaks on that wall.
- [ ] The surface underfoot is tracked, for footstep noise now and footstep audio later.
- [ ] All values in `movement.jsonc`.

### M2.3 Players, hitboxes, eliminations

- [ ] Hitbox rig in `Pb.Sim`:
  - parts: mask, head, torso, arms, legs, marker, loader, tank;
  - posed by stance, lean, shoulder and aim;
  - 200 ms of history kept for multiplayer later.
- [ ] Elimination rules:
  - a break on any lethal part eliminates;
  - bounces never do;
  - balls already in the air still count after their shooter is hit, as in real paintball (data toggle).
- [ ] Eliminated opponents call "Hit!", raise their marker and walk out. When you're hit, a short spectator view shows who got you, then the summary appears.
- [ ] Mask spray when a ball breaks near your face (spec §1.3).
- [ ] Splats stick to characters as they move.

### M2.4 Round rules and level flow

- [ ] Round flow in `Pb.Sim/Match`: briefing → live → round end (cleared, eliminated or time up) → summary. Mode rules plug in, so free-for-all or squads are data changes later.
- [ ] Time limit, paint pickups, and stats (time, shots, hits, accuracy, eliminations).
- [ ] Menus:
  - main menu;
  - level select with difficulty;
  - briefing card;
  - pause menu with basic settings (FOV, sensitivity, graphics preset, volume);
  - summary screen.
- [ ] Level ladder file (`levels/ladder.jsonc`) listing levels, difficulty tiers, opponent rosters, time limits, starting gear and pickups. Phase 2 ships Level 1 and shows later levels as locked.

### M2.5 Opponents

- [ ] Bots produce `InputCommand`s and use the same marker, paint, ballistics and rules as you.
- [ ] Senses:
  - sight: a vision cone plus a detection meter that fills faster up close, in the light, and when you stand or move;
  - hearing: shots, breaks and sprinting footsteps, muffled by walls;
  - memory of your last known position.
- [ ] Behaviour: patrol → suspicious → investigate → engage from cover → flank or push → search. Engaging means peeking, snap-shooting and refilling behind cover.
- [ ] Three behaviours in data:
  - **Sentry** holds a post;
  - **Patroller** walks a route;
  - **Rusher** is aggressive and fires on the move.
- [ ] Aim uses the shared trajectory solver: it leads moving targets and allows for drop, plus difficulty-based error.
- [ ] Behaviours in `bots/archetypes.jsonc`. Difficulty tiers (Easy, Normal, Hard) in `bots/difficulty.jsonc`: reaction time, aim error, tracking lag, decision speed, sight range and aggression. Opponent counts per level and tier are in the ladder file.
- [ ] Navigation mesh baked when the level loads. Cover points are generated from the level geometry: door frames, window edges, wall ends and props.
- [ ] Debug overlay: sight cones, detection meters, states, paths, cover points.

### M2.6 Art pass (Higgsfield)

- [ ] Import pipeline (`tools/art`):
  - pull a finished Higgsfield job, make textures tileable, derive normal and roughness maps, and resize;
  - record the job ID and prompt in `game/data/assets.jsonc`, as a provenance record that the art is original.
- [ ] Materials and the weathering shader across the whole level. Lighting presets, dust, light shafts and weeds.
- [ ] Props in place with their proxy colliders.
- [ ] Three rigged, animated opponents, marker in hand, armbands in their team colour.
- [ ] Realistic first-person marker, loader and tank.
- [ ] Everything falls back to greybox when an asset is missing, so the game and CI never depend on the art.

### M2.7 HUD

- [ ] Top bar with an in/out icon per opponent (spec §6).
- [ ] Time left, kill feed with callsigns, subtitled callouts, pickup prompts, hit confirmation, mask-spray overlay.
- [ ] The Phase 1 gear panel stays.

### M2.8 Verify and report

- [ ] CI green, including a headless match on Level 1: a bot takes your slot and plays to the end without errors.
- [ ] Benchmark: a sim tick with 10 players and 1,000 live balls in ≤ 0.5 ms.
- [ ] `docs/reports/phase-2.md` with screenshots.
- [ ] Your check: ≥ 60 fps on Medium with 10 people, and how the opponents feel.

## Tests

| Test | Pass criterion |
|---|---|
| Level kit | Openings are exactly where the file says: a ball through a window passes, and one into the wall beside it hits. Walking, paint and navigation geometry agree. Bad references fail with the file and key named. |
| Hitbox rig | Stance, lean and shoulder swap move the right parts by the configured amounts. A lean exposes the head and marker on that side only. |
| Eliminations | A break on any lethal part eliminates. Bounces never do. Eliminated players can't fire. Balls already in the air still count, and the toggle is respected. |
| Round rules | Cleared when the last opponent is out. Lost when you're out or time expires. Trades on the same tick follow the data rule. Pickups respect pod limits. Stats add up. |
| Bot senses | No sight through walls. Detection takes longer at range, in the dark, and when you're crouched and still. Shots within range are heard, and less so through walls. |
| Bot aim | With marker spread and added error both off, the solver's shots land within 5 cm of a still target's centre at 10–40 m, and within 20 cm of a target walking across at 20 m. |
| Difficulty | Easy is never faster or more accurate than Normal, and Normal never more than Hard. |
| Determinism | Same seed and inputs give an identical match outcome. |
| Performance | The sim step stays allocation-free with 10 players, and the benchmark stays within budget. |

## Acceptance checks

| Check | How it's shown |
|---|---|
| Clear Level 1 on Normal: explore, pick up paint, and eliminate 6 opponents who patrol, investigate, take cover and flank | You play it; the report includes a recorded scripted run |
| 10 people in one round (you + 9 on Hard) at ≥ 60 fps on a GTX 1070-class GPU at the Medium preset | Your PC, with the perf overlay |
| Different levels by difficulty | Level select offers Level 1 on three tiers. The ladder file lists the levels coming in Phase 3. |
| Rules and ballistics tested | All tests green in CI, including the headless bot match |
| Realistic look | Higgsfield textures, props and characters in the level |

## Revised roadmap

| Phase | Was (spec) | Now |
|---|---|---|
| 2 | Offline speedball slice | **The compound** (this plan) |
| 3 | Multiplayer | **Level ladder:** 3–4 more compound levels of rising difficulty, unlocks and a local save, Marksman and Flanker behaviours, objectives (retrieve an item, hold a room), audio pass with voiced callouts, full settings and key rebinding |
| 4 | Modes and maps | **Multiplayer:** up to 10 players, co-op against bots on the compound levels and player vs player |
| 5 | Customisation and art | **Locker and extras:** gear locker, fictional brands, gear models, splat shaders; the speedball field, CTF and Arcade as optional modes |
| 6 | Progression (optional) | Unchanged |

The sim stays ready for multiplayer throughout, so moving it later costs nothing.

## Questions (defaults in bold)

1. **Sides.** **You alone against up to 9 opponents.** The alternatives are free-for-all (everyone against everyone, so opponents fight each other too) or squads (you with AI teammates). Each is a small rules change, so the others can come later.
2. **Speedball.** **The compound replaces speedball as the main game.** The inflatable field becomes an optional mode in Phase 5.
3. **Multiplayer.** **Still wanted, moved to Phase 4**, after the level ladder.
4. **Higgsfield spend.** **Up to 400 of your 541 credits on Phase 2 art**, starting with the ≈ 75-credit pipeline test.
5. **Mood.** **Overcast late afternoon.** The alternatives are bright midday, dusk or rain.
