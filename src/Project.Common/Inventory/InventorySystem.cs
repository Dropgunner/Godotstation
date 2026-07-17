using System.Numerics;
using Project.Common.Movement;

namespace Godotstation.Common.Inventory;

/// <summary>
/// Main API for modifying inventory — port of Unitystation's static Inventory class.
/// If you need to do something with inventory, check here first.
///
/// The Unity original was a static class reaching into scene singletons; here the world
/// side-effects are injected through <see cref="IInventoryWorld"/> so the logic stays
/// headless and deterministic.
/// </summary>
public sealed class InventorySystem
{
    private readonly IInventoryWorld world;

    /// <summary>Raised after any move performs successfully (sound/UI hook for the frontend).</summary>
    public event Action<InventoryMove>? OnMovePerformed;

    public InventorySystem(IInventoryWorld world)
    {
        this.world = world;
    }

    /// <summary>
    /// Move in which the item was not previously in the inventory system and now is.
    /// </summary>
    /// <param name="replacementStrategy">what to do if toSlot is already occupied</param>
    /// <returns>true if successful</returns>
    public bool ServerAdd(IPickupable? addedObject, ItemSlot toSlot,
        ReplacementStrategy replacementStrategy = ReplacementStrategy.Cancel,
        bool ignoreRestraints = false) =>
        ServerPerform(InventoryMove.Add(addedObject, toSlot, replacementStrategy, ignoreRestraints));

    /// <summary>
    /// Move in which the item in one slot is transferred directly to another.
    /// </summary>
    /// <param name="replacementStrategy">what to do if toSlot is already occupied</param>
    /// <returns>true if successful</returns>
    public bool ServerTransfer(ItemSlot fromSlot, ItemSlot toSlot,
        ReplacementStrategy replacementStrategy = ReplacementStrategy.Cancel,
        bool ignoreRestraints = false) =>
        ServerPerform(InventoryMove.Transfer(fromSlot, toSlot, replacementStrategy, ignoreRestraints));

    /// <summary>
    /// Swaps the items in two slots, via a drop-and-re-add round trip through the world
    /// (mirrors upstream ServerSwap). No-op if either slot's item is not removable.
    /// </summary>
    public void ServerSwap(ItemSlot slotOne, ItemSlot slotTwo)
    {
        if (slotOne.ItemNotRemovable || slotTwo.ItemNotRemovable) return;

        var objectOne = slotOne.Item;
        ServerDrop(slotOne);
        var objectTwo = slotTwo.Item;
        ServerDrop(slotTwo);

        ServerAdd(objectOne, slotTwo);
        ServerAdd(objectTwo, slotOne);
    }

    /// <summary>
    /// Move in which the item in the slot is despawned directly from inventory and doesn't
    /// reappear in the world.
    /// </summary>
    public bool ServerDespawn(ItemSlot fromSlot) => ServerPerform(InventoryMove.Despawn(fromSlot));

    /// <summary>
    /// Consume the indicated amount of the item in the slot if it is stackable,
    /// otherwise despawn it.
    /// </summary>
    public bool ServerConsume(ItemSlot fromSlot, int amountToConsume)
    {
        if (fromSlot.Item == null) return false;
        var stackable = fromSlot.Item.Stackable;
        if (stackable != null)
        {
            return stackable.ServerConsume(amountToConsume);
        }

        return ServerPerform(InventoryMove.Despawn(fromSlot));
    }

    /// <summary>
    /// Move in which the item in the slot is dropped into the world at the location of its
    /// root storage.
    /// </summary>
    /// <param name="worldTargetVector">world space vector from holder to targeted position,
    /// null to drop at holder's position</param>
    public bool ServerDrop(ItemSlot fromSlot, Vector2? worldTargetVector = null) =>
        ServerPerform(InventoryMove.Drop(fromSlot, worldTargetVector));

    /// <summary>
    /// NOTE: Should RARELY be used — prefer transferring into an ItemStorage.
    /// Move in which the item is removed from inventory but neither dropped, despawned,
    /// nor made visible.
    /// </summary>
    public bool ServerVanish(ItemSlot fromSlot) => ServerPerform(InventoryMove.Vanish(fromSlot));

    /// <summary>
    /// Same as <see cref="ServerVanish"/> but supports stackables (feeding items one at a
    /// time into a machine). Returns the item that was removed.
    /// NOTE: the upstream implementation read the slot AFTER vanishing (returning null /
    /// throwing on non-stackables); this port captures the removed item first — same
    /// intent, without the bug.
    /// </summary>
    public IPickupable? ServerVanishStackable(ItemSlot fromSlot)
    {
        if (fromSlot.Item == null) return null;
        var stackable = fromSlot.Item.Stackable;
        if (stackable != null)
        {
            var clone = stackable.ServerRemoveOne();
            ServerPerform(new InventoryMove(InventoryMoveType.Remove, clone, fromSlot, null,
                InventoryRemoveType.Vanish));
            return clone;
        }

        var removed = fromSlot.Item;
        ServerPerform(InventoryMove.Vanish(fromSlot));
        return removed;
    }

