using Godot;

namespace Godotstation.Game.Playtest;

/// <summary>Short-lived launch settings passed from the lobby into the multiplayer station.</summary>
public static class PlaytestLaunchOptions
{
    public static bool IsHost { get; set; }
    public static string Address { get; set; } = "127.0.0.1";
    public static int CrewJobId { get; set; } = Godotstation.Engine.Rounds.StationCrewJobCatalog.AssistantJobId;
    public static int Port { get; private set; } = 7777;
    public static bool SmokeMove { get; private set; }

    public static bool TryReadCommandLine()
    {
        bool configured = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--playtest-host")
            {
                IsHost = true;
                configured = true;
            }
            else if (arg.StartsWith("--playtest-join=", System.StringComparison.Ordinal))
            {
                IsHost = false;
                Address = arg["--playtest-join=".Length..];
                configured = true;
            }
            else if (arg.StartsWith("--playtest-port=", System.StringComparison.Ordinal) &&
                int.TryParse(arg["--playtest-port=".Length..], out int port) && port is > 0 and <= 65535)
            {
                Port = port;
            }
            else if (arg.StartsWith("--playtest-jobid=", System.StringComparison.Ordinal) &&
                int.TryParse(arg["--playtest-jobid=".Length..], out int jobId))
            {
                CrewJobId = jobId;
            }
            else if (arg == "--playtest-smoke-move")
            {
                SmokeMove = true;
            }
        }

        return configured;
    }
}
