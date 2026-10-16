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
	IOwnedInteractionHandler<HandApply>, IStackable
{
	[Export] public string ItemName { get; set; } = "Item";
	[Export] public string ItemIconPath { get; set; } = string.Empty;
	[Export(PropertyHint.MultilineText)] public string ItemDescription { get; set; } = string.Empty;
	[Export] public float ThrowSpeed { get; set; } = 6f;
	[Export] public float ThrowRange { get; set; } = 7f;
	[Export] public bool StickyMovement { get; set; }
	[Export] public int IndexedStorageSlots { get; set; }
	[Export] public string StackTypeId { get; set; } = string.Empty;
	[Export(PropertyHint.Range, "1,999,1")] public int StackAmount { get; set; } = 1;
	[Export(PropertyHint.Range, "1,999,1")] public int MaxStackAmount { get; set; } = 50;

	private readonly List<ItemStorage> itemStorages = new();
	private readonly List<IServerInventoryMove> moveHooks = new();
	private ItemSlot? itemSlot;
	private InteractionDispatcher? dispatcher;

	string IPickupable.Name => string.IsNullOrWhiteSpace(ItemName) ? Name : ItemName;
	ItemSlot? IPickupable.ItemSlot => itemSlot;
	IStackable? IPickupable.Stackable => MaxStackAmount > 1 ? this : null;
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

	int IStackable.Amount => StackAmount;
	bool IStackable.CanAccommodate(IPickupable other) =>
		!string.IsNullOrWhiteSpace(StackTypeId)
		&& other is InventoryItemNode2D otherItem
		&& string.Equals(StackTypeId, otherItem.StackTypeId, System.StringComparison.Ordinal)
		&& StackAmount + otherItem.StackAmount <= MaxStackAmount;

	void IStackable.ServerCombine(IStackable other)
	{
		if (other is not InventoryItemNode2D otherItem ||
		    !((IStackable)this).CanAccommodate(otherItem))
			throw new System.InvalidOperationException("Incompatible item stacks cannot be combined.");
		StackAmount += otherItem.StackAmount;
	}

	bool IStackable.ServerConsume(int amountToConsume)
	{
		if (amountToConsume <= 0 || amountToConsume >= StackAmount) return false;
		StackAmount -= amountToConsume;
		return true;
	}

	IPickupable IStackable.ServerRemoveOne()
	{
		if (StackAmount <= 1)
			throw new System.InvalidOperationException("A one-item stack cannot be split.");
		var duplicate = Duplicate() as InventoryItemNode2D
			?? throw new System.InvalidOperationException("Could not duplicate the stack item.");
		duplicate.Id = System.Guid.NewGuid().ToString("N");
		duplicate.StackAmount = 1;
		StackAmount--;
		return duplicate;
	}

	private sealed class AcceptAllCapacity : IItemStorageCapacity
	{
		public static readonly AcceptAllCapacity Instance = new();
		public bool CanFit(IPickupable toCheck, SlotIdentifier inSlot) => true;
	}
}
