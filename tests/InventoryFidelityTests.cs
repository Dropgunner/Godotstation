using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Godotstation.Common.Inventory;
using Xunit;

namespace Godotstation.Tests;

/// <summary>
/// Fidelity Contract tests for the Inventory & Item Storage system.
/// See planning/FidelityContract_Inventory.md for the rules being enforced.
/// </summary>
public class InventoryFidelityTests
{
    // --- Test Helpers ---

    private class TestItem : IPickupable
    {
        public TestItem(string name) { Name = name; }
        public string Name { get; }
        public ItemSlot? ItemSlot { get; private set; }
        public void SetItemSlot(ItemSlot? slot) => ItemSlot = slot;
        public bool IsBuckled { get; set; }
        public IStackable? Stackable { get; set; }
        public List<ItemStorage> Storages { get; } = new List<ItemStorage>();
        public IEnumerable<ItemStorage> ItemStorages => Storages;
        public List<IServerInventoryMove> Hooks { get; } = new List<IServerInventoryMove>();
        public IEnumerable<IServerInventoryMove> MoveHooks => Hooks;
        public float ThrowSpeed { get; set; } = 6f;
        public float ThrowRange { get; set; } = 7f;
        public bool StickyMovement { get; set; }
    }

    private class TestStack : IStackable
    {
        public TestStack(int amount) { Amount = amount; }
        public int Amount { get; set; }
        public int MaxAmount { get; set; } = 50;
        public bool CombineCalled { get; private set; }
        public bool CanAccommodate(IPickupable other) =>
            other.Stackable is TestStack && Amount < MaxAmount;
        public void ServerCombine(IStackable other)
        {
            CombineCalled = true;
            Amount += other.Amount;
        }
        public bool ServerConsume(int amountToConsume)
        {
            if (Amount < amountToConsume) return false;
            Amount -= amountToConsume;
            return true;
        }
        public IPickupable ServerRemoveOne()
        {
            Amount -= 1;
            return new TestItem("split-off");
        }
    }

    private class AcceptAllCapacity : IItemStorageCapacity
    {
        public bool CanFit(IPickupable toCheck, SlotIdentifier inSlot) => true;
    }

    private class RejectAllCapacity : IItemStorageCapacity
    {
        public bool CanFit(IPickupable toCheck, SlotIdentifier inSlot) => false;
    }

    private class RecordingWorld : IInventoryWorld
    {
        public bool HolderInContainer { get; set; }
        public List<IPickupable> Despawned { get; } = new List<IPickupable>();
        public List<IPickupable> Dropped { get; } = new List<IPickupable>();
        public List<IPickupable> StoredInContainer { get; } = new List<IPickupable>();
        public List<IPickupable> Vanished { get; } = new List<IPickupable>();
        public List<IPickupable> DisappearedFromWorld { get; } = new List<IPickupable>();
        public List<(IPickupable Item, Vector2 Trajectory, float Speed, float AirTime, BodyPartType Aim)> Thrown { get; }
            = new List<(IPickupable, Vector2, float, float, BodyPartType)>();

        public bool IsHolderInContainer(ItemSlot fromSlot) => HolderInContainer;
        public void DisappearFromWorld(IPickupable item) => DisappearedFromWorld.Add(item);
        public void Despawn(IPickupable item) => Despawned.Add(item);
        public void Drop(IPickupable item, ItemSlot fromSlot, Vector2? worldTargetVector) => Dropped.Add(item);
        public void StoreInHolderContainer(IPickupable item, ItemSlot fromSlot) => StoredInContainer.Add(item);
        public void Throw(IPickupable item, ItemSlot fromSlot, Vector2 trajectory, float throwSpeed,
            float airTime, BodyPartType aim) => Thrown.Add((item, trajectory, throwSpeed, airTime, aim));
        public void Vanish(IPickupable item) => Vanished.Add(item);
    }

    private class RecordingHook : IServerInventoryMove
    {
        public List<InventoryMove> Moves { get; } = new List<InventoryMove>();
        public void OnInventoryMoveServer(InventoryMove info) => Moves.Add(info);
    }

