namespace Godotstation.Common.Interaction;

/// <summary>
/// The default "will this interaction be allowed" logic that interactable components use unless
/// they override it. Runs identically on the client (prediction) and the server (authority).
///
/// Ported from US13.Core.Input_System.InteractionV2.DefaultWillInteract.
/// </summary>
public static class DefaultWillInteract
{
	/// <summary>
	/// Invoked with a message when an unhandled interaction type reaches <see cref="Default{T}"/>.
	/// Replaces the US13 Loggy error; wire this to the engine's logger.
	/// </summary>
	public static Action<string>? OnUnrecognizedInteraction;

	/// <summary>
	/// Chooses and invokes the default WillInteract logic corresponding to the supplied type.
	/// Dispatch is on the declared type parameter, not the runtime type - IF2 components declare
	/// which interaction type they handle, and a PositionalHandApply routed to a component that
	/// declared HandApply must be validated as a plain HandApply. Preserved verbatim from US13.
	/// </summary>
	/// <param name="interaction">interaction to check</param>
	/// <param name="side">side of the network this is being checked on</param>
	/// <param name="world">environment queries (reach linecast, inventory, physics)</param>
	/// <param name="apt">the allowed PlayerTypes of this interaction, defaults to Normal</param>
	/// <param name="allowTelekinesis">whether the telekinesis reach extension may apply</param>
	public static bool Default<T>(T interaction, NetworkSide side, IInteractionWorld world,
		PlayerTypes apt = PlayerTypes.Normal, bool allowTelekinesis = true) where T : Interaction
	{
		if (typeof(T) == typeof(PositionalHandApply))
		{
			var positionalHandApply = (PositionalHandApply)(Interaction)interaction;

			var reachRange = ReachRange.Standard;
			if (allowTelekinesis && positionalHandApply.Performer?.HasTelekinesis == true &&
			    positionalHandApply.HandObject == null)
			{
				reachRange = ReachRange.Telekinesis;
			}

			return Validations.CanApply(positionalHandApply.Performer,
				positionalHandApply.TargetObject, world, side,
				targetPosition: positionalHandApply.TargetPosition, apt: apt,
				reachRange: reachRange);
		}

		if (typeof(T) == typeof(HandApply))
		{
			var handApply = (HandApply)(Interaction)interaction;

			var reachRange = ReachRange.Standard;
			if (allowTelekinesis && handApply.Performer?.HasTelekinesis == true &&
			    handApply.HandObject == null)
			{
				reachRange = ReachRange.Telekinesis;
			}

			return Validations.CanApply(handApply.Performer, handApply.TargetObject, world, side,
				apt: apt, reachRange: reachRange);
		}

		if (typeof(T) == typeof(AimApply))
		{
			return AimApplyDefault((AimApply)(Interaction)interaction, side, world, apt);
		}

		if (typeof(T) == typeof(MouseDrop))
		{
			var mouseDrop = (MouseDrop)(Interaction)interaction;

			// US13 dereferences DroppedObject/TargetObject unconditionally in the second check
			// and would throw on null; the port fails closed instead.
			return Validations.CanApply(mouseDrop.Performer, mouseDrop.DroppedObject, world, side,
				       apt: apt) &&
			       mouseDrop.DroppedObject != null && mouseDrop.TargetObject != null &&
			       Validations.IsReachableByPositions(mouseDrop.DroppedObject.AssumedWorldPositionServer,
				       mouseDrop.TargetObject.AssumedWorldPositionServer,
				       side == NetworkSide.Server, world);
		}

		if (typeof(T) == typeof(HandActivate))
		{
			return Validations.CanInteract(interaction.Performer, world, side, apt: apt);
		}

		if (typeof(T) == typeof(InventoryApply))
		{
			return Validations.CanInteract(interaction.Performer, world, side, apt: apt);
		}

		// TileApply, ConnectionApply, ContextMenuApply and AiActivate arrive with the tile,
		// wiring and AI waves. Unknown types fail closed, exactly as in US13.
		OnUnrecognizedInteraction?.Invoke($"Unable to recognize interaction type {typeof(T).Name}.");
		return false;
	}

	/// <summary>
	/// Default WillInteract logic for AimApply: the performer must be able to interact and must
	/// not be hidden in a container. On the client only the local player's hidden state is known.
	/// </summary>
	private static bool AimApplyDefault(AimApply interaction, NetworkSide side,
		IInteractionWorld world, PlayerTypes allowedPlayerTypes)
	{
		if (Validations.CanInteract(interaction.Performer, world, side,
			    apt: allowedPlayerTypes) == false)
		{
			return false;
		}

		bool? isNotHidden = side == NetworkSide.Client
			? !world.LocalPlayer?.IsHidden
			: !interaction.Performer?.IsHidden;

		return isNotHidden.GetValueOrDefault(true);
	}
}
