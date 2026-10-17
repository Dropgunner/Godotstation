using System;
using System.Collections.Generic;

namespace Godotstation.Engine.Rounds;

public sealed record CrewJob(int Id, string Name, string Description, int PositionLimit);

public enum CrewAssignmentResult
{
    Accepted,
    InvalidPeer,
    UnknownJob,
    PositionFilled,
    AlreadyAssigned,
}

/// <summary>Server-owned job positions for a crew roster. This type contains no engine or UI dependencies.</summary>
public sealed class CrewRoster
{
    private readonly Dictionary<int, CrewJob> jobsById = new();
    private readonly Dictionary<int, int> assignmentsByPeer = new();

    public CrewRoster(IEnumerable<CrewJob> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        foreach (CrewJob job in jobs)
        {
            if (job.Id <= 0 || string.IsNullOrWhiteSpace(job.Name) || job.PositionLimit <= 0)
                throw new ArgumentException("Crew jobs need a positive id, a name, and at least one position.", nameof(jobs));
            if (!jobsById.TryAdd(job.Id, job))
                throw new ArgumentException($"Duplicate crew job id {job.Id}.", nameof(jobs));
        }

        if (jobsById.Count == 0)
            throw new ArgumentException("A crew roster needs at least one job.", nameof(jobs));
    }

    public CrewAssignmentResult TryAssign(int peerId, int jobId)
    {
        if (peerId <= 0) return CrewAssignmentResult.InvalidPeer;
        if (assignmentsByPeer.ContainsKey(peerId)) return CrewAssignmentResult.AlreadyAssigned;
        if (!jobsById.TryGetValue(jobId, out CrewJob? job)) return CrewAssignmentResult.UnknownJob;

        int assignedCount = 0;
        foreach (int assignedJobId in assignmentsByPeer.Values)
            if (assignedJobId == jobId) assignedCount++;
        if (assignedCount >= job.PositionLimit) return CrewAssignmentResult.PositionFilled;

        assignmentsByPeer.Add(peerId, jobId);
        return CrewAssignmentResult.Accepted;
    }

    public bool Release(int peerId) => assignmentsByPeer.Remove(peerId);

    public bool TryGetAssignment(int peerId, out CrewJob? job)
    {
        if (assignmentsByPeer.TryGetValue(peerId, out int jobId) && jobsById.TryGetValue(jobId, out job))
            return true;
        job = null;
        return false;
    }

    public bool TryGetJob(int jobId, out CrewJob? job) => jobsById.TryGetValue(jobId, out job);

    public int GetAssignedCount(int jobId)
    {
        int count = 0;
        foreach (int assignedJobId in assignmentsByPeer.Values)
            if (assignedJobId == jobId) count++;
        return count;
    }
}

/// <summary>Small sourced role catalog for the LAN playtest, using Unitystation JobType IDs and limits.</summary>
public static class StationCrewJobCatalog
{
    public const int AssistantJobId = 2;
    public const int EngineerJobId = 20;

    private static readonly CrewJob[] jobs =
    [
        new CrewJob(AssistantJobId, "Assistant", "Assist the crew and learn station operations.", 99),
        new CrewJob(EngineerJobId, "Engineer", "Maintain station power and repair the station.", 5),
    ];

    private static readonly IReadOnlyList<CrewJob> readOnlyJobs = Array.AsReadOnly(jobs);

    public static IReadOnlyList<CrewJob> Jobs => readOnlyJobs;

    public static CrewRoster CreateRoster() => new(jobs);
}
