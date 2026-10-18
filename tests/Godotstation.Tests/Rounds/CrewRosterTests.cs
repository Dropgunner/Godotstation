using System;
using Godotstation.Engine.Rounds;
using Xunit;

namespace Godotstation.Tests.Rounds;

public sealed class CrewRosterTests
{
    [Fact]
    public void CatalogUsesSourcedUnitystationIdsAndPositionLimits()
    {
        var roster = StationCrewJobCatalog.CreateRoster();

        Assert.True(roster.TryGetJob(StationCrewJobCatalog.AssistantJobId, out CrewJob? assistant));
        Assert.Equal("Assistant", assistant!.Name);
        Assert.Equal(99, assistant.PositionLimit);
        Assert.True(roster.TryGetJob(StationCrewJobCatalog.EngineerJobId, out CrewJob? engineer));
        Assert.Equal("Engineer", engineer!.Name);
        Assert.Equal(5, engineer.PositionLimit);
    }

    [Fact]
    public void AssignmentRejectsInvalidPeerUnknownJobAndFullPosition()
    {
        var roster = new CrewRoster([new CrewJob(7, "Test Engineer", "", 1)]);

        Assert.Equal(CrewAssignmentResult.InvalidPeer, roster.TryAssign(0, 7));
        Assert.Equal(CrewAssignmentResult.UnknownJob, roster.TryAssign(1, 8));
        Assert.Equal(CrewAssignmentResult.Accepted, roster.TryAssign(1, 7));
        Assert.Equal(CrewAssignmentResult.PositionFilled, roster.TryAssign(2, 7));
        Assert.Equal(1, roster.GetAssignedCount(7));
        Assert.False(roster.TryGetAssignment(2, out _));
    }

    [Fact]
    public void RetryDoesNotConsumeAnotherSlotAndDisconnectReleasesPosition()
    {
        var roster = new CrewRoster([new CrewJob(7, "Test Engineer", "", 2)]);

        Assert.Equal(CrewAssignmentResult.Accepted, roster.TryAssign(1, 7));
        Assert.Equal(CrewAssignmentResult.AlreadyAssigned, roster.TryAssign(1, 7));
        Assert.Equal(1, roster.GetAssignedCount(7));
        Assert.True(roster.TryGetAssignment(1, out CrewJob? assigned));
        Assert.Equal("Test Engineer", assigned!.Name);

        Assert.True(roster.Release(1));
        Assert.False(roster.Release(1));
        Assert.Equal(0, roster.GetAssignedCount(7));
        Assert.Equal(CrewAssignmentResult.Accepted, roster.TryAssign(2, 7));
    }

    [Fact]
    public void ConstructorRejectsInvalidOrDuplicateJobDefinitions()
    {
        Assert.Throws<ArgumentException>(() => new CrewRoster([]));
        Assert.Throws<ArgumentException>(() => new CrewRoster([new CrewJob(0, "Invalid", "", 1)]));
        Assert.Throws<ArgumentException>(() => new CrewRoster([
            new CrewJob(7, "First", "", 1),
            new CrewJob(7, "Duplicate", "", 1)]));
    }
}
