using System;
using System.Collections.Generic;
using System.Linq;

namespace Godotstation.Engine.Inventory;

/// <summary>
/// Allows an entity to store items. ANYTHING which can contain items has one of these —
/// players (player inventory IS storage), backpacks, crates. Items stored in an ItemStorage
/// can themselves have ItemStorage (a backpack in a player's inventory), forming a tree.
///
/// This is the pure state container + queries; all mutation goes through
/// <see cref="InventorySystem"/>. Slots iterate in declaration order, so "first empty
/// slot" queries are deterministic (Fidelity Contract Rule 7 — the Unity original
/// iterated a HashSet, which is unspecified order).
/// </summary>
public sealed class ItemStorage
{
    /// <summary>Display/debug name of this storage.</summary>
    public string Name { get; }

    /// <summary>Capacity policy — what each slot is allowed to hold.</summary>
    public IItemStorageCapacity Capacity { get; }

    /// <summary>
    /// Storage item categories this container refuses to hold. Defaults to none.
    /// </summary>
    public StorageItemName DenyStorageOfStorageItems { get; set; }

    /// <summary>
    /// The pickupable identity of this storage if it is itself an item (a backpack),
    /// null if it can never sit in a slot (a player, a wall locker). Used for root
    /// resolution and hierarchy-loop detection.
    /// </summary>
    public IPickupable? AsPickupable { get; }

    // Declaration order preserved for deterministic first-empty queries (Rule 7).
    private readonly List<SlotIdentifier> definedOrder = new();
    private readonly HashSet<SlotIdentifier> definedSlots = new();

    // Slot pool: the single canonical ItemSlot instance per identifier (Rule 1).
    private readonly Dictionary<SlotIdentifier, ItemSlot> slotPool = new();

    /// <summary>
    /// Raised when any slot in this storage has its item set. Arguments are
    /// (removed item, new item), either may be null.
    /// </summary>
    public event Action<IPickupable?, IPickupable?>? ServerInventoryItemSlotSet;

    public ItemStorage(IEnumerable<SlotIdentifier> slots, IItemStorageCapacity capacity,
        IPickupable? asPickupable = null, string name = "storage")
    {
        Name = name;
        Capacity = capacity;
        AsPickupable = asPickupable;
        foreach (var slot in slots)
        {
            if (definedSlots.Add(slot))
            {
                definedOrder.Add(slot);
            }
        }
    }

    internal void OnInventorySlotSet(IPickupable? prevItem, IPickupable? newItem) =>
        ServerInventoryItemSlotSet?.Invoke(prevItem, newItem);

    /// <summary>Number of slots this storage defines.</summary>
    public int StorageSize() => definedSlots.Count;

    /// <summary>True iff this storage defines the slot with the specified identifier.</summary>
    public bool HasSlot(SlotIdentifier slotIdentifier) => definedSlots.Contains(slotIdentifier);

    /// <summary>True iff this storage defines the slot with the specified name.</summary>
    public bool HasSlot(NamedSlot namedSlot) => HasSlot(SlotIdentifier.Named(namedSlot));

    /// <summary>
    /// Change the set of available slots in the storage. Slots no longer defined become
    /// unreachable via the getters; occupied ones keep their contents until moved out
    /// through a stale reference (mirrors upstream AcceptNewStructure).
    /// </summary>
    public void AcceptNewStructure(IEnumerable<SlotIdentifier> newSlots)
    {
        definedOrder.Clear();
        definedSlots.Clear();
        foreach (var slot in newSlots)
        {
            if (definedSlots.Add(slot))
            {
                definedOrder.Add(slot);
            }
        }
    }

    /// <summary>
    /// The canonical slot for this identifier, null if this storage doesn't define it.
    /// </summary>
    public ItemSlot? GetItemSlot(SlotIdentifier identifier)
    {
        if (HasSlot(identifier) == false) return null;

        if (slotPool.TryGetValue(identifier, out var slot) == false)
        {
            slot = new ItemSlot(this, identifier);
            slotPool.Add(identifier, slot);
        }

        return slot;
    }

    public ItemSlot? GetNamedItemSlot(NamedSlot named) => GetItemSlot(SlotIdentifier.Named(named));

