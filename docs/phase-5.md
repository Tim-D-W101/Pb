# Phase 5 — Locker and extras: plan

> **Status: approved 2026-10-10 with the defaults ("the plan is OK go ahead"); in progress.** Phase 4 (multiplayer) is
> merged and finished but for your check
> ([report](reports/phase-4.md)). Technical design: [architecture.md §17](architecture.md#17-phase-5-locker-and-extras).
> Everything in [spec.md](spec.md) still applies: ballistics, paint rules, metric units, original IP, data files, tests.
> The compound areas and their modes play exactly as they do now; everything new here is an extra choice.

## Your direction

From the revised roadmap you approved with Phase 2 ([phase-2.md](phase-2.md#revised-roadmap)):

> **Locker and extras:** gear locker, fictional brands, gear models, splat shaders; the speedball field, CTF and Arcade
> as optional modes

From the spec:

> **§7 Gear locker & customisation.** 3D room where the player can rotate and inspect items. Slots: marker, loader,
> tank, mask, jersey, pants. Primary/secondary/accent colour per item. All items are original designs from fictional
> brands you invent. Loadout saved to player profile (local file first, server later).

> **§1.3–1.4** On break: team-coloured splat decal on bunkers, terrain, players, and the first-person marker. … Gear
> variants are cosmetic by default. Any stat difference must be small and data-driven.

> **§2.1 Speedball field.** Flat, symmetrical tournament field, 45 m × 36 m, netted boundary. Balls leaving the field
> despawn. Inflatable bunkers (approximate heights, all in data): Dorito ~1.2 m, Snake ~0.9 m, Can ~1.5 m, Temple
> ~1.5 m, Cake ~1.1 m, Brick ~1.8 m. Layouts defined in a data file (bunker type, position, rotation), auto-mirrored for
> symmetry. Start boxes on each back line. Flag/buzzer at the opposing start.

> **§3 Game modes.** Speedball: 5v5, round-based. Win a round by eliminating all opponents or hanging the opposing
> flag/buzzer. 3 min round timer. Match = race to N rounds (default 4). Horn countdown at every breakout. · Capture the
> Flag: centre-flag or two-base variant. Carry flag to scoring point; carrier eliminated → flag drops. · Arcade: 3 s
> respawn, timed match, score by eliminations.

> **§4 AI bots.** Breakout (sprint to assigned bunker) → Hold (peek, snap-shoot, shoot lanes across known running gaps)
> → Advance (move up on numbers advantage) → Flank → Hang flag. … Cover points auto-generated from bunker data.

The spec's own Phase 5 also names audio and full settings menus. Phase 3 built both (every sound, voiced callouts, five
settings tabs), so here they only get what the new modes need: a countdown, a buzzer, the flag's calls.

How I've read it:

| You said | What it becomes |
|---|---|
| A 3D room where you rotate and inspect items | **The locker:** an old changing room at the works. Your character stands on a turntable in the middle, in what you've picked; drag to turn them, scroll to look closer, and picking a slot brings it up close. |
| Six slots, three colours per item | Marker, loader, tank, mask, jersey and pants, each in a primary, secondary and accent colour you pick. |
| Fictional brands, original gear models | Four brands of our own, each with its own look, and a marker, loader, tank, mask, jersey and pants from each. They're built in code like the levels' props, since there are no credits for generated ones. |
| Loadout saved to your profile | Saved with your records, and worn everywhere: in your hands in first person, on your body, in the lobby, and on everyone else's screen online. |
| Splat shaders | Paint that looks wet when it lands, wraps round arms and barrels and moves with them, and spatters your own marker and gloves when a ball breaks close to you. |
| The speedball field, CTF and Arcade, as optional modes | A fifth place to play, **the Sports Ground** (an inflatable field), and three new modes beside solo, free-for-all and teams: **speedball**, **capture the flag** and **arcade**. |

## Goal

Make the game yours to dress, and give it the sport's own game. Kit yourself out from four brands in a locker room you
can look round, and see everyone else's kit online. Play tournament speedball on an inflatable field, capture the flag,
and a fast arcade mode where you come straight back in, against the bots or with others, with paint that looks like
paint.

## What you'll be able to do

- **Kit yourself out.** A new **Gear locker** button in the main menu, and in the lobby between rounds, opens the
  locker. Pick your character (the three you have now), then each slot. Browse its items by brand, see each one up
  close on your character, and pick its three colours from a palette (or any colour). Done saves it to your profile.
- **Wear it everywhere.**
  - Your marker, loader and tank are the ones in your hands in first person.
  - Your character wears all six in the lobby's preview, in the spectator view, and on every other player's screen.
  - The bots get kit dealt from the same catalogue, so no two squads look alike.
  - In rounds with sides, jerseys and pants are worn in the side's colour (your design and accents stay yours), so you
    can still tell the sides apart at a glance (question 3).
- **Four brands.** The names are in data, so you can rename any of them:
  - **Kilnmark**: workshop-built and rugged. Chunky receivers, knurled grips, stencilled marks, matt finishes.
  - **Vellis**: tournament kit. Sleek and light, two-tone fades, bright accents.
  - **Quarrow**: scenario and woodland. Rails and pouches, camouflage, olive and tan.
  - **Norrel**: what a field hands out. Plain and sturdy. The marker you have now becomes Norrel's.
- **See the paint.** Splats look wet and glossy when they land: a thick middle, a ring of drops, flecks of the ball's
  shell. They dull as they dry. On players and gear the paint is part of the surface, so it wraps round an arm or a
  barrel and moves with it. A ball breaking near you spatters your own marker and gloves in first person, not only
  your mask.
- **Play speedball on the Sports Ground.** It's an inflatable field a club has put up on the works' old playing field:
  45 × 36 m of flat turf, nets all round, a start box at each end with a buzzer to hang, the pits behind, and the
  brands' banners. There are two layouts of wedges, snakes, cans, temples, cakes and bricks, each mirrored so both
  ends are the same.
  - Five a side, people and bots.
  - A horn countdown at every breakout.
  - Win the point by putting out the other side, or by hanging their buzzer (hold Interact on it).
  - Points last 3 minutes, and the first side to 4 points wins the match.
  - The bots play it as speedball: sprint to their bunkers at the horn, shoot the lanes, hold and snap-shoot, move up
    when they're ahead, work the sides, and hang the buzzer when the way is clear.
- **Capture the flag.**
  - **Centre flag** on the field: one flag in the middle, carried to the other side's buzzer.
  - **Two bases** in the compound areas: each side's flag at its own end, and you bring theirs home.
  - A carrier who's put out drops the flag where they fell, for anyone to pick up.
  - One life each; a point ends on a capture, a side out or the clock.
- **Play arcade.** Put out, you're back in 3 seconds at a start out of the others' sight, with full paint and air.
  Matches last 5 minutes and are won on eliminations: each side's total in teams, each player's in free-for-all. It
  works on every area and on the field.
- **All of it with others.** The new modes and the field are in the lobby, the map vote and the dedicated server's
  rotation, and everyone sees everyone's kit. Your records keep each new mode too: matches won in speedball, the most
  eliminations in arcade.

## How it works

- **Gear is looks only.** Every marker fires the same 10.5 balls a second at 88 m/s, every loader holds 200, and every
  tank gives about 1,000 shots. The hitboxes stay the same too, and each item is built to fill them, so what you see is
  still what can be hit. The spec allows small differences in data; I'd leave them out, so kit never decides a game
  online (question 1).
- **Built in code.** Each item is a recipe of shapes, the way the levels' props are built: receivers, barrels, grips and
  feed necks, loaders and tanks, mask shells with lenses and vents. Each has three colour zones and its brand's marks
  printed on, painted when the game starts with the same stencil letters the yard's markings use. Any item can take a
  generated model later, as the props did, if credits come back (question 2).
- **Your clothes.** The three characters were generated with their clothes and mask in one picture. So the game sorts
  each character's surface by the bones it moves with: legs are pants, body and arms are the jersey, the head is the
  mask. It then repaints those parts in your colours, keeping the folds and seams the picture has. A jersey or pants
  "item" is a pattern (bands, panels, camouflage) drawn over that in its colours. A brand's mask is a shell fitted to
  each character's head, as the armbands are fitted to their arms.
- **Paint in the shaders.** Players and their gear keep their last splats in their own frame and draw them in their
  own shader, so paint follows the surface as they move. Everything that stands still (walls, floors, the bunkers, which
  take a lot of paint in a match) keeps the pooled decals, with a painted wet look. If decals cost too much on your PC, the world's paint moves to the flat cards the old paint already uses
  (the stress mode measures it).
- **The field is data.** A layout lists the bunkers on one half (type, position, turn), and the game mirrors them. The
  bunkers are kit pieces like the props, so one shape gives the paint collision, the walking, the bots' cover points
  and the inflatable look. Balls leaving the nets despawn, as they do on the training ground.
- **A match of rounds.** Speedball adds what the game hasn't had: a match of points, raced to 4. Offline it carries
  your score from point to point; online it's the lobby's session score with a target, and the lobby comes back when
  the match is won.
- **Bots that play speedball.** They keep the cover fighting they use in the compound (peek, snap-shoot, move, flank).
  The new parts:
  - the squad gives each bot a bunker at breakout;
  - bots sprint, which they've never needed to;
  - the layout's lanes (the gaps runners cross) are watched;
  - bots move up when ahead, and go for the buzzer when it's clear.
- **Coming back in arcade.** The sim brings a player back with fresh gear at a start the spawn planner picks out of
  their opponents' sight. A joining copy is told, so it puts you there at once rather than easing you across the map.

## Higgsfield budget

None. The account is on the free plan with no credits (checked 2026-10-10). Everything in this phase is built in code:
the gear, the bunkers, the locker room, the brand marks and the new sounds. Generated gear would cost about 300
credits for the 16 models (markers, loaders, tanks and masks, at about 18 each, and a few pictures to make them from).
Clothes and colours need no art either way.

## Scope

**In:**

- The gear catalogue: four brands, and an item of each brand for each slot, built in code; colour zones and the brands'
  marks. Masks fitted to each character, clothes recoloured by zone with patterns. The bots get kit dealt from the
  catalogue.
- The locker room, opened from the main menu and the lobby; your loadout saved in your profile.
- Kit online: your loadout goes to the host and on to everyone.
- Splats: the wet look, paint in the shaders of players and their gear, spatter on your first-person marker and
  gloves. A fix for a splat your copy drew on someone the server says you missed.
- The Sports Ground: the field, inflatable bunkers, two mirrored layouts, nets, the pits and banners.
- Speedball rules: the buzzer, the horn countdown, a match raced to N, the time-up rule. Bots that break out, shoot
  lanes, hold, advance, flank and hang the buzzer.
- Capture the flag (centre flag and two bases) and arcade (respawns, timed matches, scored by eliminations), with bots,
  on the field and in every area.
- All of it online and on the dedicated server, with tests, CI and the report.

**Out (later):**

- Generated gear models (no credits); gear that plays differently (question 1).
- Going prone; the snakes and cakes are made tall enough to crouch behind (question 6).
- More fields or layouts beyond the two; the spec's large woodland scenario map (the compound areas took its place).
- Saving your kit on a server (the spec's "server later"): it waits for a platform, like friends lists.
- Progression, unlocks, currency and the store (Phase 6, optional): every item is yours from the start.
- The Phase 4 follow-ups you might want first (question 12): voice chat, or a bot taking over from someone who drops
  out.

## Milestones

Each milestone is pushed as it lands, and each can be played. M5.2 is the first you'll want to try: the locker.

### M5.1 Gear and brands

- [x] The catalogue in data (`gear/catalog.jsonc`): the four brands, and a marker, loader, tank, mask, jersey and pants
  from each, with their names and default colours. It's checked at load: every item has a brand, every slot has an
  item of each brand, and no item changes how the game plays.
- [x] The items built in code, each fitted to the hitbox it sits in: markers with their grips where the hands go,
  loaders, tanks, and mask shells fitted to each character's face. The brands' marks are printed on.
- [x] The generated marker becomes Norrel's. It's one piece, so it fills all three slots (with the field's coded marker,
  bowl and bottle standing in when its art is missing).
- [x] Each character's surface sorted into pants, jersey, head, gloves and boots by its bones. Their shader repaints
  the pants and jersey in the loadout's colours, with each item's pattern.
- [x] Worn in first person (your marker, loader and tank, the hands on its grips) and on every character. The bots get
  a loadout dealt from the round's seed, in their side's colours. (You wear the field's own until the locker; `--kit=BRAND`
  wears one brand's range. `--gear-demo` shows each brand's kit, `--gear-demo=masks` every mask on every character.)

### M5.2 The locker

- [x] The room: a changing room at the works, built from the kit (lockers down both walls, benches, high windows,
  strip lights, a door left ajar), with your character on a turntable under a lamp. You hold your marker low and look
  at the camera as it goes round.
- [x] The camera turns round the character as you drag (or with a pad's right stick), comes closer as you scroll, and
  goes to a slot when you pick it.
- [x] Choose your character, then each slot's item by brand, then its three colours from a palette or a colour picker.
  Done saves the loadout to your profile (its format goes to version 3, and an older profile loads with the default
  kit). The whole kit's panel can put a brand's range on you, or the field's own kit; the clothes' panels show them in
  a side's colour; what can't be changed (the generated marker's own finish, and the loader and bottle that come with
  it) says why.
- [x] **Gear locker** in the main menu. In Play with others and the lobby, the character pickers become the locker, and
  the lobby's preview shows your kit. You wear it in every round and on the training ground; online, the others see it
  from M5.3. (`--locker-tour` shows each slot.)

### M5.3 Kit online

- [x] Your loadout goes to the host when you join, and with any change between rounds (protocol version 4; a copy of
  version 3 is told to update). The lobby and each round's roster carry everyone's kit, so every copy draws the same.
  The host checks each kit against the catalogue, and the lobby's preview shows yours as the others will see it.
- [x] The dedicated server deals its bots' kit like any host. Each item's model is built once and shared by everyone
  wearing it, so ten players in ten loadouts stay within the characters' drawing budget. (CI's networked rounds put
  three of the players in three brands' kit and check that every copy has them so.)

### M5.4 Paint

- [ ] A splat atlas painted when the game starts, each splat with its depth and gloss: a thick middle, drops, shell
  flecks. Fresh paint is wet and glossy, and dulls as it dries.
- [ ] Paint in the shaders of players and their gear: each keeps its last splats in its own frame and draws them, so
  they wrap round curves and move with the part. What stands still (walls, floors, bunkers) keeps the pooled decals in
  the new look.
- [ ] A ball breaking within reach of you spatters your first-person marker and gloves, as well as your mask.
- [ ] A splat your copy drew on someone the server says your ball missed comes off (today it stays).
- [ ] The stress mode measures the paint's cost; if decals cost too much on your PC, the world's paint moves to cards.

### M5.5 The Sports Ground

- [ ] The field: 45 × 36 m of turf with the lines marked, nets all round (balls leaving them despawn), start boxes with
  a buzzer station on each back line, the pits and banners behind, and Oxbarrow Works over the fence.
- [ ] The inflatable bunkers as kit pieces: wedge (the spec's "Dorito", renamed as it echoes a snack's trademark),
  snake, can, temple, cake and brick. Their shapes go to the paint collision, the walking and the bots' cover points.
  The sim's level pieces gain the wedge and the lying tube they need.
- [ ] Their look: coated nylon, seams, panels in the club's colours, and a wobble where a ball hits them.
- [ ] Two layouts in data, each one half mirrored (or turned half round) into the whole. A layout tags its bunkers
  (front, middle, back; the snake side and the wedge side) and its lanes, for the bots.
- [ ] The Sports Ground in the menu as a fifth area. It offers speedball, capture the flag and arcade; solo, free-for-all
  and teams stay with the compound areas.

### M5.6 Speedball

- [ ] The rules: 5 v 5 (people and bots), a horn countdown at every breakout (3, 2, 1, horn), a point won by putting the
  other side out or hanging their buzzer (Interact held for 2 s), and 3 minutes a point. At time up the side with more
  players in wins the point, and a tie scores nothing.
- [ ] The match: first to 4 points (data). Between points: the score, then the next breakout. The summary comes at the
  match's end. It works offline and online, where the lobby comes back when the match is won.
- [ ] The bots:
  - at the horn, each sprints to the bunker the squad gives it, shooting the lanes on the way;
  - they hold and snap-shoot, and watch the lanes;
  - they move up a bunker when ahead by the layout's margin, and work the sides;
  - they hang the buzzer when nobody covers it.
  - All three difficulty tiers.
- [ ] The HUD: the point's clock, the match score, the buzzer's hang bar. Records keep matches played and won.

### M5.7 Capture the flag

- [ ] Centre flag on the field, two bases in every compound area (each side's flag at its starts). Carry the flag to
  where it scores, and a carrier who's put out drops it where they fell. One life each; a point ends on a capture, a
  side out or the clock.
- [ ] Built on the retrieve objective's carrying and dropping, with a flag for each side. The HUD marks both flags and
  who carries them.
- [ ] The bots take, carry and escort the flag, defend their own, and go for a dropped one.

### M5.8 Arcade

- [ ] Respawns in the sim: 3 s after going out you're back at a start out of your opponents' sight, with full paint and
  air. For 2 s after, you can't be put out; firing ends that early.
- [ ] Timed matches (5 minutes) scored by eliminations: the side's total in teams, each player's in free-for-all. The
  scoreboard and kill feed run all match.
- [ ] Bots come back the same way, on every area and the field.
- [ ] Online: a joining copy puts you at your new start at once, and the summary and records keep the eliminations.

### M5.9 Verify and report

- [ ] Tests (below), and CI:
  - the smoke tests open the locker and play a bot match on the field in each new mode;
  - the networked rounds gain a speedball match, a flag round and an arcade match.
- [ ] Screenshots: the locker, each brand's kit in first person and on the characters, the paint, the field from both
  ends, a breakout, and the flag and arcade HUDs.
- [ ] `docs/reports/phase-5.md`.
- [ ] Your check: the locker, a speedball match against the bots, the paint on your PC, and the frame rate.

## Tests

| Test | Pass criterion |
|---|---|
| Gear catalogue | Every item and brand checks out at load. A profile naming an item that's gone falls back to the default. Looks only: a round in any loadout plays shot for shot the same as in the default. |
| Loadouts | A loadout survives the profile and the network's packing, and an older profile loads with the default kit. Bots' kit is the same for the same seed. |
| Clothes and masks | Every vertex of each character falls in a zone by its bones. Every mask covers each character's head, seen from eight directions. |
| Paint | A ball breaking within reach paints your first-person marker. A splat your copy drew that the server says missed comes off. The splat caps hold. |
| The field | Each layout mirrors exactly (every bunker has its twin). Every bunker's paint, walking and cover shapes agree. Balls leaving the nets despawn. The bots' grid reaches every bunker from both starts. |
| Speedball rules | Putting out the other side wins the point; so does hanging their buzzer, but not if you're hit while hanging. At time up the side with more in wins, and a tie scores nothing. The first to 4 wins the match. The horn sounds at every breakout. |
| Speedball bots | At breakout every bot reaches its bunker. In bot-against-bot matches on both layouts every match finishes, both sides score, and bots hang the buzzer when it's clear. |
| Capture the flag | In both variants the flag can be taken, carried, dropped when its carrier is out, picked up again and captured, and only the side it scores for scores. Bots capture and defend. |
| Arcade | Players come back after 3 s out of sight of their opponents, with full paint and air. The 2 s shield holds until it ends or they fire. The match ends on time, and the most eliminations wins. |
| Online | Rounds of each new mode over the network end with the server's result on every copy. Kit is drawn the same everywhere. A respawn moves you at once, without being eased. |
| Cost | Building kit and painting allocate nothing per tick. Ten different loadouts draw within the characters' budget. The sim's tick with respawns and two flags stays within 0.5 ms. |

## Acceptance checks

| Check | How it's shown |
|---|---|
| Gear locker: a 3D room, rotate and inspect items, six slots, three colours each, fictional brands | Screenshots; your look round it |
| Original gear models | Screenshots of each brand's kit, in first person and on the characters |
| Loadout saved to your profile | The profile test; your kit still on after a restart |
| Splat shaders, and paint on the first-person marker | Screenshots; your play; the stress mode on your PC |
| Speedball: a race to 4 against bots, who break out, hold cover and put players out | CI's bot match on the field; your play |
| Capture the flag (both variants) and arcade, against bots and online | CI's rounds; your play |
| Everything online | CI's networked rounds with the new modes |
| 60 fps on Medium | Your PC, with the perf overlay |

## Risks

| Risk | What happens |
|---|---|
| Gear built in code looks plain beside the generated art | Each brand gets shapes and marks of its own, and the generated marker stays (Norrel's). Any item can take a generated model later, as the props did, if credits come back. |
| Recolouring the characters' clothes over their generated pictures | The zones come from the bones, which the armbands already rely on. If a pattern reads badly on a character, that character wears it plain. |
| Paint's cost on your GPU | It's measured with the stress mode on your PC. If decals cost too much, the world's paint moves to the old paint's cards. |
| Speedball bots on an open field | They build on the cover fighting that works in the compound; bot-against-bot matches in CI catch anything that breaks; three tiers. |
| Respawning touches a lot: prediction, the spectator view, paint on players | Arcade comes last, and its network rounds are tested without the engine, like Phase 4's. |
| The phase's size | Nine milestones, each playable, pushed as they land; the locker comes first. |

## Questions (defaults in bold)

1. **Gear and play.** **Looks only: every item plays the same.** The spec allows small differences in data (a lighter
   marker that comes up a touch faster, say); I'd leave them out, so kit never decides a game online.
2. **Gear art.** **Built in code, as the props are.** Higgsfield has no credits. Generated models for the 16 pieces
   would be about 300 credits, and any item can take one later.
3. **Telling the sides apart.** **In rounds with sides, jerseys and pants take the side's colour as their main colour**
   (your pattern and accents stay yours); in solo and free-for-all you wear your own colours. Or always your own
   colours, with only the armbands and names showing sides.
4. **The brands.** **Kilnmark, Vellis, Quarrow and Norrel**, as above. Rename any, or give me names you'd like.
5. **Speedball.** **One field, the Sports Ground, with two layouts; 5 v 5, 3-minute points, the first to 4 wins; the
   horn countdown; hang the buzzer by holding Interact for 2 s; at time up the side with more in wins the point.**
6. **Snakes and cakes.** **1.2 m tall, so crouching behind them hides you.** Real snakes are about 0.9 m and played
   lying down; going prone (lying down, crawling, its own hitboxes and poses, the bots using it) would be a milestone of
   its own.
7. **Capture the flag.** **Centre flag on the field, two bases in the compound areas; one life each; a point ends on a
   capture; a dropped flag stays where it fell.** Or with arcade's respawns.
8. **Arcade.** **3 s to come back, 5-minute matches, won on eliminations; 2 s of protection after coming back, lost if
   you fire; on every area and the field, in teams and free-for-all.**
9. **Order.** **The locker and kit first (M5.1–M5.4), then the field and the modes (M5.5–M5.8).** Or the field and
   speedball first.
10. **Online.** **Everything works online too:** kit seen by everyone, and the new modes in the lobby, the vote and the
    dedicated server.
11. **Higgsfield spend.** **None** (there are no credits).
12. **Before Phase 5.** **Nothing from Phase 4's list comes first.** If you'd like voice chat, or a bot taking over
    from someone who drops out, say so and it goes in as a milestone.
