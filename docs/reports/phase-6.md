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
