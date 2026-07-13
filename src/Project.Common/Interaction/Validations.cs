using System.Numerics;

namespace Godotstation.Common.Interaction;

/// <summary>
/// Util class containing validation logic you might want to use when developing interactable
/// components. All methods are designed to work correctly based on whether they are invoked from
/// client or server side (as specified by the NetworkSide parameter).
///
/// Ported from US13.Core.Input_System.InteractionV2.Validations. Environment lookups that
/// upstream did through singletons (MatrixManager, PlayerManager) go through the injected
/// <see cref="IInteractionWorld"/> instead. Item-trait, stacking and slot-fit helpers arrive
/// with the inventory wave.
/// </summary>
public static class Validations
{
	/// <summary>US13: PlayerScript.INTERACTION_DISTANCE.</summary>
	public const float InteractionDistance = 1.5f;

	/// <summary>US13: PlayerScript.INTERACTION_DISTANCE_EXTENDED.</summary>
	public const float InteractionDistanceExtended = 1.75f;

	/// <summary>US13: Validations.TELEKINESIS_INTERACTION_DISTANCE.</summary>
	public const float TelekinesisInteractionDistance = 15f;

	/// <summary>
	/// Checks if a player is allowed to interact with things (based on this player's status, such
	/// as being conscious, and not cuffed).
	/// </summary>
	/// <param name="player">the player to check</param>
	/// <param name="world">environment queries (local-player identity)</param>
	/// <param name="side">side of the network the check is being performed on</param>
	/// <param name="allowSoftCrit">whether interaction should be allowed if in soft crit</param>
	/// <param name="allowCuffed">whether interaction should be allowed if cuffed</param>
	/// <param name="apt">the allowed PlayerTypes for this interaction, defaults to normal players</param>
	public static bool CanInteract(IPlayerEntity? player, IInteractionWorld world, NetworkSide side,
		bool allowSoftCrit = false, bool allowCuffed = false, PlayerTypes apt = PlayerTypes.Normal)
	{
		if (player == null) return false;

		// Only allow players to interact this way if contained in the allowed player types
		// (usually only normal players, not ghosts etc). Ai uses AiActivate, which has
		// additional validations and arrives with the AI wave.
		if (apt.HasFlag(player.PlayerType) == false) return false;

		// Can't interact cuffed.
		if (allowCuffed == false && player.IsCuffed) return false;

		if (player.AllowInput == false) return false;

		if (CanInteractByConsciousState(player, world, allowSoftCrit, side) == false) return false;

		return true;
	}

	private static bool CanInteractByConsciousState(IPlayerEntity player, IInteractionWorld world,
		bool allowSoftCrit, NetworkSide side)
	{
		if (side == NetworkSide.Client && ReferenceEquals(player, world.LocalPlayer) == false)
		{
			// We only know our own conscious state, so assume true if it's not our local player.
			// The server re-validates with authoritative state.
			return true;
		}

		return player.ConsciousState == ConsciousState.CONSCIOUS ||
		       player.ConsciousState == ConsciousState.BARELY_CONSCIOUS && allowSoftCrit;
	}

	/// <summary>
	/// Validates if the performer is in range and capable of interaction - all the typical
	/// requirements for the various interactions. Works properly even if the player is hidden
	/// in a closet. Can also optionally allow soft crit.
	/// </summary>
	/// <param name="player">player performing the interaction</param>
	/// <param name="target">target object. Might be null if it's empty space</param>
	/// <param name="world">environment queries (reach linecast, inventory, physics)</param>
	/// <param name="side">side of the network this is being checked on</param>
	/// <param name="allowSoftCrit">whether to allow interaction while in soft crit</param>
	/// <param name="reachRange">range to allow</param>
	/// <param name="targetPosition">world position being clicked; if specified, range is measured
	/// to it rather than to the target object's position</param>
	/// <param name="targetVector">vector from performer to the position they are trying to click;
	/// alternative to targetPosition</param>
	/// <param name="apt">the allowed PlayerTypes for this interaction, defaults to normal players</param>
	public static bool CanApply(
		IPlayerEntity? player,
		IWorldEntity? target,
		IInteractionWorld world,
		NetworkSide side,
		bool allowSoftCrit = false,
		ReachRange reachRange = ReachRange.Standard,
		Vector2? targetPosition = null,
		Vector2? targetVector = null,
		PlayerTypes apt = PlayerTypes.Normal)
	{
		if (player == null) return false;

		if (CanInteract(player, world, side, allowSoftCrit, apt: apt) == false)
		{
			return false;
		}

		// No matter what, if the player is in a closet, they can only reach the closet.
		if (player.IsHidden)
		{
			// The client does not know what it is hidden in (the container reference is not
			// synced), so it validates optimistically and defers to the server.
			if (side == NetworkSide.Client)
			{
				return true;
			}

			// Server checks if the player is trying to click the container they are in.
			return ReferenceEquals(player.ContainedIn, target);
		}

		var result = false;

		// If the target sits in item storage, reach is never measured: it is interactable only
		// when that storage is rooted at the performer (this branch replaces the reach checks
		// rather than falling through to them - preserved verbatim from US13, where it was
		// added so NetTabs can be used on items in an inventory).
		var containingSlot = target == null ? null : world.GetContainingSlot(target);
		if (containingSlot != null)
		{
			if (ReferenceEquals(containingSlot.RootPlayer, player) && containingSlot.RootPlayer != null)
			{
				result = true;
			}
		}
		else if (reachRange == ReachRange.Unlimited)
		{
			result = true;
		}
		else if (reachRange == ReachRange.Standard)
		{
			result = IsInReachInternal(player, target, world, side, targetPosition, targetVector);
		}
		else if (reachRange == ReachRange.ExtendedServer)
		{
			// We don't check range client-side for this case.
			if (side == NetworkSide.Client)
			{
				result = true;
			}
			else if (world.TryGetPhysics(target, out var physics) == false)
			{
				result = IsInReachInternal(player, target, world, side, targetPosition, targetVector);
			}
			else
			{
				result = ServerCanReachExtended(player, physics, world);
			}
		}
		else if (reachRange == ReachRange.Telekinesis)
		{
			// US13 dereferences the target unconditionally here and would throw on null;
			// the port fails closed instead.
			result = target != null &&
			         (player.AssumedWorldPositionServer - target.AssumedWorldPositionServer).Length() <=
			         TelekinesisInteractionDistance;
		}

		return result;
	}

