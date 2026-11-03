using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Entities;
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

    private static StationEntityState Girder(SharedStationInventoryWorld world) =>
        world.GetState(StationEntityCatalog.GirderId)!;

    private static StationEntityState Metal(SharedStationInventoryWorld world) =>
        world.GetState(StationEntityCatalog.MetalSheetsId)!;

    [Fact]
    public void CrewMemberUsesTwoHeldSheetsForFourSecondsAndReplicatesCompletedWallState()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(7, new Vector2(1, 1)));
        var published = new List<StationEntityState>();
        world.EntitiesChanged += changed => published.AddRange(changed);

        Assert.True(world.TryInteract(7, StationEntityCatalog.MetalSheetsId, Intent.Help, false));
        Assert.Equal("metal sheets", world.GetHandItemName(7, InventoryNamedSlot.rightHand));
        Assert.True(world.TryInteract(7, StationEntityCatalog.GirderId, Intent.Help, false));
        Assert.Equal(7, Girder(world).Count(StationEntityField.Worker));

        world.AdvanceConstruction(7, 3.99);
        Assert.False(Girder(world).Flag(StationEntityField.Plated));
        Assert.Equal(3.99, Girder(world).Get(StationEntityField.Progress), 6);
        world.AdvanceConstruction(7, 0.01);

        Assert.True(Girder(world).Flag(StationEntityField.Plated));
        Assert.Equal(0, Metal(world).Count(StationEntityField.Amount));
        Assert.Equal(string.Empty, world.GetHandItemName(7, InventoryNamedSlot.rightHand));
        Assert.Equal(Girder(world), published.Last(state => state.Id == StationEntityCatalog.GirderId));
        Assert.Equal(Metal(world), published.Last(state => state.Id == StationEntityCatalog.MetalSheetsId));
    }

    [Fact]
    public void AssistantCanPerformSourcedConstructionWhenHoldingEnoughMaterial()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(3, new Vector2(1, 1)));

        Assert.True(world.TryInteract(3, StationEntityCatalog.MetalSheetsId, Intent.Help, false));
        Assert.True(world.TryInteract(3, StationEntityCatalog.GirderId, Intent.Help, false));
        Assert.Equal(3, Girder(world).Count(StationEntityField.Worker));
        world.AdvanceConstruction(3, 4);
        Assert.True(Girder(world).Flag(StationEntityField.Plated));
        Assert.Equal(0, Metal(world).Count(StationEntityField.Amount));
    }

    [Fact]
    public void MovingOrSwitchingHandsInterruptsAndResetsTheConstructionAction()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(4, new Vector2(1, 1)));
        Assert.True(world.TryInteract(4, StationEntityCatalog.MetalSheetsId, Intent.Help, false));
        Assert.True(world.TryInteract(4, StationEntityCatalog.GirderId, Intent.Help, false));
        world.AdvanceConstruction(4, 1.5);
        Assert.True(world.SetActorPosition(4, new Vector2(5, 5)));
        world.AdvanceConstruction(4, 0.1);

        Assert.Equal(0, Girder(world).Count(StationEntityField.Worker));
        Assert.Equal(0, Girder(world).Get(StationEntityField.Progress));
        Assert.Equal(2, Metal(world).Count(StationEntityField.Amount));

        Assert.True(world.SetActorPosition(4, new Vector2(1, 1)));
        Assert.True(world.TryInteract(4, StationEntityCatalog.GirderId, Intent.Help, false));
        Assert.True(world.TryToggleActiveHand(4));
        world.AdvanceConstruction(4, 0.1);
        Assert.Equal(0, Girder(world).Count(StationEntityField.Worker));
        Assert.Equal(0, Girder(world).Get(StationEntityField.Progress));
    }

    [Fact]
    public void BlockedGirderClickCannotStartConstruction()
    {
        var world = new SharedStationInventoryWorld(new GirderBlockedMap());
        Assert.True(world.AddActor(5, new Vector2(1, 1)));
        Assert.True(world.TryInteract(5, StationEntityCatalog.MetalSheetsId, Intent.Help, false));

        Assert.False(world.TryInteract(5, StationEntityCatalog.GirderId, Intent.Help, false));
        Assert.Equal(0, Girder(world).Count(StationEntityField.Worker));
        Assert.Equal(2, Metal(world).Count(StationEntityField.Amount));
        Assert.False(Girder(world).Flag(StationEntityField.Plated));
    }
}
