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

**Done 2026-10-01.** [Screenshots](reports/phase-2/) of the shoulder swap and lean at a wall corner (`-- --posture-demo`).

- [x] Lean left and right (Q/E), with a wall check: the head is a sphere swept from upright to leaned, and the lean stops short of whatever it would hit. The eye and the marker move with the body; the view rolls with part of the lean.
- [x] Shoulder swap (X or middle mouse). The muzzle genuinely moves to the other side, and the marker can't fire while it's crossing over.
- [x] Slide into a crouch (V, or crouch while sprinting): a burst of speed, friction, a little steering, and you can shoot once the marker is up.
- [x] Snap-shooting timings: lean out 0.16 s and back 0.12 s, swap 0.3 s, and 0.15 s from sprint to firing.
- [x] A small jump (Space, about 0.5 m), stairs (climbed end to end in CI), and a headroom check before standing up from a crouch.
- [x] Muzzle-in-cover rule: a ball fired while the barrel is behind a wall edge breaks on that wall. To shoot round a left-hand edge, swap to the left shoulder.
- [x] The surface underfoot is tracked, and footsteps, slides, jumps and landings emit noise events whose hearing radius depends on pace and surface (metal and gravel carry, grass and dirt don't). Bots will listen for them in M2.5; footstep audio comes later.
- [x] All values in `movement.jsonc`.

Bots will use the same body as the player (`PawnBody`), so they move by exactly these rules. The sim tests cover every rule above, and the level smoke test now also sprints, slides and jumps in the real scene.

### M2.3 Players, hitboxes, eliminations

**Done 2026-10-01.** [Screenshots](reports/phase-2/) from `-- --duel-demo`.

- [x] Hitbox rig in `Pb.Sim` (`hitboxes.jsonc`):
  - parts: mask, head, torso, arms, legs, marker, loader, tank, all boxes;
  - posed by stance, lean, shoulder and aim;
  - 200 ms of history kept for multiplayer later.
  - Until models arrive (M2.6), characters are drawn from these same boxes, so what you see is what you can hit.
- [x] Elimination rules:
  - a break on any lethal part eliminates (`lethalParts`; all of them by default);
  - bounces never do;
  - balls already in the air still count after their shooter is hit, as in real paintball (`ballsInFlightCount`).
- [x] Eliminated opponents call "Hit!", raise their marker and walk out; a toast tells you who you got, where and from how far. When you're hit, a short spectator view shows who got you, the part and the distance. Since M2.4, the round summary follows.
- [x] Mask spray when a ball breaks within 0.5 m of your face (spec §1.3): drops of that paint colour on your goggles, as strong as the break was close, clearing over 2.5 s.
- [x] Splats stick to characters as they move, on the part they hit.

Until the bots of M2.5, practice opponents stood at the opponent spawns, and the sentries among them shot back. The level smoke test now also shoots one (it must go out and walk off) and lets a sentry eliminate the player (the spectator view must start). Player hitboxes are off on the Phase 1 range, as before.

### M2.4 Round rules and level flow

**Done 2026-10-01.** [Screenshots](reports/phase-2/) of the menus (`-- --menu-tour`) and of a round from briefing to summary (`-- --round-tour`).

- [x] Round flow in `Pb.Sim/Match`: briefing → live → round end (cleared, eliminated, traded or time up) → summary. Mode rules plug in through `IMatchMode`; Phase 2's `solo` mode is you against everyone (`rules.jsonc`), so free-for-all or squads are data changes later.
  - When an end condition is reached, the round waits up to 1.5 s for balls still in the air, so a shot fired as you go down still counts. You and the last opponent out together is a trade (`tradeCountsAsClear` decides whether that's a clear).
  - During the briefing you can look around, but you can't move or fire, and the clock doesn't run.
- [x] Time limit, paint pickups, and stats (time, shots, hits, accuracy, eliminations, pickups).
  - Pods go into an empty pod slot only, so you never carry more than the harness holds. Air tops up a tank that's below 90 %. Oxbarrow Works has seven pickups (six pods, one tank).
  - In a round there's no gear reset (F6 now works on the range only): you find pickups instead, and the HUD says so when you run dry.
- [x] Menus, built in code from one shared kit, usable with mouse, keyboard or pad:
  - main menu (Play, Training ground, Settings, Quit);
  - level select with difficulty, showing what each tier means (clock, pods, pickups);
  - briefing card;
  - pause menu (Esc or Start) with basic settings (FOV, sensitivity, invert Y, graphics preset, v-sync, volume);
  - summary screen (Retry, Level select, Main menu).
- [x] Level ladder file (`levels/ladder.jsonc`) listing levels, difficulty tiers, opponent rosters, time limits, starting gear and pickups. Phase 2 ships Level 1 and shows later levels as locked. Easy has 4 opponents, Normal 6 and Hard 9 (ten players in the round, counting you).

F5 now opens the main menu (`scenes/Main.tscn`). Seven sim tests cover the round flow: the briefing gate, each outcome, the stats, the settle window, time up, pickups, tier rosters, and a live round's tick not allocating. The level smoke test now also checks that being hit ends the round as eliminated. CI gains a menu smoke run that checks every playable tier is offered. Opponents are still the practice dummies until M2.5 replaces them with bots.

### M2.5 Opponents

**Done 2026-10-01.** [Screenshots](reports/phase-2/) from `-- --bot-demo`: bots fighting from cover, seen with the debug overlay and through your own eyes.

- [x] Bots produce `InputCommand`s and use the same marker, paint, ballistics and rules as you. They move with the same body (`PawnBody`) and can pick up pods when they run dry.
- [x] Senses (`bots/senses.jsonc`):
  - sight: a 150° vision cone plus a detection meter that fills faster up close, in the light, in the middle of the view, and when you stand or move; partly hidden counts for less;
  - hearing: shots, balls breaking close by (they're under fire), and footsteps (each carries its own range from `movement.jsonc`), muffled by walls;
  - memory of your last known position, forgotten after 25 s;
  - someone already spotted who reappears within 4 s (a peek round cover) takes a third of the reaction time.
- [x] Behaviour: patrol or post → suspicious → investigate → engage from cover → flank or push → search → return.
  - Engaging means shooting from the open on first sight, then moving to cover and cycling: hide (crouch, refill), then peek and snap-shoot.
  - Peeking stands up over low cover, or steps past an edge, leaning and swapping to that shoulder so the muzzle-in-cover rule doesn't stop the shot.
  - They won't fire into cover, through a teammate, or before they've reacted.
  - Eliminated, they walk to the dead zone along a path.
- [x] Three behaviours in data:
  - **Sentry** holds a post;
  - **Patroller** walks a route;
  - **Rusher** is aggressive and fires on the move.
- [x] Aim uses the shared flat-fire maths: it leads moving targets by the time of flight and holds over for drop. Difficulty adds an error that starts as a snap shot and settles while the target stays steady.
- [x] Behaviours in `bots/archetypes.jsonc`. Difficulty tiers (Easy, Normal, Hard) in `bots/difficulty.jsonc`: reaction time, aim error, tracking lag, decision speed, sight range, detection, hearing, turn speed, trigger rate and aggression. Shared timings are in `bots/brain.jsonc`. Each ladder tier names its bot difficulty, and opponent counts per level and tier are in the ladder file.
- [x] A navigation grid is built when the level loads (about 0.2 s), and cover points are generated from the level geometry: door frames, window edges, wall ends and props. Oxbarrow Works has 891 cover points.
  - The grid is built in `Pb.Sim` from the same walkable primitives as everything else, instead of a Godot navigation mesh. It's engine-free and deterministic, so CI checks paths on the real level and whole fights run in sim tests without Godot.
  - It reaches every spawn, patrol point and pickup, both upper floors and the 0.9 m guardhouse door.
- [x] Debug overlay (F3 in a level): sight cones coloured by the detection meter, mode labels, paths, held cover, and every cover point.

Thirty new sim tests cover the grid on the real level, cover generation, and bots in headless fights: spotting, hearing (and not through walls), investigating, patrolling, fighting from cover, refilling, rushing, difficulty, walking off, determinism, and a nine-bot squad thinking without allocating. They include the plan's criteria: the aim solver lands within 5 cm of a still target at 10–40 m, nobody is seen through a wall, detection is slower at range, in the dark, crouched and still, and each tier is at least as good as the one below. The level smoke test now runs against real bots. Nine Hard bots in a fight cost about 0.02 ms of brain time per tick (`tools/Pb.Bench`).

Known gaps: bots don't jump, slide or climb anything but stairs, and their teamwork stops at not taking each other's cover. A long cross-map path search can take around 10 ms in the cloud container, so searches are spread out to one per tick.

### M2.6 Art pass (Higgsfield)

**In progress.** The container still can't reach Higgsfield's file host, so the parts that need no downloads came first. [Screenshots](reports/phase-2/) from `-- --shots`.

- [x] Import pipeline (`tools/art/import.sh`, which runs `game/tools/ArtImport` headless). It's ready, but hasn't run on real Higgsfield files yet, since their host is blocked:
  - pull a finished Higgsfield job, make textures tileable, derive normal and roughness maps, and resize;
    - the picture is cropped square and resized;
    - broad uneven lighting is evened out, so tiles don't repeat a light and dark pattern;
    - seams are blended away within a band at each edge, keeping the contrast;
    - normal and roughness maps are derived from the brightness;
    - models are copied in and measured, so the prop's colliders can be fitted to them;
  - record the job ID and prompt in `game/data/assets.jsonc`, as a provenance record that the art is original. A sim test fails if the kit uses a texture or model without a record. CI runs the texture steps on a generated picture (`--selftest`).
- [ ] Photographic materials across the whole level. Until they arrive, the procedural weathering shader covers every surface.
- [x] Lighting presets, dust, light shafts and weeds (`presentation.jsonc`: "weeds", "shafts", "dust", "windowLight" and the graphics presets):
  - **Weeds and dry grass** wherever rain falls: about 50,000 tufts in Oxbarrow Works, placed at load from the level and seeded by its id. They're thick on the scrubland, patchy on dirt and gravel, in a network of cracks on asphalt and concrete, along the foot of walls and props, and under the holes in the warehouse roof. They sway in rolling gusts, shrink into the ground with distance, and never grow over a pickup.
  - **Sunbeams** through every window, door and roof hole the sun reaches into a roofed space (38 in Oxbarrow Works). Each is a box of light, ray-marched per pixel, that stops at whatever is in front of it, with dust swirling in it and motes drifting through.
  - **Daylight spilling in:** an unshadowed fill light inside each window or door (neighbours share one) and a warm bounce light where the sun lands.
  - **Presets:** Low draws a third of the weeds to 28 m, with no beams. Medium draws three quarters to 45 m, with beams, dust and fill lights. High draws all of them to 70 m and keeps the beams softer beside its volumetric fog; it has no fill lights, since SDFGI bounces light.
  - **Antialiasing** is now SMAA (FXAA on Low) instead of MSAA. With MSAA on, shaders can't read the depth buffer the beams need to stop at walls, so they would shine through them; the beams switch off if MSAA is turned back on.
  - The level kit records every opening as an aperture in world space (`LevelLayout.Apertures`), checked by two new sim tests.
- [ ] Props in place with their proxy colliders.
- [ ] Three rigged, animated opponents, marker in hand, armbands in their team colour.
- [ ] Realistic first-person marker, loader and tank.
- [ ] Everything falls back to greybox when an asset is missing, so the game and CI never depend on the art.

### M2.7 HUD

**Done 2026-10-01**, ahead of M2.6, which waits for the art downloads (the container can't reach Higgsfield's file host yet). [Screenshots](reports/phase-2/) from `-- --round-tour`.

- [x] Top bar with an in/out icon per player (spec §6): the clock in the middle, you on the left, the opponents on the right, filled in their team colour while in, greyed out with a cross once out.
- [x] Time left, kill feed with callsigns, subtitled callouts, pickup prompts, hit confirmation, mask-spray overlay:
  - opponents get invented callsigns (Rook, Magpie, Tinker…), shuffled each round by the match seed, and the kill feed reads "Magpie ▸ You · mask · 25 m";
  - bots shout when they spot you, lose you, come under fire, refill and when they're hit, at most every few seconds, and you see it as a subtitle when you're within earshot;
  - a pickup within 4 m gets a prompt under the crosshair, or says there's no room for it;
  - the crosshair flashes a hit marker when your ball puts someone out;
  - the mask spray is M2.3's.
- [x] The Phase 1 gear panel stays.

Callsigns, callout lines and the HUD's timings are in `presentation.jsonc` ("hud"); how often bots shout is in `bots/brain.jsonc`.

### M2.8 Verify and report

- [x] CI green, including a headless match on Level 1: a bot takes your slot and plays to the end without errors. The bot in your slot is a "hunter" that sweeps the opponent spawns one by one (`--bot-match` in the smoke test, and a sim test that plays a whole Normal round).
- [x] Benchmark: a sim tick with 10 players and 1,000 live balls in ≤ 0.5 ms. In the cloud container it's 0.30 ms on average (p95 0.36 ms, worst 0.45 ms), after the hitbox sweep started caching each player's centre once per tick instead of fetching every pose for every ball.
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