    private static ItemStorage MakeStorage(int indexedSlots = 4, IPickupable? asPickupable = null,
        IItemStorageCapacity? capacity = null, string name = "storage")
    {
        var identifiers = Enumerable.Range(0, indexedSlots).Select(SlotIdentifier.Indexed);
        return new ItemStorage(identifiers, capacity ?? new AcceptAllCapacity(), asPickupable, name);
    }

    private static (InventorySystem System, RecordingWorld World) MakeSystem()
    {
        var world = new RecordingWorld();
        return (new InventorySystem(world), world);
    }

    // --- Rule 1: Slot Identity ---

    [Fact]
    public void Rule1_SameSlotRequestReturnsSameInstance()
    {
        var storage = MakeStorage();
        var a = storage.GetIndexedItemSlot(0);
        var b = ItemSlot.GetIndexed(storage, 0);
        Assert.NotNull(a);
        Assert.Same(a, b);
    }

    [Fact]
    public void Rule1_UndefinedSlotReturnsNull()
    {
        var storage = MakeStorage(indexedSlots: 2);
        Assert.Null(storage.GetIndexedItemSlot(5));
        Assert.Null(storage.GetNamedItemSlot(NamedSlot.back));
    }

    // --- Rule 2: The Add Gauntlet ---

    [Fact]
    public void Rule2_AddToOccupiedSlot_CancelStrategyFails()
    {
        var (inv, _) = MakeSystem();
        var storage = MakeStorage();
        var slot = storage.GetIndexedItemSlot(0)!;
        Assert.True(inv.ServerAdd(new TestItem("first"), slot));

        var second = new TestItem("second");
        Assert.False(inv.ServerAdd(second, slot));
        Assert.Equal("first", slot.Item!.Name);
        Assert.Null(second.ItemSlot);
    }

    [Fact]
    public void Rule2_AddToOccupiedSlot_DropOtherDropsOccupantFirst()
    {
        var (inv, world) = MakeSystem();
        var storage = MakeStorage();
        var slot = storage.GetIndexedItemSlot(0)!;
        var occupant = new TestItem("occupant");
        inv.ServerAdd(occupant, slot);

        var incoming = new TestItem("incoming");
        Assert.True(inv.ServerAdd(incoming, slot, ReplacementStrategy.DropOther));
        Assert.Contains(occupant, world.Dropped);
        Assert.Same(incoming, slot.Item);
        Assert.Null(occupant.ItemSlot);
    }

    [Fact]
    public void Rule2_AddToOccupiedSlot_DespawnOtherDestroysOccupantFirst()
    {
        var (inv, world) = MakeSystem();
        var storage = MakeStorage();
        var slot = storage.GetIndexedItemSlot(0)!;
        var occupant = new TestItem("occupant");
        inv.ServerAdd(occupant, slot);

        Assert.True(inv.ServerAdd(new TestItem("incoming"), slot, ReplacementStrategy.DespawnOther));
        Assert.Contains(occupant, world.Despawned);
    }

    [Fact]
    public void Rule2_BuckledItemCannotBePickedUp()
    {
        var (inv, _) = MakeSystem();
        var slot = MakeStorage().GetIndexedItemSlot(0)!;
        Assert.False(inv.ServerAdd(new TestItem("chair-guy") { IsBuckled = true }, slot));
        Assert.True(slot.IsEmpty);
    }

    [Fact]
    public void Rule2_ItemAlreadyInASlotCannotBeAdded()
    {
        var (inv, _) = MakeSystem();
        var storage = MakeStorage();
        var item = new TestItem("item");
        inv.ServerAdd(item, storage.GetIndexedItemSlot(0)!);

        Assert.False(inv.ServerAdd(item, storage.GetIndexedItemSlot(1)!));
        Assert.Same(storage.GetIndexedItemSlot(0), item.ItemSlot);
    }

