using System.Numerics;
using Godotstation.Engine.Interactions;
using Xunit;

namespace Godotstation.Tests.Interactions;

public sealed class ReplicatedStationEntitiesTests
{
    [Fact]
    public void ReplicatedPickupAndConstructionChangeBothAppearanceAndClickTargets()
    {
        var entities = new ReplicatedStationEntities();
        Assert.Equal(SharedStationInventoryWorld.WrenchId, entities.FindNearestTarget(new Vector2(-1, 2), 0.7f));
        Assert.Equal(SharedStationInventoryWorld.MetalSheetsId, entities.FindNearestTarget(new Vector2(1, 2), 0.7f));
        Assert.Equal(SharedStationInventoryWorld.GirderId, entities.FindNearestTarget(new Vector2(2, 2), 0.7f));

        entities.ApplyShared(42, new Vector2(-1, 2), new Vector2(0, 2), true);
        entities.ApplyConstruction(42, new Vector2(1, 2), 2, new Vector2(2, 2), false, 42, 1);
        Assert.False(entities.Get(SharedStationInventoryWorld.MetalSheetsId).IsTargetable);
        Assert.True(entities.Get(SharedStationInventoryWorld.GirderId).IsTargetable);
        entities.ApplyConstruction(null, new Vector2(1, 2), 0, new Vector2(2, 2), true, null, 0);

        Assert.False(entities.Get(SharedStationInventoryWorld.WrenchId).IsTargetable);
        Assert.False(entities.Get(SharedStationInventoryWorld.MetalSheetsId).IsTargetable);
        Assert.False(entities.Get(SharedStationInventoryWorld.GirderId).IsTargetable);
        Assert.Null(entities.FindNearestTarget(new Vector2(2, 2), 0.7f));
        Assert.Equal(0, entities.MetalSheetCount);
        Assert.True(entities.GirderPlated);
    }

    [Fact]
    public void LateJoinSnapshotsMoveTargetsAndKeepTheAirlockInteractive()
    {
        var entities = new ReplicatedStationEntities();
        entities.ApplyShared(null, new Vector2(-3, 1), new Vector2(0, 3), false);
        entities.ApplyConstruction(null, new Vector2(3, 1), 1, new Vector2(4, 1), false, 7, 1.5);
        entities.ApplyEngineeringAirlock(true);

        Assert.Null(entities.FindNearestTarget(new Vector2(-1, 2), 0.7f));
        Assert.Equal(SharedStationInventoryWorld.WrenchId, entities.FindNearestTarget(new Vector2(-3, 1), 0.7f));
        Assert.Equal(SharedStationInventoryWorld.MetalSheetsId, entities.FindNearestTarget(new Vector2(3, 1), 0.7f));
        Assert.Equal(SharedStationInventoryWorld.EngineeringAirlockId, entities.FindNearestTarget(new Vector2(8, -4), 0.7f));
        Assert.True(entities.EngineeringAirlockOpen);
        Assert.Equal(7, entities.ConstructionWorker);
    }
}
