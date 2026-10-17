namespace Godotstation.Engine.Rounds;

public enum SharedRoundPhase
{
    PreRound,
    Started,
}

public enum RoundStartResult
{
    Started,
    AlreadyStarted,
    NotHost,
}

/// <summary>Minimal server-owned shift gate matching Unitystation's pre-round/start boundary.</summary>
public sealed class SharedRoundLifecycle
{
    public SharedRoundPhase Phase { get; private set; } = SharedRoundPhase.PreRound;
    public bool CanRequestJob => Phase == SharedRoundPhase.Started;

    public RoundStartResult TryStart(int requestingPeerId)
    {
        if (requestingPeerId != 1) return RoundStartResult.NotHost;
        if (Phase == SharedRoundPhase.Started) return RoundStartResult.AlreadyStarted;

        Phase = SharedRoundPhase.Started;
        return RoundStartResult.Started;
    }
}
