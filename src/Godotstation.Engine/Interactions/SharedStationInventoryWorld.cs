using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Access;
using Godotstation.Engine.Construction;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Inventory;
using Godotstation.Engine.Movement;
using Godotstation.Engine.Power;
using Godotstation.Engine.Rounds;
using InventoryBodyPartType = Godotstation.Engine.Inventory.BodyPartType;
using InventoryNamedSlot = Godotstation.Engine.Inventory.NamedSlot;

namespace Godotstation.Engine.Interactions;

/// <summary>
/// Server-side playtest world that routes hand interactions through the shared
/// interaction dispatcher and the real inventory move system. Station objects
/// are spawned from <see cref="StationEntityCatalog"/> by kind and replicated
/// as <see cref="StationEntityState"/> through one change feed.
/// </summary>
public sealed partial class SharedStationInventoryWorld : IInventoryWorld
{
    private readonly Dictionary<int, CrewMember> crew = new();
    private readonly Dictionary<ItemStorage, CrewMember> crewByStorage = new();
    private readonly InventorySystem inventory;
    private readonly InteractionDispatcher dispatcher;
    private readonly IReachabilityMap reachabilityMap;
    private readonly List<StationEntity> entityOrder = new();
    private readonly Dictionary<int, StationEntity> entities = new();
    private readonly Dictionary<int, StationEntityState> lastPublished = new();
    private readonly StationPowerLoop powerLoop = new();
    private double clockSeconds;

    public SharedStationInventoryWorld(IReachabilityMap? reachabilityMap = null,
        IReadOnlyList<StationEntitySpawn>? spawns = null)
    {
        this.reachabilityMap = reachabilityMap ?? UnobstructedMap.Instance;
        inventory = new InventorySystem(this);
        inventory.OnMovePerformed += _ => PublishChanges();
        dispatcher = new InteractionDispatcher(this.reachabilityMap);
        foreach (StationEntitySpawn spawn in spawns ?? StationEntityCatalog.Playtest)
            Spawn(spawn);
        foreach (StationEntityState state in CaptureAll())
            lastPublished[state.Id] = state;
    }

    /// <summary>Raised with only the entities whose replicated state differs from the last publish.</summary>
    public event Action<IReadOnlyList<StationEntityState>>? EntitiesChanged;

    /// <summary>Raised when one crew member hits another (attacker, victim, brute dealt).</summary>
    public event Action<int, int, double>? CrewAttacked;

    /// <summary>Raised for every unarmed swing, hit or miss (attacker, victim, result).</summary>
    public event Action<int, int, UnarmedAttackResult>? CrewStruckUnarmed;

    /// <summary>Source of unarmed damage and miss rolls. Replace it with a seeded one for repeatable results.</summary>
    public Random Random { get; set; } = new();

    /// <summary>Raised when a bruise pack treatment finishes (healer, patient, brute healed).</summary>
    public event Action<int, int, double>? CrewTreated;

    /// <summary>Raised when a crew member's stat changes (peer, previous, current).</summary>
    public event Action<int, CrewStat, CrewStat>? CrewStatChanged;

    /// <summary>Raised when a dead crew member's player leaves the body as a ghost.</summary>
    public event Action<int>? CrewGhosted;

    /// <summary>Door tiles whose passability follows their entity's state.</summary>
    public IEnumerable<(int X, int Y, IPassable Passable)> PassableTiles =>
        entityOrder.OfType<AccessDoor>().Select(door =>
            ((int)MathF.Round(door.WorldPosition.X), (int)MathF.Round(door.WorldPosition.Y), (IPassable)door));

    public IReadOnlyList<StationEntityState> CaptureAll() => entityOrder.ConvertAll(entity => entity.Capture());

    public StationEntityState? GetState(int entityId) =>
        entities.TryGetValue(entityId, out StationEntity? entity) ? entity.Capture() : null;

    /// <summary>The replicated starting state of a station, for clients that have not received a snapshot yet.</summary>
    public static IReadOnlyList<StationEntityState> InitialStates(IReadOnlyList<StationEntitySpawn>? spawns = null) =>
        new SharedStationInventoryWorld(spawns: spawns).CaptureAll();

    /// <summary>Publishes every entity whose state changed since the previous publish.</summary>
    public void PublishChanges()
    {
        List<StationEntityState>? changed = null;
        foreach (StationEntity entity in entityOrder)
        {
            StationEntityState state = entity.Capture();
            if (lastPublished.TryGetValue(state.Id, out StationEntityState? previous) && previous.Equals(state))
                continue;
            lastPublished[state.Id] = state;
            (changed ??= new List<StationEntityState>()).Add(state);
        }
        if (changed != null) EntitiesChanged?.Invoke(changed);
    }