    /// <summary>
    /// Move in which the item in the slot is thrown into the world from the location of its
    /// root storage.
    /// </summary>
    /// <param name="worldTargetVector">world space vector from thrower to targeted position</param>
    /// <param name="aim">body part to target</param>
    public bool ServerThrow(ItemSlot fromSlot, Vector2 worldTargetVector,
        BodyPartType aim = BodyPartType.Chest) =>
        ServerPerform(InventoryMove.Throw(fromSlot, worldTargetVector, aim));

    /// <summary>
    /// Drops every occupied slot of the storage.
    /// </summary>
    public void ServerDropAll(ItemStorage storage, Vector2? worldTargetVector = null)
    {
        var slots = storage.GetItemSlots().ToList();
        foreach (var slot in slots)
        {
            if (slot.IsEmpty) continue;
            ServerPerform(InventoryMove.Drop(slot, worldTargetVector));
        }
    }

    /// <summary>
    /// Adds the item to the first slot of the storage it can fit in — the item's current
    /// slot if it has one is transferred from, otherwise it is added fresh
    /// (mirrors upstream ItemStorage.ServerTryAdd, minus the trait-based best-slot
    /// selection, which is deferred).
    /// </summary>
    public bool ServerTryAdd(ItemStorage storage, IPickupable item, bool ignoreRestraints = false)
    {
        var slot = storage.GetItemSlots().FirstOrDefault(s => s.CanFit(item));
        if (slot == null) return false;

        var currentSlot = item.ItemSlot;
        return currentSlot == null
            ? ServerAdd(item, slot, ignoreRestraints: ignoreRestraints)
            : ServerTransfer(currentSlot, slot, ignoreRestraints: ignoreRestraints);
    }

    /// <summary>
    /// Transfers every item of <paramref name="fromStorage"/> into <paramref name="toStorage"/>.
    /// True only if every item transferred.
    /// </summary>
    public bool ServerTryTransferFrom(ItemStorage toStorage, ItemStorage fromStorage)
    {
        var overall = true;
        foreach (var slot in fromStorage.GetItemSlots().ToList())
        {
            if (slot.Item == null) continue;
            if (ServerTryAdd(toStorage, slot.Item) == false)
            {
                overall = false;
            }
        }

        return overall;
    }

    /// <summary>
    /// General purpose method for performing an inventory move. Performs the move
    /// indicated by toPerform.
    /// </summary>
    /// <returns>true if successful</returns>
    public bool ServerPerform(InventoryMove? toPerform)
    {
        if (toPerform == null) return false;

        var pickupable = toPerform.MovedObject;
        if (pickupable == null) return false;

        var performed = toPerform.InventoryMoveType switch
        {
            InventoryMoveType.Add => ServerPerformAdd(toPerform, pickupable),
            InventoryMoveType.Remove => ServerPerformRemove(toPerform, pickupable),
            InventoryMoveType.Transfer => ServerPerformTransfer(toPerform, pickupable),
            _ => false,
        };
        if (performed == false) return false;

        OnMovePerformed?.Invoke(toPerform);
        return true;
    }

    private bool ServerPerformAdd(InventoryMove toPerform, IPickupable pickupable)
    {
        // Item is not currently in inventory; it should be moved into the indicated slot.
        if (pickupable.IsBuckled) return false;
        if (pickupable.ItemSlot != null) return false;

        var toSlot = toPerform.ToSlot;
        if (toSlot == null) return false;

        if (toSlot.Item != null)
        {
            var stackableTarget = toSlot.Item.Stackable;
            if (stackableTarget != null && stackableTarget.CanAccommodate(pickupable))
            {
                stackableTarget.ServerCombine(pickupable.Stackable!);
                return true;
            }

            switch (toPerform.ReplacementStrategy)
            {
                case ReplacementStrategy.DespawnOther:
                    ServerDespawn(toSlot);
                    break;
                case ReplacementStrategy.DropOther:
                    ServerDrop(toSlot);
                    break;
                case ReplacementStrategy.Cancel:
                default:
                    return false;
            }
        }

        if (toSlot.CanFit(pickupable, ignoreOccupied: true) == false &&
            toPerform.IgnoreConstraints == false)
        {
            return false;
        }

        // No longer physically in the world.
        world.DisappearFromWorld(pickupable);

        pickupable.SetItemSlot(toSlot);
        toSlot.ServerSetItem(pickupable);

        InvokeMoveHooks(pickupable, toPerform);
        return true;
    }

