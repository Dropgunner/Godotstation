using System;
using System.Collections.Generic;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Construction;
using Godotstation.Engine.Inventory;
using InventoryBodyPartType = Godotstation.Engine.Inventory.BodyPartType;
using InventoryNamedSlot = Godotstation.Engine.Inventory.NamedSlot;

namespace Godotstation.Engine.Interactions;

/// <summary>
/// Server-side playtest world that routes hand interactions through the shared
/// interaction dispatcher and the real inventory move system.
/// </summary>
public sealed class SharedStationInventoryWorld : IInventoryWorld
{
    public const int WrenchId = 9001;
    public const int FixtureId = 9002;
    public const int MetalSheetsId = 9003;
    public const int GirderId = 9004;

    private readonly Dictionary<int, CrewMember> crew = new();
    private readonly Dictionary<ItemStorage, CrewMember> crewByStorage = new();
    private readonly InventorySystem inventory;
    private readonly InteractionDispatcher dispatcher;
    private readonly IReachabilityMap reachabilityMap;
    private readonly SharedWorldItem wrench;
    private readonly SharedWorldItem metalSheets;
    private readonly SharedWorldFixture fixture;
    private readonly SharedWorldFixture girder;
    private readonly GirderPlatingTask platingTask = new();
    private Vector2 girderPosition = new(2, 2);
    private bool girderPlated;
    private Vector2 fixturePosition = new(0, 2);
    private bool fixtureAnchored = true;

    public SharedStationInventoryWorld(IReachabilityMap? reachabilityMap = null)
    {
        this.reachabilityMap = reachabilityMap ?? UnobstructedMap.Instance;
        inventory = new InventorySystem(this);
        inventory.OnMovePerformed += _ => Changed();
        dispatcher = new InteractionDispatcher(this.reachabilityMap);
        wrench = new SharedWorldItem(this, WrenchId, "wrench", new Vector2(-1, 2));
        metalSheets = new SharedWorldItem(this, MetalSheetsId, "metal sheets", new Vector2(1, 2), new SharedMetalStack(2));
        fixture = new SharedWorldFixture(FixtureId, "service cart", fixturePosition);
        girder = new SharedWorldFixture(GirderId, "girder", girderPosition);
        dispatcher.RegisterHandler(new WrenchPickupHandler(this, wrench));
        dispatcher.RegisterHandler(new WrenchPickupHandler(this, metalSheets));
        dispatcher.RegisterHandler(new FixtureWrenchHandler(this, fixture, wrench));
        dispatcher.RegisterHandler(new GirderPlatingHandler(this, girder, metalSheets));
    }

    public event Action<SharedStationWorldSnapshot>? StateChanged;

    public SharedStationWorldSnapshot Snapshot
    {
        get
        {
            CrewMember? owner = wrench.ItemSlot == null ? null : GetCrewForStorage(wrench.ItemSlot.ItemStorage);
            Vector2 wrenchPosition = owner?.Position ?? wrench.PositionInWorld;
            return new SharedStationWorldSnapshot(owner?.PeerId, wrenchPosition, fixturePosition, fixtureAnchored);
        }
    }

    public bool AddActor(int peerId, Vector2 position)
    {
        if (peerId <= 0 || crew.ContainsKey(peerId) || !IsFinite(position)) return false;
        var slots = new[]
        {
            SlotIdentifier.Named(InventoryNamedSlot.leftHand),
            SlotIdentifier.Named(InventoryNamedSlot.rightHand),
        };
        var storage = new ItemStorage(slots, AcceptAllCapacity.Instance, name: $"crew-{peerId}");
        var member = new CrewMember(peerId, position, storage);
        crew.Add(peerId, member);
        crewByStorage.Add(storage, member);
        return true;
    }

    public bool RemoveActor(int peerId)
    {
        if (!crew.TryGetValue(peerId, out CrewMember? member)) return false;
        if (wrench.ItemSlot?.ItemStorage == member.Inventory)
            inventory.ServerDrop(wrench.ItemSlot);
        if (metalSheets.ItemSlot?.ItemStorage == member.Inventory)
            inventory.ServerDrop(metalSheets.ItemSlot);
        platingTask.Cancel(peerId);
        crew.Remove(peerId);
        crewByStorage.Remove(member.Inventory);
        PublishConstructionState();
        return true;
    }

