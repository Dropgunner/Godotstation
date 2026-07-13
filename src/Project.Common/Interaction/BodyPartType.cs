namespace Godotstation.Common.Interaction;

/// <summary>
/// Body zone a targeted interaction is aimed at (the damage-zone selector in the HUD).
/// Values ported verbatim from US13 HealthEnums - the irregular ints are UI indices and are
/// serialized, so they must not be renumbered. Note that upstream Ears and LeftHand share
/// value 9; that collision is preserved.
/// </summary>
public enum BodyPartType
{
	None = -1,
	Head = 0,
	Chest = 1,
	RightArm = 2,
	LeftArm = 3,
	RightLeg = 4,
	LeftLeg = 5,
	Groin = 6,
	Eyes = 7,
	Mouth = 8,
	Ears = 9,
	LeftHand = 9,
	RightHand = 10,
	LeftFoot = 11,
	RightFoot = 12,

	/// <summary>Used for extra body parts on non-standard species.</summary>
	Custom = 99
}
