# Phase 5 — Feel, characters, modes, a big site and a build to share: plan

> **Status: in progress (started 2026-11-01).** Tim's brief for this phase (below) was explicit about what to build and in
> what order, so it's taken as the go-ahead, with the defaults chosen here; each item lands as its own draft pull
> request for review, with its own part of the [report](reports/phase-5.md). Technical design:
> [architecture.md §18](architecture.md#18-phase-5-feel-characters-modes-a-big-site-and-a-build-to-share). Everything in
> [spec.md](spec.md) still applies: ballistics, paint rules, gear, metric units, original IP, data files, tests.

## Your direction

The brief, verbatim (2026-11-01):

> Next phase for Pb (main now includes merged PR #10). Work in this order, one PR per item, each with tests green,
> smoke suite passing, docs updated:
> A) Paintball feel and hit feedback: satisfying shot, hopper and marker sounds, paint splats that stay on surfaces and
> players, clear hit markers, a strong hit/elimination moment (sound, screen effect, 'HIT!' callout, player raising
> hand/walking off), damage-direction indicator.
> B) Proper character animation: replace the procedural gait fixes with a real animation set (idle, walk, run, strafe,
> crouch, sprint, lean, reload, hit, eliminated walk-off) and blending, so characters look right at any frame rate.
> Blender 4.2 is available on the dev box if you generate assets via scripts. Include rendered before/after captures.
> C) New maps and modes: at least one new map distinct from Oxbarrow (e.g. outdoor woodsball or speedball arena) and two
> new modes (capture the flag, VIP/escort), working with bots.
> D) Windows build to share: Windows export preset, a CI job that builds a zipped Windows release and uploads it as an
> artifact/GitHub Release, plus a short README section on how to play.
> Open draft PRs and summarise each when done.

And Tim's addition for C, the same day:

> He finds the current areas too small. Make the new map a much bigger open area to roam (several times Oxbarrow's
> size, e.g. a large outdoor woodsball site with fields, woods, buildings and roads), with drivable vehicles (at least
> one simple car/buggy players and bots can get in, drive and get out of, with sensible paintball rules for
> vehicles). Keep performance and bot pathfinding solid at that size (streaming/LOD as needed). Plan it as its own PR
> if it's large.

How I've read it:

| You said | What it becomes |
|---|---|
| One PR per item, in this order | Five draft PRs, each stacked on the one before so each shows only its own changes: **A** feel → **B** characters → **C1** modes → **C2** the big site → **D** the build. Each passes the sim and network tests and the smoke suite, and updates the README, the architecture and this phase's report. |
| C, and "its own PR if it's large" | It is large, so C is two PRs: **C1** the two new modes, on every existing area, and **C2** the new site with its vehicles (where the modes play too). |
| Satisfying shot, hopper and marker sounds | The marker's report rebuilt in layers (sear, bolt, gas, barrel, the ball leaving), the loader feeding and its balls rattling as you move, and balls whizzing past your head. Still synthesised in code, like every sound. |
| Splats that stay on surfaces and players | Splats shaped by how the ball hit (round head-on, long and streaked at a glance, with spatter), staying until the pool's cap; on players they stay through the walk-off; and paint on your own marker when a ball breaks on it or close in front of it. |
| Clear hit markers | An X round the crosshair that says what happened: you put someone out (with their name, the part and the distance), your ball bounced off them (it doesn't count), or you hit a teammate. |
| A strong hit/elimination moment | When you're hit: the hit's thud and a ringing in your ears while the world goes muffled, the screen flushing with the paint's colour from the side it came from and draining of colour, **HIT!** big on screen with who got you, your character shouting "Hit!", then the camera easing out of your eyes to watch you put your hand and marker up and walk off the field. |
| Damage-direction indicator | Arcs round the crosshair pointing where paint came from: balls breaking or bouncing close to you, whizzing past your head, bouncing off you, and the one that put you out. It tells you where you're being shot from, never where someone is behind a wall. |
| A real animation set with blending | Clips made by a script in Blender 4.2 on the characters' own rig (idle, walk, run, sprint, strafe both ways, backing off, crouched, lean both ways, the pod refill, the flinch, hands up and the walk-off), blended by an animation tree from the sim's movement, and sampled on time, never on frames. |
| A much bigger open area, with vehicles | A woodsball site about seven times Oxbarrow's size: fields, woods, a farm, a camp, roads and rolling ground, with buggies anyone can drive. Rules: the driver can't shoot, passengers can; paint on the buggy doesn't count but anyone in it can be hit; a buggy that touches a person stops dead; speed is capped. |
| Performance and pathfinding at that size | The site is drawn in chunks with distance cut-offs and simpler far versions of the trees; the bots' paths use a coarse map of the site over the fine one, so a search across the whole site stays within the per-tick budget. |
| A Windows build to share | It's mostly there (the export, CI's Windows job, the rolling "test-build" release, `Play.bat`). D adds numbered releases from a version tag, a how-to-play page in the zip, and a short README section for whoever you send it to. |

