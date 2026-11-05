using System.Linq;
using System.Numerics;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Maps;
using Godotstation.Engine.Movement;
using Xunit;

namespace Godotstation.Tests.Maps;

public sealed class StationMapTests
{
    [Fact]
    public void DefaultMap_MatchesHandCodedLayoutAndCatalog()
    {
        StationMap map = StationMaps.CreateDefault();

        Assert.Equal(StationPlaytestLayout.GetWallTiles().ToHashSet(), map.Walls.ToHashSet());
        Assert.Equal(StationPlaytestLayout.Spawns, map.Spawns);
        Assert.Equal(StationEntityCatalog.Playtest, map.Entities);

        CollisionMap expected = StationPlaytestLayout.CreateCollisionMap();
        CollisionMap actual = map.CreateCollisionMap();
        var state = new PlayerMovementState();
        for (int y = map.Bounds.MinY; y <= map.Bounds.MaxY; y++)
        for (int x = map.Bounds.MinX; x <= map.Bounds.MaxX; x++)
            Assert.Equal(expected.IsPassable(x, y, state), actual.IsPassable(x, y, state));
    }

    [Fact]
    public void DefaultMap_HasNoValidationProblems()
    {
        Assert.Empty(StationMapValidator.Validate(StationMaps.CreateDefault()));
    }

    [Fact]
    public void CollisionMap_SealsTheRingOutsideTheBounds()
    {
        StationMap map = StationMaps.CreateBlank(5, 5);
        map.ClearWall(map.Bounds.MaxX, 0);
        CollisionMap collision = map.CreateCollisionMap();
        var state = new PlayerMovementState();

        Assert.True(collision.IsPassable(map.Bounds.MaxX, 0, state));
        Assert.False(collision.IsPassable(map.Bounds.MaxX + 1, 0, state));
        Assert.False(collision.IsPassable(map.Bounds.MinX - 1, map.Bounds.MinY - 1, state));
    }

    [Fact]
    public void Edits_RespectBoundsAndDuplicates()
    {
        StationMap map = StationMaps.CreateBlank(7, 7);

        Assert.False(map.SetWall(99, 0));
        Assert.True(map.SetWall(1, 1));
        Assert.False(map.SetWall(1, 1));
        Assert.True(map.ClearWall(1, 1));
        Assert.False(map.ClearWall(1, 1));

        Assert.True(map.HasSpawnAt(0, 0));
        Assert.False(map.AddSpawn(0, 0));
        Assert.True(map.AddSpawn(1, 0));
        Assert.False(map.AddSpawn(50, 50));
        Assert.True(map.RemoveSpawnAt(1, 0));

        StationEntitySpawn? wrench = map.PlaceEntity(StationEntityKind.Wrench, 2, 2);
        Assert.NotNull(wrench);
        Assert.Equal(1, wrench.Id);
        Assert.Null(map.PlaceEntity(StationEntityKind.Wrench, 2, 2, id: 1));
        Assert.Null(map.PlaceEntity(StationEntityKind.Wrench, 40, 2));
        StationEntitySpawn? sheets = map.PlaceEntity(StationEntityKind.MetalSheets, 2, 2, amount: 2);
        Assert.Equal(2, sheets!.Id);
        Assert.Equal(sheets, map.GetEntityAt(2, 2));
        Assert.Equal(2, map.GetEntitiesAt(2, 2).Count());
        Assert.True(map.RemoveEntityAt(2, 2));
        Assert.Equal(wrench, map.GetEntityAt(2, 2));
        Assert.Equal(2, map.NextEntityId());

        Assert.True(map.AddRoom(new StationMapRoom("BRIDGE", 2, 2, -2, -2, "#112233", "#ffffff")));
        Assert.Equal(-2, map.Rooms[0].MinX);
        Assert.False(map.AddRoom(new StationMapRoom("VOID", 90, 90, 99, 99, "#112233", "#ffffff")));
        Assert.NotNull(map.GetRoomAt(0, 0));
        Assert.True(map.RemoveRoomAt(0, 0));
        Assert.False(map.RemoveRoomAt(0, 0));
    }

    [Fact]
    public void Resize_DropsOutsideContentAndClipsRooms()
    {
        StationMap map = StationMaps.CreateBlank(11, 11);
        map.PlaceEntity(StationEntityKind.Girder, 4, 4);
        map.PlaceEntity(StationEntityKind.Wrench, 0, 1);
        map.AddSpawn(4, 0);
        map.AddRoom(new StationMapRoom("WIDE", -5, -5, 5, 5, "#112233", "#ffffff"));
        map.AddRoom(new StationMapRoom("CORNER", 4, 4, 5, 5, "#112233", "#ffffff"));

        Assert.False(map.Resize(new StationMapBounds(0, 0, 1, 1)));
        Assert.True(map.Resize(new StationMapBounds(-2, -2, 2, 2)));

        Assert.Equal(new StationMapBounds(-2, -2, 2, 2), map.Bounds);
        Assert.Single(map.Entities);
        Assert.Single(map.Spawns);
        Assert.All(map.Walls, wall => Assert.True(map.Bounds.Contains(wall.X, wall.Y)));
        StationMapRoom room = Assert.Single(map.Rooms);
        Assert.Equal(("WIDE", -2, -2, 2, 2), (room.Name, room.MinX, room.MinY, room.MaxX, room.MaxY));
    }

    [Fact]
    public void FreeSpawn_SkipsOccupiedTilesInAuthorOrder()
    {
        StationMap map = StationMaps.CreateBlank(9, 9);
        map.AddSpawn(2, 0);
        map.AddSpawn(-2, 0);

        Assert.True(map.TryFindFreeSpawn(new[] { new Vector2(0.2f, -0.3f) }, out Vector2 spawn));
        Assert.Equal(new Vector2(2, 0), spawn);
        Assert.False(map.TryFindFreeSpawn(new[] { new Vector2(0, 0), new Vector2(2, 0), new Vector2(-2, 0) }, out _));
    }

    [Fact]
    public void Clone_IsIndependentAndContentEqual()
    {
        StationMap original = StationMaps.CreateDefault();
        StationMap copy = original.Clone();
        Assert.True(original.ContentEquals(copy));

        copy.SetWall(0, 0);
        Assert.False(original.IsWall(0, 0));
        Assert.False(original.ContentEquals(copy));
    }

    [Theory]
    [InlineData("Playtest Station", "playtest_station")]
    [InlineData("  My   Cool-Map!! ", "my_cool_map")]
    [InlineData("", "untitled_station")]
    [InlineData("###", "untitled_station")]
    public void FileStem_IsSafeAndStable(string name, string expected)
    {
        Assert.Equal(expected, StationMaps.ToFileStem(name));
    }
}
