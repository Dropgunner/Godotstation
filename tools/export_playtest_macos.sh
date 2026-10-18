#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
godot_bin="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
dotnet_bin="${DOTNET_BIN:-dotnet}"
output_path="${1:-/private/tmp/Godotstation-Playtest-$(date +%Y%m%d-%H%M%S).app}"
solution_path="$repo_root/game/Godotstation.Game.sln"
export_presets_path="$repo_root/game/export_presets.cfg"
temporary_export_preset=false

if [[ ! -f "$solution_path" ]]; then
  echo "Godot C# solution not found: $solution_path" >&2
  exit 2
fi
if [[ ! -x "$godot_bin" ]]; then
  echo "Godot executable not found: $godot_bin" >&2
  exit 2
fi
if [[ -e "$output_path" ]]; then
  echo "Refusing to overwrite existing playtest app: $output_path" >&2
  exit 2
fi

cleanup() {
  if [[ "$temporary_export_preset" == true ]]; then
    rm -f "$export_presets_path"
  fi
}
trap cleanup EXIT

if [[ ! -f "$export_presets_path" ]]; then
  temporary_export_preset=true
  cat >"$export_presets_path" <<'PRESET'
[preset.0]

name="macOS Debug"
platform="macOS"
runnable=true
advanced_options=false
dedicated_server=false
custom_features=""
export_filter="all_resources"
include_filter=""
exclude_filter=""
export_path=""
encryption_include_filters=""
encryption_exclude_filters=""
encrypt_pck=false
encrypt_directory=false
script_export_mode=2

[preset.0.options]

binary_format/architecture="universal"
custom_template/debug=""
custom_template/release=""
debug/export_console_script=1
application/icon=""
application/icon_interpolation=4
application/bundle_identifier="com.dropgunner.godotstation.playtest"
application/signature=""
application/app_category="Games"
application/short_version="0.1.0"
application/version="0.1.0"
application/copyright=""
application/copyright_localized={}
display/high_res=true
codesign/codesign=0
codesign/identity=""
PRESET
fi

if ! grep -Fq 'name="macOS Debug"' "$export_presets_path"; then
  echo "Export preset 'macOS Debug' not found in $export_presets_path" >&2
  exit 2
fi

"$dotnet_bin" build "$solution_path" --nologo --verbosity minimal
"$godot_bin" --headless --path "$repo_root/game" --editor --quit
"$godot_bin" --headless --path "$repo_root/game" --export-debug "macOS Debug" "$output_path"

app_executable="$output_path/Contents/MacOS/Godotstation"
app_resources="$output_path/Contents/Resources"
if [[ ! -x "$app_executable" || ! -f "$app_resources/Godotstation.pck" || \
      ! -f "$app_resources/data_Godotstation.Game_macos_arm64/Godotstation.Game.dll" || \
      ! -f "$app_resources/data_Godotstation.Game_macos_x86_64/Godotstation.Game.dll" ]]; then
  echo "Exported app is missing the executable, game data, or a universal C# runtime: $output_path" >&2
  exit 1
fi

startup_log="$(mktemp /private/tmp/godotstation-app-startup.XXXXXX)"
if ! "$app_executable" --headless --quit-after 120 >"$startup_log" 2>&1; then
  cat "$startup_log" >&2
  rm -f "$startup_log"
  echo "Exported playtest app failed its headless startup check: $output_path" >&2
  exit 1
fi
if rg -q 'ERROR:' "$startup_log"; then
  cat "$startup_log" >&2
  rm -f "$startup_log"
  echo "Exported playtest app logged runtime errors during startup: $output_path" >&2
  exit 1
fi
rm -f "$startup_log"

echo "MACOS_PLAYTEST_EXPORTED: $output_path"
echo "MACOS_PLAYTEST_STARTUP_OK: universal C# bundle launched and exited cleanly"
