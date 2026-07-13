namespace Godotstation.Common.Interaction;

/// <summary>
/// Base class containing the info on an attempt to perform a particular interaction - each
/// interaction has a performer (player performing it) and a used object (object they are using,
/// dropping, or combining). Abstract, only used internally for IF2 - should not be used directly
/// in interactable components.
///
/// Ported from US13 Interactions.Internal.Interaction. The Performer GameObject and its derived
/// PerformerPlayerScript collapse into one <see cref="IPlayerEntity"/>; Mind/account identity is
/// deferred until the mind system is ported.
/// </summary>
public abstract class Interaction
{
	/// <summary>The player performing the interaction. Null only on Invalid sentinels.</summary>
	public IPlayerEntity? Performer { get; protected set; }

	/// <summary>
	/// Object that is being used by the player to perform the interaction.
	/// For hand apply - object in hand, null if empty hand.
	/// For combine - object that was dragged to another slot.
	/// For activate - the object being activated.
	/// For mouse drop - the object being dragged and dropped.
	/// </summary>
	public IWorldEntity? UsedObject { get; protected set; }

	/// <summary>Intent of the player for this interaction.</summary>
	public Intent Intent { get; protected set; }

	protected Interaction(IPlayerEntity? performer, IWorldEntity? usedObject, Intent intent)
	{
		Performer = performer;
		UsedObject = usedObject;
		Intent = intent;
	}
}
