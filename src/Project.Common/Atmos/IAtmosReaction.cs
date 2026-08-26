namespace Project.Common.Atmos
{
    /// <summary>
    /// Result of attempting a chemical reaction on a <see cref="GasMix"/>.
    /// Mirrors Unitystation's boolean "did this reaction do work" contract so the
    /// simulation can decide whether the tile needs to be re-queued for another pass.
    /// </summary>
    public enum ReactionResult
    {
        /// <summary>The reaction's preconditions were not met; the gas mix was untouched.</summary>
        NoReaction = 0,

        /// <summary>The reaction consumed/produced gas or released energy; the tile is still active.</summary>
        Reacted = 1
    }

    /// <summary>
    /// A single, deterministic atmospheric reaction (fire, fusion, freon burn, etc.).
    /// Port of Unitystation's <c>Reaction</c> interface, stripped of any engine coupling.
    ///
    /// Reactions operate purely on a <see cref="GasMix"/>. The owning <see cref="AtmosTile"/>
    /// is passed for spatial side effects (hotspots, wind, fire spread) that a headless
    /// simulation records as data rather than rendering.
    /// </summary>
    public interface IAtmosReaction
    {
        /// <summary>
        /// Cheap gate evaluated before <see cref="React"/>. Returns true when the required
        /// reactant gases are present. Temperature/pressure thresholds are applied inside
        /// <see cref="React"/> so a satisfied-but-inert mix does not report work.
        /// </summary>
        bool Satisfies(GasMix gasMix);

        /// <summary>
        /// Runs one reaction step against <paramref name="gasMix"/>, mutating it in place.
        /// Returns <see cref="ReactionResult.Reacted"/> only when the mix actually changed.
        /// </summary>
        ReactionResult React(GasMix gasMix, AtmosTile tile);
    }
}