    private bool ServerPerformTransfer(InventoryMove toPerform, IPickupable pickupable)
    {
        var toSlot = toPerform.ToSlot;
        var fromSlot = toPerform.FromSlot;
        if (toSlot == null) return false;

        if (toSlot.Item != null)
        {
            // Upstream quirk, preserved: an occupied destination checks ITS lock before
            // anything else, and rejects the transfer outright.
            if (toSlot.ItemNotRemovable) return false;

            var stackableTarget = toSlot.Item.Stackable;
            if (stackableTarget != null && stackableTarget.CanAccommodate(pickupable))
            {
                stackableTarget.ServerCombine(pickupable.Stackable!);
                return true;
            }

            switch (toPerform.ReplacementStrategy)
            {
                case ReplacementStrategy.DespawnOther:
                    ServerDespawn(toSlot);
                    break;
                case ReplacementStrategy.DropOther:
                    ServerDrop(toSlot);
                    break;
                case ReplacementStrategy.Cancel:
                default:
                    return false;
            }
        }

        if (pickupable.ItemSlot == null) return false;
        if (fromSlot == null) return false;

        if (toSlot.CanFit(pickupable, ignoreOccupied: true) == false &&
            toPerform.IgnoreConstraints == false)
        {
            return false;
        }

        if (fromSlot.ItemNotRemovable) return false;

        pickupable.SetItemSlot(null);
        fromSlot.ServerRemoveItem();

        pickupable.SetItemSlot(toSlot);
        toSlot.ServerSetItem(pickupable);

        InvokeMoveHooks(pickupable, toPerform);
        return true;
    }

    private bool ServerPerformRemove(InventoryMove toPerform, IPickupable pickupable)
    {
        if (pickupable.ItemSlot == null) return false;

        var fromSlot = toPerform.FromSlot;
        if (fromSlot == null) return false;
        if (fromSlot.Item == null) return false;
        if (fromSlot.ItemNotRemovable) return false;

        pickupable.SetItemSlot(null);
        fromSlot.ServerRemoveItem();

        // Decide how it should be removed. Throwing from inside a container degrades to a
        // drop (Fidelity Contract Rule 6).
        var removeType = toPerform.RemoveType;
        var holderInContainer = world.IsHolderInContainer(fromSlot);
        if (holderInContainer && removeType == InventoryRemoveType.Throw)
        {
            removeType = InventoryRemoveType.Drop;
        }

        if (removeType == InventoryRemoveType.Despawn)
        {
            world.Despawn(pickupable);
        }
        else if (removeType == InventoryRemoveType.Drop)
        {
            if (holderInContainer)
            {
                // Holder sits inside a locker/crate: the item goes into that container
                // rather than onto the floor.
                world.StoreInHolderContainer(pickupable, fromSlot);
                InvokeMoveHooks(pickupable, toPerform);
                return true;
            }

            world.Drop(pickupable, fromSlot, toPerform.WorldTargetVector);
        }
        else if (removeType == InventoryRemoveType.Throw)
        {
            var targetVector = toPerform.WorldTargetVector.GetValueOrDefault(Vector2.Zero);
            var trajectory = targetVector.LengthSquared() > 0f
                ? Vector2.Normalize(targetVector)
                : Vector2.Zero;
            var distance = ClampThrowDistance(targetVector.Length(), pickupable.ThrowRange);
            var airTime = ComputeThrowAirTime(distance, pickupable.ThrowSpeed, pickupable.StickyMovement);

            world.Throw(pickupable, fromSlot, trajectory, pickupable.ThrowSpeed, airTime,
                toPerform.ThrowAim.GetValueOrDefault(BodyPartType.Chest));
        }
        else if (removeType == InventoryRemoveType.Vanish)
        {
            world.Vanish(pickupable);
        }

        InvokeMoveHooks(pickupable, toPerform);
        return true;
    }

    private static void InvokeMoveHooks(IPickupable pickupable, InventoryMove toPerform)
    {
        foreach (var onMove in pickupable.MoveHooks)
        {
            onMove.OnInventoryMoveServer(toPerform);
        }
    }

    /// <summary>Throw distance is capped at the item's ThrowRange (Rule 6).</summary>
    public static float ClampThrowDistance(float distance, float throwRange) =>
        distance > throwRange ? throwRange : distance;

    /// <summary>
    /// Airtime of a thrown item, ported from Inventory.ServerPerformRemove's kinematics:
    /// a sticky-movement item flies the full time (distance / speed); otherwise friction
    /// eats the tail of the flight, airtime = d/v − (v² / 2f) / v with
    /// f = <see cref="MovementProcessor.DEFAULT_FRICTION"/>. Negative results (short throws
    /// of fast items) are preserved as-is — the frontend treats them as "lands immediately",
    /// exactly like the original.
    /// </summary>
    public static float ComputeThrowAirTime(float distance, float throwSpeed, bool stickyMovement)
    {
        if (throwSpeed <= 0f) return 0f;

        var timeIfNoFriction = distance / throwSpeed;
        if (stickyMovement)
        {
            return timeIfNoFriction;
        }

        var speedLossTime = (throwSpeed * throwSpeed / (2f * MovementProcessor.DEFAULT_FRICTION)) / throwSpeed;
        return timeIfNoFriction - speedLossTime;
    }
}
