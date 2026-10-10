#!/usr/bin/env bash
# Starts the Pb dedicated server (docs/hosting.md) after bringing it up to date with the latest test build
# (update-server.sh; --no-update as the first argument skips that). Its settings are in server.jsonc beside this
# script. It logs to the terminal and to ~/.local/share/godot/app_userdata/Pb (working title)/logs/server.log.
# Ctrl+C stops it. Anything else on the line goes to the server, e.g. --server-config=/path/to/other.jsonc.
here="$(cd "$(dirname "$0")" && pwd)"
if [ "${1:-}" = "--no-update" ]; then
  shift
else
  "$here/update-server.sh" || echo "Couldn't update. Starting the version you have."
fi

cd "$here"
exec "$here/Pb.x86_64" --headless -- --server "$@"
