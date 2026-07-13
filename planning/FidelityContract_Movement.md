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
All collision checks MUST resolve to integer tile coordinates. Collision resolves against the
**tile adjacent to the current tile in the movement direction** (`curTile + sign(delta)`), NOT the
rounded destination. Unitystation movement is tile-stepping; checking only the rounded destination
allows a fast body to *tunnel* through a wall it should have hit. A move is blocked the instant the
next tile in its path is impassable.

## Rule 5: Momentum & Ice Sliding (Wave 3)
Movement carries **Newtonian momentum** (`PlayerMovementState.MomentumX/Y`, tiles/second), ported
from `UniversalObjectPhysics.newtonianMovement`.
- A body moving over a **slippery** tile (ice/space) retains its velocity as momentum, so it keeps
  gliding after input stops.
- `MovementProcessor.ApplyMomentum` advances the body by its momentum each physics tick and then
  applies **friction decay** (`AppliedFriction`): `magnitude -= friction * dt`, clamped to zero.
- Friction coefficient depends on the tile underfoot: `SLIDE_FRICTION = 9` on slippery ground
  (slides several tiles) versus `DEFAULT_FRICTION = 15` on grippy floor (momentum dies within ~1
  tile). A body therefore slides measurably farther on ice than on a normal floor.
- Momentum sliding is tile-traced: a sliding body stops (and loses momentum on the blocked axis)
  when it reaches a wall, and never tunnels through one.

## Rule 6: Object Pushing (Wave 3)
Movable obstacles are modelled as `DynamicObject` on the `CollisionMap`, ported from the pushable
half of `UniversalObjectPhysics.PushPull.ForceTilePush`.
- Moving (or sliding) into a **pushable** object shoves it exactly one tile in the movement
  direction, then the mover occupies the vacated tile.
- Pushing is a **chain**: a pushable object shoved into another pushable object pushes it too. The
  push is **atomic** — nothing moves unless the entire chain has room to move one tile.
- A non-pushable object (`IsPushable == false`: bolted machinery, secured lockers) behaves exactly
  like a wall and cannot be shoved. A wall (or the map edge of solid tiles) at the end of a chain
  blocks the whole push.

## Implementation Boundary
- `Project.Common` MUST implement `PlayerMovementState`, `CollisionMap`, and `MovementProcessor`. It calculates the `TryMove` result.
- `game/` (Godot) MUST implement `CharacterBody2D`, handle Godot's `_PhysicsProcess`, collect input, pass it to `MovementProcessor.TryMove`, and apply the resulting coordinates to the Godot node. Godot handles the visual interpolation and animation states.
