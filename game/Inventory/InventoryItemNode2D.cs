using System.Collections.Generic;
using System.Linq;
using Godot;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Inventory;
using Godotstation.Game.Input;
using Godotstation.Game.UI;
using Godotstation.Game.World;

namespace Godotstation.Game.Inventory;

/// <summary>Godot scene-node adapter for the engine's Godot-free pickupable contract.</summary>
public partial class InventoryItemNode2D : InteractableNode2D, IPickupable,
	IOwnedInteractionHandler<HandApply>
{
	[Export] public string ItemName { get; set; } = "Item";
	[Export] public string ItemIconPath { get; set; } = string.Empty;
	[Export(PropertyHint.MultilineText)] public string ItemDescription { get; set; } = string.Empty;
	[Export] public float ThrowSpeed { get; set; } = 6f;
	[Export] public float ThrowRange { get; set; } = 7f;
	[Export] public bool StickyMovement { get; set; }
	[Export] public int IndexedStorageSlots { get; set; }

	private readonly List<ItemStorage> itemStorages = new();
	private readonly List<IServerInventoryMove> moveHooks = new();
	private ItemSlot? itemSlot;
	private InteractionDispatcher? dispatcher;

	string IPickupable.Name => string.IsNullOrWhiteSpace(ItemName) ? Name : ItemName;
	ItemSlot? IPickupable.ItemSlot => itemSlot;
	IStackable? IPickupable.Stackable => null;
	IEnumerable<ItemStorage> IPickupable.ItemStorages => itemStorages;
	IEnumerable<IServerInventoryMove> IPickupable.MoveHooks => moveHooks;
	bool IPickupable.IsBuckled => false;
	WorldEntity IOwnedInteractionHandler<HandApply>.Owner => InteractionEntity;
	public bool IsFirstInteractable => true;
	public int Priority => 0;

	public override void _Ready()
	{
		base._Ready();
		dispatcher = GetNode<InteractionServiceNode>("/root/Interaction").Dispatcher;
		dispatcher.RegisterHandler(this);
		if (IndexedStorageSlots <= 0) return;

		var identifiers = Enumerable.Range(0, IndexedStorageSlots)
			.Select(SlotIdentifier.Indexed)
			.ToArray();
		itemStorages.Add(new ItemStorage(identifiers, AcceptAllCapacity.Instance, this, $"{ItemName} storage"));
	}

	public override void _ExitTree()
	{
		dispatcher?.UnregisterHandler(this);
		base._ExitTree();
	}

	public bool WillInteract(HandApply interaction, NetworkSide side) => itemSlot == null;

	public void ServerPerformInteraction(HandApply interaction)
		=> UiServices.Interactions.RequestPickup(InteractionEntity.Id);

	void IPickupable.SetItemSlot(ItemSlot? slot) => itemSlot = slot;

	private sealed class AcceptAllCapacity : IItemStorageCapacity
	{
		public static readonly AcceptAllCapacity Instance = new();
		public bool CanFit(IPickupable toCheck, SlotIdentifier inSlot) => true;
	}
}
