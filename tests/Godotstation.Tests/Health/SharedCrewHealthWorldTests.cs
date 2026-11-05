using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Interactions;
using Godotstation.Engine.Rounds;
using InventoryNamedSlot = Godotstation.Engine.Inventory.NamedSlot;
using Xunit;

namespace Godotstation.Tests.Health;

public sealed class SharedCrewHealthWorldTests
{
    private const int Attacker = 2;
    private const int Victim = 1;

    /// <summary>The playtest station with its bruise packs moved beside the wrench, so one spot reaches both.</summary>
    private static readonly IReadOnlyList<StationEntitySpawn> Station = StationEntityCatalog.Playtest
        .Select(spawn => spawn.Id == StationEntityCatalog.BruisePackId ? spawn with { Position = new Vector2(-2, 2) } : spawn)
        .ToList();

    /// <summary>Attacker stands next to the wrench and bruise packs; victim is one tile below.</summary>
    private static SharedStationInventoryWorld ArmedWorld()
    {
        var world = new SharedStationInventoryWorld(spawns: Station);
        Assert.True(world.AddActor(Victim, new Vector2(-1, 0), StationCrewJobCatalog.EngineerJobId));
        Assert.True(world.AddActor(Attacker, new Vector2(-1, 1)));
        Assert.True(world.TryInteract(Attacker, StationEntityCatalog.WrenchId, Intent.Help, false));
        return world;
    }

    private static int HitUntil(SharedStationInventoryWorld world, CrewStat stat)
    {
        int hits = 0;
        while (world.GetCrewStat(Victim) != stat)
        {
            Assert.True(world.TryInteract(Attacker, Victim, Intent.Harm, false));
            world.AdvanceHealth(CrewMelee.ClickCooldownSeconds);
            hits++;
            Assert.True(hits < 100);
        }
        return hits;
    }

