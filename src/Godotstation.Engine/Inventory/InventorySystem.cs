using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Godotstation.Engine.Movement;

namespace Godotstation.Engine.Inventory;

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
    private readonly Dictionary<ItemSlot, SlotObservers> slotObservers = new();
    private long nextTransferRequestId;

    /// <summary>Optional transport used for authoritative observer updates.</summary>
    public IInventoryObserverTransport? ObserverTransport { get; set; }

    /// <summary>Optional frontend transport for source-slot inventory sounds.</summary>
    public IInventorySoundTransport? SoundTransport { get; set; }

    /// <summary>Optional world query used to auto-merge stacks after they reach the world.</summary>
    public IGroundStackWorld? GroundStackWorld { get; set; }

    /// <summary>Raised after any move performs successfully (sound/UI hook for the frontend).</summary>
    public event Action<InventoryMove>? OnMovePerformed;

    public InventorySystem(IInventoryWorld world)
    {
        this.world = world;
    }

    /// <summary>Adds a slot observer and immediately sends its current state.</summary>
    public void ServerAddObserver(ItemSlot slot, string observerId)
    {
        if (ObserverTransport == null) return;

        if (slotObservers.TryGetValue(slot, out var observers) == false)
        {
            observers = new SlotObservers(() => NotifyObservers(slot));
            slotObservers.Add(slot, observers);
            slot.OnSlotContentsChangeServer += observers.OnContentsChanged;
        }

        observers.ObserverIds.Add(observerId);
        ObserverTransport.Send(observerId, CreateUpdate(slot));
    }

    /// <summary>Removes a slot observer and sends the empty-slot update used to clear its view.</summary>
    public void ServerRemoveObserver(ItemSlot slot, string observerId)
    {
        if (slotObservers.TryGetValue(slot, out var observers) == false ||
            observers.ObserverIds.Remove(observerId) == false)
        {
            return;
        }

        ObserverTransport?.Send(observerId, CreateUpdate(slot, clearItem: true));
        if (observers.ObserverIds.Count != 0) return;

        slot.OnSlotContentsChangeServer -= observers.OnContentsChanged;
        slotObservers.Remove(slot);
    }

    /// <summary>Sends an identified client transfer request without changing local state.</summary>
    public bool ClientRequestTransfer(int fromSlotId, int toSlotId,
        IInventoryTransferSlotResolver slotResolver, IInventoryTransferRequestTransport transport)
        => ClientRequestTransfer(Interlocked.Increment(ref nextTransferRequestId),
            fromSlotId, toSlotId, slotResolver, transport);

    /// <summary>Sends a transfer request with a caller-provided prediction correlation id.</summary>
    public bool ClientRequestTransfer(long requestId, int fromSlotId, int toSlotId,
        IInventoryTransferSlotResolver slotResolver, IInventoryTransferRequestTransport transport)
    {
        if (requestId <= 0 || !slotResolver.TryResolve(fromSlotId, out var fromSlot) || fromSlot?.Item == null ||
            !slotResolver.TryResolve(toSlotId, out var toSlot) || toSlot == null)
        {
            return false;
        }

        transport.Send(new InventoryTransferRequest(fromSlotId, toSlotId) { RequestId = requestId });
        return true;
    }

    /// <summary>Authorizes and applies a client transfer request against server-owned slots.</summary>
    public bool ServerHandleTransferRequest(string actorId, InventoryTransferRequest request,
        IInventoryTransferSlotResolver slotResolver,
        IInventoryTransferRequestAuthorization authorization)
    {
        if (request.RequestId <= 0 ||
            !slotResolver.TryResolve(request.FromSlotId, out var fromSlot) || fromSlot?.Item == null ||
            !slotResolver.TryResolve(request.ToSlotId, out var toSlot) || toSlot == null ||
            !authorization.CanTransfer(actorId, fromSlot, toSlot))
        {
            return false;
        }

        return ServerTransfer(fromSlot, toSlot);
    }

    /// <summary>
    /// Propagates an EMP through nested storage. The caller owns world targeting and server
    /// authority; resistant items shield their contained contents.
    /// </summary>
    public int ServerPropagateEmp(IPickupable? rootItem, int empStrength,
        IEmpResistancePolicy resistancePolicy)
    {
        if (rootItem == null) return 0;

        var visitedItems = new HashSet<IPickupable>(ReferenceEqualityComparer.Instance);
        var visitedStorages = new HashSet<ItemStorage>(ReferenceEqualityComparer.Instance);
        var visitedBehaviors = new HashSet<IEmpAble>(ReferenceEqualityComparer.Instance);
        var behaviorCount = 0;

        void Visit(IPickupable? item)
        {
            if (item == null || !visitedItems.Add(item) || resistancePolicy.IsEmpResistant(item)) return;

            foreach (var storage in item.ItemStorages)
            {
                if (storage == null || !visitedStorages.Add(storage)) continue;
                foreach (var slot in storage.GetItemSlots())
                    Visit(slot.Item);
            }

            void Apply(IEmpAble behavior)
            {
                if (!visitedBehaviors.Add(behavior)) return;
                behavior.OnEmp(empStrength);
                behaviorCount++;
            }

            if (item is IEmpAble directBehavior) Apply(directBehavior);
            if (item is IEmpAbleProvider provider)
            {
                foreach (var behavior in provider.EmpBehaviors)
                    if (behavior != null) Apply(behavior);
            }
        }

        Visit(rootItem);
        return behaviorCount;
    }

    private void NotifyObservers(ItemSlot slot)
    {
        if (ObserverTransport == null || slotObservers.TryGetValue(slot, out var observers) == false)
            return;

        var update = CreateUpdate(slot);
        foreach (var observerId in observers.ObserverIds)
            ObserverTransport.Send(observerId, update);
    }

    private static InventorySlotUpdate CreateUpdate(ItemSlot slot, bool clearItem = false) =>
        new(slot.ItemStorage, slot.SlotIdentifier, clearItem ? null : slot.Item,
            slot.ItemNotRemovable, clearItem);

    private sealed class SlotObservers(Action onContentsChanged)
    {
        public HashSet<string> ObserverIds { get; } = new(StringComparer.Ordinal);
        public Action OnContentsChanged { get; } = onContentsChanged;
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
    /// Spawns and adds configured initial contents once. Caller owns server authority;
    /// initial contents bypass capacity constraints but still pass the ordinary Add gates.
    /// </summary>
    public int ServerPopulate(ItemStorage storage, InventoryPopulationContext context,
        IInventoryItemSpawner spawner, params IItemStoragePopulator[] populators)
    {
        if (storage.TryBeginContentsPopulation() == false) return 0;
        if (context.SpawnItems == false) return 0;
        if (storage.ManuallySpawnContent && context.SpawnManualContents == false) return 0;

        var addedCount = 0;
        foreach (var populator in populators)
        {
            if (populator == null) continue;
            foreach (var entry in populator.Populate(context))
            {
                var item = spawner.Spawn(entry.ItemId, context);
                if (item == null) continue;

                var slot = entry.Slot.HasValue
                    ? storage.GetItemSlot(entry.Slot.Value)
                    : storage.GetNextEmptySlot();
                if (slot == null) continue;

                if (ServerAdd(item, slot, entry.ReplacementStrategy, ignoreRestraints: true))
                    addedCount++;
            }
        }

        return addedCount;
    }

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
            if (amountToConsume <= 0 || stackable.Amount < amountToConsume) return false;
            if (stackable.Amount == amountToConsume) return ServerDespawn(fromSlot);
            if (!stackable.ServerConsume(amountToConsume)) return false;
            fromSlot.ServerNotifyContentsChanged();
            return true;
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
        if (stackable != null && stackable.Amount > 1)
        {
            var clone = stackable.ServerRemoveOne();
            fromSlot.ServerNotifyContentsChanged();
            world.Vanish(clone);
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

        if (toPerform.InventoryMoveType == InventoryMoveType.Remove &&
            toPerform.RemoveType is InventoryRemoveType.Drop or InventoryRemoveType.Throw)
            ServerStackOnGround(pickupable);

        OnMovePerformed?.Invoke(toPerform);
        if (toPerform.FromSlot is { } sourceSlot &&
            pickupable is IInventorySoundProvider soundProvider &&
            !string.IsNullOrWhiteSpace(soundProvider.InventoryRemoveSoundId))
        {
            SoundTransport?.PlayAtSource(soundProvider.InventoryRemoveSoundId, sourceSlot);
        }

        return true;
    }

    /// <summary>Merges this ground stack into compatible nearby stacks without exceeding capacity.</summary>
    public int ServerStackOnGround(IPickupable? item)
    {
        var ground = GroundStackWorld;
        var target = item?.Stackable;
        if (item == null || target == null || ground == null || !ground.CanAutoStack(item)) return 0;

        var mergedAmount = 0;
        foreach (var candidate in ground.GetGroundStacks(item).ToList())
        {
            if (candidate == null || ReferenceEquals(candidate, item) ||
                !ground.IsStackInitialized(candidate) || candidate.Stackable is not { } other ||
                !target.CanAccommodate(candidate))
            {
                continue;
            }

            var targetAmountBefore = target.Amount;
            var candidateAmountBefore = other.Amount;
            target.ServerCombine(other);
            mergedAmount += Math.Max(0, target.Amount - targetAmountBefore);

            if (candidateAmountBefore > 0 && other.Amount <= 0)
                world.Despawn(candidate);
        }

        return mergedAmount;
    }

    private bool ServerPerformAdd(InventoryMove toPerform, IPickupable pickupable)
    {
        // Item is not currently in inventory; it should be moved into the indicated slot.
        if (pickupable.IsBuckled) return false;
        if (pickupable.ItemSlot != null) return false;

        var toSlot = toPerform.ToSlot;
        if (toSlot == null) return false;

        // A denied storage category must not merge into an occupied stack before the
        // ordinary CanFit check runs. ignoreRestraints bypasses capacity checks by contract.
        if (toSlot.DeniesStorageItem(pickupable) && toPerform.IgnoreConstraints == false)
        {
            return false;
        }

        if (toSlot.Item != null)
        {
            var stackableTarget = toSlot.Item.Stackable;
            if (stackableTarget != null && stackableTarget.CanAccommodate(pickupable))
            {
                stackableTarget.ServerCombine(pickupable.Stackable!);
                world.Despawn(pickupable);
                toSlot.ServerNotifyContentsChanged();
                InvokeMoveHooks(pickupable, toPerform);
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

        }

        if (pickupable.ItemSlot == null) return false;
        if (fromSlot == null) return false;

        if (toSlot.CanFit(pickupable, ignoreOccupied: true) == false &&
            toPerform.IgnoreConstraints == false)
        {
            return false;
        }

        if (fromSlot.ItemNotRemovable) return false;

        if (toSlot.Item?.Stackable is { } stackableTarget && stackableTarget.CanAccommodate(pickupable))
        {
            pickupable.SetItemSlot(null);
            fromSlot.ServerRemoveItem();
            stackableTarget.ServerCombine(pickupable.Stackable!);
            world.Despawn(pickupable);
            toSlot.ServerNotifyContentsChanged();
            InvokeMoveHooks(pickupable, toPerform);
            return true;
        }

        if (toSlot.Item != null)
        {
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

