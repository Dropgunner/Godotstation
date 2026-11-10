using System;
using System.Numerics;

namespace Godotstation.Engine.Health;

/// <summary>
/// tgstation ghost rules at commit f37d36c. A dead player who tries to move leaves the body
/// (<c>client/Move</c> calls <c>ghostize</c> when <c>stat == DEAD</c>). The observer then moves
/// with <c>abstract_move</c>, which ignores every obstacle and stops only at the world edge.
/// Observers are invisible to the living and visible to each other.
/// </summary>
public static class GhostObserver
{
    /// <summary>Tiles per second. Playtest-scaled to the living run speed; not a source value.</summary>
    public const float Speed = 6f;

    /// <summary>Moves a ghost through anything, clamped to the station bounds.</summary>
    public static Vector2 Move(Vector2 position, int deltaX, int deltaY, double elapsedSeconds,
        Vector2 minimum, Vector2 maximum)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0 ||
            deltaX is < -1 or > 1 || deltaY is < -1 or > 1 || (deltaX == 0 && deltaY == 0))
            return Vector2.Clamp(position, minimum, maximum);
        Vector2 direction = Vector2.Normalize(new Vector2(deltaX, deltaY));
        return Vector2.Clamp(position + direction * (float)(Speed * elapsedSeconds), minimum, maximum);
    }

    /// <summary>Living crew never see ghosts; the dead see every ghost, including their own.</summary>
    public static bool IsVisibleTo(CrewStat viewerStat) => viewerStat == CrewStat.Dead;
}
