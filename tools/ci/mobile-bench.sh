#!/usr/bin/env bash
# The mobile benchmark (Phase 6): Thornwick Woods with the mobile graphics preset, measured by --mobile-bench
# (game/core/MobileBench.cs) against presentation.jsonc's graphics.mobileBudget, in two scenarios:
#   drive  the vehicle demo: you get into a buggy, drive it along the tracks (the handbrake, a burst from the wheel) and
#          get out
#   round  a 4 v 4 team round, bots fighting each other and taking the buggies' wheels (a fixed seed on which they do),
#          your slot played by a bot
# Each is run twice: drawn, on Godot's Mobile renderer (on Mesa's software Vulkan, lavapipe, under a virtual screen where
# there's no GPU), counting each frame's draw calls and triangles, which are what a phone's GPU is asked to draw, the same
# on any GPU (the frame rate here means nothing: the CPU draws every pixel); and headless, timing the game's own work a
# frame at 30 fps (drawn in software, Godot's process time takes in the wait for the frame).
# A run fails on a non-zero exit, a missing "MOBILE BENCH PASS" or the scenario's own "SMOKE PASS", or an engine error.
#   tools/ci/mobile-bench.sh path/to/godot [width] [height]
# Needs xvfb-run and a Vulkan driver (apt-get install xvfb mesa-vulkan-drivers).
set -uo pipefail
godot="${1:?path to the Godot .NET binary}"
width="${2:-1280}"
height="${3:-720}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
failed=0

check() {
  local name="$1" status="$2" log="$3"
  if grep -qE "^(SCRIPT )?ERROR|Unhandled exception" "$log"; then
    echo "::error::Engine or script errors during the $name mobile bench (see log above)"
    failed=1
  elif [ "$status" -ne 0 ] || ! grep -q "MOBILE BENCH PASS" "$log" || ! grep -q "SMOKE PASS" "$log"; then
    echo "::error::The $name mobile bench failed (exit code $status): $(grep -E "MOBILE BENCH (FAIL|[0-9])" "$log" | head -2)"
    failed=1
  fi
}

# Drawn: a fifth of a second a frame (--fast lets the sim run its ticks for it), few frames to draw in software, each a
# whole frame of the scene as a phone would draw it.
drawn() {
  local name="$1"
  shift
  local log
  log="$(mktemp)"
  echo "=== mobile bench: $name, drawn ==="
  xvfb-run -a -s "-screen 0 ${width}x${height}x24" "$godot" --path "$root/game" --rendering-driver vulkan --rendering-method mobile \
    --audio-driver Dummy --resolution "${width}x${height}" --fixed-fps 5 res://scenes/Level.tscn -- --level=thornwick_woods --mobile-bench "$@" \
    2>&1 | tee "$log"
  check "$name (drawn)" "${PIPESTATUS[0]}" "$log"
}

# Headless: 30 frames a second, four of the sim's ticks a frame, as a phone at 30 fps runs them.
timed() {
  local name="$1"
  shift
  local log
  log="$(mktemp)"
  echo "=== mobile bench: $name, timed headless ==="
  "$godot" --headless --path "$root/game" --fixed-fps 30 res://scenes/Level.tscn -- --level=thornwick_woods --mobile-bench "$@" 2>&1 | tee "$log"
  check "$name (timed)" "${PIPESTATUS[0]}" "$log"
}

drive=(--vehicle-demo --fast)
round=(--bot-match --fast --time-limit=90 --mode=teams --size=4 --seed=7 --min-drives=1)
drawn drive "${drive[@]}"
timed drive "${drive[@]}"
drawn round "${round[@]}"
timed round "${round[@]}"
exit "$failed"
