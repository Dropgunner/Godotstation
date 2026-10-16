using System;

namespace Godotstation.Engine.Rounds;

public enum RoundOutcome
{
    NotStarted,
    InProgress,
    Success,
    Failure,
}

/// <summary>Deterministic, engine-owned lifecycle for the Wave 8 playable round.</summary>
public sealed class RoundSession
{
    public const string RequiredToolId = "wrench";

    private readonly int objectiveActionsRequired;
    private readonly double timeLimitSeconds;

    public RoundOutcome Outcome { get; private set; }
    public int ObjectiveProgress { get; private set; }
    public double TimeRemainingSeconds { get; private set; }
    public int ObjectiveActionsRequired => objectiveActionsRequired;

    public RoundSession(int objectiveActionsRequired, double timeLimitSeconds)
    {
        if (objectiveActionsRequired <= 0)
            throw new ArgumentOutOfRangeException(nameof(objectiveActionsRequired));
        if (double.IsFinite(timeLimitSeconds) == false || timeLimitSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeLimitSeconds));

        this.objectiveActionsRequired = objectiveActionsRequired;
        this.timeLimitSeconds = timeLimitSeconds;
        Reset();
    }

    public void Start()
    {
        Reset();
        Outcome = RoundOutcome.InProgress;
    }

    public bool TryPerformObjectiveAction(string toolId)
    {
        if (Outcome != RoundOutcome.InProgress ||
            string.Equals(toolId, RequiredToolId, StringComparison.Ordinal) == false)
            return false;

        ObjectiveProgress++;
        if (ObjectiveProgress >= objectiveActionsRequired)
            Outcome = RoundOutcome.Success;
        return true;
    }

    public void AdvanceTime(double elapsedSeconds)
    {
        if (double.IsFinite(elapsedSeconds) == false || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (Outcome != RoundOutcome.InProgress || elapsedSeconds == 0) return;

        TimeRemainingSeconds = Math.Max(0, TimeRemainingSeconds - elapsedSeconds);
        if (TimeRemainingSeconds == 0 && ObjectiveProgress < objectiveActionsRequired)
            Outcome = RoundOutcome.Failure;
    }

    public void Reset()
    {
        Outcome = RoundOutcome.NotStarted;
        ObjectiveProgress = 0;
        TimeRemainingSeconds = timeLimitSeconds;
    }
}
