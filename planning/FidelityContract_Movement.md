# Fidelity Contract: Movement & Physics

## Core Architecture
Movement in Unitystation is authoritative server-side with client-side prediction. The `Project.Common` backend defines the pure mathematical rules of movement (speeds, collision checks, state modifiers).

## Rule 1: Movement Speeds
Speeds are derived from limbs. The base modifiers are:
- Walking: 1.5 per leg (Total: 3.0)
- Running: 3.0 per leg (Total: 6.0)
- Crawling: 0.5 per leg (Total: 1.0)
The absolute minimum movement speed (if not completely blocked) is clamped to `0.6f`.

## Rule 2: Movement States
A player's ability to move is completely halted if they are:
- `IsBuckled` (strapped to a chair/bed)
- `IsCuffed` (handcuffed)
- `IsSlipping` (currently in the slip animation/stun)

## Rule 3: Wall Sliding (Fudge Factor)
If a player attempts to move diagonally into a solid wall, the movement processor MUST attempt to "slide" them along the unblocked cardinal axis. For example, moving Up-Right into a wall on the Right should result in the player moving straight Up.

## Rule 4: Matrix & Tile Alignment
All collision checks MUST resolve to integer tile coordinates (`Math.Round` from the current float position plus the movement delta).

## Implementation Boundary
- `Project.Common` MUST implement `PlayerMovementState`, `CollisionMap`, and `MovementProcessor`. It calculates the `TryMove` result.
- `game/` (Godot) MUST implement `CharacterBody2D`, handle Godot's `_PhysicsProcess`, collect input, pass it to `MovementProcessor.TryMove`, and apply the resulting coordinates to the Godot node. Godot handles the visual interpolation and animation states.
