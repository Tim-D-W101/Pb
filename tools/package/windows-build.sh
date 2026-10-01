#!/usr/bin/env bash
# Exports the ready-to-run Windows build: unzip, double-click Play.bat (or Pb.exe), no Godot or .NET
# needed (the .NET runtime is bundled). Play.bat keeps the game up to date from the latest test build
# (tools/package/windows/update.ps1). Needs the Windows export templates for the Godot version
# (tools/package/fetch-templates.py) and an imported project (tools/ci/import.sh). CI builds this on
# every run, offers it as the "Pb-windows" artifact and publishes it as the "test-build" release.
#   tools/package/windows-build.sh path/to/godot [out-dir] [version]
# The game lands in <out>/Pb (default out: builds/windows); <out>/release gets Pb-windows.zip (the whole
# game), Pb-update.zip (just the game's own files) and manifest.txt (version, date, engine stamp).
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

# Debug symbols are no use to players.
rm -f "$out"/Pb/data_Pb_windows_x86_64/*.pdb
cp "$root/tools/package/windows/Play.bat" "$root/tools/package/windows/update.ps1" "$out/Pb/"
cat > "$out/Pb/HOW-TO-PLAY.txt" <<'EOF'
Pb: how to play

1. Keep everything in this folder together (Pb.exe, Pb.pck and the data_Pb_windows_x86_64 folder).
2. Double-click Play.bat. It downloads whatever changed since your version (usually a small update),
   then starts the game; without internet it just starts the game. Pb.exe starts it without updating.
   If Windows says "Windows protected your PC", click "More info", then "Run anyway" (the game isn't
   signed, so Windows doesn't know it yet).
3. In the menu: Play, then Oxbarrow Works, a difficulty, and Start.

Controls: WASD and mouse, left click fires, Shift sprints, Ctrl or C crouches, Space jumps,
Q / E lean, X swaps shoulder, V slides, R refills the loader, Esc pauses (settings, quit).
F4 shows the frame rate, F12 switches graphics presets (low / medium / high / ultra; Esc → render scale if it lags), F11 fullscreen,
F3 shows what the bots are thinking. The Training ground (main menu) is the ballistics range;
F3 there fires 1,000 balls at once.
EOF
# The game's own files make the update pack; everything else (Pb.exe, the .NET runtime, Godot's
# assemblies) is the engine, stamped so the launcher knows when it must fetch the whole game.
game_files=(Pb.pck data_Pb_windows_x86_64/Pb.dll data_Pb_windows_x86_64/Pb.Sim.dll data_Pb_windows_x86_64/Pb.deps.json
  data_Pb_windows_x86_64/Pb.runtimeconfig.json Play.bat update.ps1 HOW-TO-PLAY.txt)
for f in "${game_files[@]}"; do
  [ -f "$out/Pb/$f" ] || { echo "::error::$f is missing from the Windows build"; exit 1; }
done
engine="$(cd "$out/Pb" && find . -type f | sed 's|^\./||' | grep -vxF -f <(printf '%s\n' "${game_files[@]}" manifest.txt) \
  | LC_ALL=C sort | xargs sha256sum | sha256sum | cut -c1-16)"
printf 'version=%s\ndate=%s\nengine=%s\n' "$version" "$(date -u '+%Y-%m-%d %H:%M UTC')" "$engine" > "$out/Pb/manifest.txt"
cp "$out/Pb/manifest.txt" "$out/release/"
(cd "$out" && zip -qr -9 -X release/Pb-windows.zip Pb)
(cd "$out/Pb" && zip -q -9 -X ../release/Pb-update.zip "${game_files[@]}" manifest.txt)
echo "Windows build $version: $out/Pb ($(du -sh "$out/Pb" | cut -f1)); release zips: whole game $(du -h "$out/release/Pb-windows.zip" | cut -f1), update $(du -h "$out/release/Pb-update.zip" | cut -f1)"
