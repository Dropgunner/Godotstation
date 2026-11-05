using System.Collections.Generic;
using Godot;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Maps;

namespace Godotstation.Game.MapEditor;

/// <summary>Draws the map under edit in world units (one tile = one unit), plus hover, drag and problem overlays.</summary>
public partial class MapEditorCanvas : Node2D
{
    private static readonly Color WallColor = new("#24323b");
    private static readonly Color WallTopColor = new("#6c8591");
    private static readonly Color GridColor = new(1f, 1f, 1f, 0.07f);
    private static readonly Color BoundsColor = new("#9fc2d0");
    private static readonly Color SpawnColor = new("#7be495");
    private static readonly Color HoverColor = new(1f, 1f, 1f, 0.9f);
    private static readonly Color ErrorColor = new("#ff5c5c");
    private static readonly Color WarningColor = new("#ffd166");
    private static readonly Color DragColor = new(0.6f, 0.85f, 1f, 0.35f);

    private StationMap map = StationMaps.CreateDefault();
    private IReadOnlyList<StationMapProblem> problems = System.Array.Empty<StationMapProblem>();
    private Vector2I? hoverTile;
    private Rect2I? dragRect;

    public void SetMap(StationMap stationMap)
    {
        map = stationMap;
        QueueRedraw();
    }

    public void SetProblems(IReadOnlyList<StationMapProblem> mapProblems)
    {
        problems = mapProblems;
        QueueRedraw();
    }

    public void SetHover(Vector2I? tile)
    {
        if (hoverTile == tile) return;
        hoverTile = tile;
        QueueRedraw();
    }

    public void SetDragRect(Rect2I? rect)
    {
        dragRect = rect;
        QueueRedraw();
    }

    public override void _Draw()
    {
        StationMapBounds bounds = map.Bounds;
        var defaultFloor = new Color(StationMaps.DefaultFloorColor);
        Color defaultFloorAlt = defaultFloor.Darkened(0.06f);

        for (int y = bounds.MinY; y <= bounds.MaxY; y++)
        for (int x = bounds.MinX; x <= bounds.MaxX; x++)
        {
            StationMapRoom? room = map.GetRoomAt(x, y);
            Color floor = room != null && room.FloorColor != StationMaps.DefaultFloorColor
                ? new Color(room.FloorColor)
                : ((x + y) & 1) == 0 ? defaultFloor : defaultFloorAlt;
            DrawRect(new Rect2(x - 0.5f, y - 0.5f, 1f, 1f), floor, true);
        }

        for (int x = bounds.MinX; x <= bounds.MaxX + 1; x++)
            DrawLine(new Vector2(x - 0.5f, bounds.MinY - 0.5f), new Vector2(x - 0.5f, bounds.MaxY + 0.5f), GridColor, 0.03f);
        for (int y = bounds.MinY; y <= bounds.MaxY + 1; y++)
            DrawLine(new Vector2(bounds.MinX - 0.5f, y - 0.5f), new Vector2(bounds.MaxX + 0.5f, y - 0.5f), GridColor, 0.03f);

        foreach (var (x, y) in map.Walls)
        {
            DrawRect(new Rect2(x - 0.49f, y - 0.49f, 0.98f, 0.98f), WallColor, true);
            DrawRect(new Rect2(x - 0.49f, y - 0.49f, 0.98f, 0.12f), WallTopColor, true);
        }

        foreach (StationMapRoom room in map.Rooms)
        {
            var rect = new Rect2(room.MinX - 0.5f, room.MinY - 0.5f, room.MaxX - room.MinX + 1f, room.MaxY - room.MinY + 1f);
            var outline = new Color(room.LabelColor) { A = 0.55f };
            DrawRect(rect, outline, false, 0.06f);
            if (!string.IsNullOrWhiteSpace(room.Name))
                DrawString(ThemeDB.FallbackFont, new Vector2(room.MinX - 0.35f, room.MinY + 0.3f), room.Name,
                    HorizontalAlignment.Left, -1, 1, new Color(room.LabelColor));
        }

        for (int i = 0; i < map.Spawns.Count; i++)
        {
            var position = new Vector2(map.Spawns[i].X, map.Spawns[i].Y);
            DrawCircle(position, 0.34f, SpawnColor);
            DrawCircle(position, 0.22f, new Color("#1c3a27"));
            DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.18f, 0.22f), (i + 1).ToString(),
                HorizontalAlignment.Left, -1, 1, SpawnColor);
        }

        foreach (StationEntitySpawn entity in map.Entities)
        {
            var position = new Vector2(entity.Position.X, entity.Position.Y);
            Color tint = KindColor(entity.Kind);
            DrawRect(new Rect2(position - new Vector2(0.38f, 0.38f), new Vector2(0.76f, 0.76f)), tint, true);
            DrawRect(new Rect2(position - new Vector2(0.38f, 0.38f), new Vector2(0.76f, 0.76f)), tint.Darkened(0.45f), false, 0.06f);
            string label = Abbreviate(entity.Kind) + (entity.Amount > 0 ? $"×{entity.Amount}" : string.Empty);
            DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.3f, 0.2f), label,
                HorizontalAlignment.Left, -1, 1, new Color("#10181d"));
        }

        DrawRect(new Rect2(bounds.MinX - 0.5f, bounds.MinY - 0.5f, bounds.Width, bounds.Height), BoundsColor, false, 0.08f);

        foreach (StationMapProblem problem in problems)
        {
            if (problem.Tile is not { } tile) continue;
            DrawRect(new Rect2(tile.X - 0.46f, tile.Y - 0.46f, 0.92f, 0.92f), problem.IsError ? ErrorColor : WarningColor, false, 0.1f);
        }

        if (dragRect is { } drag)
        {
            DrawRect(new Rect2(drag.Position.X - 0.5f, drag.Position.Y - 0.5f, drag.Size.X, drag.Size.Y), DragColor, true);
            DrawRect(new Rect2(drag.Position.X - 0.5f, drag.Position.Y - 0.5f, drag.Size.X, drag.Size.Y), HoverColor, false, 0.06f);
        }

        if (hoverTile is { } hover)
            DrawRect(new Rect2(hover.X - 0.5f, hover.Y - 0.5f, 1f, 1f), HoverColor, false, 0.07f);
    }

    public static Color KindColor(string kind) => kind switch
    {
        StationEntityKind.Wrench => new Color("#f3c85d"),
        StationEntityKind.ServiceCart => new Color("#70cfa6"),
        StationEntityKind.MetalSheets => new Color("#aebbc2"),
        StationEntityKind.Girder => new Color("#8e7b5a"),
        StationEntityKind.EngineeringAirlock => new Color("#bb8b4c"),
        StationEntityKind.GeneratorFuel => new Color("#d5aa59"),
        StationEntityKind.FuelGenerator => new Color("#e18b54"),
        StationEntityKind.PowerStorage => new Color("#6397a4"),
        StationEntityKind.ApcLamp => new Color("#fff09a"),
        _ => new Color("#b06ab3"),
    };

    public static string Abbreviate(string kind)
    {
        string[] parts = kind.Split('-', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..System.Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant();
    }
}