## Milestones

### M5.1 (PR A) — Feel and hit feedback

- **Sounds** (`game/audio/SoundBank.cs`, all synthesised):
  - the report in layers: the sear's click, the bolt's knock, the gas's thump with its pitch falling, the barrel's ring,
    the ball's whoosh and the air's hiss, weaker and lower as the tank empties;
  - the loader feeding after each shot (motor and balls dropping), and rattling as you run, jump and land, more the
    emptier it is; an empty loader's motor spinning on a dry fire;
  - a ball whizzing past within reach of your head (from where it passed, its pitch falling as it goes);
  - the confirm when your ball puts someone out; when you're hit, a heavier thud, a ring in the ears and the world's
    sound muffled for a moment (a low-pass on the effects that opens up again).
- **Splats** (`SplatSystem`): stretched and turned along the ball's travel at a glancing hit (up to `splat.stretchMax`),
  with spatter thrown on ahead of it; on your marker (`ViewModelSplats`) when a ball breaks on it or within reach in
  front of it.
- **Hit markers** (`HitMarkers`): put out, bounced, teammate; each with its own look and sound, and a line under the
  crosshair for a put-out.
- **The moment you're hit** (`HitMoment`, `SpectatorView`): the screen effect (a shader over the view), **HIT!**, the
  shout, the camera easing out to third person, and you walking off with your hand up, offline as well as hosting.
- **Incoming fire** (`IncomingFire`): the arcs, from presentation-only checks of the balls in flight against your head
  (`Flybys`) and the sim's break and bounce events near you. The direction maths is engine-free and unit-tested.
- **Settings** (Gameplay): the incoming-fire arcs on or off; how strong the hit effect is.
- **Checks**: the bot match counts hit markers, arcs and whizzes (`--min-feedback`); the duel demo shows the moment.

### M5.2 (PR B) — Characters

- `tools/anim/` builds the clips in Blender 4.2 (headless) on the generator's rig: in-place loops with exact cycle
  lengths and the feet's contact times marked, plus additive poses (lean, flinch, aim) and one-shots (refill, hands up).
