namespace Godotstation.Common.Interaction;

/// <summary>
/// Defines how far a player can reach for interactions.
/// </summary>
public enum ReachRange
{
    /// <summary>Adjacent tiles only (Manhattan distance <= 1).</summary>
    Standard = 1,

    /// <summary>Extended range for telekinesis (empty hand only).</summary>
    Telekinesis = 7
}
