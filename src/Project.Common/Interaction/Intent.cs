namespace Godotstation.Common.Interaction;

/// <summary>
/// The player's selected intent, which changes what an otherwise identical click does.
/// Values ported verbatim from US13 ControlIntent (they are serialized over the network
/// and used as UI indices, so they must not change).
/// </summary>
public enum Intent
{
	None = -1,
	Help = 0,
	Disarm = 1,
	Grab = 2,
	Harm = 3
}
