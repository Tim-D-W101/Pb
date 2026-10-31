# Play-test fixes (after Phase 4)

**Date:** 2026-10-31 · **Branch:** `claude/ecstatic-wright-kquxtm`

Tim's play-test of Phase 4 raised seven things:
- voiced callouts were overdone;
- the referee's whistle at the start of a round was unwelcome;
- the characters looked glitchy;
- spawns and roles didn't really vary, and the bots stood still;
- the game hitched;
- objective rounds were badly paced;
- one network test failed on Linux.

Each was looked into before it was fixed. The fixes are measured against the game as it was, on the same machine with
the same seeds wherever the comparison allows. The design notes are [architecture §17](../architecture.md#17-after-the-phase-4-play-test-2026-10-31-as-built).

## 1. Fewer callouts, and the ones that matter reach the right teammates

**What was wrong:**
- **Too many lines.** Every bot shouted its callouts as often as its own cooldown allowed. Five bots spotting you meant
  five "Contact!"s. In 90 s of a 5 v 5 the old settings gave **30** lines.
- **"Callouts heard 0" was a counting fault.** The headless log counted only presses of *your* callout key. In a bot
  match nobody presses it, so the count was always 0. The bots' contacts did reach their teammates; the log never
  counted them.

**What changed:** shouting is decided in the sim (`bots/brain.jsonc`):
- a 10 s cooldown per bot, and no repeating the same call within 30 s;
- 55 % of everyday calls said aloud ("Hit!" and the case calls always are);
- one shout per side every 5 s, and no teammate echoing a call within 20 s;
- 1.5 s between any two lines in the round, so lines never overlap.

Passing a contact to teammates is separate from saying it, and is unaffected. The log now counts lines shouted,
contacts shared and contacts heard, and a bot teammate's "Contact!" marks the enemy on your HUD.

**Now:** 30 → **6** lines in the same 90 s. In CI's 3 v 3 round, 3 lines were shouted, 10 contacts shared and 6 heard
by teammates.

**Tests:** `CalloutTests`:
- every gap is kept over 2 minutes of a 5 v 5;
- the old settings give more than twice the shouts;
- every teammate in earshot hears a call, and the other side never does;
- the counts add up.

## 2. No whistle at the start of a round

**What was wrong:** a breakout horn and "Game on!" started every round, with more lines on top:
- the thirty-second call, with whistle pips;
- room calls every few seconds in a doorway fight.

**What changed (`presentation.jsonc` → `hud.refereePacing`):**
- The round goes live on a soft two-note knock (`startCue: "soft"`; `"horn"` brings the old horn back, `"none"` has
  nothing), and "Game on!" follows it.
- The thirty-second call is off.
- Room calls wait 2 s for the room to settle and don't repeat within 30 s.
- Everyday lines are at least 8 s apart.
- The whistle stays for the end of the round.

**Verified:** the cue is synthesised like every other sound (`--sounds` writes it out as a WAV file to listen to),
and the level smoke test checks the referee still calls the round (`RefereeCalls.Called`).

## 3. Steadier characters

**How it was measured:** the gait demo now prints three things for each move and for going into it:
- planted-foot slide;
- the head's and hands' jerk against the body;
- how far the wrists and ankles end up from their marks.

A bot match with the art prints the worst pop of any character in the round.

**What was wrong, and what changed:**

| Problem | Fix | Before | After |
|---|---|---|---|
| Running, a foot stayed down so long that the body outran the leg and dragged it | Quicker strides, with more of each stride in the air (`steps.gaits`) | 0.72 m/s slide | 0.16 m/s |
| The hips snapped down in one frame whenever a leg needed reach, and the bob turned a corner at each landing, so the head and hands popped | The drop eases over 20 ms; the bob and sway are smooth waves | Jerk 2.2 / 2.8 cm (run / sprint), 3.2 strafing, 2.9 backing off | 0.4 / 0.5, 1.4, 1.5 |
| Crouching and standing went at full speed from the first frame | The drawn body follows the sim's eye height on a 30 ms spring | Hands 6.3 cm a frame | 2.7 cm |
| A shoulder swap traded the grips in one frame | The hands ease across over 0.15 s | 21.5 cm | 8.3 cm |
| Setting off, or reversing a strafe, left the planted foot out of reach, and it skated after the body for a step | That foot steps at once, or the cycle hurries so the other lands sooner (`steps.stepWhenDragged_mps`, `draggedHurry`) | At 120 fps: 0.53 m skated starting a walk, 0.60 m starting a strafe, 0.50 m reversing one; strafe-left slide 0.43 m/s | 0.18, 0.19 and 0.29 m; 0.13 m/s |
| A character's first frames drew its hands at the world's origin | Fixed | 82 cm pops | none |

**Now:** in an art-on 3 v 3 the worst pop of any character is about 2-5 cm, from aiming and being hit.

**In CI:** the smoke test now runs two new checks.
- `--gait-demo --gait-check` fails past these limits:
  - 0.3 m/s of slide;
  - 2.5 cm of jerk in a move, or 4 cm going into one;
  - wrists 1 cm off their grips, ankles 3 cm off their marks.

  It passes at both 60 and 120 fps.
- An art-on bot match fails if any character pops more than 15 cm (`--max-pop=15`).

## 4. Starts and roles that change every round, and bots that move about

**Why it didn't play that way:** each round did get a new seed, but:
- **Narrow spawn areas.** The Hospital Wing's spawn area was its car park and the Cold Store's its lorry park, so nobody
  ever started inside either building (about 35 possible starts on the Hospital Wing, and the same few came up).
- **Fixed roles.** Half of a solo squad started at the level's ~20 named spawns, each always playing its own role.
- **Repeats.** About one start a round was the same as in the round before.
- **Bots stood still.** Once a round began, sentries and rushers stood at their spawn all round. Patrols walked the same
  loop the same way, and a Marksman always took the one best vantage near it.

**What changed:**
- **Wider spawn areas.** Both levels now deal starts anywhere inside the fence, on every floor. In 20 rounds, 85 of
  120 opponents started inside the Cold Store and 114 of 120 inside the hospital.
- **A memory of the last round.** The game remembers each area's and place's last round (`SpawnHistory`, for rounds
  alone, hosted and on the dedicated server):
  - starts within `repeatRadius_m` of last round's are used only when nothing else fits;
  - you come in at another entry.
- **Roles not tied to places.** Named spawns play their own role half the time (`spawnOwnRoleChance`).
- **Out of sight.** Opponents start out of sight of your entry and of a spot 6 m in from it (`entryLookAhead_m`).
- **Bots that move.**
  - Sentries, rushers and Marksmen move on to another post or vantage near their start every so often
    (`archetypes.jsonc` → `shiftAfter_s`, `shiftWithin_m`).
  - Patrols set off either way round.
  - A solo squad comes looking for you after 90 s of quiet.

**Now:**
- **No repeats.** Over 10 rounds, no start came back from the round before, in any area or mode.
- **Variety.** 10 rounds used 50-88 different starts per area and mode.
- **Movement.** Over four of its shift periods a sentry moved between 4 posts, up to 12.7 m from its start, and a
  Marksman moved 22 m to a new vantage.

**Tests:** `SpawnVarietyTests`, in every area and mode, check:
- no start within the repeat radius of last round's, and another entry for you;
- opponents can start inside the buildings;
- named spawns play other roles;
- sentries and Marksmen move;
- patrols go either way;
- nobody has a shot at you as you step in.

## 5. Hitches

**What was wrong:** two separate things.
- **Bot-brain peaks of 4-7 ms.** These were single path searches on the 260,000-place navigation grid. Each was three to
  four times dearer than it needed to be, because every place a search looked at tested every door leaf in the level.
  A bot that couldn't find a path asked again every tick.
- **The 49 ms sim tick (10 players, ~979 balls).** It wasn't the sim. The bench now prints each slowest tick's own
  thread CPU time beside its wall-clock time, and the slow ticks used a fraction of their wall time on the CPU. The
  difference was the machine scheduling other work. Other ticks with the same load took 2-4 ms.

**What changed:**
- **Doors.** Only places a door leaf can swing over ask about doors (same paths).
- **A budget per tick.** A search runs within a budget of places a tick (`navigation.jsonc` → `searchNodesPerTick`)
  and carries on next tick. Each squad has its own search memory.
- **Back-off.** A bot that finds no path waits before asking again.
- **Cheaper choices.** Cover and flank choices skip the sight lines of points that can't win (same choices).
- **Warm-up.** The sim's, network's and game's methods are compiled while the level loads, so the first fights don't
  pay for compilation. The GC keeps to background collections while a level is up.
- **A fairer bench.** `Pb.Bench` runs with the game's JIT settings, settles the GC before each measurement, and no
  longer allocates in measured ticks.

**Now:** free-for-all bot-brain peaks went from **1.3-5.1 ms** to **0.5-0.9 ms** on the same machine. The bench's
CPU column shows the sim's own worst ticks (see [Performance](#performance)).

**Tests:** `PathBudgetTests` check three things:
- a long search carried on over ticks keeps to its budget and finds the same path;
- only places a door can swing over ask about doors, and no path changes;
- in a free-for-all the bots never search more than the budget in a tick.

`SimWorld.Step` still allocates nothing.

## 6. Objective rounds that play out

**How it was measured:** 96 bot-only objective rounds (every area, solo and 3 v 3, the same seeds before and after)
showed rounds over in seconds and rounds run out with nobody at the objective.

**What was wrong:** the stalls were bugs.
- **Looking over the door.** A bot at a door looked 1.3 m over it (chest height was added twice), so up close the leaf
  was outside its reach. It tapped at nothing, gave up, and tried again for ever.
- **Waiting for ever.** A bot that couldn't reach its spot beside a door waited there for the rest of the round.
- **Crowded doors.** Bots crowding a door held it shut by standing in its swing.
- **Stuck on a leaf.** A bot pressed against a leaf found no way out.
- **Stale contests.** A hold room contested by two sides out of each other's sight stayed contested.
- **Unreachable spots.** Attackers could pick a spot in the room that they couldn't reach.

Two things were just too quick:
- a case beside a gap in the fence went out in seconds;
- defenders who went hunting left the objective unguarded.

**What changed:**
- **Doors.** Bots aim at the leaf, give way to its swing and step off a leaf they're pressed against.
- **Stalemates.** After `stalemateAfter_s` the attackers in a contested room are told where the defenders in it are.
- **Spots.** Spots in the room are reachable ones.
- **Ways out.** A case goes out only through a way out at least 45 m from where it lay (`minExitDistance_m`). The HUD,
  the beams, the briefing map and its text show only the ways that count.
- **Defenders.** They roam within `defendWithin_m` of the objective instead of leaving it.
- **Guidance.** The HUD shows the case once you're inside its building, and says which floor an objective is on.

**Now:** across the 96 rounds, time-outs went from 5 to 1, and the objective decided twice as many team retrieve
rounds.

**Tests:**
- `ObjectivePacingTests`:
  - the team rounds that stalled now end before the clock;
  - in a room contested too long, your bots go for theirs.
- `ObjectiveTests`: a way out right by where the case lay doesn't count.

## 7. `Network_ticks_allocate_nothing`

**What was wrong:** the in-memory network and the lagged connection held packets in `ArrayPool<byte>.Shared`. That
pool frees what it holds when a full collection runs under memory pressure. The test's own collections set that off on
a loaded Linux machine, and the next second of ticks rented fresh arrays (536 bytes is one `byte[512]`).

**What changed:** `PacketBuffers` keeps each transport's arrays for reuse and never gives them back.

**Now:** a new test empties the shared pool before measuring. It measured 4,392 bytes without the fix and 0 with it.
Both allocation tests pass every run.

## Acceptance checks

| Check | Result |
|---|---|
| `dotnet test tests/Pb.Sim.Tests -c Release` | 458 passed (418 before these changes) |
| `dotnet test tests/Pb.Net.Tests -c Release` | 66 passed (two new tests) |
| `tools/ci/smoke-test.sh` | every run passes, including the new gait check and the art-on match |
| `--gait-demo --gait-check` at 60 and 120 fps | passes at both |
| `tools/Pb.Bench` | see Performance |

## Performance

Measured with `tools/Pb.Bench` on the cloud container, before and after the fixes on the same machine:
- **Free-for-all bot-brain peaks:** 1.3-5.1 ms before, 0.5-0.9 ms after.
- **The sim's slowest ticks:** the bench now prints each one's thread CPU time beside its wall time. The slow ticks
  that looked like 49 ms of sim spent most of that time off the CPU, waiting on the machine.

These are a cloud machine's numbers. Tim's PC is the one that counts.

## How to run

```bash
dotnet test                                                    # sim and network tests
dotnet run -c Release --project tools/Pb.Bench                 # the bench, with the CPU column
tools/ci/smoke-test.sh godot                                   # the smoke tests, gait check and art-on match included
godot --headless --path game res://scenes/Level.tscn -- --gait-demo --gait-check          # the characters' numbers
godot --headless --path game res://scenes/Level.tscn -- --bot-match --mode=teams --size=3 # callout, contact and pop counts
```

## Known issues and limitations

- **A little drag remains when setting off.** The first step of a walk or strafe from a standstill still drags the
  planted foot 0.2-0.3 m. That's under a third of what it was, and over a step that takes a fifth of a second.
- **Frame rates.** Numbers under the cloud's software renderer say nothing about frame rate. Only Tim's hardware can
  confirm the 60 fps target, and the hitches are best judged there too.
- **Contacts in free-for-all.** Bots still "share" contacts with nobody in free-for-all, so the log reports contacts
  shared and heard 0 times. That's expected, and costs nothing.
- **The old shouting is a data change away.** The callout limits are data. Setting them back to 0 brings the old
  behaviour back, which is how the test compares the two.

## What's next

Tim's play-test of these changes, above all:
- whether the callouts and referee now feel right;
- whether the characters look steady on his hardware;
- whether rounds still feel the same from round to round.
