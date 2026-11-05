#!/usr/bin/env bash
# Drives the Godot map editor through a scripted authoring session without a display:
# new blank map, wall stroke, floor, spawn, entities, room, erase, undo/redo, save to
# user://maps, reload, validate. Prints MAP_EDITOR_SMOKE_OK on success.
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
godot_bin="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
playtest_app="${PLAYTEST_APP:-}"
log="$(mktemp "${TMPDIR:-/tmp}/godotstation-map-editor-smoke.XXXXXX")"

if [[ -n "$playtest_app" ]]; then
  godot_bin="$playtest_app/Contents/MacOS/Godotstation"
fi
if [[ ! -x "$godot_bin" ]]; then
  echo "Godot executable not found: $godot_bin" >&2
  exit 2
fi

if [[ -n "$playtest_app" ]]; then
  "$godot_bin" --headless --quit-after 600 -- --map-editor-smoke >"$log" 2>&1 || true
else
  "$godot_bin" --headless --path "$repo_root/game" --quit-after 600 -- --map-editor-smoke >"$log" 2>&1 || true
fi

if grep -q "ERROR:\|SCRIPT ERROR\|MAP_EDITOR_SMOKE_FAILED" "$log" || ! grep -q "MAP_EDITOR_SMOKE_OK" "$log"; then
  cat "$log"
  echo "Map editor smoke failed. Log: $log" >&2
  exit 1
fi
grep "MAP_EDITOR_SMOKE_OK\|MAP_EDITOR_SAVED" "$log"
echo "MAP_EDITOR_SMOKE_OK: the editor authored, saved, reloaded and validated a map headlessly"
