#!/usr/bin/env bash
# Packages the dedicated server (docs/hosting.md) for Linux and Windows. Each package is the game without its art,
# started headless with -- --server, with server.jsonc beside it to set it up and a script that starts it after
# bringing it up to date with the latest test build (tools/package/server/). Run tools/package/windows-build.sh first:
# the Windows server is made from its export (Pb.exe, the pack, the .NET runtime), and the Linux one is exported here
# with the same version. Needs the Linux export template and the Windows console one (tools/package/fetch-templates.py
# 4.7.2 linux_release.x86_64 windows_release_x86_64_console.exe); the console program gives the Windows server a window
# to log to. CI builds both with each test build and publishes them on the "test-build" release.
#   tools/package/server-build.sh path/to/godot [windows-build] [out-dir]
# windows-build defaults to builds/windows/Pb and out-dir to builds/server. The servers land in <out>/linux/Pb-server
# and <out>/windows/Pb-server, and <out>/release gets Pb-server-linux.tar.gz and Pb-server-windows.zip.
set -euo pipefail
godot="${1:?path to the Godot .NET binary}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
windows="$(cd "${2:-$root/builds/windows/Pb}" && pwd)"
out="$(mkdir -p "${3:-$root/builds/server}" && cd "${3:-$root/builds/server}" && pwd)"
version="$(sed -n 's/^version=//p' "$windows/manifest.txt")"
[ -n "$version" ] || { echo "::error::$windows/manifest.txt names no version (run tools/package/windows-build.sh first)"; exit 1; }
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
templates="$data_home/godot/export_templates/$("$godot" --version | sed -E 's/^([0-9.]+\.[a-z0-9]+\.mono).*/\1/')"
console="$templates/windows_release_x86_64_console.exe"
[ -f "$console" ] || { echo "::error::$console is missing (tools/package/fetch-templates.py)"; exit 1; }
rm -rf "$out/linux" "$out/windows" "$out/release"
mkdir -p "$out/linux/Pb-server" "$out/windows/Pb-server" "$out/release"
linux="$out/linux/Pb-server"
win="$out/windows/Pb-server"

# Linux: its own export, stamped with the Windows build's version, so they and the players' copies play together.
printf '%s\n' "$version" > "$root/game/build.txt"
trap 'rm -f "$root/game/build.txt"' EXIT
log="$(mktemp)"
"$godot" --headless --path "$root/game" --export-release "Linux" "$linux/Pb.x86_64" 2>&1 | tee "$log"
if [ ! -f "$linux/Pb.x86_64" ] || [ ! -f "$linux/Pb.pck" ] || grep -qE "^(SCRIPT )?ERROR" "$log"; then
  echo "::error::The Linux export failed (see log above)"
  exit 1
fi

# Debug symbols are no use to a server's owner.
[ -d "$linux/data_Pb_linuxbsd_x86_64" ] || { echo "::error::The Linux export has no data_Pb_linuxbsd_x86_64 (.NET) folder"; exit 1; }
rm -f "$linux"/data_Pb_linuxbsd_x86_64/*.pdb
cp "$root/tools/package/server/run-server.sh" "$root/tools/package/server/update-server.sh" "$root/tools/package/server/pb-server.service" "$linux/"
chmod +x "$linux/Pb.x86_64" "$linux/run-server.sh" "$linux/update-server.sh"

# Windows: the Windows build's program, pack and runtime, and the console program that starts Pb.exe in its window
# (what Godot's export makes of the same template when its console wrapper is on).
cp -a "$windows/Pb.exe" "$windows/Pb.pck" "$windows/data_Pb_windows_x86_64" "$win/"
cp "$console" "$win/Pb.console.exe"
cp "$root/tools/package/server/Server.bat" "$root/tools/package/server/update-server.ps1" "$win/"

for dir in "$linux" "$win"; do
  cp "$root/game/data/server.jsonc" "$dir/"
  printf '%s\n' "$version" > "$dir/version.txt"
done

# The guide as it was for this build (a commit's), else as it is now.
guide="https://github.com/Tim-D-W101/Pb/blob/$([[ "$version" =~ ^[0-9a-f]{7,40}$ ]] && echo "$version" || echo main)/docs/hosting.md"
cat > "$linux/HOW-TO-HOST.txt" <<EOF
Pb dedicated server for Linux ($version)

A game that runs on its own, with nobody of its own playing: people join it from Play with others → Join a game,
by this machine's address.

1. Set it up in server.jsonc: its name, a password if you want one, and the rounds it plays in turn.
2. ./run-server.sh starts it in this terminal (Ctrl+C stops it). It first brings itself up to date with the latest
   test build, as players' Play.bat does: a server only lets in copies of its own version.
3. Open UDP port 47820 to it: in the firewall (sudo ufw allow 47820/udp) and in your provider's settings.
4. To start it with the machine: pb-server.service (the steps are at the top of that file).

The step by step guide: $guide
EOF
printf '%s\r\n' "Pb dedicated server for Windows ($version)" "" \
  "A game that runs on its own, with nobody of its own playing: people join it from Play with others -> Join a game," \
  "by this computer's address." "" \
  "1. Set it up in server.jsonc (open it with Notepad): its name, a password if you want one, and the rounds it" \
  "   plays in turn." \
  "2. Double-click Server.bat. It first brings the server up to date with the latest test build, as players' Play.bat" \
  "   does (a server only lets in copies of its own version), then runs it in that window. Close the window to stop it." \
  "   If Windows says \"Windows protected your PC\", click \"More info\", then \"Run anyway\" (the game isn't signed)." \
  "3. When Windows asks, allow Pb through the firewall. For people outside your home, forward UDP port 47820 on your" \
  "   router to this computer." "" \
  "The step by step guide: $guide" > "$win/HOW-TO-HOST.txt"

# The Linux server starts and serves: it quits a moment after it says so, or fails here.
check="$(mktemp)"
if ! (cd "$linux" && timeout 120 ./Pb.x86_64 --headless --quit-after 240 -- --server) > "$check" 2>&1 \
  || ! grep -q "serving \"" "$check" || grep -qE "^(SCRIPT )?ERROR" "$check"; then
  cat "$check"
  echo "::error::The exported Linux server didn't start (see its log above)"
  exit 1
fi
grep "serving \"" "$check"

(cd "$out/linux" && tar -czf ../release/Pb-server-linux.tar.gz --owner=0 --group=0 --numeric-owner Pb-server)
(cd "$out/windows" && zip -qr -9 -X ../release/Pb-server-windows.zip Pb-server)
echo "Dedicated servers $version: Linux $(du -h "$out/release/Pb-server-linux.tar.gz" | cut -f1), Windows $(du -h "$out/release/Pb-server-windows.zip" | cut -f1)"
