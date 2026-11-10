using Godotstation.Engine.Health;
using Xunit;

namespace Godotstation.Tests.Health;

public sealed class CrewHealthTests
{
    [Fact]
    public void StatFollowsTgstationThresholds()
    {
        var health = new CrewHealth();
        Assert.Equal(CrewStat.Stable, health.Stat);

        health.ApplyBrute(99);
        Assert.Equal(CrewStat.Stable, health.Stat);
        health.ApplyBrute(1);
        Assert.Equal(0, health.Health);
        Assert.Equal(CrewStat.SoftCrit, health.Stat);
        health.ApplyBrute(30);
        Assert.Equal(CrewStat.HardCrit, health.Stat);
        health.ApplyBrute(70);
        Assert.Equal(CrewStat.Dead, health.Stat);
    }

    [Fact]
    public void DeathIsFinalAndBlocksHealing()
    {
        var health = new CrewHealth();
        health.ApplyBrute(200);
        Assert.True(health.IsDead);
        Assert.Equal(0, health.HealBrute(40));
        Assert.True(health.IsDead);
    }

    [Fact]
    public void HealingAboveCritRestoresStableAndNeverOverheals()
    {
        var health = new CrewHealth();
        health.ApplyBrute(110);
        Assert.Equal(CrewStat.SoftCrit, health.Stat);
        Assert.Equal(40, health.HealBrute(40));
        Assert.Equal(CrewStat.Stable, health.Stat);
        Assert.Equal(70, health.HealBrute(500));
        Assert.Equal(CrewHealth.MaxHealth, health.Health);
    }

    [Fact]
    public void StableCrewDoNotSuffocate()
    {
        var health = new CrewHealth();
        health.ApplyBrute(50);
        Assert.False(health.Advance(600));
        Assert.Equal(0, health.OxyLoss);
        Assert.Equal(50, health.Health);
    }

    [Fact]
    public void HardCritLosesOneHealthPerLifeTickUntilDeath()
    {
        var health = new CrewHealth();
        health.ApplyBrute(130); // health -30: hard crit
        Assert.Equal(CrewStat.HardCrit, health.Stat);

        // The first breath happens on the fourth tick; every missed breath then forces another.
        health.Advance(CrewHealth.LifeTickSeconds * CrewHealth.BreathTickInterval);
        Assert.Equal(1, health.OxyLoss);
        health.Advance(CrewHealth.LifeTickSeconds * 10);
        Assert.Equal(11, health.OxyLoss);

        health.Advance(CrewHealth.LifeTickSeconds * 100);
        Assert.Equal(CrewStat.Dead, health.Stat);
        Assert.Equal(CrewHealth.DeadThreshold, health.Health);
        double atDeath = health.OxyLoss;
        health.Advance(600);
        Assert.Equal(atDeath, health.OxyLoss);
    }

    [Fact]
    public void SoftCritMissesOneBreathInFourAndDriftsTowardHardCrit()
    {
        var health = new CrewHealth();
        health.ApplyBrute(101); // health -1
        // Breaths on ticks 4, 8, 12, 16 accumulate 0.25 each; the fourth is missed.
        health.Advance(CrewHealth.LifeTickSeconds * 16);
        Assert.Equal(1, health.OxyLoss);
        Assert.Equal(CrewStat.SoftCrit, health.Stat);

        health.Advance(60 * 60);
        Assert.NotEqual(CrewStat.SoftCrit, health.Stat);
    }

    [Fact]
    public void SuccessfulBreathHealsOxygenLossOnceOutOfCrit()
    {
        var health = new CrewHealth();
        health.ApplyBrute(130);
        health.Advance(CrewHealth.LifeTickSeconds * 14); // 11 oxy loss
        Assert.Equal(11, health.OxyLoss);
        health.HealBrute(130);
        Assert.Equal(CrewStat.Stable, health.Stat);
        health.Advance(CrewHealth.LifeTickSeconds * 2); // one more failed-breath catch-up, then healthy cadence
        health.Advance(CrewHealth.LifeTickSeconds * 16);
        Assert.Equal(0, health.OxyLoss);
    }

    [Theory]
    [InlineData(CrewStat.Stable, true, true, true, false)]
    [InlineData(CrewStat.SoftCrit, false, true, true, true)]
    [InlineData(CrewStat.HardCrit, false, false, false, false)]
    [InlineData(CrewStat.Dead, false, false, false, false)]
    public void StatTraitsMatchSetStat(CrewStat stat, bool hands, bool move, bool hear, bool whisper)
    {
        Assert.Equal(hands, stat.CanUseHands());
        Assert.Equal(move, stat.CanMove());
        Assert.Equal(hear, stat.CanHear());
        Assert.Equal(move, stat.CanSpeakLocally());
        Assert.Equal(whisper, stat.IsForcedToWhisper());
        Assert.Equal(stat != CrewStat.Stable, stat.IsFloored());
    }

    [Fact]
    public void BruisePackUsesSelfAndOtherDelays()
    {
        Assert.Equal(4, BruisePackTreatment.DelayFor(1, 1));
        Assert.Equal(2, BruisePackTreatment.DelayFor(1, 2));

        var treatment = new BruisePackTreatment();
        var patient = new CrewHealth();
        Assert.False(treatment.TryStart(1, 2, patient)); // nothing to heal
        patient.ApplyBrute(10);
        Assert.True(treatment.TryStart(1, 2, patient));
        Assert.False(treatment.TryStart(1, 2, patient));
        Assert.False(treatment.Advance(1.5, stillValid: true));
        Assert.True(treatment.Advance(0.5, stillValid: true));
    }

    [Fact]
    public void BruisePackTreatmentCancelsWhenInvalid()
    {
        var treatment = new BruisePackTreatment();
        var patient = new CrewHealth();
        patient.ApplyBrute(10);
        Assert.True(treatment.TryStart(3, 3, patient));
        Assert.False(treatment.Advance(3, stillValid: false));
        Assert.Null(treatment.HealerPeerId);
        Assert.Equal(0, treatment.ProgressSeconds);
    }
}
