using System;

namespace Godotstation.Engine.Health;

/// <summary>What one unarmed swing did.</summary>
public readonly record struct UnarmedAttackResult(bool Hit, double Damage, string Verb, string VerbContinuous);

/// <summary>
/// tgstation <c>species/harm</c> at commit f37d36c, for an unarmed human with no skills, traits,
/// armour or drink. A standing target is punched with an arm: 5 to 10 brute, and the swing misses
/// with a chance of <c>UNARMED_MISS_CHANCE_BASE</c> (20) minus the arm's effectiveness (10) plus half
/// the attacker's own brute damage, capped at <c>UNARMED_MISS_CHANCE_MAX</c> (80). A target lying
/// down is kicked with a leg instead: 7 to 15 brute, and a kick never misses.
/// </summary>
public static class UnarmedAttack
{
    public const int PunchDamageLow = 5;
    public const int PunchDamageHigh = 10;
    public const int PunchEffectiveness = 10;
    public const int KickDamageLow = 7;
    public const int KickDamageHigh = 15;
    public const double MissChanceBase = 20;
    public const double MissChanceMax = 80;

    /// <summary>Percent chance that a punch at a standing target misses.</summary>
    public static double PunchMissChance(double attackerBrute) =>
        Math.Clamp(MissChanceBase - PunchEffectiveness + Math.Max(0, attackerBrute) / 2, 0, MissChanceMax);

    /// <summary>Rolls one swing. Damage is rolled before the miss check, as in the source.</summary>
    public static UnarmedAttackResult Roll(double attackerBrute, bool targetLyingDown, Random random,
        bool targetStaggered = false)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (targetLyingDown)
            return new UnarmedAttackResult(true, random.Next(KickDamageLow, KickDamageHigh + 1), "kick", "kicks");
        int damage = random.Next(PunchDamageLow, PunchDamageHigh + 1);
        // A target staggered by a recent shove is never missed.
        bool missed = !targetStaggered && random.NextDouble() * 100 < PunchMissChance(attackerBrute);
        return new UnarmedAttackResult(!missed, missed ? 0 : damage, "punch", "punches");
    }
}
