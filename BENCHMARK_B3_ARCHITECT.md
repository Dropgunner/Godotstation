# BENCHMARK B3: The Architect Test — Fidelity Contract Extraction

## Context

This benchmark is given to **both** Claude Fable and OpenAI Codex (Sol) with identical instructions. The goal is to test which model better identifies the "feel-critical" behaviors that must be preserved during the port.

## Project Context

Godotstation is a port of Unitystation (Unity C# → Godot 4 C#). We follow the **Idiomatic Rewrite with a Fidelity Contract** methodology: everything is modernized freely, EXCEPT a small enumerated set of behaviors that define "game feel." These behaviors get white-glove treatment with regression tests.

## Your Task

Given the Unitystation **Interaction System** source code below, produce a **Fidelity Contract**: exactly 5-10 testable assertions that capture the behaviors which, if changed, would make experienced SS13 players say "this feels wrong."

## Rules for the Fidelity Contract

1. Each item must be a **testable assertion**: "Given input X against state Y, the result must be Z."
2. Focus on **player-facing feel**, not implementation details.
3. Exclude: rendering, menu systems, string handling, OO framework choices.
4. Include: interaction resolution order, reach validation, intent-based behavior switching, timing.

## Source Code: The Interaction System

### DefaultWillInteract.cs (Core interaction validation)

```csharp
namespace US13.Core.Input_System.InteractionV2
{
    public static class DefaultWillInteract
    {
        /// <summary>
        /// Chooses and invokes DefaultWillInteract method corresponding to the supplied type.
        /// </summary>
        public static bool Default<T>(T interaction, NetworkSide side,
            PlayerTypes apt = PlayerTypes.Normal, bool AllowTelekinesis = true) where T : Interaction
        {
            if (typeof(T) == typeof(PositionalHandApply))
            {
                var positionalHandApply = interaction as PositionalHandApply;
                var reachRange = ReachRange.Standard;
                if (AllowTelekinesis && positionalHandApply?.PerformerPlayerScript?.playerHealth?.brain != null
                    && positionalHandApply.PerformerPlayerScript.playerHealth.brain.HasTelekinesis)
                {
                    if (positionalHandApply.HandObject == null)
                    {
                        reachRange = ReachRange.Telekinesis;
                    }
                }
                return Validations.CanApply(positionalHandApply.PerformerPlayerScript,
                    positionalHandApply.TargetObject, side,
                    targetPosition: positionalHandApply.TargetPosition, apt: apt, reachRange: reachRange);
            }
            if (typeof(T) == typeof(HandApply))
            {
                var handApply = interaction as HandApply;
                var reachRange = ReachRange.Standard;
                if (AllowTelekinesis && handApply.PerformerPlayerScript?.playerHealth.OrNull()?.brain != null
                    && handApply.PerformerPlayerScript.playerHealth.brain.HasTelekinesis)
                {
                    if (handApply.HandObject == null)
                    {
                        reachRange = ReachRange.Telekinesis;
                    }
                }
                return Validations.CanApply(handApply.PerformerPlayerScript, handApply.TargetObject, side,
                    apt: apt, reachRange: reachRange);
            }
            if (typeof(T) == typeof(AimApply))
            {
                return AimApply(interaction as AimApply, side, apt);
            }
            if (typeof(T) == typeof(MouseDrop))
            {
                var MouseDrop = interaction as MouseDrop;
                return Validations.CanApply(interaction.PerformerPlayerScript, MouseDrop.DroppedObject, side, apt: apt) &&
                       Validations.IsReachableByPositions(MouseDrop.DroppedObject.AssumedWorldPosServer(),
                           MouseDrop.TargetObject.AssumedWorldPosServer(), side == NetworkSide.Server);
            }
            if (typeof(T) == typeof(HandActivate))
            {
                return Validations.CanInteract(interaction.PerformerPlayerScript, side, apt: apt);
            }
            if (typeof(T) == typeof(InventoryApply))
            {
                return Validations.CanInteract(interaction.PerformerPlayerScript, side, apt: apt);
            }
            if (typeof(T) == typeof(TileApply))
            {
                var tileApply = interaction as TileApply;
                return Validations.CanApply(tileApply.PerformerPlayerScript,
                    tileApply.TargetInteractableTiles.gameObject, side,
                    targetPosition: tileApply.TargetPosition, apt: apt);
            }
            // ConnectionApply, ContextMenuApply, AiActivate follow similar patterns...
        }
    }
}
```

### HandApply.cs (Core interaction type)

```csharp
namespace US13.Core.Input_System.InteractionV2.Interactions
{
    /// <summary>
    /// A hand apply interaction occurs when a player clicks something in the game world.
    /// The object in their hand (or their empty hand) is applied to the target object.
    /// </summary>
    public class HandApply : BodyPartTargetedInteraction
    {
        public GameObject HandObject => UsedObject;
        public ItemSlot HandSlot { get; protected set; }
        public bool IsAltClick { get; protected set; }
        public bool IsHighlight;

        public static HandApply ByLocalPlayer(GameObject targetObject)
        {
            if (PlayerManager.LocalMindScript.IsGhosting) return HandApply.Invalid;
            return new HandApply(
                PlayerManager.LocalPlayerObject,
                PlayerManager.LocalPlayerScript?.DynamicItemStorage?.GetActiveHandSlot()?.ItemObject,
                targetObject,
                UIManager.DamageZone,
                PlayerManager.LocalPlayerScript?.DynamicItemStorage?.GetActiveHandSlot(),
                UIManager.CurrentIntent,
                PlayerManager.LocalMindScript,
                KeyboardInputManager.IsAltActionKeyPressed());
        }
    }
}
```

### Key Interaction Types in the System

| Type | Trigger | Parameters |
|---|---|---|
| `HandApply` | Click object in world | hand item, target, body part, intent, alt-click |
| `PositionalHandApply` | Click at position | same + world position |
| `AimApply` | Hold-click (guns) | aim direction, intent |
| `MouseDrop` | Drag-and-drop in world | dragged object, drop target |
| `HandActivate` | Z key (use in-hand) | hand item only |
| `InventoryApply` | Click slot on slot | from-slot, to-slot |
| `TileApply` | Click on tile | tile position, hand item |
| `ContextMenuApply` | Right-click menu option | target, selected option |
| `AiActivate` | AI clicks object | target object |

### Intent System

Players can switch between 4 intents that change how interactions resolve:
- **Help** — heal, assist, open
- **Disarm** — push, disarm, non-lethal
- **Grab** — grab, restrain, pull
- **Harm** — attack, damage, break

### Reach Validation

- `ReachRange.Standard` — adjacent tiles only (1 tile)
- `ReachRange.Telekinesis` — extended range (if brain has telekinesis AND hand is empty)
- `IsReachableByPositions` — line-of-sight check between two world positions

## Expected Output Format

```markdown
## Fidelity Contract: Unitystation Interaction System

### FC-1: [Name]
**Assertion**: Given [input], when [condition], then [exact outcome].
**Test**: [How to verify this programmatically]
**Rationale**: [Why this matters for game feel]

### FC-2: [Name]
...
```

## Success Criteria

1. **Testability**: Can each assertion be turned into an automated test?
2. **Feel-focus**: Does it capture what players would notice, not what developers would notice?
3. **Completeness**: Does it cover the interaction resolution, reach, intent, and ordering?
4. **Conciseness**: Is it 5-10 items, not 50? (The fidelity contract is intentionally small.)
5. **Avoidance of implementation details**: Does it avoid prescribing HOW to implement, only WHAT must be true?

Begin.
