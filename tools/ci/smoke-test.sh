#!/usr/bin/env bash
# Runs each scene headless with its scripted smoke test and fails on a non-zero exit, a missing
# "SMOKE PASS" line, or any engine/script error in the log:
#   menu   (game/ui/MainMenu.cs): builds every screen and checks every area offers all its places, all open
#   range  (game/core/SmokeTest.cs): autopilot, 1,000-ball stress mode and a hot reload
#   level  (game/core/LevelSmokeTest.cs): walk in through the compound gate, sweeping and firing, up every
#          flight of stairs and ramp, through a door and a duel; then the same on the Rail Yard, the Cold Store,
#          the Hospital Wing and the Sports Ground (a solo round there: up the field between the bunkers, and the duel)
#   match  (--bot-match): a bot plays your slot against Normal bots until the round ends, with random
#          starts (--random-spawns) and the art ignored (--no-art), so the procedural and greybox
#          fallbacks keep working, and the round saved to the profile as yours (--record); then a 3 v 3
#          team round, an eight-player free-for-all, a 3 v 3 retrieve and a solo hold against four; then
#          on the Rail Yard a solo round, a 3 v 3 retrieve and an eight-player free-for-all, on the Cold Store a
#          solo round, a 3 v 3 hold and an eight-player free-for-all, and on the Hospital Wing a solo round, a 4 v 4
#          retrieve and a ten-player free-for-all, and on the Sports Ground a speedball match to two points on each
#          layout (5 v 5 on Classic, 4 v 4 on Crossfire: the level reloads between points, and every point must end);
#          capture the flag matches to two points, 4 v 4 on the Sports Ground (one flag on the centre bunker) and 3 v 3
#          in Oxbarrow Works (a flag at each side's base);
#          and in parts of the areas (walled in and taped off): a
#          six-player free-for-all in Oxbarrow Works' warehouse, a solo round in the Rail Yard's engine shed,
#          a 3 v 3 hold inside the Cold Store and a 3 v 3 retrieve in the Hospital Wing's wings
#   roles  (--role-demo): a Marksman on the Rail Yard spots you far down its view and opens up, and a Flanker on the
#          Hospital Wing goes round on a teammate's call and looks out from its spot
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
run level-sports-ground res://scenes/Level.tscn -- "--smoke-test=$level_ticks" --level=sports_ground --mode=solo
run match-sports-ground res://scenes/Level.tscn -- --bot-match --no-art --level=sports_ground --race-to=2
run match-sports-ground-crossfire res://scenes/Level.tscn -- --bot-match --no-art --level=sports_ground --place=crossfire --size=4 --race-to=2
run match-flag-field res://scenes/Level.tscn -- --bot-match --no-art --level=sports_ground --mode=flag --size=4 --race-to=2
run match-flag res://scenes/Level.tscn -- --bot-match --no-art --mode=flag --size=3 --race-to=2
run role-marksman res://scenes/Level.tscn -- --role-demo=marksman --level=rail_yard --no-art
run role-flanker res://scenes/Level.tscn -- --role-demo=flanker --level=hospital_wing --no-art
run match-place res://scenes/Level.tscn -- --bot-match --time-limit=120 --no-art --mode=ffa --size=6 --place=warehouse
run match-place-rail-yard res://scenes/Level.tscn -- --bot-match --time-limit=120 --no-art --random-spawns --level=rail_yard --place=engine_shed
run match-place-cold-store res://scenes/Level.tscn -- --bot-match --time-limit=120 --no-art --mode=teams --size=3 --objective=hold --level=cold_store --place=store
run match-place-hospital-wing res://scenes/Level.tscn -- --bot-match --time-limit=120 --no-art --mode=teams --size=3 --objective=retrieve --level=hospital_wing --place=wings
run art res://tools/ArtImport.tscn -- --selftest
exit "$failed"
