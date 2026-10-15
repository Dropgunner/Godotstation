namespace Godotstation.Engine.Inventory;

/// <summary>
/// Kind of slot identifier.
/// </summary>
public enum SlotIdentifierType
{
    /// <summary>Identified by a <see cref="NamedSlot"/> enum value.</summary>
    Named,
    /// <summary>Identified by an index.</summary>
    Indexed,
}

/// <summary>
/// Uniquely identifies a slot within a particular ItemStorage. Only unique within that
/// storage — the same identifier in two storages refers to two different slots.
///
/// The Unity original pooled these as reference types so identical identifiers compared
/// equal by reference; a readonly record struct gives the same observable semantics
/// (value equality, cheap dictionary keys) without global mutable pools.
/// </summary>
public readonly record struct SlotIdentifier
{
    /// <summary>Named slot if this is a Named identifier, else null.</summary>
    public NamedSlot? NamedSlot { get; }

    /// <summary>Index of this slot if this is an Indexed identifier, else -1.</summary>
    public int SlotIndex { get; }

    /// <summary>Type of this slot identifier.</summary>
    public SlotIdentifierType SlotIdentifierType { get; }

    private SlotIdentifier(NamedSlot? namedSlot, int slotIndex, SlotIdentifierType type)
    {
        NamedSlot = namedSlot;
        SlotIndex = slotIndex;
        SlotIdentifierType = type;
    }

    /// <summary>Gets the slot identifier for a particular named slot.</summary>
    public static SlotIdentifier Named(NamedSlot namedSlot) =>
        new(namedSlot, -1, SlotIdentifierType.Named);

    /// <summary>Gets the slot identifier for a particular indexed slot.</summary>
    public static SlotIdentifier Indexed(int slotIndex) =>
        new(null, slotIndex, SlotIdentifierType.Indexed);

    public override string ToString() =>
        $"{nameof(NamedSlot)}: {NamedSlot}, {nameof(SlotIndex)}: {SlotIndex}, {nameof(SlotIdentifierType)}: {SlotIdentifierType}";
}