    [Fact]
    public void CrewBodiesReplicateAsTargetableEntities()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(7, new Vector2(3, 1), StationCrewJobCatalog.EngineerJobId));
        StationEntityState body = world.GetState(7)!;
        Assert.Equal(StationEntityKind.Crew, body.Kind);
        Assert.True(body.IsTargetable);
        Assert.Equal(new Vector2(3, 1), body.Position);
        Assert.Equal(CrewHealth.MaxHealth, body.Get(StationEntityField.Health));
        Assert.Equal((int)CrewStat.Stable, body.Count(StationEntityField.Stat));
        Assert.Equal(StationCrewJobCatalog.EngineerJobId, body.Count(StationEntityField.Job));

        Assert.True(world.SetActorPosition(7, new Vector2(4, 1)));
        Assert.Equal(new Vector2(4, 1), world.GetState(7)!.Position);

        Assert.True(world.RemoveActor(7));
        Assert.Null(world.GetState(7));
        Assert.DoesNotContain(world.CaptureAll(), state => state.Kind == StationEntityKind.Crew);
    }

    [Fact]
    public void PeerIdThatCollidesWithAStationEntityIsRejected()
    {
        var world = new SharedStationInventoryWorld();
        Assert.False(world.AddActor(StationEntityCatalog.WrenchId, new Vector2(0, 0)));
    }

    [Fact]
    public void HarmIntentWrenchDealsFiveBruteAndPublishes()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        var attacks = new List<(int, int, double)>();
        world.CrewAttacked += (a, v, d) => attacks.Add((a, v, d));
        IReadOnlyList<StationEntityState>? published = null;
        world.EntitiesChanged += changed => published = changed;

        Assert.True(world.TryInteract(Attacker, Victim, Intent.Harm, false));

        Assert.Equal(95, world.GetCrewHealth(Victim));
        Assert.Equal((Attacker, Victim, CrewMelee.WrenchForce), Assert.Single(attacks));
        Assert.Contains(published!, state => state.Id == Victim && state.Get(StationEntityField.BruteLoss) == 5);
    }

    [Fact]
    public void HelpIntentAndEmptyHandsDoNotHurt()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        Assert.False(world.TryInteract(Attacker, Victim, Intent.Help, false));
        Assert.True(world.TryToggleActiveHand(Attacker));
        Assert.False(world.TryInteract(Attacker, Victim, Intent.Harm, false));
        Assert.Equal(CrewHealth.MaxHealth, world.GetCrewHealth(Victim));
    }

    [Fact]
    public void MeleeCooldownIsEightDeciseconds()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        Assert.True(world.TryInteract(Attacker, Victim, Intent.Harm, false));
        Assert.False(world.TryInteract(Attacker, Victim, Intent.Harm, false));
        world.AdvanceHealth(0.79);
        Assert.False(world.TryInteract(Attacker, Victim, Intent.Harm, false));
        world.AdvanceHealth(0.01);
        Assert.True(world.TryInteract(Attacker, Victim, Intent.Harm, false));
        Assert.Equal(90, world.GetCrewHealth(Victim));
    }

    [Fact]
    public void AttackRequiresReach()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        Assert.True(world.SetActorPosition(Victim, new Vector2(-1, -3)));
        Assert.False(world.TryInteract(Attacker, Victim, Intent.Harm, false));
        Assert.Equal(CrewHealth.MaxHealth, world.GetCrewHealth(Victim));
    }

    [Fact]
    public void TwentyWrenchHitsPutCrewInSoftCritWhichBlocksHands()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        var changes = new List<(int, CrewStat, CrewStat)>();
        world.CrewStatChanged += (peer, before, after) => changes.Add((peer, before, after));

        Assert.Equal(20, HitUntil(world, CrewStat.SoftCrit));
        Assert.Equal((Victim, CrewStat.Stable, CrewStat.SoftCrit), Assert.Single(changes));
        Assert.Equal((int)CrewStat.SoftCrit, world.GetState(Victim)!.Count(StationEntityField.Stat));

        Assert.False(world.TryInteract(Victim, StationEntityCatalog.MetalSheetsId, Intent.Help, false));
        Assert.False(world.TryToggleActiveHand(Victim));
        Assert.False(world.TryInteract(Victim, StationEntityCatalog.EngineeringAirlockId, Intent.Help, false));
    }

    [Fact]
    public void CritCancelsGirderWork()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(Victim, new Vector2(1, 1)));
        Assert.True(world.AddActor(Attacker, new Vector2(0, 1)));
        Assert.True(world.TryInteract(Victim, StationEntityCatalog.MetalSheetsId, Intent.Help, false));
        Assert.True(world.TryInteract(Victim, StationEntityCatalog.GirderId, Intent.Help, false));
        Assert.True(world.SetActorPosition(Attacker, new Vector2(-1, 1)));
        Assert.True(world.TryInteract(Attacker, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.True(world.SetActorPosition(Attacker, new Vector2(0, 1)));
        HitUntil(world, CrewStat.SoftCrit);

        world.AdvanceConstruction(Victim, 10);
        Assert.False(world.GetState(StationEntityCatalog.GirderId)!.Flag(StationEntityField.Plated));
        Assert.Equal(0, world.GetState(StationEntityCatalog.GirderId)!.Count(StationEntityField.Worker));
    }

    [Fact]
    public void BruisePackOnAnotherCrewHealsFortyAfterTwoSecondsAndConsumesOne()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        HitUntil(world, CrewStat.SoftCrit);
        Assert.True(world.TryToggleActiveHand(Attacker));
        Assert.True(world.TryInteract(Attacker, StationEntityCatalog.BruisePackId, Intent.Help, false));
        Assert.Equal("bruise pack", world.GetHandItemName(Attacker, InventoryNamedSlot.leftHand));
        var treated = new List<(int, int, double)>();
        world.CrewTreated += (h, p, amount) => treated.Add((h, p, amount));

        Assert.True(world.TryInteract(Attacker, Victim, Intent.Help, false));
        Assert.Equal(Victim, world.GetState(Attacker)!.Count(StationEntityField.TreatmentPatient));
        world.AdvanceHealth(1.9);
        Assert.Empty(treated);
        world.AdvanceHealth(0.1);

        Assert.Equal((Attacker, Victim, BruisePackTreatment.HealBrute), Assert.Single(treated));
        Assert.Equal(CrewStat.Stable, world.GetCrewStat(Victim));
        Assert.Equal(5, world.GetState(StationEntityCatalog.BruisePackId)!.Count(StationEntityField.Amount));
        Assert.Equal(0, world.GetState(Attacker)!.Count(StationEntityField.TreatmentPatient));
        Assert.True(world.TryToggleActiveHand(Victim));
    }

    [Fact]
    public void SelfTreatmentTakesFourSeconds()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        Assert.True(world.TryInteract(Attacker, Victim, Intent.Harm, false));
        Assert.True(world.SetActorPosition(Victim, new Vector2(-2, 1)));
        Assert.True(world.TryInteract(Victim, StationEntityCatalog.BruisePackId, Intent.Help, false));
        Assert.True(world.TryInteract(Victim, Victim, Intent.Help, false));
        world.AdvanceHealth(3.9);
        Assert.Equal(95, world.GetCrewHealth(Victim));
        world.AdvanceHealth(0.1);
        Assert.Equal(CrewHealth.MaxHealth, world.GetCrewHealth(Victim));
    }

    [Fact]
    public void TreatmentIsRejectedForUninjuredAndCancelledWhenPatientLeavesReach()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        Assert.True(world.TryToggleActiveHand(Attacker));
        Assert.True(world.TryInteract(Attacker, StationEntityCatalog.BruisePackId, Intent.Help, false));
        Assert.False(world.TryInteract(Attacker, Victim, Intent.Help, false));

        Assert.True(world.TryToggleActiveHand(Attacker));
        world.AdvanceHealth(1);
        Assert.True(world.TryInteract(Attacker, Victim, Intent.Harm, false));
        Assert.True(world.TryToggleActiveHand(Attacker));
        Assert.True(world.TryInteract(Attacker, Victim, Intent.Help, false));
        Assert.True(world.SetActorPosition(Victim, new Vector2(-1, -3)));
        world.AdvanceHealth(5);
        Assert.Equal(95, world.GetCrewHealth(Victim));
        Assert.Equal(6, world.GetState(StationEntityCatalog.BruisePackId)!.Count(StationEntityField.Amount));
    }

    [Fact]
    public void UntreatedHardCritDiesAndTheDeadCannotBeHealedOrAct()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        HitUntil(world, CrewStat.HardCrit);
        Assert.Equal(CrewStat.HardCrit, world.GetCrewStat(Victim));
        world.AdvanceHealth(10 * 60);
        Assert.Equal(CrewStat.Dead, world.GetCrewStat(Victim));

        Assert.True(world.TryToggleActiveHand(Attacker));
        Assert.True(world.TryInteract(Attacker, StationEntityCatalog.BruisePackId, Intent.Help, false));
        Assert.False(world.TryInteract(Attacker, Victim, Intent.Help, false));
        Assert.False(world.TryInteract(Victim, StationEntityCatalog.MetalSheetsId, Intent.Help, false));
        Assert.False(world.TryDropActiveHand(Victim));
    }

    [Fact]
    public void RoundEndReportCountsCrewConditions()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        HitUntil(world, CrewStat.HardCrit);
        world.AdvanceHealth(10 * 60);
        RoundEndReport report = RoundEndReport.Create(1, 60, ["Engineer", "Assistant"], world.CaptureAll());
        Assert.Equal(1, report.CrewStable);
        Assert.Equal(0, report.CrewCritical);
        Assert.Equal(1, report.CrewDead);
        Assert.Contains("Crew condition: 1 stable · 0 critical · 1 dead", report.ToDisplayText());
        Assert.DoesNotContain(world.CaptureAll().Where(state => state.Kind == StationEntityKind.Crew),
            state => !state.IsTargetable);
    }
}
