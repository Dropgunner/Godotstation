using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Movement;

namespace Godotstation.Engine.Maps;

/// <summary>Well-known maps and naming helpers shared by the game and the editor.</summary>
public static class StationMaps
{
    public const string FileExtension = ".json";
    public const string DefaultMapFileName = "playtest_station";
    public const string DefaultFloorColor = "#485965";
    public const string DefaultLabelColor = "#d1e2e8";

    /// <summary>
    /// The Wave 9 playtest station expressed as a map document. It is built from the same
    /// hand-coded layout and catalog the earlier playtests used, so rounds on the default map
    /// keep every existing smoke and fidelity test valid.
    /// </summary>
    public static StationMap CreateDefault()
    {
        var map = new StationMap(new StationMapBounds(StationPlaytestLayout.MinX, StationPlaytestLayout.MinY,
            StationPlaytestLayout.MaxX, StationPlaytestLayout.MaxY))
        {
            Name = "Playtest Station",
            Author = "Godotstation",
            Description = "Five-room LAN playtest station: medical, security and engineering above a central hall, crew quarters below.",
        };
        foreach ((int x, int y) in StationPlaytestLayout.GetWallTiles())
            map.SetWall(x, y);
        foreach (Vector2 spawn in StationPlaytestLayout.Spawns)
            map.AddSpawn((int)spawn.X, (int)spawn.Y);
        foreach (StationEntitySpawn entity in StationEntityCatalog.Playtest)
            map.PlaceEntity(entity.Kind, (int)entity.Position.X, (int)entity.Position.Y, entity.Amount, entity.Id);
        map.AddRoom(new StationMapRoom("MEDICAL", -11, -8, -5, -5, "#475b64", "#b8efff"));
        map.AddRoom(new StationMapRoom("SECURITY", -3, -8, 3, -5, "#54525d", "#ffd1bd"));
        map.AddRoom(new StationMapRoom("ENGINEERING", 5, -8, 11, -5, "#5b5848", "#ffe1a8"));
        map.AddRoom(new StationMapRoom("CENTRAL HALL", -11, -3, 11, 3, DefaultFloorColor, DefaultLabelColor));
        map.AddRoom(new StationMapRoom("CREW QUARTERS", -11, 5, 11, 8, "#484d5a", "#ded9ff"));
        return map;
    }

    /// <summary>A sealed empty station of the given size, centred on the origin, ready for an author.</summary>
    public static StationMap CreateBlank(int width, int height, string name = "New Station")
    {
        width = Math.Clamp(width, StationMap.MinimumSide, StationMap.MaximumSide);
        height = Math.Clamp(height, StationMap.MinimumSide, StationMap.MaximumSide);
        int minX = -(width / 2), minY = -(height / 2);
        StationMap map = StationMap.CreateEmpty(new StationMapBounds(minX, minY, minX + width - 1, minY + height - 1));
        map.Name = name;
        map.AddSpawn(0, 0);
        return map;
    }

    /// <summary>A safe file stem for a map name: lowercase ASCII letters, digits and underscores.</summary>
    public static string ToFileStem(string? name)
    {
        var stem = new StringBuilder();
        foreach (char character in (name ?? string.Empty).Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character)) stem.Append(character);
            else if ((character == ' ' || character == '-' || character == '_') && stem.Length > 0 && stem[^1] != '_') stem.Append('_');
        }
        string result = stem.ToString().TrimEnd('_');
        return result.Length == 0 ? "untitled_station" : result;
    }
}
