using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Movement;

namespace Godotstation.Engine.Maps;

/// <summary>Inclusive tile rectangle a station occupies.</summary>
public sealed record StationMapBounds(int MinX, int MinY, int MaxX, int MaxY)
{
    public int Width => MaxX - MinX + 1;
    public int Height => MaxY - MinY + 1;

    public bool Contains(int x, int y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    public bool IsEdge(int x, int y) => Contains(x, y) && (x == MinX || x == MaxX || y == MinY || y == MaxY);

    public bool IsValidSize =>
        Width >= StationMap.MinimumSide && Height >= StationMap.MinimumSide &&
        Width <= StationMap.MaximumSide && Height <= StationMap.MaximumSide;
}

/// <summary>A labelled floor area. Rooms only affect presentation: floor tint and the department label.</summary>
public sealed record StationMapRoom(string Name, int MinX, int MinY, int MaxX, int MaxY, string FloorColor, string LabelColor)
{
    public bool Contains(int x, int y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    public float CenterX => (MinX + MaxX) / 2f;
    public float CenterY => (MinY + MaxY) / 2f;

    public StationMapRoom Normalized() => new(Name, Math.Min(MinX, MaxX), Math.Min(MinY, MaxY),
        Math.Max(MinX, MaxX), Math.Max(MinY, MaxY), FloorColor, LabelColor);
}

/// <summary>
/// One editable station layout: the tile bounds, wall tiles, ordered crew spawn points,
/// starting entities and presentation rooms. This is the whole input a shared round needs,
/// so the host can send it to joining clients and community authors can save it as a file.
/// </summary>
public sealed class StationMap
{
    public const int FormatVersion = 1;
    public const int MinimumSide = 3;
    public const int MaximumSide = 256;
    public const int MaximumEntities = 4096;
    public const int MaximumSpawns = 256;
    public const int MaximumRooms = 256;
    public const int MaximumNameLength = 64;

    private readonly HashSet<(int X, int Y)> walls = new();
    private readonly List<Vector2> spawns = new();
    private readonly List<StationEntitySpawn> entities = new();
    private readonly List<StationMapRoom> rooms = new();

    public StationMap(StationMapBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (!bounds.IsValidSize)
            throw new ArgumentException($"Map sides must be between {MinimumSide} and {MaximumSide} tiles.", nameof(bounds));
        Bounds = bounds;
    }

    public string Name { get; set; } = "Untitled Station";
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public StationMapBounds Bounds { get; private set; }

