# Godotstation

A Godot 4 port of Unitystation, utilizing the **Dual-Agent Methodology** (Claude Fable + OpenAI Codex).

## Architecture

This project strictly follows the Separation Principle:
- `src/Project.Common/`: Pure C# engine-agnostic logic. (Claude's Domain)
- `game/`: Godot 4 Client, UI, and rendering. (Codex's Domain)

## Community maps

Rounds run on JSON station maps. The in-game **Map Editor** (lobby button or `--map-editor`)
paints walls, spawns, objects and rooms, validates the result, and can host a LAN shift on it;
the host streams the map to every joining client. Format and controls: `game/maps/README.md`.

## Porting Workflow

1. Extract a system from Unitystation (e.g., Atmospherics).
2. Feed it to **Claude** with `CLAUDE.md` to get pure C# logic in `Project.Common`.
3. Feed the result + original UI to **Codex** with `CODEX.md` to get the Godot implementation in `game/`.

See `planning/Operational_Playbook.md` for full details.
