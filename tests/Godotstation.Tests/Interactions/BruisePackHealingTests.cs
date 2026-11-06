using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Interactions;
using Xunit;

namespace Godotstation.Tests.Interactions;

/// <summary>FC-HL-1..5 from planning/FidelityContract_SurvivalConsciousness.md (healing addendum).</summary>
public sealed class BruisePackHealingTests
{
    private const int Medic = 1;
    private const int Patient = 2;
    private static readonly int PatientBody = StationEntityCatalog.CrewEntityId(Patient);
    private static readonly int MedicBody = StationEntityCatalog.CrewEntityId(Medic);

    private static SharedStationInventoryWorld MedicBesidePatient()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(Medic, new Vector2(0, 1)));
        Assert.True(world.AddActor(Patient, new Vector2(-1, 1)));
        return world;
    }

    private static void Hit(SharedStationInventoryWorld world, int times)
    {
        for (int i = 0; i < times; i++)
            Assert.True(world.TryInteract(Medic, PatientBody, Intent.Harm, false));
    }

    private static void PickUpPack(SharedStationInventoryWorld world) =>
        Assert.True(world.TryInteract(Medic, StationEntityCatalog.BruisePackId, Intent.Help, false));

    private static int PackUses(SharedStationInventoryWorld world) =>
        world.GetState(StationEntityCatalog.BruisePackId)!.Count(StationEntityField.Amount);

    [Fact]
    public void HealRulesRemoveBruteAndRederiveState()
    {
        // FC-HL-2: healing re-derives conscious state from health, never below zero damage.
        var health = new CrewHealth();
        health.ApplyBrute(175);
        Assert.Equal(ConsciousState.Unconscious, health.ConsciousState);

        Assert.True(health.HealBrute(CrewHealthRules.BruisePackHeal));
        Assert.Equal(135, health.Brute);
        Assert.Equal(ConsciousState.BarelyConscious, health.ConsciousState);

        Assert.True(health.HealBrute(500));
        Assert.Equal(0, health.Brute);
        Assert.Equal(ConsciousState.Conscious, health.ConsciousState);
        Assert.False(health.HealBrute(CrewHealthRules.BruisePackHeal));
    }

    [Fact]
    public void DeadBodiesAreNotHealed()
    {
        // FC-HL-2 with FC-SC-2: death stays terminal; healing a corpse changes nothing.
        var health = new CrewHealth();
        health.ApplyBrute(225);

        Assert.False(health.HealBrute(CrewHealthRules.BruisePackHeal));
        Assert.Equal(225, health.Brute);
        Assert.Equal(ConsciousState.Dead, health.ConsciousState);
    }

    [Fact]
    public void StationSpawnsOneSixUseBruisePack()
    {
        // FC-HL-1: Bruise Pack.prefab initialAmount 6.
        var world = new SharedStationInventoryWorld();
        StationEntityState pack = world.GetState(StationEntityCatalog.BruisePackId)!;
        Assert.Equal(StationEntityKind.BruisePack, pack.Kind);
        Assert.Equal(CrewHealthRules.BruisePackUses, pack.Count(StationEntityField.Amount));
    }

    [Fact]
    public void HelpIntentWithPackHealsFortyAndConsumesOneUse()
    {
        // FC-HL-3: HealsTheLiving accepts Help intent and consumes one stack use per application.
        SharedStationInventoryWorld world = MedicBesidePatient();
        Hit(world, 2);
        PickUpPack(world);

        Assert.True(world.TryInteract(Medic, PatientBody, Intent.Help, false));

        Assert.Equal(10, world.GetCrewHealth(Patient)!.Brute);
        Assert.Equal(CrewHealthRules.BruisePackUses - 1, PackUses(world));
        Assert.Equal(Medic, world.GetState(StationEntityCatalog.BruisePackId)!.Holder);
    }

    [Fact]
    public void HealingRevivesAnUnconsciousCrewmateToMovement()
    {
        // FC-HL-4: the recovery path: unconscious → treated twice → Conscious, movement and clicks allowed again.
        SharedStationInventoryWorld world = MedicBesidePatient();
        Hit(world, 7);
        Assert.Equal(ConsciousState.Unconscious, world.GetCrewHealth(Patient)!.ConsciousState);
        Assert.False(world.CanActorMove(Patient));
        PickUpPack(world);

        Assert.True(world.TryInteract(Medic, PatientBody, Intent.Help, false));
        Assert.True(world.TryInteract(Medic, PatientBody, Intent.Help, false));

        Assert.Equal(95, world.GetCrewHealth(Patient)!.Brute);
        Assert.Equal(ConsciousState.Conscious, world.GetCrewHealth(Patient)!.ConsciousState);
        Assert.True(world.CanActorMove(Patient));
        Assert.True(world.TryInteract(Patient, MedicBody, Intent.Harm, false));
        StationEntityState body = world.GetState(PatientBody)!;
        Assert.Equal((int)ConsciousState.Conscious, body.Count(StationEntityField.Conscious));
    }

    [Fact]
    public void PackIsRefusedOnUninjuredDeadSelfOrWrongIntent()
    {
        // FC-HL-3/5: no damage, a dead body, one's own body, or a non-Help intent → no heal and no use consumed.
        SharedStationInventoryWorld world = MedicBesidePatient();
        PickUpPack(world);

        Assert.False(world.TryInteract(Medic, PatientBody, Intent.Help, false));
        Hit(world, 1);
        Assert.False(world.TryInteract(Medic, MedicBody, Intent.Help, false));
        Assert.False(world.TryInteract(Medic, PatientBody, Intent.Grab, false));
        Assert.Equal(CrewHealthRules.BruisePackUses, PackUses(world));

        Hit(world, 8);
        Assert.True(world.GetCrewHealth(Patient)!.IsDead);
        Assert.False(world.TryInteract(Medic, PatientBody, Intent.Help, false));
        Assert.Equal(CrewHealthRules.BruisePackUses, PackUses(world));
    }

    [Fact]
    public void OutOfReachTreatmentIsRefused()
    {
        // FC-HL-3: the dispatcher's reach validation applies.
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(Medic, new Vector2(0, 1)));
        Assert.True(world.AddActor(Patient, new Vector2(-1, 1)));
        Hit(world, 1);
        PickUpPack(world);
        world.SetActorPosition(Patient, new Vector2(-5, 1));

        Assert.False(world.TryInteract(Medic, PatientBody, Intent.Help, false));
        Assert.Equal(25, world.GetCrewHealth(Patient)!.Brute);
    }

    [Fact]
    public void UsesRunOutAfterSixApplications()
    {
        // FC-HL-1/3: the stack empties after six applications and the empty stack cannot heal.
        SharedStationInventoryWorld world = MedicBesidePatient();
        PickUpPack(world);
        for (int i = 0; i < CrewHealthRules.BruisePackUses; i++)
        {
            Hit(world, 2);
            Assert.True(world.TryInteract(Medic, PatientBody, Intent.Help, false), $"application {i + 1}");
        }
        Assert.Equal(0, PackUses(world));
        Assert.False(world.TryInteract(Medic, PatientBody, Intent.Help, false));
    }
}