    /// <summary>Wall tiles in row-major order, so two maps with the same walls list them identically.</summary>
    public IReadOnlyList<(int X, int Y)> Walls
    {
        get
        {
            var ordered = new List<(int X, int Y)>(walls);
            ordered.Sort(static (a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            return ordered;
        }
    }

    public int WallCount => walls.Count;
    public IReadOnlyList<Vector2> Spawns => spawns;
    public IReadOnlyList<StationEntitySpawn> Entities => entities;
    public IReadOnlyList<StationMapRoom> Rooms => rooms;

    /// <summary>A sealed empty station: perimeter walls around an open floor.</summary>
    public static StationMap CreateEmpty(StationMapBounds bounds)
    {
        var map = new StationMap(bounds);
        for (int x = bounds.MinX; x <= bounds.MaxX; x++)
        {
            map.walls.Add((x, bounds.MinY));
            map.walls.Add((x, bounds.MaxY));
        }
        for (int y = bounds.MinY; y <= bounds.MaxY; y++)
        {
            map.walls.Add((bounds.MinX, y));
            map.walls.Add((bounds.MaxX, y));
        }
        return map;
    }

    public bool IsWall(int x, int y) => walls.Contains((x, y));

    public bool SetWall(int x, int y) => Bounds.Contains(x, y) && walls.Add((x, y));

    public bool ClearWall(int x, int y) => walls.Remove((x, y));

    public bool HasSpawnAt(int x, int y) => spawns.Any(spawn => ToTile(spawn) == (x, y));

    public bool AddSpawn(int x, int y)
    {
        if (!Bounds.Contains(x, y) || HasSpawnAt(x, y) || spawns.Count >= MaximumSpawns) return false;
        spawns.Add(new Vector2(x, y));
        return true;
    }

    public bool RemoveSpawnAt(int x, int y) => spawns.RemoveAll(spawn => ToTile(spawn) == (x, y)) > 0;

    public StationEntitySpawn? GetEntityAt(int x, int y) =>
        entities.LastOrDefault(entity => ToTile(entity.Position) == (x, y));

    public IEnumerable<StationEntitySpawn> GetEntitiesAt(int x, int y) =>
        entities.Where(entity => ToTile(entity.Position) == (x, y));

    public StationEntitySpawn? GetEntity(int id) => entities.FirstOrDefault(entity => entity.Id == id);

    /// <summary>The smallest positive id no entity uses yet.</summary>
    public int NextEntityId()
    {
        int next = 1;
        foreach (StationEntitySpawn entity in entities)
            if (entity.Id >= next) next = entity.Id + 1;
        return next;
    }

    /// <summary>Places an entity, assigning a fresh id unless one is given. Returns null when the tile is outside the map or the id is taken.</summary>
    public StationEntitySpawn? PlaceEntity(string kind, int x, int y, int amount = 0, int? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (!Bounds.Contains(x, y) || entities.Count >= MaximumEntities) return null;
        int entityId = id ?? NextEntityId();
        if (entityId <= 0 || GetEntity(entityId) != null) return null;
        var spawn = new StationEntitySpawn(entityId, kind, new Vector2(x, y), amount);
        entities.Add(spawn);
        return spawn;
    }

    public bool RemoveEntity(int id) => entities.RemoveAll(entity => entity.Id == id) > 0;

    /// <summary>Removes the topmost entity on a tile.</summary>
    public bool RemoveEntityAt(int x, int y)
    {
        StationEntitySpawn? entity = GetEntityAt(x, y);
        return entity != null && entities.Remove(entity);
    }

    public bool AddRoom(StationMapRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);
        if (rooms.Count >= MaximumRooms) return false;
        StationMapRoom normalized = room.Normalized();
        if (!Bounds.Contains(normalized.MinX, normalized.MinY) && !Bounds.Contains(normalized.MaxX, normalized.MaxY))
            return false;
        rooms.Add(normalized);
        return true;
    }

    /// <summary>The topmost room covering a tile, if any.</summary>
    public StationMapRoom? GetRoomAt(int x, int y) => rooms.LastOrDefault(room => room.Contains(x, y));

    public bool RemoveRoomAt(int x, int y)
    {
        StationMapRoom? room = GetRoomAt(x, y);
        return room != null && rooms.Remove(room);
    }

    public bool RemoveRoom(StationMapRoom room) => rooms.Remove(room);

    /// <summary>
    /// Changes the bounds, dropping walls, spawns and entities that fall outside and clipping rooms.
    /// Fails without changing anything when the new size is out of range.
    /// </summary>
    public bool Resize(StationMapBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (!bounds.IsValidSize) return false;
        Bounds = bounds;
        walls.RemoveWhere(tile => !bounds.Contains(tile.X, tile.Y));
        spawns.RemoveAll(spawn => !bounds.Contains(ToTile(spawn).X, ToTile(spawn).Y));
        entities.RemoveAll(entity => !bounds.Contains(ToTile(entity.Position).X, ToTile(entity.Position).Y));
        for (int i = rooms.Count - 1; i >= 0; i--)
        {
            StationMapRoom room = rooms[i];
            int minX = Math.Max(room.MinX, bounds.MinX), minY = Math.Max(room.MinY, bounds.MinY);
            int maxX = Math.Min(room.MaxX, bounds.MaxX), maxY = Math.Min(room.MaxY, bounds.MaxY);
            if (minX > maxX || minY > maxY) rooms.RemoveAt(i);
            else rooms[i] = room with { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };
        }
        return true;
    }

    /// <summary>
    /// Static terrain for movement: the wall tiles plus a sealed ring one tile outside the
    /// bounds, so no author can accidentally let the crew walk off the station.
    /// </summary>
    public CollisionMap CreateCollisionMap()
    {
        var map = new CollisionMap();
        foreach ((int x, int y) in walls)
            map.SetTile(x, y, new SolidWall());
        for (int x = Bounds.MinX - 1; x <= Bounds.MaxX + 1; x++)
        {
            map.SetTile(x, Bounds.MinY - 1, new SolidWall());
            map.SetTile(x, Bounds.MaxY + 1, new SolidWall());
        }
        for (int y = Bounds.MinY - 1; y <= Bounds.MaxY + 1; y++)
        {
            map.SetTile(Bounds.MinX - 1, y, new SolidWall());
            map.SetTile(Bounds.MaxX + 1, y, new SolidWall());
        }
        return map;
    }

    /// <summary>
    /// Picks the first spawn point whose tile no crew member stands on, so a late joiner
    /// never lands on a player who is still at their own spawn.
    /// </summary>
    public bool TryFindFreeSpawn(IEnumerable<Vector2> occupiedPositions, out Vector2 spawn)
    {
        ArgumentNullException.ThrowIfNull(occupiedPositions);
        var occupied = new HashSet<(int X, int Y)>();
        foreach (Vector2 position in occupiedPositions)
            occupied.Add(ToTile(position));
        foreach (Vector2 candidate in spawns)
        {
            if (occupied.Contains(ToTile(candidate))) continue;
            spawn = candidate;
            return true;
        }
        spawn = default;
        return false;
    }

    public StationMap Clone()
    {
        var copy = new StationMap(Bounds) { Name = Name, Author = Author, Description = Description };
        copy.walls.UnionWith(walls);
        copy.spawns.AddRange(spawns);
        copy.entities.AddRange(entities);
        copy.rooms.AddRange(rooms);
        return copy;
    }

    /// <summary>True when both maps describe the same station, ignoring wall insertion order.</summary>
    public bool ContentEquals(StationMap? other) =>
        other != null && Name == other.Name && Author == other.Author && Description == other.Description &&
        Bounds == other.Bounds && walls.SetEquals(other.walls) && spawns.SequenceEqual(other.spawns) &&
        entities.SequenceEqual(other.entities) && rooms.SequenceEqual(other.rooms);

    public static (int X, int Y) ToTile(Vector2 position) =>
        ((int)MathF.Round(position.X), (int)MathF.Round(position.Y));
}
