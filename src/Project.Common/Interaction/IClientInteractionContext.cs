namespace Godotstation.Common.Interaction;

/// <summary>
/// Everything the client-side interaction factories (the ByLocalPlayer methods) need to know
/// about the local player and HUD. Replaces the US13 singletons PlayerManager, UIManager and
/// KeyboardInputManager so interaction construction is testable without an engine.
/// </summary>
public interface IClientInteractionContext
{
	/// <summary>US13: PlayerManager.LocalPlayerScript / LocalPlayerObject.</summary>
	IPlayerEntity LocalPlayer { get; }

	/// <summary>
	/// True while the local mind is ghosting (observing outside its body).
	/// US13: PlayerManager.LocalMindScript.IsGhosting - distinct from
	/// <see cref="IPlayerEntity.PlayerType"/> being <see cref="PlayerTypes.Ghost"/>.
	/// </summary>
	bool IsGhosting { get; }

	/// <summary>
	/// True if the local player has an item storage (hands). US13: DynamicItemStorage != null.
	/// </summary>
	bool HasItemStorage { get; }

	/// <summary>US13: DynamicItemStorage.GetActiveHandSlot(). Null if no storage.</summary>
	IItemSlot? ActiveHandSlot { get; }

	/// <summary>US13: UIManager.CurrentIntent.</summary>
	Intent CurrentIntent { get; }

	/// <summary>The HUD damage-zone selector. US13: UIManager.DamageZone.</summary>
	BodyPartType TargetedBodyPart { get; }

	/// <summary>US13: KeyboardInputManager.IsAltActionKeyPressed().</summary>
	bool IsAltActionKeyPressed { get; }
}
