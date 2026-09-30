#!/usr/bin/env bash
# Imports the Godot project headlessly (creates game/.godot caches) and fails on errors.
#   tools/ci/import.sh path/to/godot
set -uo pipefail
godot="${1:?path to the Godot .NET binary}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
log="$(mktemp)"
"$godot" --headless --path "$root/game" --import 2>&1 | tee "$log"
if grep -qE "^(SCRIPT )?ERROR" "$log"; then
  echo "::error::Errors while importing the Godot project (see log above)"
  exit 1
fi
