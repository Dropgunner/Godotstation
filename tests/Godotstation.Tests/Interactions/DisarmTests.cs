using System;
using System.Collections.Generic;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Interactions;
using Xunit;

namespace Godotstation.Tests.Interactions;

/// <summary>FC-DA-1..4 from planning/FidelityContract_SurvivalConsciousness.md (disarm addendum).</summary>
public sealed class DisarmTests
{
    private const int Attacker = 1;
    private const int Victim = 2;
    private static readonly int VictimBody = StationEntityCatalog.CrewEntityId(Victim);
    private static readonly int AttackerBody = StationEntityCatalog.CrewEntityId(Attacker);

    /// <summary>Feeds the given rolls in order, like Random.Next(1, 100).</summary>
    private static Func<int, int, int> Rolls(params int[] values)
    {
        var queue = new Queue<int>(values);
        return (_, _) => queue.Dequeue();
    }

    private static SharedStationInventoryWorld VictimHoldingWrench(Func<int, int, int> rolls)
    {
        var world = new SharedStationInventoryWorld(nextRandom: rolls);
        Assert.True(world.AddActor(Attacker, new Vector2(0, 1)));
        Assert.True(world.AddActor(Victim, new Vector2(-1, 1)));
        Assert.True(world.TryInteract(Victim, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.Equal(Victim, world.GetState(StationEntityCatalog.WrenchId)!.Holder);
        return world;
    }

    [Theory]
    [InlineData(new[] { 1 }, DisarmOutcome.Knockdown)]
    [InlineData(new[] { 5 }, DisarmOutcome.Knockdown)]
    [InlineData(new[] { 6, 50 }, DisarmOutcome.Disarm)]
    [InlineData(new[] { 99, 1 }, DisarmOutcome.Disarm)]
    [InlineData(new[] { 6, 51 }, DisarmOutcome.Miss)]
    public void RollMatchesDisarmableChances(int[] rolls, DisarmOutcome expected)
    {
        // FC-DA-2: <=5 knockdown, else a fresh roll <=50 disarms, else miss.
        Assert.Equal(expected, DisarmRules.Roll(Rolls(rolls)));
    }

    [Fact]
    public void SuccessfulDisarmDropsBothHandsAtTheVictim()
    {
        // FC-DA-2: Disarm() drops every hand slot through ServerDrop.
        SharedStationInventoryWorld world = VictimHoldingWrench(Rolls(50, 10));
        var outcomes = new List<DisarmOutcome>();
        world.DisarmResolved += (_, _, outcome) => outcomes.Add(outcome);

        Assert.True(world.TryInteract(Attacker, VictimBody, Intent.Disarm, false));

        Assert.Equal(new[] { DisarmOutcome.Disarm }, outcomes);
        StationEntityState wrench = world.GetState(StationEntityCatalog.WrenchId)!;
        Assert.Null(wrench.Holder);
        Assert.Equal(new Vector2(-1, 1), wrench.Position);
        Assert.Equal(0, world.GetCrewHealth(Victim)!.Brute);
    }

    [Fact]
    public void MissChangesNothing()
    {
        SharedStationInventoryWorld world = VictimHoldingWrench(Rolls(50, 90));

        Assert.True(world.TryInteract(Attacker, VictimBody, Intent.Disarm, false));

        Assert.Equal(Victim, world.GetState(StationEntityCatalog.WrenchId)!.Holder);
        Assert.True(world.CanActorMove(Victim));
    }

    [Fact]
    public void KnockdownStunsForSixSecondsKeepsHandsAndBlocksActing()
    {
        // FC-DA-3: ServerStun(6, dropItem: false) stops movement and interaction, then wears off.
        SharedStationInventoryWorld world = VictimHoldingWrench(Rolls(3));

        Assert.True(world.TryInteract(Attacker, VictimBody, Intent.Disarm, false));

        Assert.True(world.IsStunned(Victim));
        Assert.False(world.CanActorMove(Victim));
        Assert.Equal(Victim, world.GetState(StationEntityCatalog.WrenchId)!.Holder);
        Assert.True(world.GetState(VictimBody)!.Flag(StationEntityField.Stunned));
        Assert.False(world.TryInteract(Victim, AttackerBody, Intent.Harm, false));

        world.AdvanceCrewTimers(5.9);
        Assert.False(world.CanActorMove(Victim));
        world.AdvanceCrewTimers(0.2);
        Assert.True(world.CanActorMove(Victim));
        Assert.False(world.GetState(VictimBody)!.Flag(StationEntityField.Stunned));
        Assert.True(world.TryInteract(Victim, AttackerBody, Intent.Harm, false));
    }

    [Fact]
    public void AttemptsAreHalfASecondApart()
    {
        // FC-DA-1: Disarmable.TimeBetweenDisarms = 0.5 s cooldown per performer.
        SharedStationInventoryWorld world = VictimHoldingWrench(Rolls(50, 90, 50, 90));

        Assert.True(world.TryInteract(Attacker, VictimBody, Intent.Disarm, false));
        Assert.False(world.TryInteract(Attacker, VictimBody, Intent.Disarm, false));
        world.AdvanceCrewTimers(0.5);
        Assert.True(world.TryInteract(Attacker, VictimBody, Intent.Disarm, false));
    }

    [Fact]
    public void SelfOutOfReachAndKnockedDownPerformersAreRefused()
    {
        // FC-DA-1: no self-disarm, reach applies, and a performer lying down cannot disarm.
        var world = new SharedStationInventoryWorld(nextRandom: Rolls(1));
        Assert.True(world.AddActor(Attacker, new Vector2(0, 1)));
        Assert.True(world.AddActor(Victim, new Vector2(-1, 1)));

        Assert.False(world.TryInteract(Attacker, AttackerBody, Intent.Disarm, false));
        Assert.True(world.TryInteract(Attacker, VictimBody, Intent.Disarm, false)); // knockdown
        Assert.False(world.TryInteract(Victim, AttackerBody, Intent.Disarm, false));

        world.SetActorPosition(Victim, new Vector2(-5, 1));
        world.AdvanceCrewTimers(1);
        Assert.False(world.TryInteract(Attacker, VictimBody, Intent.Disarm, false));
    }
}
