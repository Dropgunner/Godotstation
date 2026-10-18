namespace Godotstation.Engine.Rounds;

public enum SharedRoundPhase
{
    PreRound,
    Started,
    Ended,
}

public enum RoundStartResult
{
    Started,
    AlreadyStarted,
    NotHost,
    RoundEnded,
}

public enum RoundEndResult
{
    Ended,
    NotHost,
    NotStarted,
    StaleRound,
}

public enum RoundRestartResult
{
    Restarted,
    NotHost,
    NotEnded,
}

/// <summary>Server-owned playtest phase and round identity across start, end, and restart.</summary>
public sealed class SharedRoundLifecycle
{
    public SharedRoundPhase Phase { get; private set; } = SharedRoundPhase.PreRound;
    public int RoundId { get; private set; } = 1;
    public bool CanRequestJob => Phase == SharedRoundPhase.Started;

    public RoundStartResult TryStart(int requestingPeerId)
    {
        if (requestingPeerId != 1) return RoundStartResult.NotHost;
        if (Phase == SharedRoundPhase.Started) return RoundStartResult.AlreadyStarted;
        if (Phase == SharedRoundPhase.Ended) return RoundStartResult.RoundEnded;

        Phase = SharedRoundPhase.Started;
        return RoundStartResult.Started;
    }

    public RoundEndResult TryEnd(int requestingPeerId, int roundId)
    {
        if (requestingPeerId != 1) return RoundEndResult.NotHost;
        if (roundId != RoundId) return RoundEndResult.StaleRound;
        if (Phase != SharedRoundPhase.Started) return RoundEndResult.NotStarted;

        Phase = SharedRoundPhase.Ended;
        return RoundEndResult.Ended;
    }

    public RoundRestartResult TryRestart(int requestingPeerId)
    {
        if (requestingPeerId != 1) return RoundRestartResult.NotHost;
        if (Phase != SharedRoundPhase.Ended) return RoundRestartResult.NotEnded;

        RoundId++;
        Phase = SharedRoundPhase.PreRound;
        return RoundRestartResult.Restarted;
    }
}
