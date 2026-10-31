using System.Collections.Generic;
using System.Numerics;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Interactions;
using Xunit;

namespace Godotstation.Tests.Interactions;

public sealed class ReplicatedStationEntitiesTests
{
    private static StationEntityState State(int id, string kind, Vector2 position, int? holder,
        StationEntityFlags flags, params (string Key, double Value)[] fields)
    {
        var map = new Dictionary<string, double>();
        foreach ((string key, double value) in fields) map[key] = value;
        return new StationEntityState(id, kind, position, holder, flags, map);
    }

    [Fact]
    public void StartsFromTheCatalogSoEveryObjectIsDrawnAndTargetableBeforeTheFirstSnapshot()
    {
        var entities = new ReplicatedStationEntities();

        foreach (StationEntitySpawn spawn in StationEntityCatalog.Playtest)
        {
            Assert.Equal(spawn.Kind, entities.Get(spawn.Id).Kind);
            Assert.Equal(spawn.Position, entities.Get(spawn.Id).Position);
        }
        Assert.Equal(StationEntityCatalog.WrenchId, entities.FindNearestTarget(new Vector2(-1, 2), 0.7f));
        Assert.Equal(StationEntityCatalog.MetalSheetsId, entities.FindNearestTarget(new Vector2(1, 2), 0.7f));
        Assert.Equal(StationEntityCatalog.GirderId, entities.FindNearestTarget(new Vector2(2, 2), 0.7f));
        Assert.Equal(2, entities.Get(StationEntityCatalog.MetalSheetsId).Count(StationEntityField.Amount));
        Assert.Null(entities.FindNearestTarget(new Vector2(10, -7), 0.7f));
    }

    [Fact]
    public void ReplicatedPickupAndConstructionChangeBothAppearanceAndClickTargets()
    {
        var entities = new ReplicatedStationEntities();
        entities.Apply([
            State(StationEntityCatalog.WrenchId, StationEntityKind.Wrench, new Vector2(-1, 2), 42, StationEntityFlags.Item),
            State(StationEntityCatalog.MetalSheetsId, StationEntityKind.MetalSheets, new Vector2(1, 2), 42,
                StationEntityFlags.Item, (StationEntityField.Amount, 2)),
            State(StationEntityCatalog.GirderId, StationEntityKind.Girder, new Vector2(2, 2), null,
                StationEntityFlags.Solid | StationEntityFlags.Targetable,
                (StationEntityField.Worker, 42), (StationEntityField.Progress, 1)),
        ]);
        Assert.False(entities.Get(StationEntityCatalog.MetalSheetsId).IsTargetable);
        Assert.True(entities.Get(StationEntityCatalog.GirderId).IsTargetable);

        entities.Apply([
            State(StationEntityCatalog.GirderId, StationEntityKind.Girder, new Vector2(2, 2), null,
                StationEntityFlags.Solid, (StationEntityField.Plated, 1)),
        ]);

        Assert.False(entities.Get(StationEntityCatalog.WrenchId).IsTargetable);
        Assert.Null(entities.FindNearestTarget(new Vector2(2, 2), 0.7f));
        Assert.True(entities.Get(StationEntityCatalog.GirderId).Flag(StationEntityField.Plated));
        Assert.Equal(42, entities.Get(StationEntityCatalog.MetalSheetsId).Holder);
    }

    [Fact]
    public void LateJoinSnapshotFromTheServerMovesTargetsAndCarriesEveryObjectsState()
    {
        var server = new SharedStationInventoryWorld();
        Assert.True(server.AddActor(2, new Vector2(7, -7), Godotstation.Engine.Rounds.StationCrewJobCatalog.EngineerJobId));
        Assert.True(server.TryInteract(2, StationEntityCatalog.GeneratorFuelId, Godotstation.Common.Interactions.Intent.Help, false));
        Assert.True(server.TryInteract(2, StationEntityCatalog.FuelGeneratorId, Godotstation.Common.Interactions.Intent.Help, false));
        Assert.True(server.SetEntityPosition(StationEntityCatalog.ServiceCartId, new Vector2(0, 3)));

        Assert.True(StationEntityStateCodec.TryDecode(StationEntityStateCodec.Encode(server.CaptureAll()),
            out IReadOnlyList<StationEntityState> snapshot));
        var entities = new ReplicatedStationEntities();
        entities.Apply(snapshot);

        Assert.Equal(2, entities.Get(StationEntityCatalog.GeneratorFuelId).Holder);
        Assert.Equal(2, entities.Get(StationEntityCatalog.GeneratorFuelId).Count(StationEntityField.Amount));
        Assert.Equal(4, entities.Get(StationEntityCatalog.FuelGeneratorId).Get(StationEntityField.FuelSeconds));
        Assert.False(entities.Get(StationEntityCatalog.GeneratorFuelId).IsTargetable);
        Assert.Null(entities.FindNearestTarget(new Vector2(0, 2), 0.3f));
        Assert.Equal(StationEntityCatalog.ServiceCartId, entities.FindNearestTarget(new Vector2(0, 3), 0.7f));
        Assert.Equal(StationEntityCatalog.EngineeringAirlockId, entities.FindNearestTarget(new Vector2(8, -4), 0.7f));
    }
}
