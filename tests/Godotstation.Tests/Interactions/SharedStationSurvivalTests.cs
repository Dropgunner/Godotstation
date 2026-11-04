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

namespace Godotstation.Tests.Interactions;

/// <summary>FC-SC-3..7, FC-SC-9 and FC-SC-10 from planning/FidelityContract_SurvivalConsciousness.md.</summary>
public sealed class SharedStationSurvivalTests
{
    private const int Attacker = 1;
    private const int Victim = 2;
    private static readonly int VictimBody = StationEntityCatalog.CrewEntityId(Victim);
    private static readonly int AttackerBody = StationEntityCatalog.CrewEntityId(Attacker);

    private static SharedStationInventoryWorld TwoAdjacentCrew()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(Attacker, new Vector2(0, 1)));
        Assert.True(world.AddActor(Victim, new Vector2(-1, 1)));
        return world;
    }

    private static void Hit(SharedStationInventoryWorld world, int times)
    {
        for (int i = 0; i < times; i++)
            Assert.True(world.TryInteract(Attacker, VictimBody, Intent.Harm, false), $"hit {i + 1} was refused");
    }

    [Fact]
    public void HarmIntentAppliesBruteThroughDispatcher()
    {
        // FC-SC-3: one validated harm click = one unarmed hit on the target body only.
        SharedStationInventoryWorld world = TwoAdjacentCrew();

        Hit(world, 1);

        Assert.Equal(CrewHealthRules.UnarmedBruteDamage, world.GetCrewHealth(Victim)!.Brute);
        Assert.Equal(0, world.GetCrewHealth(Attacker)!.Brute);
        Assert.Equal(ConsciousState.Conscious, world.GetCrewHealth(Victim)!.ConsciousState);
    }

    [Fact]
    public void HelpIntentOnCrewAppliesNothing()
    {
        // FC-SC-3: only harm intent produces an outcome on a crew body in this slice.
        SharedStationInventoryWorld world = TwoAdjacentCrew();

        Assert.False(world.TryInteract(Attacker, VictimBody, Intent.Help, false));
        Assert.False(world.TryInteract(Attacker, VictimBody, Intent.Disarm, false));
        Assert.False(world.TryInteract(Attacker, VictimBody, Intent.Grab, false));
        Assert.Equal(0, world.GetCrewHealth(Victim)!.Brute);
    }

    [Fact]
    public void SelfHarmIsRefused()
    {
        // Deviation recorded in the contract: a harm click on one's own body does nothing.
        SharedStationInventoryWorld world = TwoAdjacentCrew();

        Assert.False(world.TryInteract(Attacker, AttackerBody, Intent.Harm, false));
        Assert.Equal(0, world.GetCrewHealth(Attacker)!.Brute);
    }

    [Fact]
    public void OutOfReachHarmIsRefused()
    {
        // FC-SC-3: the dispatcher's reach validation applies to bodies like any other target.
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(Attacker, new Vector2(0, 1)));
        Assert.True(world.AddActor(Victim, new Vector2(5, 1)));

        Assert.False(world.TryInteract(Attacker, VictimBody, Intent.Harm, false));
        Assert.Equal(0, world.GetCrewHealth(Victim)!.Brute);
        Assert.False(world.TryInteract(Attacker, StationEntityCatalog.CrewEntityId(99), Intent.Harm, false));
    }

    [Fact]
    public void UnconsciousActorCannotInteract()
    {
        // FC-SC-4: after seven hits the victim is Unconscious and FC-4 refuses every click server-side.
        SharedStationInventoryWorld world = TwoAdjacentCrew();
        Hit(world, 7);
        Assert.Equal(ConsciousState.Unconscious, world.GetCrewHealth(Victim)!.ConsciousState);

        Assert.False(world.TryInteract(Victim, AttackerBody, Intent.Harm, false));
        Assert.False(world.TryInteract(Victim, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.Equal(0, world.GetCrewHealth(Attacker)!.Brute);
    }

    [Fact]
    public void BarelyConsciousVictimCanStillHitBack()
    {
        // FC-SC-4 boundary: BarelyConscious is still refused by FC-4's default (no soft crit), recorded as a
        // TODO(fidelity) in the contract. This test pins the current behavior so a change is deliberate.
        SharedStationInventoryWorld world = TwoAdjacentCrew();
        Hit(world, 5);
        Assert.Equal(ConsciousState.BarelyConscious, world.GetCrewHealth(Victim)!.ConsciousState);

        Assert.False(world.TryInteract(Victim, AttackerBody, Intent.Harm, false));
    }

    [Fact]
    public void DeadActorCannotHarm()
    {
        // FC-SC-2/FC-SC-4: nine hits kill; the dead body refuses to act and stays dead under more hits.
        SharedStationInventoryWorld world = TwoAdjacentCrew();
        Hit(world, 9);
        Assert.Equal(ConsciousState.Dead, world.GetCrewHealth(Victim)!.ConsciousState);

        Assert.False(world.TryInteract(Victim, AttackerBody, Intent.Harm, false));
        Hit(world, 1);
        Assert.Equal(ConsciousState.Dead, world.GetCrewHealth(Victim)!.ConsciousState);
    }

    [Fact]
    public void UnconsciousAndDeadCrewCannotMove()
    {
        // FC-SC-5
        SharedStationInventoryWorld world = TwoAdjacentCrew();
        Assert.True(world.CanActorMove(Victim));
        Assert.False(world.CanActorMove(99));

        Hit(world, 5);
        Assert.True(world.CanActorMove(Victim), "BarelyConscious crew may still move");
        Hit(world, 2);
        Assert.False(world.CanActorMove(Victim));
        Hit(world, 2);
        Assert.False(world.CanActorMove(Victim));
        Assert.True(world.CanActorMove(Attacker));
    }

    [Fact]
    public void UnconsciousAndDeadCrewCannotSpeak()
    {
        // FC-SC-10: speech follows the same live state as movement, but soft crit may still talk.
        SharedStationInventoryWorld world = TwoAdjacentCrew();
        Assert.True(world.CanActorSpeak(Victim));
        Assert.Null(world.GetSpeechRefusal(Victim));
        Assert.False(world.CanActorSpeak(99));
        Assert.NotNull(world.GetSpeechRefusal(99));

        Hit(world, 5);
        Assert.True(world.CanActorSpeak(Victim), "BarelyConscious crew may still speak");
        Hit(world, 2);
        Assert.False(world.CanActorSpeak(Victim));
        Assert.Equal(Godotstation.Engine.Chat.LocalSpeechRules.UnconsciousReason, world.GetSpeechRefusal(Victim));
        Hit(world, 2);
        Assert.False(world.CanActorSpeak(Victim));
        Assert.Equal(Godotstation.Engine.Chat.LocalSpeechRules.DeadReason, world.GetSpeechRefusal(Victim));
        Assert.True(world.CanActorSpeak(Attacker));
    }

    [Fact]
    public void FallingDropsHeldItems()
    {
        // FC-SC-6: the fifth hit takes the victim below Conscious and drops the wrench at the body.
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(Attacker, new Vector2(0, 1)));
        Assert.True(world.AddActor(Victim, new Vector2(-1, 1)));
        Assert.True(world.TryInteract(Victim, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.Equal(Victim, world.GetState(StationEntityCatalog.WrenchId)!.Holder);

        Hit(world, 4);
        Assert.Equal(Victim, world.GetState(StationEntityCatalog.WrenchId)!.Holder);
        Hit(world, 1);

        StationEntityState wrench = world.GetState(StationEntityCatalog.WrenchId)!;
        Assert.Null(wrench.Holder);
        Assert.Equal(new Vector2(-1, 1), wrench.Position);
        Assert.Equal(string.Empty, world.GetHandItemName(Victim, InventoryNamedSlot.rightHand));
    }

    [Fact]
    public void CrewStatusReplicatesAsEntityState()
    {
        // FC-SC-7: the body is a targetable entity with brute/conscious fields in the change feed.
        SharedStationInventoryWorld world = TwoAdjacentCrew();
        Assert.Contains(world.CaptureAll(), state => state.Id == VictimBody && state.Kind == StationEntityKind.Crew);
        var published = new List<StationEntityState>();
        world.EntitiesChanged += changed => published.AddRange(changed);

        Hit(world, 7);

        StationEntityState body = world.GetState(VictimBody)!;
        Assert.True(body.IsTargetable);
        Assert.Equal(new Vector2(-1, 1), body.Position);
        Assert.Equal(7 * CrewHealthRules.UnarmedBruteDamage, body.Get(StationEntityField.Brute));
        Assert.Equal((int)ConsciousState.Unconscious, body.Count(StationEntityField.Conscious));
        Assert.Equal(7, published.Count(state => state.Id == VictimBody));
        Assert.DoesNotContain(published, state => state.Id == AttackerBody);

        var client = new ReplicatedStationEntities();
        client.Apply(published);
        Assert.Equal(VictimBody, client.FindNearestTarget(new Vector2(-1, 1), 0.7f));
        Assert.Equal((int)ConsciousState.Unconscious, client.Get(VictimBody).Count(StationEntityField.Conscious));
    }

    [Fact]
    public void RemovedCrewPublishesAnUntargetableBody()
    {
        // FC-SC-7: a departing peer's body stops being a click target on every client.
        SharedStationInventoryWorld world = TwoAdjacentCrew();
        var published = new List<StationEntityState>();
        world.EntitiesChanged += changed => published.AddRange(changed);

        Assert.True(world.RemoveActor(Victim));

        StationEntityState last = published.Last(state => state.Id == VictimBody);
        Assert.False(last.IsTargetable);
        Assert.Null(world.GetState(VictimBody));
        Assert.DoesNotContain(world.CaptureAll(), state => state.Id == VictimBody);
    }

    [Fact]
    public void RestartRestoresFullHealth()
    {
        // FC-SC-9: the adapter rebuilds the world after SharedRoundLifecycle.TryRestart; re-added crew start fresh.
        var lifecycle = new SharedRoundLifecycle();
        Assert.Equal(RoundStartResult.Started, lifecycle.TryStart(1));
        SharedStationInventoryWorld world = TwoAdjacentCrew();
        Hit(world, 9);
        Assert.True(world.GetCrewHealth(Victim)!.IsDead);

        Assert.Equal(RoundEndResult.Ended, lifecycle.TryEnd(1, lifecycle.RoundId));
        Assert.Equal(RoundRestartResult.Restarted, lifecycle.TryRestart(1));
        SharedStationInventoryWorld fresh = new();
        Assert.True(fresh.AddActor(Victim, new Vector2(-1, 1)));

        Assert.Equal(0, fresh.GetCrewHealth(Victim)!.Brute);
        Assert.Equal(ConsciousState.Conscious, fresh.GetCrewHealth(Victim)!.ConsciousState);
        Assert.True(fresh.CanActorMove(Victim));
        // The same world object also forgets a removed body's damage when the peer re-joins.
        Assert.True(world.RemoveActor(Victim));
        Assert.True(world.AddActor(Victim, new Vector2(-1, 1)));
        Assert.Equal(0, world.GetCrewHealth(Victim)!.Brute);
    }
}
