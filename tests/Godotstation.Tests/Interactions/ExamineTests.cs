using System.Collections.Generic;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Interactions;
using Godotstation.Engine.Rounds;
using Xunit;

namespace Godotstation.Tests.Interactions;

public sealed class ExamineTests
{
    private sealed class FullyBlockedMap : IReachabilityMap
    {
        public bool IsPassableForClick(Vector2 fromWorldPos, Vector2 toWorldPos, bool isServer,
            WorldEntity? context = null) => false;
    }

    /// <summary>Blocks any line that ends in Medical (y below -4), like the station's partition wall.</summary>
    private sealed class WalledOffMedical : IReachabilityMap
    {
        public bool IsPassableForClick(Vector2 fromWorldPos, Vector2 toWorldPos, bool isServer,
            WorldEntity? context = null) => (fromWorldPos.Y < -4) == (toWorldPos.Y < -4);
    }

    private static readonly Vector2 Min = new(-12, -9);
    private static readonly Vector2 Max = new(12, 9);

    private static IReadOnlyList<string> Examine(SharedStationInventoryWorld world, int examiner, int target)
    {
        Assert.Equal(ExamineResult.Ok, world.TryExamine(examiner, target, out IReadOnlyList<string> lines));
        return lines;
    }

    private static SharedStationInventoryWorld ArmedWorld(IReachabilityMap? map = null)
    {
        var world = new SharedStationInventoryWorld(map);
        Assert.True(world.AddActor(1, new Vector2(-1, 0), StationCrewJobCatalog.EngineerJobId));
        Assert.True(world.AddActor(2, new Vector2(-1, 1)));
        return world;
    }

    private static void Hit(SharedStationInventoryWorld world, int times)
    {
        for (int i = 0; i < times; i++)
        {
            Assert.True(world.TryInteract(2, 1, Intent.Harm, false));
            world.AdvanceHealth(CrewMelee.ClickCooldownSeconds);
        }
    }

    [Fact]
    public void ObjectsGiveNameDescriptionAndState()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        Assert.Equal(new[] { "That's a wrench.", "A wrench with common uses. Can be found in your hand." },
            Examine(world, 2, StationEntityCatalog.WrenchId));
        Assert.Equal(new[]
        {
            "That's a bruise pack.",
            "A therapeutic gel pack and bandages designed to treat blunt-force trauma.",
            "There are 6 bruise packs in the stack.",
        }, Examine(world, 2, StationEntityCatalog.BruisePackId));
        Assert.Contains("There are 2 metal sheets in the stack.", Examine(world, 2, StationEntityCatalog.MetalSheetsId));
        Assert.Contains("There are 3 units of generator fuel in the stack.",
            Examine(world, 2, StationEntityCatalog.GeneratorFuelId));
        Assert.Contains("It is anchored to the floor.", Examine(world, 2, StationEntityCatalog.ServiceCartId));
        Assert.Contains("It is closed.", Examine(world, 2, StationEntityCatalog.EngineeringAirlockId));
        Assert.Contains("It is off and has no fuel.", Examine(world, 2, StationEntityCatalog.FuelGeneratorId));
        Assert.Contains("It is dark.", Examine(world, 2, StationEntityCatalog.ApcLampId));
        Assert.Equal("That's an APC-powered lamp.", Examine(world, 2, StationEntityCatalog.ApcLampId)[0]);
    }

    [Fact]
    public void CrewShowRoleHeldItemsBruisingAndCondition()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        Assert.Equal(new[] { "This is Crew 1, Engineer." }, Examine(world, 2, 1));

        Assert.True(world.TryInteract(2, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.Equal(new[] { "This is Crew 2, Assistant.", "They are holding a wrench in their right hand." },
            Examine(world, 1, 2));

        Hit(world, 1);
        Assert.Contains("They have minor bruising.", Examine(world, 2, 1));
        Hit(world, 4);
        Assert.Contains("They have moderate bruising!", Examine(world, 2, 1));
        Hit(world, 5);
        Assert.Contains("They have severe bruising!", Examine(world, 2, 1));
        Hit(world, 10);
        Assert.Contains("They are barely conscious.", Examine(world, 2, 1));
        Hit(world, 6);
        Assert.Contains("They aren't responding to anything around them and seem to be asleep.", Examine(world, 2, 1));
    }

    [Fact]
    public void DeadBodiesReadDifferentlyOnceTheGhostHasLeft()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        Assert.True(world.TryInteract(2, StationEntityCatalog.WrenchId, Intent.Help, false));
        for (int i = 0; i < 100 && world.GetCrewStat(1) != CrewStat.Dead; i++) Hit(world, 1);

        Assert.Contains("They are limp and unresponsive; there are no signs of life...", Examine(world, 2, 1));
        Assert.True(world.TryMoveGhost(1, 1, 0, 0.1, Min, Max));
        Assert.Contains("They are limp and unresponsive; there are no signs of life and their soul has departed...",
            Examine(world, 2, 1));
    }

    [Fact]
    public void ExamineNeedsSightButNotHands()
    {
        SharedStationInventoryWorld world = ArmedWorld(new FullyBlockedMap());
        Assert.Equal(ExamineResult.OutOfView, world.TryExamine(2, StationEntityCatalog.WrenchId, out _));
        Assert.Equal(ExamineResult.OutOfView, world.TryExamine(2, 1, out _));
        Assert.Equal(ExamineResult.Ok, world.TryExamine(2, 2, out _));
        Assert.Equal(ExamineResult.UnknownTarget, world.TryExamine(2, 424242, out _));
        Assert.Equal(ExamineResult.UnknownExaminer, world.TryExamine(77, StationEntityCatalog.WrenchId, out _));
    }

    [Fact]
    public void SoftCritCanStillExamineButHardCritCannotAndTheDeadSeeThroughWalls()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        Assert.True(world.TryInteract(2, StationEntityCatalog.WrenchId, Intent.Help, false));
        Hit(world, 20);
        Assert.Equal(CrewStat.SoftCrit, world.GetCrewStat(1));
        Assert.Equal(ExamineResult.Ok, world.TryExamine(1, 2, out _));
        Hit(world, 6);
        Assert.Equal(CrewStat.HardCrit, world.GetCrewStat(1));
        Assert.Equal(ExamineResult.Unconscious, world.TryExamine(1, 2, out _));

    }

    [Fact]
    public void TheDeadSeeThroughWalls()
    {
        // The wall-aware station map: Medical's bruise packs are walled off from the central hall.
        var map = new WalledOffMedical();
        var world = new SharedStationInventoryWorld(map);
        Assert.True(world.AddActor(1, new Vector2(-1, 0)));
        Assert.True(world.AddActor(2, new Vector2(-1, 1)));
        Assert.True(world.TryInteract(2, StationEntityCatalog.WrenchId, Intent.Help, false));
        for (int i = 0; i < 100 && world.GetCrewStat(1) != CrewStat.Dead; i++) Hit(world, 1);

        Assert.Equal(ExamineResult.OutOfView, world.TryExamine(2, StationEntityCatalog.BruisePackId, out _));
        Assert.Equal(ExamineResult.Ok, world.TryExamine(1, StationEntityCatalog.BruisePackId, out _));
    }

    [Fact]
    public void HeldItemsAreExaminedThroughTheirHolder()
    {
        SharedStationInventoryWorld world = ArmedWorld();
        Assert.True(world.TryInteract(2, StationEntityCatalog.WrenchId, Intent.Help, false));
        Assert.Equal(ExamineResult.UnknownTarget, world.TryExamine(1, StationEntityCatalog.WrenchId, out _));
    }
}
