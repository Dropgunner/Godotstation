using System;
using Godotstation.Engine.Rounds;
using Xunit;

namespace Godotstation.Tests.Rounds;

public sealed class RoundSessionTests
{
    [Fact]
    public void StartProgressAndResetProduceFreshRound()
    {
        var session = new RoundSession(3, 90);

        session.Start();
        Assert.Equal(RoundOutcome.InProgress, session.Outcome);
        Assert.Equal(0, session.ObjectiveProgress);
        Assert.Equal(90, session.TimeRemainingSeconds);

        Assert.False(session.TryPerformObjectiveAction("hand"));
        Assert.True(session.TryPerformObjectiveAction(RoundSession.RequiredToolId));
        Assert.True(session.TryPerformObjectiveAction(RoundSession.RequiredToolId));
        Assert.Equal(2, session.ObjectiveProgress);
        Assert.Equal(RoundOutcome.InProgress, session.Outcome);

        session.Reset();
        AssertFresh(session);

        session.Start();
        session.AdvanceTime(90);
        Assert.Equal(RoundOutcome.Failure, session.Outcome);
        session.Start();
        AssertFreshInProgress(session);
    }

    [Fact]
    public void ObjectiveCompletionWinsAndTerminalStateDoesNotChange()
    {
        var session = new RoundSession(2, 30);
        session.Start();

        Assert.True(session.TryPerformObjectiveAction(RoundSession.RequiredToolId));
        Assert.True(session.TryPerformObjectiveAction(RoundSession.RequiredToolId));
        Assert.Equal(RoundOutcome.Success, session.Outcome);

        var timeAtSuccess = session.TimeRemainingSeconds;
        Assert.False(session.TryPerformObjectiveAction(RoundSession.RequiredToolId));
        session.AdvanceTime(100);
        Assert.Equal(RoundOutcome.Success, session.Outcome);
        Assert.Equal(timeAtSuccess, session.TimeRemainingSeconds);
        Assert.Equal(2, session.ObjectiveProgress);

        session.Start();
        AssertFreshInProgress(session, 30);
    }

    [Fact]
    public void TimeExpiresAtZeroAndRejectsInvalidDurations()
    {
        var session = new RoundSession(1, 10);
        session.Start();
        session.AdvanceTime(3.5);
        Assert.Equal(6.5, session.TimeRemainingSeconds);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.AdvanceTime(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.AdvanceTime(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.AdvanceTime(double.PositiveInfinity));

        session.AdvanceTime(6.5);
        Assert.Equal(0, session.TimeRemainingSeconds);
        Assert.Equal(RoundOutcome.Failure, session.Outcome);
        Assert.False(session.TryPerformObjectiveAction(RoundSession.RequiredToolId));
    }

    [Fact]
    public void ConstructorRejectsInvalidConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RoundSession(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RoundSession(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RoundSession(1, double.NaN));
    }

    private static void AssertFresh(RoundSession session)
    {
        Assert.Equal(RoundOutcome.NotStarted, session.Outcome);
        Assert.Equal(0, session.ObjectiveProgress);
        Assert.Equal(90, session.TimeRemainingSeconds);
    }

    private static void AssertFreshInProgress(RoundSession session, double initialTime = 90)
    {
        Assert.Equal(RoundOutcome.InProgress, session.Outcome);
        Assert.Equal(0, session.ObjectiveProgress);
        Assert.Equal(initialTime, session.TimeRemainingSeconds);
    }
}
