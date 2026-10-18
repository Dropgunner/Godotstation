using System.Numerics;
using Godotstation.Common.Interactions;
using Xunit;

namespace Godotstation.Tests.Interactions;

public sealed class SharedStationInteractionWorldTests
{
    private sealed class FullyBlockedMap : IReachabilityMap
    {
        public bool IsPassableForClick(Vector2 fromWorldPos, Vector2 toWorldPos, bool isServer, WorldEntity? context = null)
            => false;
    }

    [Fact]
    public void TwoCrewMembersSharePickupAndWrenchState()
    {
        var world = new SharedStationInteractionWorld();
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        Assert.True(world.AddActor(2, new Vector2(1, 1)));

        Assert.True(world.TryInteract(1, SharedStationInteractionWorld.WrenchId, Intent.Help, false));
        Assert.Equal(1, world.Snapshot.WrenchOwner);
        Assert.False(world.TryInteract(2, SharedStationInteractionWorld.WrenchId, Intent.Help, false));

        Assert.True(world.TryInteract(1, SharedStationInteractionWorld.FixtureId, Intent.Help, false));
        Assert.False(world.Snapshot.FixtureAnchored);
        Assert.False(world.TryInteract(2, SharedStationInteractionWorld.FixtureId, Intent.Help, false));
    }

    [Fact]
    public void PickupAndToolUseRequireStandardReach()
    {
        var world = new SharedStationInteractionWorld();
        Assert.True(world.AddActor(1, new Vector2(-6, 0)));

        Assert.False(world.TryInteract(1, SharedStationInteractionWorld.WrenchId, Intent.Help, false));
        Assert.Null(world.Snapshot.WrenchOwner);

        Assert.True(world.SetActorPosition(1, new Vector2(-1, 1)));
        Assert.True(world.TryInteract(1, SharedStationInteractionWorld.WrenchId, Intent.Help, false));
        Assert.True(world.SetActorPosition(1, new Vector2(0, 1)));
        Assert.True(world.TryInteract(1, SharedStationInteractionWorld.FixtureId, Intent.Help, false));
        Assert.False(world.Snapshot.FixtureAnchored);
    }

    [Fact]
    public void UnknownTargetsAndPeersCannotMutateSharedState()
    {
        var world = new SharedStationInteractionWorld();
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        SharedStationWorldSnapshot before = world.Snapshot;

        Assert.False(world.TryInteract(99, SharedStationInteractionWorld.WrenchId, Intent.Help, false));
        Assert.False(world.TryInteract(1, 123456, Intent.Help, false));
        Assert.Equal(before, world.Snapshot);
    }

    [Fact]
    public void BlockedClickLineAndDisconnectCannotLeaveAnOwnedOrMutatedItem()
    {
        var blockedWorld = new SharedStationInteractionWorld(new FullyBlockedMap());
        Assert.True(blockedWorld.AddActor(1, new Vector2(-1, 1)));
        Assert.False(blockedWorld.TryInteract(1, SharedStationInteractionWorld.WrenchId, Intent.Help, false));
        Assert.Null(blockedWorld.Snapshot.WrenchOwner);

        var world = new SharedStationInteractionWorld();
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        Assert.True(world.TryInteract(1, SharedStationInteractionWorld.WrenchId, Intent.Help, false));
        Assert.True(world.RemoveActor(1));
        Assert.Null(world.Snapshot.WrenchOwner);
        Assert.True(world.Snapshot.WrenchPosition == new Vector2(-1, 1));
    }

    [Fact]
    public void StateChangesPublishSnapshotsForReplication()
    {
        var world = new SharedStationInteractionWorld();
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        SharedStationWorldSnapshot? published = null;
        world.StateChanged += snapshot => published = snapshot;

        Assert.True(world.TryInteract(1, SharedStationInteractionWorld.WrenchId, Intent.Help, false));

        Assert.Equal(world.Snapshot, published);
    }

    [Fact]
    public void OwnerCanDropWrenchForAnotherCrewMemberToPickUp()
    {
        var world = new SharedStationInteractionWorld();
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        Assert.True(world.AddActor(2, new Vector2(-1, 2)));
        Assert.True(world.TryInteract(1, SharedStationInteractionWorld.WrenchId, Intent.Help, false));

        Assert.False(world.TryDropWrench(2));
        Assert.True(world.TryDropWrench(1));
        Assert.Null(world.Snapshot.WrenchOwner);
        Assert.Equal(new Vector2(-1, 1), world.Snapshot.WrenchPosition);
        Assert.True(world.TryInteract(2, SharedStationInteractionWorld.WrenchId, Intent.Help, false));
        Assert.Equal(2, world.Snapshot.WrenchOwner);
    }
}
