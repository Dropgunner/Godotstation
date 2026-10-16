using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Inventory;
using Godotstation.Game.Input;
using Godotstation.Game.Player;
using Godotstation.Game.UI;
using Godotstation.Game.World;
using EngineNamedSlot = Godotstation.Engine.Inventory.NamedSlot;
using InteractionBodyPartType = Godotstation.Common.Interactions.BodyPartType;
using PresentationNamedSlot = Godotstation.Common.Interactions.NamedSlot;

namespace Godotstation.Game.Inventory;

/// <summary>
/// Local single-player bridge between the authoritative inventory engine and the HUD
/// contracts. Multiplayer transports can replace this node while keeping the same interfaces.
/// </summary>
public partial class InventoryServiceNode : Node, IInventoryProvider, IInteractionService
{
	private static readonly (PresentationNamedSlot Presentation, EngineNamedSlot Engine)[] PlayerSlots =
	{
		(PresentationNamedSlot.LeftHand, EngineNamedSlot.leftHand),
		(PresentationNamedSlot.RightHand, EngineNamedSlot.rightHand),
		(PresentationNamedSlot.Head, EngineNamedSlot.head),
		(PresentationNamedSlot.Eyes, EngineNamedSlot.eyes),
		(PresentationNamedSlot.Mask, EngineNamedSlot.mask),
		(PresentationNamedSlot.Ear, EngineNamedSlot.ear),
		(PresentationNamedSlot.Neck, EngineNamedSlot.neck),
		(PresentationNamedSlot.UnderSuit, EngineNamedSlot.uniform),
		(PresentationNamedSlot.Suit, EngineNamedSlot.outerwear),
		(PresentationNamedSlot.SuitStorage, EngineNamedSlot.suitStorage),
		(PresentationNamedSlot.Back, EngineNamedSlot.back),
		(PresentationNamedSlot.Belt, EngineNamedSlot.belt),
		(PresentationNamedSlot.Gloves, EngineNamedSlot.hands),
		(PresentationNamedSlot.Shoes, EngineNamedSlot.feet),
		(PresentationNamedSlot.Pocket1, EngineNamedSlot.storage01),
		(PresentationNamedSlot.Pocket2, EngineNamedSlot.storage02),
		(PresentationNamedSlot.Id, EngineNamedSlot.id),
	};

	private readonly Dictionary<ItemSlot, int> idsBySlot = new();
	private readonly Dictionary<int, ItemSlot> slotsById = new();
	private readonly Dictionary<ItemStorage, Node2D> holdersByStorage = new();
	private readonly Dictionary<int, List<ItemStorage>> storagesByItemId = new();
	private PlayerCharacter? player;
	private ItemStorage? playerStorage;
	private InventorySystem? inventory;
	private int nextSlotId = 1;

	public event Action<int, SlotData>? OnSlotChanged;
	public event Action<int, StorageSlotData>? OnStorageSlotChanged;

	public override void _Ready()
	{
		UiServices.Inventory = this;
		UiServices.Interactions = this;
	}

	/// <summary>Called by the local player once its scene nodes have entered the tree.</summary>
	public void RegisterPlayer(PlayerCharacter localPlayer)
	{
		if (playerStorage != null)
		{
			if (ReferenceEquals(player, localPlayer)) return;
			throw new InvalidOperationException("The local inventory service supports one player.");
		}

		player = localPlayer;
		UiServices.LocalPlayerId = localPlayer.InteractionPlayer.Id;
		var slots = PlayerSlots.Select(mapping => SlotIdentifier.Named(mapping.Engine));
		playerStorage = new ItemStorage(slots, AcceptAllCapacity.Instance, name: "local player");
		holdersByStorage.Add(playerStorage, localPlayer);
		var sceneRoot = GetTree().CurrentScene as Node2D ?? localPlayer.GetParent() as Node2D;
		if (sceneRoot == null)
		{
			throw new InvalidOperationException("The local player must be under a Node2D scene root.");
		}

		var world = new GodotInventoryWorld(sceneRoot, slot =>
			holdersByStorage.TryGetValue(slot.GetRootStorage(), out var holder) ? holder : null);
		inventory = new InventorySystem(world);
		TrackStorage(playerStorage);

		var leftHand = playerStorage.GetNamedItemSlot(EngineNamedSlot.leftHand)!;
		UiServices.ActiveHandSlotId = GetSlotId(leftHand);
		if (localPlayer.ActiveHandItem is IPickupable heldItem && heldItem.ItemSlot == null)
		{
			RegisterNestedStorages(heldItem);
			inventory.ServerAdd(heldItem, leftHand);
		}

		UpdateActiveHandItem();
	}

