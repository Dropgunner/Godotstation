using System;

namespace Godotstation.Engine.Inventory;

/// <summary>
/// Represents a slot which can store an item, belonging to a particular <see cref="ItemStorage"/>.
///
/// Unlike <see cref="SlotIdentifier"/>, it's an actual instance of a slot in an instance of
/// a storage. There is only ever ONE ItemSlot instance for a given (storage, identifier) pair
/// (Fidelity Contract Rule 1) — the storage pools its slots, so reference identity is safe
/// to rely on.
/// </summary>
public sealed class ItemSlot
{
    /// <summary>ItemStorage which contains this slot.</summary>
    public ItemStorage ItemStorage { get; }

    /// <summary>Identifier of this slot within its item storage.</summary>
    public SlotIdentifier SlotIdentifier { get; }

    /// <summary>Item in this slot, null if empty.</summary>
    public IPickupable? Item { get; private set; }

    /// <summary>
    /// Dictates whether the item can be removed by any means (Fidelity Contract Rule 3).
    /// </summary>
    public bool ItemNotRemovable { get; set; }

    /// <summary>Marks the slot as unable to take anything (used by UI).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Named slot this item slot is identified by in this storage. Null if indexed.</summary>
    public NamedSlot? NamedSlot => SlotIdentifier.NamedSlot;

    /// <summary>True if the slot has no item.</summary>
    public bool IsEmpty => Item == null;

    /// <summary>True if the slot has an item.</summary>
    public bool IsOccupied => !IsEmpty;

    /// <summary>Invoked after the contents of this slot change.</summary>
    public event Action? OnSlotContentsChangeServer;

    internal ItemSlot(ItemStorage itemStorage, SlotIdentifier slotIdentifier)
    {
        ItemStorage = itemStorage;
        SlotIdentifier = slotIdentifier;
    }

    /// <summary>
    /// Gets the specified slot from the specified storage. Null if the storage does not
    /// define a slot with the given identifier.
    /// </summary>
    public static ItemSlot? Get(ItemStorage itemStorage, SlotIdentifier slotIdentifier) =>
        itemStorage.GetItemSlot(slotIdentifier);

    /// <summary>Gets the specified named slot. Null if the storage doesn't define it.</summary>
    public static ItemSlot? GetNamed(ItemStorage? itemStorage, NamedSlot named) =>
        itemStorage?.GetItemSlot(SlotIdentifier.Named(named));

    /// <summary>Gets the specified indexed slot. Null if the storage doesn't define it.</summary>
    public static ItemSlot? GetIndexed(ItemStorage itemStorage, int slotIndex) =>
        itemStorage.GetItemSlot(SlotIdentifier.Indexed(slotIndex));

    /// <summary>
    /// Convenience method for getting a slot when it's not known if it is indexed or named.
    /// If slotIndex == -1, gets it as a named slot, otherwise as an indexed slot.
    /// </summary>
    public static ItemSlot? Get(ItemStorage itemStorage, NamedSlot namedSlot, int slotIndex) =>
        slotIndex == -1 ? GetNamed(itemStorage, namedSlot) : GetIndexed(itemStorage, slotIndex);

    /// <summary>Named slot as its flagged-enum equivalent (2^ordinal).</summary>
    public static NamedSlotFlagged GetFlaggedSlot(NamedSlot slot) =>
        (NamedSlotFlagged)(1 << (int)slot);

    /// <summary>
    /// The top-level ItemStorage containing this slot — if this slot is inside a crate in a
    /// backpack, returns the crate's storage.
    /// </summary>
    public ItemStorage GetRootStorage() => ItemStorage.GetRootStorage();

    /// <summary>
    /// NOTE: Use <see cref="InventorySystem"/> for moving inventory around instead.
    /// Sets the item in this slot, firing change events. Does nothing besides record that
    /// the item is now in the slot — the caller is responsible for the rest of the move.
    /// </summary>
    public void ServerSetItem(IPickupable? newItem)
    {
        var removedItem = Item;
        Item = newItem;
        OnSlotContentsChangeServer?.Invoke();
        ItemStorage.OnInventorySlotSet(removedItem, newItem);
    }

    /// <summary>
    /// NOTE: Use <see cref="InventorySystem"/> for moving inventory around instead.
    /// Removes the current item from this slot, firing change events.
    /// </summary>
    public void ServerRemoveItem() => ServerSetItem(null);

    /// <summary>
    /// Checks if the indicated item can fit in this slot: slot enabled, no inventory
    /// hierarchy loop (Rule 4), stacking rules when occupied (Rule 5), and the storage's
    /// capacity policy.
    /// </summary>
    /// <param name="ignoreOccupied">if true, does not check if an item is already in the slot</param>
    public bool CanFit(IPickupable? toStore, bool ignoreOccupied = false)
    {
        if (toStore == null) return false;
        if (IsEnabled == false) return false;

        // Walk this slot's ancestors and make sure none of them ARE toStore, as that would
        // create a loop in the inventory hierarchy (putting the storage inside itself).
        // The upstream implementation aborts after 5 levels; preserved (Rule 4).
        var storageToCheck = (ItemStorage?)ItemStorage;
        var count = 0;
        while (storageToCheck != null)
        {
            if (ReferenceEquals(storageToCheck.AsPickupable, toStore)) return false;

            storageToCheck = storageToCheck.AsPickupable?.ItemSlot?.ItemStorage;

            count++;
            if (count > 5) return false;
        }

        // If the slot already has an item, it's allowed to "fit" only if the incoming item
        // can stack with the existing item.
        if (ignoreOccupied == false && Item != null)
        {
            var thisStackable = Item.Stackable;
            var otherStackable = toStore.Stackable;
            return thisStackable != null && otherStackable != null &&
                   thisStackable.CanAccommodate(toStore);
        }

        // No item in slot and no inventory loop created — defer to the storage's
        // specific capacity logic.
        return ItemStorage.Capacity.CanFit(toStore, SlotIdentifier);
    }

    public override string ToString() =>
        $"storage {ItemStorage.Name}, slot {SlotIdentifier}, contains " + (Item?.Name ?? "nothing");
}

