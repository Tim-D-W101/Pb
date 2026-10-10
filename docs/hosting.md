# Hosting a game of Pb

There are three ways to play together:

1. **On your network** (everyone at home): one of you hosts from the game, and the others see the game in their
   list. There's nothing to set up.
2. **Over the internet, from your PC:** one of you hosts from the game, and forwards a port on their router to their
   PC.
3. **A dedicated server:** a copy of the game that runs on its own, with nobody of its own playing.
   - It plays the rounds you list in `server.jsonc` in turn and keeps going when you're away.
   - Run it on your PC, or on a small Linux machine you rent.

Everyone has to have the same version. Play.bat brings a player's copy up to date each time it starts the game, and
the server scripts below do the same for a server. A host or server only lets in copies of its own version, and tells
anyone else what to do: "The host runs build 3ccb5a6 and you 515146a: both of you update with Play.bat."

The game uses **UDP port 47820** (UDP 47821 answers searches on your own network and never needs forwarding).

## 1. On your network

1. Host it: **Play with others → Host a game**. A password is optional.
2. Join it: the others open **Play with others**. Your game shows in their list after a moment; they click it.
   - Or they type your PC's address in **Join a game**. To find it: press the Windows key, type `cmd`, press Enter, type
     `ipconfig` and press Enter. Your address is the **IPv4 Address** line, for example `192.168.1.20`.
3. The firewall: the first time you host, Windows asks whether Pb may use the network.
   - Tick **Private networks** (and **Public networks** if your home network is set as public).
   - Click **Allow access**.
   - If you clicked Cancel: Start → type **Allow an app through Windows Firewall** → **Change settings**. Find Pb
     (or Pb.exe), tick **Private**, then OK.

## 2. Over the internet, from your PC

Your router sits between your PC and the internet, and turns away anything it wasn't asked for. Forwarding a port tells
it to pass the game's traffic to your PC.

1. **Find your PC's address on your network:** `ipconfig`, as above (for example `192.168.1.20`). Note the **Default
   Gateway** line too: that's your router (often `192.168.1.1` or `192.168.0.1`).
2. **Open your router's page:** type the Default Gateway into your browser, then log in. The password is usually on a
   sticker on the router.
3. **Forward the port.** Look for **Port forwarding**; routers also call it Virtual server, NAT, or Applications &
   Gaming. Add a rule with:

   | Setting | Value |
   |---|---|
   | Name | Pb |
   | Protocol | UDP |
   | External (public) port | 47820 |
   | Internal port | 47820 |
   | Internal (local) address | your PC's address from step 1 |

   Save it.
4. **Keep your PC's address from changing.** Most routers have this on the same page, or under DHCP: **DHCP
   reservation**, **address reservation** or **static lease**. If yours doesn't, check the address with `ipconfig` and
   update the rule when it changes.
5. **Find your internet address.** Your router's status page shows it (**WAN IP** or **Internet address**), or search
   for "what is my IP" in a browser.
6. **Host:** Play with others → Host a game. Your friends type your internet address in **Join a game**, for example
   `203.0.113.7`. If you changed the port, they add it: `203.0.113.7:47900`.

Hosting needs some upload speed: about 0.1 Mbit/s for each player who joins, and at most 0.5 Mbit/s each. Nine
joiners need at most 5 Mbit/s, which most home connections have.

If nobody can get in:

- **Check the firewall** (section 1, step 3).
- **Your provider may share one internet address between several homes** ("carrier-grade NAT"). Signs: the internet
  address on your router's page is different from the one websites show, or it starts with `100.64` to `100.127`.
  Forwarding can't work then. Ask your provider for a public IPv4 address, or rent a server (section 4).
- **Join your own game by its network address.** Many routers can't connect you to your own internet address from
  inside your home, so join with your network address and let a friend outside test the internet one.
- **Your internet address can change.** On most home connections it does from time to time, so check it before each
  game. Many routers offer a fixed name for it under **Dynamic DNS**.

## 3. A dedicated server on your PC (Windows)

The server is a copy of the game without its art. It has no window of its own and no sound: it runs in a console
window and writes what happens there.

