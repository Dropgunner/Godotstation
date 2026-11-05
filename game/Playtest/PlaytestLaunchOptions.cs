using Godot;
using Godotstation.Engine.Maps;

namespace Godotstation.Game.Playtest;

/// <summary>Short-lived launch settings passed from the lobby into the multiplayer station.</summary>
public static class PlaytestLaunchOptions
{
    public static bool IsHost { get; set; }
    public static string Address { get; set; } = "127.0.0.1";
    public static int CrewJobId { get; set; } = Godotstation.Engine.Rounds.StationCrewJobCatalog.EngineerJobId;
    public static int Port { get; private set; } = 7777;
    public static bool SmokeMove { get; private set; }
    public static bool SmokeChat { get; private set; }
    public static bool SmokeInteractions { get; private set; }
    public static bool SmokeConstruction { get; private set; }
    public static bool SmokeEngineeringAccess { get; private set; }
    public static bool SmokeRoundRestart { get; private set; }
    public static bool SmokePowerLoop { get; private set; }
    /// <summary>Map file the host loads (res://, user:// or absolute). Null means the built-in station.</summary>
    public static string? MapPath { get; set; }
    /// <summary>A map handed straight from the editor to the host, taking precedence over <see cref="MapPath"/>.</summary>
    public static StationMap? Map { get; set; }
    public static bool OpenMapEditor { get; private set; }
    public static bool MapEditorSmoke { get; private set; }

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
            else if (arg == "--playtest-smoke-chat")
            {
                SmokeChat = true;
            }
            else if (arg == "--playtest-smoke-interactions")
            {
                SmokeInteractions = true;
            }
            else if (arg == "--playtest-smoke-construction")
            {
                SmokeConstruction = true;
            }
            else if (arg == "--playtest-smoke-engineering-access")
            {
                SmokeEngineeringAccess = true;
            }
            else if (arg == "--playtest-smoke-round-restart")
            {
                SmokeRoundRestart = true;
            }
            else if (arg == "--playtest-smoke-power-loop")
            {
                SmokePowerLoop = true;
            }
            else if (arg.StartsWith("--playtest-map=", System.StringComparison.Ordinal))
            {
                MapPath = arg["--playtest-map=".Length..];
            }
            else if (arg == "--map-editor")
            {
                OpenMapEditor = true;
            }
            else if (arg == "--map-editor-smoke")
            {
                OpenMapEditor = true;
                MapEditorSmoke = true;
            }
        }

        return configured;
    }
}
