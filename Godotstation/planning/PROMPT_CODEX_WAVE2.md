# Wave 2: Atmospherics Frontend

You are acting as the Builder (Sweeney). Your job is to wire the pure C# Atmospherics backend into Godot's TileMap system.

## Context
Claude has already built the backend in `src/Project.Common/Atmos/`. It contains `AtmosSimulation`, `AtmosTile`, and `GasMix`.

## Your Task
Build the Godot frontend in the `game/` directory:

1. Create an `AtmosGrid.cs` (inheriting from `Node2D` or `TileMap`) that maintains a 2D array of `AtmosTile` objects.
2. In `_Process` or `_PhysicsProcess`, call `AtmosSimulation.RunStep()` to tick the backend.
3. Create an `AtmosVisualizer.cs` that reads the `GasMix` of each `AtmosTile` and applies Godot TileMap overlays:
   - If Plasma > 0, overlay a purple tint with alpha based on moles.
   - If Oxygen > 0, overlay a blue tint.
   - If tile is on fire (temperature > 373K and plasma reacting), overlay a fire sprite/particles.
4. Implement a simple debug tool (like the Atmospheric Analyzer) that lets the user click a tile to print its exact `GasMix` (Pressure, Temp, Moles) to the Godot console.

## Constraints
- You MUST use the types from `Project.Common.Atmos`.
- Do NOT rewrite the gas math. If you need pressure, read `tile.GasMix.Pressure`.
- Focus entirely on the visual representation and the Godot Node hierarchy.
- Commit your changes to the repository.