	public void UnregisterPlayer(PlayerCharacter localPlayer)
	{
		if (!ReferenceEquals(player, localPlayer)) return;
		// Scene teardown can call this while Godot is still attaching or removing children.
		// Inventory moves reparent scene nodes, so perform gameplay drops explicitly before
		// unregistering rather than mutating the tree from _ExitTree.
		player = null;
		playerStorage = null;
		inventory = null;
		idsBySlot.Clear();
		slotsById.Clear();
		holdersByStorage.Clear();
		storagesByItemId.Clear();
		nextSlotId = 1;
		UiServices.ActiveHandSlotId = -1;
	}

	public InteractableNode2D? GetActiveHandItem(PlayerCharacter requestor)
	{
		if (!ReferenceEquals(player, requestor)) return requestor.ActiveHandItem;
		return slotsById.TryGetValue(UiServices.ActiveHandSlotId, out var slot)
			? slot.Item as InteractableNode2D
			: null;
	}

	public bool IsRegisteredPlayer(PlayerCharacter candidate) => ReferenceEquals(player, candidate);

	public IReadOnlyList<SlotData> GetPlayerSlots(int playerId)
	{
		if (playerStorage == null || player?.InteractionPlayer.Id != playerId) return Array.Empty<SlotData>();
		return playerStorage.GetItemSlots().Select(ToSlotData).ToArray();
	}

	public SlotData GetSlot(int slotId)
	{
		if (!slotsById.TryGetValue(slotId, out var slot))
		{
			throw new KeyNotFoundException($"Inventory slot {slotId} is not registered.");
		}

		return ToSlotData(slot);
	}

	public IReadOnlyList<StorageSlotData> GetStorageSlots(int itemId)
	{
		if (!storagesByItemId.TryGetValue(itemId, out var storages)) return Array.Empty<StorageSlotData>();
		return storages.SelectMany(storage => storage.GetItemSlots()).Select(ToStorageSlotData).ToArray();
	}

	public void RequestHandApply(int performerId, int targetId, BodyPart targetPart, bool isAltClick)
	{
		if (player == null || performerId != player.InteractionPlayer.Id) return;
		var target = InteractableNode2D.FromEntityId(targetId);
		var localPlayer = player.InteractionPlayer;
		var damageZone = ConvertBodyPart(targetPart);
		var activeSlot = slotsById.TryGetValue(UiServices.ActiveHandSlotId, out var selectedSlot)
			? selectedSlot : null;
		var heldEntity = activeSlot?.Item is InteractableNode2D activeItem
			? activeItem.InteractionEntity : null;
		var local = new LocalPlayerContext
		{
			Player = localPlayer,
			ActiveHandSlot = heldEntity == null ? null : new ItemSlotRef
			{
				Named = activeSlot?.NamedSlot is { } engineHand
					? PlayerSlots.First(mapping => mapping.Engine == engineHand).Presentation
					: PresentationNamedSlot.LeftHand,
				Item = heldEntity,
				RootPlayer = localPlayer,
			},
			CurrentIntent = player.CurrentIntent,
			IsAltActionKeyPressed = isAltClick,
			DamageZone = damageZone,
		};
		GetNode<InteractionServiceNode>("/root/Interaction").Dispatcher.TryResolveClick(
			local, target?.InteractionEntity, target?.WorldPosition ?? player.WorldPosition, NetworkSide.Server);
	}

	public void RequestInventoryTransfer(int fromSlotId, int toSlotId)
	{
		if (inventory == null || !slotsById.TryGetValue(fromSlotId, out var from)
			|| !slotsById.TryGetValue(toSlotId, out var to)) return;

		inventory.ServerTransfer(from, to);
		UpdateActiveHandItem();
	}

	public void RequestDrop(int slotId)
	{
		if (inventory == null || !slotsById.TryGetValue(slotId, out var slot)) return;
		inventory.ServerDrop(slot);
		UpdateActiveHandItem();
	}

	public bool RequestConsume(int slotId, int amount)
	{
		if (inventory == null || !slotsById.TryGetValue(slotId, out var slot)) return false;
		var consumed = inventory.ServerConsume(slot, amount);
		UpdateActiveHandItem();
		return consumed;
	}

	public bool RequestVanishOne(int slotId)
	{
		if (inventory == null || !slotsById.TryGetValue(slotId, out var slot)) return false;
		var removed = inventory.ServerVanishStackable(slot);
		if (removed is Node node) node.QueueFree();
		UpdateActiveHandItem();
		return removed != null;
	}

