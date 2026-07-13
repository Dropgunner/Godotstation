# Godotstation

A Godot 4 port of Unitystation, utilizing the **Dual-Agent Methodology** (Claude Fable + OpenAI Codex).

## Architecture

This project strictly follows the Separation Principle:
- `src/Project.Common/`: Pure C# engine-agnostic logic. (Claude's Domain)
- `game/`: Godot 4 Client, UI, and rendering. (Codex's Domain)

## Porting Workflow

1. Extract a system from Unitystation (e.g., Atmospherics).
2. Feed it to **Claude** with `CLAUDE.md` to get pure C# logic in `Project.Common`.
3. Feed the result + original UI to **Codex** with `CODEX.md` to get the Godot implementation in `game/`.

See `planning/Operational_Playbook.md` for full details.
