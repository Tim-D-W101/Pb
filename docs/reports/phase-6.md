# Phase 6 report — Pb on Android

The plan is [phase-6.md](../phase-6.md); the design is [architecture.md §19](../architecture.md#19-phase-6-pb-on-android).
Each part below is its own pull request, stacked on the one before.

## M6.1 (A) — The Android build

**Branch:** `claude/eager-cannon-c05kkv` (on main, after Phase 5)

### What was built

- **An Android export preset** for 64-bit ARM phones (arm64-v8a) running Android 7.0 or later: landscape either way
  round, full screen, with the network permission for playing with others. The package is `org.pbgame.pb`, called
  "Pb", with a new icon (the title mark's orange splat with "Pb" stencilled on it, drawn by
  `tools/package/android/make-icon.py`; it's the desktop window's icon now too).
- **C# on Android.** Godot's Android template only takes .NET 9, so the game targets net9.0 when it's built for Android
  and stays on .NET 8 everywhere else. The APK carries the Mono runtime and the game's three assemblies.
- **The art in the APK**, its textures in the phone formats: the project now imports each texture as ETC2/ASTC as well
  as S3TC/BPTC (each `.import` lists both; every export takes the ones its platform reads).
- **`tools/package/android-build.sh`** makes `builds/android/Pb.apk`. It uses Godot's release template (the debug one
  costs frame time), signed with a debug key kept in the repository (`tools/package/android/debug.keystore`). Every
  build installs over the one before, and none is fit for a store. The version name is the build's stamp, the version
  code CI's run number. It drops 60 MB of static libraries that come with .NET's runtime pack (no phone loads them),
  aligns and signs the APK again, and checks it: the signature, arm64 alone, the permission, the version, the engine,
  the runtime, the assemblies, the data and the art in its phone formats. The APK is about 230 MB.
- **CI's "Android build" job**, after the smoke test like the Windows build: it builds the APK and uploads it with
  `HOW-TO-INSTALL.txt` as the **Pb-android** artifact.
- **Docs:** [docs/android.md](../android.md) (getting the APK, installing it on the phone or over USB with adb,
  updating, removing it, logs, building it yourself), `HOW-TO-INSTALL.txt` beside the APK, and the README's "Play it".

### How to run it

```bash
python3 tools/package/fetch-templates.py 4.7.2 android_release.apk
ANDROID_HOME=~/android-sdk tools/package/android-build.sh path/to/godot      # builds/android/Pb.apk
adb install -r builds/android/Pb.apk
```

It needs the Android SDK's build tools, a JDK and the .NET 9 SDK as well ([docs/android.md](../android.md#building-it-yourself)).

### Checks

