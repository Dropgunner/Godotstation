using System.Numerics;

namespace Godotstation.Common.Interaction;

/// <summary>
/// HandApply but with a vector2 which indicates the exact position the player is trying to click.
/// Useful for objects which have different logic based on where you click (such as interactable
/// tiles). Also fires when clicking on empty / open space.
///
/// Ported from US13 Interactions.PositionalHandApply. Upstream stores the position matrix-local
/// and converts through the performer's matrix; the matrix system lives engine-side, so this
/// port speaks world space at the boundary - the engine adapter converts before constructing.
/// </summary>
public class PositionalHandApply : HandApply
{
	/// <summary>Sentinel returned when the interaction cannot be created at all.</summary>
	public static new readonly PositionalHandApply Invalid =
		new(null, null, null, Vector2.Zero, null, Intent.Help, BodyPartType.None, false);

	/// <summary>World position the performer is pointing at.</summary>
	public Vector2 TargetPosition { get; }

	/// <summary>Target world position. Alias kept for API parity with US13.</summary>
	public Vector2 WorldPositionTarget => TargetPosition;

	/// <summary>Vector pointing from the performer's position to the target position.</summary>
	public Vector2 GetTargetVector(NetworkSide side) =>
		Performer == null ? TargetPosition : TargetPosition - Performer.GetWorldPosition(side);

	private PositionalHandApply(IPlayerEntity? performer, IWorldEntity? handObject,
		IWorldEntity? targetObject, Vector2 targetPosition, IItemSlot? handSlot, Intent intent,
		BodyPartType targetBodyPart, bool isAltClick)
		: base(performer, handObject, targetObject, targetBodyPart, handSlot, intent, isAltClick)
	{
		TargetPosition = targetPosition;
	}

	/// <summary>
	/// Creates a PositionalHandApply interaction performed by the local player targeting the
	/// specified object.
	/// </summary>
	/// <param name="targetObject">object targeted by the interaction, null to target empty space</param>
	/// <param name="targetWorldPosition">world position the performer is pointing at (upstream
	/// defaults this to the mouse; resolving the mouse is the engine adapter's job)</param>
	public static PositionalHandApply ByLocalPlayer(IClientInteractionContext client,
		IWorldEntity? targetObject, Vector2 targetWorldPosition)
	{
		if (client.HasItemStorage == false) return Invalid;

		var handSlot = client.ActiveHandSlot;
		return new PositionalHandApply(client.LocalPlayer,
			handSlot?.Item,
			targetObject,
			targetWorldPosition,
			handSlot,
			client.CurrentIntent,
			client.TargetedBodyPart,
			client.IsAltActionKeyPressed);
	}

	/// <summary>
	/// For server only. Create a positional hand apply interaction initiated by the client.
	/// </summary>
	public static PositionalHandApply ByClient(IPlayerEntity clientPlayer, IWorldEntity? handObject,
		IWorldEntity? targetObject, Vector2 targetWorldPosition, IItemSlot? handSlot, Intent intent,
		BodyPartType targetBodyPart, bool isAltClick)
	{
		return new PositionalHandApply(clientPlayer, handObject, targetObject, targetWorldPosition,
			handSlot, intent, targetBodyPart, isAltClick);
	}
}
