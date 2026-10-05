# Phase 3 — The level ladder: plan

> **Status: approved 2026-10-05** (you asked to "continue and build phase 3"; the defaults below are taken, and
> any of them can still change). Phase 2 is merged ([report](reports/phase-2.md)). Technical design:
> [architecture.md §15](architecture.md#15-phase-3-the-level-ladder), added as the milestones land. Everything in
> [spec.md](spec.md) still applies: ballistics, paint rules, gear, metric units, original IP, data files, tests.

## Your direction

From the revised roadmap you approved with Phase 2 ([phase-2.md](phase-2.md#revised-roadmap)):

> **Level ladder:** 3–4 more compound levels of rising difficulty, unlocks and a local save, Marksman and Flanker
> behaviours, objectives (retrieve an item, hold a room), audio pass with voiced callouts, doors that open and
> close, full settings and key rebinding.

And from the spec: settings with rebindable controls for mouse, keyboard and both pad layouts, colourblind-safe
team colours (§6, §10); shot sounds that follow tank pressure, distinct break sounds per surface, footsteps per
surface and positional 3D audio (§8).

## Goal

A ladder of four abandoned compounds, each harder than the last, that you climb in order and that remembers how
far you got. Each place plays differently, opponents gain two new behaviours, rounds can be won by objectives as
well as by eliminations, doors open and close, the game sounds like a real paintball game with opponents who shout
out loud, and every setting and key can be changed. The rules and ballistics stay exactly as they are: one hit
eliminates, a bounce never does.

## What you'll be able to do

- **Climb the ladder.** Start at Oxbarrow Works. Win a round there (any mode, any difficulty) and the Rail Yard
  opens, then the Cold Store, then the Hospital Wing. Level select shows what's open, what's locked and why, and
  your best result on each difficulty. Progress and your last choices survive quitting the game. An "open every
  level" switch in the settings skips the climb (for testing).
- **Play three new places**, each in every mode (solo, free-for-all, teams), every size and every difficulty:
  - **The Rail Yard**: long sightlines down the tracks broken by rows of rusting wagons, an engine shed with a
    gantry, a signal box and a footbridge over the tracks for whoever wants height, and paint that flies under
    the wagons at your ankles.
  - **The Cold Store**: dark, close and full of doors: cold chambers off a central aisle, a loading dock a metre
    above the yard with lorry trailers backed onto it that you can run through, a plant room humming with dead
    compressors.
  - **The Hospital Wing**: three storeys of corridors, wards and stairwells round an overgrown courtyard that
    every window looks down on, half of one wing fallen in.
- **Open and close doors** (F; on a pad, the refill button while you're facing a door): doors stop paint and block
  sight, and opening one makes a noise opponents can hear. A tap opens or shuts a door; holding eases it open a
  little at a time, so you can peek through the gap. Opponents open the doors on their way.
- **Pick an objective** besides clearing the place, in solo and teams:
  - **Retrieve**: a case is somewhere in the compound (its building is marked). Find it, pick it up and carry it
    out through any of the ways you came in. Opponents guard it, and once it's taken they come after you. If
    you're hit carrying it, it drops where you fell.
  - **Hold**: take a marked room and hold it for a minute (the clock only counts while your side is in it and
    nobody else is). Opponents defend it and, once you're in, counter-attack.
  - Eliminating everyone still wins either way.
- **Face Marksmen and Flankers.** A Marksman takes high ground with long views (the signal box, an upper window),
  shoots carefully at range and moves after a few shots. A Flanker, once a teammate has called you out, works
  round out of your sight to come at you from the side. Opponents now pass on what they see: a shout of "Contact!"
  tells the others where you are.
- **Hear it.** Every sound is new: a marker report that sags as the tank empties, breaks and bounces that sound
  different on metal, wood, concrete, glass, ground and people, footsteps per surface and pace, doors creaking and
  slamming, wind outside and a room's hum and echo inside, the round's whistle and warnings. Opponents and your
  teammates shout their callouts out loud, from where they are, in six different voices, with the subtitles as
  before.
- **Change everything** in a full settings menu: rebind every action for mouse and keyboard and for the pad,
  sensitivity and stick response, the graphics preset and its parts, window mode and frame cap, volume for
  effects, voices, ambience and menus, subtitles, crosshair style and colour, head-bob and camera jolt, and
  colourblind-safe team colours.

## The levels

All three are built from the level kit like Oxbarrow Works, so one definition makes the visible meshes, walking,
paint, sight and the bots' navigation. Every name is a placeholder in data. Sizes are the walled area.

### Level 2: The Rail Yard (about 150 × 80 m)

A disused goods yard and engine shed. Four tracks run east–west the length of the yard; rakes of derelict wagons
stand on them with gaps between, so the lanes along the tracks are long and the crossings between them short and
exposed. Wagons stand on their wheels: the gap under a wagon's body stops nobody walking, but paint flies through
it, so legs show under the wagons. Elevated spots (the signal box's upper floor, the footbridge, the engine shed's
gantry) are where Marksmen go. Tracks are a new level feature (rails that paint hits and feet step over, sleepers
drawn on the ballast); wagons, a shunting locomotive, buffer stops and stacks of sleepers and rails are new props.

```
                                         N
  +-------------------------------------------------------------------------------+
  | brick wall                                                coal stage   water   |
  |  +----- ENGINE SHED 44 x 18, gantry, two tracks in -----+                tower  |
  |  ===track 1================================================================[]  |
  |       [box vans][box vans]       [tank wagons]          [open wagons]           |
  |  ===track 2================================================================[]  |
  |                  FOOTBRIDGE (deck 6 m up, stairs at each end)                   |
  |  ===track 3================================================================[]  |
  |     [flat wagons, pipes]   [shunter]               [box vans]                   |
  |  ===track 4=====+--- GOODS SHED 30 x 12, raised platform ---+==================|
  |                                                                                 |
  |  SIGNAL BOX       yard office        sleeper stacks      lamp hut               |
  |  (2 storeys)                                                    breach in fence |
  |  ======== GATE (you start) ========                                             |
  +-------------------------------------------------------------------------------+
```

### Level 3: The Cold Store (about 100 × 80 m)

A refrigerated depot gone dark. Most of the fighting is inside one big insulated building: six cold chambers off
a central aisle, each behind a heavy door (some shut, some open, a different mix every round), racking inside them,
and the dock hall along the front, its floor 1.2 m above the yard. Lorry trailers stand backed onto the dock doors
with their rear doors open, so a trailer is a tunnel from the yard up onto the dock. It's dark inside (bots see you
later, and you them), and quiet, so a door opening carries. Outside: the yard, a plant room of dead compressors
and ammonia tanks, a two-storey office, the gatehouse and a weighbridge.

```
                                     N
  +--------------------------------------------------------------+
  | fence                                                         |
  |   +--------------- COLD STORE 56 x 36 -------------+  PLANT   |
  |   | [chamber 1] [chamber 2] [chamber 3] [chamber 4] |  ROOM    |
  |   |---------------- central aisle -----------------|  12 x 10 |
  |   | [chamber 5] [chamber 6]   [blast freezers]      |  ammonia |
  |   |========= dock hall, floor 1.2 m up =============|  tanks   |
  |   +--[dock]----[dock]----[dock]----[dock]---[door]--+          |
  |       [trailer] [trailer]       [trailer]                       |
  |                      YARD (lorry park)          OFFICES          |
  |   GATEHOUSE   weighbridge                       (2 storeys)      |
  |   ======= GATE (you start) =======            breach in fence   |
  +--------------------------------------------------------------+
```

### Level 4: The Hospital Wing (about 110 × 80 m)

The hardest. Two three-storey wings in an L round an overgrown courtyard: long central corridors with wards and side
rooms off them (double swing doors with vision panels, fire doors at the stairwells), a stairwell at each end of
each wing, an operating theatre, a lift shaft open through every floor, and the far end of one wing fallen in so the
upper floors break off into the sky. Every window over the courtyard is a Marksman's; the two stairwells per wing
are a Flanker's. Outside: an ambulance bay under a canopy, a car park of wrecks, and a boiler house with a tall
chimney.

```
                                      N
  +---------------------------------------------------------------+
  | fence                                      BOILER HOUSE  (o)   |
  |  +----------------- NORTH WING, 3 storeys, 56 x 14 ---------+  |
  |  | stair | wards | theatre | wards | nurses | (fallen in)  |    |
  |  +-------+--------------------------------------------------+   |
  |  |  WEST |                                                       |
  |  |  WING |        COURTYARD (overgrown garden, benches)          |
  |  |  3    |                                                       |
  |  |storeys|                               AMBULANCE BAY (canopy)  |
  |  |14 x 40|                                                       |
  |  | stair |   CAR PARK (wrecks)                                   |
  |  +-------+                                                       |
  |  ======== GATE (you start) ========          breach in fence     |
  +---------------------------------------------------------------+
```

### Rising difficulty

Difficulty still sets only how good the bots are, how long the round lasts and what everyone carries, never how many
there are. Going up the ladder:

- **The places get harder**: more height, more doors, darker interiors, shorter sightlines that end in a corner.
- **The opponents' mix gets harder**: Oxbarrow Works keeps its sentries, patrollers and rushers; the Rail Yard adds
  Marksmen; the Cold Store adds Flankers; the Hospital Wing has both.
- **The clock and paint get tighter**: each level's own Easy, Normal and Hard tiers (in `levels/ladder.jsonc`) give a
  little less time and a pod less than the level before.

## How it will look

The new places reuse the 30 photographic materials where they fit (concrete, brick, corrugated sheet, rust, wood,
gravel, tarp, glass) and the dressing systems built in Phase 2 (weeds, cracks, damp, graffiti, old paint, debris,
cobwebs, run-off, birds, sunbeams through holes in the roof), which work on any level made from the kit.

- **New materials** from Higgsfield texture sheets (four per picture): rail ballast, creosoted sleepers, bauxite
  wagon paint, cinder ground; white insulated panel, quarry tiles, painted floor; hospital green and cream paint,
  white wall tiles, worn linoleum. Until they're generated, the shader's procedural look stands in.
- **New props**, each with its colliders in data and a detail model built in code (like the Phase 2 props): box vans,
  open, flat and tank wagons, a shunter, buffer stops, sleeper and rail stacks; lorry trailers you can walk through,
  tall pallet racking, roll cages, compressors, ammonia tanks; hospital beds, trolleys, wheelchairs, lockers, ward
  curtains (paint goes through them, sight doesn't), benches, an ambulance wreck. A few get generated models.
- **Doors** are built in code: panel doors, steel doors, cold-room doors and double swing doors with wired-glass
  vision panels, with frames, handles and kick plates, and the paint that hits them moves with them.

## How it will sound

- **Sound effects are synthesised in code**, as now, but a full set: Higgsfield's audio tools only make speech (its
  sound-effect model is reserved for its own game pipeline). Each sound is built from its physics (struck metal
  rings, wood knocks, gravel crunches as many small grains) with several variations so nothing repeats exactly.
- **Positional 3D audio** with air absorption over distance (distant shots lose their crack), muffling through walls,
  and a reverb that follows where you are (dry outside, a long ring in the engine shed, close and boxy in a ward).
- **Voices** from Higgsfield's text-to-speech: six voices (two per character model: two male voices for each of the
  first two models, two female for the third), about 35 lines each, plus a referee for "Game on!", the time
  warnings and "Game over!". Each line is imported with its provenance like the art, and a missing line falls back
  to its subtitle.

## Higgsfield budget

About 297 credits are left. Speech is cheap (0.15 credits a line):

| Item | Credits |
|---|---|
| Voices: six voices × about 35 lines, plus the referee (about 220 lines) | ≈ 35 |
| Three texture sheets (twelve materials) | ≈ 13 |
| Up to four generated props (one picture of six, then Tripo H3.1 at 18 each) | ≈ 77 |
| **Phase 3 total** | **≈ 125 (cap 150)** |
| Already approved in Phase 2: four props (tyres, sandbag wall, pallet stack, generator) and the crouched walk | 80 |

Generations are limited to five a day, so the art arrives over several days; everything works before it does.

## Scope

**In:**

- Profile, unlocks and records (local save).
- Doors that open and close, for you and the bots.
- Marksman and Flanker behaviours; bots sharing what they see.
- Objectives: retrieve and hold, in solo and teams.
- Three new levels with their kit (buildings, props, materials, tracks).
- The audio pass: synthesised effects, ambience, reverb, voiced callouts.
- The full settings menu with rebinding for mouse, keyboard and pad.
- Tests, CI runs on every level, and the report.

**Out (later phases):**

- Multiplayer (Phase 4). The sim stays ready for it: doors, objectives and the profile's choices all go through the
  same commands and events.
- Gear locker, brands, the speedball field, CTF and Arcade (Phase 5).
- Progression and economy (Phase 6).
- Objectives in free-for-all (it stays last one standing).

## Milestones

Each milestone is pushed as it lands, with screenshots.

### M3.1 Save and unlocks

**Done 2026-10-05.**

- [x] A profile in `user://profile.json`: unlocked levels, and per level, mode and difficulty your rounds, wins, best
  time to clear, best accuracy and most eliminations; plus your last level, mode, size and difficulty (the objective
  joins them with M3.4). An unreadable file is kept aside as `profile.bad.json` and you start afresh.
- [x] The unlock rule in `levels/ladder.jsonc` (`unlock.minTier`): a level opens when you win a round on the one before
  it (any mode, any difficulty by default; the rule names the lowest difficulty that counts, so it can be made stricter).
  Accuracy only counts from rounds of at least 10 shots (`records.accuracyMinShots`).
- [x] Level select: locked levels say what opens them; open ones show your record for the mode and difficulty picked
  (won of played, fastest win, best accuracy, most eliminations) and the difficulties you've won on. It opens on the
  level you last played, else the newest one you've opened, with your last choices.
- [x] Settings → "Open every level", and `-- --unlock-all` (CI's menu check uses it, so it sees every level).
- [x] The summary says when a round opened a new level.
- [x] The main menu's backdrop shows the newest level you've opened (a camera drift per level in
  `presentation.jsonc` → `menuBackdrop.shots`).
- [x] Twelve sim tests for the rules (what opens what, losses, a stricter rule, records, "open every level", the newest
  level, the file's round trip and a broken file) and the ladder data check. CI's bot match now saves its round to the
  profile (`--record`), so writing the file is exercised too.

### M3.2 Doors

- [ ] Door openings can have a leaf (`"leaf"` on an opening of kind door): hinged (from one side, swinging one way
  or both) or sliding, single or double, its material, and how it starts: shut, open, ajar or random each round.
- [ ] Doors are part of the sim (`DoorSet`), moved by commands like everything else: paint hits them (and the splats
  move with them), they block sight, and a door stops rather than pushing through someone in its way.
- [ ] Interact (F; on a pad, the refill button while you're facing a door) opens or shuts the door you're facing
  within reach; holding it eases the door open a little at a time, for a peek.
- [ ] Opening and closing make a noise event, so bots hear doors (louder for heavy doors), and the audio plays it.
- [ ] Bots open closed doors on their path, and a bot searching or investigating notices a door that's moved.
- [ ] Walking collision follows each leaf, and door meshes are built in code (panel, steel, cold-room, double swing
  with vision panels), with frames, handles and kick plates.
- [ ] Oxbarrow Works gets doors on its guardhouse, offices, pump house and warehouse.
- [ ] Tests: a shut door stops a ball and sight, an open one doesn't; interact toggles only the door in reach; a bot
  walks through a shut door to its goal; doors move deterministically; stepping stays allocation-free.

### M3.3 Marksman and Flanker

- [ ] Bots share contacts: a bot that spots someone shouts, and teammates within earshot get where they were as a
  lead (with a little error), so they can react without seeing you themselves.
- [ ] **Marksman**: at load, every cover point and spawn is scored for its view (how far you can see from it, over
  how wide an arc, with a bonus for height). A Marksman takes the best vantage near its start, scans the open
  ground slowly, engages from long range only with its aim settled (single, careful shots), and moves to another
  vantage after a few shots or when balls start landing near it.
- [ ] **Flanker**: when it hears of you from a teammate (or loses you), it picks a spot off to your side and goes
  there by the route you can see least of (candidate routes scored for how much of them is in your sight), walking
  and crouching near the end, holding fire until it's in position unless it's spotted first.
- [ ] Both in `bots/archetypes.jsonc`, with the new keys documented; the open modes' role chances include them.
- [ ] Tests: vantage scores favour long, high views; a Marksman holds a vantage and hits at 40 m; it moves after
  being shot at; a Flanker's route is less exposed than the direct one and it arrives off the line of fire; a
  contact shout gives teammates a lead and no one else.

### M3.4 Objectives

- [ ] The menu offers an objective for solo and teams: **Eliminate** (as now), **Retrieve** or **Hold**. Each level
  lists its case spots, its ways out and its rooms to hold (`"objectives"` in the level file).
- [ ] **Retrieve** (`rules.jsonc` → `objectives.retrieve`): the case starts at a random spot each round, in a marked
  building; walk over it to pick it up; carry it to any way out to win; a carrier can't sprint; if the carrier is
  hit it drops where they fell, and a teammate can pick it up. Defenders start near it (some guarding it), and once
  it's taken they know where it is every few seconds and go after it or cut off the ways out.
- [ ] **Hold** (`objectives.hold`): a room is picked each round; your side holds it while at least one of you is in
  it and none of them are; 60 s held in all wins; progress doesn't run while it's contested. Defenders start in and
  round it; once you're in, they counter-attack.
- [ ] Your bot teammates play the objective too: they go for the case and escort its carrier, or take the room and
  hold it from its cover.
- [ ] HUD: a marker on the objective (the case's building, the case itself once seen, the ways out while you carry
  it, the room) with its distance; the hold progress bar; toasts and callouts when the case is taken or dropped and
  when the room is contested. The briefing card says what to do and its map shows the ways out and the room; the
  summary says how the objective went.
- [ ] Tests: each way to win and lose each objective; the case drops and can be picked up again; contested time
  doesn't count; defenders start near the objective; bots deliver the case and hold the room in headless rounds.

### M3.5 The Rail Yard

- [ ] Kit: tracks (rails and sleepers along a polyline), the engine shed, signal box, goods shed with its platform
  and canopy, yard office, footbridge, lamp hut; wagons (box van, open, flat, tank), a shunter, buffer stops,
  sleeper and rail stacks, with detail models in code.
- [ ] The level file: spawns (with Marksmen on the high spots), patrols, pickups, case spots, ways out, rooms,
  areas, viewpoints; its ladder tiers; its scenery (a main line running past beyond the wall).
- [ ] Checked in CI: the walk-through (stairs to the signal box, footbridge and gantry), and bot matches in all three
  modes, without errors. Screenshots from its viewpoints.

### M3.6 The Cold Store

- [ ] Kit: the cold store (chambers, aisle, raised dock hall with steps and a ramp, dock doors), plant room, offices;
  trailers you can walk through, tall racking, roll cages, compressors, ammonia tanks; insulated-panel materials.
- [ ] Dark interiors (area light levels), heavy doors with their own sound.
- [ ] The level file and tiers as above, checked in CI the same way, with screenshots.

### M3.7 The Hospital Wing

- [ ] Kit: the two three-storey wings (wards, side rooms, theatre, nurses' station, stairwells, lift shaft, the
  fallen end), the boiler house and chimney, the ambulance canopy; beds, trolleys, wheelchairs, lockers, curtains,
  benches, an ambulance wreck; hospital materials.
- [ ] The level file and tiers as above, checked in CI the same way, with screenshots.

### M3.8 Sound and voices

- [ ] Audio buses (master, effects, voices, ambience, menus) with their volumes in settings.
- [ ] Synthesised effects, several variations each: the marker report (pitch and body following tank pressure),
  breaks and bounces per surface (player, metal, wood, concrete and brick, glass, ground, tarp, inflatable),
  footsteps per surface and pace (and in water), slides, jumps and landings, doors per kind, the loader and
  refills, pickups, being hit, the hit marker, the case and the hold room, menu clicks, the round's whistle and the
  last-30-seconds warning.
- [ ] Ambience: wind outdoors (following the gusts the weeds sway in), each indoor area's hum, crows, distant
  traffic and trains; mixed by where you are.
- [ ] Space: air absorption over distance, muffling when a wall's in the way, reverb by area (indoor, size).
- [ ] Voices: the cast and lines in `presentation.jsonc`, imported with `tools/art/import.sh voice` and recorded in
  `assets.jsonc` (provenance, like the art); each bot gets a voice of its model; callouts play from the caller with
  the subtitle; new callouts for flanking, pushing, a teammate out, the case and the room; the referee's lines.
- [ ] Everything off with `--no-art` falls back to subtitles, and CI runs without audio as now.

### M3.9 Settings and rebinding

- [ ] A tabbed settings menu (the same one in the main menu and the pause menu): **Controls**, **Video**, **Audio**,
  **Gameplay**, **Accessibility**.
- [ ] Rebinding: every action, two mouse-and-keyboard bindings and one pad binding each, captured by pressing the
  new key or button; clashes are shown and can be swapped; reset per action or all. Saved as overrides on top of
  `input.jsonc`.
- [ ] Controls: mouse sensitivity, invert Y, stick speed and response curve, crouch and walk as hold or toggle.
- [ ] Video: window mode, v-sync, frame cap, graphics preset plus its parts (shadows, ambient occlusion, glow,
  weeds, sunbeams, ground detail), render scale, field of view.
- [ ] Audio: the bus volumes, subtitles on or off and their size.
- [ ] Gameplay and accessibility: crosshair (on, style, colour, size), hit marker, head-bob, camera jolt, mask spray
  strength, colourblind-safe team colour sets (spec §6), HUD scale.
- [ ] Tests for the settings file (old files load, bad values are clamped) and the binding overrides.

### M3.10 Art

- [ ] The three texture sheets and the props that get generated models, imported with their provenance.
- [ ] The Phase 2 leftovers: generated tyres, sandbag wall, pallet stack and generator, and the crouched walk.

### M3.11 Verify and report

- [ ] CI green: sim tests, the menu, range and level smoke tests, and a bot match on every level and mode.
- [ ] Benchmark: the sim with ten players and 1,000 balls stays within 0.5 ms a tick on every level.
- [ ] [`docs/reports/phase-3.md`](reports/phase-3.md) with screenshots of every level, door, objective and screen.
- [ ] Your check: the ladder from Level 1 to Level 4, the new opponents, the sound, and 60 fps on Medium.

## Tests

| Test | Pass criterion |
|---|---|
| Profile and unlocks | Winning on a level opens the next and nothing else; losing opens nothing; records keep the best; the file round-trips and an old or broken one loads with defaults. |
| Doors | A shut door stops paint and sight, an open one doesn't; interact opens only the door in reach and in front; doors stop for people in their way; a bot gets through a shut door; door noise is heard through walls less. |
| Marksman | Vantage scores rank long, high views first; a Marksman holds its vantage, engages at long range and relocates after being shot at. |
| Flanker | Its route is less exposed to the enemy than the straight one, and it arrives at least 45° off the enemy's line to its teammate. |
| Shared contacts | A contact shout gives teammates within earshot a lead; opponents of the shouter get nothing. |
| Objectives | Retrieve: pick-up, drop on elimination, pick-up again, extraction wins, time up loses. Hold: progress only while held uncontested; 60 s wins; eliminations still win. |
| New levels | Every level loads without errors, its navigation reaches every spawn, patrol point, pickup, case spot, way out and room, and starts are fair for every mode and size. |
| Settings | Old settings files load; bad values are clamped; binding overrides apply over the defaults and survive a reload. |
| Determinism and cost | The same seed and inputs give the same round with doors and objectives; stepping stays allocation-free; ten players and 1,000 balls stay within budget on every level. |

## Acceptance checks

| Check | How it's shown |
|---|---|
| Climb the ladder: win on each level to open the next; progress survives a restart | You play it; sim tests for the rules; the summary's unlock screenshot |
| Three new levels of rising difficulty, each playable in every mode, size and difficulty | Level select; CI bot matches on every level in three modes |
| Marksman and Flanker behave as described | Sim tests; a scripted demo of each, screenshots |
| Objectives: retrieve and hold can be won and lost by their rules | Sim tests and headless rounds; screenshots of the HUD |
| Doors open and close for you and the bots, stop paint and sight | Sim tests; the level smoke test opens a door; screenshots |
| Audio: every event has its sound; callouts are voiced and come from the caller | Your play; the sound list in the report |
| Every action rebindable on mouse, keyboard and pad; colourblind-safe colours | Your play; settings tests; screenshots |
| 10 people in one round at ≥ 60 fps on a GTX 1070-class GPU at Medium, on every level | Your PC, with the perf overlay |

## Questions (defaults in bold)

1. **Levels.** **The three the ladder already announces (Rail Yard, Cold Store, Hospital Wing), making four.** A fifth
   can follow if you want one.
2. **Unlocks.** **Win any round on a level (any mode, any difficulty) to open the next, with an "open every level"
   switch in settings.** Stricter would be "win on Normal or harder".
3. **Objectives.** **Retrieve and hold, in solo and teams; free-for-all stays last one standing.** Your side always
   attacks and theirs defends.
4. **Doors.** **You open and close them with interact; opponents open them on their way and never lock them.** No
   kicking doors in.
5. **Voices.** **Six voices across the three character models plus a referee, about 35 lines each, from Higgsfield's
   text-to-speech.**
6. **Higgsfield spend.** **Up to 150 of the ~297 credits left on Phase 3**, plus the Phase 2 leftovers you already
   approved (80).
7. **Sound effects.** **Synthesised in code** (Higgsfield only makes speech). Recorded sounds could replace them later.