    private void Spawn(StationEntitySpawn spawn)
    {
        if (entities.ContainsKey(spawn.Id))
            throw new ArgumentException($"Duplicate station entity id {spawn.Id}.", nameof(spawn));
        StationEntity entity;
        switch (spawn.Kind)
        {
            case StationEntityKind.Wrench:
                entity = new ItemEntity(this, spawn, "wrench");
                break;
            case StationEntityKind.MetalSheets:
                entity = new ItemEntity(this, spawn, "metal sheets", new SharedStack(spawn.Kind, spawn.Amount, "metal sheet"));
                break;
            case StationEntityKind.GeneratorFuel:
                entity = new ItemEntity(this, spawn, "generator fuel", new SharedStack(spawn.Kind, spawn.Amount, "generator fuel"));
                break;
            case StationEntityKind.BruisePack:
                entity = new ItemEntity(this, spawn, "bruise pack", new SharedStack(spawn.Kind, spawn.Amount, "bruise pack"));
                break;
            case StationEntityKind.ServiceCart:
                var cart = new ServiceCart(spawn);
                dispatcher.RegisterHandler(new CartWrenchHandler(cart));
                entity = cart;
                break;
            case StationEntityKind.Girder:
                var girder = new Girder(spawn);
                dispatcher.RegisterHandler(new GirderPlatingHandler(this, girder));
                entity = girder;
                break;
            case StationEntityKind.EngineeringAirlock:
                var door = new AccessDoor(spawn, "engineering airlock", StationClearance.Engine);
                dispatcher.RegisterHandler(new AccessDoorHandler(this, door));
                entity = door;
                break;
            case StationEntityKind.FuelGenerator:
                var generator = new PowerNetworkEntity(spawn, "fuel generator", powerLoop);
                dispatcher.RegisterHandler(new GeneratorHandler(this, generator));
                entity = generator;
                break;
            case StationEntityKind.PowerStorage:
                var storage = new PowerNetworkEntity(spawn, "power storage", powerLoop);
                dispatcher.RegisterHandler(new PowerStorageHandler(this, storage));
                entity = storage;
                break;
            case StationEntityKind.ApcLamp:
                entity = new PowerNetworkEntity(spawn, "APC-powered lamp", powerLoop);
                break;
            default:
                throw new ArgumentException($"Unknown station entity kind '{spawn.Kind}'.", nameof(spawn));
        }

        if (entity is ItemEntity item)
            dispatcher.RegisterHandler(new ItemPickupHandler(this, item));
        entities.Add(entity.Id, entity);
        entityOrder.Add(entity);
    }

    public bool AddActor(int peerId, Vector2 position) =>
        AddActor(peerId, position, StationCrewJobCatalog.AssistantJobId);

    public bool AddActor(int peerId, Vector2 position, int jobId)
    {
        if (peerId <= 0 || crew.ContainsKey(peerId) || entities.ContainsKey(peerId) || !IsFinite(position)) return false;
        CrewLoadoutProfile? profile = CrewLoadoutCatalog.GetForJob(jobId);
        var slotNames = new List<InventoryNamedSlot>
        {
            InventoryNamedSlot.leftHand,
            InventoryNamedSlot.rightHand,
        };
        if (profile != null)
            foreach (CrewLoadoutItem item in profile.Equipment)
                if (!slotNames.Contains(item.Slot)) slotNames.Add(item.Slot);

        var slots = slotNames.ConvertAll(SlotIdentifier.Named);
        var storage = new ItemStorage(slots, AcceptAllCapacity.Instance, name: $"crew-{peerId}");
        var member = new CrewMember(peerId, position, storage, jobId);
        crew.Add(peerId, member);
        crewByStorage.Add(storage, member);
        member.Body = new CrewBody(member);
        member.Handlers.Add(new CrewMeleeHandler(this, member.Body));
        member.Handlers.Add(new BruisePackHandler(this, member.Body));
        member.Handlers.Add(new CprHandler(this, member.Body));
        foreach (IInteractionHandler<HandApply> handler in member.Handlers)
            dispatcher.RegisterHandler(handler);
        entities.Add(peerId, member.Body);
        entityOrder.Add(member.Body);

        if (profile != null)
        {
            foreach (CrewLoadoutItem entry in profile.Equipment)
            {
                var item = new CrewEquipmentItem(entry,
                    entry.Slot == InventoryNamedSlot.id ? profile.StandardClearance : Array.Empty<StationClearance>(),
                    entry.Slot == InventoryNamedSlot.id ? profile.LowPopulationClearance : Array.Empty<StationClearance>());
                ItemSlot? destination = storage.GetNamedItemSlot(entry.Slot);
                if (destination == null || !inventory.ServerAdd(item, destination))
                    throw new InvalidOperationException($"Could not equip {entry.DisplayName} in {entry.Slot}.");
            }
        }
        PublishChanges();
        return true;
    }

    /// <summary>Checks the clearance sources Unitystation searches: active hand, ID, belt, and suit storage.</summary>
    public bool HasClearance(int peerId, StationClearance required, bool lowPopulation = false)
    {
        if (!crew.TryGetValue(peerId, out CrewMember? member)) return false;
        InventoryNamedSlot[] sourceSlots =
        [
            member.ActiveHand,
            InventoryNamedSlot.id,
            InventoryNamedSlot.belt,
            InventoryNamedSlot.suitStorage,
        ];

        foreach (InventoryNamedSlot slotName in sourceSlots)
        {
            if (member.Inventory.GetNamedItemSlot(slotName)?.Item is not CrewEquipmentItem item || item.ClearanceDisabled)
                continue;
            IReadOnlyList<StationClearance> issued = lowPopulation ? item.LowPopulationClearance : item.StandardClearance;
            if (issued.Contains(required)) return true;
        }
        return false;
    }

