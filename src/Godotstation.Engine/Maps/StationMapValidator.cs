using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Interactions;

namespace Godotstation.Engine.Maps;

public enum StationMapProblemSeverity
{
    Warning,
    Error,
}

/// <summary>One thing wrong with a map, anchored to a tile when it concerns one.</summary>
public sealed record StationMapProblem(StationMapProblemSeverity Severity, string Message, (int X, int Y)? Tile = null)
{
    public bool IsError => Severity == StationMapProblemSeverity.Error;
    public override string ToString() => Tile is { } tile ? $"{Severity}: {Message} at ({tile.X}, {tile.Y})" : $"{Severity}: {Message}";
}

/// <summary>
/// Checks a map against the rules the shared round depends on. Errors mean the host must
/// refuse to start on this map; warnings are author guidance (unreachable objects, open edges).
/// </summary>
public static class StationMapValidator
{
    public static IReadOnlyList<StationMapProblem> Validate(StationMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var problems = new List<StationMapProblem>();

        if (string.IsNullOrWhiteSpace(map.Name))
            problems.Add(new StationMapProblem(StationMapProblemSeverity.Warning, "The map has no name."));
        if (!map.Bounds.IsValidSize)
            problems.Add(new StationMapProblem(StationMapProblemSeverity.Error,
                $"Map sides must be between {StationMap.MinimumSide} and {StationMap.MaximumSide} tiles."));

        if (map.Spawns.Count == 0)
            problems.Add(new StationMapProblem(StationMapProblemSeverity.Error, "The map needs at least one crew spawn."));
        foreach (Vector2 spawn in map.Spawns)
        {
            (int x, int y) = StationMap.ToTile(spawn);
            if (!map.Bounds.Contains(x, y))
                problems.Add(new StationMapProblem(StationMapProblemSeverity.Error, "Spawn is outside the map", (x, y)));
            else if (map.IsWall(x, y))
                problems.Add(new StationMapProblem(StationMapProblemSeverity.Error, "Spawn is inside a wall", (x, y)));
        }

        bool entitiesWellFormed = ValidateEntities(map, problems);
        IReadOnlyList<StationEntityState>? states = null;
        if (entitiesWellFormed)
        {
            try
            {
                states = SharedStationInventoryWorld.InitialStates(map.Entities);
            }
            catch (ArgumentException exception)
            {
                problems.Add(new StationMapProblem(StationMapProblemSeverity.Error, exception.Message));
            }
        }

        if (states != null)
        {
            var solidTiles = new Dictionary<(int X, int Y), StationEntityState>();
            foreach (StationEntityState state in states.Where(state => state.IsSolid))
            {
                (int X, int Y) tile = StationMap.ToTile(state.Position);
                if (solidTiles.TryGetValue(tile, out StationEntityState? other))
                    problems.Add(new StationMapProblem(StationMapProblemSeverity.Error,
                        $"Solid entities {other.Kind}#{other.Id} and {state.Kind}#{state.Id} share a tile", tile));
                else solidTiles[tile] = state;
                if (map.HasSpawnAt(tile.X, tile.Y))
                    problems.Add(new StationMapProblem(StationMapProblemSeverity.Error,
                        $"Spawn is blocked by {state.Kind}#{state.Id}", tile));
            }
            AddReachabilityProblems(map, states, problems);
        }

        for (int x = map.Bounds.MinX; x <= map.Bounds.MaxX; x++)
        {
            CheckEdge(map, x, map.Bounds.MinY, problems);
            CheckEdge(map, x, map.Bounds.MaxY, problems);
        }
        for (int y = map.Bounds.MinY + 1; y < map.Bounds.MaxY; y++)
        {
            CheckEdge(map, map.Bounds.MinX, y, problems);
            CheckEdge(map, map.Bounds.MaxX, y, problems);
        }

        return problems;
    }

    /// <summary>True when the map has no errors, so a host may start a round on it.</summary>
    public static bool IsPlayable(StationMap map) => Validate(map).All(problem => !problem.IsError);

