using System;
using Godotstation.Engine.Rounds;
using Xunit;

namespace Godotstation.Tests.Rounds;

public sealed class RoundEndReportTests
{
    [Fact]
    public void ReportCapturesTrackedStationAndCrewFactsDeterministically()
    {
        RoundEndReport report = RoundEndReport.Create(3, 3661.9, new[] { "Engineer", "Assistant" },
            generatorRunning: true, powerOutputEnabled: true, devicePowered: true, storedEnergy: 7.5,
            engineeringAirlockOpen: false, girderPlated: true, metalSheetsRemaining: 0);

        Assert.Equal(new[] { "Assistant", "Engineer" }, report.CrewRoles);
        Assert.Equal("Shift 3 ended\nDuration: 1:01:01\nCrew roles present: Assistant, Engineer\n" +
                     "Generator: RUNNING\nPower storage output: ON · stored energy: 7.5\nAPC lamp: POWERED\n" +
                     "Engineering airlock: CLOSED\nGirder: PLATED · metal sheets remaining: 0\n" +
                     "Not tracked in this prototype: game-mode victory, antagonist outcomes, evacuation, survival, or deaths.",
            report.ToDisplayText());
    }

    [Fact]
    public void ReportDoesNotInventTrackedStateWhenCrewOrPowerIsAbsent()
    {
        RoundEndReport report = RoundEndReport.Create(1, 12, Array.Empty<string>(),
            generatorRunning: false, powerOutputEnabled: false, devicePowered: false, storedEnergy: 0,
            engineeringAirlockOpen: false, girderPlated: false, metalSheetsRemaining: 2);

        Assert.Contains("Crew roles present: None", report.ToDisplayText());
        Assert.Contains("Generator: STOPPED", report.ToDisplayText());
        Assert.Contains("APC lamp: OFF", report.ToDisplayText());
        Assert.Contains("Not tracked in this prototype", report.ToDisplayText());
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 1, -1)]
    public void RejectsInvalidRoundDurationOrMaterialCount(int roundId, double duration, int remainingSheets)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoundEndReport.Create(roundId, duration,
            Array.Empty<string>(), false, false, false, 0, false, false, remainingSheets));
    }
}
