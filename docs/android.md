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

It starts on the main menu, in landscape, full screen; tap your way through it as with a mouse. **Play with others**
works over Wi-Fi as on a computer: it lists the games on your network, or you join one by its address (a game hosted
on a computer, or a dedicated server).

A game pad (Bluetooth or USB) plays it as on a computer, with the same buttons.

## If something goes wrong

With the phone on USB (as for adb above), `adb logcat -s godot` shows what the game prints while it runs, its errors
among them; keep that when reporting a problem.

## Building it yourself

`tools/package/android-build.sh path/to/godot` (from the repository) makes `builds/android/Pb.apk`. It needs:
- Godot's Android export template: `tools/package/fetch-templates.py 4.7.2 android_release.apk`;
- the Android SDK's build tools (apksigner, zipalign, aapt2), with `ANDROID_HOME` pointing at the SDK, and a JDK;
- the .NET 9 SDK (Godot's Android template only takes net9.0; `game/Pb.csproj` targets it for Android only);
- an imported project (`tools/ci/import.sh path/to/godot`), which also makes each texture's phone formats (ETC2/ASTC).

The script signs it with `tools/package/android/debug.keystore` (password `android`), checks it (the signature, arm64
only, the network permission, the assemblies, the data and the art in their phone formats) and puts
`HOW-TO-INSTALL.txt` beside it. CI does the same on every run.