1. **Download it.** On the [test-build release page](https://github.com/Tim-D-W101/Pb/releases/tag/test-build),
   download **Pb-server-windows.zip**.
2. **Unzip it somewhere of its own**, for example `C:\Pb-server`. Keep it out of your game's folder.
3. **Set it up.** Open `server.jsonc` with Notepad: its name, a password if you want one, and the rounds it plays (see
   [server.jsonc](#serverjsonc) below). Save it.
4. **Start it.** Double-click **Server.bat**.
   - If Windows says "Windows protected your PC", click **More info**, then **Run anyway**: the game isn't signed.
   - It first brings the server up to date with the latest test build, then runs it in that window.
   - The window shows `serving "Pb dedicated server" on UDP port 47820 (build …)`, then who joins and leaves, and
     each round.
   - Allow it through the firewall when Windows asks.
5. **For people outside your home**, forward UDP port 47820 to this PC (section 2).
6. **To play on the same PC**, start the game as usual. In **Play with others** the server is in your list, or type
   `127.0.0.1`.
7. **To stop it**, close its window.
8. **To update it**, start Server.bat again after a new build, between rounds. It updates itself first, and keeps
   your `server.jsonc`.

The log is also kept in `%APPDATA%\Godot\app_userdata\Pb (working title)\logs\server.log`.

## 4. A dedicated server on a rented Linux machine

A rented server is always on, has a fixed internet address, and needs no port forwarding at home.

**What to rent:** a small virtual server (a "VPS") from any hosting provider, as close as you can to where you all
live, since that's your ping:

- Ubuntu 24.04 (Ubuntu 22.04 or Debian 12 also work);
- 2 CPU cores and 2 GB of memory.

The server uses one core and about 200 MB of memory. During a round it sends each player about 7 KB/s, and never
more than 66 KB/s.

The commands below go into the machine's terminal. Log in from your PC with `ssh USER@ADDRESS`; Windows PowerShell has
`ssh`, and your provider tells you the user and address.

1. **Make a user for the server, and its folder:**

   ```bash
   sudo useradd --system --create-home --shell /usr/sbin/nologin pb
   sudo mkdir -p /opt/pb-server
   sudo chown pb:pb /opt/pb-server
   ```

2. **Download it and unpack it** there, straight from the release:

   ```bash
   cd /opt/pb-server
   sudo -u pb sh -c 'curl -fsSL https://github.com/Tim-D-W101/Pb/releases/download/test-build/Pb-server-linux.tar.gz | tar -xz --strip-components=1'
   ```

   To copy it up from your PC instead, download **Pb-server-linux.tar.gz** from the release page, then:

   ```bash
   scp Pb-server-linux.tar.gz USER@ADDRESS:/tmp/                                          # on your PC
   sudo -u pb tar -xzf /tmp/Pb-server-linux.tar.gz -C /opt/pb-server --strip-components=1  # on the machine
   ```

3. **Set it up:** `sudo -u pb nano /opt/pb-server/server.jsonc` (see [server.jsonc](#serverjsonc)). Ctrl+O saves,
   Ctrl+X quits.

4. **Open the port.** In your provider's control panel, the firewall (sometimes called a security group) must let in
   **UDP 47820**. If the machine runs its own firewall (`sudo ufw status` says active), run
   `sudo ufw allow 47820/udp`.

5. **Try it:** `sudo -u pb /opt/pb-server/run-server.sh`.
   - It brings itself up to date, then shows `serving "…" on UDP port 47820`.
   - From your PC: **Play with others → Join a game**, with the machine's address.
   - Ctrl+C stops it.

6. **Start it with the machine:**

   ```bash
   sudo cp /opt/pb-server/pb-server.service /etc/systemd/system/
   sudo systemctl daemon-reload
   sudo systemctl enable --now pb-server
   ```

   This starts it now and whenever the machine starts, and starts it again if it ever stops.
   - Watch its log: `journalctl -u pb-server -f` (Ctrl+C stops watching, not the server).
   - See whether it's running: `systemctl status pb-server`.

7. **Update it** with `sudo systemctl restart pb-server`, between rounds. It updates as it starts, and keeps your
   `server.jsonc`.
   - To have it update every night at 05:00, run once:
     `echo '0 5 * * * root systemctl restart pb-server' | sudo tee /etc/cron.d/pb-server`.
   - To change its settings: edit `server.jsonc`, then restart it the same way.
   - To stop it: `sudo systemctl stop pb-server`. To stop it starting with the machine:
     `sudo systemctl disable pb-server`.

The log is also kept in `/home/pb/.local/share/godot/app_userdata/Pb (working title)/logs/server.log`.

## server.jsonc

The server reads, in this order:

1. the file `--server-config=PATH` names (`run-server.sh --server-config=/path/to/other.jsonc`);
2. otherwise, the `server.jsonc` beside its program;
3. otherwise, the game's own copy.

Comments (`// …`) are allowed.

| Setting | What it does |
|---|---|
| `name` | What the game is called in everyone's list. |
| `port` | The UDP port people join on (47820). If you change it, forward that port instead, and people join with `ADDRESS:PORT`. |
| `password` | One everyone joining must type, or `""` for none. |
| `maxPeople` | The most people in at once, at most 10. Bots fill the places nobody takes. |
| `rotation` | The rounds it plays in turn, starting again after the last. Each has `level`, `place`, `mode`, `size`, `objective` and `tier`. |
| `bots` | `true`: bots fill the empty places. `false`: only people play, so teams and free-for-all need at least two (co-op always has the squad). |
| `vote` | `true`: after each round, everyone votes on where to play next instead of following the rotation. |
| `lobbyWait_s` | A round starts once everyone in the lobby is ready, or this many seconds after the first person readies up (`0`: only when everyone is). |
| `summary_s` | How long the round's results stay up before everyone goes back to the lobby. |

A round in `rotation` takes these values:

| Key | Values |
|---|---|
| `level` | `oxbarrow_works`, `rail_yard`, `cold_store`, `hospital_wing` |
| `place` | `null` for the whole area, or one of its places: `warehouse`, `offices`, `yard`, `east_field` (Oxbarrow Works); `engine_shed`, `wagons`, `signal_box`, `goods_shed` (Rail Yard); `store`, `docks`, `plant_room` (Cold Store); `wings`, `courtyard`, `boiler_house`, `car_park` (Hospital Wing) |
| `mode` | `solo` (co-op against the squad), `teams`, `ffa` (free-for-all) |
| `size` | as the menus offer it: solo 3, 4, 6 or 9 opponents; teams 2–5 a side; ffa 4, 6, 8 or 10 players |
| `objective` | `eliminate`, `retrieve` or `hold` (free-for-all is always eliminate) |
| `tier` | the difficulty: `easy`, `normal`, `hard` |

Anything that doesn't fit falls back, and the log says so when the server starts:

- an area it doesn't know becomes Oxbarrow Works;
- a place the area doesn't have becomes the whole area;
- a size that needs more than ten players becomes the mode's usual one;
- an objective the place has no room for becomes eliminate;
- a difficulty it doesn't know becomes normal.

Every setting has to be there. A missing one, or a value out of range (`maxPeople` 12, say), stops the server with a
message saying which.

## What the log says

From a round on the dedicated server, with players joining from 100 ms away:

```text
2026-10-09 18:38:02 serving "Pb dedicated server" on UDP port 47820 (build 5b1f2e0, at most 10 people)
2026-10-09 18:38:02 settings from /opt/pb-server/server.jsonc: 4 rounds in turn, bots on, vote off, password none
2026-10-09 18:38:05 Cy joined (1 in the game)
2026-10-09 18:38:05 Ada joined (2 in the game)
2026-10-09 18:38:05 turned away Dev: The server runs build 5b1f2e0 and you 3ccb5a6: update with Play.bat, and whoever runs the server starts it again.
2026-10-09 18:38:12 round 1: Oxbarrow Works · Teams · 5 v 5 · Normal with Cy, Ada and 8 bots (seed 4270030047475795249)
2026-10-09 18:38:27 everyone has the round built after 15.0 s
2026-10-09 18:38:32 round 1 live
2026-10-09 18:39:47 round 1 over: TimeUp, won by nobody after 75 s; Cy 1 out/1 hits, Ada 0 out/0 hits, Ferret 0 out/0 hits, …
2026-10-09 18:39:47   Cy: round trip 133 ms, 9.9 KB/s live, commands missing 58, late 91, too far ahead 0 (0 skipped), merged 15, violations 0, 17 shots in 75.0 s (0.2/s, cap 10.5/s)
2026-10-09 18:39:47   Ada: round trip 125 ms, 9.9 KB/s live, commands missing 5, late 20, too far ahead 0 (0 skipped), merged 0, violations 0, 6 shots in 75.0 s (0.1/s, cap 10.5/s)
```

- **Rounds:** each has a line when it starts, when everyone has built it, when it goes live, and when it's over.
- **Each player's connection:** after the result, a line for each player:
  - their round trip, and what they were sent while the round was live;
  - how their commands came: missing, late, or two run in one tick to catch up;
  - their shots against the fire-rate cap.
- **Settings:** a round in `rotation` that the server can't play as written gets a line saying what it plays instead,
  for example `round 2: there's no area "rail_yrad", so it's oxbarrow_works`.
- **Dropped commands:** anything a player sent that the server dropped gets a line too. A copy whose clock runs fast
  shows as `Clock sent commands faster than the clock (run two to a tick)` every few seconds; the server runs its
  commands two to a tick, so it moves no faster than anyone else. `sent commands too far ahead of their turn (skipped on
  to them)` means a player's commands got more than two seconds ahead: the server had stalled, or their clock runs fast.
  The server skips on to their newest, so they're playing again at once, and gain nothing.
