namespace Godotstation.Common.Interaction;

/// <summary>
/// Defines a particular way of determining if a player can reach something.
/// Ported from US13.Core.Input_System.InteractionV2.ReachRange.
/// </summary>
public enum ReachRange
{
	/// <summary>
	/// Based on standard interaction distance (<see cref="Validations.InteractionDistance"/>).
	/// </summary>
	Standard,

	/// <summary>
	/// Object can still be in reach even if outside standard interaction distance - such as for an
	/// object not perfectly aligned on the tile. Range is never checked on the client side for this
	/// mode - only on the server side.
	/// </summary>
	ExtendedServer,

	/// <summary>
	/// Don't check range at all.
	/// </summary>
	Unlimited,

	/// <summary>
	/// FOV distance (<see cref="Validations.TelekinesisInteractionDistance"/>).
	/// </summary>
	Telekinesis
}