    public bool SetActorPosition(int peerId, Vector2 position)
    {
        if (!IsFinite(position) || !crew.TryGetValue(peerId, out CrewMember? member)) return false;
        member.Position = position;
        member.InteractionPlayer.PlaceAt(position);
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

    /// <summary>Changes the active hand without moving its contents.</summary>
    public bool TryToggleActiveHand(int peerId)
    {
        if (!crew.TryGetValue(peerId, out CrewMember? member)) return false;
        member.ActiveHand = member.ActiveHand == InventoryNamedSlot.rightHand
            ? InventoryNamedSlot.leftHand
            : InventoryNamedSlot.rightHand;
        return true;
    }

    public InventoryNamedSlot? GetActiveHand(int peerId) =>
        crew.TryGetValue(peerId, out CrewMember? member) ? member.ActiveHand : null;

    public string GetHandItemName(int peerId, InventoryNamedSlot hand)
    {
        if (!crew.TryGetValue(peerId, out CrewMember? member)) return string.Empty;
        return member.Inventory.GetNamedItemSlot(hand)?.Item?.Name ?? string.Empty;
    }

    public StationConstructionSnapshot ConstructionSnapshot
    {
        get
        {
            CrewMember? owner = metalSheets.ItemSlot == null ? null : GetCrewForStorage(metalSheets.ItemSlot.ItemStorage);
            return new StationConstructionSnapshot(owner?.PeerId, owner?.Position ?? metalSheets.PositionInWorld,
                girderPlated ? 0 : metalSheets.Stackable?.Amount ?? 0, girderPosition, girderPlated, platingTask.WorkerPeerId,
                platingTask.ProgressSeconds);
        }
    }

    public event Action<StationConstructionSnapshot>? ConstructionStateChanged;

    /// <summary>Advance the server-owned construction action and recheck its live prerequisites.</summary>
    public void AdvanceConstruction(int peerId, double elapsedSeconds, bool publishProgress = true)
    {
        if (platingTask.WorkerPeerId != peerId || !crew.TryGetValue(peerId, out CrewMember? member)) return;
        bool stillValid = ReferenceEquals(member.ActiveHandSlot.Item, metalSheets) &&
            Validations.IsReachableByPositions(member.Position, girderPosition, true, reachabilityMap,
                context: girder);
        bool completed = platingTask.Advance(peerId, elapsedSeconds, stillValid,
            metalSheets.Stackable?.Amount ?? 0);
        if (completed)
        {
            if (!inventory.ServerConsume(member.ActiveHandSlot, GirderPlatingTask.RequiredSheets))
                throw new InvalidOperationException("Validated girder material could not be consumed.");
            girderPlated = true;
        }
        if (completed || !platingTask.WorkerPeerId.HasValue || publishProgress)
            PublishConstructionState();
    }

    public bool TryInteract(int senderPeerId, int targetId, Intent intent, bool isAltClick)
    {
        if (!crew.TryGetValue(senderPeerId, out CrewMember? member) ||
            intent is not (Intent.Help or Intent.Disarm or Intent.Grab or Intent.Harm)) return false;

        IInteractable? target = targetId switch
        {
            WrenchId when wrench.ItemSlot == null && wrench.IsInWorld => wrench,
            MetalSheetsId when metalSheets.ItemSlot == null && metalSheets.IsInWorld => metalSheets,
            FixtureId => fixture,
            GirderId when !girderPlated => girder,
            _ => null,
        };
        if (target == null) return false;

        if (!dispatcher.TryResolveClick(member, member.ActiveHandItem, target,
                target.WorldPosition, intent, isAltClick, NetworkSide.Server)) return false;
        Changed();
        PublishConstructionState();
        return true;
    }

    /// <summary>Drops only the current active-hand item, as the source inventory does.</summary>
    public bool TryDropActiveHand(int senderPeerId)
    {
        if (!crew.TryGetValue(senderPeerId, out CrewMember? member)) return false;
        ItemSlot? activeHand = member.ActiveHandSlot;
        return activeHand.Item != null && inventory.ServerDrop(activeHand);
    }

    /// <summary>Compatibility helper for the current playtest's single shared wrench.</summary>
    public bool TryDropWrench(int senderPeerId)
    {
        if (!crew.TryGetValue(senderPeerId, out CrewMember? member) ||
            !ReferenceEquals(member.ActiveHandSlot.Item, wrench)) return false;
        return inventory.ServerDrop(member.ActiveHandSlot);
    }

    private CrewMember? GetCrewForStorage(ItemStorage storage) =>
        crewByStorage.TryGetValue(storage.GetRootStorage(), out CrewMember? member) ? member : null;

    private bool IsWrenchInActiveHand(int peerId) =>
        crew.TryGetValue(peerId, out CrewMember? member) && ReferenceEquals(member.ActiveHandSlot.Item, wrench);

    private void ToggleFixture() => fixtureAnchored = !fixtureAnchored;

    private bool TryStartGirderPlating(int peerId)
    {
        if (!crew.TryGetValue(peerId, out CrewMember? member) ||
            !ReferenceEquals(member.ActiveHandSlot.Item, metalSheets))
            return false;
        return platingTask.TryStart(peerId, metalSheets.Stackable?.Amount ?? 0,
            isReachable: Validations.IsReachableByPositions(member.Position, girderPosition, true,
                reachabilityMap, context: girder));
    }

    private void Changed() => StateChanged?.Invoke(Snapshot);
    private void PublishConstructionState() => ConstructionStateChanged?.Invoke(ConstructionSnapshot);

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    public bool IsHolderInContainer(ItemSlot fromSlot) => false;

    public void DisappearFromWorld(IPickupable item)
    {
        if (item is SharedWorldItem worldItem) worldItem.SetInWorld(false);
    }

    public void Despawn(IPickupable item)
    {
        if (item is SharedWorldItem worldItem) worldItem.SetInWorld(false);
    }

    public void Drop(IPickupable item, ItemSlot fromSlot, Vector2? worldTargetVector)
    {
        if (item is not SharedWorldItem worldItem) return;
        CrewMember? member = GetCrewForStorage(fromSlot.ItemStorage);
        Vector2 position = member?.Position ?? Vector2.Zero;
        if (worldTargetVector.HasValue) position += worldTargetVector.Value;
        worldItem.PlaceInWorld(position);
    }

    public void StoreInHolderContainer(IPickupable item, ItemSlot fromSlot) =>
        DisappearFromWorld(item);

    public void Throw(IPickupable item, ItemSlot fromSlot, Vector2 trajectory, float throwSpeed,
        float airTime, InventoryBodyPartType aim) => Drop(item, fromSlot, trajectory);

    public void Vanish(IPickupable item) => DisappearFromWorld(item);

    private sealed class CrewMember : IPerformer
    {
        public CrewMember(int peerId, Vector2 position, ItemStorage inventory)
        {
            PeerId = peerId;
            Position = position;
            Inventory = inventory;
            InteractionPlayer = new PlayerEntity { Id = peerId, Name = $"crew-{peerId}" };
            InteractionPlayer.PlaceAt(position);
        }

        public int PeerId { get; }
        public Vector2 Position { get; set; }
        public ItemStorage Inventory { get; }
        public InventoryNamedSlot ActiveHand { get; set; } = InventoryNamedSlot.rightHand;
        public ItemSlot ActiveHandSlot => Inventory.GetNamedItemSlot(ActiveHand)!;
        public Vector2 WorldPosition => Position;
        public bool IsGhosting => false;
        public bool HasTelekinesis => false;
        public bool IsAlive => true;
        public IInteractable? ActiveHandItem => ActiveHandSlot.Item as IInteractable;
        public Intent CurrentIntent => Intent.Help;
        public PlayerEntity InteractionPlayer { get; }
    }

    private sealed class SharedWorldItem : WorldEntity, IPickupable, IInteractable
    {
        private readonly SharedStationInventoryWorld world;
        private ItemSlot? itemSlot;

        public SharedWorldItem(SharedStationInventoryWorld world, int id, string name, Vector2 position,
            IStackable? stackable = null)
        {
            this.world = world;
            Id = id;
            Name = name;
            PositionInWorld = position;
            Stackable = stackable;
            PlaceAt(position);
        }

        public ItemSlot? ItemSlot => itemSlot;
        public bool IsBuckled => false;
        public IStackable? Stackable { get; }
        public IEnumerable<ItemStorage> ItemStorages => Array.Empty<ItemStorage>();
        public IEnumerable<IServerInventoryMove> MoveHooks => Array.Empty<IServerInventoryMove>();
        public float ThrowSpeed => 6f;
        public float ThrowRange => 7f;
        public bool StickyMovement => false;
        public string InteractionId => Name;
        string IInteractable.Id => InteractionId;
        public Vector2 WorldPosition => itemSlot == null
            ? PositionInWorld
            : world.GetCrewForStorage(itemSlot.ItemStorage)?.Position ?? PositionInWorld;
        public WorldEntity InteractionEntity => this;
        public Vector2 PositionInWorld { get; private set; }
        public bool IsInWorld { get; private set; } = true;

        public void SetItemSlot(ItemSlot? slot)
        {
            itemSlot = slot;
            IsInItemSlot = slot != null;
            InventoryRootPlayer = slot == null ? null : world.GetCrewForStorage(slot.ItemStorage)?.InteractionPlayer;
        }

        public void SetInWorld(bool value) => IsInWorld = value;

        public void PlaceInWorld(Vector2 position)
        {
            PositionInWorld = position;
            PlaceAt(position);
            IsInWorld = true;
        }
    }

    private sealed class SharedWorldFixture : WorldEntity, IInteractable
    {
        public SharedWorldFixture(int id, string name, Vector2 position)
        {
            Id = id;
            Name = name;
            PlaceAt(position);
        }

        string IInteractable.Id => Name;
        public Vector2 WorldPosition => AssumedWorldPosServer;
        public WorldEntity InteractionEntity => this;
    }

    private sealed class SharedMetalStack(int initialAmount) : IStackable
    {
        public int Amount { get; private set; } = initialAmount;
        public bool CanAccommodate(IPickupable other) => other.Stackable is SharedMetalStack;
        public void ServerCombine(IStackable other)
        {
            if (other is not SharedMetalStack stack) return;
            Amount += stack.Amount;
            stack.Amount = 0;
        }
        public bool ServerConsume(int amountToConsume)
        {
            if (amountToConsume <= 0 || amountToConsume > Amount) return false;
            Amount -= amountToConsume;
            return true;
        }
        public IPickupable ServerRemoveOne()
        {
            if (Amount <= 0) throw new InvalidOperationException("Cannot split an empty metal stack.");
            Amount--;
            return new LooseMetalSheet();
        }
    }

    private sealed class LooseMetalSheet : IPickupable
    {
        public string Name => "metal sheet";
        public ItemSlot? ItemSlot { get; private set; }
        public bool IsBuckled => false;
        public IStackable? Stackable => null;
        public IEnumerable<ItemStorage> ItemStorages => Array.Empty<ItemStorage>();
        public IEnumerable<IServerInventoryMove> MoveHooks => Array.Empty<IServerInventoryMove>();
        public float ThrowSpeed => 6f;
        public float ThrowRange => 7f;
        public bool StickyMovement => false;
        public void SetItemSlot(ItemSlot? slot) => ItemSlot = slot;
    }

    private sealed class GirderPlatingHandler(SharedStationInventoryWorld world,
        SharedWorldFixture owner, SharedWorldItem metal)
        : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;

        public bool WillInteract(HandApply interaction, NetworkSide side) =>
            interaction.Performer != null && interaction.TargetObject == Owner &&
            ReferenceEquals(interaction.HandObject, metal) && (metal.Stackable?.Amount ?? 0) >= GirderPlatingTask.RequiredSheets &&
            !world.platingTask.IsComplete && !world.platingTask.WorkerPeerId.HasValue;

        public void ServerPerformInteraction(HandApply interaction)
        {
            if (interaction.Performer != null && world.TryStartGirderPlating(interaction.Performer.Id))
                world.PublishConstructionState();
        }
    }