	/// <summary>
	/// Figures out what method of reach checking to use based on the parameters.
	/// </summary>
	private static bool IsInReachInternal(IPlayerEntity player, IWorldEntity? target,
		IInteractionWorld world, NetworkSide side, Vector2? targetPosition, Vector2? targetVector)
	{
		var isServer = side == NetworkSide.Server;

		if (targetPosition == null && targetVector == null)
		{
			if (target == null) return false;
			return IsReachableByPositions(player.GetWorldPosition(side),
				target.GetWorldPosition(side), isServer, world, context: target);
		}

		// Use position/vector based range check.
		var playerWorldPos = player.GetWorldPosition(side);
		var toWorldPos = targetPosition ?? playerWorldPos + targetVector!.Value;
		return IsReachableByPositions(playerWorldPos, toWorldPos, isServer, world, context: target);
	}

	/// <summary>
	/// Checks if a delta vector is within interaction distance. The metric is Chebyshev
	/// (max of the axis distances), strictly less than the interaction distance - so diagonal
	/// neighbours are in reach but two tiles in a line are not.
	/// </summary>
	public static bool IsInReachDistanceByDelta(Vector2 targetVector,
		float interactDist = InteractionDistance)
	{
		return MathF.Max(MathF.Abs(targetVector.X), MathF.Abs(targetVector.Y)) < interactDist;
	}

	/// <summary>
	/// Checks if two positions are within interaction distance of each other.
	/// </summary>
	public static bool IsInReachDistanceByPositions(Vector2 fromWorldPos, Vector2 toWorldPos,
		float interactDist = InteractionDistance)
	{
		return IsInReachDistanceByDelta(fromWorldPos - toWorldPos, interactDist);
	}

	/// <summary>
	/// Checks if two positions are within interaction distance AND there is no blocking element
	/// between them.
	/// </summary>
	/// <param name="context">if not null, collisions caused by this entity are ignored</param>
	public static bool IsReachableByPositions(Vector2 fromWorldPos, Vector2 toWorldPos,
		bool isServer, IInteractionWorld world, float interactDist = InteractionDistance,
		IWorldEntity? context = null)
	{
		if (IsNotBlocked(fromWorldPos, toWorldPos, isServer, world, context))
		{
			return IsInReachDistanceByPositions(fromWorldPos, toWorldPos, interactDist);
		}

		return false;
	}

	private static bool IsNotBlocked(Vector2 worldPosA, Vector2 worldPosB, bool isServer,
		IInteractionWorld world, IWorldEntity? context)
	{
		if (worldPosA == worldPosB)
		{
			return true;
		}

		return world.IsPassableForReach(worldPosA, worldPosB, isServer, context);
	}

	/// <summary>
	/// ExtendedServer reach: the object is reachable at its official position, or - to be lenient
	/// with objects that were just thrown or pushed - at the position it occupied before its
	/// current newtonian movement, with a slightly larger allowance.
	/// </summary>
	private static bool ServerCanReachExtended(IPlayerEntity player, PhysicsSnapshot physics,
		IInteractionWorld world)
	{
		var playerPos = player.GetWorldPosition(NetworkSide.Server);
		return IsReachableByPositions(playerPos, physics.OfficialPosition, true, world) ||
		       IsReachableByPositions(playerPos, physics.OfficialPosition - physics.NewtonianMovement,
			       true, world, InteractionDistanceExtended);
	}
}
