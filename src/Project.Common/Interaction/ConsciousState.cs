namespace Godotstation.Common.Interaction;

/// <summary>
/// Consciousness level of a living player. Values ported verbatim from US13 HealthEnums -
/// the non-sequential values are load-bearing (they are network-serialized).
/// </summary>
public enum ConsciousState
{
	/// <summary>Alive and well.</summary>
	CONSCIOUS = 0,

	/// <summary>In crit, can crawl.</summary>
	BARELY_CONSCIOUS = 3,

	/// <summary>Unconscious, can't crawl.</summary>
	UNCONSCIOUS = 1,

	/// <summary>Really dead.</summary>
	DEAD = 2
}
