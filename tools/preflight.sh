#!/usr/bin/env bash
# Godotstation preflight — every deterministic check Codex would otherwise
# spend tokens reasoning about. Prints ONE line of JSON. Exit 0 = all green.
#
#   bash tools/preflight.sh            # full: lint + build + test
#   bash tools/preflight.sh --fast     # lint only (no dotnet)
set -uo pipefail
cd "$(dirname "$0")/.."

FAST=0; [[ "${1:-}" == "--fast" ]] && FAST=1
fail=0
json_list() { local IFS=,; printf '[%s]' "$*"; }
q() { printf '"%s"' "${1//\"/\\\"}"; }

# 1. Boundary: no Godot outside game/
godot_leaks=()
while IFS= read -r f; do godot_leaks+=("$(q "$f")"); done < <(
  grep -rlE '^\s*using Godot\s*;' --include='*.cs' src tests 2>/dev/null)
(( ${#godot_leaks[@]} )) && fail=1

# 2. Boundary: no simulation logic hint in game/ (heuristic: game/ must not
#    define handlers' resolution order or touch backend internals)
backend_edits_in_game=()
while IFS= read -r f; do backend_edits_in_game+=("$(q "$f")"); done < <(
  grep -rlE 'namespace Godotstation\.(Common|Engine)' --include='*.cs' game 2>/dev/null)
(( ${#backend_edits_in_game[@]} )) && fail=1

# 3. Legacy trees that must not exist after Phase 0
legacy=()
for p in Godotstation src/Project.Common src/Project.Engine tests/Tests.csproj \
         tests/InteractionFidelityTests.cs tests/AtmosFidelityTests.cs tests/MovementFidelityTests.cs; do
  [[ -e "$p" ]] && legacy+=("$(q "$p")")
done
(( ${#legacy[@]} )) && fail=1

# 4. Every .csproj on disk must be in the solution
orphans=()
while IFS= read -r p; do
  rel="${p#./}"
  grep -qF "Path=\"$rel\"" Godotstation.slnx || orphans+=("$(q "$rel")")
done < <(find . -name '*.csproj' -not -path './.git/*' -not -path '*/obj/*' -not -path '*/bin/*')
(( ${#orphans[@]} )) && fail=1

# 5. Build + test (skipped with --fast or when dotnet is absent)
build="skipped"; test="skipped"; test_summary=""
if (( ! FAST )) && command -v dotnet >/dev/null; then
  if dotnet build Godotstation.slnx -v q -nologo >/tmp/gs_build.log 2>&1; then build="pass"
  else build="fail"; fail=1; fi
  if [[ $build == pass ]]; then
    if dotnet test tests/Godotstation.Tests -v q -nologo --no-build >/tmp/gs_test.log 2>&1; then test="pass"
    else test="fail"; fail=1; fi
    test_summary=$(grep -E 'Passed!|Failed!|Total tests' /tmp/gs_test.log | tail -1 | tr -d '"')
  fi
fi

printf '{"ok":%s,"godot_leaks":%s,"backend_ns_in_game":%s,"legacy_paths":%s,"orphan_csproj":%s,"build":"%s","test":"%s","test_summary":"%s"%s}\n' \
  "$([[ $fail == 0 ]] && echo true || echo false)" \
  "$(json_list "${godot_leaks[@]+"${godot_leaks[@]}"}")" \
  "$(json_list "${backend_edits_in_game[@]+"${backend_edits_in_game[@]}"}")" \
  "$(json_list "${legacy[@]+"${legacy[@]}"}")" \
  "$(json_list "${orphans[@]+"${orphans[@]}"}")" \
  "$build" "$test" "$test_summary" \
  "$([[ $build == fail ]] && printf ',"build_errors":"see /tmp/gs_build.log (grep error)"')"
exit $fail
