using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Chat;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Interactions;
using Godotstation.Engine.Movement;
using Xunit;

namespace Godotstation.Tests.Interactions;

/// <summary>FC-GH-1..5 from planning/FidelityContract_SurvivalConsciousness.md (ghost addendum).</summary>
public sealed class GhostObserverTests
{
    private const int Killer = 1;
    private const int Victim = 2;
    private static readonly int VictimBody = StationEntityCatalog.CrewEntityId(Victim);

    private static SharedStationInventoryWorld WorldWithDeadVictim()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(Killer, new Vector2(0, 1)));
        Assert.True(world.AddActor(Victim, new Vector2(-1, 1)));
        for (int i = 0; i < 9; i++) Assert.True(world.TryInteract(Killer, VictimBody, Intent.Harm, false));
        Assert.True(world.GetCrewHealth(Victim)!.IsDead);
        return world;
    }

    [Fact]
    public void OnlyTheDeadBecomeGhostsAndOnlyOnce()
    {
        // FC-GH-1: the living cannot ghost; a dead body ghosts once at its own tile.
        SharedStationInventoryWorld world = WorldWithDeadVictim();

        Assert.False(world.TryBecomeGhost(Killer));
        Assert.True(world.TryBecomeGhost(Victim));
        Assert.False(world.TryBecomeGhost(Victim));
        Assert.True(world.IsGhost(Victim));
        Assert.Equal(new Vector2(-1, 1), world.GetGhostPosition(Victim));
    }

    [Fact]
    public void GhostMovesThroughWallsButBodyStays()
    {
        // FC-GH-2: ghost movement ignores collision; the corpse stays put.
        SharedStationInventoryWorld world = WorldWithDeadVictim();
        world.TryBecomeGhost(Victim);

        Assert.True(world.TryMoveGhost(Victim, 0, -1, 1.0));

        Assert.Equal(new Vector2(-1, 1 - SharedStationInventoryWorld.GhostMoveSpeed), world.GetGhostPosition(Victim));
        Assert.Equal(new Vector2(-1, 1), world.GetState(VictimBody)!.Position);
        Assert.False(world.CanActorMove(Victim));
    }

    [Fact]
    public void GhostStaysInsideStationBounds()
    {
        // FC-GH-2 deviation: the ghost is clamped to the playtest map instead of drifting into space.
        SharedStationInventoryWorld world = WorldWithDeadVictim();
        world.TryBecomeGhost(Victim);

        world.TryMoveGhost(Victim, -1, 0, 60);

        Assert.Equal(StationPlaytestLayout.MinX, world.GetGhostPosition(Victim)!.Value.X);
        Assert.False(world.TryMoveGhost(Victim, -1, 0, 1));
    }

    [Fact]
    public void GhostCannotInteract()
    {
        // FC-GH-4: the dispatcher's IsGhosting short-circuit refuses every click from a ghost.
        SharedStationInventoryWorld world = WorldWithDeadVictim();
        world.TryBecomeGhost(Victim);

        Assert.False(world.TryInteract(Victim, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.Null(world.GetState(StationEntityCatalog.WrenchId)!.Holder);
    }

    [Fact]
    public void GhostStateReplicatesOnTheCrewBody()
    {
        // FC-GH-5: every peer learns the ghost flag and ghost position from the body's entity state.
        SharedStationInventoryWorld world = WorldWithDeadVictim();
        world.TryBecomeGhost(Victim);
        world.TryMoveGhost(Victim, 1, 0, 0.25);

        StationEntityState body = world.GetState(VictimBody)!;
        Assert.True(body.Flag(StationEntityField.Ghost));
        Assert.Equal(1, body.Get(StationEntityField.GhostX), 3);
        Assert.Equal(1, body.Get(StationEntityField.GhostY), 3);
        Assert.False(world.GetState(StationEntityCatalog.CrewEntityId(Killer))!.Flag(StationEntityField.Ghost));
    }

    [Fact]
    public void GhostsHearAllLocalSpeechRegardlessOfRangeOrWalls()
    {
        // FC-GH-3: ChatRelay sends every Local message to ghosts; dead bodies that have not ghosted still do not hear it.
        var listeners = new[]
        {
            new LocalChatListener(1, Vector2.Zero),
            new LocalChatListener(2, new Vector2(40, 0), State: ConsciousState.Dead, IsGhost: true),
            new LocalChatListener(3, new Vector2(1, 0), State: ConsciousState.Dead),
        };

        SpeechRoute route = LocalSpeechRouter.Route(1, ConsciousState.Conscious, Vector2.Zero, listeners, (_, _) => false);

        Assert.Equal(SpeechChannel.Local, route.Channel);
        Assert.Equal(new[] { 1, 2 }, route.Recipients);
    }
}
