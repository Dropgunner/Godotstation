namespace Godotstation.Common.Interaction;

/// <summary>
/// Encapsulates all of the info needed for handling an inventory apply interaction.
///
/// Triggered by clicking an inventory slot (in which case the active hand will be the from slot
/// even if it's empty) or dragging from one slot to another.
///
/// Ported from US13 Interactions.InventoryApply.
/// </summary>
public class InventoryApply : TargetedInteraction
{
	/// <summary>Sentinel returned when the interaction cannot be created at all.</summary>
	public static readonly InventoryApply Invalid = new(null, null, null, Intent.Help, false);

	/// <summary>
	/// Slot of the hand that is being used to perform the apply, or slot the item was dragged
	/// from if dragging from one slot to another.
	/// </summary>
	public IItemSlot? FromSlot { get; }

	/// <summary>Slot of object that the player is applying the used object to.</summary>
	public IItemSlot? TargetSlot { get; }

	/// <summary>True if the alt button is pressed by the user. Set clientside.</summary>
	public bool IsAltClick;

	/// <summary>True if the FromSlot is one of the performer's hands.</summary>
	public bool IsFromHandSlot =>
		FromSlot != null && ReferenceEquals(FromSlot.RootPlayer, Performer) && FromSlot.IsHandSlot;

	/// <summary>
	/// True if the target slot is one of the performer's hands. Note: the ownership check is on
	/// FromSlot, not TargetSlot - preserved verbatim from US13.
	/// </summary>
	public bool IsToHandSlot =>
		FromSlot != null && ReferenceEquals(FromSlot.RootPlayer, Performer) &&
		TargetSlot is { IsHandSlot: true };

	private InventoryApply(IPlayerEntity? performer, IItemSlot? targetSlot, IItemSlot? fromSlot,
		Intent intent, bool isAltClick)
		: base(performer, fromSlot?.Item, targetSlot?.Item, intent)
	{
		FromSlot = fromSlot;
		TargetSlot = targetSlot;
		IsAltClick = isAltClick;
	}

	/// <summary>
	/// Create an InventoryApply interaction performed by the local player using their active hand.
	/// </summary>
	/// <param name="targetSlot">slot of the object that the player is applying the active hand item to</param>
	/// <param name="fromSlot">hand slot if clicking on something in inventory, otherwise slot
	/// the item is being dragged from</param>
	public static InventoryApply ByLocalPlayer(IClientInteractionContext client,
		IItemSlot? targetSlot, IItemSlot? fromSlot)
	{
		if (client.LocalPlayer.PlayerType == PlayerTypes.Ghost)
		{
			return Invalid;
		}

		return new InventoryApply(client.LocalPlayer, targetSlot, fromSlot, client.CurrentIntent,
			client.IsAltActionKeyPressed);
	}

	/// <summary>
	/// For server only. Create an InventoryApply interaction initiated by the client.
	/// </summary>
	public static InventoryApply ByClient(IPlayerEntity clientPlayer, IItemSlot? targetSlot,
		IItemSlot? fromSlot, Intent intent, bool isAltClick)
	{
		return new InventoryApply(clientPlayer, targetSlot, fromSlot, intent, isAltClick);
	}
}
