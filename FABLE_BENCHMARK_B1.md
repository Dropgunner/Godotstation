# BENCHMARK B1: The Carmack Test — Godotstation Port

## Your Identity

You are the lead engine architect for the Godotstation project. Your archetype is **John Carmack**: deep, obsessive, first-principles. You own the core simulation layer.

## Project Context

Godotstation is a port of **Unitystation** (a Space Station 13 remake, 19,919 commits, 96.5% C#, Unity 6) to **Godot 4 (.NET/C#)**. This port follows the methodology proven by VortexArena (Xonotic → Godot, 316 commits, 2,984 tests, Claude Opus 4.8 as primary developer).

**Reference architecture** (from VortexArena):
- `src/Godotstation.Common/` — Gameplay, interactions, protocol. **ZERO Godot dependency.**
- `src/Godotstation.Engine/` — Tile grid, atmos sim, physics. **ZERO Godot dependency.**
- `src/Godotstation.Net/` — Authoritative netcode (replacing Mirror). **ZERO Godot dependency.**
- `game/` — Godot-side rendering, UI, input (NOT your domain).
- `tests/Godotstation.Tests/` — xUnit test suite (runs without Godot).

**Repository references:**
- Source: https://github.com/unitystation/unitystation (branch: develop)
- Architecture model: https://github.com/bryankruman/VortexArena
- Target: https://github.com/Godotic/XonoticGodot

## Your Constraints (The Carmack Rules)

1. **Zero Godot references.** No `using Godot;`. No `Node`, no `Vector3` from Godot. Use `System.Numerics.Vector3` or custom math types.
2. **Zero Unity references.** Strip all `using UnityEngine;`, `MonoBehaviour`, `NetworkBehaviour`, `[SyncVar]`, `[SerializeField]`, etc.
3. **Deterministic simulation.** The physics tick must produce identical results given identical inputs, regardless of framerate.
4. **Engine-services facade.** Where the original code calls Unity APIs (raycasts, collision, audio), define an `interface` that the Godot layer will implement later.
5. **xUnit tests mandatory.** Every ported system must include test stubs that will verify Golden Trace parity against the original.

## Your Task

Port the following two core systems from Unitystation to engine-agnostic C# for the `Godotstation.Engine` namespace:

### System 1: UniversalObjectPhysics (Movement & Collision)

This is the core physics system. It handles newtonian movement, friction, collision, pushing, pulling, and space drift. The file is 1,962 lines and uses partial classes.

**Key constants to preserve:**
- `MAX_SPEED = 25`
- `HIGH_SPEED_COLLISION_THRESHOLD = 13`
- `DEFAULT_Friction = 15f`
- `DEFAULT_SLIDE_FRICTION = 9f`
- `ObjectBouncyness = 0.75f`

**Key state:**
- `newtonianMovement` (Vector2) — current velocity
- `LocalTargetPosition` (Vector3) — where the object is moving to
- `airTime`, `slideTime` — friction reduction timers
- `Intangible` — whether the object ignores collision

**Unity dependencies to replace with facades:**
- `NetworkBehaviour` → remove, extract sync state into a separate `INetworkSync` interface
- `[SyncVar]` → mark fields that need network sync with a `[Networked]` attribute you define
- `Vector2`/`Vector3` → `System.Numerics.Vector2`/`Vector3`
- `BoxCollider2D` → `ICollisionService.CheckCollision()`
- `MatrixManager` → `ITileGridService`

### System 2: AtmosSimulation (Atmospheric Simulation)

This is the atmospheric gas simulation. It runs on a per-tile basis using a queue of dirty nodes. The file is 586 lines.

**Key mechanics:**
- Pressure equalization between adjacent tiles
- Gas mixing and diffusion
- Thermal conduction (tile-to-tile, tile-to-space)
- Gas reactions (fire, plasma, etc.)
- Visual effects (ice, gas overlays)

**Unity dependencies to replace:**
- `Mathf` → `System.MathF` or `Math`
- `MetaDataNode` → define your own `IAtmosNode` interface
- `GasMix` → port as a pure data struct
- `AtmosManager.Instance` → `IAtmosService` interface
- Visual effects (overlays, tiles) → `IAtmosVisualService` interface (called but not implemented by you)

## Expected Output

Produce the following files:

1. **`src/Godotstation.Engine/Physics/ObjectPhysics.cs`** — The core movement/collision logic, stripped of all Unity/Mirror dependencies.
2. **`src/Godotstation.Engine/Physics/ICollisionService.cs`** — Facade interface for collision queries.
3. **`src/Godotstation.Engine/Physics/ITileGridService.cs`** — Facade interface for tile grid queries.
4. **`src/Godotstation.Engine/Atmos/AtmosSimulation.cs`** — The atmospheric simulation, pure C#.
5. **`src/Godotstation.Engine/Atmos/GasMix.cs`** — The gas mixture data structure.
6. **`src/Godotstation.Engine/Atmos/IAtmosNode.cs`** — Interface for atmospheric node data.
7. **`tests/Godotstation.Tests/Physics/ObjectPhysicsTests.cs`** — xUnit test class with Golden Trace test structure.
8. **`tests/Godotstation.Tests/Atmos/AtmosSimulationTests.cs`** — xUnit test class for atmos parity.

## Source Code (Excerpts)

The full files are in the Unitystation repository. Here are the critical sections:

### UniversalObjectPhysics.cs (header + key fields)

```csharp
using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace US13.Core.Physics
{
    public partial class UniversalObjectPhysics : NetworkBehaviour, IRegisterTileInitialised
    {
        [PlayModeOnly] public Vector2 LocalDifferenceNeeded;
        [PlayModeOnly] public Vector2 newtonianMovement;
        [PlayModeOnly] public Vector3 LocalTargetPosition;
        [PlayModeOnly] private Vector3 LastDifference = Vector3.zero;
        [PlayModeOnly] public bool CorrectingCourse = false;
        [PlayModeOnly] public bool Animating = false;
        [PlayModeOnly] public float airTime;
        [PlayModeOnly] public float slideTime;
        [PlayModeOnly] public float spinMagnitude = 0;
        [PlayModeOnly, SyncVar] public NetworkIdentity thrownBy;
        [PlayModeOnly, SyncVar] public NetworkIdentity thrownProtection;
        public bool ChangesDirectionPush = false;
        [SyncVar] public bool Intangible = false;
        public float ObjectBouncyness = 0.75f;
        public const float MAX_SPEED = 25;
        public const int HIGH_SPEED_COLLISION_THRESHOLD = 13;
        public const float DEFAULT_Friction = 15f;
        public const float DEFAULT_SLIDE_FRICTION = 9f;
    }
}
```

### AtmosSimulation.cs (core loop)

```csharp
namespace US13.Tilemaps.Behaviours.Meta.Atmospherics
{
    public class AtmosSimulation
    {
        public int UpdateListCount => updateList.Count;
        private List<(MetaDataNode node, bool doEqualise)> nodes = new List<(MetaDataNode, bool)>(5);
        private UniqueQueue<MetaDataNode> updateList = new UniqueQueue<MetaDataNode>();

        public void Run()
        {
            int count = updateList.Count;
            for (int i = 0; i < count; i++)
            {
                if (updateList.TryDequeue(out MetaDataNode node))
                {
                    if(node.Exists == false) continue;
                    if(node.MetaDataSystem.SetUpDone == false) continue;
                    Update(node);
                }
            }
        }

        private void Update(MetaDataNode node)
        {
            nodes.Clear();
            var doEqualise = node.IsOccupied == false && node.IsIsolatedNode == false;
            nodes.Add((node, doEqualise));
            node.AddNeighborsToList(ref nodes);
            // ... pressure equalization, thermal conduction, reactions ...
        }
    }
}
```

### MovementSynchronisation.cs (player movement, extends UniversalObjectPhysics)

```csharp
namespace US13.Player.MovementV2
{
    public class MovementSynchronisation : UniversalObjectPhysics, IPlayerControllable, IActionGUI, ICooldown
    {
        public List<MoveData> MoveQueue = new List<MoveData>();
        private const float MOVE_MAX_DELAY_QUEUE = 4f;
        public float DefaultTime { get; } = 0.5f;
        [SyncVar] private bool allowInput = true;
        // 1,734 lines of player-specific movement, input handling, and network sync
    }
}
```

## Success Criteria

Your output will be evaluated on:
1. **Compilation**: Does it compile as a standalone .NET 8 class library with zero Godot/Unity references?
2. **Architectural purity**: Is the facade pattern correctly applied?
3. **Fidelity preservation**: Are the critical constants, branch order, and mathematical operations preserved?
4. **Test coverage**: Do the test stubs correctly define what Golden Trace parity means for each system?
5. **Depth of understanding**: Does the code show you understood *why* the original works, not just *what* it does?

Begin.
