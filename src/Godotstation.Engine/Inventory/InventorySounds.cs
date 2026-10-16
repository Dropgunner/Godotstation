namespace Godotstation.Engine.Inventory;

/// <summary>Provides the source inventory-remove sound identifier for an item.</summary>
public interface IInventorySoundProvider
{
    string? InventoryRemoveSoundId { get; }
}

/// <summary>Frontend seam for playing an inventory sound at its source slot.</summary>
public interface IInventorySoundTransport
{
    void PlayAtSource(string soundId, ItemSlot sourceSlot);
}
