using Godotstation.Engine.Construction;
using Xunit;

namespace Godotstation.Tests.Construction;

public sealed class GirderPlatingTaskTests
{
    [Fact]
    public void RequiresEngineerTwoSheetsAndReachBeforeStarting()
    {
        var task = new GirderPlatingTask();

        Assert.False(task.TryStart(1, 1, isReachable: true));
        Assert.False(task.TryStart(1, 2, isReachable: false));
        Assert.True(task.TryStart(1, 2, isReachable: true));
    }

    [Fact]
    public void CompletesAfterFourValidSecondsAndCannotRepeat()
    {
        var task = new GirderPlatingTask();
        Assert.True(task.TryStart(1, 2, true));

        Assert.False(task.Advance(1, 3.9, true, 2));
        Assert.Equal(3.9, task.ProgressSeconds, 6);
        Assert.True(task.Advance(1, 0.1, true, 2));
        Assert.True(task.IsComplete);
        Assert.False(task.TryStart(1, 2, true));
    }

    [Fact]
    public void InvalidatedOrInterruptedWorkResetsProgress()
    {
        var task = new GirderPlatingTask();
        Assert.True(task.TryStart(1, 2, true));
        Assert.False(task.Advance(1, 2, true, 2));
        Assert.False(task.Advance(1, 0.1, false, 2));

        Assert.Equal(0, task.ProgressSeconds);
        Assert.Null(task.WorkerPeerId);
        Assert.False(task.Advance(1, 4, true, 2));
    }

    [Fact]
    public void OnlyTheWorkerCanAdvanceOrCancel()
    {
        var task = new GirderPlatingTask();
        Assert.True(task.TryStart(1, 2, true));

        Assert.False(task.Advance(2, 4, true, 2));
        task.Cancel(2);
        Assert.Equal(1, task.WorkerPeerId);
        Assert.False(task.Advance(1, double.NaN, true, 2));
        Assert.Equal(0, task.ProgressSeconds);
    }
}
