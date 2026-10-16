using System.Collections.Generic;
using System.Linq;

namespace Godotstation.Engine.Inventory;

/// <summary>Optional item traits used to select preferred named inventory slots.</summary>
public interface IItemTraitProvider
{
    bool HasTrait(string traitId);
}

/// <summary>An ordered preference mapping from an optional trait to a named slot.</summary>
public readonly record struct TraitSlotMapping(string? TraitId, NamedSlot Slot);

/// <summary>
/// Selects the first fit from ordered trait preferences, then the first usable slot in
/// storage declaration order. Left and right hand slots are excluded from fallback.
/// </summary>
public sealed class BestSlotForTrait
{
    private static readonly HashSet<NamedSlot> FallbackExcludedSlots =
        new() { NamedSlot.leftHand, NamedSlot.rightHand };

    private readonly IReadOnlyList<TraitSlotMapping> mappings;

    public BestSlotForTrait(IEnumerable<TraitSlotMapping> mappings)
    {
        this.mappings = mappings.ToArray();
    }

    /// <summary>
    /// Returns the first matching preferred slot that can fit the item. If no trait
    /// provider is present or no preference fits, returns the first fit fallback slot.
    /// Returns null when inputs are null or no eligible slot can fit the item.
    /// </summary>
    public ItemSlot? GetBestSlot(IPickupable? item, ItemStorage? storage)
    {
        if (item == null || storage == null) return null;

        if (item is IItemTraitProvider traitProvider)
        {
            foreach (var mapping in mappings)
            {
                if (mapping.TraitId != null && traitProvider.HasTrait(mapping.TraitId) == false)
                    continue;

                var preferredSlot = storage.GetNamedItemSlot(mapping.Slot);
                if (preferredSlot?.CanFit(item) == true) return preferredSlot;
            }
        }

        return storage.GetItemSlots().FirstOrDefault(slot =>
            (slot.NamedSlot == null || FallbackExcludedSlots.Contains(slot.NamedSlot.Value) == false) &&
            slot.CanFit(item));
    }
}
