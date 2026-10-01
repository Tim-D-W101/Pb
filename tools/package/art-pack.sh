#!/usr/bin/env bash
# Exports the art (game/art: textures and models, as imported for desktop) into a pack of its own, which an
# exported game mounts when it starts (game/core/ArtFiles.cs). The game's own pack leaves the art out
# (export_presets.cfg excludes art/*), so an update that changes only code or data stays small and the
# art is downloaded again only when it changes. Needs an imported project (tools/ci/import.sh).
#   tools/package/art-pack.sh path/to/godot out/Pb-art.pck
set -euo pipefail
godot="${1:?path to the Godot .NET binary}"
out="${2:?where to write the art pack}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
presets="$root/game/export_presets.cfg"

# A preset listing every art file, added for this export only.
backup="$(mktemp)"
cp "$presets" "$backup"
trap 'cp "$backup" "$presets"; rm -f "$backup"' EXIT
files="$(cd "$root/game" && find art -type f ! -name '*.import' ! -name '.*' | LC_ALL=C sort | sed 's|^|"res://|; s|$|"|' | paste -sd, -)"
if [ -z "$files" ]; then
  echo "::error::No art to pack in game/art"
  exit 1
fi
next=$(grep -c '^\[preset\.[0-9]*\]$' "$presets")
cat >> "$presets" <<PRESET

[preset.$next]

name="Art pack"
platform="Windows Desktop"
runnable=false
dedicated_server=false
custom_features=""
export_filter="resources"
export_files=PackedStringArray($files)
include_filter=""
exclude_filter=""
export_path=""

[preset.$next.options]

binary_format/architecture="x86_64"
PRESET

mkdir -p "$(dirname "$out")"
log="$(mktemp)"
"$godot" --headless --path "$root/game" --export-pack "Art pack" "$out" 2>&1 | tee "$log"
if [ ! -s "$out" ] || grep -qE "^(SCRIPT )?ERROR" "$log"; then
  echo "::error::The art pack export failed (see log above)"
  exit 1
fi
echo "Art pack: $out ($(du -h "$out" | cut -f1), $(tr ',' '\n' <<<"$files" | wc -l) art files)"
