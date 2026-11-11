using System;
using System.Linq;
using System.Numerics;
using Godotstation.Engine.Health;

namespace Godotstation.Engine.Interactions;

public enum PullStopReason
{
    /// <summary>The puller ctrl-clicked again or pressed the release key.</summary>
    Released,
    /// <summary>Puller and pulled are more than one tile apart.</summary>
    OutOfReach,
    /// <summary>The puller can no longer use their hands.</summary>
    Incapacitated,
    /// <summary>The pulled object was anchored.</summary>
    Anchored,
    /// <summary>Someone else started pulling the same thing.</summary>
    Taken,
    /// <summary>The puller or the pulled crew member left the round.</summary>
    Left,
}

/// <summary>
/// Pulling, after tgstation at commit f37d36c: ctrl-click (<c>base_click_ctrl</c>) starts a pull on an
/// adjacent, unanchored movable (<c>can_be_pulled</c>); a ctrl-click while already pulling drops the
/// pull (<c>start_pulling</c> with state 0); one puller per object; the pull breaks beyond one tile
/// or when the object is anchored (<c>check_pulling</c>); and after the puller steps, a pulled thing
/// left diagonal or out of reach moves into the tile the puller vacated (<c>Moved</c>).
/// </summary>
public sealed partial class SharedStationInventoryWorld
{
    /// <summary>
    /// Speed factor while dragging someone who is lying down. tgstation adds PULL_PRONE_SLOWDOWN (1.5)
    /// to the move delay; this factor is a playtest-scaled stand-in, not a conversion of that value.
    /// </summary>
    public const float ProneDragSpeedFactor = 0.5f;

    /// <summary>Raised when a pull starts (puller, pulled entity id).</summary>
    public event Action<int, int>? PullStarted;

    /// <summary>Raised when a pull ends (puller, pulled entity id, reason).</summary>
    public event Action<int, int, PullStopReason>? PullStopped;

    public int? GetPulledEntity(int pullerPeerId) =>
        crew.TryGetValue(pullerPeerId, out CrewMember? member) ? member.Pulling?.Id : null;

    public int? GetPuller(int entityId) =>
        crew.Values.FirstOrDefault(member => member.Pulling?.Id == entityId)?.PeerId;

    /// <summary>True for a critical crew member who is being pulled: tgstation immobilizes them.</summary>
    public bool IsImmobilizedByPull(int peerId) =>
        crew.TryGetValue(peerId, out CrewMember? member) && member.Health.Stat != CrewStat.Stable &&
        GetPuller(peerId) != null;

    /// <summary>True while this crew member drags someone who is lying down.</summary>
    public bool IsDraggingProne(int pullerPeerId) =>
        crew.TryGetValue(pullerPeerId, out CrewMember? member) && member.Pulling is CrewBody body &&
        IsLying(body.Member);

    /// <summary>
    /// Ctrl-click. Drops the current pull if there is one; otherwise starts pulling the target.
    /// Returns false when nothing changed.
    /// </summary>
    public bool TryTogglePull(int pullerPeerId, int targetId)
    {
        if (!crew.TryGetValue(pullerPeerId, out CrewMember? puller) || !puller.Health.CanUseHands) return false;
        if (puller.Pulling != null)
        {
            StopPull(puller, PullStopReason.Released);
            PublishChanges();
            return true;
        }
        if (!entities.TryGetValue(targetId, out StationEntity? target) || !CanBePulled(puller, target) ||
            !IsAdjacent(puller.Position, target.WorldPosition) || !IsReachable(puller, target))
            return false;

        StopPullsOf(target, PullStopReason.Taken);
        puller.Pulling = target;
        PullStarted?.Invoke(pullerPeerId, target.Id);
        PublishChanges();
        return true;
    }

    /// <summary>The release key: drops the current pull, if any.</summary>
    public bool TryReleasePull(int pullerPeerId)
    {
        if (!crew.TryGetValue(pullerPeerId, out CrewMember? puller) || puller.Pulling == null) return false;
        StopPull(puller, PullStopReason.Released);
        PublishChanges();
        return true;
    }

    /// <summary>
    /// Call after the puller's position changed tile. Returns where the pulled thing must move to keep
    /// following: the tile the puller vacated, when it is now diagonal to or out of reach of the puller.
    /// </summary>
    public bool TryGetPullFollow(int pullerPeerId, Vector2 vacatedTile, out int pulledId, out Vector2 destination)
    {
        pulledId = 0;
        destination = default;
        if (!crew.TryGetValue(pullerPeerId, out CrewMember? puller) || puller.Pulling == null) return false;
        (int px, int py) = Tile(puller.Position);
        (int tx, int ty) = Tile(puller.Pulling.WorldPosition);
        (int vx, int vy) = Tile(vacatedTile);
        bool diagonal = px != tx && py != ty;
        bool apart = Math.Max(Math.Abs(px - tx), Math.Abs(py - ty)) > 1;
        if ((!diagonal && !apart) || (vx == tx && vy == ty) || (vx == px && vy == py)) return false;
        pulledId = puller.Pulling.Id;
        destination = new Vector2(vx, vy);
        return true;
    }

    /// <summary>Breaks pulls that are no longer valid. Call once per tick after movement.</summary>
    public void ValidatePulls()
    {
        bool changed = false;
        foreach (CrewMember puller in crew.Values.ToList())
        {
            if (puller.Pulling == null) continue;
            PullStopReason? reason =
                !puller.Health.CanUseHands ? PullStopReason.Incapacitated
                : puller.Pulling is ServiceCart { Anchored: true } ? PullStopReason.Anchored
                : !IsAdjacent(puller.Position, puller.Pulling.WorldPosition) ? PullStopReason.OutOfReach
                : null;
            if (reason == null) continue;
            StopPull(puller, reason.Value);
            changed = true;
        }
        if (changed) PublishChanges();
    }

    private static bool CanBePulled(CrewMember puller, StationEntity target) => target switch
    {
        CrewBody body => body.Member != puller,
        ServiceCart cart => !cart.Anchored,
        _ => false,
    };

    private void StopPull(CrewMember puller, PullStopReason reason)
    {
        if (puller.Pulling == null) return;
        int pulledId = puller.Pulling.Id;
        puller.Pulling = null;
        PullStopped?.Invoke(puller.PeerId, pulledId, reason);
    }

    private void StopPullsOf(StationEntity target, PullStopReason reason)
    {
        foreach (CrewMember other in crew.Values.ToList())
            if (ReferenceEquals(other.Pulling, target)) StopPull(other, reason);
    }

    private static (int X, int Y) Tile(Vector2 position) =>
        ((int)MathF.Round(position.X), (int)MathF.Round(position.Y));

    private static bool IsAdjacent(Vector2 a, Vector2 b)
    {
        (int ax, int ay) = Tile(a);
        (int bx, int by) = Tile(b);
        return Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by)) <= 1;
    }
}
