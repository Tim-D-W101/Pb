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

- Choose **Level 1** and a difficulty: Easy (4 opponents), Normal (6) or Hard (9, so ten people in the round). *Since M2.10 you pick a mode (solo, free-for-all or teams) and how many play separately; the difficulty sets only how good the bots are.*
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

Spent by 2026-10-02: 93 credits (the oil drum and the first character, 68; 20 textures in seven pictures, the marker's reference picture and the concept pictures, 25), so about 307 are left.

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
  - a loading card while the level builds, naming it with its mode, size and difficulty (or the training ground), so the menu doesn't sit frozen (2026-10-02).
- [x] Level ladder file (`levels/ladder.jsonc`) listing levels, difficulty tiers, opponent rosters, time limits, starting gear and pickups. Phase 2 ships Level 1 and shows later levels as locked. Easy has 4 opponents, Normal 6 and Hard 9 (ten players in the round, counting you). *Since M2.10 the number is picked separately, and the rosters became one level roster for scripted runs.*

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

**In progress.** The parts that needed no downloads came first, while the container couldn't reach Higgsfield's file host. Now that it can, real art is coming in, at up to five generations a day (the account's daily cap). On 2026-10-02 that day's five had already gone on the texture sheets, so the rest of the day's art was built in code: a detail model for every prop, frames and broken glass in the windows, things lying on the ground, a proper first-person marker, gutters and downpipes on the buildings, steel trusses under the warehouse roof, piers and barbed wire on the perimeter wall, pylons and telegraph poles beyond it, ivy on the walls, and old paint from past games on the walls and cover; the training ground was dressed to match; and the movement clips' playback was built, ready for the walk and run clips. [Screenshots](reports/phase-2/) from `-- --shots` (with `--views=` for close-ups), `-- --duel-demo` and `-- --bot-demo`.

- [x] Import pipeline (`tools/art/import.sh`, which runs `game/tools/ArtImport` headless). It has brought in 20 materials, the oil drum and the first opponent. Its steps:
  - pull a finished Higgsfield job, make textures tileable, derive normal and roughness maps, and resize;
    - the picture is cropped square and resized;
    - broad uneven lighting is evened out, so tiles don't repeat a light and dark pattern;
    - seams are blended away within a band at each edge, keeping the contrast;
    - normal and roughness maps are derived from the brightness;
    - textures are saved as JPEG with their import settings;
    - models are tidied (pictures shrunk to JPEG, the baked-in glow removed) and measured, so the prop's colliders can be fitted to them;
  - record the job ID and prompt in `game/data/assets.jsonc`, as a provenance record that the art is original. A sim test fails if the kit or the characters use a texture or model without a record. CI runs the texture steps on a generated picture (`--selftest`);
  - **texture sheets** (added 2026-10-02): with five generations a day, one picture holds four materials in a 2 × 2 grid, each cut out with `--region`. A regular pattern (bricks, blocks, planks, corrugations) is cut to whole repeats, measured by autocorrelation, and `--repeats` blends its seams in step with them, so joints don't double where the tiles meet (`--stretch` squares a cut that isn't square). The self-test checks that a repeating pattern comes through unchanged.
- [ ] Photographic materials across the whole level. 23 of 29 so far, each with normal and roughness maps:
  - day 1: cracked asphalt, concrete walls and floors, red brick;
  - day 2 (four sheets): dirt, gravel, dry grass, roofing felt, concrete block, painted brick, peeling plaster, wooden planks, rusty and galvanised corrugated sheet, rusted, painted and yellow steel, and the red, blue and green containers;
  - day 3, from those photos: the pallets (the planks), the rusty drums and the burnt-out car (the rusted steel). A material can now tint its photo (`"tint"`), so the car's rust is duller and darker than a drum's.

  The six left (machinery, the blue drum, tyres, tarp, sandbags, glass) are now mostly drawn by the props' shapes, so they show less. Painted steel and machinery are less metallic than before (paint isn't metal), so desks and pumps no longer go black indoors.

  So the warehouse (corrugated sheet on a block base), the perimeter wall, the offices, the pump house, the guardhouse, the ground and the containers are all photographic now. Left: machinery, car bodies, the two drums' paint, pallets, tyres, tarp, sandbags and glass. Tile sizes follow each pattern's real size (a block 45 cm, a corrugation 7.6 cm). Textures were drawn upside down on walls (the kit's UVs run up a wall, a picture's rows run down it); the shader now flips them, so rust and damp run down. Textured materials get broad light and dark patches, and those without a regular pattern (`breakUpRepeat`: asphalt, dirt, gravel, grass, concrete, plaster, steel) are shuffled in patches a few metres across, so the repeat doesn't read as a grid from the office windows or the water tower. The half-open roller door is painted steel now, not rust.
- [x] Lighting presets, dust, light shafts and weeds (`presentation.jsonc`: "weeds", "shafts", "dust", "windowLight" and the graphics presets):
  - **Weeds and dry grass** wherever rain falls: about 50,000 tufts in Oxbarrow Works, placed at load from the level and seeded by its id. They're thick on the scrubland, patchy on dirt and gravel, in a network of cracks on asphalt and concrete, along the foot of walls and props, and under the holes in the warehouse roof. They sway in rolling gusts, shrink into the ground with distance, and never grow over a pickup.
  - **Sunbeams** through every window, door and roof hole the sun reaches into a roofed space (38 in Oxbarrow Works). Each is a box of light, ray-marched per pixel, that stops at whatever is in front of it, with dust swirling in it and motes drifting through.
  - **Daylight spilling in:** an unshadowed fill light inside each window or door (neighbours share one) and a warm bounce light where the sun lands.
  - **Presets:** Low draws a third of the weeds to 28 m, with no beams. Medium draws three quarters to 45 m, with beams, dust and fill lights. High draws all of them to 70 m and keeps the beams softer beside its volumetric fog; it has no fill lights, since SDFGI bounces light.
  - **Ultra's volumetric fog** no longer veils the sky (2026-10-02): it had hung a brown haze over the whole sky, overhead too. It now leaves the sky to the depth fog, which hazes the horizon, and is thinner; its look is data (`lighting.volumetricFog`).
  - **Antialiasing** is now SMAA (FXAA on Low and Medium) instead of MSAA. With MSAA on, shaders can't read the depth buffer the beams need to stop at walls, so they would shine through them; the beams switch off if MSAA is turned back on.
  - The level kit records every opening as an aperture in world space (`LevelLayout.Apertures`), checked by two new sim tests.
