# Wave 3: Movement & Physics Frontend

You are acting as the Builder (Sweeney). Your job is to wire the pure C# Movement backend into Godot's physics system.

## Context
Claude has already built the backend in `src/Project.Common/Movement/`. It contains `PlayerMovementState`, `CollisionMap`, and `MovementProcessor`.

## Your Task
Build the Godot frontend in the `game/` directory:

1. Create a `PlayerCharacter.cs` (inheriting from `CharacterBody2D`).
2. In `_PhysicsProcess`, collect player input (WASD) and construct a `MoveIntent`.
3. Pass the intent to `MovementProcessor.TryMove()` from `Project.Common`.
4. If `TryMove` returns true, update the `CharacterBody2D.GlobalPosition`.
5. Implement an `AnimationController` that reads `PlayerMovementState.CurrentType` (Running/Walking) and plays the appropriate Godot `AnimationPlayer` or `AnimatedSprite2D` animations.
6. Create a Godot `TileMap` bridge that populates the `CollisionMap` backend with solid walls based on the Godot physics layers.

## Constraints
- You MUST use the math and collision resolution from `Project.Common.Movement`.
- Do NOT use Godot's built-in `MoveAndSlide()` for the player character; use `MovementProcessor.TryMove()` to determine the final position, then set `GlobalPosition` directly (or use `MoveAndCollide` just for the visual sweep if necessary, but trust the backend math).
- Focus entirely on input collection, visual position updating, and animations.
- Commit your changes to the repository.
