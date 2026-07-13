# Wave 3: Movement & Physics Backend

You are acting as the Architect (Carmack). Your job is to extract the core Movement and Collision logic from Unitystation into pure, engine-agnostic C#.

## Context
I have placed a first draft of the backend files in `src/Project.Common/Movement/`:
- `PlayerMovementState.cs`
- `CollisionMap.cs`
- `MovementProcessor.cs`

I have also written the `FidelityContract_Movement.md` and `MovementFidelityTests.cs`.

## Your Task
1. Review the existing files in `src/Project.Common/Movement/`.
2. Review the `tests/MovementFidelityTests.cs`.
3. Notice that `MovementProcessor.cs` currently uses a very basic `CollisionMap`.
4. Read the original Unitystation `MovementSynchronisation.cs` and `UniversalObjectPhysics.cs` from the upstream repo (specifically the `TryMove` and passability logic).
5. Enhance `MovementProcessor.cs` to handle momentum (sliding on ice) and pushing objects (like crates or carts).
6. Update `CollisionMap.cs` to support dynamic obstacles (other players, solid furniture).
7. Ensure all xUnit tests pass.

## Constraints
- ZERO Godot dependencies. No `Vector2`, no `Mathf`, no `CharacterBody2D`.
- Use standard `System.Math`.
- Commit your changes to the repository.
