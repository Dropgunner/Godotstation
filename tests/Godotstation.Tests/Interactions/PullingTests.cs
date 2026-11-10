using System.Collections.Generic;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Interactions;
using Xunit;

namespace Godotstation.Tests.Interactions;

public sealed class PullingTests
{
    private const int Puller = 2;
    private const int Other = 1;

    private static SharedStationInventoryWorld World()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(Other, new Vector2(-1, 0)));
        Assert.True(world.AddActor(Puller, new Vector2(-1, 1)));
        return world;
    }

    private static void HitUntil(SharedStationInventoryWorld world, CrewStat stat)
    {
        Assert.True(world.TryInteract(Puller, StationEntityCatalog.WrenchId, Intent.Help, false));
        for (int i = 0; i < 100 && world.GetCrewStat(Other) != stat; i++)
        {
            Assert.True(world.TryInteract(Puller, Other, Intent.Harm, false));
            world.AdvanceHealth(CrewMelee.ClickCooldownSeconds);
        }
        Assert.Equal(stat, world.GetCrewStat(Other));
    }

    [Fact]
    public void CtrlClickPullsAnAdjacentCrewMemberAndReplicatesIt()
    {
        SharedStationInventoryWorld world = World();
        var started = new List<(int, int)>();
        world.PullStarted += (puller, pulled) => started.Add((puller, pulled));

        Assert.True(world.TryTogglePull(Puller, Other));

        Assert.Equal(Other, world.GetPulledEntity(Puller));
        Assert.Equal(Puller, world.GetPuller(Other));
        Assert.Equal((Puller, Other), Assert.Single(started));
        Assert.Equal(Other, world.GetState(Puller)!.Count(StationEntityField.Pulling));
    }

    [Fact]
    public void ASecondCtrlClickDropsThePullWhateverItTargets()
    {
        SharedStationInventoryWorld world = World();
        var stopped = new List<PullStopReason>();
        world.PullStopped += (_, _, reason) => stopped.Add(reason);
        Assert.True(world.TryTogglePull(Puller, Other));

        Assert.True(world.TryTogglePull(Puller, StationEntityCatalog.WrenchId));

        Assert.Null(world.GetPulledEntity(Puller));
        Assert.Equal(PullStopReason.Released, Assert.Single(stopped));
        Assert.Equal(0, world.GetState(Puller)!.Count(StationEntityField.Pulling));
        Assert.False(world.TryReleasePull(Puller));
    }

    [Fact]
    public void SelfDistantAnchoredAndFixedThingsCannotBePulled()
    {
        SharedStationInventoryWorld world = World();
        Assert.False(world.TryTogglePull(Puller, Puller));
        Assert.False(world.TryTogglePull(Puller, StationEntityCatalog.GirderId));
        Assert.False(world.TryTogglePull(Puller, StationEntityCatalog.WrenchId));
        Assert.True(world.SetActorPosition(Puller, new Vector2(0, 1)));
        Assert.False(world.TryTogglePull(Puller, StationEntityCatalog.ServiceCartId)); // anchored
        Assert.True(world.SetActorPosition(Other, new Vector2(-1, -3)));
        Assert.False(world.TryTogglePull(Puller, Other)); // too far
    }

    [Fact]
    public void AnUnanchoredCartCanBePulledAndAnchoringBreaksThePull()
    {
        SharedStationInventoryWorld world = World();
        Assert.True(world.TryInteract(Puller, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.True(world.SetActorPosition(Puller, new Vector2(0, 1)));
        Assert.True(world.TryInteract(Puller, StationEntityCatalog.ServiceCartId, Intent.Help, false));
        Assert.True(world.TryTogglePull(Puller, StationEntityCatalog.ServiceCartId));

        var stopped = new List<PullStopReason>();
        world.PullStopped += (_, _, reason) => stopped.Add(reason);
        Assert.True(world.TryInteract(Puller, StationEntityCatalog.ServiceCartId, Intent.Help, false));
        world.ValidatePulls();
        Assert.Equal(PullStopReason.Anchored, Assert.Single(stopped));
    }

    [Fact]
    public void OnePullerPerObject()
    {
        SharedStationInventoryWorld world = World();
        Assert.True(world.AddActor(3, new Vector2(0, 0)));
        Assert.True(world.TryTogglePull(Puller, Other));
        var stopped = new List<(int, PullStopReason)>();
        world.PullStopped += (puller, _, reason) => stopped.Add((puller, reason));

        Assert.True(world.TryTogglePull(3, Other));

        Assert.Equal((Puller, PullStopReason.Taken), Assert.Single(stopped));
        Assert.Equal(3, world.GetPuller(Other));
    }

    [Fact]
    public void ThePulledThingFollowsIntoTheVacatedTile()
    {
        SharedStationInventoryWorld world = World();
        Assert.True(world.TryTogglePull(Puller, Other)); // puller (-1,1), pulled (-1,0)

        // Still adjacent and in line: nothing to do.
        Assert.False(world.TryGetPullFollow(Puller, new Vector2(-1, 1), out _, out _));

        // Puller steps straight away: the pulled body is now two tiles behind.
        Assert.True(world.SetActorPosition(Puller, new Vector2(-1, 2)));
        Assert.True(world.TryGetPullFollow(Puller, new Vector2(-1, 1), out int pulled, out Vector2 destination));
        Assert.Equal(Other, pulled);
        Assert.Equal(new Vector2(-1, 1), destination);
        Assert.True(world.SetActorPosition(Other, destination));

        // Puller steps sideways: the pulled body is now diagonal, so it takes the vacated tile.
        Assert.True(world.SetActorPosition(Puller, new Vector2(0, 2)));
        Assert.True(world.TryGetPullFollow(Puller, new Vector2(-1, 2), out _, out destination));
        Assert.Equal(new Vector2(-1, 2), destination);
    }

    [Fact]
    public void ThePullBreaksBeyondOneTileOrWhenThePullerIsIncapacitated()
    {
        SharedStationInventoryWorld world = World();
        Assert.True(world.TryTogglePull(Puller, Other));
        var stopped = new List<PullStopReason>();
        world.PullStopped += (_, _, reason) => stopped.Add(reason);

        world.ValidatePulls();
        Assert.Empty(stopped);
        Assert.True(world.SetActorPosition(Other, new Vector2(-3, 0)));
        world.ValidatePulls();
        Assert.Equal(PullStopReason.OutOfReach, Assert.Single(stopped));

        // Now the other way round: the victim pulls, then is beaten into crit.
        Assert.True(world.SetActorPosition(Other, new Vector2(-1, 0)));
        Assert.True(world.TryTogglePull(Other, Puller));
        HitUntil(world, CrewStat.SoftCrit);
        world.ValidatePulls();
        Assert.Equal(PullStopReason.Incapacitated, stopped[^1]);
        Assert.False(world.TryTogglePull(Other, Puller));
    }

    [Fact]
    public void ACriticalCrewMemberBeingPulledCannotCrawlAndSlowsThePuller()
    {
        SharedStationInventoryWorld world = World();
        Assert.True(world.TryTogglePull(Puller, Other));
        Assert.False(world.IsImmobilizedByPull(Other));
        Assert.False(world.IsDraggingProne(Puller));
        Assert.True(world.TryReleasePull(Puller));

        HitUntil(world, CrewStat.SoftCrit);
        Assert.False(world.IsImmobilizedByPull(Other));
        Assert.True(world.TryTogglePull(Puller, Other));
        Assert.True(world.IsImmobilizedByPull(Other));
        Assert.True(world.IsDraggingProne(Puller));
    }

    [Fact]
    public void LeavingTheRoundEndsPullsInBothDirections()
    {
        SharedStationInventoryWorld world = World();
        var stopped = new List<PullStopReason>();
        world.PullStopped += (_, _, reason) => stopped.Add(reason);
        Assert.True(world.TryTogglePull(Puller, Other));
        Assert.True(world.RemoveActor(Other));
        Assert.Equal(PullStopReason.Left, Assert.Single(stopped));
        Assert.Null(world.GetPulledEntity(Puller));
    }
}
