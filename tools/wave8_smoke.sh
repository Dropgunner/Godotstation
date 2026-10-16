#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
project_file="$repo_root/game/project.godot"
backup_file="$(mktemp)"
cp "$project_file" "$backup_file"

restore_project() {
	cp "$backup_file" "$project_file"
	/bin/rm -f "$backup_file"
}
trap restore_project EXIT HUP INT TERM

if ! grep -q '^Round=' "$project_file"; then
	echo "Round autoload was not found in game/project.godot" >&2
	exit 1
fi

awk '{ print; if ($0 ~ /^Round=/) print "SmokeRunner=\"*res://World/Wave8Smoke.gd\"" }' \
	"$backup_file" > "$project_file"

godot_bin="${GODOT_BIN:-godot}"
"$godot_bin" --headless --path "$repo_root/game" --quit-after 900
