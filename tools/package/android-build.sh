#!/usr/bin/env bash
# Exports the Android build: Pb.apk, for 64-bit ARM phones (arm64-v8a), the art inside it. It's Godot's release
# template (optimised: the debug one costs frame time), signed with the repository's debug key
# (tools/package/android/debug.keystore, "android"), so every build installs over the last one and none is fit for
# a store. CI builds it on every run and offers it as the "Pb-android" artifact; docs/android.md says how to put it
# on a phone.
#   tools/package/android-build.sh path/to/godot [out-dir] [version] [version-code]
# Needs:
#   - the Android export template for the Godot version (tools/package/fetch-templates.py 4.7.2 android_release.apk);
#   - the Android SDK's build tools (apksigner, zipalign, aapt2) where ANDROID_HOME says, and a JDK;
#   - the .NET 9 SDK (Godot's Android template only takes net9.0; game/Pb.csproj targets it for Android);
#   - an imported project (tools/ci/import.sh), whose textures then have their ETC2/ASTC forms as well.
# The game lands in <out>/Pb.apk, with HOW-TO-INSTALL.txt (default out: builds/android). The version name is the
# build's stamp; the version code (an Android update needs it no lower than the installed one's) is CI's run number,
# 1 by default.
set -euo pipefail
godot="${1:?path to the Godot .NET binary}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="$(mkdir -p "${2:-$root/builds/android}" && cd "${2:-$root/builds/android}" && pwd)"
version="${3:-$(git -C "$root" rev-parse --short HEAD)}"
code="${4:-1}"
: "${ANDROID_HOME:?ANDROID_HOME must be the Android SDK}"
tools="$(find "$ANDROID_HOME/build-tools" -mindepth 1 -maxdepth 1 -type d | sort -V | tail -1)"
for tool in apksigner zipalign aapt2; do
  [ -x "$tools/$tool" ] || { echo "::error::$tool isn't in $tools (the Android SDK's build tools)"; exit 1; }
done
keystore="$root/tools/package/android/debug.keystore"
# Godot finds the SDK and the JDK through its editor settings (made by the import): point them at ANDROID_HOME and
# JAVA_HOME (if set), whatever an earlier run left there.
settings="${XDG_CONFIG_HOME:-$HOME/.config}/godot/editor_settings-$("$godot" --version | cut -d. -f1-2).tres"
set_editor() {
  if grep -q "^$1 = " "$settings"; then
    sed -i "s|^$1 = .*|$1 = \"$2\"|" "$settings"
  else
    printf '%s = "%s"\n' "$1" "$2" >> "$settings"
  fi
}
if [ -f "$settings" ]; then
  set_editor export/android/android_sdk_path "$ANDROID_HOME"
  [ -z "${JAVA_HOME:-}" ] || set_editor export/android/java_sdk_path "$JAVA_HOME"
fi
apk="$out/Pb.apk"
rm -f "$apk" "$apk.idsig"

# The build's stamp goes in its files (res://build.txt), as on Windows; the version name and code go in the preset,
# put back as it was afterwards.
printf '%s\n' "$version" > "$root/game/build.txt"
presets="$root/game/export_presets.cfg"
cp "$presets" "$out/export_presets.cfg.orig"
trap 'rm -f "$root/game/build.txt"; mv -f "$out/export_presets.cfg.orig" "$presets"' EXIT
sed -i -e "s/^version\/code=.*/version\/code=$code/" -e "s/^version\/name=.*/version\/name=\"$version\"/" "$presets"

# Godot signs the release export with the "release" keystore; for these builds that's the debug key.
export GODOT_ANDROID_KEYSTORE_RELEASE_PATH="$keystore" GODOT_ANDROID_KEYSTORE_RELEASE_USER=androiddebugkey \
  GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD=android
log="$(mktemp)"
"$godot" --headless --path "$root/game" --export-release "Android" "$apk" 2>&1 | tee "$log"
if [ ! -f "$apk" ] || grep -qE "^(SCRIPT )?ERROR" "$log"; then
  echo "::error::The Android export failed (see log above)"
  exit 1
fi

# The .NET runtime pack brings its static libraries (lib/*/*.a, about 60 MB) along with the shared ones; a phone
# never loads them. Out they go, then the APK is aligned again (16 KB pages, for Android 15's larger pages) and signed
# again with the same key.
zip -q -d "$apk" 'lib/*.a' > /dev/null
"$tools/zipalign" -P 16 -f 4 "$apk" "$apk.aligned"
mv "$apk.aligned" "$apk"
"$tools/apksigner" sign --ks "$keystore" --ks-key-alias androiddebugkey --ks-pass pass:android --key-pass pass:android \
  --v4-signing-enabled false "$apk"
rm -f "$apk.idsig" # Godot's own signing left one; it's only for streaming an install over adb
"$tools/apksigner" verify "$apk"

# What it is: the package, the one ABI, the permission to play with others, the version.
badging="$("$tools/aapt2" dump badging "$apk")"
grep -q "^package: name='org.pbgame.pb' versionCode='$code' versionName='$version'" <<< "$badging" \
  || { echo "::error::The APK's package or version isn't as asked:"; grep "^package:" <<< "$badging"; exit 1; }
grep -qx "native-code: 'arm64-v8a'" <<< "$badging" || { echo "::error::The APK isn't for arm64-v8a alone"; exit 1; }
grep -q "uses-permission: name='android.permission.INTERNET'" <<< "$badging" \
  || { echo "::error::The APK can't use the network"; exit 1; }
# What's in it: the engine, the .NET runtime and the game's assemblies, its data, and the art (in its phone formats).
contents="$(unzip -l "$apk")"
assemblies=assets/.godot/mono/publish/arm64
for file in lib/arm64-v8a/libgodot_android.so lib/arm64-v8a/libmonosgen-2.0.so "$assemblies/Pb.dll" "$assemblies/Pb.Sim.dll" \
  "$assemblies/Pb.Net.dll" assets/data/rules.jsonc assets/build.txt; do
  grep -q " $file$" <<< "$contents" || { echo "::error::$file is missing from the APK"; exit 1; }
done
if grep -q "\.a$" <<< "$contents"; then echo "::error::static libraries are still in the APK"; exit 1; fi
art=$(grep -c "assets/\.godot/imported/.*\.etc2\.ctex$" <<< "$contents" || true)
[ "$art" -gt 0 ] || { echo "::error::no art in its phone formats (ETC2/ASTC) in the APK"; exit 1; }
# How to put it on a phone, beside it (the artifact is the folder).
cp "$root/tools/package/android/HOW-TO-INSTALL.txt" "$out/"
echo "Android build $version (code $code): $apk ($(du -h "$apk" | cut -f1)), $art textures in their phone formats"
