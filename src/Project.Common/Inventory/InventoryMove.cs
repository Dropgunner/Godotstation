using System.Numerics;

namespace Godotstation.Common.Inventory;

/// <summary>
/// The different kinds of inventory movements.
/// </summary>
public enum InventoryMoveType
{
    /// <summary>Transferred directly from one item slot to another.</summary>
    Transfer,
    /// <summary>Totally removed from the inventory system (e.g. dropped on the ground).</summary>
    Remove,
    /// <summary>Added to the inventory system when previously in no slot (e.g. picked up).</summary>
    Add,
}

/// <summary>
/// Different ways in which inventory can be removed.
/// </summary>
public enum InventoryRemoveType
{
    /// <summary>Despawned directly from inventory, doesn't go into the world first.</summary>
    Despawn,
    /// <summary>Dropped into the world wherever its root storage is.</summary>
    Drop,
    /// <summary>Thrown / ejected into the world in a particular direction.</summary>
    Throw,
    /// <summary>Removed from inventory but kept hidden — not in the world, not despawned. Rare.</summary>
    Vanish,
}

/// <summary>
/// Defines what should happen if an item already occupies the slot being added to.
/// </summary>
public enum ReplacementStrategy
{
    /// <summary>Despawn the item currently in the target slot.</summary>
    DespawnOther,
    /// <summary>Drop the item currently in the target slot.</summary>
    DropOther,
    /// <summary>Cancel the inventory movement (default).</summary>
    Cancel,
}

/// <summary>
/// Describes (but does not perform) a particular inventory movement of a particular item.
/// Used to perform the described movement (in <see cref="InventorySystem"/>) as well as to
/// pass the information to inventory movement hooks.
/// </summary>
public sealed class InventoryMove
{
    /// <summary>What to do if the target slot contains something.</summary>
    public ReplacementStrategy ReplacementStrategy { get; }

    /// <summary>What kind of movement occurred.</summary>
    public InventoryMoveType InventoryMoveType { get; }

    /// <summary>Item being moved. Null only for malformed moves, which ServerPerform rejects.</summary>
    public IPickupable? MovedObject { get; }

    /// <summary>Slot the item was moved from (null if Add).</summary>
    public ItemSlot? FromSlot { get; }

    /// <summary>Slot the item was moved to (null if Remove).</summary>
    public ItemSlot? ToSlot { get; }

    /// <summary>If Remove, the way in which it is removed.</summary>
    public InventoryRemoveType? RemoveType { get; }

    /// <summary>If Throw, what body part to aim at.</summary>
    public BodyPartType? ThrowAim { get; }

    /// <summary>
    /// If Throw or Drop, world space vector pointing from the thrower/dropper to the
    /// targeted position. Null on a drop means "drop at holder position".
    /// </summary>
    public Vector2? WorldTargetVector { get; }

    /// <summary>
    /// Skip CanFit constraints — allows things like a suit in a cardboard box that can
    /// only be taken out, never put back in.
    /// </summary>
    public bool IgnoreConstraints { get; }

    public InventoryMove(InventoryMoveType inventoryMoveType, IPickupable? movedObject,
        ItemSlot? fromSlot, ItemSlot? toSlot, InventoryRemoveType? removeType = null,
        BodyPartType? throwAim = null, Vector2? worldTargetVector = null,
        ReplacementStrategy replacementStrategy = ReplacementStrategy.Cancel,
        bool ignoreRestraints = false)
    {
        InventoryMoveType = inventoryMoveType;
        MovedObject = movedObject;
        FromSlot = fromSlot;
        ToSlot = toSlot;
        RemoveType = removeType;
        ThrowAim = throwAim;
        WorldTargetVector = worldTargetVector;
        ReplacementStrategy = replacementStrategy;
        IgnoreConstraints = ignoreRestraints;
    }

    /// <summary>Move in which the item in one slot is transferred directly to another.</summary>
    public static InventoryMove Transfer(ItemSlot fromSlot, ItemSlot toSlot,
        ReplacementStrategy replacementStrategy = ReplacementStrategy.Cancel,
        bool ignoreRestraints = false) =>
        new(InventoryMoveType.Transfer, fromSlot.Item, fromSlot, toSlot,
            replacementStrategy: replacementStrategy, ignoreRestraints: ignoreRestraints);

    /// <summary>Move in which an item not currently in any slot enters the inventory system.</summary>
    public static InventoryMove Add(IPickupable? addedObject, ItemSlot toSlot,
        ReplacementStrategy replacementStrategy = ReplacementStrategy.Cancel,
        bool ignoreRestraints = false) =>
        new(InventoryMoveType.Add, addedObject, null, toSlot,
            replacementStrategy: replacementStrategy, ignoreRestraints: ignoreRestraints);

    /// <summary>Move in which the item in the slot is despawned directly from inventory.</summary>
    public static InventoryMove Despawn(ItemSlot fromSlot) =>
        new(InventoryMoveType.Remove, fromSlot.Item, fromSlot, null, InventoryRemoveType.Despawn);

    /// <summary>Move in which the item in the slot is dropped into the world at its root storage.</summary>
    public static InventoryMove Drop(ItemSlot fromSlot, Vector2? worldTargetVector = null) =>
        new(InventoryMoveType.Remove, fromSlot.Item, fromSlot, null, InventoryRemoveType.Drop,
            null, worldTargetVector);

    /// <summary>
    /// Move in which the item is removed from inventory but neither dropped, despawned,
    /// nor made visible. Should RARELY be used — prefer transferring into an ItemStorage.
    /// </summary>
    public static InventoryMove Vanish(ItemSlot fromSlot) =>
        new(InventoryMoveType.Remove, fromSlot.Item, fromSlot, null, InventoryRemoveType.Vanish);

    /// <summary>Move in which the item in the slot is thrown from its root storage.</summary>
    public static InventoryMove Throw(ItemSlot fromSlot, Vector2 worldTargetVector,
        BodyPartType aim = BodyPartType.Chest) =>
        new(InventoryMoveType.Remove, fromSlot.Item, fromSlot, null, InventoryRemoveType.Throw,
            aim, worldTargetVector);

    public override string ToString() =>
        $"InventoryMove of type {InventoryMoveType} for object {MovedObject?.Name ?? "null"} " +
        $"from slot {FromSlot?.ToString() ?? "null"} to slot {ToSlot?.ToString() ?? "null"}.";
}