    [Fact]
    public void Rule2_IgnoreRestraintsBypassesCapacity()
    {
        var (inv, _) = MakeSystem();
        var storage = MakeStorage(capacity: new RejectAllCapacity());
        var slot = storage.GetIndexedItemSlot(0)!;
        var item = new TestItem("oversized");

        Assert.False(inv.ServerAdd(item, slot));
        Assert.True(inv.ServerAdd(item, slot, ignoreRestraints: true));
        Assert.Same(item, slot.Item);
    }

    // --- Rule 3: The Lock ---

    [Fact]
    public void Rule3_LockedSlotNeverReleasesItsItem()
    {
        var (inv, world) = MakeSystem();
        var storage = MakeStorage();
        var slot = storage.GetIndexedItemSlot(0)!;
        var item = new TestItem("cursed-sword");
        inv.ServerAdd(item, slot);
        slot.ItemNotRemovable = true;

        Assert.False(inv.ServerDrop(slot));
        Assert.False(inv.ServerDespawn(slot));
        Assert.False(inv.ServerVanish(slot));
        Assert.False(inv.ServerThrow(slot, new Vector2(3, 0)));
        Assert.False(inv.ServerTransfer(slot, storage.GetIndexedItemSlot(1)!));
        Assert.Same(item, slot.Item);
        Assert.Empty(world.Dropped);
        Assert.Empty(world.Despawned);
    }

    [Fact]
    public void Rule3_OccupiedLockedDestinationRejectsTransfer()
    {
        var (inv, _) = MakeSystem();
        var storage = MakeStorage();
        var from = storage.GetIndexedItemSlot(0)!;
        var to = storage.GetIndexedItemSlot(1)!;
        inv.ServerAdd(new TestItem("moving"), from);
        inv.ServerAdd(new TestItem("locked-in"), to);
        to.ItemNotRemovable = true;

        Assert.False(inv.ServerTransfer(from, to, ReplacementStrategy.DropOther));
        Assert.Equal("locked-in", to.Item!.Name);
    }

    // --- Rule 4: No Inventory Loops ---

    [Fact]
    public void Rule4_StorageCannotGoInsideItself()
    {
        var (inv, _) = MakeSystem();
        var backpackItem = new TestItem("backpack");
        var backpackStorage = MakeStorage(asPickupable: backpackItem, name: "backpack");
        backpackItem.Storages.Add(backpackStorage);

        Assert.False(inv.ServerAdd(backpackItem, backpackStorage.GetIndexedItemSlot(0)!));
    }

    [Fact]
    public void Rule4_StorageCannotGoInsideItsOwnDescendant()
    {
        var (inv, _) = MakeSystem();
        var outerItem = new TestItem("outer-bag");
        var outerStorage = MakeStorage(asPickupable: outerItem, name: "outer");
        outerItem.Storages.Add(outerStorage);

        var innerItem = new TestItem("inner-bag");
        var innerStorage = MakeStorage(asPickupable: innerItem, name: "inner");
        innerItem.Storages.Add(innerStorage);

        // inner bag goes inside outer bag: fine
        Assert.True(inv.ServerAdd(innerItem, outerStorage.GetIndexedItemSlot(0)!));
        // outer bag into the inner bag would loop the hierarchy: rejected
        Assert.False(inv.ServerAdd(outerItem, innerStorage.GetIndexedItemSlot(0)!));
    }

    // --- Rule 5: Stacking Wins Over Replacement ---

    [Fact]
    public void Rule5_TransferOntoCompatibleStackMerges()
    {
        var (inv, _) = MakeSystem();
        var storage = MakeStorage();
        var from = storage.GetIndexedItemSlot(0)!;
        var to = storage.GetIndexedItemSlot(1)!;

        var targetStack = new TestStack(10);
        var target = new TestItem("metal x10") { Stackable = targetStack };
        inv.ServerAdd(target, to);

        var incoming = new TestItem("metal x5") { Stackable = new TestStack(5) };
        inv.ServerAdd(incoming, from);

        Assert.True(inv.ServerTransfer(from, to));
        Assert.True(targetStack.CombineCalled);
        Assert.Equal(15, targetStack.Amount);
        // target keeps its slot identity — no replacement happened
        Assert.Same(target, to.Item);
    }

