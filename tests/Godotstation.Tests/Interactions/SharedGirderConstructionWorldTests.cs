using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Interactions;
using Godotstation.Engine.Inventory;
using InventoryNamedSlot = Godotstation.Engine.Inventory.NamedSlot;
using Xunit;

namespace Godotstation.Tests.Interactions;

public sealed class SharedGirderConstructionWorldTests
{
    private sealed class GirderBlockedMap : IReachabilityMap
    {
        public bool IsPassableForClick(Vector2 fromWorldPos, Vector2 toWorldPos, bool isServer,
            WorldEntity? context = null) => toWorldPos != new Vector2(2, 2);
    }

    [Fact]
    public void CrewMemberUsesTwoHeldSheetsForFourSecondsAndReplicatesCompletedWallState()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(7, new Vector2(1, 1)));
        SharedStationInventoryWorld.StationConstructionSnapshot? published = null;
        world.ConstructionStateChanged += snapshot => published = snapshot;

        Assert.True(world.TryInteract(7, SharedStationInventoryWorld.MetalSheetsId, Intent.Help, false));
        Assert.Equal("metal sheets", world.GetHandItemName(7, InventoryNamedSlot.rightHand));
        Assert.True(world.TryInteract(7, SharedStationInventoryWorld.GirderId, Intent.Help, false));
        Assert.Equal(7, world.ConstructionSnapshot.WorkerPeerId);

        world.AdvanceConstruction(7, 3.99);
        Assert.False(world.ConstructionSnapshot.GirderPlated);
        Assert.Equal(3.99, world.ConstructionSnapshot.ProgressSeconds, 6);
        world.AdvanceConstruction(7, 0.01);

        Assert.True(world.ConstructionSnapshot.GirderPlated);
        Assert.Equal(0, world.ConstructionSnapshot.MetalSheetCount);
        Assert.Equal(string.Empty, world.GetHandItemName(7, InventoryNamedSlot.rightHand));
        Assert.Equal(world.ConstructionSnapshot, published);
    }

    [Fact]
    public void AssistantCanPerformSourcedConstructionWhenHoldingEnoughMaterial()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(3, new Vector2(1, 1)));

        Assert.True(world.TryInteract(3, SharedStationInventoryWorld.MetalSheetsId, Intent.Help, false));
        Assert.True(world.TryInteract(3, SharedStationInventoryWorld.GirderId, Intent.Help, false));
        Assert.Equal(3, world.ConstructionSnapshot.WorkerPeerId);
        world.AdvanceConstruction(3, 4);
        Assert.True(world.ConstructionSnapshot.GirderPlated);
        Assert.Equal(0, world.ConstructionSnapshot.MetalSheetCount);
    }

    [Fact]
    public void MovingOrSwitchingHandsInterruptsAndResetsTheConstructionAction()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(4, new Vector2(1, 1)));
        Assert.True(world.TryInteract(4, SharedStationInventoryWorld.MetalSheetsId, Intent.Help, false));
        Assert.True(world.TryInteract(4, SharedStationInventoryWorld.GirderId, Intent.Help, false));
        world.AdvanceConstruction(4, 1.5);
        Assert.True(world.SetActorPosition(4, new Vector2(5, 5)));
        world.AdvanceConstruction(4, 0.1);

        Assert.Null(world.ConstructionSnapshot.WorkerPeerId);
        Assert.Equal(0, world.ConstructionSnapshot.ProgressSeconds);
        Assert.Equal(2, world.ConstructionSnapshot.MetalSheetCount);

        Assert.True(world.SetActorPosition(4, new Vector2(1, 1)));
        Assert.True(world.TryInteract(4, SharedStationInventoryWorld.GirderId, Intent.Help, false));
        Assert.True(world.TryToggleActiveHand(4));
        world.AdvanceConstruction(4, 0.1);
        Assert.Null(world.ConstructionSnapshot.WorkerPeerId);
        Assert.Equal(0, world.ConstructionSnapshot.ProgressSeconds);
    }

    [Fact]
    public void BlockedGirderClickCannotStartConstruction()
    {
        var world = new SharedStationInventoryWorld(new GirderBlockedMap());
        Assert.True(world.AddActor(5, new Vector2(1, 1)));
        Assert.True(world.TryInteract(5, SharedStationInventoryWorld.MetalSheetsId, Intent.Help, false));

        Assert.False(world.TryInteract(5, SharedStationInventoryWorld.GirderId, Intent.Help, false));
        Assert.Null(world.ConstructionSnapshot.WorkerPeerId);
        Assert.Equal(2, world.ConstructionSnapshot.MetalSheetCount);
        Assert.False(world.ConstructionSnapshot.GirderPlated);
    }
}
