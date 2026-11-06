using System;

namespace Godotstation.Engine.Health;

/// <summary>What one disarm attempt did (Unitystation Systems/Inventory/Disarmable.cs).</summary>
public enum DisarmOutcome
{
    Miss,
    Disarm,
    Knockdown,
}

/// <summary>
/// FC-DA-1/2: Disarmable rolls <c>rng.Next(1, 100) &lt;= 5</c> for a knockdown, otherwise a fresh
/// <c>rng.Next(1, 100) &lt;= 50</c> for a disarm, otherwise a miss; attempts are 0.5 s apart and a
/// knockdown stuns for 6 s (<c>RegisterPlayer.ServerStun(6, false)</c>, hands kept).
/// </summary>
public static class DisarmRules
{
    public const int KnockdownChancePercent = 5;
    public const int DisarmChancePercent = 50;
    public const double KnockdownStunSeconds = 6;
    public const double TimeBetweenDisarms = 0.5;

    /// <param name="next">Draws an integer in [min, max), like <see cref="Random.Next(int, int)"/>.</param>
    public static DisarmOutcome Roll(Func<int, int, int> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (next(1, 100) <= KnockdownChancePercent) return DisarmOutcome.Knockdown;
        if (next(1, 100) <= DisarmChancePercent) return DisarmOutcome.Disarm;
        return DisarmOutcome.Miss;
    }
}
