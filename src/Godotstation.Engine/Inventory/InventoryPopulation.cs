using System.Collections.Generic;

namespace Godotstation.Engine.Inventory;

/// <summary>Spawn flags supplied to inventory population providers and item spawners.</summary>
public readonly record struct InventoryPopulationContext(bool SpawnItems, bool SpawnManualContents);

/// <summary>A configured item spawn and its optional target slot.</summary>
public readonly record struct InventorySpawnEntry(
    string ItemId,
    SlotIdentifier? Slot = null,
    ReplacementStrategy ReplacementStrategy = ReplacementStrategy.Cancel);

/// <summary>Produces initial item entries for a storage, in population order.</summary>
public interface IItemStoragePopulator
{
    IEnumerable<InventorySpawnEntry> Populate(InventoryPopulationContext context);
}

/// <summary>Frontend seam that creates an item instance from a content identifier.</summary>
public interface IInventoryItemSpawner
{
    IPickupable? Spawn(string itemId, InventoryPopulationContext context);
}
