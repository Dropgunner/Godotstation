using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Movement;

namespace Godotstation.Engine.Rounds;

/// <summary>Outcome of the host starting a shift.</summary>
public enum ShiftStartOutcome
{
    /// <summary>The host's role is not in this station's catalog.</summary>
    UnknownRole,
    /// <summary>The lifecycle refused the start (already started, not the host, or ended).</summary>
    NotStarted,
    /// <summary>The shift started, but the host's role could not be reserved.</summary>
    RoleUnavailable,
    Started,
}

/// <summary>The start decision. <see cref="Lifecycle"/> and <see cref="Assignment"/> are null when the step was never reached.</summary>
public sealed record ShiftStart(ShiftStartOutcome Outcome, RoundStartResult? Lifecycle, CrewAssignmentResult? Assignment, CrewJob? Job);

/// <summary>A joining crew member's admission: the reserved role and seat, or the reason it was refused.</summary>
public sealed record CrewAdmission(CrewAssignmentResult Result, CrewJob? Job, Vector2 Spawn);

/// <summary>Outcome of the host ending a shift.</summary>
public sealed record ShiftEnd(RoundEndResult Lifecycle, RoundEndReport? Report);

/// <summary>
/// The rules of one station round, with no transport or Godot types: the lifecycle, the role roster,
/// crew admission and the end-of-round report. Callers send requests and act on the decisions returned.
/// One instance lives for as long as the station process does; <see cref="Restart"/> begins the next round.
/// </summary>
public sealed class StationRound
{
    private readonly Func<CrewRoster> rosterFactory;

    public StationRound(int maximumPlayers, Func<CrewRoster>? rosterFactory = null)
    {
        if (maximumPlayers <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPlayers));
        MaximumPlayers = maximumPlayers;
        this.rosterFactory = rosterFactory ?? StationCrewJobCatalog.CreateRoster;
        Roster = this.rosterFactory();
    }

    public SharedRoundLifecycle Lifecycle { get; } = new();
    public int MaximumPlayers { get; }
    public CrewRoster Roster { get; private set; }

    public SharedRoundPhase Phase => Lifecycle.Phase;
    public int RoundId => Lifecycle.RoundId;
    public bool CanRequestJob => Lifecycle.CanRequestJob;

    /// <summary>The host starts the shift and takes the chosen role. Only the host may do this.</summary>
    public ShiftStart StartShift(int hostPeerId, int jobId)
    {
        if (!Roster.TryGetJob(jobId, out CrewJob? job) || job == null)
            return new ShiftStart(ShiftStartOutcome.UnknownRole, null, null, null);

        RoundStartResult started = Lifecycle.TryStart(hostPeerId);
        if (started != RoundStartResult.Started)
            return new ShiftStart(ShiftStartOutcome.NotStarted, started, null, null);

        CrewAssignmentResult assignment = Roster.TryAssign(hostPeerId, jobId);
        if (assignment != CrewAssignmentResult.Accepted || !Roster.TryGetAssignment(hostPeerId, out CrewJob? assigned) || assigned == null)
            return new ShiftStart(ShiftStartOutcome.RoleUnavailable, started, assignment, null);

        return new ShiftStart(ShiftStartOutcome.Started, started, assignment, assigned);
    }

    /// <summary>
    /// Decides whether a joining peer may take a role during the shift. The caller has already handled a
    /// peer who is seated, and passes how many crew are seated and where they stand.
    /// </summary>
    public CrewAdmission Admit(int peerId, int jobId, int requestedRoundId, int seatedCount, IEnumerable<Vector2> occupied)
    {
        if (!CanRequestJob || requestedRoundId != RoundId)
            return new CrewAdmission(CrewAssignmentResult.RoundNotStarted, null, default);
        if (seatedCount >= MaximumPlayers ||
            !StationPlaytestLayout.TryFindFreeSpawn(occupied, out Vector2 spawn))
            return new CrewAdmission(CrewAssignmentResult.PositionFilled, null, default);

        CrewAssignmentResult assignment = Roster.TryAssign(peerId, jobId);
        if (assignment != CrewAssignmentResult.Accepted || !Roster.TryGetAssignment(peerId, out CrewJob? job) || job == null)
            return new CrewAdmission(assignment, null, default);
        return new CrewAdmission(CrewAssignmentResult.Accepted, job, spawn);
    }

    /// <summary>A peer left the station, so their role is free again.</summary>
    public bool Release(int peerId) => Roster.Release(peerId);

    /// <summary>
    /// The host ends the shift. The report is built only when the end is accepted, from the crew and
    /// the station state at that moment.
    /// </summary>
    public ShiftEnd EndShift(int hostPeerId, int roundId, double durationSeconds,
        IEnumerable<string> crewRoles, IEnumerable<StationEntityState> station)
    {
        RoundEndResult ended = Lifecycle.TryEnd(hostPeerId, roundId);
        if (ended != RoundEndResult.Ended) return new ShiftEnd(ended, null);
        return new ShiftEnd(ended, RoundEndReport.Create(roundId, durationSeconds, crewRoles, station));
    }

    /// <summary>The host begins the next round. Every role is released for the new round.</summary>
    public RoundRestartResult Restart(int hostPeerId)
    {
        RoundRestartResult restarted = Lifecycle.TryRestart(hostPeerId);
        if (restarted == RoundRestartResult.Restarted) Roster = rosterFactory();
        return restarted;
    }
}
