#!/usr/bin/env bash
# Builds and runs the client's capture mode (src/dev_capture.rs) in an isolated data root.
# Usage: tools/capture.sh OUT_DIR "SCRIPT" [WORLD]
set -e
out="$1"; script="$2"; world="${3:-capture}"
mkdir -p "$out"
root="$(dirname "$out")/capture-data"
mkdir -p "$root"
cd "$(dirname "$0")/.."
FF_CAPTURE_DIR="$(cygpath -w "$out")" FF_DATA_ROOT="$(cygpath -w "$root")" FF_CAPTURE_SCRIPT="$script" FF_CAPTURE_WORLD="$world" \
  cargo run --quiet --bin food_factory_bevy 2>&1 | grep -E "ERROR|WARN|panicked|Capture|Map" | grep -v "closed channel" || true
ls "$out"
