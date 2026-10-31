using System.Collections.Generic;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Interactions;
using Godotstation.Engine.Inventory;
using Godotstation.Engine.Rounds;
using InventoryNamedSlot = Godotstation.Engine.Inventory.NamedSlot;
using Xunit;

namespace Godotstation.Tests.Interactions;

public sealed class SharedStationInventoryWorldTests
{
    private sealed class FullyBlockedMap : IReachabilityMap
    {
        public bool IsPassableForClick(Vector2 fromWorldPos, Vector2 toWorldPos, bool isServer,
            WorldEntity? context = null) => false;
    }

    private static StationEntityState Wrench(SharedStationInventoryWorld world) =>
        world.GetState(StationEntityCatalog.WrenchId)!;

    private static StationEntityState Fuel(SharedStationInventoryWorld world) =>
        world.GetState(StationEntityCatalog.GeneratorFuelId)!;

    private static StationEntityState Generator(SharedStationInventoryWorld world) =>
        world.GetState(StationEntityCatalog.FuelGeneratorId)!;

    [Fact]
    public void PickupPlacesWrenchInActiveHandAndPublishesOwner()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        IReadOnlyList<StationEntityState>? published = null;
        world.EntitiesChanged += changed => published = changed;

        Assert.True(world.TryInteract(1, StationEntityCatalog.WrenchId, Intent.Help, false));

        Assert.Equal("wrench", world.GetHandItemName(1, InventoryNamedSlot.rightHand));
        Assert.Equal(string.Empty, world.GetHandItemName(1, InventoryNamedSlot.leftHand));
        Assert.Equal(1, Wrench(world).Holder);
        Assert.Equal(Wrench(world), Assert.Single(published!));
    }

    [Fact]
    public void ToolUseRequiresTheWrenchInTheSelectedHandAndDropReturnsItToWorld()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        Assert.True(world.AddActor(2, new Vector2(-1, 2)));
        Assert.True(world.TryInteract(1, StationEntityCatalog.WrenchId, Intent.Help, false));

        Assert.True(world.TryToggleActiveHand(1));
        Assert.False(world.TryInteract(1, StationEntityCatalog.ServiceCartId, Intent.Help, false));
        Assert.False(world.TryDropActiveHand(1));
        Assert.Equal(1, Wrench(world).Holder);
        Assert.True(world.TryToggleActiveHand(1));
        Assert.True(world.TryInteract(1, StationEntityCatalog.ServiceCartId, Intent.Help, false));
        Assert.False(world.GetState(StationEntityCatalog.ServiceCartId)!.Flag(StationEntityField.Anchored));

        Assert.True(world.SetActorPosition(1, new Vector2(2, 3)));
        Assert.False(world.TryDropActiveHand(2));
        Assert.True(world.TryDropActiveHand(1));
        Assert.Null(Wrench(world).Holder);
        Assert.Equal(new Vector2(2, 3), Wrench(world).Position);
        Assert.Equal(string.Empty, world.GetHandItemName(1, InventoryNamedSlot.rightHand));
        Assert.True(world.SetActorPosition(2, new Vector2(2, 2)));
        Assert.True(world.TryInteract(2, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.Equal(2, Wrench(world).Holder);
    }

    [Fact]
    public void OutOfRangeBlockedAndUnknownRequestsLeaveInventoryAndWorldUnchanged()
    {
        var world = new SharedStationInventoryWorld(new FullyBlockedMap());
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        Assert.True(world.AddActor(2, new Vector2(20, 20)));
        IReadOnlyList<StationEntityState> before = world.CaptureAll();

        Assert.False(world.TryInteract(1, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.False(world.TryInteract(2, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.False(world.TryInteract(1, 123456, Intent.Help, false));
        Assert.False(world.TryDropActiveHand(1));

        Assert.Null(world.GetActiveHand(99));
        Assert.Equal(string.Empty, world.GetHandItemName(1, InventoryNamedSlot.rightHand));
        Assert.Equal(before, world.CaptureAll());
    }

    [Fact]
    public void DisconnectDropsHeldItemSoItCanBeRecovered()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        Assert.True(world.AddActor(2, new Vector2(-1, 2)));
        Assert.True(world.TryInteract(1, StationEntityCatalog.WrenchId, Intent.Help, false));

        Assert.True(world.RemoveActor(1));

        Assert.Null(Wrench(world).Holder);
        Assert.Equal(new Vector2(-1, 1), Wrench(world).Position);
        Assert.True(world.TryInteract(2, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.Equal(2, Wrench(world).Holder);
    }

    [Fact]
    public void EngineerCanFuelGeneratorAndRestoreSharedLampThroughValidatedInteractions()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(2, new Vector2(7, -7), StationCrewJobCatalog.EngineerJobId));
        var published = new List<StationEntityState>();
        world.EntitiesChanged += changed => published.AddRange(changed);

        Assert.True(world.TryInteract(2, StationEntityCatalog.GeneratorFuelId, Intent.Help, false));
        Assert.Equal(2, Fuel(world).Holder);
        Assert.Equal(3, Fuel(world).Count(StationEntityField.Amount));
        Assert.True(world.TryInteract(2, StationEntityCatalog.FuelGeneratorId, Intent.Help, false));
        Assert.Equal(4d, Generator(world).Get(StationEntityField.FuelSeconds));
        Assert.True(world.TryInteract(2, StationEntityCatalog.FuelGeneratorId, Intent.Help, false));
        Assert.Equal(8d, Generator(world).Get(StationEntityField.FuelSeconds));
        Assert.Equal(1, Fuel(world).Count(StationEntityField.Amount));

        Assert.True(world.TryToggleActiveHand(2));
        Assert.True(world.TryInteract(2, StationEntityCatalog.FuelGeneratorId, Intent.Help, false));
        Assert.True(Generator(world).Flag(StationEntityField.Running));
        Assert.True(world.SetActorPosition(2, new Vector2(8, -7)));
        Assert.True(world.TryInteract(2, StationEntityCatalog.PowerStorageId, Intent.Help, false));
        Assert.True(world.AdvancePower(1));
        world.PublishChanges();
        Assert.True(world.GetState(StationEntityCatalog.ApcLampId)!.Flag(StationEntityField.Powered));
        Assert.Equal(1, world.GetState(StationEntityCatalog.PowerStorageId)!.Get(StationEntityField.StoredEnergy));
        Assert.Contains(published, state => state.Id == StationEntityCatalog.ApcLampId && state.Flag(StationEntityField.Powered));
    }

    [Fact]
    public void DisconnectingWhileHoldingFuelPublishesItsWorldDrop()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(2, new Vector2(7, -7), StationCrewJobCatalog.EngineerJobId));
        Assert.True(world.TryInteract(2, StationEntityCatalog.GeneratorFuelId, Intent.Help, false));

        Assert.True(world.RemoveActor(2));

        Assert.Null(Fuel(world).Holder);
        Assert.Equal(new Vector2(7, -7), Fuel(world).Position);
        Assert.Equal(3, Fuel(world).Count(StationEntityField.Amount));
    }
}
