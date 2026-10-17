#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
godot_bin="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
dotnet_bin="${DOTNET_BIN:-dotnet}"
output_path="${1:-/private/tmp/Godotstation-Wave9-Playtest.app}"
solution_path="$repo_root/game/Godotstation.Game.sln"

if [[ -e "$solution_path" ]]; then
  echo "Refusing to overwrite existing solution: $solution_path" >&2
  exit 2
fi
if [[ ! -x "$godot_bin" ]]; then
  echo "Godot executable not found: $godot_bin" >&2
  exit 2
fi

cleanup() { rm -f "$solution_path"; }
trap cleanup EXIT

cd "$repo_root/game"
"$dotnet_bin" new sln -n Godotstation.Game --format sln --output . >/dev/null
"$dotnet_bin" sln Godotstation.Game.sln add Godotstation.Game.csproj >/dev/null
"$godot_bin" --headless --path "$repo_root/game" --editor --quit
"$godot_bin" --headless --path "$repo_root/game" --export-debug "macOS Debug" "$output_path"
echo "MACOS_PLAYTEST_EXPORTED: $output_path"
