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

**Done 2026-10-05.** [Screenshots](reports/phase-3/).

- [x] Door openings can have a leaf (`"leaf"` on an opening of kind door): hinged (from one edge, swinging one way
  or both) or sliding, single or double, and how it starts: shut, open, ajar or random each round (four in ten
  shut, three open, three ajar: `rules.jsonc` → `doors.randomStart`). The kinds of leaf are in `kit/doors.jsonc`:
  panel, flush, steel, swing and cold-room, each with its material, thickness, how fast it moves, how far it swings
  and how far it's heard.
- [x] Doors are part of the sim (`DoorSet`), moved by commands like everything else: paint hits them (and the splats
  move with them), they block sight, and a door stops rather than pushing through someone in its way.
- [x] Interact (F; on a pad, the refill button while you're facing a door) opens or shuts the door you're facing
  within reach (1.7 m, not through a wall); holding it eases the door open a little at a time, for a peek. The HUD
  says what interact will do.
- [x] Opening and closing make a noise event, so bots hear doors (louder for heavy doors), and the audio plays it
  (a creak, then a bang or a steel clang when it shuts; the full sound set comes with M3.8).
- [x] Bots open shut doors on their path: they stand clear of the swing on their side, face the door, tap interact
  and wait for it to open. A bot hears a door opened nearby, so a door that moves gives you away.
- [x] Walking collision follows each leaf, and door meshes are built in code (panel, flush, steel with a wired-glass
  vision panel, swing doors with round windows and push plates, cold-room), with handles, hinges and kick plates.
  Their paint pattern and stains move with them.
- [x] Oxbarrow Works has 22 leaves in 21 doorways: the guardhouse, the office block (a pair of swing doors at the
  front, panel doors along the corridors, a few doorways left bare), the pump house and the warehouse.
- [x] Twelve sim tests: the doors hang where the files say; a shut door stops a ball and sight, an open one doesn't; a
  tap swings only the door you face within reach; holding eases it and letting go leaves it; a door stops for someone
  in its way and carries on when they move; opening and shutting make their noises; a sentry hears a door opened behind
  it; a bot opens a shut door on its way out; starts follow the seed; stepping with doors on the move allocates
  nothing; a leaf naming an unknown door, or on a window, fails to load. The level smoke test shoots a shut door (the
  ball breaks on it), opens it with interact and walks through, in the real scene; CI's bot matches play with doors.

### M3.3 Marksman and Flanker

**Done 2026-10-05.**

- [x] Bots share contacts: a bot that spots someone shouts, and teammates within earshot (35 m, half that through
  walls) get where they were as a lead, give or take 2.5 m, so they react without seeing you themselves. It calls
  again every 3 s while it keeps you in sight. A call stays worth acting on for 4 s even if they hear or glimpse you
  themselves meanwhile (`brain.jsonc` → `calloutRange_m`, `contactError_m`, `shareInterval_s`, `contactMemory_s`).
  Anyone but a Flanker comes to help. New callouts: "flanking", "pushing", "moving" and "man down".
- [x] **Marksman**: at load every cover point is scored for its view: 12 rays across the 140° its cover faces, from
  where you'd shoot (over low cover, or stepped out past an edge), up to 60 m and never past the level's bounds,
  plus a bonus for height; each also remembers its most open direction. A Marksman takes the best vantage within
  35 m of its start, watches its most open view and sweeps slowly either side of it; in a fight it weighs vantage
  when it picks cover, fires beyond 12 m only once its aim has settled on a still target, pulls the trigger more
  slowly (×1.8), and after 6 shots from one spot, or as soon as balls land round it, moves at least 8 m and calls
  "moving".
- [x] **Flanker**: when a teammate calls you out, it picks a spot that can shoot where you are from at least 50° off
  the line between you and the caller (as near a right angle as it can), out of your sight, scored for how much of
  the walk there you could see, and goes, calling "flanking"; it crouch-walks the last 8 m and holds its fire on the
  way unless you face within 57° of it, come within 9 m or shoot at it. When it loses you it flanks as Phase 2's
  bots do, but much more often.
- [x] Both in `bots/archetypes.jsonc`, with the new keys documented. Free-for-all and Teams deal them (one in twelve
  marksmen; flankers one in twelve in free-for-all, one in six in teams). Oxbarrow Works' solo squad is unchanged:
  the new levels bring them.
- [x] Seven sim tests: vantage favours long, high views (the warehouse mezzanine beats its floor); a Marksman takes a
  top-quarter vantage within reach and watches from it; a hard one puts a still target out at 42 m in a few shots;
  it moves on, at least 8 m, once balls land round it, and calls it; a contact reaches a teammate 9 m away and not one
  70 m off; a call still counts after the teammate hears you, then goes stale; a Flanker on a call goes to a spot 58°
  off the line, along a way you see 70 % of against 100 % walking straight at you.

### M3.4 Objectives

**Done 2026-10-05.** [Screenshots](reports/phase-3/).

- [x] The menu offers an objective for solo and teams: **Eliminate** (as before), **Retrieve** or **Hold**, the ones
  the level has places for (none in free-for-all, which stays last one standing); your record is kept per objective
  too, and your last choice is remembered. Each level lists its case spots, its ways out and its rooms to hold
  (`"objectives"` in the level file). Oxbarrow Works: five case spots (warehouse floor and mezzanine, the office block
  down and up, the pump house), three ways out (the main gate, the west breach, the north-east collapse) and three rooms
  (the office's upper floor, the pump house, the warehouse mezzanine).
- [x] **Retrieve** (`rules.jsonc` → `objectives.retrieve`): the case starts at one of the spots, dealt from the round's
  seed, and the HUD marks its building; walk within a metre of it to pick it up; carry it within 4 m of a way out to
  win; a carrier can't sprint; if the carrier is hit it drops where they fell, and a teammate can pick it up. Two
  guards start right by it (and half the rest within 30 m); once it's picked up its holders learn where it is every
  5 s: the two nearest go after it (or guard it where it fell) and the rest make for the way out nearest it and wait.
- [x] **Hold** (`objectives.hold`): a room is dealt each round; your side holds it while at least one of you is in it
  and none of them are; 60 s held in all wins; the clock stops while it's contested and never runs back. Two guards
  start in it (half the rest within 25 m); while you're in it, every defender learns where you are every 4 s and
  comes for it. Either way, putting the other side out still wins and being put out still loses; the clock running
  out loses. You come in at an entry at least 38 m from the objective, so its guards can start by it fairly.
- [x] Your bot teammates play the objective whenever there's nothing more pressing: they fetch the case, carry it to
  the nearest way out (ignoring sounds on the way) or keep within 5 m of whoever carries it, or take a free cover spot
  in the room and hold it. CI's bot in your slot does the same.
- [x] HUD: a marker with the distance (the case's building until you've seen the case from within 30 m, then the
  case itself; the nearest way out while you carry it; the carrier while a teammate does; the room), held to the
  screen's edge when it's off to the side or behind; a status line under the top bar and the hold clock as a bar
  (red while contested); toasts when the case is taken, dropped or out and when the room changes hands; bots shout
  "got the case", "case is down", "they've got the case" and "they're in the room" (subtitles; voices with M3.8). In
  the world: the case (a hard case built in code, with a blinking light while it lies about, hanging at its carrier's
  side), a beam of light over each way out, and the room's outline on its floor. The briefing card says what to do
  and where (and its map shows the case's building and the ways out, or the room); the summary says how it went.
- [x] Tests (sixteen rule tests, four bot rounds, one records test): the case starts where the seed says; your side
  picks it up and carrying it out wins; theirs can't pick it up; a carrier put out drops it where they fell and a
  teammate picks it up; a carrier can't sprint; time running out loses; putting them out wins and being put out loses;
  holding alone for the hold time wins; contested time doesn't count and leaving doesn't lose what's held; free-for-all
  is always eliminate; an objective needs a level with places for it; Oxbarrow's places load, and an outdoor case spot
  or an unknown room fails to load; stepping an objective round allocates nothing; defenders start round the objective;
  a bot in your slot fetches the case and carries it out, and takes the room and holds it; the case's holders go after
  it and cut off the way out; defenders come for the room once you're in it; records are kept per objective. CI adds a
  3 v 3 retrieve and a solo hold to its bot matches, which log how the objective stands every 10 s.
- [x] Fixed on the way (doors, M3.2): bots now stand beside a doorway, clear of the swing, to open a door that swings
  their way (they used to stand just out of reach), plan their paths round leaves standing open across a way, and step
  clear of a leaf standing ajar towards them before opening it (two more door tests); and `-- --level=ID` now goes to
  that level even when the profile remembers another (M3.1).

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
