#!/usr/bin/env bash
# Exports a debug playtest build for one desktop platform.
#
#   bash tools/export_playtest.sh linux   out/linux     -> out/linux/Godotstation.x86_64
#   bash tools/export_playtest.sh windows out/windows   -> out/windows/Godotstation.exe
#   bash tools/export_playtest.sh macos   out/macos     -> out/macos/Godotstation.zip (universal .app)
#
# Needs Godot .NET (GODOT_BIN) with matching export templates installed, and the .NET SDK.
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
godot_bin="${GODOT_BIN:-godot}"
platform="${1:-}"
output_dir="${2:-}"
export_presets_path="$repo_root/game/export_presets.cfg"
temporary_export_preset=false

case "$platform" in
  linux)   preset="Linux Playtest";   file="Godotstation.x86_64" ;;
  windows) preset="Windows Playtest"; file="Godotstation.exe" ;;
  macos)   preset="macOS Playtest";   file="Godotstation.zip" ;;
  *) echo "Usage: $0 <linux|windows|macos> <output-dir>" >&2; exit 2 ;;
esac
if [[ -z "$output_dir" ]]; then
  echo "Usage: $0 <linux|windows|macos> <output-dir>" >&2
  exit 2
fi
if ! command -v "$godot_bin" >/dev/null 2>&1 && [[ ! -x "$godot_bin" ]]; then
  echo "Godot executable not found: $godot_bin" >&2
  exit 2
fi

cleanup() {
  if [[ "$temporary_export_preset" == true ]]; then
    rm -f "$export_presets_path"
  fi
}
trap cleanup EXIT

# export_presets.cfg is not tracked. Without a local one, use these unsigned debug presets.
if [[ ! -f "$export_presets_path" ]]; then
  temporary_export_preset=true
  cat >"$export_presets_path" <<'PRESET'
[preset.0]

name="Linux Playtest"
platform="Linux"
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

binary_format/embed_pck=false
binary_format/architecture="x86_64"

[preset.1]

name="Windows Playtest"
platform="Windows Desktop"
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

[preset.1.options]

binary_format/embed_pck=false
binary_format/architecture="x86_64"
codesign/enable=false
application/modify_resources=false

[preset.2]

name="macOS Playtest"
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

[preset.2.options]

binary_format/architecture="universal"
application/bundle_identifier="com.dropgunner.godotstation.playtest"
application/app_category="Games"
application/short_version="0.1.0"
application/version="0.1.0"
display/high_res=true
codesign/codesign=1
notarization/notarization=0
PRESET
fi

if ! grep -Fq "name=\"$preset\"" "$export_presets_path"; then
  echo "Export preset '$preset' not found in $export_presets_path" >&2
  exit 2
fi

mkdir -p "$output_dir"
output_dir="$(cd "$output_dir" && pwd)"
output_path="$output_dir/$file"
if [[ -e "$output_path" ]]; then
  echo "Refusing to overwrite existing playtest build: $output_path" >&2
  exit 2
fi

"$godot_bin" --headless --path "$repo_root/game" --import
"$godot_bin" --headless --path "$repo_root/game" --export-debug "$preset" "$output_path"

if [[ ! -s "$output_path" ]]; then
  echo "Export did not produce $output_path" >&2
  exit 1
fi
if [[ "$platform" != macos ]]; then
  if [[ ! -f "$output_dir/Godotstation.pck" ]] ||
     ! compgen -G "$output_dir/data_Godotstation.Game_*/Godotstation.Game.dll" >/dev/null; then
    echo "Export is missing the game data or the C# assemblies: $output_dir" >&2
    exit 1
  fi
fi

echo "PLAYTEST_EXPORTED platform=$platform path=$output_path"
