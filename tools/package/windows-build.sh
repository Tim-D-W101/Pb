#!/usr/bin/env bash
# Exports the ready-to-run Windows build: unzip, double-click Pb.exe, no Godot or .NET needed
# (the .NET runtime is bundled). Needs the Windows export templates for the Godot version
# (tools/package/fetch-templates.py) and an imported project (tools/ci/import.sh). CI builds this on
# every run and offers it as the "Pb-windows" artifact.
#   tools/package/windows-build.sh path/to/godot [out-dir]    (default: builds/windows; the game lands in <out>/Pb)
set -euo pipefail
godot="${1:?path to the Godot .NET binary}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
out="$(mkdir -p "${2:-$root/builds/windows}" && cd "${2:-$root/builds/windows}" && pwd)"
rm -rf "$out/Pb"
mkdir -p "$out/Pb"

log="$(mktemp)"
"$godot" --headless --path "$root/game" --export-release "Windows Desktop" "$out/Pb/Pb.exe" 2>&1 | tee "$log"
if [ ! -f "$out/Pb/Pb.exe" ] || [ ! -f "$out/Pb/Pb.pck" ] || grep -qE "^(SCRIPT )?ERROR" "$log"; then
  echo "::error::The Windows export failed (see log above)"
  exit 1
fi

# Debug symbols are no use to players.
rm -f "$out"/Pb/data_Pb_windows_x86_64/*.pdb
cat > "$out/Pb/HOW-TO-PLAY.txt" <<'EOF'
Pb: how to play

1. Keep everything in this folder together (Pb.exe, Pb.pck and the data_Pb_windows_x86_64 folder).
2. Double-click Pb.exe. If Windows says "Windows protected your PC", click "More info", then
   "Run anyway" (the game isn't signed, so Windows doesn't know it yet).
3. In the menu: Play, then Oxbarrow Works, a difficulty, and Start.

Controls: WASD and mouse, left click fires, Shift sprints, Ctrl or C crouches, Space jumps,
Q / E lean, X swaps shoulder, V slides, R refills the loader, Esc pauses (settings, quit).
F4 shows the frame rate, F12 switches graphics presets (low / medium / high), F11 fullscreen,
F3 shows what the bots are thinking. The Training ground (main menu) is the ballistics range;
F3 there fires 1,000 balls at once.
EOF
echo "Windows build ($(git -C "$root" rev-parse --short HEAD)): $out/Pb ($(du -sh "$out/Pb" | cut -f1))"
