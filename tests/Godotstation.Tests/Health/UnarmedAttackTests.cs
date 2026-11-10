using System;
using System.Collections.Generic;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Interactions;
using Xunit;

namespace Godotstation.Tests.Health;

public sealed class UnarmedAttackTests
{
    /// <summary>Returns scripted rolls: integers for damage, doubles (0..1) for the miss check.</summary>
    private sealed class ScriptedRandom(int damage, double missRoll) : Random
    {
        public override int Next(int minValue, int maxValue) => Math.Clamp(damage, minValue, maxValue - 1);
        public override double NextDouble() => missRoll;
    }

    private const int Attacker = 2;
    private const int Victim = 1;

    private static SharedStationInventoryWorld World(Random random)
    {
        var world = new SharedStationInventoryWorld { Random = random };
        Assert.True(world.AddActor(Victim, new Vector2(-1, 0)));
        Assert.True(world.AddActor(Attacker, new Vector2(-1, 1)));
        return world;
    }

    [Fact]
    public void PunchMissChanceIsTenPercentPlusHalfTheAttackersBruteCappedAtEighty()
    {
        Assert.Equal(10, UnarmedAttack.PunchMissChance(0));
        Assert.Equal(35, UnarmedAttack.PunchMissChance(50));
        Assert.Equal(80, UnarmedAttack.PunchMissChance(140));
        Assert.Equal(80, UnarmedAttack.PunchMissChance(1000));
    }

    [Fact]
    public void PunchDamageStaysBetweenFiveAndTenAndKicksBetweenSevenAndFifteen()
    {
        var random = new Random(1234);
        for (int i = 0; i < 2000; i++)
        {
            UnarmedAttackResult punch = UnarmedAttack.Roll(0, targetLyingDown: false, random);
            if (punch.Hit) Assert.InRange(punch.Damage, 5, 10);
            else Assert.Equal(0, punch.Damage);
            Assert.Equal("punch", punch.Verb);

            UnarmedAttackResult kick = UnarmedAttack.Roll(0, targetLyingDown: true, random);
            Assert.True(kick.Hit);
            Assert.InRange(kick.Damage, 7, 15);
            Assert.Equal("kicks", kick.VerbContinuous);
        }
    }

    [Fact]
    public void AnUninjuredAttackerMissesAboutOneInTen()
    {
        var random = new Random(99);
        int misses = 0;
        for (int i = 0; i < 20000; i++)
            if (!UnarmedAttack.Roll(0, targetLyingDown: false, random).Hit) misses++;
        Assert.InRange(misses, 1700, 2300);
    }

    [Fact]
    public void EmptyHandHarmPunchesAStandingCrewMember()
    {
        SharedStationInventoryWorld world = World(new ScriptedRandom(damage: 8, missRoll: 0.5));
        var swings = new List<UnarmedAttackResult>();
        world.CrewStruckUnarmed += (_, _, result) => swings.Add(result);

        Assert.True(world.TryInteract(Attacker, Victim, Intent.Harm, false));

        Assert.Equal(92, world.GetCrewHealth(Victim));
        Assert.Equal(new UnarmedAttackResult(true, 8, "punch", "punches"), Assert.Single(swings));
    }

    [Fact]
    public void AMissDealsNothingButStillCostsTheCooldown()
    {
        SharedStationInventoryWorld world = World(new ScriptedRandom(damage: 10, missRoll: 0.05));
        var swings = new List<UnarmedAttackResult>();
        world.CrewStruckUnarmed += (_, _, result) => swings.Add(result);

        Assert.True(world.TryInteract(Attacker, Victim, Intent.Harm, false));
        Assert.False(Assert.Single(swings).Hit);
        Assert.Equal(CrewHealth.MaxHealth, world.GetCrewHealth(Victim));
        Assert.False(world.TryInteract(Attacker, Victim, Intent.Harm, false));
        world.AdvanceHealth(CrewMelee.ClickCooldownSeconds);
        Assert.True(world.TryInteract(Attacker, Victim, Intent.Harm, false));
    }

    [Fact]
    public void ACollapsedCrewMemberIsKickedAndKicksNeverMiss()
    {
        // The miss roll of 0 would make every punch miss; kicks ignore it.
        SharedStationInventoryWorld world = World(new ScriptedRandom(damage: 10, missRoll: 0.0));
        Assert.True(world.TryInteract(Attacker, StationEntityCatalog.WrenchId, Intent.Help, false));
        while (world.GetCrewStat(Victim) == CrewStat.Stable)
        {
            Assert.True(world.TryInteract(Attacker, Victim, Intent.Harm, false));
            world.AdvanceHealth(CrewMelee.ClickCooldownSeconds);
        }
        Assert.True(world.TryToggleActiveHand(Attacker));
        var swings = new List<UnarmedAttackResult>();
        world.CrewStruckUnarmed += (_, _, result) => swings.Add(result);

        Assert.True(world.TryInteract(Attacker, Victim, Intent.Harm, false));

        Assert.Equal(new UnarmedAttackResult(true, 10, "kick", "kicks"), Assert.Single(swings));
        Assert.Equal(-10, world.GetCrewHealth(Victim));
    }

    [Fact]
    public void UnarmedAttacksNeedReachAndWorkingHands()
    {
        SharedStationInventoryWorld world = World(new ScriptedRandom(damage: 8, missRoll: 0.5));
        Assert.True(world.SetActorPosition(Victim, new Vector2(-1, -3)));
        Assert.False(world.TryInteract(Attacker, Victim, Intent.Harm, false));
        Assert.Equal(CrewHealth.MaxHealth, world.GetCrewHealth(Victim));
    }
}
