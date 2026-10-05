#!/usr/bin/env bash
# Exports the art as one pack per asset (each record in game/data/assets.jsonc), into <game>/art/
# Pb-art-<id>.pck, which the exported game mounts when it starts (game/core/ArtFiles.cs). A new or
# re-imported texture or model then costs players just its own pack, not all the art. A file belongs to
# the asset whose id begins its name (dirt_albedo.jpg to dirt, oil_drum_Image_0.jpg to oil_drum).
# Each pack's stamp (a hash of its files with their import settings, the project settings and the
# Godot version) goes into <stamps> as "pack=<stamp> art/Pb-art-<id>.pck". A fresh import doesn't give
# byte-identical files, so with PB_RELEASE_URL set (CI sets it to the test-build release), a pack whose
# stamp the published manifest already lists is downloaded from the release and reused as it is, after
# checking it against the published SHA-256. Needs an imported project (tools/ci/import.sh).
#   tools/package/art-packs.sh path/to/godot <game> <stamps>
set -euo pipefail
godot="${1:?path to the Godot .NET binary}"
game="${2:?the exported game folder}"
stamps="${3:?where to write the pack stamps}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
mkdir -p "$game/art"
: > "$stamps"

# Longest ids first, so an id that begins another's name doesn't take its files.
ids="$(grep -o '"id": "[A-Za-z0-9_-]*"' "$root/game/data/assets.jsonc" | cut -d'"' -f4 | awk '{ print length, $0 }' | sort -rn | cut -d' ' -f2-)"
declare -A owned
while IFS= read -r f; do
  name="$(basename "$f")"
  owner=""
  for id in $ids; do
    case "$name" in "${id}_"* | "${id}."*) owner="$id"; break ;; esac
  done
  if [ -z "$owner" ]; then
    echo "::error::$f belongs to no asset in game/data/assets.jsonc"
    exit 1
  fi
  owned[$owner]+="$f"$'\n'
done < <(cd "$root/game" && find art -type f ! -name '*.import' ! -name '.*' | LC_ALL=C sort)

published=""
if [ -n "${PB_RELEASE_URL:-}" ]; then
  published="$(mktemp)"
  curl -fsSL "$PB_RELEASE_URL/manifest.txt" -o "$published" || : > "$published"
fi
version="$("$godot" --version)"
reused=0
made=0
for id in $(printf '%s\n' "${!owned[@]}" | LC_ALL=C sort); do
  mapfile -t files < <(printf '%s' "${owned[$id]}")
  pack="art/Pb-art-$id.pck"
  stamp="$( (cd "$root/game" && for f in "${files[@]}"; do
      sha256sum "$f"
      if [ -f "$f.import" ]; then sha256sum "$f.import"; fi
    done && sha256sum project.godot && echo "$version") | sha256sum | cut -c1-16)"
  echo "pack=$stamp $pack" >> "$stamps"
  if [ -n "$published" ] && grep -qxF "pack=$stamp $pack" "$published"; then
    want="$(awk -v p="$pack" '$1 ~ /^file=/ && $3 == p { sub(/^file=/, "", $1); print $1 }' "$published")"
    if [ -n "$want" ] && curl -fsSL "$PB_RELEASE_URL/$(basename "$pack")" -o "$game/$pack" &&
       [ "$(sha256sum "$game/$pack" | cut -d' ' -f1)" = "$want" ]; then
      reused=$((reused + 1))
      continue
    fi
  fi
  "$root/tools/package/art-pack.sh" "$godot" "$game/$pack" "${files[@]}"
  made=$((made + 1))
done
echo "Art packs: $((reused + made)) in $game/art ($(du -sh "$game/art" | cut -f1)): $made exported, $reused unchanged since the published build and reused"
