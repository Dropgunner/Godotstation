namespace Godotstation.Common.Interaction;

/// <summary>
/// The interaction-relevant state of a player. Replaces the PlayerScript/playerMove/playerHealth
/// component cluster from US13 with one flat, engine-agnostic view.
/// </summary>
public interface IPlayerEntity : IWorldEntity
{
	/// <summary>US13: PlayerScript.PlayerType.</summary>
	PlayerTypes PlayerType { get; }

	/// <summary>US13: playerMove.IsCuffed.</summary>
	bool IsCuffed { get; }

	/// <summary>US13: playerMove.AllowInput.</summary>
	bool AllowInput { get; }

	/// <summary>US13: playerHealth.ConsciousState.</summary>
	ConsciousState ConsciousState { get; }

	/// <summary>
	/// True when the player is inside a container (closet, crate...). US13: PlayerScript.IsHidden.
	/// </summary>
	bool IsHidden { get; }

	/// <summary>
	/// The container the player is hidden inside, if the caller's side knows it. US13:
	/// ObjectPhysics.ContainedInObjectContainer - only populated server-side; clients return null.
	/// </summary>
	IWorldEntity? ContainedIn { get; }

	/// <summary>
	/// US13: playerHealth.brain.HasTelekinesis. Must be false when the player has no brain.
	/// </summary>
	bool HasTelekinesis { get; }
}
