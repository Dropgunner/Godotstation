using System.Numerics;

namespace Godotstation.Common.Interaction;

/// <summary>
/// Represents an entity that can perform interactions (a player).
/// Engine-agnostic — Godot implements this with its Node-based player.
/// </summary>
public interface IPerformer
{
    Vector2 WorldPosition { get; }
    bool IsGhosting { get; }
    bool HasTelekinesis { get; }
    bool IsAlive { get; }
    IInteractable? ActiveHandItem { get; }
    Intent CurrentIntent { get; }
}

/// <summary>
/// Represents anything that can be interacted with or held.
/// </summary>
public interface IInteractable
{
    string Id { get; }
    Vector2 WorldPosition { get; }
}

/// <summary>
/// Handles a specific interaction type on a target.
/// </summary>
public interface IInteractionHandler<in T> where T : Types.Interaction
{
    /// <summary>Priority (lower = checked first).</summary>
    int Priority { get; }

    /// <summary>Can this handler process the interaction?</summary>
    bool WillInteract(T interaction, NetworkSide side);

    /// <summary>Execute the interaction.</summary>
    void ServerPerformInteraction(T interaction);
}

/// <summary>
/// An IInteractionHandler attached to a specific entity, so the dispatcher can
/// tell whether it belongs to the interaction's target or the performer's held item.
/// FIDELITY CONTRACT FC-8/FC-9: held-item handlers get first refusal over the
/// target's handlers, unless the target's handler is IsFirstInteractable.
/// </summary>
public interface IOwnedInteractionHandler<in T> : IInteractionHandler<T> where T : Types.Interaction
{
    IInteractable Owner { get; }

    /// <summary>If true, this handler is checked before the held item's handler,
    /// regardless of what is held (FC-9).</summary>
    bool IsFirstInteractable { get; }
}

/// <summary>
/// Null performer for invalid interactions.
/// </summary>
public sealed class NullPerformer : IPerformer
{
    public static NullPerformer Instance { get; } = new();
    public Vector2 WorldPosition => Vector2.Zero;
    public bool IsGhosting => true;
    public bool HasTelekinesis => false;
    public bool IsAlive => false;
    public IInteractable? ActiveHandItem => null;
    public Intent CurrentIntent => Intent.Help;
}

/// <summary>
/// Body parts that can be targeted in interactions.
/// </summary>
public enum BodyPart
{
    Head,
    Chest,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
    Groin
}
