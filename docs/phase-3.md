# Phase 3 — Open areas: plan

> **Status: revised 2026-10-06 and approved the same day with the defaults ("the plan is OK go ahead"); finished
> 2026-10-08 but for your play-test.** Approved 2026-10-05 as the level ladder (you asked to "continue and build phase
> 3"), and M3.1–M3.9 and M3.11 were built ([report](reports/phase-3.md)). After your play-test you asked for the areas
> to be open, each with places to choose from, and for the opponents to stop glitching through things and walk
> clearly: that's M3.12 and M3.13, done, and M3.14 (more life in the opponents) is done too, M3.8's voices are all in
> (2026-10-07 and 08), and so are M3.10's three texture sheets (2026-10-08) and Phase 2's leftover props and crouched
> walk. You asked on 2026-10-08 for the ladders to be climbable: that's M3.15, done. M3.10's generated props for the new
> levels weren't made: Higgsfield's subscription was cancelled on 2026-10-08, taking the credits left with it, the day
> before their generations, so those props keep their models built in code. What's left is your check (M3.11). Technical design: [architecture.md §15](architecture.md#15-phase-3-open-areas).
> Everything in [spec.md](spec.md) still applies: ballistics, paint rules, gear, metric units, original IP, data files,
> tests.

## Your direction

After playing the Phase 3 build (2026-10-06):

> "Change the setting so that you have different areas to play in. It's not like a level where, if you win in
> Oxparrow, you now have to go to the new ones. You have the different areas, and in each area, you choose where you
> want to play. It's not that it's locked, and they must all be open, all the areas."

> "…the mannequins … seem a bit glitchy, like they glitch through, and when they walk, it's not very clear … make it
> more realistic."

How I've read it:

| You said | What it becomes |
|---|---|
| Different areas to play in | The four compounds are **areas**: Oxbarrow Works, the Rail Yard, the Cold Store and the Hospital Wing. |
| In each area, you choose where you want to play | Each area offers **places**: the whole of it, or one part (a building, the tracks, a courtyard), with the round kept inside that part. |
| Not locked; they must all be open | Every area and every place is open from the start, in every mode, size, objective and difficulty. Nothing unlocks; your records are only there to beat. |
| The mannequins glitch through; walking isn't clear; more realistic | Planted steps, a gait for every pace, markers that come up off walls, no walking through stairs (M3.13), and more life to come (M3.14). |

And on 2026-10-08, as you asked to finish the phase:

> "…in the railyard there was like a water tank with a ladder going up it, i want to be able to climb up that ladder,
> and wherever else there are ladders those must be climable"

That's M3.15: every ladder in the game can be climbed, by you and by the bots.

From the revised roadmap you approved with Phase 2 ([phase-2.md](phase-2.md#revised-roadmap)), the first plan's brief:

> **Level ladder:** 3–4 more compound levels of rising difficulty, unlocks and a local save, Marksman and Flanker
> behaviours, objectives (retrieve an item, hold a room), audio pass with voiced callouts, doors that open and
> close, full settings and key rebinding.

And from the spec: settings with rebindable controls for mouse, keyboard and both pad layouts, colourblind-safe
team colours (§6, §10); shot sounds that follow tank pressure, distinct break sounds per surface, footsteps per
surface and positional 3D audio (§8).

## Goal

Four abandoned compounds, every one open, each with places to play in, and a record of your best in each. Each area plays differently, opponents gain two new behaviours, rounds can be won by objectives as
well as by eliminations, doors open and close, the game sounds like a real paintball game with opponents who shout
out loud, and every setting and key can be changed. The rules and ballistics stay exactly as they are: one hit
eliminates, a bounce never does.

## What you'll be able to do

- **Play anywhere.** Pick any area, then where in it: the whole of it or one of its parts, taped off all round.
  Every area and place is open from the start, in every mode, size and difficulty, and with every objective the
  place has room for. The menu shows your best there for the mode, objective and difficulty you've picked, and
  remembers your last choices.
- **Play three new areas**, each in every mode (solo, free-for-all, teams), every size and every difficulty:
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

## The areas

All three new areas are built from the level kit like Oxbarrow Works, so one definition makes the visible meshes, walking,
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

### Places in each area (M3.12)

Each area's level file lists its places (`"places"`): the whole of it first, then its parts. A part is a rectangle on
the plan with its own ways in, a spot the eliminated walk off to, and starts closer together; its opponent spawns,
patrols, pickups and objectives are the area's that lie inside it, and a part with no case spots or rooms to hold
offers only last one standing.

| Area | Places |
|---|---|
| Oxbarrow Works | The whole works · the warehouse · the office block · the yard · the east field |
| The Rail Yard | The whole yard · the engine shed · between the wagons · the signal box and the yard office · the goods shed |
| The Cold Store | The whole depot · inside the cold store · the docks and the lorry park · the plant room and the depot office |
| The Hospital Wing | The whole hospital · the wings and the courtyard · the courtyard · the boiler house and the ambulance bay · the car park |

### How the areas differ

Difficulty still sets only how good the bots are, how long the round lasts and what everyone carries, never how many
there are. Nothing has to be played in order any more, but the areas still differ:

- **The places**: more height, more doors, darker interiors and shorter sightlines that end in a corner from Oxbarrow
  Works to the Hospital Wing.
- **The opponents' mix**: Oxbarrow Works keeps its sentries, patrollers and rushers; the Rail Yard adds Marksmen;
  the Cold Store adds Flankers; the Hospital Wing has both.
- **The clock and paint**: each area's own Easy, Normal and Hard tiers (in `levels/areas.jsonc`) give a little less
  time and a pod less than Oxbarrow Works' do.

## How it will look

The new places reuse the 30 photographic materials where they fit (concrete, brick, corrugated sheet, rust, wood,
gravel, tarp, glass) and the dressing systems built in Phase 2 (weeds, cracks, damp, graffiti, old paint, debris,
cobwebs, run-off, birds, sunbeams through holes in the roof), which work on any level made from the kit.

- **New materials** from Higgsfield texture sheets (four per picture): rail ballast, creosoted sleepers, bauxite
  wagon paint, cinder ground; white insulated panel, quarry tiles, painted floor; hospital green and cream paint,
  white wall tiles, worn linoleum. Until they're generated, the shader's procedural look stands in. *(As made on
  2026-10-08: the hospital's cream gloss paint, which the green shares, its white wall tiles, worn linoleum and the
  courtyard hedge; ballast, creosoted sleepers, a bauxite-brown planked wagon side and a black tank; white insulated
  panel, a painted concrete floor, galvanised steel and the painted timber door. Cinder ground and quarry tiles had no
  surface to go on, so the tank, the steel and the door took their places.)*
- **New props**, each with its colliders in data and a detail model built in code (like the Phase 2 props): box vans,
  open, flat and tank wagons, a shunter, buffer stops, sleeper and rail stacks; lorry trailers you can walk through,
  tall pallet racking, roll cages, compressors, ammonia tanks; hospital beds, trolleys, wheelchairs, lockers, ward
  curtains (paint goes through them, sight doesn't), benches, an ambulance wreck. A few get generated models. *(None
  did: Higgsfield's subscription was cancelled before their day; see M3.10.)*
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

235 credits were left (2026-10-06, after the Phase 2 leftovers: three props and the crouched walk); Phase 3 spent
38.45 of them (2026-10-07: five voice takes, 17.9; 2026-10-08: the last two takes and the three texture sheets, 20.55).
The other 196.42 went when Higgsfield's subscription was cancelled on 2026-10-08 (at 09:06 UTC the account moved to the
free plan and its credits were taken off), the day before the props' generations, so the props weren't made. Speech is
cheap (about 0.1 credits a line: 3.9 for a take of the 39 callouts, 2.3 for the referee's 16 lines), and a texture
sheet costs 4.25:

| Item | Credits |
|---|---|
| Voices: six voices × 39 lines, plus the referee's 16 (done) | 25.7 |
| Three texture sheets (twelve materials, done) | 12.75 |
| ~~Up to four generated props (one picture of six, then Tripo H3.1 at 18 each)~~ (not made: the subscription was cancelled first) | ~~≈ 77~~ 0 |
| ~~Two movement clips for the opponents (M3.14): the hands-up walk-off and the refill~~ (built on the poser instead) | ~~16~~ 0 |
| **Phase 3 total** | **38.45 (cap 150)** |

Generations were limited to five a day, so the art came over two days; everything worked before it did, and what
didn't come keeps the look built in code.

## Scope

**In:**

- Profile and records (local save); every area and place open (no unlocks since 2026-10-06).
- Places to play in each area (M3.12).
- The opponents' movement and gear (M3.13), and more life in them (M3.14).
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

**Done 2026-10-05. Revised 2026-10-06 (M3.12): the unlocks are gone, the records stay, kept per place.** As first
built:

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
- [x] Changed in M3.11, after the role demo showed a Flanker reach its spot and walk straight on towards you: at its
  spot it now looks out from it for 2.5 s (`brain.jsonc` → `flankLook_s`), so a flank that worked ends in a shot from
  the side, and only then searches. An eighth test has one come round unnoticed, look out and open up from its spot.

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

- [x] Kit: tracks (rails and sleepers along a polyline), the engine shed, signal box, goods shed with its platform
  and canopy, yard office, footbridge, lamp hut; wagons (box van, open, flat, tank), a shunter, buffer stops,
  sleeper and rail stacks, with detail models in code.
- [x] The level file: spawns (with Marksmen on the high spots), patrols, pickups, case spots, ways out, rooms,
  areas, viewpoints; its ladder tiers; its scenery (a main line running past beyond the wall).
- [x] Checked in CI: the walk-through (stairs to the signal box, footbridge and gantry), and bot matches in all three
  modes, without errors. Screenshots from its viewpoints.

As built: rails are paint-only primitives (you step over them; a low shot hits them), and a prop collider can be
`"paint": false`, so a wagon's underframe stops feet but not paint: you can't crawl under a wagon, but you can shoot
under it, between the wheels, at someone's legs. The engine shed's gantry got a second stair at its east end (two ways
up, for Hold), and the patrol routes keep 20 m and more from the three ways in after the first bot matches lost a
round in five seconds to a patrol passing the east breach. Tiers: Easy 14 min, 2 pods; Normal 13 min, 2 pods; Hard
11 min, 1 pod (bots carry 1, 2 and 3).

The level's first search tests found a navigation cost that would have hit every later level too: a place up on the
gantry, whose only stairs were at the far end of the shed, took up to 166,000 expanded spans to reach (60 ms and more
in one tick), and an unreachable place cost the whole search budget each time a bot asked for it. The grid now works
out walking distances from eight landmarks when the level loads (`navigation.jsonc` → `landmarks`; under a second on
the Rail Yard), which keeps searches like that under a thousand spans, refuses a goal in another connected piece
without searching, and straightens paths in doubling strides; the sim tests got faster with fourteen more of them.
`LadderLevelTests` (now `AreaLevelTests`) checks every playable level the same way (reachability from every way in, starts for every mode,
objective and size, and bot rounds), and `RailYardTests` the tracks, the wagons, the high places and the ways up.

### M3.6 The Cold Store

- [x] Kit: the cold store (chambers, aisle, raised dock hall with steps and a ramp, dock doors), plant room, offices;
  trailers you can walk through, tall racking, roll cages, compressors, ammonia tanks; insulated-panel materials.
- [x] Dark interiors (area light levels), heavy doors (their sound comes with M3.8).
- [x] The level file and tiers as above, checked in CI the same way, with screenshots.

As built: the cold store's whole floor is a solid slab 1.2 m up (a lorry's bed), so the only ways in from the yard are
up the ramp to dock 1, up the steps to the side doors, up the forklift ramp at the aisle's east end, or through a
trailer: the two backed onto docks 2 and 3 have their nose panels cut away and a loading ramp up into them (a sloped
box collider: the kit's props can carry ramps now), so each is a 13.6 m tunnel from the yard into the dock hall. Dock 4
has no trailer: its roller door is jammed half open and the drop to the yard is one way. Inside, four chambers, two
more with doors at both ends, a cross aisle and two blast freezers, all off the 56 m aisle behind eleven heavy doors
(three of them sliding), with small doors joining the chambers in pairs; the chambers' light level is 0.12 and the
freezers' 0.08, so bots see you late there and you them. Pallet racking comes in two kinds: with its middle shelf
empty, a standing player (eyes at 1.62 m) sees and shoots through it while a crouched one (eyes at 1.05 m) hides
behind the bottom load (to 1.3 m); full, it's a wall. The depot office upstairs is the Marksman's; Flankers start in the
store, the plant room and the yard. Tiers: Easy 13 min, 2 pods; Normal 12 min, 1 pod; Hard 10 min, 1 pod (bots carry
1, 2 and 3). The level smoke test now climbs every ramp too, the trailers' included. `ColdStoreTests` checks the
trailers as tunnels, the dock face nobody climbs, the dark chambers and their doors, paint under the trailers and the
racking's two heights.

### M3.7 The Hospital Wing

- [x] Kit: the two three-storey wings (wards, side rooms, theatre, nurses' station, stairwells, lift shaft, the
  fallen end), the boiler house and chimney, the ambulance canopy; beds, trolleys, wheelchairs, lockers, curtains,
  benches, an ambulance wreck; hospital materials.
- [x] The level file and tiers as above, checked in CI the same way, with screenshots.

As built: the two wings are generated by `tools/levels/hospital_wings.py` (their walls, doors, stair holes and furniture
repeat floor by floor with variations, so the script is the source: edit it and run it). Each stairwell is two flights
side by side, one per storey, with railings round the holes they climb through; the corner stairs, where the wings
meet, and the east stairs serve the north wing, the south stairs the west wing, whose corridor runs on into the north
wing's through a passage on every floor. The lift shaft is open down through every floor (a ball dropped from the top
reaches the ground floor). The north wing's east end has fallen in: the ground floor there is a ruin open to the sky,
entered through a breach in its end wall; the first floor breaks off at x 46, the top floor at x 42.5 and the roof at
x 40. The wards over the courtyard keep their beds against the corridor walls, so the windows are clear for the two
Marksmen (the women's ward and the sun ward, both on the top floor); Flankers start in the three stairwells. Curtains
round the beds hide you and stop a ball, but you walk straight through them (and your legs show under them); paint
flies under the beds and benches. In the courtyard, wild hedges are walls 1.1 m high. Tiers: Easy 12 min, 2 pods;
Normal 11 min, 1 pod; Hard 9 min, 1 pod (bots carry 2, 2 and 3). `HospitalTests` checks the floors and stairs, the
lift shaft, the fallen end, the curtains and the Marksmen's view of the courtyard; the level smoke test's duel now
falls back to any opponent with room in front of them when every sentry faces a wall. The hedges were plain coloured
boxes until M3.10 gave them a clipped hedge's photo (2026-10-08).

### M3.8 Sound and voices

**Done 2026-10-05; the voices recorded on 2026-10-07 and 2026-10-08.**

- [x] Audio buses (master, effects, voices, ambience, menus) with their volumes in settings.
- [x] Synthesised effects, several variations each: the marker report (pitch and body following tank pressure),
  breaks and bounces per surface (player, metal, wood, concrete and brick, glass, ground, tarp, inflatable),
  footsteps per surface and pace (and in water), slides, jumps and landings, doors per kind, the loader and
  refills, pickups, being hit, the hit marker, the case and the hold room, menu clicks, the round's whistle and the
  last-30-seconds warning.
- [x] Ambience: wind outdoors (following the gusts the weeds sway in), each indoor area's hum, crows, distant
  traffic and trains; mixed by where you are.
- [x] Space: air absorption over distance, muffling when a wall's in the way, reverb by area (indoor, size).
- [x] Voices: the cast and lines in `presentation.jsonc`, imported with `tools/art/import.sh voice` and recorded in
  `assets.jsonc` (provenance, like the art); each bot gets a voice of its model; callouts play from the caller with
  the subtitle; new callouts for flanking, pushing, a teammate out, the case and the room; the referee's lines.
  *Five of the seven takes were recorded on 2026-10-07 (Knox and Reid for the first model, Brooks for the second,
  Petra for the third, and Alistair as the referee: 17.9 credits), and Gideon's and Maeve's on 2026-10-08 (7.8), as the
  first day's five were used (the sixth was refused for the limit, which the refusal says belongs to the account's
  grace period): 250 lines in all, 25.7 credits. Higgsfield had refused the first take on 2026-10-05 and 2026-10-06, the days' five having
  gone on art. Every line was listened to with a speech recogniser (`tools/art/voice-check.py`): two had more in them
  than their script (Knox says "Where'd they go?" twice, and Reid "Man down." again before "They got one of us!"),
  imported again keeping just the one part (`--keep-first`, `--keep-last`); Gideon's and Maeve's say every line as
  written.*
- [x] Everything off with `--no-art` falls back to subtitles, and CI runs without audio as now.

As built: every sound is synthesised when the game starts (`game/audio/SoundBank.cs`), 87 of them in 225 variations
(about 200 s of sound), rendered on worker threads in about half a second while the briefing is up and shared by every
scene; each scene makes its own Godot streams of them (`SoundSet`) and frees them when it goes. The recipes are physical
sketches: the marker's sear click, muzzle crack, barrel ring, thump and air hiss (thinner and lower as the tank
empties); struck modes for metal, glass and wood; low-passed noise for the wet splat and soft hits; grains for gravel,
pellets and crinkling; a stick-slip squeak for hinges; a pea whistle and a two-reed breakout horn. `-- --sounds=DIR` on
the art tool (`res://tools/ArtImport.tscn`) writes them all out as WAV files to listen to. The `AudioDirector` plays
them from the sim's events: your marker in 2D; everything else from where it happened through a pool of 32 3D players
(at most 10 new ones a frame), dulled with distance and muffled (10 dB quieter, low-passed at 900 Hz) when the sim's
collision says something is in the way. What a surface sounds like is data (`presentation.jsonc` "audio" →
"surfaces"); footsteps are as loud as the sim says they carry, and splash in puddles. The `Ambience` mixes by the area
you're in: indoors the Outside bus (wind, traffic, crows, trains) closes to 700 Hz and drops 10 dB, the area's tone
plays (levels' areas now take an optional `"tone"`: room, hall, drip, pigeons, draught, cold or hum; the cold store's
chambers are cold, the wards draughty, the engine shed and warehouse full of pigeons), and the world's reverb grows from
a small room's to a hall's by the area's volume. The referee (`RefereeCalls`) blows the horn and calls "Game on!" at
the breakout, a minute and thirty seconds (with two pips) from time, the whistle and the result at the end, "You're
hit!" when you're out and the room in a hold. Voices are art: each line is a file, `art/voices/<voice>_<its
words>.ogg`, found by its words, so a missing one is a subtitle only. A voice is imported as one take of its whole
script, cut into lines at its pauses (`game/tools/VoiceSplitter.cs`), choosing the pauses that give each line the length
its words suggest, so a comma's pause isn't cut even when it's as long as a line break (tested with ffmpeg's own speech
synthesiser and on twelve made-up takes in CI); each line is levelled, faded and encoded as OGG. The first real takes
(2026-10-07) showed two more things. A take can run two lines almost together (30 ms apart), so the little kept
either side of a line now stops at the cuts either side, and no line ends with the start of the next one's first word.
And a take sometimes says more than a line's words (the line twice, or another line's words again inside it):
`--keep-first="LINE"` or `--keep-last="LINE"` on the import keeps the line's first or last part, cut at the longest
pause inside it, and its record says so. CI's art self-test checks both on its made-up takes. `tools/art/voice-check.py`
listens to every line of a voice with a speech recogniser (Whisper, run locally) and lists any whose words
aren't the script's, letter by letter, so spacing and "your" for "you're" pass but a doubled, stray or missing word
doesn't. Headless runs (CI)
build and count every sound but start none (Godot's dummy driver never retires a playback), and the level smoke tests
now check that shots, breaks, steps and doors were all heard and the referee called the round. Tests: the areas' tones
are known, a bad one fails to load, every voice file is on record and every record's files are there, and voices on
record belong to the cast; presentation's validation checks that no two lines share a voice file's name.

### M3.9 Settings and rebinding

**Done 2026-10-05.** [Screenshots](reports/phase-3/).

- [x] A tabbed settings menu (the same one in the main menu and the pause menu): **Controls**, **Video**, **Audio**,
  **Gameplay**, **Accessibility**.
- [x] Rebinding: every action, two mouse-and-keyboard bindings and one pad binding each, captured by pressing the
  new key or button; clashes are shown and can be swapped; reset per action or all. Saved as overrides on top of
  `input.jsonc`.
- [x] Controls: mouse sensitivity, invert Y, stick speed and response curve, crouch and walk as hold or toggle.
- [x] Video: window mode, v-sync, frame cap, graphics preset plus its parts (shadows, ambient occlusion, glow,
  weeds, sunbeams, ground detail), render scale, field of view.
- [x] Audio: the bus volumes, subtitles on or off and their size.
- [x] Gameplay and accessibility: crosshair (on, style, colour, size), hit marker, head-bob, camera jolt, mask spray
  strength, colourblind-safe team colour sets (spec §6), HUD scale.
- [x] Tests for the settings file (old files load, bad values are clamped) and the binding overrides.

As built: `SettingsMenu` (`game/ui/`) builds the five tabs from `UiKit`'s rows; every change is saved at once and
applied where it can be (window, v-sync and frame cap straight away, the graphics and the HUD through their owners,
team colours from the next round). The Controls tab lists every action of `input.jsonc` (which now gives each a label
and a group: movement, combat, shortcuts) with its two keyboard-and-mouse slots and its pad slot (`BindingsList`): click
a slot and press the key, mouse button, pad button or stick (Esc cancels, Backspace empties it); a binding another
action has turns red, with an offer to swap the two (the pad's X stays shared by refill and doors, as the defaults
have it); Reset per action and for all. A binding is text (`key:W`, `mouse:Left`, `pad:A`, `axis:TriggerRight+`), so
the bindings (`BindingSet`) and the settings (`GameSettings`, its file part) are plain C# that the sim test project
compiles too, with `GameSettings.Godot.cs` doing the file, the defaults from the data and the window; `input.jsonc`
stays the defaults and only your changes are saved. A graphics part left "as the preset" follows the preset;
shadows can be turned off or take another preset's size and distance. The crosshair comes in four styles (ticks and a
dot, ticks, a dot, a ring), seven colours and 50–250% sizes. Team colours: the standard set, a red–green safe one
(Okabe and Ito's palette) and a blue–yellow safe one (Paul Tol's "bright"), in `presentation.jsonc` "teamColorSets".
The HUD lays itself out at its scale across the whole screen, so the corners stay in the corners. Old settings files
(version 1) load with the new choices at their defaults. The F-key shortcuts still work and now save to the same
settings (F11 switches between windowed and borderless full screen). Nine sim tests: a Phase 2 settings file loads,
bad values are put right, an unreadable file gives the defaults, settings and bindings survive a reload, overrides
apply over the defaults, a taken binding clashes and swaps, resets put the defaults back, and a binding in the wrong
slot is refused; the menu smoke test checks the five tabs and a slot for every binding.

### M3.10 Art

- [x] The three texture sheets, imported with their provenance (2026-10-08, 12.75 credits).
- [ ] **Not made:** the props that get generated models. Higgsfield's subscription was cancelled on 2026-10-08, taking
  the 196.42 credits left with it, the day before their generations (a picture of six props, then up to four models).
  Every new prop keeps the detailed model built in code. If generations come back, the importer is ready for them.
- [x] The Phase 2 leftovers: generated tyres, sandbag wall and generator, and the crouched walk (2026-10-06, made by
  the Phase 2 session's own check-in in [Tim-D-W101/Pb#5](https://github.com/Tim-D-W101/Pb/pull/5), brought into Phase
  3 on 2026-10-08). The pallet stack's generation failed (refunded) and the day's five were spent, so both pallet stacks
  keep their models built in code.

As built (2026-10-08): three 2 × 2 sheets from GPT Image 2.5 (4K, high quality, the Phase 2 sheets' framing), one per
level, each swatch cut with `--region` from inside its white lines. The hospital's: cream gloss paint on old plaster
(`paint_cream`, and `paint_green` as the same photo tinted mint), white glazed wall tiles (`tiles_white`, cut to 4 × 4
whole tiles between grout lines, 0.6 m), worn grey-green linoleum (`lino`) and a clipped privet hedge (`hedge`). The rail
yard's: crushed granite ballast (`ballast`, 1 m, so its stones come out 6 to 9 cm), creosoted sleeper timber
(`sleeper_timber`), a bauxite-brown planked wagon side (`wagon_brown`, 4 whole planks cut above the bolt row, 0.68 m) and
a black tank wagon's shell (`tank_black`, cut between its riveted bands). The cold store's: white insulated panel
(`panel_white`, 5 whole ribs, 1.75 m), a painted concrete floor with tyre marks (`floor_painted`), galvanised steel
(`steel_galvanised`, the pylons, cabinets and compressors) and the painted timber door (`door_wood`, every panel door,
cut between its frame edges). Regular patterns are cut to whole repeats and blended in step with them (`--repeats`); the
rest break up their repeat. The tints that stood in are gone. Grey wagon steel, the shunter's green, sooty brick, the
white cladding and trailers, the render and the drawn materials (ammonia tanks, curtains, mattresses, the ambulance,
steel doors) keep their stand-ins.

Phase 2's leftovers came in on 2026-10-08 with their refitted colliders: the tyres 0.90 m across (were 0.74), the
generator 1.77 × 1.29 m (was 1.8 × 1.0) and the sandbag wall 1.90 × 0.72 m (was 2.4 × 0.6), each scaled to its
collider's height, so cover is as it was. The three new levels were laid out with the old sizes, so each of the
props' 26 placements in the four areas and their buildings was probed round its new outline: none runs into a wall or
another prop. The crouched walk plays when the opponents' legs are clips (`characters.legs` = `"clips"`); the planted
steps, which have been their legs since M3.13, have a crouched walk of their own.

### M3.11 Verify and report

**Done 2026-10-05, but for your check.** [Report](reports/phase-3.md).

- [x] CI green: sim tests, the menu, range and level smoke tests, and a bot match on every level and mode (solo and
  free-for-all on all four, teams with an objective on all four).
- [x] Benchmark: the sim with ten players and 1,000 balls stays within 0.5 ms a tick on every level (0.40–0.44 ms).
- [x] [`docs/reports/phase-3.md`](reports/phase-3.md) with screenshots of every level, door, objective and screen.
- [ ] Your check: every area and its places, the new opponents, the sound, the ladders, and 60 fps on Medium. *(Was "the
  ladder from Level 1 to Level 4" until 2026-10-06.)*

As built: `-- --role-demo=marksman` and `=flanker` (`game/core/RoleDemo.cs`) show the two new roles at work with the
F3 overlay, on the sim's clock so they can be captured at a low frame rate, and CI runs both as smoke tests: the
Marksman on the Rail Yard has to spot you 40 m down its view and open up, the Flanker on the Hospital Wing has to go
round on its teammate's call and look out from its spot. The Flanker demo finds where to stand with
the brain's own flank-spot search (now a public query, `BotBrain.FlankSpot`): somewhere a teammate of a Flanker sees
you, within earshot of it and out of its sight, with a way round to your side wherever within the call's 2.5 m error
the call puts you. Scripting it showed the Flanker reach its spot and walk on towards you (M3.3 above, now fixed). The
menu's backdrop had a camera drift for Oxbarrow Works only, so it never moved on as you climbed (M3.1 promised the
newest level you've opened); the three new levels have one now. A bot match can be filmed through to its summary (`--fast --show-summary` at `--fixed-fps 1`), which is how the summary's "new level
open" was captured; and a level that fails to load now quits a headless run instead of waiting on its error screen
(asking for teams at size 6, for instance, which is 6 a side).

### M3.12 Open areas and places

**Done 2026-10-06**, after your play-test. [Screenshots](reports/phase-3/).

- [x] The ladder is gone: `levels/ladder.jsonc` became `levels/areas.jsonc`, the areas in menu order, every one open,
  each with its difficulty tiers and the roster scripted runs fill. The unlock rule, Settings → "Open every level",
  `--unlock-all` and the summary's "new level open" went with it. A save from the ladder still loads: its records
  count as the whole area's, and what it had opened no longer matters.
- [x] Each level lists its places (`"places"`, the whole of it first, called `whole`; see the table above).
  `LevelLayout.ForPlace` makes the level for a round in a part: four walking-only walls on its edge (paint and sight
  pass over them), its own ways in and walk-off spot, and only the opponent spawns, patrols, pickups, viewpoints,
  case spots and rooms inside it; ways out inside it, else the way you came in. Starts are dealt as before, at
  `spawnScale` times the distances, in the area's spawn ground inside the place; when the spawns inside run short,
  at cover. Every door and track stays.
- [x] Red-and-white tape on posts (`PlaceBoundary`, `presentation.jsonc` → `placeBoundary`) wherever a walk could cross
  a part's edge: open ground, doorways, gaps between props, tied off at walls. It's surveyed against the walking
  geometry, so it never crosses a window or a roof.
- [x] **Where to play**: the four areas in a row, then the chosen area's card: where in it (the places down the left,
  with what each is), then the mode, size, objective (those the place has room for), difficulty, your record there,
  and Start. Back sits by the title. The briefing names the place, frames its map on it with the rest dimmed and the
  edge taped, and says what's lying about there.
- [x] Records per area, place, mode, objective and difficulty (`RecordBook`, user://profile.json).
- [x] Tests: every place of every area keeps everything inside it and has a way from each entry to each spawn, case
  spot and room inside, and none out; every mode, size and objective starts inside it, apart and on walkable ground;
  bad places are named by file and key; records by place, and a ladder save loading. The menu smoke test picks every
  area and every place in turn; CI plays a bot match in a part of each area.

### M3.13 The opponents: planted steps and gear off the walls

**Done 2026-10-06**, after your play-test. [Screenshots](reports/phase-3/).

- [x] **Planted steps** for the legs (`presentation.jsonc` → `characters.legs` = `"steps"`; `"clips"` brings the walk and
  run clips back). Each foot is planted where it lands until its next step, whichever way the body moves, at any
  pace, turning on the spot or on stairs. A gait table by speed sets the cadence, the time each foot is in the air,
  the lift, the stride and the bob, so a stroll, a walk, a run and a sprint each look like themselves. The heel peels
  up behind and the toes come up to land; the hips drop when a leg needs to reach; running is on bent knees.
- [x] Standing, a shooter's stance, bladed and staggered; turning, steps round instead of spinning on the spot.
  The idle clip's 42° turn of the hips is taken back out, so the stance comes from the steps.
- [x] Bots stroll on patrol, back to a post and walking off (1.65 m/s); guarding an objective they walk briskly.
- [x] **Gear off the walls**: pressed up to a wall or a crate, the arms and marker come up off it just far enough to
  clear it (`hitboxes.jsonc` → `tuck*`). The sim does it, so the hitboxes come up too (what you see is what you can hit,
  lag compensation included); it's set at once during the briefing.
- [x] **No walking through stairs**: walking blocks under every flight, so nobody walks into one from below or the side.
- [x] `-- --gait-demo` measures how far the planted feet move while they're down, and how far an ankle falls short
  (walking: 0.12 m/s and 0.0 cm, most of it the heel-to-toe roll).

### M3.14 The opponents: more life

**Done 2026-10-06**, but for the voices (M3.8). [Pictures](reports/phase-3/).

- [x] **Standing:** the weight goes from foot to foot every few seconds and the chest breathes, quicker after a
  sprint; they glance round now and then; the head turns towards a noise (or wherever they're turning to look) before
  the body follows; on a post the view is swept a look at a time: one way, back, the other way, each look held.
- [x] **In cover:** crouched with cover just ahead, the knees go out to the sides and the feet back, and the elbows
  come in and back along the body, instead of into it. *A shoulder to a wall edge before peeking* needed nothing new:
  the cover point is at the edge and the peek already steps out past it and leans.
- [x] **Hit:** the flinch, then the marker up over the head in the trigger hand and the other hand up, open, and the
  walk off like that, with "Hit!" over them. Built on the poser rather than as a generated clip, so it works with the
  planted steps and saved the clip's 8 credits; the refill already had its hand to the pods and up to the loader, so
  that clip was left out too (8 more).
- [x] **Running into a stop:** the foot in the air comes down short into the stance and the other follows at once,
  while the hips dip into the knees and the upper body tips on over the feet and settles back.
- [x] **A still of each place in the menu**, under the list of places, from the viewpoint the level file names for it
  (`"still"`), else the first inside it. What's there moved to the head of the right-hand column, and Start beside your
  record, so the card still fits on a 1600 × 900 screen (the menu's smoke test now checks every place's card does).

As built: the head is the sim's, so its hitboxes turn with it: `InputCommand.HeadYaw` (rad from the aim, positive to the
left, up to `movement.jsonc` → `maxHeadTurn_deg`, 70) goes into `PlayerState.HeadYaw` and the hitbox pose; the head box
turns in place and the mask goes round it, the marker staying on the aim. Bots turn theirs at `brain.jsonc` →
`headTurnSpeed_degps` towards where the body is turning (so they look at a noise first), add a glance when nothing's going
on (`glance*`), and see where they look (`BotSenses` uses the head's direction). Glances and the post sweep (`scanHold*`)
draw from their own random numbers, so nothing else a bot decides changes. Your head is your view, so it stays on the
aim. The stop (`presentation.jsonc` → `characters.steps.stop*`), the weight (`steps.weight*`), the knees and elbows in
cover (`steps.knee*`, `steps.elbow*`), the breathing (`characters.breath*`) and the raise when out (`outRaise_s`,
`outHand*`) are drawing only. `-- --cover-demo` tucks an opponent in behind low cover and prints how far its knees and
elbows got into it: at the low cover by Oxbarrow's guardhouse, 12.8 cm and 6.0 cm before, 0 now; out in the open on the
Rail Yard, 4.0 cm of knee before, 0 now. `-- --gait-demo` ends with a look round from in front (`--gait-only=look` plays
just that). `-- --place-stills=DIR` takes the menu's pictures (`game/ui/places/`).

### M3.15 Ladders

**Done 2026-10-08, at your request.** Five ladders were drawn and none could be climbed: on the two water towers (Oxbarrow
Works and the Rail Yard), one up the legs and one up the tank, and up the side of each of the Rail Yard's three tank wagons.

- [x] **Ladders are data** on their props (`kit/props.jsonc` → `"ladders"`: where the foot is, how high it climbs, which
  way a climber faces, its width, handholds above the top, how far stepping off takes you), placed with the level and
  drawn from the same numbers, so what you climb is what you see.
- [x] **Climbing** (`movement.jsonc` → `"climbing"`): facing a ladder within reach, **F** gets you on (on a pad, the
  refill button, as for doors); **W** climbs and **S** climbs down, 1.6 m/s, so the 12 m water tower takes about 8 s; at
  the top you step off onto what it climbs to, at the bottom onto the ground; **Space** lets go. Facing out over a
  ladder's top, F gets you on to climb down. On a ladder both hands are on the rungs: no firing or refilling, no
  crouching, leaning or sprinting, and the marker is slung on your back (the hitboxes too, so paint hits a climber where
  they're drawn). You can look round as you climb. A foot on a rung is heard every other rung. The HUD says what F does.
- [x] **Somewhere to stand at the top.** The water tower's tank filled its platform, so the legs now stand under the
  corners of a 6.2 m catwalk round the tank, with railings (they keep you on it; paint passes them) but for a gap where
  the ladder comes up, and the tank has a flat roof with a hatch, up the second ladder. The tank wagon's walkway along
  its top is solid now, and its ladder starts at a step just off the ground.
- [x] **Bots climb too:** a ladder joins its foot to its top in their navigation, so a bot whose path goes up (to hunt
  you on the catwalk, say) walks to the foot, faces the ladder, gets on and climbs, and comes back down the same way.
  Out on a ladder, it climbs down before walking off.
- [x] **The climbers look the part:** hands and feet on the rungs, opposite hand and foot stepping two rungs at a time,
  the hips in towards the ladder, the marker on the back; in first person the marker drops out of sight.
- [x] **Tests:** thirteen sim tests (every ladder has ground at its foot, a floor at its top and room for a body up it
  and over the top; up the tower to the catwalk, on to the roof and back down, and down from the catwalk; getting on
  needs facing it within reach; no firing on a ladder; jump lets go; the eliminated climb down; rung footsteps; no
  allocation; bots' paths climb both ladders; a bot on patrol climbs the tower and back; a bad ladder fails to load).
  CI's level walk-throughs climb every ladder up and down through the real scene (all seven pass).

As built: `LadderSpec` and `LadderSet` (`src/Pb.Sim/Level/Ladders.cs`) are the ladders in world space and the rule for
getting on; climbing is a branch of `MovementModel` (`Climb`), with the climber's state in `PlayerState.Ladder` and
`LadderPhase`, so the game's bodies, the headless mover and the network later all climb alike. The navigation grid
links each ladder's ends (`navigation.jsonc` → `ladderCostPerMetre`, 3.5), its landmarks placed by walking alone so ground
paths search as before. The pose is `LadderLimbs` and `CharacterPoser.LadderBody`, with its numbers in
`presentation.jsonc` (`ladder*`) and the slung gear in `hitboxes.jsonc` (`climbArmsPitch_deg`, `slung*`).
`-- --ladder-demo` (with `--ladder=N`) films an opponent climbing the level's tallest ladder from behind and to the side.

## Tests

| Test | Pass criterion |
|---|---|
| Profile and records | Records keep the best per area, place, mode, objective and difficulty; the file round-trips; a ladder save, or an old or broken one, loads. Every area and place is open. |
| Places | Every part of every area keeps what's inside it, its entries reach everything inside and nothing outside, and every mode, size and objective starts inside it fairly. |
| Doors | A shut door stops paint and sight, an open one doesn't; interact opens only the door in reach and in front; doors stop for people in their way; a bot gets through a shut door; door noise is heard through walls less. |
| Marksman | Vantage scores rank long, high views first; a Marksman holds its vantage, engages at long range and relocates after being shot at. |
| Flanker | Its route is less exposed to the enemy than the straight one, and it arrives at least 45° off the enemy's line to its teammate. |
| Shared contacts | A contact shout gives teammates within earshot a lead; opponents of the shouter get nothing. |
| Objectives | Retrieve: pick-up, drop on elimination, pick-up again, extraction wins, time up loses. Hold: progress only while held uncontested; 60 s wins; eliminations still win. |
| New levels | Every level loads without errors, its navigation reaches every spawn, patrol point, pickup, case spot, way out and room, and starts are fair for every mode and size. |
| Settings | Old settings files load; bad values are clamped; binding overrides apply over the defaults and survive a reload. |
| Determinism and cost | The same seed and inputs give the same round with doors and objectives; stepping stays allocation-free; ten players and 1,000 balls stay within budget on every level. |
| Ladders (M3.15) | Every ladder has ground at its foot, a floor at its top and room to climb; you get on facing it within reach, climb, step off at the top and climb back down; no firing on one; bots' paths climb ladders and a bot does. |

## Acceptance checks

| Check | How it's shown |
|---|---|
| Every area and every place in it open from the start; records survive a restart | The menu smoke test picks every area and place; you play it |
| Three new areas, each playable in every mode, size and difficulty, and in each of its places | Where to play; CI bot matches on every area in three modes, and in a part of each |
| The opponents walk, run and turn on planted feet and keep their gear out of walls | The gait demo's measurements; screenshots; you watch them |
| Marksman and Flanker behave as described | Sim tests; a scripted demo of each, screenshots |
| Objectives: retrieve and hold can be won and lost by their rules | Sim tests and headless rounds; screenshots of the HUD |
| Doors open and close for you and the bots, stop paint and sight | Sim tests; the level smoke test opens a door; screenshots |
| Every ladder can be climbed, by you and the bots (M3.15) | Sim tests; the level smoke test climbs every ladder; the ladder demo |
| Audio: every event has its sound; callouts are voiced and come from the caller | Your play; the sound list in the report |
| Every action rebindable on mouse, keyboard and pad; colourblind-safe colours | Your play; settings tests; screenshots |
| 10 people in one round at ≥ 60 fps on a GTX 1070-class GPU at Medium, on every level | Your PC, with the perf overlay |

## Questions (defaults in bold)

1. **Areas.** **Four: Oxbarrow Works, the Rail Yard, the Cold Store and the Hospital Wing.** A fifth can follow if
   you want one.
2. **Places.** **The parts in the table above, every one open.** Say if you'd like a place added, moved or split
   (each is a rectangle in the level file, so it's quick to change).
3. **Objectives.** **Retrieve and hold, in solo and teams; free-for-all stays last one standing.** Your side always
   attacks and theirs defends.
4. **Doors.** **You open and close them with interact; opponents open them on their way and never lock them.** No
   kicking doors in.
5. **Voices.** **Six voices across the three character models plus a referee, about 35 lines each, from Higgsfield's
   text-to-speech.**
6. **Higgsfield spend.** **Up to 150 of the 235 credits left on Phase 3** (now including the opponents' two clips).
   *(38.45 were spent; the rest went with the subscription, cancelled on 2026-10-08.)*
7. **Sound effects.** **Synthesised in code** (Higgsfield only makes speech). Recorded sounds could replace them later.
8. **More life in the opponents (M3.14).** **All of it, with the hands-up walk-off and the refill as generated
   clips.** Or leave the opponents as they are now. *(Built 2026-10-06; the walk-off and the refill came out better on
   the poser than a clip would have, as they keep the planted steps and the marker's hitboxes, so no clips were bought.)*
