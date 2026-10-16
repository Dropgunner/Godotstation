using Godotstation.Common.Interactions;
using Xunit;

namespace Godotstation.Tests.Inventory;

public sealed class InventoryTransferPredictionTests
{
    private static InventorySlotSnapshot Item(int slotId, int itemId, int stackAmount = 1) =>
        new(slotId, true, itemId, $"item-{itemId}", $"icons/{itemId}.png", "description", stackAmount);

    [Fact]
    public void PredictionMovesPresentationAndAcceptedReconciliationUsesServerSnapshots()
    {
        var predictor = new InventoryTransferPredictor();
        var source = Item(4, 20, stackAmount: 5);
        var destination = InventorySlotSnapshot.Empty(9);

        Assert.True(predictor.TryBegin(101, 4, 9, source, destination));
        Assert.True(predictor.TryGetDisplayedSlot(4, out var predictedSource));
        Assert.False(predictedSource.IsOccupied);
        Assert.True(predictor.TryGetDisplayedSlot(9, out var predictedDestination));
        Assert.Equal(20, predictedDestination.ItemId);
        Assert.Equal(5, predictedDestination.StackAmount);
        Assert.Equal(4, source.SlotId);
        Assert.Equal(9, destination.SlotId);

        var serverSource = InventorySlotSnapshot.Empty(4);
        var serverDestination = Item(9, 20, stackAmount: 5);
        Assert.True(predictor.Reconcile(new InventoryTransferReconciliation(
            101, true, serverSource, serverDestination)));
        Assert.True(predictor.TryGetDisplayedSlot(4, out var reconciledSource));
        Assert.Equal(serverSource, reconciledSource);
        Assert.True(predictor.TryGetDisplayedSlot(9, out var reconciledDestination));
        Assert.Equal(serverDestination, reconciledDestination);
    }

    [Fact]
    public void RejectedReconciliationRestoresAuthoritativeStateAndIgnoresMismatches()
    {
        var predictor = new InventoryTransferPredictor();
        var source = Item(1, 55);
        var destination = InventorySlotSnapshot.Empty(2);
        Assert.True(predictor.TryBegin(202, 1, 2, source, destination));

        Assert.False(predictor.Reconcile(new InventoryTransferReconciliation(
            999, true, InventorySlotSnapshot.Empty(1), InventorySlotSnapshot.Empty(2))));
        Assert.False(predictor.Reconcile(new InventoryTransferReconciliation(
            202, false, InventorySlotSnapshot.Empty(3), InventorySlotSnapshot.Empty(2))));
        Assert.True(predictor.TryGetDisplayedSlot(1, out var stillPredicted));
        Assert.False(stillPredicted.IsOccupied);

        Assert.True(predictor.Reconcile(new InventoryTransferReconciliation(
            202, false, source, destination)));
        Assert.True(predictor.TryGetDisplayedSlot(1, out var restoredSource));
        Assert.Equal(source, restoredSource);
        Assert.True(predictor.TryGetDisplayedSlot(2, out var restoredDestination));
        Assert.Equal(destination, restoredDestination);
    }

    [Fact]
    public void PredictionRejectsInvalidOrOverlappingTransfers()
    {
        var predictor = new InventoryTransferPredictor();
        var source = Item(1, 60);
        var destination = InventorySlotSnapshot.Empty(2);

        Assert.False(predictor.TryBegin(0, 1, 2, source, destination));
        Assert.False(predictor.TryBegin(1, 1, 2, InventorySlotSnapshot.Empty(1), destination));
        Assert.False(predictor.TryBegin(2, 1, 2, source, Item(2, 61)));
        Assert.True(predictor.TryBegin(3, 1, 2, source, destination));
        Assert.False(predictor.TryBegin(4, 1, 5, source, InventorySlotSnapshot.Empty(5)));
    }
}
