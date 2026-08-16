using System.Numerics;

namespace Godotstation.Common.Interactions
{
	/// <summary>
	/// Port of the subset of US13 Validations that DefaultWillInteract exercises:
	/// CanInteract, CanApply and the reach-check family. One function per original
	/// method, same guard order, same constants, parameterized by NetworkSide
	/// exactly as upstream (FC-8). Inventory-fit / stripping / stacking helpers
	/// from the original 826-line class are Wave-2 material and intentionally absent.
	///
	/// The only structural change: the original reached into the MatrixManager
	/// singleton for line-blocking; here the caller passes an IReachabilityMap.
	/// </summary>
	public static class Validations
	{
		/// <summary>Validations.TELEKINESIS_INTERACTION_DISTANCE. Euclidean, and the comparison is "&gt; 15 fails", so exactly 15 passes.</summary>
		public const float TelekinesisInteractionDistance = 15f;

		/// <summary>
		/// PlayerScript.INTERACTION_DISTANCE. The reach predicate is STRICT
		/// Chebyshev: max(|dx|,|dy|) &lt; 1.5. Tile-center to tile-center this is
		/// "one tile, orthogonal or diagonal" (FC-1) — 1.4-tile diagonals pass,
		/// 1.5 exactly does not.
		/// </summary>
		public const float InteractionDistance = 1.5f;

		/// <summary>PlayerScript.INTERACTION_DISTANCE_EXTENDED — used by the ExtendedServer flight-catch check.</summary>
		public const float InteractionDistanceExtended = 1.75f;

		/// <summary>
		/// Checks if a player is allowed to interact with things at all, based on
		/// actor state: player-type mask, cuffs, input lock, conscious state.
		/// This is the ENTIRE validation for HandActivate and InventoryApply —
		/// deliberately no reach or position check (FC-4).
		/// </summary>
		public static bool CanInteract(PlayerEntity? player, NetworkSide side, bool allowSoftCrit = false,
			bool allowCuffed = false, PlayerTypes apt = PlayerTypes.Normal)
		{
			if (player == null) return false;

			// Only player types contained in apt may interact this way (ghosts etc. excluded by default).
			if (apt.HasFlag(player.PlayerType) == false) return false;

			// Can't interact cuffed.
			if (allowCuffed == false && player.IsCuffed) return false;

			if (player.AllowInput == false) return false;

			if (CanInteractByConsciousState(player, allowSoftCrit, side) == false) return false;

			return true;
		}

		private static bool CanInteractByConsciousState(PlayerEntity player, bool allowSoftCrit, NetworkSide side)
		{
			if (side == NetworkSide.Client)
			{
				// The client only knows its own conscious state; for anyone else,
				// assume conscious and let the server be the judge (FC-8a).
				if (player.IsLocalPlayer == false) return true;
			}

			return player.ConsciousState == ConsciousState.Conscious ||
			       (player.ConsciousState == ConsciousState.BarelyConscious && allowSoftCrit);
		}

		/// <summary>
		/// The typical full validation: performer capable of interacting AND in
		/// range of the target under the given ReachRange. Guard order, branch
		/// structure and position sources match the original exactly.
		/// </summary>
		/// <param name="targetPosition">
		/// If given, range is checked against this world position instead of the
		/// target object's position (PositionalHandApply / TileApply / ConnectionApply).
		/// </param>
		/// <param name="targetVector">
		/// If given (and targetPosition is not), range is checked against
		/// performer-position + this vector.
		/// </param>
		public static bool CanApply(
			PlayerEntity? player,
			WorldEntity? target,
			NetworkSide side,
			IReachabilityMap map,
			bool allowSoftCrit = false,
			ReachRange reachRange = ReachRange.Standard,
			Vector2? targetPosition = null,
			Vector2? targetVector = null,
			PlayerTypes apt = PlayerTypes.Normal)
		{
			if (player == null) return false;

			if (CanInteract(player, side, allowSoftCrit, apt: apt) == false)
			{
				return false;
			}

			// No matter what, a player hidden in a closet can only reach the closet.
			if (player.IsHidden)
			{
				if (side == NetworkSide.Client)
				{
					// The client doesn't know what it's hidden inside; it validates
					// permissively and defers the decision to the server (FC-8a).
					return true;
				}

				// Server: the only legal target is the container itself. Faithful
				// quirk preserved: containerless-hidden + null target compares
				// null == null and passes.
				return ReferenceEquals(player.ContainedIn, target);
			}

			var result = false;

			if (target != null && target.IsInItemSlot)
			{
				// Target is inside an inventory: reachable only if that inventory
				// is the performer's own. Faithful chain semantics: an item in
				// someone ELSE'S inventory falls through to no other branch —
				// it is unreachable at any range, even Unlimited.
				if (ReferenceEquals(target.InventoryRootPlayer, player))
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
				result = IsInReachInternal(player, target, side, targetPosition, targetVector, map);
			}
			else if (reachRange == ReachRange.ExtendedServer)
			{
				if (side == NetworkSide.Client)
				{
					// Range is not checked client-side for this mode (FC-8a).
					result = true;
				}
				else if (target is not { HasPhysics: true })
				{
					result = IsInReachInternal(player, target, side, targetPosition, targetVector, map);
				}
				else
				{
					result = ServerCanReachExtended(player, target, map);
				}
			}
			else if (reachRange == ReachRange.Telekinesis)
			{
				// Euclidean on assumed-server positions, BOTH sides; > 15 fails.
				// No blocking linecast: TK reaches through walls, faithfully.
				// (Original null target NREs here; we refuse instead — clicking
				// nothing with TK is contract-neutral.)
				result = target != null &&
				         (player.AssumedWorldPosServer - target.AssumedWorldPosServer).Length()
				         <= TelekinesisInteractionDistance;
			}

			return result;
		}

