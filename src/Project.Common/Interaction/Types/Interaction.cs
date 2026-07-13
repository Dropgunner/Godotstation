using System.Numerics;

namespace Godotstation.Common.Interaction.Types;

/// <summary>
/// Base class for all interaction types. Engine-agnostic.
/// </summary>
public abstract class Interaction
{
    public IPerformer Performer { get; }
    public bool IsValid { get; protected set; } = true;

    protected Interaction(IPerformer performer)
    {
        Performer = performer;
    }
}

/// <summary>
/// A hand-apply interaction: player clicks something in the world.
/// The object in their hand (or empty hand) is applied to the target.
/// </summary>
public class HandApply : Interaction
{
    public IInteractable? HandObject { get; }
    public IInteractable? TargetObject { get; }
    public Intent Intent { get; }
    public bool IsAltClick { get; }
    public BodyPart TargetBodyPart { get; }

    public static HandApply Invalid => new(NullPerformer.Instance) { IsValid = false };

    public HandApply(
        IPerformer performer,
        IInteractable? handObject = null,
        IInteractable? targetObject = null,
        Intent intent = Intent.Help,
        bool isAltClick = false,
        BodyPart targetBodyPart = BodyPart.Chest)
        : base(performer)
    {
        HandObject = handObject;
        TargetObject = targetObject;
        Intent = intent;
        IsAltClick = isAltClick;
        TargetBodyPart = targetBodyPart;
    }
}

/// <summary>
/// A positional hand-apply: same as HandApply but includes exact world position.
/// </summary>
public class PositionalHandApply : Interaction
{
    public IInteractable? HandObject { get; }
    public IInteractable? TargetObject { get; }
    public Vector2 TargetPosition { get; }
    public Intent Intent { get; }
    public bool IsAltClick { get; }

    public PositionalHandApply(
        IPerformer performer,
        IInteractable? handObject = null,
        IInteractable? targetObject = null,
        Vector2 targetPosition = default,
        Intent intent = Intent.Help,
        bool isAltClick = false)
        : base(performer)
    {
        HandObject = handObject;
        TargetObject = targetObject;
        TargetPosition = targetPosition;
        Intent = intent;
        IsAltClick = isAltClick;
    }
}

/// <summary>
/// Aim-apply: hold-click interactions (guns, spraying).
/// </summary>
public class AimApply : Interaction
{
    public Vector2 AimDirection { get; }
    public Intent Intent { get; }

    public AimApply(IPerformer performer, Vector2 aimDirection, Intent intent = Intent.Help)
        : base(performer)
    {
        AimDirection = aimDirection;
        Intent = intent;
    }
}

/// <summary>
/// Mouse-drop: drag one object onto another in the world.
/// </summary>
public class MouseDrop : Interaction
{
    public IInteractable DroppedObject { get; }
    public IInteractable TargetObject { get; }

    public MouseDrop(IPerformer performer, IInteractable droppedObject, IInteractable targetObject)
        : base(performer)
    {
        DroppedObject = droppedObject;
        TargetObject = targetObject;
    }
}

/// <summary>
/// Hand-activate: pressing Z to use the item in hand.
/// </summary>
public class HandActivate : Interaction
{
    public IInteractable? HandObject { get; }

    public HandActivate(IPerformer performer, IInteractable? handObject = null)
        : base(performer)
    {
        HandObject = handObject;
    }
}

/// <summary>
/// Inventory-apply: clicking one inventory slot onto another.
/// </summary>
public class InventoryApply : Interaction
{
    public IInteractable? FromSlotItem { get; }
    public IInteractable? ToSlotItem { get; }

    public InventoryApply(IPerformer performer, IInteractable? fromSlotItem = null, IInteractable? toSlotItem = null)
        : base(performer)
    {
        FromSlotItem = fromSlotItem;
        ToSlotItem = toSlotItem;
    }
}

/// <summary>
/// Tile-apply: clicking on a tile position.
/// </summary>
public class TileApply : Interaction
{
    public Vector2 TilePosition { get; }
    public IInteractable? HandObject { get; }

    public TileApply(IPerformer performer, Vector2 tilePosition, IInteractable? handObject = null)
        : base(performer)
    {
        TilePosition = tilePosition;
        HandObject = handObject;
    }
}
