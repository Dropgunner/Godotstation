using System;

namespace Godotstation.Engine.Construction;

/// <summary>
/// Deterministic server-side rule for Unitystation-style girder plating.
/// Unitystation's Girder interaction consumes two metal sheets after four seconds.
/// </summary>
public sealed class GirderPlatingTask
{
    public const int RequiredSheets = 2;
    public const double DurationSeconds = 4;

    public int? WorkerPeerId { get; private set; }
    public double ProgressSeconds { get; private set; }
    public bool IsComplete { get; private set; }

    public bool TryStart(int peerId, int sheetsInActiveHand, bool isReachable)
    {
        if (peerId <= 0 || sheetsInActiveHand < RequiredSheets || !isReachable ||
            IsComplete || WorkerPeerId.HasValue)
            return false;

        WorkerPeerId = peerId;
        ProgressSeconds = 0;
        return true;
    }

    /// <summary>Advances progress only while the same engineer holds the sheets within reach.</summary>
    public bool Advance(int peerId, double elapsedSeconds, bool stillValid, int sheetsInActiveHand)
    {
        if (WorkerPeerId != peerId || IsComplete || !double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            return false;

        if (!stillValid || sheetsInActiveHand < RequiredSheets)
        {
            WorkerPeerId = null;
            ProgressSeconds = 0;
            return false;
        }

        ProgressSeconds = Math.Min(DurationSeconds, ProgressSeconds + elapsedSeconds);
        if (ProgressSeconds < DurationSeconds) return false;

        IsComplete = true;
        WorkerPeerId = null;
        return true;
    }

    public void Cancel(int peerId)
    {
        if (WorkerPeerId != peerId) return;
        WorkerPeerId = null;
        ProgressSeconds = 0;
    }
}
