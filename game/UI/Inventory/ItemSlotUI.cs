using Godot;
using Godotstation.Common.Interactions;

namespace Godotstation.Game.UI.Inventory
{
	/// <summary>
	/// One inventory slot (translation of UI_DynamicItemSlot): displays the
	/// slot's item, acts as drag source and drop target, and owns the
	/// right-click context menu. All actual inventory mutation goes through
	/// IInteractionService — this node only ever *requests*.
	///
	/// Drag-and-drop uses Godot's native _GetDragData/_CanDropData/_DropData
	/// pipeline rather than hand-rolled _GuiInput tracking: the engine then
	/// handles hover highlighting, cancel-on-escape, and preview lifetime for
	/// free, which is a straight win over the Unity DragHandler port.
	/// </summary>
	public partial class ItemSlotUI : PanelContainer
	{
		private const string DragKeySlotId = "fromSlotId";

		[Signal]
		public delegate void SlotClickedEventHandler(int slotId, bool isAltClick);

		[Export] public int SlotId = -1;

		private TextureRect icon = null!;
		private Label slotLabel = null!;
		private PopupMenu contextMenu = null!;

		private SlotData? current;

		private enum MenuAction
		{
			Drop = 0,
			Examine = 1
		}

		public override void _Ready()
		{
			icon = GetNode<TextureRect>("Icon");
			slotLabel = GetNode<Label>("SlotLabel");

			contextMenu = GetNode<PopupMenu>("ContextMenu");
			contextMenu.AddItem("Drop", (int)MenuAction.Drop);
			contextMenu.AddItem("Examine", (int)MenuAction.Examine);
			contextMenu.IdPressed += OnContextMenuAction;

			GuiInput += OnGuiInput;
			MouseFilter = MouseFilterEnum.Stop;
		}

		/// <summary>Render the slot's current content. Called by InventoryUI on OnSlotChanged.</summary>
		public void SetSlot(SlotData data)
		{
			current = data;
			SlotId = data.SlotId;

			slotLabel.Text = data.Named.ToString();
			slotLabel.Visible = data.IsOccupied == false;

			if (data.IsOccupied
			    && string.IsNullOrEmpty(data.ItemIconPath) == false
			    && ResourceLoader.Exists(data.ItemIconPath))
			{
				icon.Texture = ResourceLoader.Load<Texture2D>(data.ItemIconPath);
			}
			else
			{
				icon.Texture = null;
			}

			// Hover tooltip: item name + flavor text, or the slot's own name.
			TooltipText = data.IsOccupied
				? $"{data.ItemName}\n{data.ItemDescription}"
				: data.Named.ToString();
		}

		/// <summary>Active-hand highlight (only meaningful for hand slots).</summary>
		public void SetActiveHand(bool active)
		{
			SelfModulate = active ? new Color(1f, 0.85f, 0.3f) : Colors.White;
		}

		private void OnGuiInput(InputEvent @event)
		{
			if (@event is not InputEventMouseButton { Pressed: true } click)
			{
				return;
			}

			switch (click.ButtonIndex)
			{
				case MouseButton.Left:
					EmitSignal(SignalName.SlotClicked, SlotId, click.AltPressed);
					break;

				case MouseButton.Right when current is { IsOccupied: true }:
					contextMenu.Position = (Vector2I)GetScreenPosition() + (Vector2I)click.Position;
					contextMenu.Popup();
					break;
			}
		}

		private void OnContextMenuAction(long id)
		{
			switch ((MenuAction)id)
			{
				case MenuAction.Drop:
					UiServices.Interactions.RequestDrop(SlotId);
					break;

				case MenuAction.Examine when current is { IsOccupied: true }:
					// Examine is a hand-apply on the item with alt semantics.
					UiServices.Interactions.RequestHandApply(
						UiServices.LocalPlayerId, current.ItemId, BodyPart.Chest, isAltClick: true);
					break;
			}
		}

		// ---- Godot native drag & drop -----------------------------------

		public override Variant _GetDragData(Vector2 atPosition)
		{
			if (current is not { IsOccupied: true })
			{
				return default;
			}

			SetDragPreview(DragPreview.For(icon.Texture));

			return new Godot.Collections.Dictionary { { DragKeySlotId, SlotId } };
		}

		public override bool _CanDropData(Vector2 atPosition, Variant data)
		{
			return data.VariantType == Variant.Type.Dictionary
				&& data.AsGodotDictionary().ContainsKey(DragKeySlotId);
		}

		public override void _DropData(Vector2 atPosition, Variant data)
		{
			int fromSlotId = data.AsGodotDictionary()[DragKeySlotId].AsInt32();
			if (fromSlotId == SlotId)
			{
				return;
			}

			// The server decides whether the transfer is legal; the UI just asks.
			UiServices.Interactions.RequestInventoryTransfer(fromSlotId, SlotId);
		}
	}
}
