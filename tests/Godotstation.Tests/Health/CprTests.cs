using System.Collections.Generic;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Interactions;
using Xunit;

namespace Godotstation.Tests.Health;

public sealed class CprTests
{
    private const int Rescuer = 2;
    private const int Patient = 1;

    /// <summary>Patient beaten to the given stat; the rescuer then switches to an empty hand.</summary>
    private static SharedStationInventoryWorld WorldWithCasualty(CrewStat stat)
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(Patient, new Vector2(-1, 0)));
        Assert.True(world.AddActor(Rescuer, new Vector2(-1, 1)));
        Assert.True(world.TryInteract(Rescuer, StationEntityCatalog.WrenchId, Intent.Help, false));
        for (int i = 0; i < 100 && world.GetCrewStat(Patient) != stat; i++)
        {
            Assert.True(world.TryInteract(Rescuer, Patient, Intent.Harm, false));
            world.AdvanceHealth(CrewMelee.ClickCooldownSeconds);
        }
        Assert.Equal(stat, world.GetCrewStat(Patient));
        Assert.True(world.TryToggleActiveHand(Rescuer));
        return world;
    }

    private static double Oxy(SharedStationInventoryWorld world) =>
        world.GetState(Patient)!.Get(StationEntityField.OxyLoss);

    [Fact]
    public void CprNeedsHelpIntentAnEmptyHandAndACriticalPatient()
    {
        var healthy = new SharedStationInventoryWorld();
        Assert.True(healthy.AddActor(Patient, new Vector2(-1, 0)));
        Assert.True(healthy.AddActor(Rescuer, new Vector2(-1, 1)));
        Assert.False(healthy.TryInteract(Rescuer, Patient, Intent.Help, false));

        SharedStationInventoryWorld world = WorldWithCasualty(CrewStat.SoftCrit);
        Assert.False(world.TryInteract(Rescuer, Patient, Intent.Grab, false));
        Assert.True(world.TryToggleActiveHand(Rescuer)); // wrench hand
        Assert.False(world.TryInteract(Rescuer, Patient, Intent.Help, false));
        Assert.True(world.TryToggleActiveHand(Rescuer));

        var started = new List<(int, int)>();
        world.CprStarted += (rescuer, patient) => started.Add((rescuer, patient));
        Assert.True(world.TryInteract(Rescuer, Patient, Intent.Help, false));
        Assert.Equal((Rescuer, Patient), Assert.Single(started));
        Assert.Equal(Patient, world.GetState(Rescuer)!.Count(StationEntityField.CprPatient));
        Assert.False(world.TryInteract(Rescuer, Patient, Intent.Help, false)); // already performing
    }

    [Fact]
    public void FirstRoundTakesThreeSecondsThenRepeatsAtPanicSpeed()
    {
        SharedStationInventoryWorld world = WorldWithCasualty(CrewStat.HardCrit);
        var performed = new List<double>();
        world.CprPerformed += (_, _, healed) => performed.Add(healed);
        Assert.True(world.TryInteract(Rescuer, Patient, Intent.Help, false));

        world.AdvanceHealth(2.9);
        Assert.Empty(performed);
        world.AdvanceHealth(0.1);
        Assert.Single(performed);
        // Still critical from brute damage, so the rescuer keeps going at 0.8 seconds a round.
        Assert.Equal(Patient, world.GetState(Rescuer)!.Count(StationEntityField.CprPatient));
        world.AdvanceHealth(0.7);
        Assert.Single(performed);
        world.AdvanceHealth(0.2);
        Assert.Equal(2, performed.Count);
    }

    [Fact]
    public void CprHealsUpToSevenOxygenLossAndKeepsAHardCritPatientAlive()
    {
        SharedStationInventoryWorld world = WorldWithCasualty(CrewStat.HardCrit);
        world.AdvanceHealth(40); // untreated: oxygen loss builds
        double before = Oxy(world);
        Assert.True(before >= 7);
        var performed = new List<double>();
        world.CprPerformed += (_, _, healed) => performed.Add(healed);

        Assert.True(world.TryInteract(Rescuer, Patient, Intent.Help, false));
        world.AdvanceHealth(CprAttempt.FirstDelaySeconds);
        Assert.Equal(CprAttempt.OxyHealed, performed[0]);

        // Ten minutes of continuous CPR: without it a hard-crit patient dies in well under three.
        for (int i = 0; i < 6000; i++) world.AdvanceHealth(0.1);
        Assert.Equal(CrewStat.HardCrit, world.GetCrewStat(Patient));
        Assert.True(Oxy(world) < 7);
    }

    [Fact]
    public void MovingAwayOrPickingSomethingUpInterruptsCpr()
    {
        SharedStationInventoryWorld world = WorldWithCasualty(CrewStat.SoftCrit);
        var performed = new List<double>();
        world.CprPerformed += (_, _, healed) => performed.Add(healed);

        Assert.True(world.TryInteract(Rescuer, Patient, Intent.Help, false));
        Assert.True(world.SetActorPosition(Rescuer, new Vector2(0, 1)));
        world.AdvanceHealth(5);
        Assert.Empty(performed);
        Assert.Equal(0, world.GetState(Rescuer)!.Count(StationEntityField.CprPatient));

        Assert.True(world.SetActorPosition(Rescuer, new Vector2(-1, 1)));
        Assert.True(world.TryInteract(Rescuer, Patient, Intent.Help, false));
        Assert.True(world.TryInteract(Rescuer, StationEntityCatalog.MetalSheetsId, Intent.Help, false) ||
                    world.TryToggleActiveHand(Rescuer));
        world.AdvanceHealth(5);
        Assert.Empty(performed);
    }

    [Fact]
    public void CprDoesNothingForTheDeadAndStopsWhenThePatientRecovers()
    {
        SharedStationInventoryWorld dead = WorldWithCasualty(CrewStat.Dead);
        Assert.False(dead.TryInteract(Rescuer, Patient, Intent.Help, false));

        // A patient at exactly 0 health with oxygen loss recovers above the threshold once it is healed.
        var health = new CrewHealth();
        health.ApplyBrute(95);
        var cpr = new CprAttempt();
        Assert.False(cpr.TryStart(Rescuer, Patient, health, Vector2.Zero)); // health 5: not critical
        health.ApplyBrute(5);
        Assert.False(cpr.TryStart(Rescuer, Rescuer, health, Vector2.Zero)); // not on yourself
        Assert.True(cpr.TryStart(Rescuer, Patient, health, Vector2.Zero));
        Assert.False(cpr.Advance(2.9, stillValid: true));
        Assert.True(cpr.Advance(0.1, stillValid: true));
        health.HealBrute(40);
        Assert.False(CprAttempt.CanReceive(health));
    }
}
