namespace Godotstation.Common.Interaction;

/// <summary>
/// Hand Activate interaction. Triggers by pressing the Activate key or clicking the item while
/// it's in the active hand.
///
/// Ported from US13 Interactions.HandActivate.
/// </summary>
public class HandActivate : Interaction
{
	/// <summary>Sentinel returned when the interaction cannot be created at all.</summary>
	public static readonly HandActivate Invalid = new(null, null, null, Intent.Help, false);

	/// <summary>Hand slot being activated.</summary>
	public IItemSlot? HandSlot { get; }

	public bool IsAltClick { get; }

	private HandActivate(IPlayerEntity? performer, IWorldEntity? activatedObject,
		IItemSlot? handSlot, Intent intent, bool isAltClick)
		: base(performer, activatedObject, intent)
	{
		HandSlot = handSlot;
		IsAltClick = isAltClick;
	}

	/// <summary>
	/// Create an Activate interaction where the local player is activating their active hand item.
	/// </summary>
	public static HandActivate ByLocalPlayer(IClientInteractionContext client)
	{
		if (client.LocalPlayer.PlayerType != PlayerTypes.Normal)
		{
			// Activate never works for non-normal players (US13: LocalPlayerScript.IsNormal check).
			return Invalid;
		}

		var handSlot = client.ActiveHandSlot;
		return new HandActivate(client.LocalPlayer,
			handSlot?.Item,
			handSlot,
			client.CurrentIntent,
			client.IsAltActionKeyPressed);
	}

	/// <summary>
	/// For server only. Create an activate interaction initiated by the client.
	/// </summary>
	public static HandActivate ByClient(IPlayerEntity clientPlayer, IWorldEntity? activatedObject,
		IItemSlot? handSlot, Intent intent, bool isAltClick)
	{
		return new HandActivate(clientPlayer, activatedObject, handSlot, intent, isAltClick);
	}
}
