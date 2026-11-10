# Phase 6 — Pb on Android: plan

> **Status: built (2026-11-10), in three stacked pull requests, and a fourth after Tim's first install on a phone (the
> grey screen, below); what only a phone can show is listed in the [report](reports/phase-6.md).** Tim's brief for this phase (below) says what to build and in what order, so it's taken
> as the go-ahead, with the defaults chosen here; each item lands as its own draft pull request, stacked on the one
> before, with its own part of the [report](reports/phase-6.md). Technical design:
> [architecture.md §19](architecture.md#19-phase-6-pb-on-android). Everything in [spec.md](spec.md) still applies: the
> sim decides every outcome, metric units, original IP, data files, tests.

## Your direction

The brief, verbatim (2026-11-10):

> New phase: an Android version of Pb. All of Phase 5 (#11-#18) is merged into main, so branch from main and open
> stacked PRs as before (draft, CI green, don't merge). 1) Android export: Godot 4.7 C# Android export preset (arm64),
> and a CI job that builds a debug-signed APK and uploads it as an artifact named Pb-android, like the Windows build.
> 2) Touch controls: left virtual stick to move, right-side drag to look, buttons for fire, aim, reload, crouch, jump,
> interact and enter or exit vehicle, plus driving controls (steer, throttle, brake, handbrake, fire from the wheel).
> They appear automatically on touch devices, with a sensitivity setting and button layout that fit a phone's safe
> area. Menus must work by touch. 3) Mobile performance: a mobile graphics preset using the Mobile or Compatibility
> renderer, with lower shadows, effects and view distance, and a target of a steady 30+ fps on a mid-range phone,
> Thornwick Woods with buggies included. Add a benchmark or smoke test that runs with the mobile preset. 4) Docs: how
> to install the APK on a phone. Tell me when it's done and what you couldn't test without a real device.

How I've read it:

| You said | What it becomes |
|---|---|
| Stacked PRs, as before | Three draft PRs, each on the one before: **A** the Android build (M6.1, with the install docs), **B** touch controls and menus by touch (M6.2), **C** the mobile preset and its benchmark (M6.3). Each passes the sim and network tests and the smoke suite, and updates the README, the architecture and this phase's report. |
| A C# Android export preset (arm64) | An "Android" preset for 64-bit ARM phones (arm64-v8a), Android 7.0 and later, landscape, full screen, allowed the network (playing with others). Godot's Android template only takes .NET 9, so the game targets net9.0 when it's built for Android; the desktop builds stay on .NET 8. The art goes inside the APK, its textures in the phone formats (ETC2/ASTC) as well as the desktop ones. |
| A debug-signed APK | Signed with a debug key kept in the repository, so every build installs over the one before (a new key each run wouldn't), and none is fit for a store. It's Godot's optimised (release) template rather than its debug one: the debug template costs frame time, and a steady 30 fps is the target. The version code is CI's run number. |
| A CI job, an artifact named Pb-android, like the Windows build | "Android build": after the smoke test, like the Windows build, it exports and checks the APK (signed, arm64 only, its assemblies, data and art inside) and uploads `Pb.apk` with `HOW-TO-INSTALL.txt` as **Pb-android**. |
| Left stick, right-side drag, the buttons | A stick that appears where your left thumb lands, look by dragging anywhere on the right that isn't a button, and buttons along the right: fire, aim, reload, crouch, jump, and one for doors, ladders and buggies that says what it'll do ("Get in", "Get out"). A small pause button by the top corner. All drawn on a layer of their own over the HUD. |
| Aim | The game has no aiming down sights, so on a touch screen AIM is a tap on and a tap off (a thumb can't hold it and fire): the view narrows a little and the look slows, for a careful shot. It changes nothing the sim decides. |
| Driving controls | In the driver's seat the left stick steers (left and right only), and the right side has throttle and brake (brake then reverse, as the S key), the handbrake, fire from the wheel and get out. A passenger keeps the on-foot buttons (fire, get out). |
| Appear automatically on touch devices | Shown on a phone or tablet, and on a desktop as soon as a finger touches the screen (hidden again at a mouse click or a key, so a touch laptop works both ways). |
| A sensitivity setting, a layout that fits the safe area | Settings, Controls: touch look speed, button size and how see-through they are, and left-handed (the sides swapped). Every control sits inside the screen's safe area (round a notch or a camera hole), sized in millimetres from the screen's density, so a button is the same size on any phone. |
| Menus must work by touch | Every menu is checked by tapping: on a phone the interface is drawn bigger (by the screen's density), long pages scroll by dragging, text boxes bring up the phone's keyboard, and what only a desktop has (window modes, key bindings, mouse speed) is left out there. |
| A mobile preset (Mobile or Compatibility renderer) | A "mobile" graphics preset, the default on a phone: the Mobile renderer (Vulkan), or Compatibility (OpenGL ES 3) where a phone has no Vulkan (since M6.4, Compatibility on every phone, at the screen's own resolution); one shadow cascade, smaller and nearer; no ambient occlusion, bounced light, reflections, fog volumes, glow or sunbeams; a shorter view distance and nearer level-of-detail switches for the woods; fewer weeds and no ground detail; the 3D view drawn at 75% and scaled up. |
| A steady 30+ fps on a mid-range phone, Thornwick with buggies | It can't be measured without a phone here, so the preset is held to budgets a mid-range phone meets at 30 fps (draw calls, triangles on screen, CPU time a frame), measured on Thornwick Woods with buggies driving. |
| A benchmark or smoke test with the mobile preset | `--mobile-bench`: Thornwick Woods with the mobile preset on the Mobile renderer, a buggy driven along the tracks while bots fight, printing the draw calls, triangles and frame times, and failing past the budgets. CI runs it on a software Vulkan driver (frame rates mean nothing there, the counts do). Since M6.4 it draws with the Compatibility renderer, as a phone does, on Mesa's software OpenGL ES. |
| Docs: how to install the APK | [docs/android.md](android.md): getting the APK, installing it (from the phone, or over USB with adb), updating, the touch controls, and building it yourself; `HOW-TO-INSTALL.txt` beside the APK says the short version. |

## What can't be tested here

There's no phone, no Android emulator (no hardware virtualisation in this container) and no GPU. So the APK is built
and checked (signature, contents, manifest), the touch controls are driven by simulated touches, the menus are tapped
by simulated touches, and the mobile renderer is run on a software Vulkan driver (since M6.4, the Compatibility renderer on
Mesa's software OpenGL ES); what only a phone can show (the frame rate, the feel of the controls, the heat, a phone's own
GPU drivers) is listed in the report.

## After the first install: the grey screen (M6.4)

Tim installed M6.3's APK on his phone (2026-11-10): after Start, on Thornwick Woods at the cold store, the view stayed
grey with the desktop's CONTROLS help over it, never fading, and a render scale of 100 % didn't change it. His brief for
the fix, in short: draw the world on a real phone (the Compatibility renderer on mobile, the desktop staying on Forward+,
no MSAA or occlusion culling on a phone, no FSR at a lower render scale there and a phone starting at full resolution),
no desktop help on a phone, a log line saying how the game is drawn, the help's fade looked into, CI green with a new
APK that installs over the old one, and the docs no longer suggesting 67 % as the first thing to try for a stutter.

| You said | What it became |
|---|---|
| Make the 3D world appear on a real phone | The cause wasn't the renderer: Android's .NET runtime (Mono) threw as the level set the collector's latency mode, before the world, the camera or the HUD were built. `GcModes` leaves the mode alone where the runtime can't change it, a test keeps every other file from setting it, and a level that can't start now says why on the screen and goes back to the menu at a tap. |
| `rendering_method.mobile` = `gl_compatibility`, desktop on Forward+, no MSAA 3D or occlusion culling on mobile | All in project.godot's `.mobile` overrides (and a 1024 px shadow atlas there). What drew differently under Compatibility is fixed: the weathered walls' instance uniforms (too many for its buffer on Thornwick), overlays that multiply (darker in its sRGB blending), and glow (it blacked out the ground's overlays). |
| No FSR at a reduced scale on Android | A phone starts at 100 %, and a lower render scale is scaled up plainly (bilinear) on a phone or under Compatibility. |
| Hide the desktop CONTROLS help on phones | Not shown when `TouchUi.Phone`. |
| A startup log line: renderer, method, GPU, render scale, scaling mode | `RENDER:` lines as the menu and each level start (`adb logcat -s godot`). |
| Why the help never faded; unpause if the tree stays paused | The HUD fades it in its `_Process`, which waits for `Initialize`, and the level stopped before that. The tree wasn't paused; the round's start unpauses it all the same. |
| Docs: not 67 % first | android.md and the report point to the frame cap and the weeds first. |
