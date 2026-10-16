using System;
using System.Collections.Generic;
using Godot;
using Godotstation.Common.Interactions;

namespace Godotstation.Game.UI.Inventory
{
	/// <summary>
	/// The paper-doll inventory HUD (frontend of DynamicItemStorage).
	/// Builds one ItemSlotUI per body slot, keeps them in sync with
	/// IInventoryProvider.OnSlotChanged, tracks the active hand, and toggles
	/// the expandable backpack/belt panels.
	///
	/// DynamicItemStorage's whole point is that slots appear and disappear
	/// with clothing (put on a belt → belt slots exist). The UI therefore
	/// treats the slot list as fully dynamic: Rebuild() is cheap and correct,
	/// per-slot updates are the fast path.
	/// </summary>
	public partial class InventoryUI : Control
	{
		[Export] private PackedScene itemSlotScene = null!;

		private Control paperDoll = null!;
		private HBoxContainer handsRow = null!;
		private GridContainer backpackGrid = null!;
		private GridContainer beltGrid = null!;
		private PanelContainer backpackPanel = null!;
		private PanelContainer beltPanel = null!;

		private readonly Dictionary<int, ItemSlotUI> slotsById = new Dictionary<int, ItemSlotUI>();
		private readonly Dictionary<int, ItemSlotUI> storageSlotViewsById = new Dictionary<int, ItemSlotUI>();
		private readonly Dictionary<int, StorageSlotData> storageSlotsById = new Dictionary<int, StorageSlotData>();
		private IInventoryProvider inventory = null!;
		private Action<int, SlotData> slotChangedHandler = null!;
		private Action<int, StorageSlotData> storageSlotChangedHandler = null!;

		/// <summary>
		/// Paper-doll anchor per named slot, in the layout of the Unity HUD:
		/// clothing arranged around the silhouette, hands centered below,
		/// pockets/ID/belt on the bottom bar.
		/// </summary>
		private static readonly (NamedSlot slot, Vector2 position)[] PaperDollLayout =
		{
			(NamedSlot.Head, new Vector2(72, 0)),
			(NamedSlot.Eyes, new Vector2(24, 36)),
			(NamedSlot.Mask, new Vector2(72, 36)),
			(NamedSlot.Ear, new Vector2(120, 36)),
			(NamedSlot.Neck, new Vector2(24, 72)),
			(NamedSlot.UnderSuit, new Vector2(72, 72)),
			(NamedSlot.Suit, new Vector2(120, 72)),
			(NamedSlot.Gloves, new Vector2(24, 108)),
			(NamedSlot.SuitStorage, new Vector2(120, 108)),
			(NamedSlot.Shoes, new Vector2(72, 144)),
			(NamedSlot.Back, new Vector2(0, 180)),
			(NamedSlot.Belt, new Vector2(48, 180)),
			(NamedSlot.Pocket1, new Vector2(96, 180)),
			(NamedSlot.Pocket2, new Vector2(144, 180)),
			(NamedSlot.Id, new Vector2(192, 180))
		};

		public override void _Ready()
		{
			inventory = UiServices.Inventory;

			paperDoll = GetNode<Control>("PaperDoll");
			handsRow = GetNode<HBoxContainer>("HandsRow");
			backpackPanel = GetNode<PanelContainer>("BackpackPanel");
			backpackGrid = GetNode<GridContainer>("BackpackPanel/ScrollContainer/BackpackGrid");
			beltPanel = GetNode<PanelContainer>("BeltPanel");
			beltGrid = GetNode<GridContainer>("BeltPanel/BeltGrid");

			backpackPanel.Visible = false;
			beltPanel.Visible = false;

			slotChangedHandler = OnInventorySlotChanged;
			storageSlotChangedHandler = OnInventoryStorageSlotChanged;
			inventory.OnSlotChanged += slotChangedHandler;
			inventory.OnStorageSlotChanged += storageSlotChangedHandler;

			Rebuild();
		}

		public override void _ExitTree()
		{
			if (inventory == null) return;
			if (slotChangedHandler != null) inventory.OnSlotChanged -= slotChangedHandler;
			if (storageSlotChangedHandler != null) inventory.OnStorageSlotChanged -= storageSlotChangedHandler;
		}

		private void OnInventorySlotChanged(int slotId, SlotData data)
		{
			Callable.From(() =>
			{
				if (GodotObject.IsInstanceValid(this) && IsInsideTree()) ApplySlotChange(slotId, data);
			}).CallDeferred();
		}

		private void OnInventoryStorageSlotChanged(int slotId, StorageSlotData data)
		{
			Callable.From(() =>
			{
				if (GodotObject.IsInstanceValid(this) && IsInsideTree()) ApplyStorageSlotChange(slotId, data);
			}).CallDeferred();
		}

		/// <summary>Full rebuild from provider state (login, respawn, clothing change).</summary>
		public void Rebuild()
		{
			foreach (ItemSlotUI slot in slotsById.Values)
			{
				slot.QueueFree();
			}

			slotsById.Clear();

			foreach (SlotData data in inventory.GetPlayerSlots(UiServices.LocalPlayerId))
			{
				ItemSlotUI slot = MakeSlot(data);

				switch (data.Named)
				{
					case NamedSlot.LeftHand:
					case NamedSlot.RightHand:
						handsRow.AddChild(slot);
						break;

					default:
						paperDoll.AddChild(slot);
						slot.Position = PositionFor(data.Named);
						break;
				}
			}

			UpdateActiveHandHighlight();
		}

