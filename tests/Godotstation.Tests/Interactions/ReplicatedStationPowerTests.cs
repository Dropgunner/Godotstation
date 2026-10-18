using System.Numerics;
using Godotstation.Engine.Interactions;
using Xunit;

namespace Godotstation.Tests.Interactions;

public sealed class ReplicatedStationPowerTests
{
    [Fact]
    public void PowerSnapshotUpdatesVisibleStateAndKeepsHeldFuelUntargetable()
    {
        var entities = new ReplicatedStationEntities();

        entities.ApplyPower(2, new Vector2(6, -7), 2, true, 3.5, 4, true, true);

        Assert.Equal(2, entities.GeneratorFuelOwner);
        Assert.Equal(2, entities.GeneratorFuelCount);
        Assert.True(entities.GeneratorRunning);
        Assert.Equal(3.5, entities.GeneratorFuelSeconds);
        Assert.Equal(4, entities.StoredEnergy);
        Assert.True(entities.PowerOutputEnabled);
        Assert.True(entities.PoweredLampOn);
        Assert.False(entities.Get(SharedStationInventoryWorld.GeneratorFuelId).IsTargetable);
        Assert.Equal(SharedStationInventoryWorld.GeneratorId,
            entities.FindNearestTarget(new Vector2(8, -7), 0.7f));

        entities.ApplyPower(null, new Vector2(7, -7), 0, false, 0, 0, true, false);

        Assert.False(entities.PoweredLampOn);
        Assert.Equal(SharedStationInventoryWorld.PowerStorageId,
            entities.FindNearestTarget(new Vector2(9, -7), 0.7f));
        Assert.Null(entities.FindNearestTarget(new Vector2(10, -7), 0.7f));
    }
}
