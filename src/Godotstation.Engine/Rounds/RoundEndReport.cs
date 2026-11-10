using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;

namespace Godotstation.Engine.Rounds;

/// <summary>
/// A factual, immutable summary of the shared playtest state when the host ends a shift.
/// It does not infer a game-mode victory or evacuation outcome; crew condition is
/// a count of each body's tgstation stat when the shift ended.
/// </summary>
public sealed class RoundEndReport
{
    private RoundEndReport(int roundId, double durationSeconds, IReadOnlyList<string> crewRoles,
        bool generatorRunning, bool powerOutputEnabled, bool devicePowered, double storedEnergy,
        bool engineeringAirlockOpen, bool girderPlated, int metalSheetsRemaining,
        int crewStable, int crewCritical, int crewDead)
    {
        CrewStable = crewStable;
        CrewCritical = crewCritical;
        CrewDead = crewDead;
        RoundId = roundId;
        DurationSeconds = durationSeconds;
        CrewRoles = crewRoles;
        GeneratorRunning = generatorRunning;
        PowerOutputEnabled = powerOutputEnabled;
        DevicePowered = devicePowered;
        StoredEnergy = storedEnergy;
        EngineeringAirlockOpen = engineeringAirlockOpen;
        GirderPlated = girderPlated;
        MetalSheetsRemaining = metalSheetsRemaining;
    }

    public int RoundId { get; }
    public double DurationSeconds { get; }
    public IReadOnlyList<string> CrewRoles { get; }
    public bool GeneratorRunning { get; }
    public bool PowerOutputEnabled { get; }
    public bool DevicePowered { get; }
    public double StoredEnergy { get; }
    public bool EngineeringAirlockOpen { get; }
    public bool GirderPlated { get; }
    public int MetalSheetsRemaining { get; }
    public int CrewStable { get; }
    /// <summary>Crew in soft or hard crit.</summary>
    public int CrewCritical { get; }
    public int CrewDead { get; }

    /// <summary>Reads the tracked facts from the station's replicated entities by kind.</summary>
    public static RoundEndReport Create(int roundId, double durationSeconds, IEnumerable<string> crewRoles,
        IEnumerable<StationEntityState> station)
    {
        ArgumentNullException.ThrowIfNull(station);
        List<StationEntityState> states = station.ToList();
        bool Any(string kind, string field) => states.Any(state => state.Kind == kind && state.Flag(field));
        int Crew(Func<CrewStat, bool> match) => states.Count(state =>
            state.Kind == StationEntityKind.Crew && match((CrewStat)state.Count(StationEntityField.Stat)));
        return Create(roundId, durationSeconds, crewRoles,
            Any(StationEntityKind.FuelGenerator, StationEntityField.Running),
            Any(StationEntityKind.PowerStorage, StationEntityField.OutputEnabled),
            Any(StationEntityKind.ApcLamp, StationEntityField.Powered),
            states.Where(state => state.Kind == StationEntityKind.PowerStorage)
                .Sum(state => state.Get(StationEntityField.StoredEnergy)),
            Any(StationEntityKind.EngineeringAirlock, StationEntityField.Open),
            Any(StationEntityKind.Girder, StationEntityField.Plated),
            states.Where(state => state.Kind == StationEntityKind.MetalSheets)
                .Sum(state => state.Count(StationEntityField.Amount)),
            Crew(stat => stat == CrewStat.Stable),
            Crew(stat => stat is CrewStat.SoftCrit or CrewStat.HardCrit),
            Crew(stat => stat == CrewStat.Dead));
    }

    public static RoundEndReport Create(int roundId, double durationSeconds, IEnumerable<string> crewRoles,
        bool generatorRunning, bool powerOutputEnabled, bool devicePowered, double storedEnergy,
        bool engineeringAirlockOpen, bool girderPlated, int metalSheetsRemaining,
        int crewStable = 0, int crewCritical = 0, int crewDead = 0)
    {
        ArgumentNullException.ThrowIfNull(crewRoles);
        if (crewStable < 0 || crewCritical < 0 || crewDead < 0)
            throw new ArgumentOutOfRangeException(nameof(crewStable), "Crew condition counts cannot be negative.");
        if (roundId <= 0) throw new ArgumentOutOfRangeException(nameof(roundId));
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0 || durationSeconds > TimeSpan.MaxValue.TotalSeconds)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!double.IsFinite(storedEnergy) || storedEnergy < 0)
            throw new ArgumentOutOfRangeException(nameof(storedEnergy));
        if (metalSheetsRemaining < 0) throw new ArgumentOutOfRangeException(nameof(metalSheetsRemaining));

        var roleSnapshot = new List<string>();
        foreach (string role in crewRoles)
        {
            if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("Crew roles must be non-empty.", nameof(crewRoles));
            roleSnapshot.Add(role.Trim());
        }
        roleSnapshot.Sort(StringComparer.Ordinal);

        return new RoundEndReport(roundId, durationSeconds, roleSnapshot.AsReadOnly(), generatorRunning,
            powerOutputEnabled, devicePowered, storedEnergy, engineeringAirlockOpen, girderPlated,
            metalSheetsRemaining, crewStable, crewCritical, crewDead);
    }

    public string ToDisplayText()
    {
        string roles = CrewRoles.Count == 0 ? "None" : string.Join(", ", CrewRoles);
        string energy = StoredEnergy.ToString("0.0", CultureInfo.InvariantCulture);
        return string.Join("\n",
            $"Shift {RoundId} ended",
            $"Duration: {FormatDuration(DurationSeconds)}",
            $"Crew roles present: {roles}",
            $"Generator: {(GeneratorRunning ? "RUNNING" : "STOPPED")}",
            $"Power storage output: {(PowerOutputEnabled ? "ON" : "OFF")} · stored energy: {energy}",
            $"APC lamp: {(DevicePowered ? "POWERED" : "OFF")}",
            $"Engineering airlock: {(EngineeringAirlockOpen ? "OPEN" : "CLOSED")}",
            $"Girder: {(GirderPlated ? "PLATED" : "UNPLATED")} · metal sheets remaining: {MetalSheetsRemaining}",
            $"Crew condition: {CrewStable} stable · {CrewCritical} critical · {CrewDead} dead",
            "Not tracked in this prototype: game-mode victory, antagonist outcomes, or evacuation.");
    }

    private static string FormatDuration(double durationSeconds)
    {
        long totalSeconds = (long)Math.Floor(durationSeconds);
        long hours = totalSeconds / 3600;
        int minutes = (int)(totalSeconds / 60 % 60);
        int seconds = (int)(totalSeconds % 60);
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
    }
}
