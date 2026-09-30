#!/usr/bin/env bash
# Downloads the Godot .NET (mono) Linux build and exposes it as <dest>/godot.
#   tools/ci/install-godot.sh 4.7.2 ~/.local/godot
set -euo pipefail
version="${1:?Godot version, e.g. 4.7.2}"
dest="${2:?destination directory}"
name="Godot_v${version}-stable_mono_linux_x86_64"
url="https://github.com/godotengine/godot/releases/download/${version}-stable/${name}.zip"

mkdir -p "$dest"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
curl -fsSL --retry 4 --retry-delay 2 -o "$tmp/godot.zip" "$url"
unzip -q -o "$tmp/godot.zip" -d "$dest"
ln -sf "$dest/$name/Godot_v${version}-stable_mono_linux.x86_64" "$dest/godot"
chmod +x "$dest/$name/Godot_v${version}-stable_mono_linux.x86_64"
"$dest/godot" --version