    [Fact]
    public void Rule5_OccupiedSlotCanFitOnlyViaStacking()
    {
        var (inv, _) = MakeSystem();
        var slot = MakeStorage().GetIndexedItemSlot(0)!;
        inv.ServerAdd(new TestItem("occupant") { Stackable = new TestStack(1) }, slot);

        Assert.False(slot.CanFit(new TestItem("plain")));
        Assert.True(slot.CanFit(new TestItem("stack") { Stackable = new TestStack(1) }));
    }

    // --- Rule 6: Throw Ballistics ---

    [Fact]
    public void Rule6_ThrowDistanceClampedToThrowRange()
    {
        Assert.Equal(7f, InventorySystem.ClampThrowDistance(100f, 7f));
        Assert.Equal(3f, InventorySystem.ClampThrowDistance(3f, 7f));
    }

    [Fact]
    public void Rule6_AirtimeFormula()
    {
        // sticky movement: full flight time d/v
        Assert.Equal(1f, InventorySystem.ComputeThrowAirTime(6f, 6f, stickyMovement: true), 5);
        // friction eats the tail: d/v - v/(2*15) = 1 - 0.2 = 0.8
        Assert.Equal(0.8f, InventorySystem.ComputeThrowAirTime(6f, 6f, stickyMovement: false), 5);
    }

    [Fact]
    public void Rule6_ThrowUsesClampedAirtimeAndAim()
    {
        var (inv, world) = MakeSystem();
        var slot = MakeStorage().GetIndexedItemSlot(0)!;
        var item = new TestItem("toolbox") { ThrowSpeed = 6f, ThrowRange = 7f };
        inv.ServerAdd(item, slot);

        Assert.True(inv.ServerThrow(slot, new Vector2(100f, 0f), BodyPartType.Head));
        var thrown = Assert.Single(world.Thrown);
        Assert.Same(item, thrown.Item);
        Assert.Equal(new Vector2(1f, 0f), thrown.Trajectory);
        Assert.Equal(BodyPartType.Head, thrown.Aim);
        // clamped distance 7 at speed 6: 7/6 - 6/30
        Assert.Equal(7f / 6f - 0.2f, thrown.AirTime, 5);
    }

    [Fact]
    public void Rule6_ThrowInsideContainerDegradesToContainerDrop()
    {
        var (inv, world) = MakeSystem();
        world.HolderInContainer = true;
        var slot = MakeStorage().GetIndexedItemSlot(0)!;
        var item = new TestItem("toolbox");
        inv.ServerAdd(item, slot);

        Assert.True(inv.ServerThrow(slot, new Vector2(3f, 0f)));
        Assert.Empty(world.Thrown);
        Assert.Empty(world.Dropped);
        Assert.Contains(item, world.StoredInContainer);
        Assert.True(slot.IsEmpty);
    }

    // --- Rule 7: Deterministic Slot Order ---

    [Fact]
    public void Rule7_NextEmptySlotFollowsDeclarationOrder()
    {
        var (inv, _) = MakeSystem();
        var storage = new ItemStorage(
            new[] { SlotIdentifier.Named(NamedSlot.back), SlotIdentifier.Indexed(0), SlotIdentifier.Indexed(1) },
            new AcceptAllCapacity());

        Assert.Same(storage.GetNamedItemSlot(NamedSlot.back), storage.GetNextEmptySlot());
        inv.ServerAdd(new TestItem("bag"), storage.GetNamedItemSlot(NamedSlot.back)!);
        Assert.Same(storage.GetIndexedItemSlot(0), storage.GetNextEmptySlot());
        Assert.Same(storage.GetIndexedItemSlot(0), storage.GetNextFreeIndexedSlot());
    }

    // --- Cross-cutting behaviors ---

