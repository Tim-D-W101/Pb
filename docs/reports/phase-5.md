# Phase 5 report: the locker and extras

**Date:** 2026-10-10 · **PR:** [Tim-D-W101/Pb#9](https://github.com/Tim-D-W101/Pb/pull/9), every CI job green.
**Status:** finished and tested. One thing is left: **your check**, the locker, a speedball match and the frame rate on
your PC (see [Your check](#your-check)).

The game is yours to dress now, and it has the sport's own game:
- a gear locker, an old changing room where you kit yourself out from four brands of our own;
- paint that looks like paint, wet as it lands and wrapping round players and their gear;
- a fifth place to play, the Sports Ground: an inflatable speedball field beside the works;
- three new modes beside solo, free-for-all and teams: speedball, capture the flag and arcade.

All of it plays against the bots and with others, on the in-game host and the dedicated server.

The plan is [phase-5.md](../phase-5.md), with an as-built note for each milestone. The technical design is
[architecture §17](../architecture.md#17-phase-5-locker-and-extras).

## What was built

- **Gear and brands** (M5.1):
  - **four brands**: Kilnmark (workshop-built and rugged), Vellis (tournament kit, two-tone fades), Quarrow (scenario and
    woodland) and Norrel (what a field hands out). Each has a marker, loader, tank, mask, jersey and pants, in
    `gear/catalog.jsonc`, checked at load;
  - gear is looks only: every marker fires the same, every loader holds 200, every tank gives about 1,000 shots, and
    each item is built to fill its hitbox;
  - the markers, loaders, tanks and masks are built in code, with the brands' marks printed on. Each is built once and
    shared by everyone wearing it;
  - each character's jersey and pants are repainted in the kit's colours and patterns, and the masks are shells fitted
    to each face;
  - in first person your hands sit on your marker's grips; the bots get kit dealt from the round's seed, in their
    side's colour.
- **The locker** (M5.2): **Gear locker** in the main menu, Play with others and the lobby. You stand on a turntable
  under a lamp in the works' changing room: drag to turn round, scroll to come closer, and pick your character, each
  slot's item by brand and its three colours. Done saves it to your profile, and you wear it in every round.
- **Kit online** (M5.3, protocol 4): your kit goes to the host, who checks it against the catalogue, and every copy
  draws everyone the same.
- **Paint** (M5.4):
  - eight splat shapes painted when the game starts, wet and glossy as they land and drying to satin after 40 s;
  - on players and their gear the paint is drawn in their own shaders, so it wraps round an arm, a loader or a mask
    and moves with it;
  - a ball breaking close to your eye spatters your first-person marker and gloves;
  - a splat your copy drew on someone the server says your ball missed comes off;
  - **Paint as cards**, a graphics setting the training ground's stress mode turns on if decals cost your GPU too much.
- **The Sports Ground** (M5.5): the paintball club's field beside the works, 45 × 36 m of turf inside nets, with a start
  box and buzzer at each end, the pits behind and the works over the fence. Six inflatable bunkers (wedge, brick, can,
  cake, temple and snake), in two layouts, each mirrored: **Classic** and **Crossfire**. A ball dents a bunker's fabric
  where it strikes.
- **Speedball** (M5.6): 5 v 5 on the field, people and bots. Each point starts from the start boxes after a countdown to
  the horn, and is won by putting the other side out or holding Interact at their buzzer for 2 s; 3 minutes a point,
  first to 4. The bots break out to their bunkers, watch the lanes, move up when they're ahead, and hang the buzzer when
  nobody's watching it.
- **Capture the flag** (M5.7), on every area, one life each, first to 3: on the field one flag on the centre bunker for
  either side to take to the other's buzzer; in the compounds a flag at each side's base, always on the ground. A
  carrier can't sprint, and drops the flag where they're hit. The bots take, carry, escort, defend and chase.
- **Arcade** (M5.8), on every area, in teams or free-for-all: when you're out you're back in 3 s at a start out of the
  other side's sight, with full paint and air, and shielded for 2 s, blinking, unless you fire first. The most
  eliminations in 5 minutes wins.
- **Online:** every new mode in the lobby, the vote and the dedicated server's rotation (protocols 4 to 7), and
  everyone's kit on every copy.

## How to play it

- **The locker:** main menu → **Gear locker** (or in Play with others, or the lobby between rounds).
- **The Sports Ground:** the fifth area in the menu. Pick Classic or Crossfire, then speedball, capture the flag or
  arcade.
- **Capture the flag and arcade** are also in every compound area's modes.
- **With others:** the host picks the mode in the lobby. The shipped `server.jsonc` plays a speedball match, a flag match
  and an arcade free-for-all in its rotation ([hosting.md](../hosting.md)).

## Acceptance checks

| Check from the plan | Result | Evidence |
|---|---|---|
| Gear locker: a 3D room, rotate and inspect items, six slots, three colours each, fictional brands | ✅, ⏳ your look | The menu's smoke test builds the locker, offers every slot's items and saves what was picked; screenshots |
| Original gear models | ✅ | Built in code, four brands; screenshots of each brand's kit in first person and on the characters |
| Loadout saved to your profile | ✅ | The profile tests (format 3, an older profile loading the default kit) |
| Splat shaders, and paint on the first-person marker | ✅, ⏳ your PC | Screenshots; the test of a splat the server says missed; the stress mode measures the paint's cost on your GPU |
| Speedball: a race to 4 against bots, who break out, hold cover and put players out | ✅, ⏳ your play | CI's speedball bot matches on both layouts; the speedball bot tests |
| Capture the flag (both variants) and arcade, against bots and online | ✅, ⏳ your play | CI's bot matches in both, on the field and in the compounds; the flag and arcade tests; CI's networked rounds |
| Everything online | ✅ | CI's networked rounds play a speedball match, a flag match and an arcade round, every copy ending with the server's result |
| 60 fps on Medium | ⏳ your PC | The sim's ticks are within budget ([Performance](#performance)); only your hardware can show the frame rate |

### Tests

`dotnet test` runs **501 sim tests** and **87 network tests**, all green. Against the plan's test table:

- **Gear catalogue:** every brand makes something for every slot; a catalogue mistake is named at load; a pick that isn't
  its slot's falls back to the default; a bot's kit is dealt the same from the same seed, mixing the brands. Gear is
  looks only by design: the catalogue has nothing in it that plays, so there's no loadout to test shot for shot.
- **Loadouts:** a loadout saved to the profile reads back the same; a profile from before the locker wears the default
  kit; a saved item the catalogue no longer has reads as its slot's default. Online, a kit goes in the hello and the
  roster and comes back, the lobby has everyone's kit as the catalogue allows it, and each person is cast in theirs.
- **The locker:** a sim test keeps room clear round its turntable, and the menu's smoke test builds the locker, offers
  every slot's items and saves what was picked.
- **Clothes and masks:** checked by eye, not by a test: `--gear-demo=masks` shows every mask on every character from
  eight directions (the screenshot below).
- **Paint:** a splat your copy drew on someone the server says you missed comes off (without lag compensation at 100 ms,
  20 of your balls broke on a runner on your copy and one on the server; the 19 others came off).
- **The field:** each layout made whole, every bunker with its twin; every bunker stops paint and feet and gives cover;
  a ball leaving the nets is gone; the bots' grid reaches every bunker from both start boxes; each place plays its own
  layout.
- **Speedball rules:** each side in a row across its start box; nobody moving in the countdown, and the horn putting
  the point live; holding Interact at their buzzer for the hang time winning the point, and letting go or stepping off
  starting the hang again; nobody hanging their own; at time up the side with more in winning and level nobody; the
  match going to the first side to the target.
- **Speedball bots:** at the horn every bot dealt a place and a bunker and sprinting there; the hang when nobody of
  theirs watches the buzzer, and none while they do; a side ahead moving up a bunker at a time; whole points played to
  their end (9 to 28 s at five a side).
- **Capture the flag:** on the field either side may take the flag, and it scores only at the other side's station; a
  carrier who goes out drops it where they fell, for either side; with two bases nobody takes their own and theirs
  scores at yours; at time up the side with more in takes the point. The bots: some go for theirs and the rest guard
  their own; unopposed attackers bring it home; an escort met on the way leads the carrier home; once a flag is taken
  its side goes after the carrier; whole points played to their end.
- **Arcade:** back after 3 s with full gear and the shield; the shield taking paint but not you, and firing ending it;
  the round going to the clock and the most eliminations winning, level winning nobody, free-for-all by each player's
  own; someone who left not coming back; a start in a compound none of the other side can see, clear of everyone. The
  bots: one back in starting afresh, and whole rounds to the clock on three areas.
- **Online:** a speedball point cast into the start boxes with the match so far, the lobby keeping the match point by
  point, and a joining copy having the countdown and the buzzers as the server has them; a flag point cast at the bases
  or the boxes, the lobby's flag match, and the flag through the snapshot taken, dropped and captured; an arcade round
  cast on its own clock, the lobby playing it as one round, its fields through the snapshot, and a joining copy counting
  down, then back in where the server puts it and predicted again.
- **Cost:** a live speedball point, flag point and arcade round each step without allocating.

CI also runs, on every push:

- **The smoke tests** (35 runs): the menu with the locker, the training ground, a level on each area, and bot matches in
  every mode, among them speedball on both layouts, capture the flag on the field and in Oxbarrow Works, and arcade in
  Oxbarrow Works, on the Rail Yard (free-for-all) and on the field. An arcade match fails if someone who went out didn't
  come back.
- **The networked rounds** (`tools/ci/net-round.sh`): a dedicated server and five players over UDP, two of them
  cheating, playing seven rounds: teams, free-for-all, co-op and a retrieve on the compounds, then a speedball match to
  two on Crossfire, a flag match in the Cold Store and an arcade round in the Hospital Wing. Every copy ends every round
  and point with the server's result. Three of the players are in three brands' kit, and every copy must have them so.
- **The Windows build** and its Wine join, and **the benchmark**.

## Performance

Release build on the 4-core cloud VM (`dotnet run -c Release --project tools/Pb.Bench`). The benchmark ran three times on
Phase 5's code and, for comparison, twice on Phase 4's (`main`), all on the same VM in the same session. Where the runs
differ, the tables give the range of their means.

**The new formats, ten players in and no balls in the air** (the plan's budget: within 0.5 ms a tick). These are the first
run's numbers; the other two came within 0.005 ms of them on average:

| Area | Arcade, mean | p95 | Max | Flag point, mean | p95 | Max |
|---|---|---|---|---|---|---|
| Oxbarrow Works | 0.010 ms | 0.013 ms | 0.143 ms | 0.010 ms | 0.014 ms | 0.073 ms |
| The Rail Yard | 0.009 ms | 0.009 ms | 0.127 ms | 0.008 ms | 0.008 ms | 0.083 ms |
| The Cold Store | 0.009 ms | 0.011 ms | 0.128 ms | 0.007 ms | 0.008 ms | 0.072 ms |
| The Hospital Wing | 0.011 ms | 0.018 ms | 0.211 ms | 0.007 ms | 0.009 ms | 0.073 ms |
| The Sports Ground | 0.003 ms | 0.003 ms | 0.063 ms | 0.003 ms | 0.004 ms | 0.047 ms |

- **Arcade:** someone comes back every quarter of a second, each at a start picked out of the other side's sight. That
  is about seven times as often as the busiest bot round, on the field (52 back in over 90 s).
- **Capture the flag:** both flags are carried round all the time.
- **The worst single tick** in any run was 0.31 ms, in the Hospital Wing's arcade round.

**The sim's tick with ten players and about 1,000 balls in the air:**

| Area | Phase 5, mean | Phase 4's code, mean |
|---|---|---|
| Oxbarrow Works | 0.43–0.46 ms | 0.44 ms |
| The Rail Yard | 0.42–0.47 ms | 0.42–0.51 ms |
| The Cold Store | 0.44–0.47 ms | 0.46–0.49 ms |
| The Hospital Wing | 0.46–0.47 ms | 0.45–0.51 ms |
| The Sports Ground | 0.35–0.41 ms | (new) |

- **No dearer than Phase 4:** on every area the two builds' ranges overlap, and Phase 4's code was the slower as often as
  not.
- **At the 0.5 ms budget on this VM.** Phase 5 stayed within it in all three runs, and Phase 4's code went over twice
  (the Rail Yard and the Hospital Wing). Phase 3's report measured 0.40–0.44 ms.
- **1,000 balls is the stress case.** A real round has 17–48 in the air, and the server's tick takes 0.02–0.04 ms.
- **The field is the cheapest area.** It has 277 shapes for the balls to hit; the compounds have 583–1,394.

**The field's bots:**
- **Navigation:** the field's grid has 33,848 places and 164 cover points. It's built at load in about 60 ms.
- **Brains:** nine hard bots take 0.013 ms a tick against you and 0.10 ms in a free-for-all, about what they take in
  the compounds.

**The network, as Phase 4 measured it, now with the field.** The server's tick with ten players, nine joined over the
in-memory network, and 1,000 balls in the air:

| Area | Step, mean | Packing for nine, mean | Phase 4's code: step | Packing |
|---|---|---|---|---|
| Oxbarrow Works | 0.44–0.52 ms | 0.050–0.062 ms | 0.46–0.50 ms | 0.050–0.059 ms |
| The Rail Yard | 0.43–0.44 ms | 0.049–0.051 ms | 0.42–0.44 ms | 0.044–0.050 ms |
| The Cold Store | 0.46–0.54 ms | 0.051–0.059 ms | 0.51–0.59 ms | 0.054–0.067 ms |
| The Hospital Wing | 0.42–0.47 ms | 0.049–0.056 ms | 0.46–0.49 ms | 0.054–0.055 ms |
| The Sports Ground | 0.35–0.37 ms | 0.047–0.052 ms | (new) | |

- **The step,** like the sim's tick, is at the 0.5 ms budget on this VM in both builds:
  - it went over in the Cold Store in both builds, and in Oxbarrow Works in one of Phase 5's runs;
  - Phase 4's report had 0.33–0.40 ms, from a faster run.
- **Packing** costs the same in both builds, a tenth of the budget.
- **With 1,000 balls** the impacts fill every packet to its 1,100-byte budget (67 KB/s to each player), as in Phase 4.
- **A real round,** all ten firing at the cap:
  - each player is sent 9.7–12.5 KB/s, within the 25 KB/s budget;
  - that's 0.3–0.4 KB/s more than Phase 4's code sends.
- **A correction's replay** takes 0.003 ms, as before.

**What the cloud can't measure** is the GPU:
- **The paint:** the training ground's stress mode (F3) times it on your PC. If decals cost more than 2.5 ms a frame,
  it turns on Paint as cards.
- **Ten different kits:** each item's mesh is built once and coloured per wearer.

Rendering was only tried under software rendering in the cloud, which shows what's drawn, not how fast: the 60 fps
check is yours.

## Your check

About twenty minutes, on the latest test build (Play.bat updates it):

1. **The locker.** Main menu → **Gear locker**. Turn round your character, pick a brand's marker, a mask and a jersey,
   change their colours, and press Done. Start any round: you should be wearing it, in first person and in the
   spectator view when you're out.
2. **The paint.** On the training ground, shoot a wall from close by and look at the splats: wet and glossy at first,
   duller after 40 s. In a round, paint on a player wraps round them and stays on as they move, and a ball breaking
   right by you spatters your own marker.
3. **A speedball match.** The Sports Ground → Speedball → 5 v 5 → Normal. Wait for the horn, sprint for a bunker, and play
   a match to 4. Try hanging their buzzer (hold F at it) when nobody's watching.
4. **Capture the flag and arcade.** One of each, on any area.
5. **The frame rate.** Press **F4** for the overlay: on the **Medium** preset at 1080p the target is **60 fps or more**,
   on the field and in a compound. The training ground's stress mode (F3) also says what paint costs your GPU; if it's
   too much, it turns on Paint as cards by itself.

Please tell me how the locker felt, how speedball played against the bots, and your GPU and frame rate.

## Screenshots

Each milestone's screenshots, taken in the game under the cloud's software renderer (the look, not the frame rate).

### M5.1 Gear and brands

![The four brands](phase-5/m5.1-1-the-four-brands.jpg)
![From behind](phase-5/m5.1-2-from-behind.jpg)

*Each brand's whole kit on a bot, from the front and from behind.*

![Markers, loaders and tanks](phase-5/m5.1-3-markers-loaders-tanks.jpg)
![First person](phase-5/m5.1-4-first-person.jpg)

*Each brand's marker, loader and tank close up, and in first person in your hands.*

![Every mask on every character](phase-5/m5.1-5-every-mask-on-every-character.jpg)

*Every mask on every character from eight directions (`--gear-demo=masks`).*

![In a side's colour](phase-5/m5.1-6-in-a-sides-colour.jpg)

*In a round with sides, the jerseys and pants are worn in the side's colour; the patterns and accents stay.*

### M5.2 The locker

![The locker](phase-5/m5.2-1-the-locker.jpg)
![A brand's range](phase-5/m5.2-2-a-brands-range.jpg)

*The gear locker: your character on a turntable in the works' changing room, the slots on the left and the four brands
on the right; Vellis's whole range put on in one go.*

![The marker in your colours](phase-5/m5.2-3-the-marker-in-your-colours.jpg)
![In a side's colour](phase-5/m5.2-4-in-a-sides-colour.jpg)

*A slot brought up close, its three colours from the palette; the jersey as it's worn in a side's colour.*

![Character 2 in Quarrow's range](phase-5/m5.2-5-character-2-in-quarrows-range.jpg)
![Character 3's mask](phase-5/m5.2-6-character-3s-mask.jpg)

*The second character in Quarrow's camouflage, and the third in Kilnmark's mask.*

### M5.4 Paint

![Paint on the players](phase-5/m5.4-1-paint-on-the-players.jpg)
![On a mask and a shoulder](phase-5/m5.4-2-on-a-mask-and-a-shoulder.jpg)
![On the loader and the pods](phase-5/m5.4-3-on-the-loader-and-the-pods.jpg)
![It turns with them](phase-5/m5.4-4-it-turns-with-them.jpg)

*Paint on four players in the brands' kit: on a mask and a shoulder, on a loader and the pods, wrapping round them and
staying put as they turn.*

![On a wall by old paint](phase-5/m5.4-5-on-a-wall-by-old-paint.jpg)
![Wet then dry](phase-5/m5.4-6-wet-then-dry.jpg)
![Spatter on your marker](phase-5/m5.4-7-spatter-on-your-marker.jpg)

*On a wall beside the yard's old paint; the same splats wet and dry; and spatter on your own marker from a ball that
broke close by.*

### M5.5 The Sports Ground

![The Sports Ground](phase-5/m5.5-1-the-sports-ground.jpg)
![From the south start box](phase-5/m5.5-2-from-the-south-start-box.jpg)
![The nets and the works beyond](phase-5/m5.5-3-the-nets-and-the-works-beyond.jpg)
![Down the snake side](phase-5/m5.5-4-down-the-snake-side.jpg)

*The field (Classic): from above, from the south start box, along the nets with the club's banner and the works over the
fence, and down the snake side.*

![The pits outside the nets](phase-5/m5.5-5-the-pits-outside-the-nets.jpg)
![The Crossfire layout](phase-5/m5.5-6-the-crossfire-layout.jpg)
![A brand's banner](phase-5/m5.5-7-a-brands-banner.jpg)
![A hit dents the fabric](phase-5/m5.5-8-a-hit-dents-the-fabric.jpg)

*The pits outside the nets; the Crossfire layout; a brand's banner; and a ball denting a bunker's fabric where it struck.*

### M5.6 Speedball

![Lined up for the horn](phase-5/m5.6-1-lined-up-for-the-horn.jpg)
![The countdown](phase-5/m5.6-2-the-countdown.jpg)
![Go](phase-5/m5.6-3-go.jpg)
![The breakout](phase-5/m5.6-4-the-breakout.jpg)
![At their bunkers](phase-5/m5.6-5-at-their-bunkers.jpg)

*A point: lined up in the start box, the countdown, the horn, the breakout, and each bot at its bunker.*

![Hanging their buzzer](phase-5/m5.6-6-hanging-their-buzzer.jpg)
![Buzzer](phase-5/m5.6-7-buzzer.jpg)
![Between points](phase-5/m5.6-8-between-points.jpg)
![The match won](phase-5/m5.6-9-the-match-won.jpg)

*Holding Interact at their buzzer until it's hung, the score between points, and the match won.*

### M5.7 Capture the flag

![Go for the flag on the centre bunker](phase-5/m5.7-1-go-for-the-flag-on-the-centre-bunker.jpg)
![Taken on the field](phase-5/m5.7-2-taken-on-the-field.jpg)
![Escorting the carrier](phase-5/m5.7-3-escorting-the-carrier.jpg)
![Captured at their buzzer](phase-5/m5.7-4-captured-at-their-buzzer.jpg)
![Your point](phase-5/m5.7-5-your-point.jpg)

*On the field: the chequered flag on the centre bunker at the horn, taken, its carrier escorted up the field, captured
at their buzzer, and the point.*

![Taken from their base](phase-5/m5.7-6-taken-from-their-base.jpg)
![Carried home through the works](phase-5/m5.7-7-carried-home-through-the-works.jpg)
![Captured at your base](phase-5/m5.7-8-captured-at-your-base.jpg)

*In Oxbarrow Works: their blue flag taken from its ring by the water tower, carried home on its carrier's back, and
captured at your base.*

![Down where its carrier fell](phase-5/m5.7-9-down-where-its-carrier-fell.jpg)
![The briefing](phase-5/m5.7-10-the-briefing.jpg)

*The flag down where its carrier was hit, marked for both sides; and the briefing, with both bases on the map.*

### M5.8 Arcade

![The score under the clock](phase-5/m5.8-1-the-score-under-the-clock.jpg)

*Arcade on the field, 4 v 4: the score under the clock ("YOUR SIDE 1 – 1 THEIRS"), and your first elimination.*

![Out: back in 2](phase-5/m5.8-2-out-back-in-2.jpg)
![Back in, shielded](phase-5/m5.8-3-back-in-shielded.jpg)

*Out: who got you, and "Back in 2…" while you walk off. Back in at a start box with a full loader and your pods,
shielded for 1.9 s more unless you fire.*

![Their side wins](phase-5/m5.8-4-their-side-wins.jpg)

*At time up: the score, your eliminations and the times you were out.*

![The briefing](phase-5/m5.8-5-the-briefing.jpg)

*The briefing: five minutes on the clock, and how coming back and the shield work.*

## How to run

```bash
dotnet test                                                    # 501 sim tests and 87 network tests
tools/ci/import.sh godot && tools/ci/smoke-test.sh godot       # the smoke tests, with every new mode's bot match
tools/ci/net-round.sh godot                                    # CI's networked rounds, on one machine
dotnet run -c Release --project tools/Pb.Bench                 # the benchmark, with the new formats' cost
godot --path game -- --level=sports_ground --mode=speedball    # straight into a speedball match
```

The demos for each part (`--gear-demo`, `--locker-tour`, `--paint-demo`, `--wobble-demo`, `--speedball-demo`,
`--flag-demo`) and every mode's flags are in CLAUDE.md.

## Known issues and limitations

- **Capture the flag in a compound, against bots, is mostly a team round.** With one life each, bots put the other
  side out before anyone reaches a flag 70–150 m away. Over twelve 4 v 4 bot points per area, a flag was taken in 0 to
  2 of them and captured in 0 or 1. On the field it was taken in 7 or 8 of 12. With people playing, the flags are up to
  them. If it plays like a team round, capture the flag with arcade's respawns is now cheap to add.
- **Speedball against bots is quick and deadly:** points of 9 to 28 s at five a side, most settled at the breakout.
- **The new referee lines are subtitles only:** the buzzer, whose point it is, and the flag's calls. The voices are
  generated, and there are no credits for new takes.
- **The gear is built in code,** as planned without credits: the generated marker stays Norrel's, in its own finish,
  with its loader and bottle built in. Any item can take a generated model later, if credits come back.
- **No prone.** The snakes and cakes are 1.2 m tall, so they hide you crouching.
- **Arcade on a joined copy:** your shield's line ends a moment early on your screen (your copy runs a little ahead of
  the server), and for one round trip after you fire it can come back before the server's word that firing ended it.
  The server's shield is what counts.
- **Arcade's starts:** in the rare case every start is within 1.5 m of someone, you come back where you went out.
- **The paint's cost on your GPU is unmeasured:** the cloud's software renderer can't time the GPU. The stress mode does
  it on your PC, and turns on Paint as cards if decals cost more than 2.5 ms a frame.
- **The 1,000-ball stress case is at the 0.5 ms tick budget on the cloud VM**, in Phase 4's code as much as in Phase 5's:
  in some runs of both, its mean went over. A real round's tick is under a tenth of that. The training ground's stress
  mode (F3) shows the sim's tick on your PC.
- **Online rounds still don't go in your records.**
- From Phase 4, still: **Windows players on a Linux server are corrected more often** (the engine's walking physics
  differs a little between the two), by a few centimetres each time, eased out.

## Deviations from the plan

- **Arcade's starts** are picked by the sim, among the level's starts (and on the field the start boxes' places), not by
  the spawn planner, which plans a round's starts all at once. The pick comes from the match's seed, so it's the same on
  every copy.
- **Tests the plan's table names that are checks by eye instead:**
  - "a round in any loadout plays shot for shot the same": gear has nothing in it that plays (the catalogue is checked
    for that at load), so there's nothing for a loadout to change;
  - "every vertex in a zone, every mask covering each head from eight directions": shown by `--gear-demo=masks`, and
    looked at;
  - "ten different loadouts draw within the characters' budget": each item's model is built once and shared, so ten
    loadouts cost what one does, but the GPU's time is yours to see;
  - speedball's "not if you're hit while hanging": a hit puts you out, so you stop holding Interact and the hang starts
    again; that's tested as letting go, not as a hit.
- **The bases in capture the flag** are where each side's first player starts, always on the ground, rather than "each
  side's own end": the compounds have no ends, and a base up on a container couldn't be walked to.
- **The spec's "Dorito" is the wedge,** as the name echoes a snack's trademark.

## What's next: Phase 6

The spec's last phase is optional: **progression and economy** (XP, a seasonal track and a mock store). As every phase
has, it would start with a plan for your OK. Everything is open from the start and every item is yours, so it would add
goals rather than unlock what's there.

**Useful from you:**

- how the locker, speedball and the new modes played, and your GPU and frame rate;
- whether you want Phase 6, or something first: capture the flag with respawns, voice chat, a bot taking over from
  someone who drops out, or new voice takes for the referee's new lines (if credits come back).
