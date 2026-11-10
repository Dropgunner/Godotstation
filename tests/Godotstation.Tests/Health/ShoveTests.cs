using System;
using System.Collections.Generic;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Interactions;
using Xunit;

namespace Godotstation.Tests.Health;

public sealed class ShoveTests
{
    private sealed class AlwaysMiss : Random
    {
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0.0;
    }

    private const int Shover = 2;
    private const int Target = 1;

    private static SharedStationInventoryWorld World(Func<Vector2, bool>? canEnter = null)
    {
        var world = new SharedStationInventoryWorld();
        if (canEnter != null) world.CanCrewEnterTile = canEnter;
        Assert.True(world.AddActor(Target, new Vector2(-1, 0)));
        Assert.True(world.AddActor(Shover, new Vector2(-1, 1)));
        return world;
    }

    private static List<ShoveResult> Shoves(SharedStationInventoryWorld world)
    {
        var shoves = new List<ShoveResult>();
        world.CrewShoved += (_, _, result) => shoves.Add(result);
        return shoves;
    }

    [Fact]
    public void AShoveMovesTheTargetOneTileDirectlyAway()
    {
        SharedStationInventoryWorld world = World();
        List<ShoveResult> shoves = Shoves(world);
        var displaced = new List<(int, Vector2)>();
        world.CrewDisplaced += (peer, tile) => displaced.Add((peer, tile));

        Assert.True(world.TryInteract(Shover, Target, Intent.Disarm, false));

        Assert.Equal(new ShoveResult(true, false, string.Empty), Assert.Single(shoves));
        Assert.Equal((Target, new Vector2(-1, -1)), Assert.Single(displaced));
        Assert.Equal(new Vector2(-1, -1), world.GetState(Target)!.Position);
        Assert.False(world.IsLyingDown(Target));
        Assert.True(world.IsStaggered(Target));
    }

    [Fact]
    public void ADiagonalShovePushesDiagonally()
    {
        SharedStationInventoryWorld world = World();
        Assert.True(world.SetActorPosition(Shover, new Vector2(-2, 1)));
        Assert.True(world.TryInteract(Shover, Target, Intent.Disarm, false));
        Assert.Equal(new Vector2(0, -1), world.GetState(Target)!.Position);
    }

    [Fact]
    public void ABlockedShoveKnocksTheTargetDownForTwoSeconds()
    {
        SharedStationInventoryWorld world = World(_ => false);
        List<ShoveResult> shoves = Shoves(world);

        Assert.True(world.TryInteract(Shover, Target, Intent.Disarm, false));

        Assert.Equal(new ShoveResult(false, true, string.Empty), Assert.Single(shoves));
        Assert.Equal(new Vector2(-1, 0), world.GetState(Target)!.Position);
        Assert.True(world.IsLyingDown(Target));
        Assert.True(world.GetState(Target)!.Flag(StationEntityField.Lying));
        Assert.Equal(CrewStat.Stable, world.GetCrewStat(Target));

        world.AdvanceHealth(1.9);
        Assert.True(world.IsLyingDown(Target));
        world.AdvanceHealth(0.2);
        Assert.False(world.IsLyingDown(Target));
        Assert.False(world.GetState(Target)!.Flag(StationEntityField.Lying));
    }

