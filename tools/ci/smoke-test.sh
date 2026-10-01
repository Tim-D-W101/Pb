#!/usr/bin/env bash
# Runs each scene headless with its scripted smoke test and fails on a non-zero exit, a missing
# "SMOKE PASS" line, or any engine/script error in the log:
#   menu   (game/ui/MainMenu.cs): builds every screen and checks the ladder's levels and tiers are offered
#   range  (game/core/SmokeTest.cs): autopilot, 1,000-ball stress mode and a hot reload
#   level  (game/core/LevelSmokeTest.cs): walk in through the compound gate, sweeping and firing
#   match  (--bot-match): a bot plays your slot against the Normal tier's bots until the round ends,
#          with random starts (--random-spawns) and the art ignored (--no-art), so the procedural and
#          greybox fallbacks keep working
#   art    (game/tools/ArtImport.cs): the art import's texture steps on a generated picture
#   tools/ci/smoke-test.sh path/to/godot [range-ticks] [level-ticks]
set -uo pipefail
godot="${1:?path to the Godot .NET binary}"
range_ticks="${2:-900}"
level_ticks="${3:-1800}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
failed=0

run() {
  local name="$1"
  shift
  local log
  log="$(mktemp)"
  echo "=== $name smoke test ==="
  "$godot" --headless --path "$root/game" --fixed-fps 120 "$@" 2>&1 | tee "$log"
  local status=${PIPESTATUS[0]}

  if grep -qE "^(SCRIPT )?ERROR|Unhandled exception" "$log"; then
    echo "::error::Engine or script errors during the $name smoke test (see log above)"
    failed=1
  elif [ "$status" -ne 0 ] || ! grep -q "SMOKE PASS" "$log"; then
    echo "::error::The $name smoke test failed (exit code $status)"
    failed=1
  fi
}

run menu res://scenes/Main.tscn -- --smoke-test
run range res://scenes/Range.tscn -- "--smoke-test=$range_ticks"
run level res://scenes/Level.tscn -- "--smoke-test=$level_ticks"
run match res://scenes/Level.tscn -- --bot-match --time-limit=240 --no-art --random-spawns
run art res://tools/ArtImport.tscn -- --selftest
exit "$failed"