    /// <summary>True when the target is an access-restricted door this crew member lacks clearance for.</summary>
    public bool IsAccessDenied(int peerId, int targetId) =>
        entities.TryGetValue(targetId, out StationEntity? entity) && entity is AccessDoor door &&
        !HasClearance(peerId, door.RequiredClearance);

    public string GetCrewLoadoutSummary(int peerId)
    {
        if (!crew.TryGetValue(peerId, out CrewMember? member)) return string.Empty;
        CrewLoadoutProfile? profile = CrewLoadoutCatalog.GetForJob(member.JobId);
        if (profile == null)
        {
            string roleName = StationCrewJobCatalog.Jobs.FirstOrDefault(job => job.Id == member.JobId)?.Name
                ?? $"Unknown role {member.JobId}";
            return $"{roleName}: no standard issue kit";
        }

        var names = new List<string>();
        foreach (CrewLoadoutItem entry in profile.Equipment)
            if (member.Inventory.GetNamedItemSlot(entry.Slot)?.Item is IPickupable item)
                names.Add(item.Name);
        string access = string.Join(", ", profile.StandardClearance);
        return $"Engineer kit: {string.Join(" · ", names)}\nID access: {access}";
    }

    public bool RemoveActor(int peerId)
    {
        if (!crew.TryGetValue(peerId, out CrewMember? member)) return false;
        foreach (ItemEntity item in entityOrder.OfType<ItemEntity>())
            if (item.ItemSlot?.ItemStorage == member.Inventory)
                inventory.ServerDrop(item.ItemSlot);
        foreach (Girder girder in entityOrder.OfType<Girder>())
            girder.Task.Cancel(peerId);
        foreach (CrewMember other in crew.Values)
        {
            if (other.Treatment.PatientPeerId == peerId) other.Treatment.Cancel();
            if (other.Cpr.PatientPeerId == peerId) other.Cpr.Cancel();
        }
        StopPull(member, PullStopReason.Left);
        if (member.Body != null) StopPullsOf(member.Body, PullStopReason.Left);
        foreach (IInteractionHandler<HandApply> handler in member.Handlers)
            dispatcher.UnregisterHandler(handler);
        if (member.Body != null)
        {
            entities.Remove(peerId);
            entityOrder.Remove(member.Body);
            lastPublished.Remove(peerId);
        }
        crew.Remove(peerId);
        crewByStorage.Remove(member.Inventory);
        PublishChanges();
        return true;
    }

    public bool SetActorPosition(int peerId, Vector2 position)
    {
        if (!IsFinite(position) || !crew.TryGetValue(peerId, out CrewMember? member)) return false;
        member.Position = position;
        member.InteractionPlayer.PlaceAt(position);
        return true;
    }

    /// <summary>Moves a fixture that physics displaced, such as a pushed cart. Items move only through inventory.</summary>
    public bool SetEntityPosition(int entityId, Vector2 position)
    {
        if (!IsFinite(position) || !entities.TryGetValue(entityId, out StationEntity? entity) || entity is ItemEntity)
            return false;
        if (entity.WorldPosition == position) return true;
        entity.PlaceAt(position);
        PublishChanges();
        return true;
    }

