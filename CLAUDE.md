# CLAUDE.md

The rules for this repository are in [`AGENTS.md`](AGENTS.md). Read it first and follow it for every change, whichever agent you are.

Quick orientation:
- Game rules and shared state: `src/Godotstation.Common/` and `src/Godotstation.Engine/`. No Godot types here.
- Godot client, UI and rendering: `game/`. The only project that may use Godot.
- Tests: `tests/Godotstation.Tests/`. Run `bash tools/preflight.sh` before you push.
- Parity status and the playtest gate: `planning/UNITYSTATION_PARITY_ROADMAP.md`.