		/// <summary>
		/// Figures out which reach check applies. No explicit position given:
		/// register-tile check (works on moving matrices), falling back to
		/// transform positions only for register-tile-less targets. Explicit
		/// position/vector given: positional check from the performer's
		/// registered position.
		/// </summary>
		private static bool IsInReachInternal(PlayerEntity player, WorldEntity? target, NetworkSide side,
			Vector2? targetPosition, Vector2? targetVector, IReachabilityMap map)
		{
			if (targetPosition == null && targetVector == null)
			{
				if (target is { HasRegisterTile: true })
				{
					return IsReachableByRegisterTiles(player, target, side == NetworkSide.Server, map, context: target);
				}

				// Transform-position fallback ("should happen almost never").
				if (target == null) return false;
				return IsReachableByPositions(player.TransformPosition, target.TransformPosition,
					side == NetworkSide.Server, map, context: target);
			}

			var playerWorldPos = player.RegisteredPosition(side);

			if (targetPosition != null)
			{
				return IsReachableByPositions(playerWorldPos, targetPosition.Value,
					side == NetworkSide.Server, map, context: target);
			}

			return IsReachableByPositions(playerWorldPos, playerWorldPos + targetVector!.Value,
				side == NetworkSide.Server, map, context: target);
		}

		/// <summary>Strict Chebyshev range predicate: max(|dx|,|dy|) &lt; interactDist (FC-1).</summary>
		public static bool IsInReachDistanceByDelta(Vector2 targetVector, float interactDist = InteractionDistance)
		{
			return System.MathF.Max(System.MathF.Abs(targetVector.X), System.MathF.Abs(targetVector.Y)) < interactDist;
		}

		public static bool IsInReachDistanceByPositions(Vector2 fromWorldPos, Vector2 toWorldPos, float interactDist = InteractionDistance)
		{
			return IsInReachDistanceByDelta(fromWorldPos - toWorldPos, interactDist);
		}

		/// <summary>
		/// In range AND no blocking element between the two positions. The click
		/// line ignores Walls/Windows/Grills at the destination only — clicking
		/// a window is legal, clicking through one is not (map's responsibility).
		/// </summary>
		public static bool IsReachableByPositions(Vector2 fromWorldPos, Vector2 toWorldPos, bool isServer,
			IReachabilityMap map, float interactDist = InteractionDistance, WorldEntity? context = null)
		{
			if (IsNotBlocked(fromWorldPos, toWorldPos, isServer, map, context))
			{
				return IsInReachDistanceByPositions(fromWorldPos, toWorldPos, interactDist);
			}

			return false;
		}

		private static bool IsNotBlocked(Vector2 worldPosA, Vector2 worldPosB, bool isServer, IReachabilityMap map, WorldEntity? context)
		{
			if (worldPosA == worldPosB)
			{
				return true;
			}

			return map.IsPassableForClick(worldPosA, worldPosB, isServer, context);
		}

		/// <summary>
		/// Register-tile-based reach: the side selects WHICH registered position
		/// is read — server reads server positions, client reads client positions —
		/// and that is the only difference (FC-8). Registered tile, not visual
		/// position, is authoritative for a target mid-animation (FC-1).
		/// </summary>
		public static bool IsReachableByRegisterTiles(WorldEntity from, WorldEntity to, bool isServer,
			IReachabilityMap map, float interactDist = InteractionDistance, WorldEntity? context = null)
		{
			if (isServer)
			{
				return IsReachableByPositions(from.WorldPositionServer, to.WorldPositionServer, true, map, interactDist, context);
			}

			return IsReachableByPositions(from.WorldPositionClient, to.WorldPositionClient, false, map, interactDist, context);
		}

		/// <summary>
		/// ExtendedServer catch: the object is reachable at its official position,
		/// or — for an object in flight — at the position it just came from,
		/// with the extended 1.75 distance. Server only.
		/// </summary>
		private static bool ServerCanReachExtended(PlayerEntity player, WorldEntity target, IReachabilityMap map)
		{
			return IsPositionReachable(player, target.OfficialPosition, true, map) ||
			       IsPositionReachable(player, target.OfficialPosition - target.NewtonianMovement, true, map,
				       InteractionDistanceExtended, context: target);
		}

		/// <summary>PlayerScript.IsPositionReachable — measured from the player's transform position, faithfully.</summary>
		public static bool IsPositionReachable(PlayerEntity player, Vector2 otherPosition, bool isServer,
			IReachabilityMap map, float interactDist = InteractionDistance, WorldEntity? context = null)
		{
			return IsReachableByPositions(player.TransformPosition, otherPosition, isServer, map, interactDist, context: context);
		}
	}
}
