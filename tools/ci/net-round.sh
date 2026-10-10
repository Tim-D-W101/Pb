#!/usr/bin/env bash
# CI's networked rounds (architecture §16.14): a headless dedicated server and five headless players on this machine, over
# real UDP, playing a round on every area in every mode (tools/ci/net-round.jsonc; on the Sports Ground a speedball
# match, point after point, which counts once, and in the Cold Store a capture the flag match), with bots filling to ten. Each player has a bot at its controls, sending
# its commands through the network as a person's would:
#   Ada, Bo and Cy  play fairly at 100 ms round trip, with jitter and 1% loss, each in a brand's kit (--kit=BRAND);
#   Trigger         flips its trigger on every tick (--net-cheat=fire): the server must hold it to the fire rate;
#   Clock           runs twice as fast (--net-cheat=fast), sending twice the commands: the server must drop them, and log it.
# It fails on any error, on any copy whose result (of every round and point) differs from the server's, on more than 25 KB/s sent to a player while
# a round is live (architecture §16.5), on a shot rate over the cap, if the extra commands aren't logged, or if a copy's
# roster doesn't have Ada, Bo and Cy in their kits.
#   tools/ci/net-round.sh path/to/godot [time-limit-s]
set -uo pipefail
godot="${1:?path to the Godot .NET binary}"
limit="${2:-75}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
config="$root/tools/ci/net-round.jsonc"
logs="${NET_ROUND_LOGS:-$(mktemp -d)}"
mkdir -p "$logs"
rounds=$(grep -c '"level"' "$config")
budget_kBps=25
most_s=$((rounds * 300))
failed=0
fail() { echo "::error::$*"; failed=1; }

run() {
  local name="$1"; shift
  timeout "$most_s" "$godot" --headless --path "$root/game" "$@" > "$logs/$name.log" 2>&1 &
  pids+=("$!")
  names+=("$name")
}

pids=(); names=()
run server -- --server --server-config="$config" --host-wait=5 --rounds="$rounds" --time-limit="$limit"
for _ in $(seq 120); do
  grep -q "serving" "$logs/server.log" 2>/dev/null && break
  sleep 0.5
done
grep -q "serving" "$logs/server.log" || fail "the server didn't start"
lag=(--net-lag=100 --net-jitter=10 --net-loss=1)
declare -A kits=([Ada]=vellis [Bo]=quarrow [Cy]=kilnmark)
for who in Ada Bo Cy Trigger Clock; do
  cheat=()
  [ "$who" = Trigger ] && cheat=(--net-cheat=fire)
  [ "$who" = Clock ] && cheat=(--net-cheat=fast)
  [ -n "${kits[$who]:-}" ] && cheat+=(--kit="${kits[$who]}")
  run "$who" -- --join=127.0.0.1 --name="$who" --bot-match --rounds="$rounds" --no-art "${lag[@]}" "${cheat[@]}"
done

for i in "${!pids[@]}"; do
  wait "${pids[$i]}"
  code=$?
  [ "$code" -eq 0 ] || fail "${names[$i]} exited with $code (124: it ran out of time)"
done

echo "### The server's log"
grep -E "^[0-9]{4}-" "$logs/server.log"
echo

# No errors anywhere.
for name in "${names[@]}"; do
  if grep -qE "^(SCRIPT )?ERROR" "$logs/$name.log"; then
    grep -E -A3 "^(SCRIPT )?ERROR" "$logs/$name.log" | head -20
    fail "$name logged errors"
  fi
done

# Every copy ends every round (each point of a match one of them) with the server's result, to the shot.
played=$(grep -c "^NET RESULT round" "$logs/server.log")
[ "$played" -gt "$rounds" ] || fail "the server played $played rounds: a speedball match should have had at least two points"
for r in $(seq "$played"); do
  expected="$(grep -m1 "^NET RESULT round $r:" "$logs/server.log")"
  [ -n "$expected" ] || { fail "the server has no result for round $r"; continue; }
  echo "$expected"
  for name in "${names[@]:1}"; do
    got="$(grep -m1 "^NET RESULT round $r:" "$logs/$name.log")"
    [ "$got" = "$expected" ] || fail "$name's round $r differs from the server's: ${got:-nothing}"
  done
done

# Each player's traffic while the rounds were live, and the trigger flipper's shots against the cap.
trigger_shots=0
trigger_capped=0
while read -r line; do
  name="$(sed -E 's/^.{19}   ([^:]+): .*/\1/' <<<"$line")"
  rate="$(sed -E 's/.* ([0-9.]+) KB\/s live.*/\1/' <<<"$line")"
  awk -v r="$rate" -v b="$budget_kBps" 'BEGIN { exit !(r <= b) }' || fail "$name was sent $rate KB/s, over the $budget_kBps KB/s budget"
  if [[ "$line" =~ ([0-9]+)\ shots\ in\ ([0-9.]+)\ s\ \(.*cap\ ([0-9.]+)/s\) ]]; then
    shots="${BASH_REMATCH[1]}"; in_s="${BASH_REMATCH[2]}"; cap="${BASH_REMATCH[3]}"
    awk -v n="$shots" -v t="$in_s" -v c="$cap" 'BEGIN { exit !(n <= c * t + 1) }' || fail "$name fired $shots shots in $in_s s, over the cap of $cap a second"
    if [ "$name" = Trigger ]; then
      trigger_shots=$((trigger_shots + shots))
      # It flips the trigger 60 times a second, so it should fire right up against the cap (but for refills).
      awk -v n="$shots" -v t="$in_s" -v c="$cap" 'BEGIN { exit !(t >= 10 && n >= c * t * 0.5) }' && trigger_capped=1
    fi
  fi
done < <(grep -E "^.{19}   [^:]+: round trip" "$logs/server.log")
[ "$trigger_capped" -eq 1 ] || fail "Trigger never fired at even half the cap ($trigger_shots shots in all): the cheat didn't get going"
grep -qE "Clock sent commands (faster than the clock|too far ahead)" "$logs/server.log" || fail "the server didn't log Clock's extra commands"

# Everyone's kit went to the server and on to every copy: each roster has Ada, Bo and Cy in their brand's marker.
for name in "${names[@]:1}"; do
  roster="$(grep -m1 "^NET round 1 " "$logs/$name.log")"
  for who in Ada Bo Cy; do
    grep -q "$who on [0-9]* in ${kits[$who]}_" <<<"$roster" || fail "$name's roster doesn't have $who in ${kits[$who]}'s kit: ${roster:-no roster}"
  done
done

if [ "$failed" -ne 0 ]; then
  for name in "${names[@]:1}"; do
    echo "### The end of $name's log"
    tail -n 12 "$logs/$name.log"
  done
  echo "Logs: $logs"
  exit 1
fi
echo "NET ROUNDS PASS: $rounds rounds ($played with each point of the matches), every copy with the server's result; traffic within $budget_kBps KB/s; Trigger held to the cap ($trigger_shots shots); Clock's extra commands dropped and logged; everyone's kit on every copy"
