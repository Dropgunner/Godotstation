using System;
using Godotstation.Common.Interactions;

namespace Godotstation.Engine.Health;

/// <summary>
/// Unitystation's conscious-state thresholds over one bounded brute pool (FC-SC-1, FC-SC-2).
/// Source: HealthV2/Living/LivingHealthMasterBase.cs CalculateOverallHealth and
/// SetConsciousStateRespectHealth at c911581e8354958d664b1e32087053ce43638790.
/// </summary>
public static class CrewHealthRules
{
    /// <summary>HealthStateController default max health.</summary>
    public const double MaxHealth = 100;

    /// <summary>Damage is clamped here so health never drops below MaxHealth - MaxBrute = -150.</summary>
    public const double MaxBrute = 250;

    /// <summary>
    /// Deviation (playtest-scaled): Unitystation's HumanoidArm.ArmMeleeDamage is 5. This slice uses 25
    /// so two people reach incapacitation and death in nine hits, as recorded in the contract.
    /// </summary>
    public const double UnarmedBruteDamage = 25;

    /// <summary>
    /// FC-HL-1: one bruise pack application heals 40 of its heal type (Brute)
    /// (HealsTheLiving.ServerApplyHeal → HealDamage(null, 40, healType, ...); Bruise Pack.prefab healType 0 = Brute).
    /// </summary>
    public const double BruisePackHeal = 40;

    /// <summary>FC-HL-1: Bruise Pack.prefab sets Stackable initialAmount and maxAmount to 6.</summary>
    public const int BruisePackUses = 6;

    public static double HealthFor(double brute) => MaxHealth - brute;

    /// <summary>health &lt; 0 → BarelyConscious, &lt; -50 → Unconscious, &lt; -100 → Dead.</summary>
    public static ConsciousState StateFor(double brute)
    {
        double health = HealthFor(brute);
        if (health < -100) return ConsciousState.Dead;
        if (health < -50) return ConsciousState.Unconscious;
        if (health < 0) return ConsciousState.BarelyConscious;
        return ConsciousState.Conscious;
    }

    /// <summary>Unconscious and dead bodies refuse movement input (PlayerHealthV2 lines 87–94).</summary>
    public static bool CanMove(ConsciousState state) =>
        state is ConsciousState.Conscious or ConsciousState.BarelyConscious;
}

/// <summary>One crew member's authoritative brute damage and derived conscious state.</summary>
public sealed class CrewHealth
{
    public double Brute { get; private set; }
    public ConsciousState ConsciousState { get; private set; } = ConsciousState.Conscious;
    public double Health => CrewHealthRules.HealthFor(Brute);
    public bool IsDead => ConsciousState == ConsciousState.Dead;

    /// <summary>Applies brute damage; death is terminal (LivingHealthMasterBase.Death ignores a dead body).</summary>
    /// <returns>The state before the hit, so callers can react to a worsening transition.</returns>
    public ConsciousState ApplyBrute(double amount)
    {
        if (!double.IsFinite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        ConsciousState previous = ConsciousState;
        Brute = Math.Min(CrewHealthRules.MaxBrute, Brute + amount);
        ConsciousState derived = CrewHealthRules.StateFor(Brute);
        if (previous != ConsciousState.Dead) ConsciousState = derived;
        return previous;
    }

    /// <summary>
    /// FC-HL-2: removes brute damage from a living body and re-derives its conscious state from health
    /// (LivingHealthMasterBase.CalculateOverallHealth), so healing above a threshold restores the better
    /// state. A dead body is not healed (FC-SC-2; revival is not ported).
    /// </summary>
    /// <returns>True when damage was removed.</returns>
    public bool HealBrute(double amount)
    {
        if (!double.IsFinite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (IsDead || Brute <= 0 || amount == 0) return false;
        Brute = Math.Max(0, Brute - amount);
        ConsciousState = CrewHealthRules.StateFor(Brute);
        return true;
    }
}
