namespace Godotstation.Common.Interaction;

/// <summary>
/// Encapsulates all of the info needed for handling a hand apply interaction.
///
/// A hand apply interaction occurs when a player clicks something in the game world. The object
/// in their hand (or their empty hand) is applied to the target object.
///
/// Ported from US13 Interactions.HandApply.
/// </summary>
public class HandApply : BodyPartTargetedInteraction
{
	/// <summary>
	/// Sentinel returned when the interaction cannot be created at all (e.g. ghosting local
	/// player). Public in the port (private upstream) so callers and tests can detect it.
	/// </summary>
	public static readonly HandApply Invalid =
		new(null, null, null, BodyPartType.None, null, Intent.Help, false);

	/// <summary>Object in the performer's active hand. Null if the hand is empty.</summary>
	public IWorldEntity? HandObject => UsedObject;

	/// <summary>Active hand slot that is being used.</summary>
	public IItemSlot? HandSlot { get; protected set; }

	/// <summary>True if the alt button is pressed by the user. Set clientside.</summary>
	public bool IsAltClick { get; protected set; }

	/// <summary>
	/// True if the highlight system is probing this interaction; never sent to the server,
	/// clientside only.
	/// </summary>
	public bool IsHighlight;

	protected HandApply(IPlayerEntity? performer, IWorldEntity? handObject, IWorldEntity? targetObject,
		BodyPartType targetBodyPart, IItemSlot? handSlot, Intent intent, bool isAltClick,
		bool isHighlight = false)
		: base(performer, handObject, targetObject, targetBodyPart, intent)
	{
		HandSlot = handSlot;
		IsAltClick = isAltClick;
		IsHighlight = isHighlight;
	}

	/// <summary>
	/// Creates a HandApply interaction performed by the local player targeting the specified object.
	/// </summary>
	/// <param name="targetObject">object targeted by the interaction</param>
	public static HandApply ByLocalPlayer(IClientInteractionContext client, IWorldEntity? targetObject)
	{
		if (client.IsGhosting)
		{
			// Hand apply never works while the local player is ghosting.
			return Invalid;
		}

		var handSlot = client.ActiveHandSlot;
		return new HandApply(client.LocalPlayer,
			handSlot?.Item,
			targetObject,
			client.TargetedBodyPart,
			handSlot,
			client.CurrentIntent,
			client.IsAltActionKeyPressed);
	}

	/// <summary>
	/// For server only. Create a hand apply interaction initiated by the client. The hand object
	/// and slot are passed in because the message-processing layer has already looked them up.
	/// </summary>
	public static HandApply ByClient(IPlayerEntity clientPlayer, IWorldEntity? handObject,
		IWorldEntity? targetObject, BodyPartType targetBodyPart, IItemSlot? handSlot, Intent intent,
		bool isAltClick)
	{
		return new HandApply(clientPlayer, handObject, targetObject, targetBodyPart, handSlot,
			intent, isAltClick);
	}
}
