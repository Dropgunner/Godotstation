using Godot;

namespace Godotstation.Game.UI.Inventory
{
	/// <summary>
	/// The ghost sprite that follows the cursor during an item drag.
	///
	/// Two modes:
	/// - Handed to Control.SetDragPreview() (the normal path from ItemSlotUI):
	///   Godot parents it to the viewport and moves it with the cursor; we do
	///   nothing but look translucent.
	/// - FollowMouse = true (for custom non-Control drags, e.g. dragging an
	///   item out of the UI into the world): it tracks the mouse itself.
	/// </summary>
	public partial class DragPreview : TextureRect
	{
		[Export] public bool FollowMouse;

		public static DragPreview For(Texture2D? texture, float size = 32f)
		{
			return new DragPreview
			{
				Texture = texture,
				CustomMinimumSize = new Vector2(size, size),
				StretchMode = StretchModeEnum.KeepAspectCentered,
				ExpandMode = ExpandModeEnum.IgnoreSize,
				Modulate = new Color(1f, 1f, 1f, 0.7f), // ghost translucency
				MouseFilter = MouseFilterEnum.Ignore,   // never intercept the drop
				ZIndex = 4096
			};
		}

		public override void _Process(double delta)
		{
			if (FollowMouse)
			{
				// Centered under the cursor, same as the Unity ghost.
				GlobalPosition = GetGlobalMousePosition() - Size * 0.5f;
			}
		}
	}
}
