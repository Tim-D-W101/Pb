#!/usr/bin/env bash
# Exports the ready-to-run Windows build: unzip, double-click Play.bat (or Pb.exe), no Godot or .NET
# needed (the .NET runtime is bundled). Play.bat keeps the game up to date from the latest test build
# (tools/package/windows/update.ps1). Needs the Windows export templates for the Godot version
# (tools/package/fetch-templates.py) and an imported project (tools/ci/import.sh). CI builds this on
# every run, offers it as the "Pb-windows" artifact and publishes it as the "test-build" release.
#   tools/package/windows-build.sh path/to/godot [out-dir] [version]
# The game lands in <out>/Pb (default out: builds/windows). The art is in a pack of its own, Pb-art.pck
# (tools/package/art-pack.sh), so code and data changes don't make anyone download it again. A fresh
# import doesn't give byte-identical files, so with PB_RELEASE_URL set (CI sets it to the test-build
# release) the published art pack is reused as it is while the art hasn't changed: same art files, Godot
# version and project settings (the "art" stamp in the manifest).
# <out>/release gets what the release offers: Pb-windows.zip (the whole game), each of the game's own
# files on its own (what Play.bat downloads, only those that changed), Pb-update.zip (all of the game's
# own files, for launchers from before per-file updates) and manifest.txt (version, date, engine stamp,
# and each game file's SHA-256 and size).
set -euo pipefail
godot="${1:?path to the Godot .NET binary}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="$(mkdir -p "${2:-$root/builds/windows}" && cd "${2:-$root/builds/windows}" && pwd)"
version="${3:-$(git -C "$root" rev-parse --short HEAD)}"
rm -rf "$out/Pb" "$out/release"
mkdir -p "$out/Pb" "$out/release"

log="$(mktemp)"
"$godot" --headless --path "$root/game" --export-release "Windows Desktop" "$out/Pb/Pb.exe" 2>&1 | tee "$log"
if [ ! -f "$out/Pb/Pb.exe" ] || [ ! -f "$out/Pb/Pb.pck" ] || grep -qE "^(SCRIPT )?ERROR" "$log"; then
  echo "::error::The Windows export failed (see log above)"
  exit 1
fi

# The game's own pack leaves the art out (export_presets.cfg excludes art/*): no imported art file in it.
# (Its data files do name art paths, so look for the imported files themselves. grep -c reads it all:
# grep -q would stop at the first match and, with pipefail, hide it.)
leaked=$(strings "$out/Pb/Pb.pck" | grep -cF -f <(cd "$root/game" && find art -type f ! -name '*.import' ! -name '.*' -printf '.godot/imported/%f-\n') || true)
if [ "${leaked:-0}" -gt 0 ]; then
  echo "::error::The game's own pack contains $leaked imported art files (export_presets.cfg should exclude art/*)"
  exit 1
fi
art_stamp="$( (cd "$root/game" && find art -type f | LC_ALL=C sort | xargs sha256sum && sha256sum project.godot && "$godot" --version) |
  sha256sum | cut -c1-16)"
reused=false
if [ -n "${PB_RELEASE_URL:-}" ]; then
  published="$(mktemp)"
  if curl -fsSL "$PB_RELEASE_URL/manifest.txt" -o "$published" && grep -qx "art=$art_stamp" "$published"; then
    want="$(sed -n 's/^file=\([0-9a-f]\{64\}\) [0-9]* Pb-art\.pck$/\1/p' "$published")"
    if [ -n "$want" ] && curl -fsSL "$PB_RELEASE_URL/Pb-art.pck" -o "$out/Pb/Pb-art.pck" &&
       [ "$(sha256sum "$out/Pb/Pb-art.pck" | cut -d' ' -f1)" = "$want" ]; then
      reused=true
      echo "The art hasn't changed since the published build ($art_stamp): reusing its art pack"
    fi
  fi
fi
if [ "$reused" != true ]; then
  "$root/tools/package/art-pack.sh" "$godot" "$out/Pb/Pb-art.pck"
