using System.Numerics;

namespace Godotstation.Common.Interaction;

/// <summary>
/// Encapsulates all of the info needed for handling a mouse drag and drop interaction. Invoked
/// for dragging and dropping an object from the world to another object in the world, or an
/// object from inventory to an object in the world. Dragging and dropping between 2 inventory
/// slots is handled by InventoryApply.
///
/// Ported from US13 Interactions.MouseDrop.
/// </summary>
public class MouseDrop : TargetedInteraction
{
	/// <summary>Sentinel returned when the interaction cannot be created at all.</summary>
	public static readonly MouseDrop Invalid = new(null, null, null, null, Intent.Help);

	/// <summary>
	/// If dragging and dropping from inventory, slot it is being dragged from. Null otherwise.
	/// </summary>
	public IItemSlot? FromSlot { get; }

	public bool IsFromInventory => FromSlot != null;

	/// <summary>Object being dropped (same as UsedObject).</summary>
	public IWorldEntity? DroppedObject => UsedObject;

	/// <summary>The final location of the shadow object when the interaction was fired.</summary>
	public Vector2 ShadowWorldLocation;

	private MouseDrop(IPlayerEntity? performer, IWorldEntity? droppedObject,
		IWorldEntity? targetObject, IItemSlot? fromSlot, Intent intent)
		: base(performer, droppedObject, targetObject, intent)
	{
		FromSlot = fromSlot;
	}

	/// <summary>
	/// Create a mouse drop interaction performed by the local player.
	/// </summary>
	public static MouseDrop ByLocalPlayer(IClientInteractionContext client, IInteractionWorld world,
		IWorldEntity droppedObject, IWorldEntity? targetObject)
	{
		return new MouseDrop(client.LocalPlayer, droppedObject, targetObject,
			world.GetContainingSlot(droppedObject), client.CurrentIntent);
	}

	/// <summary>
	/// For server only. Create a mouse drop interaction initiated by the client.
	/// </summary>
	public static MouseDrop ByClient(IPlayerEntity clientPlayer, IInteractionWorld world,
		IWorldEntity droppedObject, IWorldEntity? targetObject, Intent intent)
	{
		return new MouseDrop(clientPlayer, droppedObject, targetObject,
			world.GetContainingSlot(droppedObject), intent);
	}
}
