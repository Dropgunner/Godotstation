namespace Godotstation.Common.Interaction;

/// <summary>
/// Categories of controllable players. Interactions declare which types may perform them via a
/// flags mask (see the "apt" parameters on <see cref="Validations"/>).
/// Values ported verbatim from US13 PlayerScript.
/// </summary>
[Flags]
public enum PlayerTypes
{
	None = 0,
	Normal = 1 << 0,
	Ghost = 1 << 1,
	Blob = 1 << 2,
	Ai = 1 << 3,
	Alien = 1 << 4
}
