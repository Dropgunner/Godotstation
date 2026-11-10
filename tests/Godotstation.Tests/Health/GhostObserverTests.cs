using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Chat;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Interactions;
using Xunit;

namespace Godotstation.Tests.Health;

public sealed class GhostObserverTests
{
    private const int Attacker = 2;
    private const int Victim = 1;
    private static readonly Vector2 Min = new(-12, -9);
    private static readonly Vector2 Max = new(12, 9);

    private static SharedStationInventoryWorld WorldWithDeadVictim()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(Victim, new Vector2(-1, 0)));
        Assert.True(world.AddActor(Attacker, new Vector2(-1, 1)));
        Assert.True(world.TryInteract(Attacker, StationEntityCatalog.WrenchId, Intent.Help, false));
        for (int hit = 0; hit < 100 && world.GetCrewStat(Victim) != CrewStat.Dead; hit++)
        {
            Assert.True(world.TryInteract(Attacker, Victim, Intent.Harm, false));
            world.AdvanceHealth(CrewMelee.ClickCooldownSeconds);
        }
        Assert.Equal(CrewStat.Dead, world.GetCrewStat(Victim));
        return world;
    }

    [Fact]
    public void GhostMovesAtItsSpeedAndStopsAtTheStationEdge()
    {
        Vector2 moved = GhostObserver.Move(Vector2.Zero, 1, 0, 0.5, Min, Max);
        Assert.Equal(new Vector2(GhostObserver.Speed * 0.5f, 0), moved);

        Vector2 diagonal = GhostObserver.Move(Vector2.Zero, 1, 1, 1, Min, Max);
        Assert.Equal(GhostObserver.Speed, diagonal.Length(), 3);

        Assert.Equal(Max, GhostObserver.Move(new Vector2(11.9f, 8.9f), 1, 1, 10, Min, Max));
        Assert.Equal(Vector2.Zero, GhostObserver.Move(Vector2.Zero, 0, 0, 1, Min, Max));
        Assert.Equal(Vector2.Zero, GhostObserver.Move(Vector2.Zero, 5, 0, 1, Min, Max));
    }

    [Fact]
    public void OnlyTheDeadSeeGhosts()
    {
        Assert.False(GhostObserver.IsVisibleTo(CrewStat.Stable));
        Assert.False(GhostObserver.IsVisibleTo(CrewStat.SoftCrit));
        Assert.False(GhostObserver.IsVisibleTo(CrewStat.HardCrit));
        Assert.True(GhostObserver.IsVisibleTo(CrewStat.Dead));
    }

    [Fact]
    public void TheLivingCannotGhost()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(Victim, new Vector2(-1, 0)));
        Assert.False(world.TryMoveGhost(Victim, 1, 0, 1, Min, Max));
        Assert.Null(world.GetGhostPosition(Victim));
        Assert.Equal(new Vector2(-1, 0), world.GetPerceptionPosition(Victim));
        Assert.False(world.GetState(Victim)!.Flag(StationEntityField.Ghost));
    }

    [Fact]
    public void ADeadPlayerStaysInTheBodyUntilTheyTryToMove()
    {
        SharedStationInventoryWorld world = WorldWithDeadVictim();
        var ghosted = new List<int>();
        world.CrewGhosted += ghosted.Add;

        Assert.Null(world.GetGhostPosition(Victim));
        Assert.False(world.TryMoveGhost(Victim, 0, 0, 1, Min, Max));
        Assert.Empty(ghosted);

        Assert.True(world.TryMoveGhost(Victim, 0, 1, 0.5, Min, Max));
        Assert.Equal(Victim, Assert.Single(ghosted));
        Assert.Equal(new Vector2(-1, 3), world.GetGhostPosition(Victim));
        Assert.Equal(new Vector2(-1, 3), world.GetPerceptionPosition(Victim));

        Assert.True(world.TryMoveGhost(Victim, 0, 1, 0.5, Min, Max));
        Assert.Single(ghosted);
    }

    [Fact]
    public void TheBodyStaysBehindAndTheGhostReplicatesOnTheCrewEntity()
    {
        SharedStationInventoryWorld world = WorldWithDeadVictim();
        IReadOnlyList<StationEntityState>? published = null;
        world.EntitiesChanged += changed => published = changed;

        // Straight down from the body, through the wall row at y = 4, to the station edge.
        Assert.True(world.TryMoveGhost(Victim, 0, 1, 60, Min, Max));
        world.PublishChanges();

        StationEntityState body = world.GetState(Victim)!;
        Assert.Equal(new Vector2(-1, 0), body.Position);
        Assert.True(body.Flag(StationEntityField.Ghost));
        Assert.Equal(-1, body.Get(StationEntityField.GhostX));
        Assert.Equal(9, body.Get(StationEntityField.GhostY));
        Assert.Contains(published!, state => state.Id == Victim && state.Flag(StationEntityField.Ghost));
    }

    [Fact]
    public void AGhostStillCannotTouchTheWorld()
    {
        SharedStationInventoryWorld world = WorldWithDeadVictim();
        Assert.True(world.TryMoveGhost(Victim, 1, 0, 0.1, Min, Max));
        Assert.False(world.TryInteract(Victim, StationEntityCatalog.MetalSheetsId, Intent.Help, false));
        Assert.False(world.TryToggleActiveHand(Victim));
        Assert.False(world.TryDropActiveHand(Victim));
    }

    [Fact]
    public void DeadListenersHearThroughWallsButOnlyInRange()
    {
        var listeners = new[]
        {
            new LocalChatListener(2, new Vector2(5, 0)),
            new LocalChatListener(3, new Vector2(5, 0), IgnoresWalls: true),
            new LocalChatListener(4, new Vector2(20, 0), IgnoresWalls: true),
        };
        IReadOnlyList<int> heard = LocalSpeechRouter.FindListeners(1, Vector2.Zero, listeners, (_, _) => false);
        Assert.Equal(new[] { 3 }, heard.ToArray());
    }
}
