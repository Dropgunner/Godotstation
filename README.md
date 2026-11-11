# Godotstation

A Godot 4 (C#) remake of [Unitystation](https://github.com/unitystation/unitystation), a Space Station 13-style multiplayer game. Players join a shared station round, take crew roles, and work together on station systems.

Read [`AGENTS.md`](AGENTS.md) before making changes. [`CLAUDE.md`](CLAUDE.md) points to the same rules.

## Architecture

- `src/Godotstation.Common/`: engine-independent interaction and world logic. No Godot types.
- `src/Godotstation.Engine/`: engine-independent rules: rounds, crew, movement, health, power, and the station simulation. No Godot types.
- `game/`: Godot 4 client, UI and rendering. The only project allowed to use Godot.
- `tests/Godotstation.Tests/`: xUnit tests for the rules.
- `tools/`: preflight, two-client smoke scripts and export scripts.

## Working on the project

1. Read `AGENTS.md` and the parity status in `planning/UNITYSTATION_PARITY_ROADMAP.md`.
2. Record each fidelity decision in a `planning/FidelityContract_*.md` file.
3. Add tests in `tests/` for any rule change.
4. Run `bash tools/preflight.sh` (lint, build and tests).
5. For network changes, run the two-client smokes with `tools/multiplayer_smoke.sh <mode>`. CI runs all of them.

## Building and testing

```bash
bash tools/preflight.sh            # boundaries, build and tests
bash tools/preflight.sh --fast     # boundaries only
dotnet test tests/Godotstation.Tests
```

To try the game, download a playtest build from the latest green **Playtest build** run in GitHub Actions, then follow [`planning/PLAYTEST_CHECKLIST.md`](planning/PLAYTEST_CHECKLIST.md).

## Background

The project began as a dual-agent experiment (Claude for engine logic and tests, OpenAI Codex for the Godot client). Both agents now follow `AGENTS.md`. The early process is kept in `planning/Operational_Playbook.md` for reference.
