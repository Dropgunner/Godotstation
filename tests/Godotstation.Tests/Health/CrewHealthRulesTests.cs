using System;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Health;
using Xunit;

namespace Godotstation.Tests.Health;

/// <summary>FC-SC-1 and FC-SC-2 from planning/FidelityContract_SurvivalConsciousness.md.</summary>
public sealed class CrewHealthRulesTests
{
    [Theory]
    [InlineData(0, ConsciousState.Conscious)]
    [InlineData(100, ConsciousState.Conscious)]        // health 0 is not yet below 0
    [InlineData(100.5, ConsciousState.BarelyConscious)]
    [InlineData(150, ConsciousState.BarelyConscious)]  // health -50 is not yet below -50
    [InlineData(150.5, ConsciousState.Unconscious)]
    [InlineData(200, ConsciousState.Unconscious)]      // health -100 is not yet below -100
    [InlineData(200.5, ConsciousState.Dead)]
    [InlineData(250, ConsciousState.Dead)]
    public void ThresholdsMatchUnitystation(double brute, ConsciousState expected)
    {
        // FC-SC-1: LivingHealthMasterBase.CalculateOverallHealth thresholds at MaxHealth 100.
        Assert.Equal(100, CrewHealthRules.MaxHealth);
        Assert.Equal(expected, CrewHealthRules.StateFor(brute));
    }

    [Fact]
    public void DamageIsClampedAndNonNegative()
    {
        // FC-SC-1: the pool is bounded; negative or non-finite damage is a programming error.
        var health = new CrewHealth();
        health.ApplyBrute(1000);
        Assert.Equal(CrewHealthRules.MaxBrute, health.Brute);
        Assert.Throws<ArgumentOutOfRangeException>(() => health.ApplyBrute(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => health.ApplyBrute(double.NaN));
    }

    [Fact]
    public void UnarmedHitsWalkThroughEveryStateInOrder()
    {
        // FC-SC-1 with the playtest-scaled 25 damage per hit: 5 → BarelyConscious, 7 → Unconscious, 9 → Dead.
        var health = new CrewHealth();
        var seen = new System.Collections.Generic.List<ConsciousState>();
        for (int hit = 1; hit <= 9; hit++)
        {
            health.ApplyBrute(CrewHealthRules.UnarmedBruteDamage);
            seen.Add(health.ConsciousState);
        }
        Assert.Equal(ConsciousState.Conscious, seen[3]);
        Assert.Equal(ConsciousState.BarelyConscious, seen[4]);
        Assert.Equal(ConsciousState.BarelyConscious, seen[5]);
        Assert.Equal(ConsciousState.Unconscious, seen[6]);
        Assert.Equal(ConsciousState.Unconscious, seen[7]);
        Assert.Equal(ConsciousState.Dead, seen[8]);
    }

    [Fact]
    public void DeathIsTerminal()
    {
        // FC-SC-2: once dead, more damage keeps the body dead and nothing in this slice revives it.
        var health = new CrewHealth();
        health.ApplyBrute(201);
        Assert.True(health.IsDead);
        ConsciousState previous = health.ApplyBrute(1000);
        Assert.Equal(ConsciousState.Dead, previous);
        Assert.Equal(ConsciousState.Dead, health.ConsciousState);
    }

    [Theory]
    [InlineData(ConsciousState.Conscious, true)]
    [InlineData(ConsciousState.BarelyConscious, true)]
    [InlineData(ConsciousState.Unconscious, false)]
    [InlineData(ConsciousState.Dead, false)]
    public void OnlyUnconsciousAndDeadCannotMove(ConsciousState state, bool canMove)
    {
        // FC-SC-5: PlayerHealthV2 records ServerAllowInput=false for UNCONSCIOUS and DEAD only.
        Assert.Equal(canMove, CrewHealthRules.CanMove(state));
    }
}
