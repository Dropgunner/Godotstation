using Godotstation.Engine.Rounds;
using Xunit;

namespace Godotstation.Tests.Rounds;

public sealed class SharedRoundLifecycleTests
{
    [Fact]
    public void NewShiftWaitsInPreRoundUntilHostStartsIt()
    {
        var lifecycle = new SharedRoundLifecycle();

        Assert.Equal(SharedRoundPhase.PreRound, lifecycle.Phase);
        Assert.False(lifecycle.CanRequestJob);
        Assert.Equal(RoundStartResult.Started, lifecycle.TryStart(1));
        Assert.Equal(SharedRoundPhase.Started, lifecycle.Phase);
        Assert.True(lifecycle.CanRequestJob);
    }

    [Fact]
    public void OnlyHostCanStartAndStartIsIdempotentlyRejected()
    {
        var lifecycle = new SharedRoundLifecycle();

        Assert.Equal(RoundStartResult.NotHost, lifecycle.TryStart(2));
        Assert.Equal(SharedRoundPhase.PreRound, lifecycle.Phase);
        Assert.Equal(RoundStartResult.Started, lifecycle.TryStart(1));
        Assert.Equal(RoundStartResult.AlreadyStarted, lifecycle.TryStart(1));
    }

    [Fact]
    public void OnlyHostCanEndAndRestartTheCurrentRound()
    {
        var lifecycle = new SharedRoundLifecycle();
        Assert.Equal(RoundEndResult.NotStarted, lifecycle.TryEnd(1, 1));
        Assert.Equal(RoundRestartResult.NotEnded, lifecycle.TryRestart(1));
        Assert.Equal(RoundStartResult.Started, lifecycle.TryStart(1));
        Assert.Equal(RoundEndResult.NotHost, lifecycle.TryEnd(2, 1));
        Assert.Equal(RoundEndResult.StaleRound, lifecycle.TryEnd(1, 0));
        Assert.Equal(RoundEndResult.Ended, lifecycle.TryEnd(1, 1));
        Assert.False(lifecycle.CanRequestJob);
        Assert.Equal(RoundStartResult.RoundEnded, lifecycle.TryStart(1));
        Assert.Equal(RoundEndResult.NotStarted, lifecycle.TryEnd(1, 1));
        Assert.Equal(RoundRestartResult.NotHost, lifecycle.TryRestart(2));
        Assert.Equal(RoundRestartResult.Restarted, lifecycle.TryRestart(1));
        Assert.Equal(2, lifecycle.RoundId);
        Assert.Equal(SharedRoundPhase.PreRound, lifecycle.Phase);
        Assert.Equal(RoundRestartResult.NotEnded, lifecycle.TryRestart(1));
        Assert.Equal(RoundStartResult.Started, lifecycle.TryStart(1));
        Assert.Equal(RoundEndResult.StaleRound, lifecycle.TryEnd(1, 1));
        Assert.Equal(RoundEndResult.Ended, lifecycle.TryEnd(1, 2));
    }
}