- `CharacterAnimator` (game/player) builds an `AnimationTree` per character: locomotion blend spaces for standing and
  crouched (by the body's velocity in its own frame), sprint above run, the lean and flinch added on top, the refill as
  a one-shot in step with the sim's refill, and the out state (hands up, walking off). Playback rate follows the
  ground speed so the feet don't slide; foot IK only corrects for the ground's height; the hands stay on the marker's
  grips by IK.
- The procedural steps (`StepGait`) and their fixes are retired; ladders keep their own limbs.
- **Checks**: the gait demo and the pop check at 120, 30 and 15 fps, with before/after captures.
- *As built* (architecture §18.2): the blend is `ClipBlender`, in code rather than an `AnimationTree` (one phase
  across every pace and direction, matched to the stride); planted feet are held in the world (turning, between
  strides) and step round when standing; the steps stay as the fallback when the set is missing.

### M5.3 (PR C1) — Capture the flag and VIP escort

- **Capture the flag** (`CaptureFlagMode`): each side's flag at its base; take theirs home to your own flag to score
  the round; a carrier can't sprint; a carrier who's hit drops it where they fell; their own side touching a dropped
  flag sends it home. Also won by the last side standing.
- **VIP escort** (`EscortMode`): one side escorts a VIP (unarmed, a little slower) to an extraction point; the other wins
  by putting the VIP out, or when time runs out.
- Every area offers both in teams (flag bases and extraction points in the level files, or worked out from the starts
  and ways out); bots attack, defend, carry, escort and hunt; the HUD, briefing map, referee and network play them.
- **Checks**: match-rule tests for both, bot rounds of both on every area in the sim tests, smoke runs.
- *As built* (architecture §18.3): the bases come from each side's starts and the VIP's ways out from the level's
  (the far ones), so no level file changed; the modes are `ObjectiveKind.Flag` and `Escort` beside retrieve and hold;
  bot rounds of both run in the smoke tests on three areas, and the sim tests on Oxbarrow Works.

### M5.4 (PR C2) — The woodsball site, with buggies

- **Ground that rolls**: a height field in the sim's collision (paint, sight, walking and the bots' grid all use it).
- **The site** (about 360 × 260 m): fields, hedges and fences, woods (trunks for cover, undergrowth), a farm and a
  camp from the kit, roads; places to play in it like every area.
- **Buggies** (`Pb.Sim/Vehicles`): in the sim, so the server decides and bots drive by the same rules; get in and out
  with interact; seats with their own hitboxes; the rules above; the network carries them.
- **Bots** drive when where they're going is far and a buggy is near, along the roads, and get out short of it.
- **Size**: drawing in chunks with distance cut-offs and level-of-detail trees; a coarse path map over the bots' grid
  so long searches stay within budget; the bench and the smoke tests measure both.
- *As built* (architecture §18.4):
  - *The site.* Thornwick Woods is 344 × 244 m inside its fence, seven times Oxbarrow Works, generated by
    `tools/levels/thornwick_woods.py`. Its woods are grown from seeds in the level file, and each trunk is a cylinder
    paint, sight and walking meet.
  - *Not built: rolling ground.* The ground stays flat; the woods, hedges and buildings are the cover. That's left for
    later.
  - *Buggies.* They're as planned, except that a joined driver isn't predicted: they ride where their copy draws the
    buggy.
  - *Bots.* They drive along a road graph, keeping to it round corners.
  - *Size.* Instead of a coarse path map, the level's own coarser grid (0.35 m) and a grid build spread over threads
    were enough. The grid builds in 3 s, and searches across the site take at most 12 ms, spread over ticks.

### M5.5 (PR D) — A build to share

- A pushed version tag (`v0.5.0`) makes a numbered GitHub Release with `Pb-windows.zip` (and the servers) besides the
  rolling test build.
- `HOW-TO-PLAY.txt` in the zip; a short "Play it" section at the top of the README.
- *As built* (architecture §18.5): the export preset, the zip, `Play.bat` and the rolling test build were already
  there, so D adds the numbered releases, a `HOW-TO-PLAY.txt` that covers every area, mode and control (as a file of its
  own), and the README section.

## Acceptance checks (every PR)

- `dotnet test` (sim and network) passes, with new tests for what the PR adds.
- `tools/ci/smoke-test.sh` passes, with new runs for what the PR adds.
- README, architecture §18 and the report updated.

## Risks

| Risk | Mitigation |
|---|---|
| Sound quality judged without ears | Each recipe's shape is measured (attack, decay, spectrum) and written to WAV (`--sounds`) for you to listen to; all levels are in `presentation.jsonc`. |
| Generated clips look stiff | They're keyframed from measured poses of the rig and blended; the clip script is the source, easy to tune and rerun. |
| The big site costs too much to draw or to path-find | Chunked drawing and a coarse path map, with the bench and smoke tests reporting both; the site's size and detail are data. |
| Vehicles and the network | Vehicles live in the sim and are replicated like players; a joined driver predicts its own vehicle. |