fi

# Debug symbols are no use to players.
rm -f "$out"/Pb/data_Pb_windows_x86_64/*.pdb
cp "$root/tools/package/windows/Play.bat" "$root/tools/package/windows/update.ps1" "$out/Pb/"
cat > "$out/Pb/HOW-TO-PLAY.txt" <<'EOF'
Pb: how to play

1. Keep everything in this folder together (Pb.exe, Pb.pck, Pb-art.pck and the data_Pb_windows_x86_64
   folder).
2. Double-click Play.bat. It downloads only the files that changed since your version (usually about
   1 MB; more when new art arrives), then starts the game; without internet it just starts the game.
   Pb.exe starts it without updating.
   If Windows says "Windows protected your PC", click "More info", then "Run anyway" (the game isn't
   signed, so Windows doesn't know it yet).
3. In the menu: Play, then for Oxbarrow Works pick a mode (Solo, Free-for-all or Teams), how many
   players, a difficulty, and Start.

Controls: WASD and mouse, left click fires, Shift sprints, Ctrl or C crouches, Space jumps,
Q / E lean, X swaps shoulder, V slides, R refills the loader, Esc pauses (settings, quit).
F4 shows the frame rate, F12 switches graphics presets (low / medium / high / ultra; Esc → render scale if it lags), F11 fullscreen,
F3 shows what the bots are thinking. The Training ground (main menu) is the ballistics range;
F3 there fires 1,000 balls at once.
EOF
# The game's own files make the update pack; everything else (Pb.exe, the .NET runtime, Godot's
# assemblies) is the engine, stamped so the launcher knows when it must fetch the whole game.
game_files=(Pb.pck Pb-art.pck data_Pb_windows_x86_64/Pb.dll data_Pb_windows_x86_64/Pb.Sim.dll data_Pb_windows_x86_64/Pb.deps.json
  data_Pb_windows_x86_64/Pb.runtimeconfig.json Play.bat update.ps1 HOW-TO-PLAY.txt)
for f in "${game_files[@]}"; do
  [ -f "$out/Pb/$f" ] || { echo "::error::$f is missing from the Windows build"; exit 1; }
done
engine="$(cd "$out/Pb" && find . -type f | sed 's|^\./||' | grep -vxF -f <(printf '%s\n' "${game_files[@]}" manifest.txt) \
  | LC_ALL=C sort | xargs sha256sum | sha256sum | cut -c1-16)"
{
  printf 'version=%s\ndate=%s\nengine=%s\nart=%s\n' "$version" "$(date -u '+%Y-%m-%d %H:%M UTC')" "$engine" "$art_stamp"
  for f in "${game_files[@]}"; do
    printf 'file=%s %s %s\n' "$(sha256sum "$out/Pb/$f" | cut -d' ' -f1)" "$(stat -c %s "$out/Pb/$f")" "$f"
  done
} > "$out/Pb/manifest.txt"
cp "$out/Pb/manifest.txt" "$out/release/"
# Each game file is published under its own name (they're all different), for Play.bat to fetch one by one.
for f in "${game_files[@]}"; do
  cp "$out/Pb/$f" "$out/release/$(basename "$f")"
done
(cd "$out" && zip -qr -9 -X release/Pb-windows.zip Pb)
(cd "$out/Pb" && zip -q -9 -X ../release/Pb-update.zip "${game_files[@]}" manifest.txt)
code_kb=$(( ($(stat -c %s "$out/Pb/Pb.pck") + $(stat -c %s "$out/Pb/data_Pb_windows_x86_64/Pb.dll") + $(stat -c %s "$out/Pb/data_Pb_windows_x86_64/Pb.Sim.dll")) / 1024 ))
echo "Windows build $version: $out/Pb ($(du -sh "$out/Pb" | cut -f1)); whole game $(du -h "$out/release/Pb-windows.zip" | cut -f1)," \
  "art pack $(du -h "$out/Pb/Pb-art.pck" | cut -f1), a code and data update about ${code_kb} KB"