| Check | Result |
|---|---|
| `tools/package/android-build.sh` | Builds and checks `Pb.apk` here: 230 MB, `org.pbgame.pb`, arm64-v8a only, Android 7.0 (API 24) to 16 (API 36), signed with the Pb debug key, the network permission, Godot's engine, the Mono runtime, `Pb.dll`, `Pb.Sim.dll` and `Pb.Net.dll`, the data, the build stamp, and 126 textures in their phone formats. Vulkan is wanted but not required, so a phone without it falls back to OpenGL ES 3. |
| `dotnet test` | 567 sim tests and 73 network tests pass (built with the .NET 9 SDK, which `global.json` now rolls forward to here; CI's other jobs stay on .NET 8). |
| Smoke runs | The menu, the range, Oxbarrow Works' walk-through and a bot match, Thornwick Woods' walk-through and the vehicle demo pass with the new project settings (the icon, the orientation, the phone texture formats). CI runs the whole suite, the Windows build and the new Android build. |

### Known issues

- **Not run on a phone.** There's no phone here and no emulator (this container has no hardware virtualisation), so
  the APK is built and checked, not started. Whether it starts, and how it runs, needs a phone.
- **No touch controls yet:** the menus work by tapping (touches act as clicks), but a round needs a game pad until
  M6.2. It uses Godot's Mobile renderer, Godot's default on Android, with the desktop's Medium preset until M6.3.
- The APK is big (about 230 MB), nearly all of it the art and the engine.

### What's next

M6.2: touch controls (a stick to move, drag to look, the buttons, driving), and menus that fit a phone.

## M6.2 (B) — Touch controls and menus by touch

**Branch:** `claude/eager-cannon-c05kkv-touch` (on M6.1)

### What was built

- **Touch controls**, drawn over the HUD, sized in millimetres from the screen's density and kept inside its safe area
  (clear of a notch or a camera hole). A **stick** appears where the left thumb lands (pushed out past its ring, you
  sprint); **dragging** anywhere on the right that isn't a button looks round, and so does dragging on FIRE as you hold
  it. On foot: **FIRE**, **AIM** (a tap on, a tap off: the view narrows to 80 % and the look slows; it changes nothing
  the sim decides), **JUMP**, **CROUCH** (a tap down, a tap up), **RELOAD**, and **USE**, which shows only when there's
  something in reach and says what it'll do: GET IN, GET OUT, OPEN, SHUT, CLIMB, CLIMB DOWN. **At the wheel** the stick
  steers and the buttons become **GAS**, **BRAKE** (then reverse), **HANDBRAKE**, **FIRE** from the wheel and **GET OUT**
  once you're slow enough. Along the top: **pause**, **SCORES** (held) and, playing with others, **CHAT**. The layout's
  all in presentation.jsonc (`touch`).
- **Up automatically:** on a phone or tablet from the start; on a computer at the first touch of its screen, and away
  again at a mouse click, a key or a game pad (a phone with a pad plays with the pad until the screen's touched again).
  The training ground has them too.
- **Settings, Controls:** touch look speed, button size, how see-through they are, and **left-handed** (the sides
  swapped). On a phone, what only a computer has (the mouse's sensitivity, key bindings, the window mode) is left out,
  and the window mode isn't applied (it took the game out of full screen on Android; fixed in M6.1's pull request).
- **Menus by touch:** on a phone the whole interface is drawn bigger by the screen's density (an interface pixel 0.13
  mm, at least 480 of them in the screen's height): about 1170 × 530 on a 6.5-inch phone, 850 × 480 on a small or 16:9
  one. Every menu fits that: narrow margins, nothing wider than the screen, the level card's columns stacked, Play with
  others' hosting and joining stacked, choices that wrap, the title's buttons beside its mark, the briefing's Start and
  Back first, cards over the round in pages that scroll, and pages that scroll by dragging (panels let the drag through).
  Text boxes bring up the phone's keyboard (Godot's own). The phone's **back** button goes back a screen, leaves the
  game from the title, and in a round leaves the chat, steps back in the pause menu or opens it. The HUD's prompts
  name the touch buttons, and the gear panel moves up out of the buttons' way.
- **Two demos that are also tests:** `--touch-demo` plays a round on Thornwick Woods by simulated fingers, the touches a
  phone sends, and `--menu-touch-demo` works the menus by taps and drags (both below).

### How to run it

```bash
godot --path game res://scenes/Level.tscn -- --touch --dpi=200          # a round with the touch controls, on a computer
godot --headless --path game res://scenes/Level.tscn -- --touch-demo --level=thornwick_woods --no-art --dpi=400
godot --headless --path game res://scenes/Main.tscn -- --menu-touch-demo --dpi=480 --screen=1920x1080
```

On a computer with a touch screen the controls come up at the first touch. `--safe-area=L,T,R,B` (screen pixels) makes
a notch.

### Checks

| Check | Result |
|---|---|
| `--touch-demo` | Passes all 30 steps on Thornwick Woods, by simulated fingers alone: Start tapped on the briefing; the buttons inside the safe area and clear of each other, on foot and at the wheel; a 2 cm drag turns 60.0° (30 °/cm) and back; the stick pushed near its ring moves you at 5.2 m/s and, pushed past it, sprints (7.0 m/s); JUMP leaves the ground; CROUCH down and up; AIM narrows the view and lets it go; FIRE held fires while a drag on it turns you; RELOAD starts a refill; USE says GET IN at the buggy's door and gets you in; GAS with the stick to one side drives and turns (up to 24 km/h, 60° round in 2.5 s); HANDBRAKE goes on; FIRE from the wheel; BRAKE stops it; GET OUT; SCORES held shows the scoreboard; pause by its button, the back button closes and opens it, Resume tapped; left-handed swaps the sides. On a 20:9 phone (2400 × 1080, 400 dpi) with a notch either side, a 16:9 one (1920 × 1080, 480 dpi) with a notch on one side, and a small one (1600 × 720, 300 dpi). |
| `--menu-touch-demo` | Passes its 13 steps at the same three sizes (about 1170 × 530, 850 × 480 and 1070 × 480 interface pixels): every screen within the screen's width, the title's buttons all in sight, Play, a place picked, the level card dragged until Start is in reach (dragging scrolls the page), the back button, Settings with the touch settings and none of the mouse's, the Video tab tapped (no window mode), Play with others, the name box taking the tap, and the back button on the title leaving the game. |
| Rendered (lavapipe) | The touch demo filmed on a 16:9 phone-sized screen passes the same 30 steps; the frames show the controls where they should be: pause and SCORES at the top left with the gear panel under them, the stick's ring at the bottom left, FIRE, AIM, RELOAD, JUMP and CROUCH by the right thumb, GAS, BRAKE, HANDBRAKE, FIRE and GET OUT at the wheel, and GET IN by the buggy. |
| `dotnet test` | 568 sim tests (one new: the touch settings kept in their ranges) and 73 network tests pass. |
| Smoke runs | The menu, the range, Oxbarrow Works' walk-through and a bot match, the splat and duel demos pass as before (a computer without a touch screen plays as it did); CI runs the whole suite with the five new touch runs. |

### Known issues

- **Not tried by a thumb.** Every touch here is simulated (the same events a phone sends, through Godot's input), so
  how the controls feel (the look speed, the stick's size and where the buttons sit for a real hand) needs a phone and
  a person; the sizes are a starting point, and Settings changes them.
- No lean, shoulder swap, walk, fire mode or callout buttons: the brief's buttons only, to keep the right thumb's
  cluster small. A pad has them all.
- The phone's own keyboard and its back button are Godot's: shown and sent by Android, not seen here.

### What's next

M6.3: a mobile graphics preset (the Mobile renderer, lower shadows, effects and view distance) and its benchmark.
