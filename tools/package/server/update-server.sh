#!/usr/bin/env bash
# Brings this Pb dedicated server up to date with the latest test build (run-server.sh runs it before each start).
# When the release's version differs from this one's (version.txt), it downloads Pb-server-linux.tar.gz and puts
# its files in place of these, keeping your server.jsonc. Players' copies update with Play.bat, and a server only
# lets in copies of its own version, so restart the server after a new build (docs/hosting.md).
set -euo pipefail
base="${PB_RELEASE_URL:-https://github.com/Tim-D-W101/Pb/releases/download/test-build}"
here="$(cd "$(dirname "$0")" && pwd)"
have="$(head -n1 "$here/version.txt" 2>/dev/null || echo none)"
newest="$(curl -fsSL --max-time 20 "$base/manifest.txt" | sed -n 's/^version=//p' | tr -d '\r')"
if [ -z "$newest" ]; then
  echo "The release names no version." >&2
  exit 1
fi

if [ "$newest" = "$have" ]; then
  echo "This Pb server ($have) is the newest."
  exit 0
fi

echo "Updating the Pb server from $have to $newest..."
staging="$(mktemp -d "$here/.update.XXXXXX")"
trap 'rm -rf "$staging"' EXIT
curl -fsSL --max-time 900 "$base/Pb-server-linux.tar.gz" | tar -xz -C "$staging" --strip-components=1
[ -x "$staging/Pb.x86_64" ] || { echo "The download has no server in it." >&2; exit 1; }
# Each file and folder moves in whole (a rename on the same disk), so nothing is ever half written, this script included.
for item in "$staging"/*; do
  name="$(basename "$item")"
  if [ "$name" = server.jsonc ] && [ -e "$here/server.jsonc" ]; then
    continue
  fi
  rm -rf "$here/.old-$name"
  if [ -e "$here/$name" ]; then
    mv "$here/$name" "$here/.old-$name"
  fi
  mv "$item" "$here/$name"
  rm -rf "$here/.old-$name"
done
echo "Updated to $(head -n1 "$here/version.txt")."
