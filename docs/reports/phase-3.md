# Phase 3 report: the level ladder

**Date:** 2026-10-05 · **PR:** [Tim-D-W101/Pb#4](https://github.com/Tim-D-W101/Pb/pull/4), into `main`.
**Status:** built and tested. Three things are left:

- **your play-test** on your PC: the climb from Level 1 to Level 4, the new opponents, the sound, and 60 fps on Medium
  with ten people in a round (see [Your check](#your-check));
- **the voices**: everything for them is built and tested, but Higgsfield allows five generations a day and today's
  had gone on the last Phase 2 art before Phase 3 began. Tomorrow's five are already booked by the Phase 2 session's
  own check-in (the four generated props and the crouched walk left over from Phase 2, in their own PR), so the seven
  takes (six callout voices and the referee) come the day after. Until then the callouts are subtitles, as in Phase 2,
  and the referee's calls are subtitles with the horn and whistles;
- **the art for the new levels** (M3.10): three texture sheets (twelve materials) and up to four generated props, with
  the daily generations after the voices. Until then the new levels wear the 30 photographic materials where they fit
  and the shader's procedural look elsewhere (the hospital's hedges are plain green boxes).

The plan, with every milestone's details, is [phase-3.md](../phase-3.md); the technical design is
[architecture §15](../architecture.md#15-phase-3-the-level-ladder).

## What was built

You asked to continue and build Phase 3, the level ladder from the roadmap you approved with Phase 2. All of it is in
but the recordings and the art above.

- **The ladder and the save** (M3.1). Four compounds of rising difficulty: Oxbarrow Works, then the Rail Yard, the Cold
  Store and the Hospital Wing. Winning any round on a level opens the next (the rule is data, in `levels/ladder.jsonc`,
  so it can be made stricter). The profile keeps what's open and, for each level, mode, difficulty and objective, your
  rounds, wins, fastest win, best accuracy and most eliminations, and your last choices. Level select shows what's
  locked and what opens it, and your record; the summary says when a round opened a level; the main menu's backdrop is
  the newest level you've opened. "Open every level" in the settings skips the climb.
- **Doors** (M3.2): hinged or sliding, single or double, in five kinds (panel, flush, steel, swing and cold-room),
  starting shut, open, ajar or at random each round. They're part of the sim: paint hits them and the splats move with
  them, they block sight, and they stop rather than push through someone in the way. F (on a pad, the refill button)
  opens or shuts the door you're facing; holding it eases the door open a little at a time, for a peek. Doors make a
  noise the bots hear, and the bots open the doors on their way.
- **Marksman and Flanker** (M3.3), and bots passing on what they see: a bot that spots you shouts, and teammates
  within earshot get where you are as a lead. A **Marksman** takes the best vantage near its start (every cover point
  is scored for its view when the level loads), watches its most open view, fires at range only once its aim has
  settled, and moves on after six shots or as soon as balls land round it. A **Flanker**, on a teammate's call, picks a
  spot that can shoot where you are from at least 50° off the line between you and the caller, out of your sight, by
  the way you'd see least of, and goes, calling "Flanking!". It holds its fire on the way unless you turn towards it,
  and at its spot it looks out before it searches.
- **Objectives** (M3.4), in solo and teams: **Retrieve** (find the case in its marked building and carry it out
  through a way in; it drops where its carrier is hit) and **Hold** (take a marked room and hold it for 60 s, which
  only counts while nobody else is in it). Putting the other side out still wins. Bots guard the objective and
  counter-attack, your bot teammates play it, and the HUD marks where to go.
- **Three new levels** (M3.5–M3.7), each playable in every mode, size and difficulty, with their own kit:
  - **The Rail Yard** (150 × 80 m, 779 primitives): four tracks with rakes of wagons you can shoot under but not crawl
    under, an engine shed with a gantry, a goods shed and platform, a signal box and a footbridge for the Marksmen.
  - **The Cold Store** (100 × 80 m, 859 primitives): a dock hall 1.2 m up, reached by ramps, steps or through the
    lorry trailers backed onto it; dark chambers behind eleven heavy doors; racking you can see through standing but
    not crouched; a plant room, offices and a weighbridge outside.
  - **The Hospital Wing** (110 × 80 m, 1,286 primitives): two three-storey wings in an L round an overgrown courtyard;
    wards with curtains you walk through but can't see or shoot through; a lift shaft open through every floor; a
    fallen end open to the sky; an operating theatre, a boiler house and an ambulance bay.

  The Rail Yard brought a navigation cost that would have hit every level (one search for a place on the gantry
  could take 60 ms); bots now search guided by eight landmarks per level, which keeps every search under a thousand
  steps.
- **Sound** (M3.8): 87 sounds in 225 variations, all synthesised when the game starts (no recordings): the marker's
  report sagging as the tank empties, breaks and bounces per surface, footsteps per surface and pace, slides and
  landings, doors per kind, the case and the room, the referee's horn and whistles, wind, traffic, crows and trains
  outside and a room's tone inside. Each sound plays from where it happened, dulled with distance and muffled through
  walls, with the reverb of the room you're in. The referee calls the round: "Game on!", the time warnings, the
  result. The voices (39 callouts in each of six voices, and the referee's 16 lines) are imported like the art, with
  their provenance, and a missing line shows as its subtitle.
- **Settings** (M3.9) in five tabs, the same in the main menu and the pause menu: every action rebindable (two
  keyboard-and-mouse bindings and one pad binding each, by pressing the new key, button or stick; a clash is shown,
  with an offer to swap), stick speed and curve, crouch and walk as hold or toggle, the graphics preset and each of its
  parts, window mode, frame cap, render scale and field of view, the five volumes, subtitles and their size, the
  crosshair's style, colour and size, the hit marker, head-bob, camera jolt, mask spray, HUD scale, and two
  colourblind-safe team colour sets.
- **Verification** (M3.11): a scripted demo of each new role (screenshots below), and a fix it showed was needed: a
  Flanker that reached its spot used to walk straight on towards you; now it looks out from the spot first, so a flank
  that worked ends in a shot from the side.

## Acceptance checks

| Check from the plan | Result | Evidence |
|---|---|---|
| Climb the ladder: win on each level to open the next; progress survives a restart | ✅ rules, ⏳ your play | Eleven ladder tests: only the first level is open at the start, a win opens the next and nothing else, a loss opens nothing, a stricter rule, records, per-objective records, "open every level", the file's round trip and a broken file. CI's bot match saves its round to the profile. Level select and the summary below. |
| Three new levels of rising difficulty, each playable in every mode, size and difficulty | ✅ | Level select; the level tests (every spawn, patrol point, pickup, case spot, way out and room reachable; fair starts for every mode, objective and size; bot rounds); in CI, a walk through each level and three bot matches on each. |
| Marksman and Flanker behave as described | ✅ | Eight behaviour tests; the role demos below, which CI also runs on the Rail Yard and the Hospital Wing. |
| Objectives: retrieve and hold can be won and lost by their rules | ✅ | Fifteen rule tests and four bot-round tests; CI's retrieve and hold bot matches on every level; HUD screenshots below. |
| Doors open and close for you and the bots, stop paint and sight | ✅ | Fifteen door tests; each level's walk-through in CI shoots a door, opens it and walks through; screenshots below. |
| Every event has its sound; callouts are voiced and come from the caller | ✅ sounds, ⏳ voices | The [sound list](#sound-list); the walk-throughs check that shots, breaks, steps and doors were heard and the referee called the round. Callouts play from the caller as soon as the takes are in; until then they're subtitles. |
| Every action rebindable on mouse, keyboard and pad; colourblind-safe colours | ✅, ⏳ your play | Nine settings tests; CI's menu check finds the five tabs and a slot for every binding (111); screenshots below. |
| 10 people in one round at ≥ 60 fps on a GTX 1070-class GPU at Medium, on every level | ⏳ your PC | The sim's share stays within its budget on every level ([Performance](#performance)). |

### Tests

`dotnet test` runs **304 sim tests** (191 at the end of Phase 2), all green. Against the plan's test table:

- **Profile and unlocks:** as in the table above, plus the shipped ladder opening in order and a rule naming a tier a
  level lacks failing to load.
- **Doors:** leaves hang where the files say; a shut door stops paint and sight and an open one doesn't; a tap swings
  the door you face and nothing else, holding eases it open; a door stops rather than swing into someone; opening and
  shutting make a noise a bot hears; bots open shut doors on their way, doors swinging towards them and doors standing
  ajar, and their paths go round a leaf standing open across the way; doors start the same from the same seed; moving
  doors don't allocate.
- **Marksman:** vantage ranks long, high views first; a Marksman takes a top-quarter vantage near its start and watches
  from it; a hard one puts a still target out at 42 m in a few careful shots; it moves on at least 8 m once balls land
  round it.
- **Flanker:** on a call it goes to a spot 58° off the line, along a way you see 70 % of against 100 % walking straight
  at you; coming round unnoticed, it looks out from its spot and opens up from there.
- **Shared contacts:** a shout reaches a teammate 9 m away and not one 70 m off; a call still counts after the
  teammate hears you, then goes stale.
- **Objectives:** the case starts where the seed says; your side picks it up and carrying it out wins, theirs can't
  pick it up; a carrier put out drops it and a teammate picks it up; a carrier can't sprint; time running out loses;
  holding alone for 60 s wins, contested time doesn't count and leaving doesn't lose what's held; free-for-all is
  always eliminate; defenders start round the objective; a bot in your slot fetches the case and carries it out, and
  takes the room and holds it; defenders come for the case and the room.
- **New levels:** every level loads and every place on it is reachable from every way in; starts are fair for every
  mode, objective and size; per level, the features that make it (the tracks and wagons, the trailers and racking,
  the hospital's floors, lift shaft, fallen end and curtains).
- **Settings:** a Phase 2 settings file loads, bad values are put right, an unreadable file gives the defaults,
  settings and bindings survive a reload, overrides apply over the defaults, a taken binding clashes and swaps.
- **Determinism and cost:** the same seed and inputs give the same round with doors and objectives; stepping
  allocates nothing with doors on the move or an objective round on.
- **Sound data:** every area's tone is known, every voice file is on record and every record's files are there.

CI also runs the game headless (`tools/ci/smoke-test.sh`): the menu (four levels, three modes, the settings' five tabs
and 111 binding slots), the range under a 1,000-ball stress test, a walk through each of the four levels (every flight
of stairs and every ramp, a slide, a jump, a door shot, opened and walked through, a duel each way, and every kind of
sound heard), fourteen bot matches (solo and free-for-all on every level, teams with retrieve or hold on every level),
the two role demos (the Marksman has to spot you and open up, the Flanker has to go round and look out from its spot),
and the art importer's self-test (including twelve made-up voice takes cut into their lines), all with zero sim
errors. The bot that plays your slot only sweeps the opponents' spawns, so it loses most of its rounds; the matches are
there to play whole rounds without errors.

## Performance

Sim cost per tick, Release build, on the 4-core cloud VM (`dotnet run -c Release --project tools/Pb.Bench`). The sim
ticks at 120 Hz, so twice per 60 fps frame; the budget is 0.5 ms a tick.

| Level | Primitives | Balls only | **10 players and about 1,000 balls** | p95 | Per 60 fps frame |
|---|---|---|---|---|---|
| Oxbarrow Works | 546 | 0.20 ms (979 balls) | **0.44 ms** (979) | 0.63 ms | 0.88 ms |
| The Rail Yard | 779 | 0.18 ms (988) | **0.40 ms** (988) | 0.59 ms | 0.81 ms |
| The Cold Store | 859 | 0.24 ms (860) | **0.41 ms** (971) | 0.60 ms | 0.81 ms |
| The Hospital Wing | 1,286 | 0.20 ms (978) | **0.40 ms** (978) | 0.60 ms | 0.81 ms |

Nine Hard bots' brains take 0.01–0.05 ms a tick in solo and 0.06–0.14 ms in a free-for-all, where each watches all the
others. Each level's navigation grid (224,000–301,000 places) and cover points (891–1,405) are built as it loads, in
0.6–0.7 s. The ten-player figure is up from 0.28 ms at the end of Phase 2: the sim does more now (doors, shared
contacts, objectives), and the shared VM varies from run to run.

Rendering was only measured under software rendering in the cloud container, which shows what's drawn but not how
fast.

## Your check

About ten minutes, on the latest test build (`Play.bat` updates it):

1. **The climb.** With "Open every level" off (Settings → Accessibility), only the levels you've won your way to are
   open: on a first run, just Oxbarrow Works. Win a round there (Solo, Easy, 3 opponents is quickest) and the summary
   says the Rail Yard is open; quit and restart, and it's still open.
2. **The frame rate.** On the Medium preset (F12 cycles them), with v-sync off (F8), start the **Hospital Wing** as a
   **Free-for-all** with **10 players** and press **F4** for the performance overlay. The target is **60 fps or more at
   1080p**. The Hospital Wing has the most to draw; the Rail Yard the longest views.
3. **The new opponents.** Solo on the Rail Yard (Marksmen on the signal box, the footbridge and the gantry) and the
   Hospital Wing (Marksmen at the top-floor windows over the courtyard, Flankers in the stairwells). Press **F3** to see
   what they think.
4. **The sound.** Shots across the yard, a door slamming in the next room, footsteps on metal stairs, wind outside and
   the hum inside. (The voices aren't in yet.)
5. **The settings.** Rebind something on the Controls tab and try a colourblind team colour set (Accessibility).

If the frame rate is short, the levers are, in order: render scale (Video), then Medium's weeds and shadows. Please
tell me your GPU and the figures, and how the Marksmen and Flankers feel.

## Sound list

Every sound is synthesised in `game/audio/SoundBank.cs`, several variations each; `-- --sounds=DIR` on the art tool
writes them all out as WAV files.

| Group | Sounds |
|---|---|
| Your marker | the report at full, half and low tank pressure; the loader feeding; dry fire; fire-mode switch; low-air warning; refill (pod pop, pour, snap, nothing left); picking up a pod or air |
| Breaks | on stone and brick, metal, wood, glass, ground, gravel, tarp, inflatables, rubber, a person, a mask |
| Bounces | off hard surfaces, metal, wood, glass, soft things |
| Feet | steps on stone, metal, wood, gravel, ground, grass, tarp, water and rubber; slides on hard ground, soft ground, metal and gravel; jumping; landing hard, soft and on metal |
| Doors | a hinge's creak and a slam; a steel door's opening and clank; swing doors opening and flapping; a cold-room door's seal and thud; a sliding door's roll and bang |
| You | being hit; the hit marker |
| Objectives | the case taken, dropped and out; its beep and alarm; the room held, contested and lost |
| The round | the breakout horn; the referee's long whistle, triple whistle and pips |
| Menus | click, hover, toggle, back, reward |
| Ambience | wind; distant traffic; room tones (room, hall, dripping water, pigeons, draught, cold, hum); crows cawing and wings flapping; trains passing |

The voices, once recorded: 39 callouts in each of six voices (two per character model), from "Contact!" and
"Flanking!" to "They're in the room!", and the referee's 16 lines ("Game on!", "One minute left!", "Hit! You're out.",
"Room held! Round over." and so on).

## Higgsfield spend

None yet in Phase 3: 296.87 credits are left. The plan allows up to 150:

| Item | Credits |
|---|---|
| Voices: seven takes of the scripts (about 4 each) | ≈ 25 |
| Three texture sheets (twelve materials) | ≈ 13 |
| Up to four generated props (one picture of six, then Tripo H3.1 at 18 each) | ≈ 77 |
| **Phase 3, to come** | **≈ 115** |
| Phase 2's leftovers (four props and the crouched walk), approved with Phase 2 and booked for tomorrow by its own check-in | 80 |

At five generations a day, Phase 3's share is about three days of generations once Phase 2's are done: the voices
first, then the texture sheets, then the props.

## Screenshots

These were rendered in the cloud container with Mesa's software Vulkan, so the look is right but frame rates mean
nothing. All the shots from the phase are in [phase-3/](phase-3/), named by milestone; the bot overlay (F3) is on in
the role demos: sight cones coloured by how alarmed each bot is, paths in blue, the cover a bot is making for in white,
and over each bot its state and its detection meter.

**The three new levels:**

![The Rail Yard from above](phase-3/m3.5-2-over-the-yard.jpg)
*The Rail Yard: the goods shed and its platform, the yard office, stacks of sleepers and rails, the engine shed and
the rakes of wagons beyond.*

![Between the wagons](phase-3/m3.5-4-between-the-wagons.jpg)
*Between the rakes of wagons, under the footbridge, with an opponent on patrol.*

![The Cold Store from above](phase-3/m3.6-2-over-the-yard.jpg)
*The Cold Store: the store with its dock and trailers, the offices, the gatehouse and the weighbridge.*

![Through a trailer](phase-3/m3.6-4-through-a-trailer.jpg)
*Through a trailer backed onto the dock: a tunnel from the yard up into the dock hall.*

![In a chamber](phase-3/m3.6-7-in-a-chamber.jpg)
*In a cold chamber: racking you can see through standing but not crouched, and a dead forklift.*

![The Hospital Wing from above](phase-3/m3.7-2-over-the-grounds.jpg)
*The Hospital Wing: the two wings round the overgrown courtyard, the boiler house and its chimney, the ambulance bay
and the car park.*

![The courtyard](phase-3/m3.7-3-the-courtyard.jpg)
*The courtyard that every window looks down on, with its hedges (still plain boxes until their texture comes).*

![The fallen end](phase-3/m3.7-7-the-fallen-end.jpg)
*The fallen end of the north wing, its floors breaking off into the sky.*

**The new opponents** (`-- --role-demo`):

![The Marksman at its vantage](phase-3/m3.3-1-marksman-at-its-vantage.jpg)
*A Marksman at its vantage on the Rail Yard: the signal box's glazed operating floor (right), watching the yard.*

![The Marksman takes aim](phase-3/m3.3-2-marksman-takes-aim.jpg)
*You step out 40 m down its view; from the back of the signal box, it has spotted you and opens fire.*

![A teammate calls you out](phase-3/m3.3-3-flanker-the-call.jpg)
*On the Hospital Wing, an opponent spots you down the waiting room and calls it (its sight cone has gone red).*

![The Flanker on its way](phase-3/m3.3-4-flanker-on-its-way.jpg)
*The Flanker, out through the building and across the courtyard to a spot off to your side, the white line to the
cover it's making for.*

![The Flanker looks out from its spot](phase-3/m3.3-5-flanker-looks-out.jpg)
*At its spot behind the skip it looks out over it, towards the wing you're in, before it searches (beside it, a
teammate already in the fight).*

**Doors, objectives and settings:**

![Swing doors](phase-3/m3.2-2-office-swing-doors.jpg)
*Swing doors with their vision panels in the office block.*

![Retrieve: the case](phase-3/m3.4-2-retrieve-the-case.jpg)
*Retrieve: the case and its marker.*

![Hold: contested](phase-3/m3.4-5-hold-contested.jpg)
*Hold: the room contested, its clock stopped.*

![Controls](phase-3/m3.9-1-controls.jpg)
*Settings → Controls: every action, two keyboard-and-mouse bindings and a pad binding each.*

![Accessibility](phase-3/m3.9-5-accessibility.jpg)
*Settings → Accessibility: the team colours (the standard set or one of two colourblind-safe sets), HUD size and
"Open every level".*

## How to run

- **Play on Windows:** download [Pb-windows.zip](https://github.com/Tim-D-W101/Pb/releases/download/test-build/Pb-windows.zip),
  unzip it and run `Play.bat`; it keeps itself up to date. **Play**, pick a level, a mode, a size, a difficulty and
  (in solo and teams) an objective, then **Start**.
- **From Godot 4.7.2 .NET:** open `game/project.godot` and press F5.
- **Controls and debug keys** are in the [README](../../README.md#controls); every binding can be changed in
  Settings → Controls.
- **Without Godot:** `dotnet test` for the sim tests, `dotnet run -c Release --project tools/Pb.Bench` for the
  benchmark.
- **Scripted views** (how these screenshots were made): `-- --shots` tours a level's viewpoints, `-- --role-demo=marksman`
  or `=flanker` shows a new role at work, `-- --objective-demo`, `-- --round-tour` and `-- --menu-tour` show the
  objectives and the screens, and `-- --bot-match` lets a bot play your slot (see [CLAUDE.md](../../CLAUDE.md) for the
  full list).

## Known issues and limitations

- **The frame rate is unchecked on a real GPU.** Everything here was rendered in software; your check above is the one
  that counts. The Hospital Wing is the heaviest level to draw.
- **No voices yet** (Higgsfield's daily limit): callouts are subtitles and the referee's lines are subtitles with the
  horn and whistles, until the takes are imported.
- **The new levels' own materials and props aren't generated yet** (M3.10): they use the Phase 2 materials and the
  procedural look, and the hospital's hedges are plain boxes. The four Phase 2 props and the crouched walk come first,
  from the Phase 2 session.
- **Flank spots are scarce indoors.** A Flanker needs cover that is hidden from you yet has a shot at where you were;
  inside the hospital and the cold store there often isn't one, and then it comes to help like anyone else. The call
  also only says roughly where you are (give or take 2.5 m), so even a good spot can miss you, and then it searches
  from there.
- **Team colour changes apply from the next round**, and the pause menu's settings panel is wide (900 px).
- **Bots** still don't jump, slide or climb anything but stairs and ramps. The bot that plays your slot in CI only
  sweeps the spawns, so it loses most rounds.
- **Oxbarrow Works' rear-alley patrol** passes within sight of its north-east way in; a round starting there can open
  with a fight.

## Deviations from the plan

- **Sound effects are synthesised, not generated:** Higgsfield's audio tools only make speech, as the plan expected.
  The recipes are physical sketches in code, so every sound is data-driven and nothing is a recording but the voices.
- **One take per voice, not one generation per line:** each voice's 39 lines are read in one take and cut into a file
  per line at its pauses (`VoiceSplitter`), so the seven voices cost seven generations instead of about 250.
- **The hospital's wings are generated by a script** (`tools/levels/hospital_wings.py`), since their walls, doors and
  furniture repeat floor by floor; the script is the source.
- **Bot navigation got landmarks** (not in the plan): a fix the Rail Yard needed and every level gained from.
- **A Flanker looks out from its spot** before searching: a change to M3.3's behaviour, made in M3.11 after the demo
  showed what it did without it.

## What's next: Phase 4, multiplayer

From the [revised roadmap](../phase-2.md#revised-roadmap): up to ten players, co-op against bots on the compound levels
and player against player. The sim was kept ready for it: doors, objectives and the profile's choices all go through
the same commands and events, and the bots send the same commands as players.

Before that, over the next few days: the voices, then the new levels' materials and props, each pushed as it's
imported, with this report updated.

**Useful from you:**

- your frame rate and GPU, from the check above;
- how the Marksmen and Flankers feel on each difficulty, and whether the climb's pace is right (the unlock rule can
  ask for a win on Normal or harder);
- whether a fifth level is wanted before multiplayer.