    public sealed record StationConstructionSnapshot(int? MetalOwner, Vector2 MetalPosition, int MetalSheetCount,
        Vector2 GirderPosition, bool GirderPlated, int? WorkerPeerId, double ProgressSeconds);

    private sealed class AcceptAllCapacity : IItemStorageCapacity
    {
        public static AcceptAllCapacity Instance { get; } = new();
        public bool CanFit(IPickupable toCheck, SlotIdentifier inSlot) => true;
    }

    private sealed class WrenchPickupHandler(SharedStationInventoryWorld world, SharedWorldItem owner)
        : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;

        public bool WillInteract(HandApply interaction, NetworkSide side) =>
            interaction.Performer != null && interaction.TargetObject == Owner &&
            interaction.HandObject == null && owner.IsInWorld && owner.ItemSlot == null;

        public void ServerPerformInteraction(HandApply interaction)
        {
            if (interaction.Performer != null &&
                world.crew.TryGetValue(interaction.Performer.Id, out CrewMember? member))
                world.inventory.ServerAdd(owner, member.ActiveHandSlot);
        }
    }

    private sealed class FixtureWrenchHandler(SharedStationInventoryWorld world,
        SharedWorldFixture owner, SharedWorldItem wrench) : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;

        public bool WillInteract(HandApply interaction, NetworkSide side) =>
            interaction.Performer != null && interaction.TargetObject == Owner &&
            ReferenceEquals(interaction.HandObject, wrench) &&
            world.IsWrenchInActiveHand(interaction.Performer.Id);

        public void ServerPerformInteraction(HandApply interaction) => world.ToggleFixture();
    }
}