    [Fact]
    public void Transfer_MovesItemBetweenStoragesAndFiresHooks()
    {
        var (inv, _) = MakeSystem();
        var player = MakeStorage(name: "player");
        var box = MakeStorage(name: "box");
        var hook = new RecordingHook();
        var item = new TestItem("wrench");
        item.Hooks.Add(hook);

        var hand = player.GetIndexedItemSlot(0)!;
        var boxSlot = box.GetIndexedItemSlot(0)!;
        inv.ServerAdd(item, hand);
        Assert.True(inv.ServerTransfer(hand, boxSlot));

        Assert.True(hand.IsEmpty);
        Assert.Same(item, boxSlot.Item);
        Assert.Same(boxSlot, item.ItemSlot);
        Assert.Equal(InventoryMoveType.Transfer, hook.Moves.Last().InventoryMoveType);
    }

    [Fact]
    public void Despawn_EmptiesSlotAndDestroysItem()
    {
        var (inv, world) = MakeSystem();
        var slot = MakeStorage().GetIndexedItemSlot(0)!;
        var item = new TestItem("trash");
        inv.ServerAdd(item, slot);

        Assert.True(inv.ServerDespawn(slot));
        Assert.True(slot.IsEmpty);
        Assert.Null(item.ItemSlot);
        Assert.Contains(item, world.Despawned);
    }

    [Fact]
    public void Swap_ExchangesItemsViaWorldRoundTrip()
    {
        var (inv, world) = MakeSystem();
        var storage = MakeStorage();
        var slotOne = storage.GetIndexedItemSlot(0)!;
        var slotTwo = storage.GetIndexedItemSlot(1)!;
        var itemOne = new TestItem("one");
        var itemTwo = new TestItem("two");
        inv.ServerAdd(itemOne, slotOne);
        inv.ServerAdd(itemTwo, slotTwo);

        inv.ServerSwap(slotOne, slotTwo);

        Assert.Same(itemTwo, slotOne.Item);
        Assert.Same(itemOne, slotTwo.Item);
        // upstream swaps by dropping both then re-adding
        Assert.Equal(2, world.Dropped.Count);
    }

    [Fact]
    public void VanishStackable_NonStackableReturnsTheRemovedItem()
    {
        // Regression for the upstream bug where the slot was read after being emptied.
        var (inv, world) = MakeSystem();
        var slot = MakeStorage().GetIndexedItemSlot(0)!;
        var item = new TestItem("gear");
        inv.ServerAdd(item, slot);

        var removed = inv.ServerVanishStackable(slot);
        Assert.Same(item, removed);
        Assert.True(slot.IsEmpty);
        Assert.Contains(item, world.Vanished);
    }

    [Fact]
    public void Consume_StackableConsumesAmount_NonStackableDespawns()
    {
        var (inv, world) = MakeSystem();
        var storage = MakeStorage();
        var stackSlot = storage.GetIndexedItemSlot(0)!;
        var stack = new TestStack(10);
        inv.ServerAdd(new TestItem("rods") { Stackable = stack }, stackSlot);

        Assert.True(inv.ServerConsume(stackSlot, 4));
        Assert.Equal(6, stack.Amount);
        Assert.False(inv.ServerConsume(stackSlot, 100));

        var plainSlot = storage.GetIndexedItemSlot(1)!;
        var plain = new TestItem("apple");
        inv.ServerAdd(plain, plainSlot);
        Assert.True(inv.ServerConsume(plainSlot, 1));
        Assert.Contains(plain, world.Despawned);
    }

    [Fact]
    public void SlotTree_IncludesNestedStorageSlots()
    {
        var (inv, _) = MakeSystem();
        var player = MakeStorage(indexedSlots: 2, name: "player");
        var backpackItem = new TestItem("backpack");
        var backpackStorage = MakeStorage(indexedSlots: 3, asPickupable: backpackItem, name: "backpack");
        backpackItem.Storages.Add(backpackStorage);

        inv.ServerAdd(backpackItem, player.GetIndexedItemSlot(0)!);

        Assert.Equal(5, player.GetItemSlotTree().Count());
        Assert.Same(player, backpackStorage.GetRootStorage());
        Assert.Same(player, backpackStorage.GetIndexedItemSlot(0)!.GetRootStorage());
    }
}