    /// <summary>Changes the active hand without moving its contents.</summary>
    public bool TryToggleActiveHand(int peerId)
    {
        if (!crew.TryGetValue(peerId, out CrewMember? member) || !member.Health.CanUseHands) return false;
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

    /// <summary>Advance this crew member's server-owned construction work and recheck its live prerequisites.</summary>
    public void AdvanceConstruction(int peerId, double elapsedSeconds)
    {
        if (!crew.TryGetValue(peerId, out CrewMember? member)) return;
        foreach (Girder girder in entityOrder.OfType<Girder>())
        {
            if (girder.Task.WorkerPeerId != peerId) continue;
            ItemEntity? sheets = member.ActiveHandSlot.Item is ItemEntity { Kind: StationEntityKind.MetalSheets } held
                ? held
                : null;
            bool stillValid = sheets != null && IsReachable(member, girder);
            bool completed = girder.Task.Advance(peerId, elapsedSeconds, stillValid, sheets?.Stackable?.Amount ?? 0);
            if (completed)
            {
                if (!inventory.ServerConsume(member.ActiveHandSlot, GirderPlatingTask.RequiredSheets))
                    throw new InvalidOperationException("Validated girder material could not be consumed.");
                girder.Plated = true;
            }
            if (completed || !girder.Task.WorkerPeerId.HasValue)
                PublishChanges();
        }
    }

    public bool TryInteract(int senderPeerId, int targetId, Intent intent, bool isAltClick)
    {
        if (!crew.TryGetValue(senderPeerId, out CrewMember? member) || !member.Health.CanUseHands ||
            intent is not (Intent.Help or Intent.Disarm or Intent.Grab or Intent.Harm)) return false;
        if (!entities.TryGetValue(targetId, out StationEntity? target) || !target.CanBeTargeted) return false;

        if (!dispatcher.TryResolveClick(member, member.ActiveHandItem, target,
                target.WorldPosition, intent, isAltClick, NetworkSide.Server)) return false;
        PublishChanges();
        return true;
    }

    public CrewStat? GetCrewStat(int peerId) =>
        crew.TryGetValue(peerId, out CrewMember? member) ? member.Health.Stat : null;

    public double? GetCrewHealth(int peerId) =>
        crew.TryGetValue(peerId, out CrewMember? member) ? member.Health.Health : null;

    /// <summary>Where a dead player's ghost is, or null while they are alive or still in the body.</summary>
    public Vector2? GetGhostPosition(int peerId) =>
        crew.TryGetValue(peerId, out CrewMember? member) ? member.GhostPosition : null;

    /// <summary>Where this player perceives the world from: the ghost if they have one, otherwise the body.</summary>
    public Vector2? GetPerceptionPosition(int peerId) =>
        crew.TryGetValue(peerId, out CrewMember? member) ? member.GhostPosition ?? member.Position : null;

    /// <summary>
    /// Applies a dead player's movement input. The first input leaves the body at its position;
    /// later input moves the ghost through anything inside the bounds. The living are refused.
    /// Call <see cref="PublishChanges"/> to replicate the result.
    /// </summary>
    public bool TryMoveGhost(int peerId, int deltaX, int deltaY, double elapsedSeconds, Vector2 minimum, Vector2 maximum)
    {
        if (!crew.TryGetValue(peerId, out CrewMember? member) || !member.Health.IsDead) return false;
        if (deltaX is < -1 or > 1 || deltaY is < -1 or > 1 || (deltaX == 0 && deltaY == 0)) return false;
        bool leaving = member.GhostPosition == null;
        member.GhostPosition = GhostObserver.Move(member.GhostPosition ?? member.Position, deltaX, deltaY,
            elapsedSeconds, minimum, maximum);
        if (leaving) CrewGhosted?.Invoke(peerId);
        return true;
    }

    /// <summary>
    /// Advances crew life ticks, melee cooldowns and bruise pack treatments on the host.
    /// Call <see cref="PublishChanges"/> to replicate the result.
    /// </summary>
    public void AdvanceHealth(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return;
        clockSeconds += elapsedSeconds;
        foreach (CrewMember member in crew.Values.ToList())
        {
            CrewStat before = member.Health.Stat;
            member.Health.Advance(elapsedSeconds);
            NotifyStat(member, before);
            AdvanceTreatment(member, elapsedSeconds);
            AdvanceCpr(member, elapsedSeconds);
        }
    }

    private void AdvanceTreatment(CrewMember healer, double elapsedSeconds)
    {
        BruisePackTreatment treatment = healer.Treatment;
        if (treatment.PatientPeerId is not int patientId) return;
        bool hasPatient = crew.TryGetValue(patientId, out CrewMember? patient);
        ItemEntity? pack = healer.ActiveHandSlot.Item is ItemEntity { Kind: StationEntityKind.BruisePack } held &&
                           held.Stackable is { Amount: > 0 }
            ? held
            : null;
        bool valid = hasPatient && pack != null && healer.Health.CanUseHands && !patient!.Health.IsDead &&
                     IsReachable(healer, patient.Body!);
        if (!treatment.Advance(elapsedSeconds, valid)) return;

        treatment.Cancel();
        if (!inventory.ServerConsume(healer.ActiveHandSlot, 1))
            throw new InvalidOperationException("Validated bruise pack could not be consumed.");
        CrewStat before = patient!.Health.Stat;
        double healed = patient.Health.HealBrute(BruisePackTreatment.HealBrute);
        NotifyStat(patient, before);
        CrewTreated?.Invoke(healer.PeerId, patient.PeerId, healed);
        PublishChanges();
    }

    private void NotifyStat(CrewMember member, CrewStat before)
    {
        if (member.Health.Stat == before) return;
        if (!member.Health.CanUseHands)
        {
            member.Treatment.Cancel();
            member.Cpr.Cancel();
            foreach (Girder girder in entityOrder.OfType<Girder>())
                girder.Task.Cancel(member.PeerId);
        }
        CrewStatChanged?.Invoke(member.PeerId, before, member.Health.Stat);
    }

    private bool TryAttack(CrewMember attacker, CrewMember victim)
    {
        if (clockSeconds < attacker.NextMeleeAt) return false;
        attacker.NextMeleeAt = clockSeconds + CrewMelee.ClickCooldownSeconds;
        CrewStat before = victim.Health.Stat;
        victim.Health.ApplyBrute(CrewMelee.WrenchForce);
        NotifyStat(victim, before);
        CrewAttacked?.Invoke(attacker.PeerId, victim.PeerId, CrewMelee.WrenchForce);
        return true;
    }

    private bool TryUnarmedAttack(CrewMember attacker, CrewMember victim)
    {
        if (clockSeconds < attacker.NextMeleeAt) return false;
        attacker.NextMeleeAt = clockSeconds + CrewMelee.ClickCooldownSeconds;
        UnarmedAttackResult result = UnarmedAttack.Roll(attacker.Health.BruteLoss,
            victim.Health.Stat.IsFloored(), Random);
        if (result.Hit)
        {
            CrewStat before = victim.Health.Stat;
            victim.Health.ApplyBrute(result.Damage);
            NotifyStat(victim, before);
        }
        CrewStruckUnarmed?.Invoke(attacker.PeerId, victim.PeerId, result);
        return true;
    }

    private bool CanAttackNow(int peerId) =>
        crew.TryGetValue(peerId, out CrewMember? member) && clockSeconds >= member.NextMeleeAt;

    /// <summary>Advance authoritative power simulation time on the host only. Call <see cref="PublishChanges"/> to replicate it.</summary>
    public bool AdvancePower(double elapsedSeconds) => powerLoop.Advance(elapsedSeconds);

    /// <summary>Drops only the current active-hand item, as the source inventory does.</summary>
    public bool TryDropActiveHand(int senderPeerId)
    {
        if (!crew.TryGetValue(senderPeerId, out CrewMember? member) || !member.Health.CanUseHands) return false;
        ItemSlot? activeHand = member.ActiveHandSlot;
        return activeHand.Item != null && inventory.ServerDrop(activeHand);
    }

    private CrewMember? GetCrewForStorage(ItemStorage storage) =>
        crewByStorage.TryGetValue(storage.GetRootStorage(), out CrewMember? member) ? member : null;

    private bool IsReachable(CrewMember member, StationEntity target) =>
        Validations.IsReachableByPositions(member.Position, target.WorldPosition, true, reachabilityMap, context: target);

    private bool TryStartGirderPlating(int peerId, Girder girder)
    {
        if (!crew.TryGetValue(peerId, out CrewMember? member) ||
            member.ActiveHandSlot.Item is not ItemEntity { Kind: StationEntityKind.MetalSheets } sheets)
            return false;
        return girder.Task.TryStart(peerId, sheets.Stackable?.Amount ?? 0, isReachable: IsReachable(member, girder));
    }

    private bool TryLoadGeneratorFuel(int peerId)
    {
        if (!crew.TryGetValue(peerId, out CrewMember? member) ||
            member.ActiveHandSlot.Item is not ItemEntity { Kind: StationEntityKind.GeneratorFuel } fuel ||
            fuel.Stackable is not { Amount: > 0 } || !powerLoop.CanLoadFuel(1)) return false;
        if (!inventory.ServerConsume(member.ActiveHandSlot, 1)) return false;
        if (!powerLoop.TryLoadFuel(1)) throw new InvalidOperationException("Validated generator fuel could not be loaded.");
        return true;
    }

    private bool IsInActiveHand(int peerId, WorldEntity? item) =>
        item != null && crew.TryGetValue(peerId, out CrewMember? member) && ReferenceEquals(member.ActiveHandSlot.Item, item);

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    public bool IsHolderInContainer(ItemSlot fromSlot) => false;

    public void DisappearFromWorld(IPickupable item)
    {
        if (item is ItemEntity worldItem) worldItem.SetInWorld(false);
    }

    public void Despawn(IPickupable item)
    {
        if (item is ItemEntity worldItem) worldItem.SetInWorld(false);
    }

    public void Drop(IPickupable item, ItemSlot fromSlot, Vector2? worldTargetVector)
    {
        if (item is not ItemEntity worldItem) return;
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
        public CrewMember(int peerId, Vector2 position, ItemStorage inventory, int jobId)
        {
            PeerId = peerId;
            Position = position;
            Inventory = inventory;
            JobId = jobId;
            InteractionPlayer = new PlayerEntity { Id = peerId, Name = $"crew-{peerId}" };
            InteractionPlayer.PlaceAt(position);
        }

        public int PeerId { get; }
        public Vector2 Position { get; set; }
        public ItemStorage Inventory { get; }
        public int JobId { get; }
        public CrewHealth Health { get; } = new();
        public BruisePackTreatment Treatment { get; } = new();
        public CprAttempt Cpr { get; } = new();
        public CrewBody? Body { get; set; }
        public List<IInteractionHandler<HandApply>> Handlers { get; } = new();
        public double NextMeleeAt { get; set; }
        public Vector2? GhostPosition { get; set; }
        public StationEntity? Pulling { get; set; }
        public InventoryNamedSlot ActiveHand { get; set; } = InventoryNamedSlot.rightHand;
        public ItemSlot ActiveHandSlot => Inventory.GetNamedItemSlot(ActiveHand)!;
        public Vector2 WorldPosition => Position;
        public bool IsGhosting => false;
        public bool HasTelekinesis => false;
        public bool IsAlive => !Health.IsDead;
        public ConsciousState ConsciousState => Health.Stat switch
        {
            CrewStat.Stable => ConsciousState.Conscious,
            CrewStat.SoftCrit => ConsciousState.BarelyConscious,
            CrewStat.HardCrit => ConsciousState.Unconscious,
            _ => ConsciousState.Dead,
        };
        public IInteractable? ActiveHandItem => ActiveHandSlot.Item as IInteractable;
        public Intent CurrentIntent => Intent.Help;
        public PlayerEntity InteractionPlayer { get; }
    }

    /// <summary>Server half of one station entity: interaction target plus its replicated state.</summary>
    private abstract class StationEntity : WorldEntity, IInteractable
    {
        protected StationEntity(StationEntitySpawn spawn, string name)
        {
            Id = spawn.Id;
            Name = name;
            Kind = spawn.Kind;
            PlaceAt(spawn.Position);
        }

        public string Kind { get; }
        string IInteractable.Id => Name;
        public virtual Vector2 WorldPosition => AssumedWorldPosServer;
        public WorldEntity InteractionEntity => this;
        public virtual bool CanBeTargeted => true;
        protected virtual int? Holder => null;
        protected virtual StationEntityFlags Flags => StationEntityFlags.None;
        protected virtual void WriteFields(Dictionary<string, double> fields) { }

        public StationEntityState Capture()
        {
            var fields = new Dictionary<string, double>(StringComparer.Ordinal);
            WriteFields(fields);
            StationEntityFlags flags = Flags | (CanBeTargeted ? StationEntityFlags.Targetable : StationEntityFlags.None);
            return new StationEntityState(Id, Kind, WorldPosition, Holder, flags, fields);
        }
    }

    private sealed class ItemEntity : StationEntity, IPickupable
    {
        private readonly SharedStationInventoryWorld world;
        private ItemSlot? itemSlot;

        public ItemEntity(SharedStationInventoryWorld world, StationEntitySpawn spawn, string name,
            IStackable? stackable = null) : base(spawn, name)
        {
            this.world = world;
            PositionInWorld = spawn.Position;
            Stackable = stackable;
        }

        public ItemSlot? ItemSlot => itemSlot;
        public bool IsBuckled => false;
        public IStackable? Stackable { get; }
        public IEnumerable<ItemStorage> ItemStorages => Array.Empty<ItemStorage>();
        public IEnumerable<IServerInventoryMove> MoveHooks => Array.Empty<IServerInventoryMove>();
        public float ThrowSpeed => 6f;
        public float ThrowRange => 7f;
        public bool StickyMovement => false;
        public override Vector2 WorldPosition => itemSlot == null
            ? PositionInWorld
            : world.GetCrewForStorage(itemSlot.ItemStorage)?.Position ?? PositionInWorld;
        public Vector2 PositionInWorld { get; private set; }
        public bool IsInWorld { get; private set; } = true;
        private int RemainingAmount => IsInWorld || itemSlot != null ? Stackable?.Amount ?? 0 : 0;
        public override bool CanBeTargeted => IsInWorld && itemSlot == null && (Stackable == null || RemainingAmount > 0);
        protected override int? Holder => itemSlot == null ? null : world.GetCrewForStorage(itemSlot.ItemStorage)?.PeerId;
        protected override StationEntityFlags Flags => StationEntityFlags.Item;

        protected override void WriteFields(Dictionary<string, double> fields)
        {
            if (Stackable != null) fields[StationEntityField.Amount] = RemainingAmount;
        }

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

    /// <summary>The crew member's body as a station entity, so clicks and snapshots treat people like objects.</summary>
    private sealed class CrewBody(CrewMember member)
        : StationEntity(new StationEntitySpawn(member.PeerId, StationEntityKind.Crew, member.Position), $"crew-{member.PeerId}")
    {
        public CrewMember Member { get; } = member;
        public override Vector2 WorldPosition => Member.Position;

        protected override void WriteFields(Dictionary<string, double> fields)
        {
            fields[StationEntityField.Job] = Member.JobId;
            fields[StationEntityField.Health] = Member.Health.Health;
            fields[StationEntityField.BruteLoss] = Member.Health.BruteLoss;
            fields[StationEntityField.OxyLoss] = Member.Health.OxyLoss;
            fields[StationEntityField.Stat] = (int)Member.Health.Stat;
            if (Member.Pulling != null) fields[StationEntityField.Pulling] = Member.Pulling.Id;
            if (Member.GhostPosition is Vector2 ghost)
            {
                fields[StationEntityField.Ghost] = 1;
                fields[StationEntityField.GhostX] = ghost.X;
                fields[StationEntityField.GhostY] = ghost.Y;
            }
            if (Member.Cpr.PatientPeerId is int cprPatient)
            {
                fields[StationEntityField.CprPatient] = cprPatient;
                fields[StationEntityField.Progress] = Member.Cpr.ProgressSeconds;
            }
            if (Member.Treatment.PatientPeerId is int patient)
            {
                fields[StationEntityField.TreatmentPatient] = patient;
                fields[StationEntityField.Progress] = Member.Treatment.ProgressSeconds;
            }
        }
    }

    private sealed class ServiceCart(StationEntitySpawn spawn) : StationEntity(spawn, "service cart")
    {
        public bool Anchored { get; set; } = true;
        protected override StationEntityFlags Flags =>
            StationEntityFlags.Solid | (Anchored ? StationEntityFlags.None : StationEntityFlags.Pushable);
        protected override void WriteFields(Dictionary<string, double> fields) =>
            fields[StationEntityField.Anchored] = Anchored ? 1 : 0;
    }

    private sealed class Girder(StationEntitySpawn spawn) : StationEntity(spawn, "girder")
    {
        public GirderPlatingTask Task { get; } = new();
        public bool Plated { get; set; }
        public override bool CanBeTargeted => !Plated;
        protected override StationEntityFlags Flags => StationEntityFlags.Solid;

        protected override void WriteFields(Dictionary<string, double> fields)
        {
            fields[StationEntityField.Plated] = Plated ? 1 : 0;
            fields[StationEntityField.Worker] = Task.WorkerPeerId ?? 0;
            fields[StationEntityField.Progress] = Task.ProgressSeconds;
        }
    }

    private sealed class AccessDoor(StationEntitySpawn spawn, string name, StationClearance requiredClearance)
        : StationEntity(spawn, name), IPassable
    {
        public StationClearance RequiredClearance { get; } = requiredClearance;
        public bool IsOpen { get; private set; }
        public bool CanPass(PlayerMovementState state) => IsOpen;
        public void Toggle() => IsOpen = !IsOpen;
        protected override void WriteFields(Dictionary<string, double> fields) =>
            fields[StationEntityField.Open] = IsOpen ? 1 : 0;
    }

    /// <summary>A generator, storage unit or powered device on the playtest's single power loop.</summary>
    private sealed class PowerNetworkEntity(StationEntitySpawn spawn, string name, StationPowerLoop loop)
        : StationEntity(spawn, name)
    {
        public StationPowerLoop Loop { get; } = loop;
        public override bool CanBeTargeted => Kind != StationEntityKind.ApcLamp;

        protected override void WriteFields(Dictionary<string, double> fields)
        {
            switch (Kind)
            {
                case StationEntityKind.FuelGenerator:
                    fields[StationEntityField.Running] = Loop.GeneratorRunning ? 1 : 0;
                    fields[StationEntityField.FuelSeconds] = Loop.FuelSeconds;
                    break;
                case StationEntityKind.PowerStorage:
                    fields[StationEntityField.StoredEnergy] = Loop.StoredEnergy;
                    fields[StationEntityField.OutputEnabled] = Loop.OutputEnabled ? 1 : 0;
                    break;
                default:
                    fields[StationEntityField.Powered] = Loop.DevicePowered ? 1 : 0;
                    break;
            }
        }
    }

    private sealed class CrewEquipmentItem(
        CrewLoadoutItem definition,
        IReadOnlyList<StationClearance> standardClearance,
        IReadOnlyList<StationClearance> lowPopulationClearance) : IPickupable
    {
        public string Name => definition.DisplayName;
        public string ItemId => definition.ItemId;
        public ItemSlot? ItemSlot { get; private set; }
        public bool IsBuckled => false;
        public IStackable? Stackable => null;
        public IEnumerable<ItemStorage> ItemStorages => Array.Empty<ItemStorage>();
        public IEnumerable<IServerInventoryMove> MoveHooks => Array.Empty<IServerInventoryMove>();
        public float ThrowSpeed => 6f;
        public float ThrowRange => 7f;
        public bool StickyMovement => false;
        public IReadOnlyList<StationClearance> StandardClearance { get; } = standardClearance;
        public IReadOnlyList<StationClearance> LowPopulationClearance { get; } = lowPopulationClearance;
        public bool ClearanceDisabled { get; set; }
        public void SetItemSlot(ItemSlot? slot) => ItemSlot = slot;
    }

    /// <summary>A stack that only merges with stacks of the same entity kind.</summary>
    private sealed class SharedStack(string kind, int initialAmount, string pieceName) : IStackable
    {
        public string Kind { get; } = kind;
        public int Amount { get; private set; } = initialAmount;
        public bool CanAccommodate(IPickupable other) => other.Stackable is SharedStack stack && stack.Kind == Kind;
        public void ServerCombine(IStackable other)
        {
            if (other is not SharedStack stack || stack.Kind != Kind) return;
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
            if (Amount <= 0) throw new InvalidOperationException($"Cannot split an empty {pieceName} stack.");
            Amount--;
            return new LooseStackPiece(pieceName);
        }
    }

    private sealed class LooseStackPiece(string name) : IPickupable
    {
        public string Name => name;
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

    private sealed class AcceptAllCapacity : IItemStorageCapacity
    {
        public static AcceptAllCapacity Instance { get; } = new();
        public bool CanFit(IPickupable toCheck, SlotIdentifier inSlot) => true;
    }

    private sealed class ItemPickupHandler(SharedStationInventoryWorld world, ItemEntity owner)
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

    private sealed class CartWrenchHandler(ServiceCart owner) : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;

        public bool WillInteract(HandApply interaction, NetworkSide side) =>
            interaction.Performer != null && interaction.TargetObject == Owner &&
            interaction.HandObject is ItemEntity { Kind: StationEntityKind.Wrench };

        public void ServerPerformInteraction(HandApply interaction) => owner.Anchored = !owner.Anchored;
    }

    private sealed class GirderPlatingHandler(SharedStationInventoryWorld world, Girder owner)
        : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;

        public bool WillInteract(HandApply interaction, NetworkSide side) =>
            interaction.Performer != null && interaction.TargetObject == Owner &&
            interaction.HandObject is ItemEntity { Kind: StationEntityKind.MetalSheets } sheets &&
            world.IsInActiveHand(interaction.Performer.Id, sheets) &&
            (sheets.Stackable?.Amount ?? 0) >= GirderPlatingTask.RequiredSheets &&
            !owner.Task.IsComplete && !owner.Task.WorkerPeerId.HasValue;

        public void ServerPerformInteraction(HandApply interaction)
        {
            if (interaction.Performer != null) world.TryStartGirderPlating(interaction.Performer.Id, owner);
        }
    }

    private sealed class AccessDoorHandler(SharedStationInventoryWorld world, AccessDoor owner)
        : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => true;
        public int Priority => 0;

        public bool WillInteract(HandApply interaction, NetworkSide side) =>
            side == NetworkSide.Server && interaction.Performer != null &&
            interaction.TargetObject == Owner && world.HasClearance(interaction.Performer.Id, owner.RequiredClearance);

        public void ServerPerformInteraction(HandApply interaction) => owner.Toggle();
    }

