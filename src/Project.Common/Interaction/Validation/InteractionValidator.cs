using System.Numerics;
using Godotstation.Common.Interaction.Types;

namespace Godotstation.Common.Interaction.Validation;

/// <summary>
/// Pure C# interaction validation. No engine dependencies.
/// Equivalent to Unitystation's DefaultWillInteract + Validations.
/// </summary>
public static class InteractionValidator
{
    /// <summary>
    /// Validates whether a HandApply interaction can proceed.
    /// Enforces: ghost check, reach validation, telekinesis rules.
    /// </summary>
    public static bool CanInteract(HandApply interaction, NetworkSide side)
    {
        if (!interaction.IsValid) return false;
        if (interaction.Performer.IsGhosting) return false;
        if (!interaction.Performer.IsAlive) return false;

        var reachRange = DetermineReachRange(interaction.Performer);

        if (interaction.TargetObject == null) return false;

        return IsWithinReach(
            interaction.Performer.WorldPosition,
            interaction.TargetObject.WorldPosition,
            reachRange);
    }

    /// <summary>
    /// Validates whether a PositionalHandApply interaction can proceed.
    /// </summary>
    public static bool CanInteract(PositionalHandApply interaction, NetworkSide side)
    {
        if (!interaction.IsValid) return false;
        if (interaction.Performer.IsGhosting) return false;
        if (!interaction.Performer.IsAlive) return false;

        var reachRange = DetermineReachRange(interaction.Performer);

        return IsWithinReach(
            interaction.Performer.WorldPosition,
            interaction.TargetPosition,
            reachRange);
    }

    /// <summary>
    /// Validates whether a MouseDrop interaction can proceed.
    /// CRITICAL FIDELITY: Both objects must be reachable from each other.
    /// </summary>
    public static bool CanInteract(MouseDrop interaction, NetworkSide side)
    {
        if (!interaction.IsValid) return false;
        if (interaction.Performer.IsGhosting) return false;
        if (!interaction.Performer.IsAlive) return false;

        // Performer must reach the dropped object
        if (!IsWithinReach(
            interaction.Performer.WorldPosition,
            interaction.DroppedObject.WorldPosition,
            ReachRange.Standard))
            return false;

        // FIDELITY CONTRACT FC-7: Both objects must be in mutual reach
        return IsReachableByPositions(
            interaction.DroppedObject.WorldPosition,
            interaction.TargetObject.WorldPosition);
    }

    /// <summary>
    /// Validates basic interaction capability (HandActivate, InventoryApply).
    /// </summary>
    public static bool CanInteract(IPerformer performer, NetworkSide side)
    {
        if (performer.IsGhosting) return false;
        if (!performer.IsAlive) return false;
        return true;
    }

    /// <summary>
    /// Determines reach range based on telekinesis rules.
    /// FIDELITY CONTRACT FC-3: Telekinesis only applies with EMPTY hand.
    /// </summary>
    public static ReachRange DetermineReachRange(IPerformer performer)
    {
        if (performer.HasTelekinesis && performer.ActiveHandItem == null)
        {
            return ReachRange.Telekinesis;
        }
        return ReachRange.Standard;
    }

    /// <summary>
    /// Checks if two positions are within the given reach range (Manhattan distance).
    /// FIDELITY CONTRACT FC-2: Standard range = 1 tile Manhattan distance.
    /// </summary>
    public static bool IsWithinReach(Vector2 from, Vector2 to, ReachRange range)
    {
        float manhattanDistance = MathF.Abs(from.X - to.X) + MathF.Abs(from.Y - to.Y);
        return manhattanDistance <= (int)range;
    }

    /// <summary>
    /// Checks if two world positions are reachable from each other.
    /// Used for MouseDrop validation (FC-7).
    /// </summary>
    public static bool IsReachableByPositions(Vector2 posA, Vector2 posB)
    {
        // Standard reach between the two objects
        return IsWithinReach(posA, posB, ReachRange.Standard);
    }
}
