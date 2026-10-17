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
}