    public ItemSlot? GetIndexedItemSlot(int slotIndex) => GetItemSlot(SlotIdentifier.Indexed(slotIndex));

    /// <summary>
    /// All item slots defined in this particular storage, in declaration order. Does NOT
    /// recurse into stored items' own storages — use <see cref="GetItemSlotTree"/> for that.
    /// </summary>
    public IEnumerable<ItemSlot> GetItemSlots()
    {
        foreach (var identifier in definedOrder)
        {
            yield return GetItemSlot(identifier)!;
        }
    }

    public IEnumerable<ItemSlot> GetIndexedSlots() =>
        GetItemSlots().Where(its => its.SlotIdentifier.SlotIdentifierType == SlotIdentifierType.Indexed);

    public IEnumerable<ItemSlot> GetNamedItemSlots() =>
        GetItemSlots().Where(its => its.SlotIdentifier.SlotIdentifierType == SlotIdentifierType.Named);

    /// <summary>First defined slot with no item, null if all occupied.</summary>
    public ItemSlot? GetNextEmptySlot() => GetItemSlots().FirstOrDefault(slot => slot.Item == null);

    /// <summary>Returns the preferred fitting slot selected by the supplied trait policy.</summary>
    public ItemSlot? GetBestSlotFor(IPickupable? item, BestSlotForTrait traitPolicy) =>
        traitPolicy.GetBestSlot(item, this);

    /// <summary>First free indexed slot, null if none.</summary>
    public ItemSlot? GetNextFreeIndexedSlot() => GetIndexedSlots().FirstOrDefault(its => its.Item == null);

    /// <summary>First free named slot, null if none.</summary>
    public ItemSlot? GetNextFreeNamedSlot() => GetNamedItemSlots().FirstOrDefault(its => its.Item == null);

    /// <summary>Last (highest declared) occupied indexed slot, null if none occupied.</summary>
    public ItemSlot? GetTopOccupiedIndexedSlot() => GetIndexedSlots().LastOrDefault(ids => ids.Item != null);

    /// <summary>First occupied slot, null if none.</summary>
    public ItemSlot? GetFirstOccupiedSlot() => GetItemSlots().FirstOrDefault(slot => slot.Item != null);

    public List<ItemSlot> GetOccupiedSlots() => GetItemSlots().Where(slot => slot.IsOccupied).ToList();

    public bool HasAnyOccupied() => GetItemSlots().Any(slot => slot.IsOccupied);

    /// <summary>The slot in this storage holding the given item, null if not here.</summary>
    public ItemSlot? GetSlotFromItem(IPickupable item) =>
        GetItemSlots().FirstOrDefault(slot => ReferenceEquals(slot.Item, item));

    /// <summary>
    /// The top-level ItemStorage containing this storage — if this is a crate inside a
    /// backpack, returns the crate's storage. Returns this if not inside anything.
    /// </summary>
    public ItemStorage GetRootStorage()
    {
        var storage = this;
        var pickupable = storage.AsPickupable;
        while (pickupable?.ItemSlot != null)
        {
            storage = pickupable.ItemSlot.ItemStorage;
            pickupable = storage.AsPickupable;
        }

        return storage;
    }

    /// <summary>
    /// All item slots in this and all contained item storages — every slot that exists
    /// somewhere in the hierarchy of storage contained in this storage.
    /// </summary>
    public IEnumerable<ItemSlot> GetItemSlotTree() => GetItemSlots().SelectMany(SlotSubtree);

    /// <summary>The given slot plus, if it holds a storage item, all slots inside it (recursively).</summary>
    public static IEnumerable<ItemSlot> SlotSubtree(ItemSlot slot)
    {
        if (slot.Item == null)
        {
            return new[] { slot };
        }

        var nested = slot.Item.ItemStorages.SelectMany(storage => storage.GetItemSlotTree());
        return nested.Concat(new[] { slot });
    }

    /// <summary>This storage plus every storage nested anywhere inside it.</summary>
    public IEnumerable<ItemStorage> GetItemStorageTree()
    {
        var storages = new List<ItemStorage> { this };
        foreach (var slot in GetItemSlots())
        {
            if (slot.Item == null) continue;
            foreach (var nested in slot.Item.ItemStorages)
            {
                storages.AddRange(nested.GetItemStorageTree());
            }
        }

        return storages;
    }
}

