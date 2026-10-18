using System;
using System.Collections.Generic;
using System.Numerics;

namespace Godotstation.Common.Interactions;

/// <summary>Small authoritative shared-world slice for the LAN playtest.</summary>
public sealed class SharedStationInteractionWorld
{
    public const int WrenchId = 9001;
    public const int FixtureId = 9002;

    private readonly InteractionDispatcher dispatcher;
    private readonly Dictionary<int, Actor> actors = new();
    private readonly WrenchPickupHandler pickupHandler;
    private readonly FixtureWrenchHandler fixtureHandler;
    private readonly WorldEntity wrench = new() { Id = WrenchId, Name = "wrench" };
    private readonly WorldEntity fixture = new() { Id = FixtureId, Name = "service cart" };
    private int? wrenchOwner;
    private Vector2 wrenchPosition = new(-1, 2);
    private Vector2 fixturePosition = new(0, 2);
    private bool fixtureAnchored = true;

    public SharedStationInteractionWorld(IReachabilityMap? reachabilityMap = null)
    {
        wrench.PlaceAt(wrenchPosition);
        fixture.PlaceAt(fixturePosition);
        dispatcher = new InteractionDispatcher(reachabilityMap);
        pickupHandler = new WrenchPickupHandler(this, wrench);
        fixtureHandler = new FixtureWrenchHandler(this, fixture, wrench);
        dispatcher.RegisterHandler(pickupHandler);
        dispatcher.RegisterHandler(fixtureHandler);
    }

    public event Action<SharedStationWorldSnapshot>? StateChanged;

    public SharedStationWorldSnapshot Snapshot => new(wrenchOwner, wrenchPosition, fixturePosition, fixtureAnchored);

    public bool AddActor(int peerId, Vector2 position)
    {
        if (peerId <= 0 || actors.ContainsKey(peerId) || !IsFinite(position)) return false;
        var player = new PlayerEntity { Id = peerId, Name = $"crew-{peerId}", IsLocalPlayer = true };
        player.PlaceAt(position);
        actors.Add(peerId, new Actor(player) { WorldPosition = position });
        return true;
    }

    public bool RemoveActor(int peerId)
    {
        if (!actors.Remove(peerId, out Actor? actor)) return false;
        if (wrenchOwner == peerId)
        {
            wrenchOwner = null;
            wrenchPosition = actor.WorldPosition;
            wrench.PlaceAt(wrenchPosition);
            wrench.IsInItemSlot = false;
            wrench.InventoryRootPlayer = null;
            Changed();
        }
        return true;
    }

    public bool SetActorPosition(int peerId, Vector2 position)
    {
        if (!IsFinite(position) || !actors.TryGetValue(peerId, out Actor? actor)) return false;
        actor.WorldPosition = position;
        actor.InteractionPlayer.PlaceAt(position);
        return true;
    }

    public bool SetFixturePosition(Vector2 position)
    {
        if (!IsFinite(position)) return false;
        if (fixturePosition == position) return true;
        fixturePosition = position;
        fixture.PlaceAt(position);
        Changed();
        return true;
    }

    public bool TryDropWrench(int senderPeerId)
    {
        if (wrenchOwner != senderPeerId || !actors.TryGetValue(senderPeerId, out Actor? actor)) return false;
        wrenchOwner = null;
        wrenchPosition = actor.WorldPosition;
        wrench.PlaceAt(wrenchPosition);
        wrench.IsInItemSlot = false;
        wrench.InventoryRootPlayer = null;
        Changed();
        return true;
    }

    public bool TryInteract(int senderPeerId, int targetId, Intent intent, bool isAltClick)
    {
        if (!actors.TryGetValue(senderPeerId, out Actor? actor) ||
            intent is not (Intent.Help or Intent.Disarm or Intent.Grab or Intent.Harm)) return false;
        IInteractable? target = targetId switch
        {
            WrenchId => wrenchOwner == null ? new Interactable("wrench", wrench, wrenchPosition) : null,
            FixtureId => new Interactable("service-cart", fixture, fixturePosition),
            _ => null
        };
        if (target == null) return false;

        actor.CurrentIntent = intent;
        actor.ActiveHandItem = wrenchOwner == senderPeerId
            ? new Interactable("wrench", wrench, actor.WorldPosition)
            : null;
        if (!dispatcher.TryResolveClick(actor, actor.ActiveHandItem, target, target.WorldPosition,
                intent, isAltClick, NetworkSide.Server)) return false;

        if (wrenchOwner == senderPeerId) wrenchPosition = actor.WorldPosition;
        Changed();
        return true;
    }

    private void Changed() => StateChanged?.Invoke(Snapshot);
    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private sealed class Actor : IPerformer
    {
        public Actor(PlayerEntity player) => InteractionPlayer = player;
        public Vector2 WorldPosition { get; set; }
        public bool IsGhosting => false;
        public bool HasTelekinesis => false;
        public bool IsAlive => true;
        public IInteractable? ActiveHandItem { get; set; }
        public Intent CurrentIntent { get; set; } = Intent.Help;
        public PlayerEntity InteractionPlayer { get; }
    }

    private sealed class Interactable(string id, WorldEntity entity, Vector2 position) : IInteractable
    {
        public string Id { get; } = id;
        public Vector2 WorldPosition { get; } = position;
        public WorldEntity InteractionEntity { get; } = entity;
    }

    private sealed class WrenchPickupHandler(SharedStationInteractionWorld world, WorldEntity owner)
        : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;
        public bool WillInteract(HandApply interaction, NetworkSide side)
            => interaction.Performer != null && interaction.HandObject == null && world.wrenchOwner == null &&
               interaction.TargetObject == Owner;
        public void ServerPerformInteraction(HandApply interaction)
        {
            var player = interaction.Performer!;
            world.wrenchOwner = player.Id;
            world.wrench.IsInItemSlot = true;
            world.wrench.InventoryRootPlayer = player;
        }
    }

    private sealed class FixtureWrenchHandler(SharedStationInteractionWorld world, WorldEntity owner, WorldEntity tool)
        : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;
        public bool WillInteract(HandApply interaction, NetworkSide side)
            => interaction.Performer != null && interaction.TargetObject == Owner && interaction.HandObject == tool &&
               world.wrenchOwner == interaction.Performer?.Id;
        public void ServerPerformInteraction(HandApply interaction) => world.fixtureAnchored = !world.fixtureAnchored;
    }
}

public sealed record SharedStationWorldSnapshot(int? WrenchOwner, Vector2 WrenchPosition,
    Vector2 FixturePosition, bool FixtureAnchored);