- [ ] Props in place with their proxy colliders. Every prop now has a detail model **built in code** from its own colliders (`game/world/PropShapes.cs`, `kit/props.jsonc` → `"shape"`) with the kit's photographic materials, and the oil drum has its generated model (0.88 m tall, matching its collider). Generated models will replace coded ones where they look better; the shape stays as the fallback.
  - **Wood:** pallet stacks made of boards, with a board missing here and there; crates with battens and diagonal braces; a cable reel with plank flanges and a few turns of cable.
  - **Stacks and walls:** stacked tyres; ribbed oil drums (one tipped over); sandbag walls laid in a proper bond; precast barriers with lifting loops.
  - **Machinery:** a generator with louvres, doors and an exhaust; pump sets on concrete plinths; a forklift with its mast, forks and overhead guard; pallet racking with things left on it.
  - **The yard:** a burnt-out car on its hubs (wheel arches, pillars, a gutted interior); a skip; containers with corner posts, castings and locking bars on their doors; the water tower with braced legs, a railed platform, a hooped tank and ladders.
  - **Offices:** steel desks with papers, a keyboard, a phone, a mug or a box file left on them, all low (2026-10-02); filing cabinets, a drawer often left open; a table tipped up as a barricade.
  - **Heaps:** rubble and scrap lying on a mound; a slatted half-open roller door.

  They're looks only: paint and walking still hit the colliders, which are unchanged, so every shape keeps inside them. 95 props come to about 98,000 triangles, merged per 24 m square and material, and each is varied by a seed from where it stands. [Screenshots](reports/phase-2/): m2.6-16 to m2.6-19 and m2.6-22.
