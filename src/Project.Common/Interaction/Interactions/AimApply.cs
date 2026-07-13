using System.Numerics;

namespace Godotstation.Common.Interaction;

/// <summary>
/// Encapsulates all of the info needed for handling aim apply interactions.
///
/// An AimApply is an interaction where the player is aiming at the current mouse position. The
/// interaction fires continuously while the mouse button is being held down, and fires once on
/// the initial click and also on the release. This is used for things like guns.
///
/// Ported from US13 Interactions.AimApply. Upstream's ByLocalPlayer resolves the mouse position,
/// self-aim raycast and target under the cursor; those are engine concerns, so the port's
/// ByLocalPlayer takes their results as parameters.
/// </summary>
public class AimApply : BodyPartTargetedInteraction
{
	/// <summary>Hand slot being used.</summary>
	public IItemSlot? HandSlot { get; }

	/// <summary>State of the mouse button when this interaction was triggered.</summary>
	public MouseButtonState MouseButtonState { get; }

	/// <summary>World position the player is aiming at.</summary>
	public Vector2 TargetPosition { get; }

	/// <summary>
	/// Performer position when the shot was made. Warning: includes client prediction, unsafe
	/// to trust for validation.
	/// </summary>
	public Vector2 OriginatorPosition { get; }

	/// <summary>Whether player is aiming at themselves.</summary>
	public bool IsAimingAtSelf => OriginatorPosition == TargetPosition;

	private AimApply(IPlayerEntity? performer, IWorldEntity? handObject, IItemSlot? handSlot,
		MouseButtonState buttonState, Vector2 targetPosition, BodyPartType bodyPartType,
		Intent intent, Vector2 originatorPosition, IWorldEntity? targetingObject)
		: base(performer, handObject, targetingObject, bodyPartType, intent)
	{
		HandSlot = handSlot;
		MouseButtonState = buttonState;
		TargetPosition = targetPosition;
		OriginatorPosition = originatorPosition;
	}

	/// <summary>
	/// Create an aim apply performed by the local player.
	/// </summary>
	/// <param name="targetWorldPosition">world position under the cursor (performer position if
	/// the engine's self-aim check hit)</param>
	/// <param name="targetingObject">physics-backed object under the cursor, or null</param>
	public static AimApply ByLocalPlayer(IClientInteractionContext client,
		MouseButtonState buttonState, Vector2 targetWorldPosition, IWorldEntity? targetingObject)
	{
		var handSlot = client.ActiveHandSlot;
		return new AimApply(client.LocalPlayer,
			handSlot?.Item,
			handSlot,
			buttonState,
			targetWorldPosition,
			client.TargetedBodyPart,
			client.CurrentIntent,
			client.LocalPlayer.GetWorldPosition(NetworkSide.Client),
			targetingObject);
	}

	/// <summary>
	/// For server only. Create an aim apply interaction initiated by the client.
	/// </summary>
	public static AimApply ByClient(IPlayerEntity clientPlayer, Vector2 targetWorldPosition,
		IWorldEntity? handObject, IItemSlot? handSlot, MouseButtonState mouseButtonState,
		BodyPartType targetBodyPart, Intent intent, Vector2 originatorPosition,
		IWorldEntity? targetObject)
	{
		return new AimApply(clientPlayer, handObject, handSlot, mouseButtonState,
			targetWorldPosition, targetBodyPart, intent, originatorPosition, targetObject);
	}
}

/// <summary>
/// Represents the particular state of the mouse button during an AimApply interaction.
/// Values ported verbatim from US13.
/// </summary>
public enum MouseButtonState
{
	/// <summary>Button pressed down.</summary>
	PRESS = 0,

	/// <summary>Button being held down.</summary>
	HOLD = 1
}
