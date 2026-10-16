using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Godotstation.Engine.Inventory;
using Xunit;

namespace Godotstation.Tests.Inventory;

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

    private sealed class TestStorageItem : TestItem, IStorageIdentifier
    {
        public TestStorageItem(string name, StorageItemName storageItemName) : base(name) =>
            StorageItemName = storageItemName;

        public StorageItemName StorageItemName { get; }
    }

    private sealed class TraitTestItem : TestItem, IItemTraitProvider
    {
        private readonly HashSet<string> traits;

        public TraitTestItem(string name, params string[] traits) : base(name) =>
            this.traits = new HashSet<string>(traits, StringComparer.Ordinal);

        public bool HasTrait(string traitId) => traits.Contains(traitId);
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
        public List<string> Events { get; } = new List<string>();
        public List<IPickupable> Despawned { get; } = new List<IPickupable>();
        public List<IPickupable> Dropped { get; } = new List<IPickupable>();
        public List<IPickupable> StoredInContainer { get; } = new List<IPickupable>();
        public List<IPickupable> Vanished { get; } = new List<IPickupable>();
        public List<IPickupable> DisappearedFromWorld { get; } = new List<IPickupable>();
        public List<(IPickupable Item, Vector2 Trajectory, float Speed, float AirTime, BodyPartType Aim)> Thrown { get; }
            = new List<(IPickupable, Vector2, float, float, BodyPartType)>();

        public bool IsHolderInContainer(ItemSlot fromSlot) => HolderInContainer;
        public void DisappearFromWorld(IPickupable item)
        {
            Events.Add("disappear");
            DisappearedFromWorld.Add(item);
        }
        public void Despawn(IPickupable item) => Despawned.Add(item);
        public void Drop(IPickupable item, ItemSlot fromSlot, Vector2? worldTargetVector)
        {
            Events.Add("drop");
            Dropped.Add(item);
        }
        public void StoreInHolderContainer(IPickupable item, ItemSlot fromSlot) => StoredInContainer.Add(item);
        public void Throw(IPickupable item, ItemSlot fromSlot, Vector2 trajectory, float throwSpeed,
            float airTime, BodyPartType aim) => Thrown.Add((item, trajectory, throwSpeed, airTime, aim));
        public void Vanish(IPickupable item) => Vanished.Add(item);
    }

    private sealed class RecordingObserverTransport : IInventoryObserverTransport
    {
        public List<(string ObserverId, InventorySlotUpdate Update)> Updates { get; } = new();
        public void Send(string observerId, InventorySlotUpdate update) => Updates.Add((observerId, update));
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

    private static ItemStorage MakeNestedStorage(InventorySystem inventory, int nestedBags)
    {
        var current = MakeStorage(indexedSlots: 1, name: "player");
        for (var i = 0; i < nestedBags; i++)
        {
            var bag = new TestItem($"bag-{i}");
            var bagStorage = MakeStorage(indexedSlots: 1, asPickupable: bag, name: $"bag-storage-{i}");
            bag.Storages.Add(bagStorage);
            Assert.True(inventory.ServerAdd(bag, current.GetIndexedItemSlot(0)!));
            current = bagStorage;
        }

        return current;
    }

    // --- Rule 1: Slot Identity ---

    [Fact]
    public void NamedSlot_SerializedOrdinalsPreserveGapAndNone()
    {
        Assert.Equal(
            new[] { 0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
                21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 50, 51, 52, 2048 },
            Enum.GetValues<NamedSlot>().Select(slot => (int)slot));
        Assert.False(Enum.IsDefined((NamedSlot)4));
        Assert.Equal(2048, (int)NamedSlot.none);
    }

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
    public void Rule2_AddToOccupiedSlot_DropOtherRunsBeforeIncomingAddSideEffects()
    {
        var (inv, world) = MakeSystem();
        var storage = MakeStorage();
        var slot = storage.GetIndexedItemSlot(0)!;
        inv.ServerAdd(new TestItem("occupant"), slot);
        world.Events.Clear();
        slot.OnSlotContentsChangeServer += () => world.Events.Add("slot-change");

        Assert.True(inv.ServerAdd(new TestItem("incoming"), slot, ReplacementStrategy.DropOther));
        Assert.Equal(new[] { "slot-change", "drop", "disappear", "slot-change" }, world.Events);
    }

    [Fact]
    public void Rule2_AddToNullDestinationIsRejected()
    {
        var (inv, world) = MakeSystem();
        var item = new TestItem("loose-item");

        Assert.False(inv.ServerAdd(item, null!));
        Assert.Null(item.ItemSlot);
        Assert.Empty(world.DisappearedFromWorld);
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

    [Fact]
    public void Rule3_SwapWithLockedSlotLeavesBothItemsInPlace()
    {
        var (inv, world) = MakeSystem();
        var storage = MakeStorage();
        var first = storage.GetIndexedItemSlot(0)!;
        var second = storage.GetIndexedItemSlot(1)!;
        var firstItem = new TestItem("locked");
        var secondItem = new TestItem("other");
        inv.ServerAdd(firstItem, first);
        inv.ServerAdd(secondItem, second);
        first.ItemNotRemovable = true;

        inv.ServerSwap(first, second);

        Assert.Same(firstItem, first.Item);
        Assert.Same(secondItem, second.Item);
        Assert.Empty(world.Dropped);
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

    [Fact]
    public void Rule4_FiveAncestorLimitAllowsFiveLevelsAndRejectsSix()
    {
        var (inv, _) = MakeSystem();
        var fiveLevelTarget = MakeNestedStorage(inv, nestedBags: 4).GetIndexedItemSlot(0)!;
        var sixLevelTarget = MakeNestedStorage(inv, nestedBags: 5).GetIndexedItemSlot(0)!;

        Assert.True(fiveLevelTarget.CanFit(new TestItem("fits-at-five")));
        Assert.False(sixLevelTarget.CanFit(new TestItem("rejected-at-six")));
    }

    // --- Rule 5: Stacking Wins Over Replacement ---

    [Fact]
    public void Rule5_TransferOntoCompatibleStackMerges()
    {
        var (inv, world) = MakeSystem();
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
        Assert.Null(from.Item);
        Assert.Null(incoming.ItemSlot);
        Assert.Contains(incoming, world.Despawned);
    }

    [Fact]
    public void Rule5_AddOntoCompatibleStackMergesWithoutReplacingTarget()
    {
        var (inv, world) = MakeSystem();
        var slot = MakeStorage().GetIndexedItemSlot(0)!;
        var targetStack = new TestStack(10);
        var target = new TestItem("metal x10") { Stackable = targetStack };
        var incoming = new TestItem("metal x5") { Stackable = new TestStack(5) };
        inv.ServerAdd(target, slot);
        world.DisappearedFromWorld.Clear();

        Assert.True(inv.ServerAdd(incoming, slot, ReplacementStrategy.DropOther));

        Assert.Same(target, slot.Item);
        Assert.Equal(15, targetStack.Amount);
        Assert.Null(incoming.ItemSlot);
        Assert.DoesNotContain(incoming, world.DisappearedFromWorld);
        Assert.Empty(world.Dropped);
        Assert.Contains(incoming, world.Despawned);
    }

    [Fact]
    public void Rule5_ConsumeUpdatesSlotAndDespawnsWhenStackIsEmpty()
    {
        var (inv, world) = MakeSystem();
        var slot = MakeStorage().GetIndexedItemSlot(0)!;
        var stack = new TestStack(3);
        var item = new TestItem("metal") { Stackable = stack };
        inv.ServerAdd(item, slot);
        var updates = 0;
        slot.OnSlotContentsChangeServer += () => updates++;

        Assert.True(inv.ServerConsume(slot, 2));
        Assert.Equal(1, stack.Amount);
        Assert.Same(item, slot.Item);
        Assert.Equal(1, updates);
        Assert.True(inv.ServerConsume(slot, 1));
        Assert.Null(slot.Item);
        Assert.Null(item.ItemSlot);
        Assert.Contains(item, world.Despawned);
    }

    [Fact]
    public void VanishStackable_SplitsOneAndNotifiesRemainingStack()
    {
        var (inv, world) = MakeSystem();
        var slot = MakeStorage().GetIndexedItemSlot(0)!;
        var stack = new TestStack(3);
        var item = new TestItem("metal") { Stackable = stack };
        inv.ServerAdd(item, slot);
        var updates = 0;
        slot.OnSlotContentsChangeServer += () => updates++;

        var removed = inv.ServerVanishStackable(slot);

        Assert.NotNull(removed);
        Assert.NotSame(item, removed);
        Assert.Equal(2, stack.Amount);
        Assert.Same(item, slot.Item);
        Assert.Equal(1, updates);
        Assert.Contains(removed, world.Vanished);
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

    [Fact]
    public void Rule8_DeniedStorageCategoryCannotFitOrBeAdded()
    {
        var (inv, world) = MakeSystem();
        var storage = MakeStorage(indexedSlots: 1);
        storage.DenyStorageOfStorageItems = StorageItemName.Backpack | StorageItemName.Belt;
        var slot = storage.GetIndexedItemSlot(0)!;
        var backpack = new TestStorageItem("backpack", StorageItemName.Backpack);

        Assert.False(slot.CanFit(backpack));
        Assert.False(inv.ServerAdd(backpack, slot));
        Assert.True(slot.IsEmpty);
        Assert.Null(backpack.ItemSlot);
        Assert.Empty(world.DisappearedFromWorld);
    }

    [Fact]
    public void Rule8_DeniedStorageCategoryCannotMergeIntoOccupiedStack()
    {
        var (inv, world) = MakeSystem();
        var storage = MakeStorage(indexedSlots: 1);
        storage.DenyStorageOfStorageItems = StorageItemName.Backpack;
        var slot = storage.GetIndexedItemSlot(0)!;
        var existing = new TestItem("existing stack") { Stackable = new TestStack(2) };
        var incoming = new TestStorageItem("backpack", StorageItemName.Backpack)
            { Stackable = new TestStack(3) };
        Assert.True(inv.ServerAdd(existing, slot));

        Assert.False(inv.ServerAdd(incoming, slot));
        Assert.Same(existing, slot.Item);
        Assert.Null(incoming.ItemSlot);
        Assert.Empty(world.Despawned);
    }

    [Fact]
    public void Rule8_DeniedStorageCategoryCannotTransferIntoDestination()
    {
        var (inv, _) = MakeSystem();
        var source = MakeStorage(indexedSlots: 1);
        var destination = MakeStorage(indexedSlots: 1);
        destination.DenyStorageOfStorageItems = StorageItemName.Backpack;
        var item = new TestStorageItem("backpack", StorageItemName.Backpack);
        var sourceSlot = source.GetIndexedItemSlot(0)!;
        var destinationSlot = destination.GetIndexedItemSlot(0)!;
        Assert.True(inv.ServerAdd(item, sourceSlot));

        Assert.False(inv.ServerTransfer(sourceSlot, destinationSlot));
        Assert.Same(item, sourceSlot.Item);
        Assert.Null(destinationSlot.Item);
        Assert.Same(sourceSlot, item.ItemSlot);
    }

    [Fact]
    public void Rule8_StorageCategoryFlagOrdinalsMatchSource()
    {
        Assert.Equal(new[] { 0, 1, 2, 4, 8, 16, 32, 64, 128, 256 },
            Enum.GetValues<StorageItemName>().Select(value => (int)value));
    }

    [Fact]
    public void Rule8_AllowedAndUnidentifiedItemsIgnoreDenyFlags()
    {
        var storage = MakeStorage(indexedSlots: 3);
        storage.DenyStorageOfStorageItems = StorageItemName.Backpack;

        Assert.True(storage.GetIndexedItemSlot(0)!.CanFit(
            new TestStorageItem("toolbox", StorageItemName.ToolBox)));
        Assert.True(storage.GetIndexedItemSlot(1)!.CanFit(
            new TestStorageItem("unclassified storage", StorageItemName.None)));
        Assert.True(storage.GetIndexedItemSlot(2)!.CanFit(new TestItem("ordinary item")));
    }

    [Fact]
    public void Rule8_DenyFlagsApplyOnlyToTheDestinationStorage()
    {
        var outer = MakeStorage(indexedSlots: 1);
        var innerItem = new TestStorageItem("bag", StorageItemName.Backpack);
        var inner = MakeStorage(indexedSlots: 1, asPickupable: innerItem);
        innerItem.Storages.Add(inner);
        outer.DenyStorageOfStorageItems = StorageItemName.Backpack;

        Assert.True(inner.GetIndexedItemSlot(0)!.CanFit(
            new TestStorageItem("nested-bag", StorageItemName.Backpack)));
    }

    [Fact]
    public void Rule8_IgnoreRestraintsBypassesStorageDenyLikeOtherFitConstraints()
    {
        var (inv, _) = MakeSystem();
        var storage = MakeStorage(indexedSlots: 1);
        storage.DenyStorageOfStorageItems = StorageItemName.Backpack;
        var backpack = new TestStorageItem("backpack", StorageItemName.Backpack);

        Assert.True(inv.ServerAdd(backpack, storage.GetIndexedItemSlot(0)!,
            ignoreRestraints: true));
    }

    [Fact]
    public void Rule9_BestSlotUsesOrderedMatchingTraitPreferences()
    {
        var orderedStorage = new ItemStorage(new[]
        {
            SlotIdentifier.Named(NamedSlot.belt), SlotIdentifier.Named(NamedSlot.back)
        }, new AcceptAllCapacity());
        var item = new TraitTestItem("tools", "tool");
        var policy = new BestSlotForTrait(new[]
        {
            new TraitSlotMapping("medical", NamedSlot.back),
            new TraitSlotMapping("tool", NamedSlot.back),
            new TraitSlotMapping("tool", NamedSlot.belt),
        });

        Assert.Same(orderedStorage.GetNamedItemSlot(NamedSlot.back), orderedStorage.GetBestSlotFor(item, policy));
    }

    [Fact]
    public void Rule9_BestSlotContinuesWhenPreferredSlotCannotFit()
    {
        var back = SlotIdentifier.Named(NamedSlot.back);
        var belt = SlotIdentifier.Named(NamedSlot.belt);
        var storage = new ItemStorage(new[] { back, SlotIdentifier.Indexed(0), belt }, new RejectBackCapacity());
        var item = new TraitTestItem("medical kit", "medical");
        var policy = new BestSlotForTrait(new[]
        {
            new TraitSlotMapping("medical", NamedSlot.back),
            new TraitSlotMapping("medical", NamedSlot.belt),
        });

        Assert.Same(storage.GetNamedItemSlot(NamedSlot.belt), policy.GetBestSlot(item, storage));
    }

    [Fact]
    public void Rule9_BestSlotFallbackUsesDeclaredOrderAndSkipsHandSlots()
    {
        var identifiers = new[]
        {
            SlotIdentifier.Named(NamedSlot.leftHand),
            SlotIdentifier.Indexed(0),
            SlotIdentifier.Named(NamedSlot.rightHand),
            SlotIdentifier.Named(NamedSlot.back),
        };
        var storage = new ItemStorage(identifiers, new AcceptAllCapacity());
        var item = new TestItem("ordinary item");
        var policy = new BestSlotForTrait(new[]
        {
            new TraitSlotMapping("tool", NamedSlot.belt),
        });

        Assert.Same(storage.GetIndexedItemSlot(0), policy.GetBestSlot(item, storage));
    }

    [Fact]
    public void Rule9_BestSlotDoesNotApplyPreferencesWithoutTraitProvider()
    {
        var storage = new ItemStorage(new[]
        {
            SlotIdentifier.Named(NamedSlot.back), SlotIdentifier.Named(NamedSlot.belt)
        }, new AcceptAllCapacity());
        var item = new TestItem("ordinary item");
        var policy = new BestSlotForTrait(new[]
        {
            new TraitSlotMapping(null, NamedSlot.belt),
        });

        Assert.Same(storage.GetNamedItemSlot(NamedSlot.back), policy.GetBestSlot(item, storage));
    }

    [Fact]
    public void Rule9_CatchAllMappingMatchesItemsWithTraitData()
    {
        var storage = new ItemStorage(new[]
        {
            SlotIdentifier.Named(NamedSlot.back), SlotIdentifier.Named(NamedSlot.belt)
        }, new AcceptAllCapacity());
        var item = new TraitTestItem("unclassified item");
        var policy = new BestSlotForTrait(new[]
        {
            new TraitSlotMapping(null, NamedSlot.belt),
        });

        Assert.Same(storage.GetNamedItemSlot(NamedSlot.belt), policy.GetBestSlot(item, storage));
    }

    [Fact]
    public void Rule9_BestSlotReturnsNullWhenNothingCanFit()
    {
        var storage = new ItemStorage(new[] { SlotIdentifier.Named(NamedSlot.back) }, new RejectAllCapacity());
        var policy = new BestSlotForTrait(new[] { new TraitSlotMapping(null, NamedSlot.back) });

        Assert.Null(policy.GetBestSlot(new TestItem("item"), storage));
        Assert.Null(policy.GetBestSlot(null, storage));
        Assert.Null(policy.GetBestSlot(new TestItem("item"), null));
    }

    [Fact]
    public void Rule10_ServerPopulateSpawnsInOrderAndUsesInventoryAdd()
    {
        var (inventory, world) = MakeSystem();
        var storage = MakeStorage(indexedSlots: 2, capacity: new RejectAllCapacity());
        var first = new TestItem("first");
        var second = new TestItem("second");
        var populator = new TestPopulator();
        populator.Entries.Add(new InventorySpawnEntry("first"));
        populator.Entries.Add(new InventorySpawnEntry("second", SlotIdentifier.Indexed(1)));
        var spawner = new TestSpawner();
        spawner.Items.Add("first", first);
        spawner.Items.Add("second", second);

        var added = inventory.ServerPopulate(storage, new InventoryPopulationContext(true, false),
            spawner, populator);

        Assert.Equal(2, added);
        Assert.Equal(new[] { "first", "second" }, spawner.SpawnedIds);
        Assert.Same(first, storage.GetIndexedItemSlot(0)!.Item);
        Assert.Same(second, storage.GetIndexedItemSlot(1)!.Item);
        Assert.Equal(new IPickupable[] { first, second }, world.DisappearedFromWorld);
        Assert.True(storage.ContentsPopulated);
    }

    [Fact]
    public void Rule10_ServerPopulateHonorsSpawnAndManualFlagsAndRunsOnlyOnce()
    {
        var (inventory, _) = MakeSystem();
        var storage = MakeStorage(indexedSlots: 1);
        storage.ManuallySpawnContent = true;
        var populator = new TestPopulator();
        populator.Entries.Add(new InventorySpawnEntry("item"));
        var spawner = new TestSpawner();
        spawner.Items.Add("item", new TestItem("item"));

        Assert.Equal(0, inventory.ServerPopulate(storage,
            new InventoryPopulationContext(false, true), spawner, populator));
        Assert.Equal(0, inventory.ServerPopulate(storage,
            new InventoryPopulationContext(true, true), spawner, populator));
        var manualStorage = MakeStorage(indexedSlots: 1);
        manualStorage.ManuallySpawnContent = true;
        Assert.Equal(0, inventory.ServerPopulate(manualStorage,
            new InventoryPopulationContext(true, false), spawner, populator));

        Assert.Equal(0, populator.CallCount);
        Assert.Empty(spawner.SpawnedIds);
    }

    [Fact]
    public void Rule10_ServerPopulateSkipsMissingTargetsAndFailedSpawns()
    {
        var (inventory, _) = MakeSystem();
        var storage = MakeStorage(indexedSlots: 1);
        var populator = new TestPopulator();
        populator.Entries.Add(new InventorySpawnEntry("missing-item"));
        populator.Entries.Add(new InventorySpawnEntry("item", SlotIdentifier.Indexed(5)));
        var spawner = new TestSpawner();
        spawner.Items.Add("item", new TestItem("item"));

        Assert.Equal(0, inventory.ServerPopulate(storage,
            new InventoryPopulationContext(true, false), spawner, populator));
        Assert.Empty(storage.GetOccupiedSlots());
        Assert.Equal(new[] { "missing-item", "item" }, spawner.SpawnedIds);
    }

    [Fact]
    public void Rule11_ObserversReceiveInitialStateAndAuthoritativeChanges()
    {
        var (inventory, _) = MakeSystem();
        var transport = new RecordingObserverTransport();
        inventory.ObserverTransport = transport;
        var storage = MakeStorage(indexedSlots: 1);
        var slot = storage.GetIndexedItemSlot(0)!;
        var item = new TestItem("observed");

        inventory.ServerAddObserver(slot, "client-a");
        Assert.Single(transport.Updates);
        Assert.Same(storage, transport.Updates[0].Update.Storage);
        Assert.Equal(slot.SlotIdentifier, transport.Updates[0].Update.Slot);
        Assert.Null(transport.Updates[0].Update.Item);

        Assert.True(inventory.ServerAdd(item, slot));
        slot.ItemNotRemovable = true;

        Assert.Equal(3, transport.Updates.Count);
        Assert.Same(item, transport.Updates[1].Update.Item);
        Assert.False(transport.Updates[1].Update.ItemNotRemovable);
        Assert.Same(item, transport.Updates[2].Update.Item);
        Assert.True(transport.Updates[2].Update.ItemNotRemovable);
    }

    [Fact]
    public void Rule11_RemovingObserverClearsViewAndStopsFurtherUpdates()
    {
        var (inventory, _) = MakeSystem();
        var transport = new RecordingObserverTransport();
        inventory.ObserverTransport = transport;
        var slot = MakeStorage(indexedSlots: 1).GetIndexedItemSlot(0)!;
        var item = new TestItem("observed");

        inventory.ServerAdd(item, slot);
        inventory.ServerAddObserver(slot, "client-a");
        inventory.ServerAddObserver(slot, "client-b");
        Assert.Equal(2, transport.Updates.Count);

        inventory.ServerRemoveObserver(slot, "client-a");
        var clear = Assert.Single(transport.Updates,
            update => update.ObserverId == "client-a" && update.Update.ClearItem);
        Assert.True(clear.Update.ClearItem);
        Assert.Null(clear.Update.Item);

        Assert.True(inventory.ServerDespawn(slot));
        Assert.Equal(2, transport.Updates.Count(update => update.ObserverId == "client-a"));
        Assert.Equal(2, transport.Updates.Count(update => update.ObserverId == "client-b"));
        Assert.Null(transport.Updates.Last(update => update.ObserverId == "client-b").Update.Item);
    }

    [Fact]
    public void Rule11_ObserverRegistrationIsIdempotentAndTransportCanBeAbsent()
    {
        var (inventory, _) = MakeSystem();
        var storage = MakeStorage(indexedSlots: 1);
        var slot = storage.GetIndexedItemSlot(0)!;
        inventory.ServerAddObserver(slot, "client-a");
        Assert.True(inventory.ServerAdd(new TestItem("quiet"), slot));

        var transport = new RecordingObserverTransport();
        inventory.ObserverTransport = transport;
        inventory.ServerAddObserver(slot, "client-a");
        inventory.ServerAddObserver(slot, "client-a");
        Assert.Equal(2, transport.Updates.Count);

        Assert.True(inventory.ServerDespawn(slot));
        Assert.Equal(3, transport.Updates.Count);
    }

    private sealed class RejectBackCapacity : IItemStorageCapacity
    {
        public bool CanFit(IPickupable toCheck, SlotIdentifier inSlot) =>
            inSlot.NamedSlot != NamedSlot.back;
    }

    private sealed class TestPopulator : IItemStoragePopulator
    {
        public List<InventorySpawnEntry> Entries { get; } = new();
        public int CallCount { get; private set; }

        public IEnumerable<InventorySpawnEntry> Populate(InventoryPopulationContext context)
        {
            CallCount++;
            return Entries;
        }
    }

    private sealed class TestSpawner : IInventoryItemSpawner
    {
        public Dictionary<string, IPickupable> Items { get; } = new(StringComparer.Ordinal);
        public List<string> SpawnedIds { get; } = new();

        public IPickupable? Spawn(string itemId, InventoryPopulationContext context)
        {
            SpawnedIds.Add(itemId);
            return Items.GetValueOrDefault(itemId);
        }
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

    [Fact]
    public void Rule6_NegativeThrowAirtimePassesThroughToWorld()
    {
        var (inv, world) = MakeSystem();
        var slot = MakeStorage().GetIndexedItemSlot(0)!;
        var item = new TestItem("fast-item") { ThrowSpeed = 10f, ThrowRange = 7f };
        inv.ServerAdd(item, slot);

        Assert.True(inv.ServerThrow(slot, new Vector2(1f, 0f)));

        var thrown = Assert.Single(world.Thrown);
        Assert.True(thrown.AirTime < 0f);
        Assert.Equal(0.1f - (10f / 30f), thrown.AirTime, 5);
    }

    [Fact]
    public void Rule6_NormalDropInsideContainerStoresItemInContainer()
    {
        var (inv, world) = MakeSystem();
        world.HolderInContainer = true;
        var slot = MakeStorage().GetIndexedItemSlot(0)!;
        var item = new TestItem("dropped-inside-locker");
        inv.ServerAdd(item, slot);

        Assert.True(inv.ServerDrop(slot));

        Assert.Empty(world.Dropped);
        Assert.Contains(item, world.StoredInContainer);
        Assert.True(slot.IsEmpty);
        Assert.Null(item.ItemSlot);
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
