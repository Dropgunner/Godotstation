using System;
using System.Collections.Generic;
using Godot;
using Godotstation.Common.Interactions;
using Godotstation.Game.UI;

namespace Godotstation.Game.Round;

/// <summary>Single-player local job source; a network lobby can replace this adapter.</summary>
public partial class LocalLobbyServiceNode : Node, ILobbyService
{
    private static readonly JobData[] Jobs =
    [
        new JobData(1, "Station Engineer", "Engineering", "#e5a84b",
            "Maintain the station relay array and restore service.", string.Empty,
            1, 1, true, string.Empty),
    ];

    public event Action<LobbyState>? OnLobbyStateChanged;
    public string SelectedJobName { get; private set; } = string.Empty;

    public override void _Ready() => UiServices.Lobby = this;

    public IReadOnlyList<JobData> GetAvailableJobs() => Jobs;
    public IReadOnlyList<ServerInfo> GetServerHistory() => Array.Empty<ServerInfo>();

    public void SelectJob(int jobId)
    {
        foreach (JobData job in Jobs)
        {
            if (job.JobId == jobId && job.IsAvailable && job.SlotsOpen > 0)
            {
                SelectedJobName = job.Name;
                OnLobbyStateChanged?.Invoke(LobbyState.WaitingToSpawn);
                return;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(jobId), "The selected job is not available.");
    }

    public void RequestSpectate() => OnLobbyStateChanged?.Invoke(LobbyState.WaitingToSpawn);
}