	public void RequestPickup(int itemId)
	{
		if (inventory == null || playerStorage == null) return;
		if (InteractableNode2D.FromEntityId(itemId) is not IPickupable pickupable) return;
		RegisterNestedStorages(pickupable);
		inventory.ServerTryAdd(playerStorage, pickupable);
		UpdateActiveHandItem();
	}

	private void TrackStorage(ItemStorage storage)
	{
		foreach (var slot in storage.GetItemSlots())
		{
			GetSlotId(slot);
		}
	}

	private void RegisterNestedStorages(IPickupable item)
	{
		if (item is not Node2D itemNode || item is not InteractableNode2D interactableItem) return;
		if (!storagesByItemId.TryGetValue(interactableItem.InteractionEntity.Id, out var itemStorages))
		{
			itemStorages = new List<ItemStorage>();
			storagesByItemId.Add(interactableItem.InteractionEntity.Id, itemStorages);
		}

		foreach (var storage in item.ItemStorages)
		{
			if (itemStorages.Contains(storage)) continue;
			itemStorages.Add(storage);
			holdersByStorage[storage] = itemNode;
			TrackStorage(storage);
		}
	}

	private int GetSlotId(ItemSlot slot)
	{
		if (idsBySlot.TryGetValue(slot, out var id)) return id;
		id = nextSlotId++;
		idsBySlot.Add(slot, id);
		slotsById.Add(id, slot);
		slot.OnSlotContentsChangeServer += () =>
		{
			if (slot.NamedSlot is null)
				OnStorageSlotChanged?.Invoke(id, ToStorageSlotData(slot));
			else
				OnSlotChanged?.Invoke(id, ToSlotData(slot));
		};
		return id;
	}

	private StorageSlotData ToStorageSlotData(ItemSlot slot)
	{
		var item = slot.Item;
		var itemNode = item as InventoryItemNode2D;
		return new StorageSlotData(
			GetSlotId(slot),
			slot.SlotIdentifier.SlotIndex,
			item != null,
			item is InteractableNode2D interactable ? interactable.InteractionEntity.Id : 0,
			item?.Name ?? string.Empty,
			itemNode?.ItemIconPath ?? string.Empty,
			itemNode?.ItemDescription ?? string.Empty)
		{
			StackAmount = (item as IPickupable)?.Stackable?.Amount ?? 0,
		};
	}

	private SlotData ToSlotData(ItemSlot slot)
	{
		var mapping = PlayerSlots.FirstOrDefault(candidate => candidate.Engine == slot.NamedSlot);
		if (slot.NamedSlot == null || mapping.Engine != slot.NamedSlot.Value)
		{
			throw new InvalidOperationException($"Slot {slot} has no HUD presentation mapping.");
		}

		var item = slot.Item;
		var itemNode = item as InventoryItemNode2D;
		return new SlotData(
			GetSlotId(slot),
			mapping.Presentation,
			item != null,
			item is InteractableNode2D interactable ? interactable.InteractionEntity.Id : 0,
			item?.Name ?? string.Empty,
			itemNode?.ItemIconPath ?? string.Empty,
			itemNode?.ItemDescription ?? string.Empty)
		{
			StackAmount = (item as IPickupable)?.Stackable?.Amount ?? 0,
		};
	}

	private void UpdateActiveHandItem()
	{
		if (playerStorage == null || player == null) return;
		player.ActiveHandItem = slotsById.TryGetValue(UiServices.ActiveHandSlotId, out var selectedSlot)
			? selectedSlot.Item as InteractableNode2D
			: null;
	}

	private static InteractionBodyPartType ConvertBodyPart(BodyPart part) => part switch
	{
		BodyPart.Head => InteractionBodyPartType.Head,
		BodyPart.Eyes => InteractionBodyPartType.Eyes,
		BodyPart.Mouth => InteractionBodyPartType.Mouth,
		BodyPart.LeftArm => InteractionBodyPartType.LeftArm,
		BodyPart.RightArm => InteractionBodyPartType.RightArm,
		BodyPart.LeftHand => InteractionBodyPartType.LeftHand,
		BodyPart.RightHand => InteractionBodyPartType.RightHand,
		BodyPart.Groin => InteractionBodyPartType.Groin,
		BodyPart.LeftLeg => InteractionBodyPartType.LeftLeg,
		BodyPart.RightLeg => InteractionBodyPartType.RightLeg,
		BodyPart.LeftFoot => InteractionBodyPartType.LeftFoot,
		BodyPart.RightFoot => InteractionBodyPartType.RightFoot,
		_ => InteractionBodyPartType.Chest,
	};

	private sealed class AcceptAllCapacity : IItemStorageCapacity
	{
		public static readonly AcceptAllCapacity Instance = new();
		public bool CanFit(IPickupable toCheck, SlotIdentifier inSlot) => true;
	}
}
