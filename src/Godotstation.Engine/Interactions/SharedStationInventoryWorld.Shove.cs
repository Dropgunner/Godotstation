using System;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Health;

namespace Godotstation.Engine.Interactions;

/// <summary>What one shove did to its target.</summary>
public readonly record struct ShoveResult(bool Moved, bool KnockedDown, string DroppedItem);

/// <summary>
/// Shoving, after tgstation <c>living/disarm</c> at commit f37d36c. A standing crew member shoves
/// another one tile directly away. If the target cannot move there, it is knocked down for
/// <c>SHOVE_KNOCKDOWN_SOLID</c> (2 seconds). A target that is lying down drops what its active hand
/// holds. Every shove staggers the target for <c>STAGGERED_SLOWDOWN_LENGTH</c> (3 seconds), and a
/// staggered target cannot be missed by a punch.
/// </summary>
public sealed partial class SharedStationInventoryWorld
{
    public const double ShoveKnockdownSeconds = 2;
    public const double StaggerSeconds = 3;

    /// <summary>Raised for every shove (shover, target, result).</summary>
    public event Action<int, int, ShoveResult>? CrewShoved;

    /// <summary>Raised when a shove moved a crew member, so the host can move the body and its collision.</summary>
    public event Action<int, Vector2>? CrewDisplaced;

    /// <summary>
    /// Whether a crew member may be pushed onto a tile. The host supplies its collision check;
    /// without one, every tile is open.
    /// </summary>
    public Func<Vector2, bool> CanCrewEnterTile { get; set; } = _ => true;

    /// <summary>True while a crew member lies on the floor, from a critical stat or a knockdown.</summary>
    public bool IsLyingDown(int peerId) => crew.TryGetValue(peerId, out CrewMember? member) && IsLying(member);

    public bool IsStaggered(int peerId) =>
        crew.TryGetValue(peerId, out CrewMember? member) && clockSeconds < member.StaggeredUntil;

    private bool IsLying(CrewMember member) =>
        member.Health.Stat.IsFloored() || clockSeconds < member.KnockdownUntil;

    private bool TryShove(CrewMember shover, CrewMember target)
    {
        if (clockSeconds < shover.NextMeleeAt) return false;
        (int sx, int sy) = Tile(shover.Position);
        (int tx, int ty) = Tile(target.Position);
        shover.NextMeleeAt = clockSeconds + CrewMelee.ClickCooldownSeconds;

        var destination = new Vector2(tx + Math.Sign(tx - sx), ty + Math.Sign(ty - sy));
        bool moved = CanCrewEnterTile(destination);
        bool knockedDown = false;
        if (moved)
        {
            target.Position = destination;
            target.InteractionPlayer.PlaceAt(destination);
            CrewDisplaced?.Invoke(target.PeerId, destination);
        }
        else if (!IsLying(target))
        {
            target.KnockdownUntil = clockSeconds + ShoveKnockdownSeconds;
            knockedDown = true;
        }

        string dropped = string.Empty;
        if (IsLying(target) && target.ActiveHandSlot.Item is { } held)
        {
            dropped = held.Name;
            inventory.ServerDrop(target.ActiveHandSlot);
        }
        target.StaggeredUntil = clockSeconds + StaggerSeconds;
        CrewShoved?.Invoke(shover.PeerId, target.PeerId, new ShoveResult(moved, knockedDown, dropped));
        return true;
    }

    /// <summary>Disarm intent with an empty hand on another crew member.</summary>
    private sealed class ShoveHandler(SharedStationInventoryWorld world, CrewBody owner)
        : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;

        public bool WillInteract(HandApply interaction, NetworkSide side) =>
            side == NetworkSide.Server && interaction.Performer != null && interaction.TargetObject == Owner &&
            interaction.Intent == Intent.Disarm && interaction.HandObject == null &&
            world.crew.TryGetValue(interaction.Performer.Id, out CrewMember? shover) &&
            shover != owner.Member && !world.IsLying(shover) &&
            Tile(shover.Position) != Tile(owner.Member.Position) && world.CanAttackNow(shover.PeerId);

        public void ServerPerformInteraction(HandApply interaction)
        {
            if (interaction.Performer != null &&
                world.crew.TryGetValue(interaction.Performer.Id, out CrewMember? shover))
                world.TryShove(shover, owner.Member);
        }
    }
}
