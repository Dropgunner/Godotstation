using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Interactions;
using Godotstation.Engine.Inventory;
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

    [Fact]
    public void PickupPlacesWrenchInActiveHandAndPublishesOwner()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        SharedStationWorldSnapshot? published = null;
        world.StateChanged += snapshot => published = snapshot;

        Assert.True(world.TryInteract(1, SharedStationInventoryWorld.WrenchId, Intent.Help, false));

        Assert.Equal("wrench", world.GetHandItemName(1, InventoryNamedSlot.rightHand));
        Assert.Equal(string.Empty, world.GetHandItemName(1, InventoryNamedSlot.leftHand));
        Assert.Equal(1, world.Snapshot.WrenchOwner);
        Assert.Equal(world.Snapshot, published);
    }

    [Fact]
    public void ToolUseRequiresTheWrenchInTheSelectedHandAndDropReturnsItToWorld()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        Assert.True(world.AddActor(2, new Vector2(-1, 2)));
        Assert.True(world.TryInteract(1, SharedStationInventoryWorld.WrenchId, Intent.Help, false));

        Assert.True(world.TryToggleActiveHand(1));
        Assert.False(world.TryInteract(1, SharedStationInventoryWorld.FixtureId, Intent.Help, false));
        Assert.False(world.TryDropActiveHand(1));
        Assert.Equal(1, world.Snapshot.WrenchOwner);
        Assert.True(world.TryToggleActiveHand(1));
        Assert.True(world.TryInteract(1, SharedStationInventoryWorld.FixtureId, Intent.Help, false));
        Assert.False(world.Snapshot.FixtureAnchored);

        Assert.True(world.SetActorPosition(1, new Vector2(2, 3)));
        Assert.False(world.TryDropActiveHand(2));
        Assert.True(world.TryDropActiveHand(1));
        Assert.Null(world.Snapshot.WrenchOwner);
        Assert.Equal(new Vector2(2, 3), world.Snapshot.WrenchPosition);
        Assert.Equal(string.Empty, world.GetHandItemName(1, InventoryNamedSlot.rightHand));
        Assert.True(world.SetActorPosition(2, new Vector2(2, 2)));
        Assert.True(world.TryInteract(2, SharedStationInventoryWorld.WrenchId, Intent.Help, false));
        Assert.Equal(2, world.Snapshot.WrenchOwner);
    }

    [Fact]
    public void OutOfRangeBlockedAndUnknownRequestsLeaveInventoryAndWorldUnchanged()
    {
        var world = new SharedStationInventoryWorld(new FullyBlockedMap());
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        Assert.True(world.AddActor(2, new Vector2(20, 20)));
        SharedStationWorldSnapshot before = world.Snapshot;

        Assert.False(world.TryInteract(1, SharedStationInventoryWorld.WrenchId, Intent.Help, false));
        Assert.False(world.TryInteract(2, SharedStationInventoryWorld.WrenchId, Intent.Help, false));
        Assert.False(world.TryInteract(1, 123456, Intent.Help, false));
        Assert.False(world.TryDropActiveHand(1));

        Assert.Null(world.GetActiveHand(99));
        Assert.Equal(string.Empty, world.GetHandItemName(1, InventoryNamedSlot.rightHand));
        Assert.Equal(before, world.Snapshot);
    }

    [Fact]
    public void DisconnectDropsHeldItemSoItCanBeRecovered()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        Assert.True(world.AddActor(2, new Vector2(-1, 2)));
        Assert.True(world.TryInteract(1, SharedStationInventoryWorld.WrenchId, Intent.Help, false));

        Assert.True(world.RemoveActor(1));

        Assert.Null(world.Snapshot.WrenchOwner);
        Assert.Equal(new Vector2(-1, 1), world.Snapshot.WrenchPosition);
        Assert.True(world.TryInteract(2, SharedStationInventoryWorld.WrenchId, Intent.Help, false));
        Assert.Equal(2, world.Snapshot.WrenchOwner);
    }
}
