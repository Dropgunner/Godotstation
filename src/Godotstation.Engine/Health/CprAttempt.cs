using System;
using System.Numerics;

namespace Godotstation.Engine.Health;

/// <summary>
/// tgstation <c>human/do_cpr</c> at commit f37d36c: not on yourself and not on the dead; a 3-second
/// do-after, then up to 7 oxygen loss healed if the patient is still at or below the crit threshold.
/// While the patient stays there, the rescuer repeats at the 0.8-second "panic" speed. Masks, lungs
/// and mood are outside this slice.
/// </summary>
public sealed class CprAttempt
{
    public const double FirstDelaySeconds = 3;
    public const double PanicDelaySeconds = 0.8;
    public const double OxyHealed = 7;

    /// <summary>A do-after needs the rescuer to hold still; this is the drift that still counts as still.</summary>
    public const float HoldStillTolerance = 0.2f;

    public int? PatientPeerId { get; private set; }
    public double ProgressSeconds { get; private set; }
    public double DurationSeconds { get; private set; }
    public Vector2 StartPosition { get; private set; }

    public static bool CanReceive(CrewHealth patient) =>
        !patient.IsDead && patient.Health <= CrewHealth.CritThreshold;

    public bool TryStart(int rescuerPeerId, int patientPeerId, CrewHealth patient, Vector2 rescuerPosition)
    {
        ArgumentNullException.ThrowIfNull(patient);
        if (rescuerPeerId <= 0 || patientPeerId <= 0 || rescuerPeerId == patientPeerId ||
            PatientPeerId.HasValue || !CanReceive(patient))
            return false;
        Begin(patientPeerId, FirstDelaySeconds, rescuerPosition);
        return true;
    }

    /// <summary>
    /// Advances the do-after. Returns true on the tick a round of CPR completes; the caller then heals
    /// and calls <see cref="Continue"/> or <see cref="Cancel"/>.
    /// </summary>
    public bool Advance(double elapsedSeconds, bool stillValid)
    {
        if (!PatientPeerId.HasValue || !double.IsFinite(elapsedSeconds) || elapsedSeconds < 0) return false;
        if (!stillValid)
        {
            Cancel();
            return false;
        }
        ProgressSeconds = Math.Min(DurationSeconds, ProgressSeconds + elapsedSeconds);
        return ProgressSeconds >= DurationSeconds;
    }

    /// <summary>Starts the next, faster round on the same patient.</summary>
    public void Continue(Vector2 rescuerPosition)
    {
        if (PatientPeerId is int patient) Begin(patient, PanicDelaySeconds, rescuerPosition);
    }

    public void Cancel()
    {
        PatientPeerId = null;
        ProgressSeconds = 0;
        DurationSeconds = 0;
    }

    private void Begin(int patientPeerId, double duration, Vector2 rescuerPosition)
    {
        PatientPeerId = patientPeerId;
        ProgressSeconds = 0;
        DurationSeconds = duration;
        StartPosition = rescuerPosition;
    }
}