    private static bool ValidateEntities(StationMap map, List<StationMapProblem> problems)
    {
        bool wellFormed = true;
        var seen = new HashSet<int>();
        foreach (StationEntitySpawn entity in map.Entities)
        {
            (int X, int Y) tile = StationMap.ToTile(entity.Position);
            if (entity.Id <= 0)
            {
                problems.Add(new StationMapProblem(StationMapProblemSeverity.Error, $"Entity {entity.Kind} has non-positive id {entity.Id}", tile));
                wellFormed = false;
            }
            else if (!seen.Add(entity.Id))
            {
                problems.Add(new StationMapProblem(StationMapProblemSeverity.Error, $"Entity id {entity.Id} is used more than once", tile));
                wellFormed = false;
            }
            if (!StationEntityKinds.TryGet(entity.Kind, out StationEntityKindInfo? info))
            {
                problems.Add(new StationMapProblem(StationMapProblemSeverity.Error, $"Unknown entity kind '{entity.Kind}'", tile));
                wellFormed = false;
                continue;
            }
            if (info.Stackable && entity.Amount < 1)
            {
                problems.Add(new StationMapProblem(StationMapProblemSeverity.Error, $"{info.DisplayName} #{entity.Id} needs an amount of at least 1", tile));
                wellFormed = false;
            }
            if (!map.Bounds.Contains(tile.X, tile.Y))
                problems.Add(new StationMapProblem(StationMapProblemSeverity.Error, $"{info.DisplayName} #{entity.Id} is outside the map", tile));
            else if (map.IsWall(tile.X, tile.Y))
                problems.Add(new StationMapProblem(StationMapProblemSeverity.Error, $"{info.DisplayName} #{entity.Id} is inside a wall", tile));
        }
        return wellFormed;
    }

    private static void CheckEdge(StationMap map, int x, int y, List<StationMapProblem> problems)
    {
        if (!map.IsWall(x, y))
            problems.Add(new StationMapProblem(StationMapProblemSeverity.Warning,
                "Edge tile is open; the round seals the boundary automatically", (x, y)));
    }

    /// <summary>Flood-fills walkable floor from the first spawn and reports what the crew could never reach.</summary>
    private static void AddReachabilityProblems(StationMap map, IReadOnlyList<StationEntityState> states, List<StationMapProblem> problems)
    {
        if (map.Spawns.Count == 0) return;
        var blocked = new HashSet<(int X, int Y)>();
        foreach (StationEntityState state in states)
            if (state.IsSolid && !state.IsPushable) blocked.Add(StationMap.ToTile(state.Position));

        (int X, int Y) origin = StationMap.ToTile(map.Spawns[0]);
        if (!map.Bounds.Contains(origin.X, origin.Y) || map.IsWall(origin.X, origin.Y)) return;
        var reachable = new HashSet<(int X, int Y)> { origin };
        var frontier = new Queue<(int X, int Y)>();
        frontier.Enqueue(origin);
        while (frontier.Count > 0)
        {
            (int x, int y) = frontier.Dequeue();
            foreach ((int dx, int dy) in Neighbours)
            {
                (int X, int Y) next = (x + dx, y + dy);
                if (!map.Bounds.Contains(next.X, next.Y) || map.IsWall(next.X, next.Y) || blocked.Contains(next) ||
                    !reachable.Add(next)) continue;
                frontier.Enqueue(next);
            }
        }

        for (int i = 1; i < map.Spawns.Count; i++)
        {
            (int X, int Y) tile = StationMap.ToTile(map.Spawns[i]);
            if (map.Bounds.Contains(tile.X, tile.Y) && !map.IsWall(tile.X, tile.Y) && !reachable.Contains(tile))
                problems.Add(new StationMapProblem(StationMapProblemSeverity.Warning, "Spawn cannot be reached from the first spawn", tile));
        }
        foreach (StationEntityState state in states)
        {
            (int X, int Y) tile = StationMap.ToTile(state.Position);
            if (!map.Bounds.Contains(tile.X, tile.Y) || map.IsWall(tile.X, tile.Y)) continue;
            bool adjacentReachable = reachable.Contains(tile) ||
                Neighbours.Any(step => reachable.Contains((tile.X + step.X, tile.Y + step.Y)));
            if (!adjacentReachable)
                problems.Add(new StationMapProblem(StationMapProblemSeverity.Warning,
                    $"{state.Kind}#{state.Id} cannot be reached from the first spawn", tile));
        }
    }

    private static readonly (int X, int Y)[] Neighbours = [(1, 0), (-1, 0), (0, 1), (0, -1)];
}
