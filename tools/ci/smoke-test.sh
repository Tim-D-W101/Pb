#!/usr/bin/env bash
# Runs each scene headless with its scripted smoke test and fails on a non-zero exit, a missing
# "SMOKE PASS" line, or any engine/script error in the log:
#   menu   (game/ui/MainMenu.cs): builds every screen and checks the ladder's levels and tiers are offered
#          (with every level open: --unlock-all)
#   range  (game/core/SmokeTest.cs): autopilot, 1,000-ball stress mode and a hot reload
#   level  (game/core/LevelSmokeTest.cs): walk in through the compound gate, sweeping and firing, up every
#          flight of stairs and ramp, through a door and a duel; then the same on the Rail Yard, the Cold Store and
#          the Hospital Wing
#   match  (--bot-match): a bot plays your slot against Normal bots until the round ends, with random
#          starts (--random-spawns) and the art ignored (--no-art), so the procedural and greybox
#          fallbacks keep working, and the round saved to the profile as yours (--record); then a 3 v 3
#          team round, an eight-player free-for-all, a 3 v 3 retrieve and a solo hold against four; then
#          on the Rail Yard a solo round, a 3 v 3 retrieve and an eight-player free-for-all, on the Cold Store a
#          solo round, a 3 v 3 hold and an eight-player free-for-all, and on the Hospital Wing a solo round, a 4 v 4
#          retrieve and a ten-player free-for-all
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

run menu res://scenes/Main.tscn -- --smoke-test --unlock-all
run range res://scenes/Range.tscn -- "--smoke-test=$range_ticks"
run level res://scenes/Level.tscn -- "--smoke-test=$level_ticks"
run match res://scenes/Level.tscn -- --bot-match --time-limit=240 --no-art --random-spawns --record
run match-teams res://scenes/Level.tscn -- --bot-match --time-limit=150 --no-art --mode=teams --size=3
run match-ffa res://scenes/Level.tscn -- --bot-match --time-limit=150 --no-art --mode=ffa --size=8
run match-retrieve res://scenes/Level.tscn -- --bot-match --time-limit=150 --no-art --mode=teams --size=3 --objective=retrieve
run match-hold res://scenes/Level.tscn -- --bot-match --time-limit=150 --no-art --objective=hold --size=4
run level-rail-yard res://scenes/Level.tscn -- "--smoke-test=$level_ticks" --level=rail_yard
run match-rail-yard res://scenes/Level.tscn -- --bot-match --time-limit=240 --no-art --random-spawns --level=rail_yard
run match-rail-yard-retrieve res://scenes/Level.tscn -- --bot-match --time-limit=150 --no-art --mode=teams --size=3 --objective=retrieve --level=rail_yard
run match-rail-yard-ffa res://scenes/Level.tscn -- --bot-match --time-limit=150 --no-art --mode=ffa --size=8 --level=rail_yard
run level-cold-store res://scenes/Level.tscn -- "--smoke-test=$level_ticks" --level=cold_store
run match-cold-store res://scenes/Level.tscn -- --bot-match --time-limit=240 --no-art --random-spawns --level=cold_store
run match-cold-store-hold res://scenes/Level.tscn -- --bot-match --time-limit=150 --no-art --mode=teams --size=3 --objective=hold --level=cold_store
run match-cold-store-ffa res://scenes/Level.tscn -- --bot-match --time-limit=150 --no-art --mode=ffa --size=8 --level=cold_store
run level-hospital-wing res://scenes/Level.tscn -- "--smoke-test=$level_ticks" --level=hospital_wing
run match-hospital-wing res://scenes/Level.tscn -- --bot-match --time-limit=240 --no-art --random-spawns --level=hospital_wing
run match-hospital-wing-retrieve res://scenes/Level.tscn -- --bot-match --time-limit=150 --no-art --mode=teams --size=4 --objective=retrieve --level=hospital_wing
run match-hospital-wing-ffa res://scenes/Level.tscn -- --bot-match --time-limit=150 --no-art --mode=ffa --size=10 --level=hospital_wing
run art res://tools/ArtImport.tscn -- --selftest
exit "$failed"
