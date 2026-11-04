using Godotstation.Engine.Interactions;
using Godotstation.Engine.Entities;
using Godotstation.Common.Interactions;
using System.Numerics;
using System.Collections.Generic;
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
                     "Crew casualties: 0\n" +
                     "Not tracked in this prototype: game-mode victory, antagonist outcomes, evacuation, or revival.",
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
    [Fact]
    public void ReportCountsCasualties()
    {
        // FC-SC-8: dead crew bodies in the station snapshot are counted; living ones are not.
        var dead = new Dictionary<string, double> { ["brute"] = 225, ["conscious"] = (int)ConsciousState.Dead };
        var hurt = new Dictionary<string, double> { ["brute"] = 50, ["conscious"] = (int)ConsciousState.Conscious };
        var station = new List<StationEntityState>(SharedStationInventoryWorld.InitialStates())
        {
            new(StationEntityCatalog.CrewEntityId(1), StationEntityKind.Crew, Vector2.Zero, null, StationEntityFlags.Targetable, hurt),
            new(StationEntityCatalog.CrewEntityId(2), StationEntityKind.Crew, Vector2.One, null, StationEntityFlags.Targetable, dead),
        };

        RoundEndReport report = RoundEndReport.Create(2, 30, new[] { "Engineer", "Assistant" }, station);

        Assert.Equal(1, report.CrewCasualties);
        Assert.Contains("Crew casualties: 1", report.ToDisplayText());
        Assert.Throws<ArgumentOutOfRangeException>(() => RoundEndReport.Create(1, 1, Array.Empty<string>(),
            false, false, false, 0, false, false, 0, crewCasualties: -1));
    }
}
