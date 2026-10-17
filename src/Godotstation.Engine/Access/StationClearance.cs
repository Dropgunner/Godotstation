namespace Godotstation.Engine.Access;

/// <summary>
/// Unitystation clearance values used by the playable station slice. Numeric values
/// are the serialized enum ordinals from the pinned upstream Clearance.cs.
/// </summary>
public enum StationClearance
{
    Engine = 10,
    EngineEquip = 11,
    MaintTunnels = 12,
    ExternalAirlocks = 13,
    Atmospherics = 23,
    Construction = 31,
}
