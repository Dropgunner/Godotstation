using System;

namespace Godotstation.Common.Interactions
{
	/// <summary>
	/// Which side of the network a validation is running on. The rule set is one
	/// function parameterized by side (FC-8); side only ever selects a position
	/// source or a sanctioned client-permissive shortcut, never different logic.
	/// Port of US13.Core.Input_System.InteractionV2.NetworkSide.
	/// </summary>
	public enum NetworkSide
	{
		Client,
		Server
	}

	/// <summary>
	/// Defines a particular way of determining if a player can reach something.
	/// Port of US13.Core.Input_System.InteractionV2.ReachRange, member-for-member.
	/// </summary>
	public enum ReachRange
	{
		/// <summary>Standard interaction distance: strict Chebyshev &lt; 1.5 tiles plus an unblocked click line.</summary>
		Standard,

		/// <summary>
		/// Object can still be in reach even if outside standard interaction distance —
		/// such as an object not perfectly aligned on its tile. Range is never checked
		/// on the client for this mode; only the server checks (sanctioned asymmetry).
		/// </summary>
		ExtendedServer,

		/// <summary>Don't check range at all.</summary>
		Unlimited,

		/// <summary>
		/// Telekinesis reach: Euclidean distance ≤ 15 on assumed server positions,
		/// evaluated on BOTH sides from server-assumed positions, and — faithfully
		/// to the original — with NO blocking linecast. TK reaches through walls;
		/// field-of-view gating is a different system's job.
		/// </summary>
		Telekinesis
	}

	/// <summary>
	/// The four-intent system. Values match the original exactly (UI code indexes
	/// sprites by these ints; the net protocol serializes them).
	/// Port of Intent from US13 ControlIntent.cs.
	/// </summary>
	public enum Intent
	{
		None = -1,
		Help = 0,
		Disarm,
		Grab,
		Harm
	}

	/// <summary>
	/// Port of ConsciousState (CONSCIOUS / BARELY_CONSCIOUS / UNCONSCIOUS / DEAD).
	/// Only Conscious interacts freely; BarelyConscious interacts only where a
	/// validation opts into allowSoftCrit.
	/// </summary>
	public enum ConsciousState
	{
		Conscious,
		BarelyConscious,
		Unconscious,
		Dead
	}

	/// <summary>
	/// Player-type mask used by the `apt` (allowed player types) parameter.
	/// The mask plumbing is explicitly OUTSIDE the Fidelity Contract (see
	/// FIDELITY_CONTRACT_INTERACTION.md exclusions); only HasFlag semantics
	/// against the performer's type must hold.
	/// </summary>
	[Flags]
	public enum PlayerTypes
	{
		None = 0,
		Normal = 1 << 0,
		Ghost = 1 << 1,
		Ai = 1 << 2,
		Alien = 1 << 3,
		Blob = 1 << 4
	}

	/// <summary>
	/// Targeted body zone captured at click time (FC-7). Opaque to the interaction
	/// pipeline itself — validation never branches on it; it is frozen into the
	/// interaction and carried to combat/health systems.
	/// </summary>
	public enum BodyPartType
	{
		None,
		Head,
		Eyes,
		Mouth,
		Chest,
		LeftArm,
		RightArm,
		LeftHand,
		RightHand,
		Groin,
		LeftLeg,
		RightLeg,
		LeftFoot,
		RightFoot
	}
}
