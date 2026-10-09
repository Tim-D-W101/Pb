#!/usr/bin/env bash
# The Windows build joins the Linux server under Wine (architecture §16.14). The Windows .NET runtime has faulted where
# Linux's didn't (§14.7), so the exported Windows build plays a whole round, with a bot at its controls, on the packaged
# Linux dedicated server: both must end it without an error and with the same result.
#   tools/ci/wine-join.sh path/to/linux-server-folder path/to/windows-build-folder
# (builds/server/linux/Pb-server and builds/windows/Pb, from tools/package/server-build.sh and windows-build.sh.)
set -uo pipefail
server_dir="$(cd "${1:?the Linux server folder}" && pwd)"
windows_dir="$(cd "${2:?the Windows build folder}" && pwd)"
wine="$(command -v wine || command -v wine64 || echo /usr/lib/wine/wine64)"
logs="${WINE_JOIN_LOGS:-$(mktemp -d)}"
mkdir -p "$logs"
# Wine's DirectInput crashes Godot (CLAUDE.md); its own chatter is no use here.
export WINEDLLOVERRIDES="dinput8=d" WINEDEBUG=-all WINEPREFIX="${WINEPREFIX:-$logs/prefix}"
"$wine" wineboot --init > "$logs/wineboot.log" 2>&1 || true
failed=0
fail() { echo "::error::$*"; failed=1; }

(cd "$server_dir" && exec timeout 900 ./Pb.x86_64 --headless -- --server --host-wait=1 --rounds=1 --time-limit=60) > "$logs/server.log" 2>&1 &
server=$!
for _ in $(seq 120); do
  grep -q "serving" "$logs/server.log" 2>/dev/null && break
  sleep 0.5
done
grep -q "serving" "$logs/server.log" || fail "the Linux server didn't start"

(cd "$windows_dir" && exec timeout 900 "$wine" Pb.exe --headless -- --join=127.0.0.1 --name=Wine --bot-match --rounds=1 --no-art) \
  > "$logs/wine.log" 2>&1
code=$?
[ "$code" -eq 0 ] || fail "the Windows build exited with $code under Wine (124: it ran out of time)"
wait "$server"
code=$?
[ "$code" -eq 0 ] || fail "the Linux server exited with $code"

grep -E "^[0-9]{4}-" "$logs/server.log"
grep -E "SMOKE|NET joined" "$logs/wine.log" | cut -c1-300
for name in server wine; do
  if grep -qE "^(SCRIPT )?ERROR" "$logs/$name.log"; then
    grep -E -A3 "^(SCRIPT )?ERROR" "$logs/$name.log" | head -20
    fail "the $name logged errors"
  fi
done

expected="$(grep -m1 "^NET RESULT round 1:" "$logs/server.log")"
got="$(grep -m1 "^NET RESULT round 1:" "$logs/wine.log" | tr -d '\r')"
[ -n "$expected" ] || fail "the server has no result"
[ "$got" = "$expected" ] || fail "the Windows build's result differs from the server's: ${got:-nothing}"
if [ "$failed" -ne 0 ]; then
  echo "Logs: $logs"
  exit 1
fi
echo "WINE JOIN PASS: the Windows build played a round on the Linux server under Wine, with the server's result: $expected"
