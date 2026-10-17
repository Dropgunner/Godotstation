using System;
using System.Collections.Generic;
using System.Numerics;

namespace Godotstation.Engine.Chat;

public sealed record LocalChatListener(int PeerId, Vector2 Position, float HearingRange = 14f);

/// <summary>Server-side listener selection for Unitystation-style local speech.</summary>
public static class LocalSpeechRouter
{
    public const float DefaultHearingRange = 14f;

    public static IReadOnlyList<int> FindListeners(
        int speakerPeerId,
        Vector2 speechPosition,
        IEnumerable<LocalChatListener> listeners,
        Func<Vector2, Vector2, bool> hasLineOfSight)
    {
        if (speakerPeerId <= 0) throw new ArgumentOutOfRangeException(nameof(speakerPeerId));
        if (!IsFinite(speechPosition)) throw new ArgumentOutOfRangeException(nameof(speechPosition));
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(hasLineOfSight);

        var recipients = new List<int>();
        var seenPeers = new HashSet<int>();
        foreach (LocalChatListener listener in listeners)
        {
            if (listener.PeerId <= 0 || !IsFinite(listener.Position) ||
                !float.IsFinite(listener.HearingRange) || listener.HearingRange < 0 ||
                !seenPeers.Add(listener.PeerId))
                continue;

            if (listener.PeerId == speakerPeerId ||
                (Vector2.DistanceSquared(speechPosition, listener.Position) <= listener.HearingRange * listener.HearingRange &&
                 hasLineOfSight(speechPosition, listener.Position)))
                recipients.Add(listener.PeerId);
        }

        return recipients.AsReadOnly();
    }

    private static bool IsFinite(Vector2 position) => float.IsFinite(position.X) && float.IsFinite(position.Y);
}

/// <summary>Grid line cast that ignores the speaker and listener's occupied endpoint tiles.</summary>
public static class GridLineOfSight
{
    public static bool IsClear(Vector2 from, Vector2 to, Func<int, int, bool> isBlocked)
    {
        ArgumentNullException.ThrowIfNull(isBlocked);
        if (!float.IsFinite(from.X) || !float.IsFinite(from.Y) ||
            !float.IsFinite(to.X) || !float.IsFinite(to.Y))
            return false;

        int x = (int)MathF.Round(from.X);
        int y = (int)MathF.Round(from.Y);
        int endX = (int)MathF.Round(to.X);
        int endY = (int)MathF.Round(to.Y);
        int dx = Math.Abs(endX - x);
        int dy = Math.Abs(endY - y);
        int stepX = x < endX ? 1 : -1;
        int stepY = y < endY ? 1 : -1;
        int error = dx - dy;

        while (x != endX || y != endY)
        {
            int doubledError = error * 2;
            if (doubledError > -dy)
            {
                error -= dy;
                x += stepX;
            }
            if (doubledError < dx)
            {
                error += dx;
                y += stepY;
            }

            if ((x != endX || y != endY) && isBlocked(x, y))
                return false;
        }

        return true;
    }
}
