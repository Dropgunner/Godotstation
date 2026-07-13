namespace Godotstation.Common.Interaction;

/// <summary>
/// Targeted interaction which targets a specific body part. Abstract, only used internally for IF2.
/// Ported from US13 Interactions.Internal.BodyPartTargetedInteraction.
/// </summary>
public abstract class BodyPartTargetedInteraction : TargetedInteraction
{
	/// <summary>Body part being targeted.</summary>
	public BodyPartType TargetBodyPart { get; protected set; }

	protected BodyPartTargetedInteraction(IPlayerEntity? performer, IWorldEntity? usedObject,
		IWorldEntity? targetObject, BodyPartType targetBodyPart, Intent intent)
		: base(performer, usedObject, targetObject, intent)
	{
		TargetBodyPart = targetBodyPart;
	}
}