    [Fact]
    public void AKnockedDownTargetDropsWhatItHoldsAndIsKickedNotPunched()
    {
        SharedStationInventoryWorld world = World(_ => false);
        world.Random = new AlwaysMiss();
        Assert.True(world.SetActorPosition(Target, new Vector2(-1, 1)));
        Assert.True(world.SetActorPosition(Shover, new Vector2(0, 1)));
        Assert.True(world.TryInteract(Target, StationEntityCatalog.WrenchId, Intent.Help, false));
        List<ShoveResult> shoves = Shoves(world);

        // The shove is blocked, so the target goes down. The drop is checked after the knockdown,
        // as in the source, so the wrench falls on the same shove.
        Assert.True(world.TryInteract(Shover, Target, Intent.Disarm, false));
        Assert.Equal(new ShoveResult(false, true, "wrench"), Assert.Single(shoves));
        Assert.Null(world.GetState(StationEntityCatalog.WrenchId)!.Holder);

        // A punch that would always miss a standing target becomes a kick that cannot miss.
        world.AdvanceHealth(CrewMelee.ClickCooldownSeconds);
        var swings = new List<UnarmedAttackResult>();
        world.CrewStruckUnarmed += (_, _, result) => swings.Add(result);
        Assert.True(world.TryInteract(Shover, Target, Intent.Harm, false));
        Assert.Equal("kick", Assert.Single(swings).Verb);
        Assert.True(swings[0].Hit);
    }

    [Fact]
    public void AStaggeredTargetCannotBeMissedByAPunch()
    {
        SharedStationInventoryWorld world = World();
        world.Random = new AlwaysMiss();
        var swings = new List<UnarmedAttackResult>();
        world.CrewStruckUnarmed += (_, _, result) => swings.Add(result);

        Assert.True(world.TryInteract(Shover, Target, Intent.Harm, false));
        Assert.False(swings[0].Hit);

        world.AdvanceHealth(CrewMelee.ClickCooldownSeconds);
        Assert.True(world.TryInteract(Shover, Target, Intent.Disarm, false)); // target now at (-1,-1), staggered
        Assert.True(world.SetActorPosition(Shover, new Vector2(-1, 0)));
        world.AdvanceHealth(CrewMelee.ClickCooldownSeconds);
        Assert.True(world.TryInteract(Shover, Target, Intent.Harm, false));
        Assert.True(swings[1].Hit);
        Assert.Equal("punch", swings[1].Verb);

        world.AdvanceHealth(SharedStationInventoryWorld.StaggerSeconds);
        Assert.False(world.IsStaggered(Target));
        Assert.True(world.TryInteract(Shover, Target, Intent.Harm, false));
        Assert.False(swings[2].Hit);
    }

    [Fact]
    public void ShovingNeedsAnEmptyHandReachTheCooldownAndAStandingShover()
    {
        SharedStationInventoryWorld world = World(_ => false);
        Assert.False(world.TryInteract(Shover, Shover, Intent.Disarm, false));

        Assert.True(world.TryInteract(Shover, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.False(world.TryInteract(Shover, Target, Intent.Disarm, false)); // wrench in hand
        Assert.True(world.TryToggleActiveHand(Shover));

        Assert.True(world.TryInteract(Shover, Target, Intent.Disarm, false));
        Assert.False(world.TryInteract(Shover, Target, Intent.Disarm, false)); // cooldown

        // The knocked-down target cannot shove back until it is up again.
        world.AdvanceHealth(CrewMelee.ClickCooldownSeconds);
        Assert.False(world.TryInteract(Target, Shover, Intent.Disarm, false));
        world.AdvanceHealth(SharedStationInventoryWorld.ShoveKnockdownSeconds);
        Assert.True(world.TryInteract(Target, Shover, Intent.Disarm, false));

        Assert.True(world.SetActorPosition(Target, new Vector2(-1, -3)));
        world.AdvanceHealth(CrewMelee.ClickCooldownSeconds);
        Assert.False(world.TryInteract(Shover, Target, Intent.Disarm, false)); // out of reach
    }

    [Fact]
    public void DraggingAKnockedDownCrewMemberCountsAsDraggingProne()
    {
        SharedStationInventoryWorld world = World(_ => false);
        Assert.True(world.TryInteract(Shover, Target, Intent.Disarm, false));
        Assert.True(world.TryTogglePull(Shover, Target));
        Assert.True(world.IsDraggingProne(Shover));
        world.AdvanceHealth(SharedStationInventoryWorld.ShoveKnockdownSeconds + 0.1);
        Assert.False(world.IsDraggingProne(Shover));
    }
}
