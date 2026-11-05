using System.Linq;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Maps;
using Xunit;

namespace Godotstation.Tests.Maps;

public sealed class StationMapValidatorTests
{
    private static StationMap Arena()
    {
        StationMap map = StationMaps.CreateBlank(9, 9, "Arena");
        return map;
    }

    [Fact]
    public void BlankMap_IsPlayable()
    {
        Assert.Empty(StationMapValidator.Validate(Arena()));
        Assert.True(StationMapValidator.IsPlayable(Arena()));
    }

    [Fact]
    public void MissingSpawn_IsAnError()
    {
        StationMap map = Arena();
        map.RemoveSpawnAt(0, 0);
        StationMapProblem problem = Assert.Single(StationMapValidator.Validate(map));
        Assert.True(problem.IsError);
        Assert.Contains("spawn", problem.Message);
    }

    [Fact]
    public void SpawnOrEntityInsideWall_IsAnError()
    {
        StationMap map = Arena();
        map.SetWall(0, 0);
        map.PlaceEntity(StationEntityKind.Wrench, map.Bounds.MinX, 0);

        var problems = StationMapValidator.Validate(map);
        Assert.Contains(problems, problem => problem.IsError && problem.Message == "Spawn is inside a wall" && problem.Tile == (0, 0));
        Assert.Contains(problems, problem => problem.IsError && problem.Message.Contains("inside a wall") && problem.Tile == (map.Bounds.MinX, 0));
    }

    [Fact]
    public void UnknownKind_StackWithoutAmount_AndDuplicateIds_AreErrors()
    {
        StationMap map = Arena();
        map.PlaceEntity("teleporter", 1, 1);
        map.PlaceEntity(StationEntityKind.MetalSheets, 2, 1);
        map.PlaceEntity(StationEntityKind.Wrench, 1, 2, id: 50);
        map.PlaceEntity(StationEntityKind.Wrench, 2, 2, id: 51);

        var problems = StationMapValidator.Validate(map);
        Assert.Contains(problems, problem => problem.Message.Contains("Unknown entity kind 'teleporter'"));
        Assert.Contains(problems, problem => problem.Message.Contains("amount of at least 1"));
        Assert.All(problems, problem => Assert.True(problem.IsError));
        Assert.False(StationMapValidator.IsPlayable(map));
    }

    [Fact]
    public void SolidEntities_MayNotShareATileOrBlockASpawn()
    {
        StationMap map = Arena();
        map.PlaceEntity(StationEntityKind.Girder, 1, 1);
        map.PlaceEntity(StationEntityKind.ServiceCart, 1, 1);
        map.PlaceEntity(StationEntityKind.Girder, 0, 0);

        var problems = StationMapValidator.Validate(map);
        Assert.Contains(problems, problem => problem.IsError && problem.Message.Contains("share a tile") && problem.Tile == (1, 1));
        Assert.Contains(problems, problem => problem.IsError && problem.Message.Contains("Spawn is blocked") && problem.Tile == (0, 0));
    }

    [Fact]
    public void ItemsMayStackOnOneTileAndSitOnASpawn()
    {
        StationMap map = Arena();
        map.PlaceEntity(StationEntityKind.Wrench, 0, 0);
        map.PlaceEntity(StationEntityKind.MetalSheets, 0, 0, amount: 2);
        Assert.Empty(StationMapValidator.Validate(map));
    }

    [Fact]
    public void OpenEdge_IsOnlyAWarning()
    {
        StationMap map = Arena();
        map.ClearWall(map.Bounds.MaxX, 0);
        StationMapProblem problem = Assert.Single(StationMapValidator.Validate(map));
        Assert.Equal(StationMapProblemSeverity.Warning, problem.Severity);
        Assert.Equal((map.Bounds.MaxX, 0), problem.Tile);
        Assert.True(StationMapValidator.IsPlayable(map));
    }

    [Fact]
    public void SealedOffSpawnAndEntity_AreWarnings()
    {
        StationMap map = Arena();
        for (int y = map.Bounds.MinY; y <= map.Bounds.MaxY; y++) map.SetWall(2, y);
        map.AddSpawn(3, 0);
        map.PlaceEntity(StationEntityKind.Wrench, 3, 1);
        map.PlaceEntity(StationEntityKind.Girder, -1, 1);

        var problems = StationMapValidator.Validate(map);
        Assert.Contains(problems, problem => !problem.IsError && problem.Message.Contains("Spawn cannot be reached") && problem.Tile == (3, 0));
        Assert.Contains(problems, problem => !problem.IsError && problem.Message.Contains("wrench#1 cannot be reached") && problem.Tile == (3, 1));
        Assert.DoesNotContain(problems, problem => problem.Message.Contains("girder"));
        Assert.True(StationMapValidator.IsPlayable(map));
    }

    [Fact]
    public void Airlock_DoesNotBlockReachability()
    {
        StationMap map = Arena();
        for (int y = map.Bounds.MinY; y <= map.Bounds.MaxY; y++) map.SetWall(2, y);
        map.ClearWall(2, 0);
        map.PlaceEntity(StationEntityKind.EngineeringAirlock, 2, 0);
        map.PlaceEntity(StationEntityKind.ApcLamp, 3, 1);
        Assert.Empty(StationMapValidator.Validate(map));
    }
}
