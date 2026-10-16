using System.Collections.Generic;

namespace Godotstation.Common.Interactions;

/// <summary>Network-safe visible contents for one inventory slot.</summary>
public readonly record struct InventorySlotSnapshot(
    int SlotId,
    bool IsOccupied,
    int ItemId,
    string ItemName,
    string ItemIconPath,
    string ItemDescription,
    int StackAmount = 1)
{
    public static InventorySlotSnapshot Empty(int slotId) =>
        new(slotId, false, 0, string.Empty, string.Empty, string.Empty, 0);
}

/// <summary>Authoritative reply for a predicted inventory transfer.</summary>
public readonly record struct InventoryTransferReconciliation(
    long RequestId,
    bool Accepted,
    InventorySlotSnapshot Source,
    InventorySlotSnapshot Destination);

/// <summary>
/// Presentation-only transfer prediction. It never changes engine inventory state;
/// authoritative snapshots supplied during reconciliation always replace predictions.
/// </summary>
public sealed class InventoryTransferPredictor
{
    private readonly Dictionary<int, InventorySlotSnapshot> authoritativeSlots = new();
    private readonly Dictionary<int, InventorySlotSnapshot> predictedSlots = new();
    private readonly Dictionary<long, PendingTransfer> pendingTransfers = new();
    private readonly Dictionary<int, long> pendingSlots = new();

    public bool TryBegin(long requestId, int fromSlotId, int toSlotId,
        InventorySlotSnapshot source, InventorySlotSnapshot destination)
    {
        if (requestId <= 0 || fromSlotId == toSlotId || source.SlotId != fromSlotId ||
            destination.SlotId != toSlotId || !source.IsOccupied || destination.IsOccupied ||
            pendingTransfers.ContainsKey(requestId) || pendingSlots.ContainsKey(fromSlotId) ||
            pendingSlots.ContainsKey(toSlotId))
        {
            return false;
        }

        authoritativeSlots[fromSlotId] = source;
        authoritativeSlots[toSlotId] = destination;
        var predictedSource = InventorySlotSnapshot.Empty(fromSlotId);
        var predictedDestination = source with { SlotId = toSlotId };
        pendingTransfers.Add(requestId, new PendingTransfer(fromSlotId, toSlotId));
        pendingSlots.Add(fromSlotId, requestId);
        pendingSlots.Add(toSlotId, requestId);
        predictedSlots[fromSlotId] = predictedSource;
        predictedSlots[toSlotId] = predictedDestination;
        return true;
    }

    public bool TryGetDisplayedSlot(int slotId, out InventorySlotSnapshot snapshot)
    {
        if (predictedSlots.TryGetValue(slotId, out snapshot)) return true;
        return authoritativeSlots.TryGetValue(slotId, out snapshot);
    }

    /// <summary>Replaces both affected views with server snapshots and clears the prediction.</summary>
    public bool Reconcile(InventoryTransferReconciliation reconciliation)
    {
        if (!pendingTransfers.TryGetValue(reconciliation.RequestId, out var pending) ||
            reconciliation.Source.SlotId != pending.SourceSlotId ||
            reconciliation.Destination.SlotId != pending.DestinationSlotId)
        {
            return false;
        }

        pendingTransfers.Remove(reconciliation.RequestId);
        pendingSlots.Remove(pending.SourceSlotId);
        pendingSlots.Remove(pending.DestinationSlotId);
        predictedSlots.Remove(pending.SourceSlotId);
        predictedSlots.Remove(pending.DestinationSlotId);
        authoritativeSlots[pending.SourceSlotId] = reconciliation.Source;
        authoritativeSlots[pending.DestinationSlotId] = reconciliation.Destination;
        return true;
    }

    private readonly record struct PendingTransfer(int SourceSlotId, int DestinationSlotId);
}
