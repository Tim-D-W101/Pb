# Pb on Android

Pb runs on Android phones and tablets with a 64-bit ARM processor (arm64, nearly every phone since 2017) and Android 7.0
or later. It's one file, `Pb.apk` (about 230 MB): the game, its .NET runtime and all its art. It isn't in a store:
you put it on the phone yourself ("sideloading"), as below.

## Getting the APK

Every change builds it. On GitHub, open the repository's **Actions** tab, pick the newest **CI** run with a green tick
(or the one for the pull request you want to try), scroll to **Artifacts** and download **Pb-android**. It's a zip
holding `Pb.apk` and `HOW-TO-INSTALL.txt`. You need to be signed in to GitHub to download it; on the phone itself
that works in its browser too.

## Installing it on the phone

Either of these:

**On the phone, from the file.**
1. Unzip `Pb-android.zip` (on the phone, the Files app opens a zip; on a computer, unzip it and copy `Pb.apk` to the
   phone's Download folder over USB, or send it any way you like).
2. Tap `Pb.apk`. Android asks you to allow the app you opened it from (the Files app, or the browser) to install
   apps: tap **Settings**, turn on **Allow from this source**, and go back.
3. Tap **Install**. Play Protect may say it doesn't know the developer: tap **More details**, then **Install anyway**
   (the game is signed with its own debug key, not a store's). When it's done, tap **Open**, or find **Pb** with your
   other apps.

**Over USB, with adb** (a computer with Android's platform tools):
1. On the phone, turn on **Developer options** (Settings, About phone, tap **Build number** seven times) and, in them,
   **USB debugging**; plug it in and allow the computer when it asks.
2. `adb install -r Pb.apk`

## Updating and removing it

A newer build installs over the old one the same way, keeping your settings: every build is signed with the same key
and numbered higher. If Android ever says the app isn't installed because it conflicts with one already there (a build
signed with another key, made on someone's own machine), uninstall Pb first. To remove it, hold its icon and tap
**Uninstall**, or `adb uninstall org.pbgame.pb`.

## Playing

It starts on the main menu, in landscape, full screen, drawn big enough to tap. Tap your way through it; a page that's
longer than the screen scrolls when you drag it, and a text box (your name, an address) brings up the keyboard. The
phone's **back** button goes back a screen, and on the title screen leaves the game.

In a round, the controls are drawn over the view:

- **Moving:** put your left thumb down anywhere on the left of the screen and a stick appears under it; push it the way
  you want to go. Push it out past its ring to sprint.
- **Looking:** drag anywhere on the right that isn't a button. Dragging on **FIRE** while you hold it turns you too, so
  you can follow what you're shooting at.
- **Buttons** by your right thumb: **FIRE**, **AIM** (a tap to aim, a tap to stop: the view closes in a little and
  turns slower, for a careful shot), **JUMP**, **CROUCH** (a tap to go down, a tap to stand), **RELOAD** (refills the
  loader from a pod), and a button that appears when there's something to use and says what: **GET IN**, **OPEN**,
  **SHUT**, **CLIMB**.
- **In a buggy:** the stick steers, **GAS** and **BRAKE** (held once you've stopped, it reverses), the **HANDBRAKE**,
  **FIRE** to shoot from the wheel, and **GET OUT** once you've slowed down. In a passenger's seat you shoot as on foot.
- **Along the top:** pause (**II**), **SCORES** (the scoreboard, while you hold it) and, playing with others, **CHAT**.
  The back button pauses too.

In **Settings, Controls** (at the top on a phone): how fast dragging turns you, how big the buttons are and how
see-through, and **Left-handed**, which swaps the sides. The buttons keep clear of a notch or a camera hole.

**Play with others** works over Wi-Fi as on a computer: it lists the games on your network, or you join one by its
address (a game hosted on a computer, or a dedicated server).

A game pad (Bluetooth or USB) plays it as on a computer, with the same buttons: once you use it the touch controls go,
and they're back at your next touch of the screen.

## Graphics

A phone starts on the **Mobile** graphics preset, drawn by Godot's Compatibility renderer (OpenGL ES 3, which every
phone it runs on has): one sun shadow, nearer and coarser; none of the desktop's costlier effects (ambient occlusion,
glow, sunbeams, reflections); the trees drawn less far and made simpler sooner; fewer weeds; the 3D view drawn at the
screen's own resolution; and a cap of 30 frames a second, for a steady frame rate and a cooler phone. There's no 3D
scene behind the menus.

**Settings, Video** changes any of it. A fast phone can lift the **Frame cap** to 60. If a phone stutters or gets hot,
keep the frame cap at 30 and set **Weeds** to **None** first. A lower **Render scale** comes last: it draws the view
smaller and scales it up, so it looks softer, and it hasn't been tried on a phone yet.

## If something goes wrong

With the phone on USB (as for adb above), `adb logcat -s godot` shows what the game prints while it runs, its errors
among them; keep that when reporting a problem. Its `RENDER:` lines (one as the menu starts, one as each round starts)
say what drew the game: the renderer (`gl_compatibility` on a phone), its driver and the phone's GPU, the 3D view's scale
and how it's scaled up, MSAA, and the screen's size.

If a round can't start, the screen says why instead of staying grey. Tap it to go back to the menu; the log has the
whole report.

## Building it yourself

`tools/package/android-build.sh path/to/godot` (from the repository) makes `builds/android/Pb.apk`. It needs:
- Godot's Android export template: `tools/package/fetch-templates.py 4.7.2 android_release.apk`;
- the Android SDK's build tools (apksigner, zipalign, aapt2), with `ANDROID_HOME` pointing at the SDK, and a JDK;
- the .NET 9 SDK (Godot's Android template only takes net9.0; `game/Pb.csproj` targets it for Android only);
- an imported project (`tools/ci/import.sh path/to/godot`), which also makes each texture's phone formats (ETC2/ASTC).

The script signs it with `tools/package/android/debug.keystore` (password `android`), checks it (the signature, arm64
only, the network permission, the assemblies, the data and the art in their phone formats) and puts
`HOW-TO-INSTALL.txt` beside it. CI does the same on every run.
