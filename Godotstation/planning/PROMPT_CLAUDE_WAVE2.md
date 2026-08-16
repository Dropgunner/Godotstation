# Wave 2: Atmospherics Backend

You are acting as the Architect (Carmack). Your job is to extract the core Atmospherics simulation from Unitystation into pure, engine-agnostic C#.

## Context
I have already placed a first draft of the backend files in `src/Project.Common/Atmos/`:
- `AtmosDefines.cs`
- `GasMix.cs`
- `AtmosTile.cs`
- `AtmosSimulation.cs`

I have also written the `FidelityContract_Atmospherics.md` and the `AtmosFidelityTests.cs`.

## Your Task
1. Review the existing files in `src/Project.Common/Atmos/`.
2. Review the `tests/AtmosFidelityTests.cs`.
3. Notice that `AtmosSimulation.cs` currently lacks the implementation for **Chemical Reactions**.
4. Read the original Unitystation `ReactionManager.cs` and `Reaction.cs` from the upstream repo (or rely on your knowledge of SS13 atmos reactions).
5. Implement the `IAtmosReaction` interface and add the reaction processing loop to `AtmosSimulation.UpdateTile`.
6. Implement at least one concrete reaction (e.g., `PlasmaFireReaction`) in `Project.Common` to prove the system works.
7. Ensure all xUnit tests pass.

## Constraints
- ZERO Godot dependencies. No `Vector2`, no `Mathf`, no `Node`.
- Use standard `System.Math`.
- Commit your changes to the repository.