		private static Vector2 PositionFor(NamedSlot named)
		{
			foreach ((NamedSlot slot, Vector2 position) in PaperDollLayout)
			{
				if (slot == named)
				{
					return position;
				}
			}

			return Vector2.Zero;
		}

		private ItemSlotUI MakeSlot(SlotData data)
		{
			var slot = itemSlotScene.Instantiate<ItemSlotUI>();
			slotsById[data.SlotId] = slot;
			slot.SlotClicked += OnSlotClicked;
			// SetSlot needs the slot's children, which only exist after _Ready.
			slot.Ready += () => slot.SetSlot(data);
			return slot;
		}

		private void ApplySlotChange(int slotId, SlotData data)
		{
			if (slotsById.TryGetValue(slotId, out ItemSlotUI? slot))
			{
				slot.SetSlot(data);
				if (data.Named == NamedSlot.Back && backpackPanel.Visible)
					PopulateStorage(backpackGrid, inventory.GetStorageSlots(data.ItemId));
				if (data.Named == NamedSlot.Belt && beltPanel.Visible)
					PopulateStorage(beltGrid, inventory.GetStorageSlots(data.ItemId));
			}
			else
			{
				// A slot we don't know about: storage layout changed
				// (new clothing with slots). Cheap and always correct:
				Rebuild();
			}
		}

		private void OnSlotClicked(int slotId, bool isAltClick)
		{
			if (storageSlotsById.TryGetValue(slotId, out StorageSlotData? storageSlot))
			{
				if (UiServices.ActiveHandSlotId >= 0 && slotId != UiServices.ActiveHandSlotId)
				{
					UiServices.Interactions.RequestInventoryTransfer(
						storageSlot.IsOccupied ? slotId : UiServices.ActiveHandSlotId,
						storageSlot.IsOccupied ? UiServices.ActiveHandSlotId : slotId);
				}

				return;
			}

			SlotData clicked = inventory.GetSlot(slotId);

			// Clicking either hand selects it as the active hand.
			if (clicked.Named is NamedSlot.LeftHand or NamedSlot.RightHand
			    && slotId != UiServices.ActiveHandSlotId)
			{
				UiServices.ActiveHandSlotId = slotId;
				UpdateActiveHandHighlight();
				return;
			}

			// Expandable storage: clicking an occupied back/belt slot toggles its panel.
			if (clicked.Named == NamedSlot.Back && clicked.IsOccupied)
			{
				backpackPanel.Visible = !backpackPanel.Visible;
				if (backpackPanel.Visible)
					PopulateStorage(backpackGrid, inventory.GetStorageSlots(clicked.ItemId));
				return;
			}

			if (clicked.Named == NamedSlot.Belt && clicked.IsOccupied)
			{
				beltPanel.Visible = !beltPanel.Visible;
				if (beltPanel.Visible)
					PopulateStorage(beltGrid, inventory.GetStorageSlots(clicked.ItemId));
				return;
			}

			// Everything else is a transfer between the clicked slot and the
			// active hand; the server validates, we just request.
			if (UiServices.ActiveHandSlotId >= 0 && slotId != UiServices.ActiveHandSlotId)
			{
				UiServices.Interactions.RequestInventoryTransfer(
					clicked.IsOccupied ? slotId : UiServices.ActiveHandSlotId,
					clicked.IsOccupied ? UiServices.ActiveHandSlotId : slotId);
			}
		}

		private void UpdateActiveHandHighlight()
		{
			foreach ((int slotId, ItemSlotUI slot) in slotsById)
			{
				slot.SetActiveHand(slotId == UiServices.ActiveHandSlotId);
			}
		}

		/// <summary>
		/// Populate an expandable container panel (backpack/belt contents)
		/// from the provider's slot list for that container.
		/// </summary>
		public void PopulateStorage(GridContainer grid, IReadOnlyList<SlotData> contents)
		{
			foreach (Node child in grid.GetChildren())
			{
				child.QueueFree();
			}

			foreach (SlotData data in contents)
			{
				grid.AddChild(MakeSlot(data));
			}
		}

		public void PopulateStorage(GridContainer grid, IReadOnlyList<StorageSlotData> contents)
		{
			foreach (Node child in grid.GetChildren())
			{
				if (child is ItemSlotUI oldSlot)
				{
					storageSlotViewsById.Remove(oldSlot.SlotId);
					storageSlotsById.Remove(oldSlot.SlotId);
				}

				child.QueueFree();
			}

			foreach (StorageSlotData data in contents)
			{
				var slot = itemSlotScene.Instantiate<ItemSlotUI>();
				storageSlotsById[data.SlotId] = data;
				storageSlotViewsById[data.SlotId] = slot;
				slot.SlotClicked += OnSlotClicked;
				slot.Ready += () => slot.SetStorageSlot(data);
				grid.AddChild(slot);
			}
		}

		private void ApplyStorageSlotChange(int slotId, StorageSlotData data)
		{
			storageSlotsById[slotId] = data;
			if (storageSlotViewsById.TryGetValue(slotId, out ItemSlotUI? slot))
				slot.SetStorageSlot(data);
		}
	}
}
