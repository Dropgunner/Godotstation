using System;

namespace Godotstation.Engine.Health;

/// <summary>
/// tgstation <c>/obj/item/stack/medical/bruise_pack</c> at commit f37d36c:
/// heals 40 brute after a 4-second self-application or a 2-second application
/// to someone else, consuming one pack. The stack starts with six packs and
/// does not work on the dead. The source heals one limb; this slice heals
/// total brute because crew bodies have no limbs yet, and it does not loop.
/// </summary>
public sealed class BruisePackTreatment
{
    public const double HealBrute = 40;
    public const double SelfDelaySeconds = 4;
    public const double OtherDelaySeconds = 2;
    public const int StackAmount = 6;

    public int? HealerPeerId { get; private set; }
    public int? PatientPeerId { get; private set; }
    public double ProgressSeconds { get; private set; }
    public double DurationSeconds { get; private set; }

    public static double DelayFor(int healerPeerId, int patientPeerId) =>
        healerPeerId == patientPeerId ? SelfDelaySeconds : OtherDelaySeconds;

    /// <summary>Starts treating a living, injured patient. A healer runs one treatment at a time.</summary>
    public bool TryStart(int healerPeerId, int patientPeerId, CrewHealth patient)
    {
        ArgumentNullException.ThrowIfNull(patient);
        if (healerPeerId <= 0 || patientPeerId <= 0 || HealerPeerId.HasValue ||
            patient.IsDead || patient.BruteLoss <= 0)
            return false;
        HealerPeerId = healerPeerId;
        PatientPeerId = patientPeerId;
        ProgressSeconds = 0;
        DurationSeconds = DelayFor(healerPeerId, patientPeerId);
        return true;
    }

    /// <summary>
    /// Advances the do-after. It is cancelled when <paramref name="stillValid"/> is false
    /// (healer incapacitated, pack no longer in the active hand, or patient out of reach).
    /// Returns true on the tick the treatment completes; the caller then heals and consumes.
    /// </summary>
    public bool Advance(double elapsedSeconds, bool stillValid)
    {
        if (!HealerPeerId.HasValue || !double.IsFinite(elapsedSeconds) || elapsedSeconds < 0) return false;
        if (!stillValid)
        {
            Cancel();
            return false;
        }
        ProgressSeconds = Math.Min(DurationSeconds, ProgressSeconds + elapsedSeconds);
        if (ProgressSeconds < DurationSeconds) return false;
        return true;
    }

    public void Cancel()
    {
        HealerPeerId = null;
        PatientPeerId = null;
        ProgressSeconds = 0;
        DurationSeconds = 0;
    }
}

/// <summary>tgstation melee timing and the force of the one weapon in the playtest.</summary>
public static class CrewMelee
{
    /// <summary><c>CLICK_CD_MELEE</c> is 8 deciseconds.</summary>
    public const double ClickCooldownSeconds = 0.8;

    /// <summary><c>/obj/item/wrench</c> has <c>force = 5</c> brute.</summary>
    public const double WrenchForce = 5;
}
