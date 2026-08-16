namespace Godotstation.Common.Interactions
{
	/// <summary>
	/// Port of US13 DefaultWillInteract — the default WillInteract policy every
	/// interactable component gets unless it overrides one. Chooses the
	/// validation recipe by interaction type.
	///
	/// Divergences from upstream, all sanctioned by the Fidelity Contract's
	/// exclusion list:
	///  * The typeof(T) == typeof(X) chain is replaced by a pattern switch.
	///    ORDER MATTERS: PositionalHandApply derives from HandApply and must be
	///    matched first, reproducing the original's exact-type dispatch.
	///  * The MatrixManager singleton is replaced by the injected map.
	///  * AiActivate is not ported in Wave 1 (drags in the whole AI camera
	///    subsystem); it lands with the AI systems wave. Unrecognized
	///    interactions refuse, as upstream (which logs an error and returns false).
	///  * Null dropped/target objects on MouseDrop refuse instead of NREing.
	/// </summary>
	public static class DefaultWillInteract
	{
		/// <param name="apt">Allowed player types for this interaction.</param>
		/// <param name="allowTelekinesis">
		/// When false, a telekinetic performer gets standard reach regardless of
		/// hand content (FC-2) — components pass false for tool-use interactions.
		/// </param>
		public static bool Default(Interaction interaction, NetworkSide side, IReachabilityMap map,
			PlayerTypes apt = PlayerTypes.Normal, bool allowTelekinesis = true)
		{
			switch (interaction)
			{
				// Must precede HandApply: PositionalHandApply is a HandApply.
				case PositionalHandApply positionalHandApply:
				{
					var reachRange = ReachRange.Standard;
					if (allowTelekinesis && positionalHandApply.Performer is { HasTelekinesis: true } &&
					    positionalHandApply.HandObject == null)
					{
						// Telekinesis extends reach ONLY for an empty hand (FC-2).
						reachRange = ReachRange.Telekinesis;
					}

					return Validations.CanApply(positionalHandApply.Performer, positionalHandApply.TargetObject, side, map,
						targetPosition: positionalHandApply.TargetPosition, apt: apt, reachRange: reachRange);
				}

				case HandApply handApply:
				{
					var reachRange = ReachRange.Standard;
					if (allowTelekinesis && handApply.Performer is { HasTelekinesis: true } &&
					    handApply.HandObject == null)
					{
						reachRange = ReachRange.Telekinesis;
					}

					return Validations.CanApply(handApply.Performer, handApply.TargetObject, side, map,
						apt: apt, reachRange: reachRange);
				}

				case AimApply aimApply:
					return AimApplyDefault(aimApply, side, apt);

				case MouseDrop mouseDrop:
					// Performer must be able to touch the dragged object under
					// standard rules AND a clear line must exist between the two
					// objects — measured on ASSUMED SERVER positions on both
					// sides (FC-3). Individual reachability of A and B is not enough.
					return Validations.CanApply(mouseDrop.Performer, mouseDrop.DroppedObject, side, map, apt: apt) &&
					       mouseDrop.DroppedObject != null && mouseDrop.TargetObject != null &&
					       Validations.IsReachableByPositions(mouseDrop.DroppedObject.AssumedWorldPosServer,
						       mouseDrop.TargetObject.AssumedWorldPosServer, side == NetworkSide.Server, map);

				case HandActivate:
				case InventoryApply:
					// Actor-state only. What's in your hand always works (FC-4).
					return Validations.CanInteract(interaction.Performer, side, apt: apt);

				case TileApply tileApply:
					return Validations.CanApply(tileApply.Performer, tileApply.TargetObject, side, map,
						targetPosition: tileApply.TargetPosition, apt: apt);

				case ConnectionApply connectionApply:
					return Validations.CanApply(connectionApply.Performer, connectionApply.TargetObject, side, map,
						targetPosition: connectionApply.TargetPosition, apt: apt);

				case ContextMenuApply contextMenuApply:
					return Validations.CanApply(contextMenuApply.Performer, contextMenuApply.TargetObject, side, map, apt: apt);

				default:
					// Upstream logs "Unable to recognize interaction type." and refuses.
					return false;
			}
		}

		private static bool AimApplyDefault(AimApply interaction, NetworkSide side, PlayerTypes allowedPlayerTypes)
		{
			if (Validations.CanInteract(interaction.Performer, side, apt: allowedPlayerTypes) == false)
			{
				return false;
			}

			// You can't fire from inside a closet. The original reads the local
			// player's hidden flag on the client and the performer's on the
			// server; client-side evaluation only ever runs for the local
			// performer, so both collapse to the performer here. Null performer
			// defaults to not-hidden, as upstream.
			return interaction.Performer is not { IsHidden: true };
		}
	}
}
