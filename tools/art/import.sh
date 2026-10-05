#!/usr/bin/env bash
# Imports one finished Higgsfield result into the game, with a provenance record in
# game/data/assets.jsonc (job, generator, prompt, download URL, files produced):
#   tools/art/import.sh texture <material-id> <job-id> <generator> <url> "<prompt>" [extra ArtImport flags]
#   tools/art/import.sh model <prop-id> <job-id> <generator> <url> "<prompt>"
# Textures are made tileable and get normal and roughness maps (art/textures/, JPEG). Extra texture
# flags: --size=1024 --region=x,y,w,h (one tile of a picture holding several, as fractions)
# --stretch (resize the region square rather than cropping its middle) --repeats=across,down (whole
# repeats of a regular pattern in the region: bricks, courses, planks, corrugations; 0 for none)
# --flatten=0.8 (even out broad shading) --band=0.12 (seam blend width) --normal-strength=2
# --roughness=0.9 --roughness-variation=0.15.
# Models are tidied (textures shrunk to JPEG, the baked glow removed) into art/models/ and measured.
# Extra model flags: --max-texture=1024, --roughness=R (untextured materials), --height=M (scale to M
# metres tall and stand it on the origin).
# Needs Godot .NET as $GODOT (default: godot) and a built game/Pb.csproj.
set -euo pipefail
kind="${1:?texture or model}"
id="${2:?asset id (material or prop id)}"
job="${3:?Higgsfield job id}"
generator="${4:?generator model name}"
url="${5:?download URL}"
prompt="${6:?prompt}"
shift 6
godot="${GODOT:-godot}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
case "$kind" in
  texture) suffix="${url##*.}"; suffix="${suffix%%\?*}" ;;
  model) suffix="glb" ;;
  *) echo "unknown kind '$kind' (texture or model)" >&2; exit 2 ;;
esac

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
file="$tmp/source.$suffix"
curl -fsSL "$url" -o "$file"
"$godot" --headless --path "$root/game" res://tools/ArtImport.tscn -- "--$kind" "--id=$id" "--source=$file" \
  "--job=$job" "--generator=$generator" "--url=$url" "--prompt=$prompt" "$@"
