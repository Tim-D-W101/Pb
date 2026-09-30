#!/usr/bin/env bash
# Runs the range scene headless with the scripted smoke test (see game/core/SmokeTest.cs) and
# fails on a non-zero exit, a missing "SMOKE PASS" line, or any engine/script error in the log.
#   tools/ci/smoke-test.sh path/to/godot [ticks]
set -uo pipefail
godot="${1:?path to the Godot .NET binary}"
ticks="${2:-900}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
log="$(mktemp)"

"$godot" --headless --path "$root/game" --fixed-fps 120 -- "--smoke-test=$ticks" 2>&1 | tee "$log"
status=${PIPESTATUS[0]}

if grep -qE "^(SCRIPT )?ERROR|Unhandled exception" "$log"; then
  echo "::error::Engine or script errors during the smoke test (see log above)"
  exit 1
fi

if [ "$status" -ne 0 ] || ! grep -q "SMOKE PASS" "$log"; then
  echo "::error::Smoke test failed (exit code $status)"
  exit 1
fi