    private sealed class GeneratorHandler(SharedStationInventoryWorld world, PowerNetworkEntity owner)
        : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;

        public bool WillInteract(HandApply interaction, NetworkSide side)
        {
            if (side != NetworkSide.Server || interaction.Performer == null || interaction.TargetObject != Owner)
                return false;
            if (interaction.HandObject is ItemEntity { Kind: StationEntityKind.GeneratorFuel } fuel)
                return fuel.Stackable is { Amount: > 0 } && owner.Loop.CanLoadFuel(1);
            return interaction.HandObject == null && (owner.Loop.GeneratorRunning || owner.Loop.FuelSeconds > 0);
        }

        public void ServerPerformInteraction(HandApply interaction)
        {
            if (interaction.Performer == null) return;
            if (interaction.HandObject is ItemEntity { Kind: StationEntityKind.GeneratorFuel })
                world.TryLoadGeneratorFuel(interaction.Performer.Id);
            else
                owner.Loop.TryToggleGenerator();
        }
    }

    /// <summary>
    /// Harm-intent attack on a crew body, then the attacker's melee cooldown: 5 brute with the wrench,
    /// or an unarmed punch or kick with an empty hand.
    /// </summary>
    private sealed class CrewMeleeHandler(SharedStationInventoryWorld world, CrewBody owner)
        : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;

        public bool WillInteract(HandApply interaction, NetworkSide side) =>
            side == NetworkSide.Server && interaction.Performer != null && interaction.TargetObject == Owner &&
            interaction.Intent == Intent.Harm && world.CanAttackNow(interaction.Performer.Id) &&
            (interaction.HandObject == null ||
             (interaction.HandObject is ItemEntity { Kind: StationEntityKind.Wrench } wrench &&
              world.IsInActiveHand(interaction.Performer.Id, wrench)));

        public void ServerPerformInteraction(HandApply interaction)
        {
            if (interaction.Performer == null ||
                !world.crew.TryGetValue(interaction.Performer.Id, out CrewMember? attacker)) return;
            if (interaction.HandObject == null) world.TryUnarmedAttack(attacker, owner.Member);
            else world.TryAttack(attacker, owner.Member);
        }
    }

    /// <summary>Bruise pack in the active hand applied to an injured, living crew body (including oneself).</summary>
    private sealed class BruisePackHandler(SharedStationInventoryWorld world, CrewBody owner)
        : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;

        public bool WillInteract(HandApply interaction, NetworkSide side) =>
            side == NetworkSide.Server && interaction.Performer != null && interaction.TargetObject == Owner &&
            interaction.HandObject is ItemEntity { Kind: StationEntityKind.BruisePack, Stackable.Amount: > 0 } pack &&
            world.IsInActiveHand(interaction.Performer.Id, pack) &&
            world.crew.TryGetValue(interaction.Performer.Id, out CrewMember? healer) &&
            !healer.Treatment.PatientPeerId.HasValue &&
            !owner.Member.Health.IsDead && owner.Member.Health.BruteLoss > 0;

        public void ServerPerformInteraction(HandApply interaction)
        {
            if (interaction.Performer != null &&
                world.crew.TryGetValue(interaction.Performer.Id, out CrewMember? healer))
                healer.Treatment.TryStart(healer.PeerId, owner.Member.PeerId, owner.Member.Health);
        }
    }

    private sealed class PowerStorageHandler(SharedStationInventoryWorld world, PowerNetworkEntity owner)
        : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;

        public bool WillInteract(HandApply interaction, NetworkSide side) =>
            side == NetworkSide.Server && interaction.Performer != null && interaction.TargetObject == Owner &&
            interaction.HandObject == null && world.crew.ContainsKey(interaction.Performer.Id);

        public void ServerPerformInteraction(HandApply interaction) => owner.Loop.ToggleOutput();
    }
}
