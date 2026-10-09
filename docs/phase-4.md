# Phase 4 — Multiplayer: plan

> **Status: approved 2026-10-09 with the defaults ("the plan is OK go ahead"); being built.** Phase 3 is finished but for your
> play-test ([report](reports/phase-3.md)). Technical design: [architecture.md §16](architecture.md#16-phase-4-multiplayer).
> Everything in [spec.md](spec.md) still applies: ballistics, paint rules, gear, metric units, original IP, data files,
> tests.

## Your direction

From the revised roadmap you approved with Phase 2 ([phase-2.md](phase-2.md#revised-roadmap)):

> **Multiplayer:** up to 10 players, co-op against bots on the compound levels and player vs player

From the spec (§5):

> - Server-authoritative. Dedicated server build plus listen server for testing.
> - Client-side prediction and reconciliation for movement; interpolation for remote players.
> - Projectiles: clients simulate cosmetically from a shared per-shot seed; the server simulates the authoritative ball
>   and decides hits, with lag compensation (rewind hitboxes to shooter's time, capped at 200 ms).
> - 60 Hz network tick, 120 Hz physics tick.
> - Lobby: create/join, team slots with auto-balance, loadout preview, ready-up, map vote, countdown.
> - Server validates fire rate, ammo, air pressure and movement speed.
> - Platform services behind an abstraction layer so crossplay can be added later.

And the spec's acceptance checks for multiplayer (its Phase 3, now this one):

> Dedicated + listen server, lobby, 10 players. ✅ Hits feel fair at 100 ms simulated latency · no score desync ·
> server rejects over-rate fire.

How I've read it:

| You said | What it becomes |
|---|---|
| Up to 10 players | Up to ten in a round, people and bots together, and any number of them can be people. |
| Co-op against bots on the compound levels | **Co-op**: all of you on one side against the squad holding the compound, in every area and place, with every objective and difficulty. |
| Player vs player | **Teams** with people on both sides (bots fill the empty places) and **free-for-all**. |
| Dedicated server plus listen server | Host a game from your own copy (a listen server), or run the server on its own on a spare PC or a rented Linux machine (a dedicated server). |
| Hits feel fair at 100 ms | What you hit on your screen is what counts: the server checks each of your balls against where the others were on your screen, up to 200 ms back. |
| Server validates fire rate, ammo, air, speed | Players only send what they press. The server's own marker and movement rules decide what that does, so nobody can fire faster, carry more paint or run faster. |

## Goal

Play every area with friends, on the same side or against each other, over the internet or on your own network, and
have it feel like the game you play alone: your movement and your shots answer the moment you press, everyone else
moves smoothly, and only the server decides who's out. Playing alone stays exactly as it is now.

## What you'll be able to do

- **Host or join from the main menu.** A new **Play with others** button leads to Host (a game on your copy that
  others join) or Join (games on your own network are listed; for anything else, type the host's address, and the
  last few are remembered). You give yourself a name once; it's kept in your profile.
- **Meet in the lobby.** Everyone who has joined is listed by side, with their ping. The host picks the area, where
  in it, the mode, the size, the objective and the difficulty, as in the solo menu, and bots fill whatever places
  people don't. You can switch sides (the lobby keeps them even unless the host says otherwise), pick which of the
  three characters you play (it turns in your side's colour: the loadout preview, until the gear locker comes in
  Phase 5) and say you're ready. Once everyone is, a countdown starts the round. The host can instead put the next
  round to a vote: three areas and places to choose from, and the most votes wins. A text chat runs down the side,
  and the host can remove a player.
- **Play together in three modes:**
  - **Co-op**: all of you against the squad that holds the compound (the solo round, played together). Clear it, or
    retrieve the case, or hold the room.
  - **Teams**: two sides, any mix of people and bots on each, starting at opposite ends. With an objective, the sides
    take turns to attack, round by round.
  - **Free-for-all**: everyone against everyone, bots filling up to the size.
- **Play it the way you know.** Every area, place, objective and difficulty; doors, ladders, pickups, the referee and
  the callouts all work the same online. Your teammates' names float over them, Tab shows everyone's eliminations, hits
  and ping, and the kill feed names who put out whom. Once you're out you watch the others play on. Esc opens the menu
  without pausing anyone else's round.
- **Call out.** A key you can bind has your character shout "Contact!" and marks the spot for your side, people and bot
  teammates alike: the same shared contacts the bots already use.
- **Between rounds** everyone sees the summary, then the lobby again, with the session's score so far (rounds won by
  each side, or by each player in free-for-all). Someone who joins during a round watches it and plays from the next.
- **Run a dedicated server** on a spare PC or a rented Linux machine (about $5 a month): a separate download with a
  short settings file (its name, port, password, the areas and places it plays in turn, mode, size, objective,
  difficulty, bots), and a guide that takes you through it step by step.
- **Try it alone first.** Run two copies on one PC, host in one and join "localhost" in the other, with a setting that
  adds a pretend 100 ms ping, so you can feel how it plays with lag before your friends join.
- **Stay on the same version.** Your friends download the same build from the test-build page and start it with
  `Play.bat`, which keeps everyone up to date. A copy on another version is told so when it joins.

## How it works

- **The server decides everything.** One copy of the game runs the round: the host's, or the dedicated server. It runs
  it exactly as the game does now, with the same ballistics, rules and bots. Every other copy sends only what its
  player presses, 60 times a second, with the last few presses repeated in case some go missing. It never says where
  its player is or what they hit. So nobody can make their marker fire faster or carry more paint, and nobody can run
  faster: the server's own marker and movement rules turn everyone's presses into moves and shots. They cap the fire
  rate at 10.5 balls a second, count the loader and pods, use up air and limit speed by stance. Anything odd (too many
  presses, impossible angles) is dropped and logged.
- **You don't wait for it.** Your copy moves you and fires your marker the moment you press, by the same rules the
  server uses, then checks what the server sends back. If the two ever disagree (someone bumped into you, say), it
  quietly puts you where the server says and replays your last presses from there. Everyone else moves smoothly
  because your copy shows them a few hundredths of a second behind, in between the last two updates it has.
- **Paintballs aren't sent flying.** When anyone fires, the server sends the shot once: where from, how fast and its
  number. Every copy flies its own ball with the same random draws, so everyone sees the same arc. Where a ball ends
  (on which wall or player, broken or bounced) comes from the server.
- **Fair hits.** You see the others slightly in the past, so the server checks each of your balls against where they
  were on your screen when you fired, as far back as 200 ms. That covers a ping of about 130 ms; beyond that you'd
  lead your shots a little. The other side of it is the usual one in online shooters: now and then you're out just
  after you reached cover, because on the shooter's screen you hadn't reached it yet.
- **How much it sends.** About 0.1 Mbit/s down for each player, and less up. A host with nine others needs about
  1 Mbit/s of upload, which any broadband has.
- **Over the internet**, the host's router has to pass one UDP port (47820 unless you change it) to the hosting PC.
  A dedicated server on a rented machine has no router in the way. The guide covers both. There are no accounts and
  no matchmaking service: you share the address.

## Higgsfield budget

None. The account has been on the free plan with no credits since 2026-10-08, and Phase 4 needs no new art or voices:
the lobby is drawn in code, and your callouts use the six voices the opponents already have.

Hosting costs nothing unless you want a server online all the time, on a small rented Linux machine (about $5 a month).

## Scope

**In:**

- Rules for rounds with several people: co-op, teams with people on both sides, free-for-all.
- The network core (`src/Pb.Net`): messages, snapshots, prediction, interpolation, lag compensation, a lag simulator.
- Hosting from the game (listen server) and a dedicated server for Linux and Windows.
- Finding games on your network, joining by address, a version check, a password.
- The lobby: sides with auto-balance, the host's choices, the map vote, the loadout preview, ready-up, the countdown,
  text chat.
- The HUD for several people: names over teammates, the scoreboard, the callout key, connection warnings.
- The platform layer (`IPlatformServices`) with an offline version.
- Tests, a networked round in CI on every push, the hosting guide and the report.

**Out (later):**

- Matchmaking, server lists on the internet, getting past routers without forwarding a port (a relay). A platform like
  Steam brings these, and the platform layer is where it plugs in.
- Voice chat (use a voice app alongside).
- The host handing the game over to someone else when they leave (it ends the game for everyone).
- Joining a round already under way as a player (you watch it and join the next).
- Anti-cheat beyond the server deciding everything. The shot seeds are known to every copy, so a cheat could predict
  the spread; the architecture accepts that (§6), and server-only seeds can come later.
- The training ground online; online rounds in your records.
- The gear locker, brands, the speedball field, CTF and Arcade (Phase 5); progression (Phase 6).

## Milestones

Each milestone is pushed as it lands. M4.3 is the first you can play: two copies on your PC.

### M4.1 Rules for several players

- [x] The round rules stop assuming one person ("the hero"). A round's setup lists who plays on which side, and its
  result is the winning side (or none) and how. Each player's screens read it from their side: won, lost, traded or
  time up.
- [x] Co-op is the solo round with everyone who joined on your side; teams take people on both sides; free-for-all
  takes any number of people.
- [x] Starts for several people, still dealt from the round's seed: in co-op they come in together at the area's
  entries; in teams each side starts at its own end, out of the other's sight; in free-for-all everyone starts apart.
- [x] Objectives name the side that attacks. In teams with people on both sides it changes round by round.
- [x] Every person starts with the difficulty's gear for players, and every bot with the bots'.
- [x] Lag compensation in the sim: each ball keeps how far back its shooter was seeing, and is checked against players
  as they were then (up to 200 ms back, the spec's cap). The shot's aim point is found the same way.
- [x] A step for a joining copy: its own player's movement and marker, and the balls in flight, without deciding hits
  on players or how the round stands.
- [x] Playing alone becomes a round with one person, and nothing about it changes: every existing test still passes.

### M4.2 The network core

- [x] `src/Pb.Net`, engine-free like the sim and with its own tests: the messages, packed bit by bit, with positions to
  a few millimetres and angles to a hundredth of a degree.
- [x] Snapshots 60 times a second, each sent as the difference from the last one that player confirmed. The events
  everyone must get (shots, where balls ended, eliminations, doors, pickups, the round's progress, callouts) ride with
  the snapshots and are repeated until confirmed, so a lost packet never holds the rest up.
- [x] Commands sent with the last few repeated. The server keeps a short queue of each player's commands and runs one a
  tick, two now and then if they've built up, never more than time allows; a missing one is replaced by the last.
- [x] Your own player's history for replaying after a correction, and the buffer the others are shown from.
- [x] A loopback connection and a lag simulator (delay, jitter, loss, reordering, duplicates) for tests, CI and
  `--net-lag`.
- [x] Sending and receiving allocate nothing per tick, like the sim.

### M4.3 Host and join

- [x] ENet over UDP, through the ENet built into Godot, with no new dependencies. A handshake checks both copies are the
  same version with the same data files, and the password if there is one.
- [x] First, a check that replaying your own moves through Godot's walking collision gives the server's result to the
  millimetre. If it doesn't, walking moves into the sim (see Risks).
- [x] Host: everyone else's body is moved by their commands, the bots as now; snapshots and events go out to each
  player.
- [x] Join: you're predicted and corrected. The others are shown between updates with all their movement (planted
  steps, leaning, climbing, the walk-off), balls fly from the server's shots, and sound, splats, voices and the HUD
  all come from the server's events, through the same code as now.
- [x] Everyone loads the level; the briefing waits until all have (at most 30 s).
- [x] When you're out you watch the others. Someone who drops out mid-round counts as out; the host leaving ends the
  game for everyone, with a message.
- [x] `-- --host`, `-- --join=ADDRESS` and `-- --net-lag=MS` on the command line, and the two-copies-on-one-PC set-up.

*As built:* the replay check holds, so walking stays with Godot. In rounds of two and three copies over UDP on one
machine (one joiner at 100 ms with jitter and 1% loss), every copy ended with the host's result and numbers to the
shot; a joiner was put right 0–47 times a round, nearly always by under a millimetre (the largest 2.5 cm). Bodies
of people who are out walk off the field like the bots, and their own copy shows them where the host has them.
The lobby is bare until M4.4: hosting, who's in and Start round; joined, you wait for the host.

### M4.4 Lobby, menus and the HUD

- [x] Main menu: **Play with others** → Host or Join (games on your network, an address, the last few you used), and
  your name.
- [x] The lobby: sides with their places, people and bots; switching sides, auto-balance; the host's choices (area,
  place, mode, size, objective, difficulty); the map vote; your character turning in your colour; ready-up and the
  countdown; text chat; removing a player.
- [x] Between rounds: everyone's summary, the session's score, then the lobby.
- [x] In the round: names over teammates, the scoreboard on Tab with pings, names in the kill feed, a warning when the
  connection is poor, the callout key, Esc without pausing.
- [x] Settings: your name and the pretend lag; the callout, scoreboard and chat keys are rebindable like the rest.

*As built:*
- **Lobby.** It's engine-free (`Pb.Net/Lobby`): the host decides every request, and sends the lobby to everyone when
  it changes and every 2 s during a round, for the pings.
- **Callout key.** It's a button in your command. The host's sim calls out the opponent nearest your aim, within 10°
  and 90 m and in sight, at most once every 3 s (`rules.jsonc`). Your bot teammates take it as one of their shared
  contacts. Your side sees a mark over the spot with who called and how far it is, and hears the shout in your
  character's voice. The callout key works offline too, with bot teammates.
- **Scoreboard and teammates' names.** These show offline too.
- **Hits.** Only hits on players still in count; paint on someone walking off doesn't. This changes Phase 2's
  accuracy figure too.

### M4.5 Dedicated server

- [ ] The game started with `-- --server` runs headless: no window and no sound, building only what walking and paint
  need. It logs who joined and left, each round, and anything it dropped.
- [ ] `server.jsonc` next to it: name, port, password, the most people, the areas and places it plays in turn, mode,
  size, objective, difficulty, bots, the vote, and how long it waits between rounds.
- [ ] CI builds a Linux server and a Windows one with each test build and puts them on the release page.
- [ ] `docs/hosting.md`, step by step: hosting from your PC (the router's port and the firewall), and on a rented
  Linux machine (copying it up, starting it with the machine, updating it).

### M4.6 Platform layer

- [ ] `IPlatformServices`: who you are (your name, and an id kept in your profile), finding games (on your network and
  by address), friends and invites (none yet), and a store (later). There's an offline version now; a platform's (Steam,
  for crossplay) can come later without touching the game.

### M4.7 Verify and report

- [ ] CI plays a networked round on every push: a headless dedicated server and three headless players at 100 ms ping
  with jitter and 1% loss, bots filling to ten, right through to its end. It fails on any error, any difference
  between a player's result and the server's, or more traffic than the budget.
- [ ] In the same run, a player that toggles fire every tick is held to the cap, and one that sends too many commands
  has them dropped and logged.
- [ ] The Windows build joins the Linux server under Wine, which catches faults only the Windows .NET runtime has.
- [ ] The benchmark: the server's tick with ten players and 1,000 balls stays within budget, and so does the cost of a
  correction's replay.
- [ ] `docs/reports/phase-4.md` with screenshots of the menus, the lobby and rounds from several players' screens.
- [ ] Your check: a round with friends, or two copies on your PC with `--net-lag=100`. Hits feel fair, everyone sees
  the same score, and it runs at 60 fps on Medium.

## Tests

| Test | Pass criterion |
|---|---|
| Rules for several players | Co-op, teams and free-for-all rounds with 1–10 people end with the right side winning, read correctly from every player's side; the named side attacks the objective; starts keep a side together and away from the other; rounds alone are unchanged. |
| Messages | Every message survives packing and unpacking; positions come back within 2 mm and angles within 0.01°; a snapshot difference applied to its base gives the full snapshot. |
| Loss and order | With 5% loss, duplicates and reordering, every event arrives once and in order, lost commands are recovered from the repeats, and an older snapshot never overwrites a newer one. |
| Prediction | At 100 ms ping your predicted position matches the server's on every tick of a scripted walk, run, jump, slide, climb and lean; after a forced correction it is back on the server's path at once. |
| Interpolation | The others move without stalls under 30 ms of jitter, and a lost snapshot is bridged. |
| Lag compensation | At 100 ms ping a shooter firing at someone running across at 5.5 m/s hits as often as at 0 ms (within 5 percentage points); a ball is checked against the target where the shooter saw it; the rewind never passes 200 ms. |
| Validation | A player toggling fire every tick fires no more than 10.5 balls a second, and none with an empty loader or tank; one sending twice the commands moves no faster than the rules allow, and its extra commands are dropped and logged; bad angles and unknown buttons are clamped and logged. |
| No desync | After a networked round, every player's result, scores and stats equal the server's. |
| Lobby | Joining, leaving, switching sides, auto-balance, ready-up, the vote, the countdown and removing a player follow the rules; another version is refused with a message saying which. |
| Cost | Packing and unpacking allocate nothing per tick; ten players and 1,000 balls stay within the sim's budget on the server; each player's traffic stays within budget. |

## Acceptance checks

| Check | How it's shown |
|---|---|
| Listen server: host from the game, others join | Your play (two copies on your PC, or with friends); CI joins players to a host |
| Dedicated server on Linux and Windows | CI's networked round runs on the Linux server each push; the hosting guide; your try |
| Lobby: create and join, sides with auto-balance, loadout preview, ready-up, map vote, countdown | Lobby tests; screenshots; your play |
| 10 players in a round | CI's networked round fills to ten; your play |
| Hits feel fair at 100 ms simulated latency | The lag compensation test; your play with `--net-lag=100` |
| No score desync | CI compares every player's result with the server's |
| Server rejects over-rate fire | The validation tests; the cheating player in CI |
| Co-op against bots and player vs player on every area | CI rounds in all three modes; your play |
| 60 fps on Medium with ten in an online round | Your PC, with the perf overlay |

## Risks

| Risk | What happens |
|---|---|
| Replaying your moves through Godot's walking collision doesn't match the server's | It's checked first, in M4.3. If it doesn't hold, walking moves into the sim (a sweep against the level's own shapes, which paint already uses), so prediction becomes exact; that would be a milestone of its own. |
| Your friends' routers | Port forwarding is one setting on the host's router, and the guide shows it; a rented server avoids it. A relay needs a platform such as Steam (later). |
| Faults only Windows shows | The Windows build joins the Linux server under Wine in CI, as the Windows .NET runtime faulted before (architecture §14.7, Shipping). |
| The phase's size | It's the biggest yet, so it lands in milestones, and M4.3 is playable on your own PC before the lobby exists. |

## Questions (defaults in bold)

1. **Hosting.** **Both: host from your copy, and a dedicated server you can run on a spare PC or a rented Linux
   machine.** Over the internet, the host forwards one UDP port, or uses the rented machine.
2. **Finding each other.** **Games on your own network are listed; otherwise type the address.** No accounts and no
   matchmaking service until a platform like Steam comes in.
3. **Modes.** **Co-op, teams and free-for-all, with bots filling empty places at the difficulty the host picks.** Up to
   ten in a round, any number of them people.
4. **Who picks where to play.** **The host, in the lobby; a map vote (three choices, the most votes wins) when the host
   turns it on.**
5. **Objectives with people on both sides.** **The sides take turns to attack, round by round.** Or the host picks who
   attacks.
6. **Joining late and dropping out.** **You join between rounds (during one, you watch it); someone who drops out
   counts as out.** Or a bot could take their place for the rest of the round.
7. **Friendly fire.** **On, as now:** a teammate's hit puts you out.
8. **Records.** **Online rounds don't go in your records**, which stay your bests alone; the lobby keeps the session's
   score.
9. **Talking.** **Text chat in the lobby and in rounds (to everyone or your side), and the callout key; no voice
   chat.**
10. **Names and looks.** **You type a name; you pick one of the three characters, which also gives your callouts their
    voice.**
11. **Higgsfield spend.** **None.** There are no credits, and nothing in this phase needs them.
