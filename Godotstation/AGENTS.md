# AGENTS.md — Godotstation Port

## Project Overview
Porting Unitystation (Unity C#) to Godot 4 (.NET/C#) using the Dual-Agent Methodology.

## Agent Roles
- **Claude Fable (Carmack)**: Writes `src/Project.Common/` — pure C#, fidelity contracts, tests.
- **OpenAI Codex (Sweeney)**: Writes `game/` — Godot UI, scenes, signals, input handling.
- **Manus**: Orchestrates, creates prompts, manages repo, runs CI.

## Architecture Rules
1. `src/Project.Common/` must NEVER reference Godot assemblies.
2. `game/` must NEVER contain simulation logic — only View/Controller.
3. All feel-critical behaviors must have a Fidelity Contract in `planning/`.
4. Tests in `tests/` run without Godot (pure .NET).

## Wave Status
- Wave 1: Interaction System — IN PROGRESS
