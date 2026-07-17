using System.Numerics;

namespace Godotstation.Common.Inventory;

/// <summary>
/// Body part a throw can be aimed at. Pass-through data for the combat/health systems;
/// the inventory backend never branches on it beyond defaulting to Chest.
/// </summary>
public enum BodyPartType
{
    None,
    Head,
    Eyes,
    Mouth,
    Chest,
    LeftArm,
    RightArm,
    Groin,
    LeftLeg,
    RightLeg,
}

/// <summary>
/// An item that can live in an <see cref="ItemSlot"/>. Engine seam for Unitystation's
/// Pickupable component — Godot implements this on its item entities.
/// </summary>
public interface IPickupable
{
    /// <summary>Display/debug name of the item.</summary>
    string Name { get; }

    /// <summary>Slot this item currently occupies, null if not in the inventory system.</summary>
    ItemSlot? ItemSlot { get; }

    /// <summary>
    /// Bookkeeping only — records which slot the item is in. Never call directly;
    /// <see cref="InventorySystem"/> keeps this in sync with the slot's own item reference.
    /// </summary>
    void SetItemSlot(ItemSlot? slot);

    /// <summary>True if the item is buckled to something in the world (can't be picked up).</summary>
    bool IsBuckled { get; }

    /// <summary>Stackable behaviour of this item, null if not stackable.</summary>
    IStackable? Stackable { get; }

    /// <summary>Nested storages this item itself provides (a backpack in a slot), empty if none.</summary>
    IEnumerable<ItemStorage> ItemStorages { get; }

    /// <summary>Per-item hooks notified after this item completes any inventory move.</summary>
    IEnumerable<IServerInventoryMove> MoveHooks { get; }

    /// <summary>Speed (tiles/second) this item leaves the thrower's hand at.</summary>
    float ThrowSpeed { get; }

    /// <summary>Maximum distance (tiles) this item can be thrown.</summary>
    float ThrowRange { get; }

    /// <summary>True if the item moves stickily (magboots-style, no friction glide).</summary>
    bool StickyMovement { get; }
}

/// <summary>
/// Stackable behaviour of an item (a stack of metal sheets). Engine seam for
/// Unitystation's Stackable component.
/// </summary>
public interface IStackable
{
    /// <summary>Current amount in the stack.</summary>
    int Amount { get; }

    /// <summary>True if <paramref name="other"/> can merge into this stack.</summary>
    bool CanAccommodate(IPickupable other);

    /// <summary>Merges <paramref name="other"/> into this stack (other is consumed).</summary>
    void ServerCombine(IStackable other);

    /// <summary>Consumes the given amount from the stack. False if not enough.</summary>
    bool ServerConsume(int amountToConsume);

    /// <summary>Splits one unit off the stack and returns it as a new item.</summary>
    IPickupable ServerRemoveOne();
}

/// <summary>
/// Hook invoked server-side after an inventory move completes. Port of Unitystation's
/// IServerInventoryMove component interface.
/// </summary>
public interface IServerInventoryMove
{
    void OnInventoryMoveServer(InventoryMove info);
}

/// <summary>
/// Capacity policy of a storage — decides what each slot is allowed to hold
/// (size limits, trait requirements). Engine seam for ItemStorageCapacity assets.
/// </summary>
public interface IItemStorageCapacity
{
    bool CanFit(IPickupable toCheck, SlotIdentifier inSlot);
}

/// <summary>
/// World side-effects of inventory moves — spawning items back into the world, despawning,
/// throw physics. The inventory backend decides WHAT happens; the frontend decides how it
/// looks in the world. Mirrors the IInteractionWorld seam from the Interaction port.
/// </summary>
public interface IInventoryWorld
{
    /// <summary>
    /// True if the root holder of this slot (player/crate) is itself inside an object
    /// container (a locker). Throws degrade to drops inside containers.
    /// </summary>
    bool IsHolderInContainer(ItemSlot fromSlot);

    /// <summary>Item entered inventory: remove its physical presence from the world.</summary>
    void DisappearFromWorld(IPickupable item);

    /// <summary>Item was despawned directly from inventory (never reappears).</summary>
    void Despawn(IPickupable item);

    /// <summary>
    /// Item dropped into the world at its root holder's position, offset by
    /// <paramref name="worldTargetVector"/> if given (null = at holder, inheriting momentum).
    /// </summary>
    void Drop(IPickupable item, ItemSlot fromSlot, Vector2? worldTargetVector);

    /// <summary>Item removed while the holder sits inside a container: store it in that container.</summary>
    void StoreInHolderContainer(IPickupable item, ItemSlot fromSlot);

    /// <summary>
    /// Item thrown from its root holder's position along <paramref name="trajectory"/>
    /// (normalized) at <paramref name="throwSpeed"/>, airborne for <paramref name="airTime"/>
    /// seconds, aimed at <paramref name="aim"/>. The backend has already clamped range and
    /// computed the airtime (Fidelity Contract Rule 6).
    /// </summary>
    void Throw(IPickupable item, ItemSlot fromSlot, Vector2 trajectory, float throwSpeed,
        float airTime, BodyPartType aim);

    /// <summary>Item removed from inventory but kept out of the world (hidden). Rare.</summary>
    void Vanish(IPickupable item);
}
