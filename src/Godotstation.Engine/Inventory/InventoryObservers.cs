namespace Godotstation.Engine.Inventory;

/// <summary>Current server state for one slot, sent to a slot observer.</summary>
public sealed record InventorySlotUpdate(
    ItemStorage Storage,
    SlotIdentifier Slot,
    IPickupable? Item,
    bool ItemNotRemovable,
    bool ClearItem = false);

/// <summary>Frontend/network seam for delivering authoritative slot updates.</summary>
public interface IInventoryObserverTransport
{
    void Send(string observerId, InventorySlotUpdate update);
}
