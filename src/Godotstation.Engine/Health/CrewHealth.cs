using System;

namespace Godotstation.Engine.Health;

/// <summary>
/// tgstation mob stat ladder (<c>code/__DEFINES/stat.dm</c>, <c>set_stat</c> in
/// <c>code/modules/mob/living/living.dm</c>). Death is final in this slice.
/// </summary>
public enum CrewStat
{
    Stable = 0,
    SoftCrit = 1,
    HardCrit = 2,
    Dead = 3,
}

/// <summary>The traits tgstation's <c>set_stat</c> attaches to each stat.</summary>
public static class CrewStatRules
{
    /// <summary>Stable crew can use hands; every critical stat adds <c>TRAIT_HANDS_BLOCKED</c>.</summary>
    public static bool CanUseHands(this CrewStat stat) => stat == CrewStat.Stable;

    /// <summary>Soft crit floors the mob but it may still crawl; hard crit and death knock it out.</summary>
    public static bool CanMove(this CrewStat stat) => stat is CrewStat.Stable or CrewStat.SoftCrit;

    /// <summary>Every critical stat adds <c>TRAIT_FLOORED</c>; a floored mob that can move crawls.</summary>
    public static bool IsFloored(this CrewStat stat) => stat != CrewStat.Stable;

    /// <summary>Hard crit and death add <c>TRAIT_DEAF</c>; such crew do not hear local speech.</summary>
    public static bool CanHear(this CrewStat stat) => stat is CrewStat.Stable or CrewStat.SoftCrit;

    /// <summary>Hard crit is knocked out and cannot talk; the dead speak only to the dead.</summary>
    public static bool CanSpeakLocally(this CrewStat stat) => stat is CrewStat.Stable or CrewStat.SoftCrit;

    /// <summary>Soft crit adds <c>TRAIT_FORCE_WHISPER</c>.</summary>
    public static bool IsForcedToWhisper(this CrewStat stat) => stat == CrewStat.SoftCrit;

    public static string DisplayName(this CrewStat stat) => stat switch
    {
        CrewStat.Stable => "Stable",
        CrewStat.SoftCrit => "Critical",
        CrewStat.HardCrit => "Unconscious",
        CrewStat.Dead => "Dead",
        _ => "Unknown",
    };
}

/// <summary>
/// Server-owned health of one crew body, following tgstation carbon rules at
/// commit f37d36c: health is <c>MAX_LIVING_HEALTH</c> minus brute and oxygen
/// loss; <c>update_stat</c> moves through soft crit (≤ 0), hard crit (≤ −30)
/// and death (≤ −100); while critical, <c>breathe</c> misses breaths and
/// <c>handle_suffocation</c> adds oxygen loss, so an untreated casualty dies.
/// Limbs, wounds, burn/toxin damage, blood, and revival are outside this slice.
/// </summary>
public sealed class CrewHealth
{
    public const double MaxHealth = 100;
    public const double CritThreshold = 0;
    public const double HardCritThreshold = -30;
    public const double DeadThreshold = -100;

    /// <summary>SSmobs fires every 2 seconds.</summary>
    public const double LifeTickSeconds = 2;

    /// <summary>A healthy carbon breathes every fourth life tick.</summary>
    public const int BreathTickInterval = 4;

    private long lifeTicks;
    private double tickAccumulator;
    private double loseBreath;
    private bool failedLastBreath;

    public double BruteLoss { get; private set; }
    public double OxyLoss { get; private set; }
    public double Health => MaxHealth - BruteLoss - OxyLoss;
    public CrewStat Stat { get; private set; } = CrewStat.Stable;

    public bool IsDead => Stat == CrewStat.Dead;

    public bool CanUseHands => Stat.CanUseHands();
    public bool CanMove => Stat.CanMove();

    /// <summary>Applies brute damage from an attack. Dead bodies still take damage but stay dead.</summary>
    public bool ApplyBrute(double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0) return false;
        BruteLoss += amount;
        UpdateStat();
        return true;
    }

    /// <summary>Heals brute damage, never below zero. Returns the amount actually healed.</summary>
    public double HealBrute(double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0 || IsDead) return 0;
        double healed = Math.Min(amount, BruteLoss);
        BruteLoss -= healed;
        UpdateStat();
        return healed;
    }

    /// <summary>Heals oxygen loss, never below zero. Returns the amount actually healed.</summary>
    public double HealOxy(double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0 || IsDead) return 0;
        double healed = Math.Min(amount, OxyLoss);
        OxyLoss -= healed;
        UpdateStat();
        return healed;
    }

    /// <summary>Advances life ticks. Returns true when any replicated value changed.</summary>
    public bool Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return false;
        tickAccumulator += elapsedSeconds;
        bool changed = false;
        while (tickAccumulator >= LifeTickSeconds)
        {
            tickAccumulator -= LifeTickSeconds;
            changed |= LifeTick();
        }
        return changed;
    }

    private bool LifeTick()
    {
        lifeTicks++;
        if (IsDead) return false;
        if (lifeTicks % BreathTickInterval != 0 && !failedLastBreath) return false;

        double before = OxyLoss;
        CrewStat statBefore = Stat;
        Breathe();
        UpdateStat();
        return before != OxyLoss || statBefore != Stat;
    }

    private void Breathe()
    {
        if (Stat == CrewStat.HardCrit) loseBreath += 1;
        else if (Stat == CrewStat.SoftCrit) loseBreath += 0.25;

        if (loseBreath >= 1)
        {
            // A missed breath is an empty breath: zero-pressure suffocation.
            loseBreath -= 1;
            failedLastBreath = true;
            OxyLoss += Health >= CritThreshold ? 3 : 1;
            return;
        }

        // The playtest station always has breathable air.
        failedLastBreath = false;
        if (Health >= CritThreshold) OxyLoss = Math.Max(0, OxyLoss - 5);
    }

    private void UpdateStat()
    {
        if (IsDead) return;
        if (Health <= DeadThreshold) Stat = CrewStat.Dead;
        else if (Health <= HardCritThreshold) Stat = CrewStat.HardCrit;
        else if (Health <= CritThreshold) Stat = CrewStat.SoftCrit;
        else Stat = CrewStat.Stable;
    }
}
