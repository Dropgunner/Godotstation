# Station Server Architecture

**Goal:** one authoritative station round per process, capped at 50–100 players, with many stations behind a shared lobby (see the discussion in the roadmap). This document tracks the split between rules and the LAN adapter.

## Boundary

| Layer | Owns | Godot types? |
| --- | --- | --- |
| `src/Godotstation.Engine` | Rules, shared world, round lifecycle, movement and collision (`Simulation/StationSimulation`) | No |
| `game/Playtest/StationPlaytestController` | Transport (RPCs), avatars, HUD, chat log, smoke drivers, round-end dialog | Yes |

## Done

- **`StationSimulation`** (`src/Godotstation.Engine/Simulation/StationSimulation.cs`): per-player movement state and collision, the per-frame `Tick` (power, health, ghosts, immobilising pulls, lying-down density, movement, pull-follow, construction, pushed fixtures, pull validation), `MoveEntity` for shoves and pulls, `CanCrewEnterTile`, and entity obstacle sync. Covered by `StationSimulationTests`, which run headless.
- The controller calls `simulation.Tick` and reads positions back. It no longer owns movement state or collision.

## Still in the controller (next slices, in order)

1. **Round lifecycle and roster.** `StartShift`, `EndShift`, `RestartShift`, crew role requests and the round-end report belong in an engine host class that takes requests and returns decisions. The controller only sends and displays them.
2. **Interaction requests.** `ResolveSharedInteraction`, hand toggles, drops, pulls, shoves and examine currently decide which handler runs in the controller. The engine should accept an actor id and a target id and return the outcome; the controller only turns outcomes into messages.
3. **Speech routing.** `RouteLocalSpeech` and `RouteVisibleMessage` decide who hears what, which is a rule. Move it into the engine with the listener positions as input.
4. **Snapshot publishing.** Map each `EntitiesChanged` batch and position update to RPC payloads in a single adapter. This is the piece a headless server replaces.
5. **Headless entry point.** A Godot project (or `--headless` mode) that runs the host class with no UI, takes a port, a player cap and a round id on the command line, and exits when the round ends.

## Behaviour notes for this slice

- Rounding matches Godot's `Mathf.RoundToInt` (midpoints to even), so tiles are unchanged.
- Pushed-fixture sync now reads the authoritative world state rather than the client's replicated copy. On the host the two agree after each publish; the difference is timing within one frame.
- The LAN smoke checks (`tools/multiplayer_smoke.sh`) are the end-to-end evidence for this slice. They must pass in CI before the slice is considered done.
