namespace Godotstation.Common.Interaction;

/// <summary>
/// Represents an interaction that has a target upon which the interaction is performed.
/// Abstract, only used internally for IF2.
/// Ported from US13 Interactions.Internal.TargetedInteraction.
/// </summary>
public abstract class TargetedInteraction : Interaction
{
	/// <summary>Object that is targeted by the interaction. Null if clicking on open space.</summary>
	public IWorldEntity? TargetObject { get; protected set; }

	protected TargetedInteraction(IPlayerEntity? performer, IWorldEntity? usedObject,
		IWorldEntity? targetObject, Intent intent)
		: base(performer, usedObject, intent)
	{
		TargetObject = targetObject;
	}
}