- [x] Three rigged, animated opponents, marker in hand, armbands in their team colour, dealt to the opponents in turn, each copy with a different tint (`presentation.jsonc` → `characters`). A came first (2026-10-01). B and C were generated on 2026-10-03 from one picture of the two side by side (job `931e5577-7dd9-4e3e-a7da-816c0f31b91b`), each half rigged on its own (`image_to_3d`, with PBR maps): B is a stocky man in woodland camouflage with an olive mask and smoked lens, 1.80 m; C is a slim woman in slate grey and dusty blue with a black mask and clear lens, 1.68 m ([B, hit at 2 m](reports/phase-2/m2.6-62-opponent-b.jpg), [C running](reports/phase-2/m2.6-63-opponent-c.jpg)). All three are on the generator's rig, so they share the clips and the posing:
  - It's posed to the sim's hitboxes every frame, on top of its idle clip:
    - the hips drop for a crouch;
    - the spine rolls with the lean;
    - the chest and head follow the aim;
    - the feet step as it moves (IK);
    - both hands hold the marker (IK).
  - Eliminated opponents still raise the marker and walk off.
  - Refilling (2026-10-03), in step with the sim's refill: the support hand leaves the foregrip for the pods on the belt, pulls one out, brings it up beside the loader to tip it in, and goes back. Only the hand moves (the marker stays where its hitbox is), so an opponent caught refilling shows it: hand off the marker, down at the hip.
  - A team-colour armband sits on each upper arm. Since 2026-10-03 it's fitted to each model: halfway down the upper arm, the vertices skinned to it give the sleeve's reach all round the bone, and the band is a ring round that (offset where the bone isn't in the sleeve's middle), so it wraps B's padded sleeve and C's slim one alike instead of sinking into one and floating off the other.
  - Splats stick to the nearest bone and move with it. They reach through to the model's surface, which lies inside the hitbox in some places and bulges past it in others.
  - They hold the marker built in code (below), each part fitted into its hitbox, with their team's paint in the loader.
  - **Movement clips** (code 2026-10-02; walk and run generated 2026-10-03 on A's model, jobs `5ea6374f-1777-4fb0-a7a1-794549f01368` and `75fb11c2-4b41-4c77-bb5e-1df10e9a9470`, the generator's in-place clips 690 and 654, a walk and a run with a gun held up; the crouched walk comes with the next day's generations): `characters.clips` takes a walk, a run and a crouched walk, each one in-place clip on the generator's rig, and fits each to every model's build by bone name (`MoveClipBaker`), so clips made on one character play on all of them. The clip's own feet give its ground speed, and `Gait` plays it by the ground covered, so feet don't slide; walk blends into run by speed and into the crouched walk by stance, in step; the legs turn towards where the body goes, with the chest on the aim, and backing off plays the cycle backwards. The feet are IK'd onto the clip's footfalls, so they stay on the ground through a crouch. Until the clips arrive, the procedural steps stand in (now stepping along the travel). `-- --gait-demo` shows one opponent standing, walking, running, sprinting, strafing, backing off and walking crouched, from the side; it was checked with throwaway clips made in code, which weren't kept. With the real clips, the walk measures 0.3 m/s (a slow, careful walk, a 3.3 s cycle) and the run 4.9 m/s (5.3 m/s on B's longer legs); the sim's 3 m/s walk plays mostly the run, at about three steps a second, and a sprint plays the run 1.4 times as fast. Baking them showed rounding building up down a chain of bone turns, which Godot refuses when turning a vector; the baker now keeps each turn unit length.
- [ ] Realistic first-person marker, loader and tank. **Built in code for now** (`game/player/MarkerShape.cs`), after the reference picture (2026-10-02, job `504fef01-d6f7-4ac5-959b-2aea7e92ba90`: a generic black marker with a smoke-grey loader and a silver bottle as the stock, no brand or text): an angular receiver, a tapered and ported barrel, a pistol grip, foregrip and trigger guard, the bottle on its regulator, and a see-through smoky loader with about 80 balls of your paint in it ([screenshot](reports/phase-2/m2.6-21-first-person-marker.jpg)). Opponents hold the same model, each part fitted into its hitbox, with their team's paint in the loader. In first person it's now in **your gloved hands** (`HandShape`, 2026-10-02): the trigger hand round the pistol grip with the index finger through the guard and the thumb over the top, the support hand's four fingers round the foregrip, each with a cuff and a sleeve running back out of view (glove and sleeve colours in `presentation.jsonc` → `viewModel`). At the marker's usual place the support hand shows at the bottom of the view and the trigger hand is just below it ([screenshot](reports/phase-2/m2.6-30-first-person-hands.jpg)). On the left shoulder the whole model is mirrored, so the hands swap. Next: the generated 3D model from the picture, with this one as the fallback. **Refilling from a pod** (2026-10-03) now plays in your hands, in step with the 2.5 s refill: the marker cants its loader towards you, your support hand drops off the foregrip and comes back up from below with a pod of your paint (a smoked tube, lid flipped open), tips it over the loader, paint tumbling in, and goes back to the foregrip; stop the refill early and it runs on quickly to the end (`ViewModel`, `HandShape.BuildPodHand`; [screenshot](reports/phase-2/m2.6-64-refill.jpg)).
- [x] **Window and door frames** (2026-10-02). Each building names its frame material (`kit/buildings` → `"frames"`: steel in the offices and warehouse, rusty steel in the pump house, wood in the guardhouse). The game draws a frame in every window, divided by mullions and transoms into panes, with shards of dirty glass left along the edges of about half the panes, and a frame round every doorway: 84 openings. The perimeter wall's breaks stay bare, and the openings stay open to paint and light.
- [x] **Gutters, downpipes and roof trusses** (2026-10-02, `kit/buildings` → `"gutters"` and `"trusses"`). Half-round gutters along the eaves of the warehouse, pump house and guardhouse, with downpipes from their ends (a swan neck back to the wall, collars bracketed to it, a shoe at the foot), moved along the wall past doors and windows; the office block's parapet roof has rainwater heads and downpipes instead. Seven rusty steel trusses span the warehouse with purlins along them; where the roof has fallen in, they're broken off and what's left hangs down into the hole. Looks only, merged into the props' meshes ([trusses](reports/phase-2/m2.6-23-trusses-under-the-warehouse-roof.jpg), [gutters and downpipes](reports/phase-2/m2.6-24-gutters-and-downpipes.jpg)). The trusses carry pendant lamps on cables (`"lampsPerTruss"`) and, since the evening of 2026-10-02, two or three runs of black cable slung from one to the next, sagging between them, their ends hanging loose where a truss is broken off over the hole. The office block and guardhouse have **fluorescent fittings** on their ceilings (`"ceilingLights"`), a row down each strip between walls, only where the ceiling is whole: most fixed flat, some hanging from one end, some gone but for their plates and a wire ([screenshot](reports/phase-2/m2.6-29-ceiling-lights-and-lamps.jpg)). Each building also names a material for its **fittings** (`"fittings"`): a dead lamp on a bracket over every door and loading bay, and along the outside walls, clear of the openings, junction boxes with a conduit up the wall, louvred vents, and pipes along the wall turning down into the ground.
- [x] **A town's edge on the horizon** (2026-10-02, `presentation.jsonc` → `horizon.landmarks`): two chimneys and sawtooth sheds to the north-east, a gasholder's frame to the north-west, blocks of flats to the south-west, far off among the tree lines and grey in the haze (`Horizon`, shared by the levels and the training ground).
- [x] **Scenery beyond the wall** (2026-10-02, the level's `"scenery"`): a power line on steel lattice pylons across the northern skyline (legs tapering to a waist, X-braced faces, three crossarms a side with insulator strings, an earth-wire peak, conductors sagging between them), and wooden telegraph poles with crossarms and wires down the road from the main gate. Out of reach, looks only; the pylons use a new galvanised steel without a photo ([screenshot](reports/phase-2/m2.6-27-pylons-and-poles.jpg)).
- [x] **The perimeter wall dressed** (2026-10-02, the level's wall `"dressing"`): piers every 4.5 m standing a little proud of it, a concrete coping, and barbed wire on angle-iron brackets leaning out, three strands sagging between them and hanging loose where the wall is broken; broken blocks lie either side of the breaches and the gate, and sit on top of the stretches knocked down to waist height. Since the evening of 2026-10-02, one leaf of the main gate lies off its hinges on the road outside it (the dressing's `"fallenGates"`): a rusty barred frame with a brace across it, one end propped on a block. Looks only ([screenshot](reports/phase-2/m2.6-26-perimeter-wall.jpg)).
- [x] **Creepers** (2026-10-02, `presentation.jsonc` → `"creepers"`): 110 patches of ivy and dead vine climbing the brick, block and plaster walls outdoors, painted at load (`CreeperPainter`: woody stems fanning up and branching, lobed leaves thick low down and thinning towards the top, some dried brown). Each stands on the ground against a wall with wall behind it all the way up and across, so none climbs over a window or round a corner ([screenshot](reports/phase-2/m2.6-28-creepers.jpg)).
- [x] **Old paint from past games** (2026-10-02, `presentation.jsonc` → `"oldPaint"`). About 2,100 faded splats on the walls, the containers and crates, and the ground, where people shooting from one cover spot at another would have hit: shots are cast at load from the bots' cover points at others facing them across their cover, at any height a head, body or legs would show, straying round where the other would peek. A splat is a card laid flat on what the shot hit (all four corners must land on the same plane, so none hangs off an edge), cut from an atlas painted at load (`SplatPainter`: round splats with fingers and droplets, glancing ones sprayed one way, clusters, half washed-out ones; those on walls have drips running down). Colours are common paint fills, faded towards grey. Round or ragged props (drums, tyres, sandbags, the car, heaps) take none. One draw call; off on Low ([screenshot](reports/phase-2/m2.6-25-old-paint.jpg)).
- [x] **Things on the ground** (2026-10-02, `presentation.jsonc` → `"groundDetail"`). 765 of them in Oxbarrow Works:
  - oil stains round the car, the machines, the drums and the skip;
  - puddles under the open sky that shine with it;
  - damp patches;
  - rust run-off by rusting things;
  - tyre tracks;
  - drifts of leaves against walls;
  - litter, with nothing printed on it;
  - broken chips of concrete and brick.

  They're painted at load into one atlas (two variants of each, with a surface map so oil and water are glossy), drawn as flat cards in one draw call that fade out with distance, and placed by the same ground survey as the weeds ([screenshot](reports/phase-2/m2.6-20-oil-and-rust-round-the-car.jpg)). They're off on the Low preset, as is the old paint.

  Measured over the level's viewpoints under software rendering, the day's art had made frames about 22% dearer; indexing the meshes built in code and giving them generated levels of detail brought that to about 10%. The afternoon's additions (old paint, the wall's dressing, gutters, trusses, fittings and the scenery, about 176,000 triangles built in code in all) add about 3% more, most of it looking across the warehouse under its trusses (11% on that view). Only the owner's hardware can say what that means for the 60 fps target.
- [x] **The compound behind the main menu** (2026-10-02, `presentation.jsonc` → `"menuBackdrop"`): instead of a flat dark wash, the menu sits over Oxbarrow Works itself (buildings, props, weeds, things on the ground, ivy, the level's light, at the saved graphics preset), seen from a camera drifting slowly among the containers and back, under a shade that's darkest on the left where the panels are. It's built a piece a frame after the menu first shows and then fades in, so the menu never waits for it; CI's headless runs skip it. The title is stencilled over a splat of orange paint, painted at load (`TitleMark`) ([screenshot](reports/phase-2/m2.6-31-menu-backdrop.jpg)).
- [x] **Painted markings** (2026-10-02, a building template's or the level's `"markings"`, drawn by `Markings`). In the warehouse:
  - a yellow walkway along the mezzanine's edge;
  - a box round the racking;
  - hatched aprons inside the four loading bays;
  - yellow-and-dark stripes round the foot of every column;
  - the bays' numbers stencilled in white on the cladding outside.

  On the yard: eight numbered parking bays along the office block and a stop line inside the main gate. Stencils take capital letters too, so the warehouse's cladding says OXBARROW WORKS and BAY 1 to BAY 4, the office block's door OFFICES, and the wall either side of the main gate KEEP OUT and PRIVATE PROPERTY in red ([the warehouse](reports/phase-2/m2.6-39-loading-bay-numbers.jpg), [the office block](reports/phase-2/m2.6-43-painted-words.jpg)). All the paint is worn through in scuffs and specks, cut from an atlas painted at load (`MarkingPainter`: worn bands, diagonal stripes, and stencil digits, capitals and a dash), one draw call. Looks only; a sim test checks they pass through and that bad colours, stencil text and areas are named by file and key ([walkway and columns](reports/phase-2/m2.6-38-warehouse-markings.jpg), [bay numbers](reports/phase-2/m2.6-39-loading-bay-numbers.jpg), [a hatched bay](reports/phase-2/m2.6-40-hatched-loading-bay.jpg), [parking bays](reports/phase-2/m2.6-41-parking-bays.jpg)).
- [x] **Skirting boards** (2026-10-02, `kit/buildings` → `"skirting"`): a board along the foot of every inside wall face that stands on a floor in an indoor area, stopping at doorways and running under windows: white in the office block, wood in the guardhouse (150 faces). Looks only ([screenshot](reports/phase-2/m2.6-45-skirting.jpg)).
- [x] **Cobwebs** (2026-10-02, `presentation.jsonc` → `"cobwebs"`): webs in about four in ten corners of the buildings' windows and the top corners of their doorways, inside the frames, painted at load (`Cobwebs`: threads fanning out of the corner, a sagging spiral strung between them, broken spans and loose strands), one draw call, looks only ([screenshot](reports/phase-2/m2.6-44-cobwebs.jpg)).
- [x] **Things left on the walls** (2026-10-02, `presentation.jsonc` → `"wallHangings"`): in the offices and the guardhouse (the buildings with finished interiors), noticeboards with papers pinned to them, whiteboards with faded scribbles, a clock stopped at twenty to five, wall planners, exit and hazard signs, a framed landscape and a site plan, about one in three lengths of wall, some hanging crooked (49 in the compound). They're painted at load (`HangingPainter`): lines and pictograms stand in for writing, so nothing can be read and nothing is anyone's mark. Faded, dusty and blotchy, flat on the wall; paint lands on them as on the wall ([screenshot](reports/phase-2/m2.6-53-things-on-the-walls.jpg)).
- [x] **Contact shadows** (2026-10-02, `presentation.jsonc` → `"contactShadows"`): soft darkening on the floor round the foot of every wall, column and prop that stands on the ground or a floor, so they sit down on the presets without ambient occlusion (Low and Medium; High and Ultra have SSAO, which does the job, so they're hidden there). One card per footprint (a wall's pieces round its doors and windows are joined into one run first, so the shadow doesn't double up where they meet), 222 in the compound, one draw call that multiplies what's under it ([screenshot](reports/phase-2/m2.6-47-contact-shadows.jpg)).
- [x] **Run-off streaks** (2026-10-02, `presentation.jsonc` → `"runOff"`): where rain has run down the outside walls for years. Grime runs from under every window's sill (rust under the pump house's rusty frames), rust from the wall lamps, junction boxes, vents, the downpipes' brackets and the barbed wire's brackets on the piers, and dark stains hang all along under the gutters, the office's rainwater heads and the perimeter wall's coping. Each streak stops above an opening below it and at the ground; 455 in the compound, one of four patterns painted at load (`RunOff`: a stain along the edge, broad striated plumes, thin wavering drips), coloured from grime to rust by what it runs off (`rustFrom`), one draw call under the old paint and markings ([screenshot](reports/phase-2/m2.6-48-run-off-streaks.jpg)).
- [x] **Debris on the floors indoors** (2026-10-02, `presentation.jsonc` → `"floorDebris"`): on every storey of every building, papers lying about or screwed up into balls (ruled, nothing written), plaster fallen off the walls along their foot, drifts of dust in the corners, leaves blown in, and glass under the broken windows on both sides, on the floor inside and the ground outside. 835 cards in the compound, from one atlas painted at load (`DebrisPainter`), each placed where all of it lies on one flat floor (rays down through the room); off on Low with the things on the ground ([screenshot](reports/phase-2/m2.6-49-floor-debris.jpg)).
- [x] **Clouds drifting over** (2026-10-02, `presentation.jsonc` → `lighting.clouds`): a deck of broken cloud moving slowly on the wind, over the levels, the training ground and the menu. A dome round the camera casts each view ray up to a flat layer of cloud 1.4 km up and reads two tiling noises there (`CloudDeck`, `clouds.gdshader`), so the clouds keep their perspective and shrink towards the horizon; thick cloud is grey underneath, its edges towards the sun bright and its rims glowing near the sun, and low down it thins into the haze. The sky's old painted cover stays behind it as a faint, still, higher layer, so the sky's light and reflections don't change as the deck moves ([screenshot](reports/phase-2/m2.6-50-cloud-deck.jpg)). Since the evening of 2026-10-02 that layer fades out above mid-sky (`clouds.highLayerFade_deg`): drawn on a panorama, it pinched into streaks converging overhead, a haze just over the player's head. It also no longer thins into a hazy band due north and along the horizon, where Godot's seamless noise averaged two fields; it now wraps round with a blend that keeps its contrast.
- [x] **Trees beyond the walls** (2026-10-02, `presentation.jsonc` → `"woods"`): ten copses of broadleaf trees in the scrubland round the compound and the training ground, between the wall and the tree lines on the horizon, so the view over the wall has some depth. Each tree is grown in code (`Woods`): a leaning, tapering trunk with a flared foot, main branches reaching out and up to the edge of its crown with smaller ones off them, a few young trees among the old, and clumps of leaves through the crown: crossed cards from an atlas painted at load (a hundred-odd pointed leaves and a few twigs each), alpha-cut, lit as if the crown were round, light coming through from behind, swaying in a gusting wind (`leaves.gdshader`). A copse takes one of five late-summer greens, some turning; trees keep clear of the power and telegraph lines. Wood is one mesh and casts shadows; the leaves are one MultiMesh and don't (alpha-cut cards are dear to cast, and their shadows would fall outside the walls). They cost about 5% of a frame under software rendering ([screenshot](reports/phase-2/m2.6-51-trees-beyond-the-wall.jpg)).
- [x] **Manhole covers and drain gratings** (2026-10-02, `presentation.jsonc` → `"yardFittings"`): seven round cast-iron covers in their frames out in the yard, ribbed, with two lifting slots, and fourteen gratings (bars over a dark hole, a bar or two missing), most of them along the foot of the buildings' and the perimeter's walls. Each lies flush on asphalt or concrete under the open sky with nothing standing on it (`YardFittings`); they're cast in a new kit material, `cast_iron` (the rusted-steel photo, darkened). Looks only ([screenshot](reports/phase-2/m2.6-52-manhole-and-drain.jpg)).
- [x] **Cracks in the asphalt and concrete** (2026-10-02, `presentation.jsonc` → `"cracks"`): the crack network the weeds were already growing along is now drawn, so the tufts come up out of the cracks (`Cracks`, `CrackNetwork`). Each crack is a dark, jagged line wandering a little either side of the network, in a faint band of grime, with fine spurs branching off and here and there a stretch where its edges have crumbled. How open they are varies in patches across the yard, from gaping to hairlines; concrete cracks finer than asphalt, and nine patches of the asphalt have broken up into small pieces (alligator cracking). They stop where the ground changes, and run through the painted lines. 4,467 lines, 3.8 km in all, multiplied over the ground; looks only ([screenshot](reports/phase-2/m2.6-55-cracks.jpg)).
- [x] **Worn paths** (2026-10-02, `presentation.jsonc` → `"wornPaths"`): where people have walked for years across the dirt, dry grass and gravel, between the doorways, the gaps in the walls (the low ones too) and the clusters of cover out on the soft ground, the ground is trodden down to bare earth and nothing grows down the middle. The paths join those places in a spanning tree with a few loops, never through anything standing, wandering a little; they show only over soft ground, fading out where they reach asphalt or a floor (45 stretches, 520 m, in the compound). Planned before the weeds, which keep off them (`WornPaths`, `worn_paths.gdshader`); looks only.
- [x] **Graffiti** (2026-10-02, `presentation.jsonc` → `"graffiti"`): throw-ups in fat bubble letters low on the outside walls and tags (a word in one thin, slanting, joined-up line, underlined with a flourish) at arm's height, 23 in the compound, painted at load from invented words in the data, so nothing is anyone's mark (`GraffitiPainter`: the stencil letters drawn freehand, outlined, shaded, with a shine and paint run down from them). They go on the outside faces of walls standing on the ground (a free-standing wall's face looking in; the buildings' walls get more than their share), each wholly on its face and clear of the piers and the other pieces, worn away in patches, bleached by the sun and drawn under the rust streaks (`Graffiti`, `graffiti.gdshader`); looks only ([screenshot](reports/phase-2/m2.6-56-graffiti.jpg)).
- [x] **Bags caught on the wire** (2026-10-02, `presentation.jsonc` → `"snaggedBags"`): fourteen plastic bags snagged on the perimeter wall's barbed wire, mostly white, a few black, blue, red or yellow, each caught at a point along a strand between two brackets, gathered where it's caught and split at the bottom into its handles. The wind holds them out, further in a gust, ripples run down them and their free ends flap and twist, so no two move together; none hangs below the top of the wall (`SnaggedBags`, `snagged_bags.gdshader`, from the strands the wall dressing reports); looks only ([screenshot](reports/phase-2/m2.6-57-bags-on-the-wire.jpg)).
- [x] **Fresh paint runs** (2026-10-02, `presentation.jsonc` → `"paintDrips"`): where a ball breaks on a wall or any steep face of the world (not on a player or a target), one to three runs of paint creep down from the splat over a few seconds, quick at first and slowing as the paint gives out, each ending in a bead, in the paint's colour. On the levels and the training ground's backstop and bunkers alike (`PaintDrips`, decals beside the splats, pooled with a cap); looks only.
- [x] **Footprints** (2026-10-02, `presentation.jsonc` → `"footprints"`): every step on dirt presses a boot print into it, left and right in turn, pointing the way the walker was going, with the tread showing, fading out over three minutes; a landing leaves both feet side by side. Bots leave them too, so the east field shows where people have been. Tread in fresh paint on the ground, anywhere, and your next eight prints are in that paint, fainter each step. Step in a puddle or a drip's pool and your next ten prints on concrete or asphalt are wet, dark and fainter each step, drying out within a minute from their edges in, the tread's bars last (`Footprints`, `footprints.gdshader`, `wet_prints.gdshader`, the sole painted at load, darkening the ground under it); looks only ([screenshots](reports/phase-2/m2.6-58-footprints.jpg), [in paint](reports/phase-2/m2.6-59-paint-footprints.jpg), [wet](reports/phase-2/m2.6-61-wet-footprints.jpg)).
- [x] **Tatters under the roof holes** (2026-10-02, `presentation.jsonc` → `"roofTatters"`): torn strips of roofing felt and insulation hang from the edges of the holes where a roof has fallen in, 66 in the warehouse and the pump house, swaying and fluttering in the draught (`RoofTatters`, with the bags' shader); high up, so they never hide anyone; looks only.
- [x] **Ripples on the puddles** (2026-10-02, `presentation.jsonc` → `"ripples"`): where a foot comes down in a puddle's water, two rings spread out from it and fade (three on landing), and where a ball breaks or bounces in one, two smaller ones; never wider than the water reaches from there. Pale crests with a darker trough inside, as the water mirrors the sky. From the sim's events, so bots splash through them too (`PuddleRipples`, `ripples.gdshader`, from the puddles `GroundDetail` lays); off with the things on the ground on Low; looks only.
- [x] **Water dripping through the roof** (2026-10-02, `presentation.jsonc` → `"roofDrips"`): water still drips from the torn edges of the holes in the warehouse's and the pump house's roofs. At 35 places along them a drop gathers every few seconds and falls to the floor, a faint pale streak against the dark, into a small dark pool of its own, and a ring spreads across the pool where it lands. The pools are water like the puddles, so feet and paint ripple them too (`RoofDrips`, `roof_drips.gdshader`, `drip_pools.gdshader`); off with the things on the ground on Low; looks only ([screenshot](reports/phase-2/m2.6-60-roof-drips.jpg)).
- [x] **Weeds pushed aside** (2026-10-03, `presentation.jsonc` → `weeds.push*`): walk through the weeds and dry grass and the tufts within 0.7 m of your feet lean away from you and lie a little flatter, closer ones more, then spring back over about a second once you've passed. Bots part them too, on the levels and on the training ground's grass (`WeedField.Follow`, `weeds.gdshader`); looks only.
- [x] **Crows startled** (2026-10-02, `presentation.jsonc` → `birds`): the crows sitting on the walls and feeding on the ground no longer wait for the camera: a shot within 18 m of them puts them up, so does a ball breaking within 6 m, and anyone's footsteps, as far as half the distance they're heard (a sprint carries furthest, a crouched step hardly at all), from the sim's events. So a firefight clears the wall tops; looks only.
- [x] **Litter blowing about** (2026-10-02, `presentation.jsonc` → `"blowingLitter"`): sixty dry leaves, scraps of paper and crumpled wrappers lie about the open ground. When a gust rolling across the level is strong enough for one, it lifts it: leaves tumble over and hop, paper slides with its edges lifting, and each skitters off downwind until the gust drops, it fetches up against a wall or a prop, or it's blown in under a roof. Ones pinned for a while go back out onto open ground when the camera isn't looking at either place (`BlowingLitter`, `blowing_litter.gdshader`, its atlas painted at load); looks only.
- [x] **Dust underfoot** (2026-10-02, `presentation.jsonc` → `"footDust"`): on dirt, gravel and dry grass (and faintly on the dusty concrete floors indoors), every footstep raises a soft puff in the ground's colour, bigger the harder the step (a walk's to a sprint's; crouched steps raise none), a landing a ring of them, a jump one, and a slide a trail; a ball breaking or bouncing there knocks up a little too, so on the training ground you see where your misses land in the grass. They billow out, grow, rise a little and drift off on the wind, darker in shade. Bots and people raise it alike, from the sim's footstep events (`FootDust`, `foot_dust.gdshader`); looks only. One ground wind (`groundWind_mps`) now drives the weeds' sway, the bags on the wire and the dust, so they all blow the same way.
- [x] **The roof fallen in** (2026-10-02, a building's roof `"holes_m"`): under each hole in the warehouse roof, the corrugated sheets that came down lie on the floor, some flat, some with an end propped on the rubble, some bent up across the middle where they caught on the way down, with a broken purlin or two among them, on whatever floor is below (the ground floor or the mezzanine), each clear of everything standing there (`BuildingDetails.FallenRoof`, merged into the props' meshes; looks only, a few centimetres high). Puddles now lie on the concrete floor under the holes too.
- [x] **Damp and splash-back** (2026-10-02, `presentation.jsonc` → `"damp"`): grime splashed up the foot of the outside walls that stand on the ground, rising to a soft, wavering edge, specked with splashes and greener on the shady faces looking north; indoors, tide marks of rising damp along the foot of the walls on the ground floor (a crust of salt along each mark, the wall darker below it) and black mould spreading from the top corners of the rooms. Painted at load (`DampPainter`: two bands that repeat along a wall and an atlas of mould and stains), each card flat on one wall face (`Damp`); looks only, under the old paint. Water stains on the ceilings are built too but switched off, as the ceilings are too dark for them to show.
- [x] **Smoke off a far chimney** (2026-10-02, `presentation.jsonc` → `horizon.smoke`, and a landmark's `"smoke"`): the taller of the two chimneys on the north-east skyline smokes, a grey plume rising and carried off across the view on the wind, each puff growing and thinning as it goes, already drawn out when a level loads (`Horizon`, `smoke.gdshader`). On the levels, the training ground and behind the menu.
- [x] **Crows overhead** (2026-10-02, `presentation.jsonc` → `"birds"`): two flocks wheeling over the compound (and one over the training ground and behind the menu), each bird on its own circle and height, banked into the turn, gliding with its wings a little raised and flapping in bursts every few seconds (`Birds`, `birds.gdshader`). Nine more sit on the wall tops under the open sky (the perimeter wall's coping, the office block's parapet), turning now and then; come within 9 m and one flaps up off its perch and joins the wheeling. Six more feed together on the open ground (dirt, dry grass, gravel), pecking at it and hopping on a little way now and then, and fly up the same way (`"feeding"`). Looks only ([a crow on the wall](reports/phase-2/m2.6-46-crow-on-the-wall.jpg), [crows feeding on the east field](reports/phase-2/m2.6-54-crows-feeding.jpg)).
- [x] **The training ground dressed** (2026-10-02, `presentation.jsonc` → `"trainingGround"`, `game/world/RangeBuilder.cs` and `RangeShapes.cs`). The Phase 1 range behind the menu's **Training ground** was still greybox: flat green turf, primitive props, a plain sky, a grey box at the end. Now it has:
  - the levels' sky, sun and tree lines, and the kit's photographic dry grass for the lane, with painted lines across it and gravel behind the firing line;
  - netting down both sides on galvanised posts, a cable sagging along their tops (the netting is painted at load, so far off it thins to a haze);
  - a timber backstop reaching past the lane, a grassy earth bank behind it and netting on tall poles above;
  - distance boards on posts, bigger further out so they read from the firing line;
  - the inflatable bunkers built in code over their colliders, in their own colours: rounded, seamed, tethered to pegs. The thin board stands on stakes;
  - dummies padded round their hitbox in paintball masks, on tyres filled with concrete. The runner rides a trolley on rails. The targets now face the firing line (`yaw_deg` 180; their hitboxes are round, so nothing they're hit by changes);
  - old paint on the backstop, the bunkers, the ground round the targets and the dummies themselves (the runner carries its own);
  - a shelter behind the firing point (timber frame, corrugated roof, a bench, a table with pods and a tank);
  - pylons and telegraph poles beyond;
  - grass and weeds (the levels' `WeedField`, now fed by any place's ground through `IWeedGround`): rough scrub outside the nets, longer grass along their foot and round every bunker, dummy, post and rail, the lane itself mown ([screenshot](reports/phase-2/m2.6-42-training-ground-grass.jpg)).

  The nets, backstop, boards and shelter stop you walking off the lane; what paint hits is still decided by `ranges/phase1.jsonc`, unchanged. Graphics presets now apply there too. The range gained `"viewpoints"`, so `-- --shots` tours it as it does a level. [Screenshots](reports/phase-2/): m2.6-32 to m2.6-37 and m2.6-42.
- [x] Everything falls back when an asset is missing, so the game and CI never depend on the art: materials to the procedural look, props to their shapes built in code (then greybox), opponents to their hitbox boxes. CI's bot match runs with `--no-art` to keep the fallbacks working.

### M2.7 HUD

**Done 2026-10-01**, ahead of M2.6, which was waiting for the art downloads (the container couldn't reach Higgsfield's file host yet). [Screenshots](reports/phase-2/) from `-- --round-tour`.

- [x] Top bar with an in/out icon per player (spec §6): the clock in the middle, you on the left, the opponents on the right, filled in their team colour while in, greyed out with a cross once out.
- [x] Time left, kill feed with callsigns, subtitled callouts, pickup prompts, hit confirmation, mask-spray overlay:
  - opponents get invented callsigns (Rook, Magpie, Tinker…), shuffled each round by the match seed, and the kill feed reads "Magpie ▸ You · mask · 25 m";
  - bots shout when they spot you, lose you, come under fire, refill and when they're hit, at most every few seconds, and you see it as a subtitle when you're within earshot;
  - a pickup within 4 m gets a prompt under the crosshair, or says there's no room for it;
  - the crosshair flashes a hit marker when your ball puts someone out;
  - the mask spray is M2.3's.
- [x] The Phase 1 gear panel stays. On 2026-10-02 it's drawn rather than written (`GearPanel`), with the same information:
  - the fire mode;
  - a loader whose bowl holds balls of your paint up to how full it is, with its count (and the refill's progress under it);
  - a pod for each pod, filled to what's left in it, and the balls in them all;
  - the air as a bar marked where the regulator gives out and at the low warning, green, amber or red, with the figure in bar.

  [Screenshot](reports/phase-2/m2.7-2-gear-panel.jpg).
- [x] **A plan of the level on the briefing card** (2026-10-02, `LevelMap`): drawn from the level's primitives, north up: the ground and its patches in their materials' colours, the buildings' floors with their walls round them, upper floors faintly over them, props in their own colours (the containers red, blue and green), where you start with an arrow the way you face, and your teammates beside you, with a north arrow and a 20 m bar. Nothing about the opponents or the pickups is shown ([screenshot](reports/phase-2/m2.7-3-briefing-map.jpg)).

Callsigns, callout lines and the HUD's timings are in `presentation.jsonc` ("hud"); how often bots shout is in `bots/brain.jsonc`.

### M2.9 Your first play-test

**In progress (2026-10-01).** You played the Windows build: it felt glitchy, lagged on anything above Low, and starting positions never changed, so you could learn where everyone was.

- [x] **V-sync is on by default.** It was off, a leftover from measuring raw frame rates, so the picture tore whenever you turned. F8 or the pause menu still turns it off. Saved settings get it switched on once.
- [x] **Lighter graphics presets and a render scale setting.** Each effect's cost was measured over the level's viewpoints (`-- --shots --preset=NAME` prints per-view frame times). Ambient occlusion was the biggest single cost, so:
  - **Medium** is now about 1.2× the cost of Low: glow, half the weeds, shadows to 75 m; no ambient occlusion, sunbeams or fill lights;
  - **High** is the old Medium (about 2.3× Low) and **Ultra** the old High;
  - **render scale** (pause menu → settings): the 3D view is drawn at 50–100 % of the screen's resolution and upscaled with FSR 1.0, for any preset.

  F12 cycles Low → Medium → High → Ultra. The details are in `docs/architecture.md` §14.3.
- [x] **Random starts every round.** Each round (and each restart) deals a new seed:
  - you come in at the main gate, the breach in the west wall or the collapse in the north-east corner;
  - opponents start at a random mix of the level's opponent spawns and cover points (anywhere the level offers cover) inside the compound;
  - every opponent starts at least 28 m from you, out of your sight, and 10 m from the others;
  - each plays a random role: one of its spawn's roles, or by chance at a cover point (half guards, the rest patrollers on the nearest route or rushers).

  The numbers are in `rules.jsonc` ("spawning") and the entry points in the level file (`playerSpawns`). Scripted runs keep the data's seed and the level's roster, so they play out the same every time; `--seed=N` replays a round and `--random-spawns` deals random starts in a scripted run (CI's bot match). Four sim tests: the same seed deals the same starts, every round starts differently (12 rounds: 12 line-ups, 47 start places, all three entry points), starts are fair (distance, sight, spacing, inside the compound), and roles are mixed with patrollers on routes.
- [x] **A ready-to-run Windows build** from every CI run, and a **launcher that updates itself**:
  - CI publishes each build as the "test-build" release, so one link always has the newest version;
  - `Play.bat` checks it, downloads only what changed, then starts the game;
  - without internet, it just starts the game.
- [x] **Smaller updates.** You asked not to download the whole game for every change.
  - **Art in packs of its own.** The art was 21 of the game's 22.6 MB, so it now ships apart from the game, which mounts it when it starts. Since 2026-10-02 there's one pack per asset (`art/Pb-art-<id>.pck`, `tools/package/art-packs.sh`): with art arriving every day (65 MB of it now), a day's new textures cost just their own packs, not all the art again.
  - **Only changed files.** The release lists each game file with its SHA-256, and `Play.bat` downloads only the files that differ from yours. Each is checked against its fingerprint before it replaces yours.

  A code or data change is now under 1 MB (the game's own pack is about 150 KB, and its two code files about 700 KB). Art comes down only when it's new or changed, and the whole game only when the engine does. A fresh import doesn't give byte-identical art, so CI reuses each published pack while its asset is unchanged. Launchers from before this change still update through `Pb-update.zip`, which brings the new launcher with it. Tested with PowerShell 7 against a local copy of the release:
  - an old install got just the changed files;
  - a corrupted download was refused and nothing was replaced;
  - an install that was already up to date downloaded nothing but the manifest;
  - with per-asset packs: an install with the single art pack got the 22 packs (65 MB) and, on the next update, lost the old pack; a re-imported asset brought down just its own pack (2.7 MB), and a pack the release no longer lists was removed.

### M2.10 Modes: free-for-all and teams

**Built (2026-10-01), as planned and approved.** You asked for opponents who fight each other as well as you, team games such as 3 against 3, and difficulty that doesn't depend on the number of players.

- [x] **Menu.** Each level offers a mode, a size and a difficulty, then Start; it remembers your last choices.
- [x] **Modes** (`rules.jsonc` → `modes`):
  - **Solo**: the original game, you against a squad holding the compound (3, 4, 6 or 9 opponents);
  - **Free-for-all**: 4, 6, 8 or 10 players, everyone against everyone, each in their own paint colour;
  - **Teams**: 2 v 2 up to 5 v 5, you and bot teammates against a bot team.
- [x] **Difficulty** (Easy, Normal, Hard) now sets only how good the bots are, how long the round lasts and what everyone carries. It never sets how many there are: the tiers no longer list opponents.
- [x] **Who wins.** One rule for every mode: the last team standing wins. Free-for-all is everyone on a team of their own.
  - **Teams:** the round goes on while your team still has someone in, so your teammates can win it without you.
  - **Free-for-all:** the rest play on after you're out, and the summary gives your placing ("3rd of 8").
  - **Friendly fire:** a hit from a teammate puts you out, as in real paintball. It doesn't count as an elimination.
- [x] **Starts** (`rules.jsonc` → `spawning`):
  - **Free-for-all:** everyone at least 16 m apart and, where the level allows, out of each other's sight. In tests, no pair of players could see each other at the start over ten rounds of every size.
  - **Teams:** your teammates start within 12 m of you, facing the way you do. The other team starts grouped round a spot on the far side, out of sight of your whole team; in tests, the nearest opponent was at least 40 m away.
- [x] **Bots that go looking for a fight.** In free-for-all and teams, bots are dealt behaviours from the mode's chances, mostly hunters who roam the compound. A bot that has had nothing to go on for 40 s starts hunting too, so a round can't stall with everyone holding a corner. Hunters pick their next spot at random from the three nearest, so they don't all sweep the same way.
- [x] **When you're out.** You see who got you as before. Then, while your team (or, in free-for-all, anyone) is still in, a camera follows one of them: **Next player** (Space or a click) switches, and **Skip to summary** (Enter) leaves. If you skip, the summary updates when the round ends.
- [x] **On screen:**
  - the top bar shows your team on the left and everyone else on the right, each in their team's colour;
  - teammates wear your armband colour;
  - the kill feed reports bots putting each other out;
  - putting out a teammate says so.
- [x] **Tests:**
  - 20 new sim tests: outcomes for each mode, placings and ties, friendly fire, a free-for-all round that steps without allocating, starts for every mode and size, data checks, a restless bot going hunting, and whole free-for-all and team rounds played by bots;
  - CI's smoke test now also plays a 3 v 3 and an eight-player free-for-all with bots in every slot.
- [x] **Cost.** Nine Hard bots in a free-for-all, each watching all the others, take 0.08 ms of brain time per tick on average (0.02 ms in solo), about 0.16 ms per 60 fps frame.

### M2.8 Verify and report

This closes the phase, after M2.9 and M2.10.


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
