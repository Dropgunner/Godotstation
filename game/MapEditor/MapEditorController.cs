using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Maps;
using Godotstation.Game.Maps;
using Godotstation.Game.Playtest;

namespace Godotstation.Game.MapEditor;

/// <summary>
/// Community map editor. Input and camera live here; every change goes through the engine's
/// <see cref="StationMapEditorSession"/>, so the rules of what a map may contain are the same
/// ones the host enforces when a round starts.
/// </summary>
public partial class MapEditorController : Node2D
{
    private const float MinimumZoom = 6f;
    private const float MaximumZoom = 72f;
    private const float KeyPanSpeed = 14f;

    private readonly StationMapEditorSession session = new(StationMaps.CreateDefault());
    private Camera2D camera = null!;
    private MapEditorCanvas canvas = null!;
    private MapEditorPanel panel = null!;
    private MapEditorTool tool = MapEditorTool.Wall;
    private IReadOnlyList<StationMapProblem> problems = Array.Empty<StationMapProblem>();
    private bool painting;
    private bool paintingPrimary;
    private bool panning;
    private Vector2I? dragStart;
    private Vector2I? hoverTile;
    private string currentPath = string.Empty;

    public StationMap Map => session.Map;

    public override void _Ready()
    {
        camera = new Camera2D { Name = "EditorCamera", Enabled = true };
        AddChild(camera);
        canvas = new MapEditorCanvas();
        AddChild(canvas);

        var layer = new CanvasLayer();
        AddChild(layer);
        panel = new MapEditorPanel();
        layer.AddChild(panel);
        panel.ToolSelected += SelectTool;
        panel.UndoRequested += () => { if (session.Undo()) panel.SetStatus("Undid the last change."); };
        panel.RedoRequested += () => { if (session.Redo()) panel.SetStatus("Redid the change."); };
        panel.SaveRequested += SaveMap;
        panel.LoadRequested += LoadMap;
        panel.NewRequested += NewMap;
        panel.ResizeRequested += ResizeMap;
        panel.HostRequested += HostMap;
        panel.BackRequested += () => GetTree().ChangeSceneToFile("res://UI/RoundLobby.tscn");
        panel.MetadataChanged += (name, author, description) => session.Edit(map =>
        {
            bool changed = map.Name != name || map.Author != author || map.Description != description;
            map.Name = name;
            map.Author = author;
            map.Description = description;
            return changed;
        });

        session.Changed += OnMapChanged;
        string startPath = PlaytestLaunchOptions.MapPath ?? StationMapLibrary.DefaultBundledPath;
        if (StationMapLibrary.TryLoad(startPath, out StationMap startMap, out _))
        {
            currentPath = startPath;
            session.Load(startMap);
        }
        else
        {
            session.Load(StationMaps.CreateDefault());
        }
        SelectTool(MapEditorTool.Wall);
        FrameMap();

        if (PlaytestLaunchOptions.MapEditorSmoke)
            CallDeferred(nameof(RunHeadlessSmoke));
    }

    private void OnMapChanged()
    {
        canvas.SetMap(session.Map);
        problems = StationMapValidator.Validate(session.Map);
        canvas.SetProblems(problems);
        panel.ShowProblems(problems);
        panel.ShowMap(session.Map);
        panel.SetHistory(session.CanUndo, session.CanRedo);
    }

    private void SelectTool(MapEditorTool selected)
    {
        tool = selected;
        panel.ShowTool(tool);
        panel.SetStatus(tool switch
        {
            MapEditorTool.Wall => "Wall: left-drag paints walls, right-drag clears them.",
            MapEditorTool.Floor => "Floor: drag to clear walls back to floor.",
            MapEditorTool.Spawn => "Spawn: click toggles a crew spawn. Order is join order.",
            MapEditorTool.Entity => "Entity: click places the selected kind, right-click removes the top entity.",
            MapEditorTool.Room => "Room: drag a rectangle, then release to name and tint it. Right-click removes a room.",
            _ => "Erase: click removes entity, then spawn, then wall; right-click removes a room.",
        });
    }

    private void FrameMap()
    {
        StationMapBounds bounds = session.Map.Bounds;
        camera.Position = new Vector2((bounds.MinX + bounds.MaxX) / 2f, (bounds.MinY + bounds.MaxY) / 2f);
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float usableWidth = Math.Max(200f, viewport.X - 380f);
        float zoom = Math.Min(usableWidth / (bounds.Width + 2f), viewport.Y / (bounds.Height + 2f));
        zoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        camera.Zoom = new Vector2(zoom, zoom);
        camera.Position += new Vector2(190f / zoom, 0f);
    }

    public override void _Process(double delta)
    {
        Vector2 pan = Vector2.Zero;
        if (Godot.Input.IsPhysicalKeyPressed(Key.A) || Godot.Input.IsPhysicalKeyPressed(Key.Left)) pan.X -= 1;
        if (Godot.Input.IsPhysicalKeyPressed(Key.D) || Godot.Input.IsPhysicalKeyPressed(Key.Right)) pan.X += 1;
        if (Godot.Input.IsPhysicalKeyPressed(Key.W) || Godot.Input.IsPhysicalKeyPressed(Key.Up)) pan.Y -= 1;
        if (Godot.Input.IsPhysicalKeyPressed(Key.S) || Godot.Input.IsPhysicalKeyPressed(Key.Down)) pan.Y += 1;
        if (pan != Vector2.Zero && GetViewport().GuiGetFocusOwner() == null)
            camera.Position += pan.Normalized() * KeyPanSpeed * (float)delta;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventMouseButton button:
                HandleMouseButton(button);
                break;
            case InputEventMouseMotion motion:
                HandleMouseMotion(motion);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key);
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton button)
    {
        Vector2I tile = TileUnderMouse();
        switch (button.ButtonIndex)
        {
            case MouseButton.WheelUp:
                if (button.Pressed) Zoom(1.15f);
                return;
            case MouseButton.WheelDown:
                if (button.Pressed) Zoom(1f / 1.15f);
                return;
            case MouseButton.Middle:
                panning = button.Pressed;
                return;
            case MouseButton.Left:
            case MouseButton.Right:
                bool primary = button.ButtonIndex == MouseButton.Left;
                if (button.Pressed) BeginAction(tile, primary);
                else EndAction(tile, primary);
                return;
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion motion)
    {
        if (panning)
        {
            camera.Position -= motion.Relative / camera.Zoom.X;
            return;
        }
        Vector2I tile = TileUnderMouse();
        if (hoverTile == tile) return;
        hoverTile = tile;
        canvas.SetHover(tile);
        panel.SetHover(DescribeTile(tile));
        if (painting && IsContinuous(tool)) ApplyToolAt(tile, paintingPrimary);
        if (dragStart is { } start) canvas.SetDragRect(RectBetween(start, tile));
    }

    private void HandleKey(InputEventKey key)
    {
        if (GetViewport().GuiGetFocusOwner() != null) return;
        if (key.CtrlPressed || key.MetaPressed)
        {
            if (key.Keycode == Key.Z && key.ShiftPressed) { session.Redo(); return; }
            if (key.Keycode == Key.Z) { session.Undo(); return; }
            if (key.Keycode == Key.Y) { session.Redo(); return; }
            if (key.Keycode == Key.S) { SaveMap(string.Empty); return; }
            return;
        }
        MapEditorTool? selected = key.Keycode switch
        {
            Key.Key1 => MapEditorTool.Wall,
            Key.Key2 => MapEditorTool.Floor,
            Key.Key3 => MapEditorTool.Spawn,
            Key.Key4 => MapEditorTool.Entity,
            Key.Key5 => MapEditorTool.Room,
            Key.Key6 => MapEditorTool.Erase,
            _ => null,
        };
        if (selected is { } choice) SelectTool(choice);
        else if (key.Keycode == Key.F) FrameMap();
    }

    private void Zoom(float factor)
    {
        float zoom = Math.Clamp(camera.Zoom.X * factor, MinimumZoom, MaximumZoom);
        camera.Zoom = new Vector2(zoom, zoom);
    }

    private Vector2I TileUnderMouse()
    {
        Vector2 world = GetGlobalMousePosition();
        return new Vector2I(Mathf.RoundToInt(world.X), Mathf.RoundToInt(world.Y));
    }

    private static bool IsContinuous(MapEditorTool tool) => tool is MapEditorTool.Wall or MapEditorTool.Floor or MapEditorTool.Erase;

    private static Rect2I RectBetween(Vector2I a, Vector2I b)
    {
        var min = new Vector2I(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
        var max = new Vector2I(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        return new Rect2I(min, max - min + Vector2I.One);
    }

    private void BeginAction(Vector2I tile, bool primary)
    {
        if (tool == MapEditorTool.Room && primary)
        {
            dragStart = tile;
            canvas.SetDragRect(RectBetween(tile, tile));
            return;
        }
        if (IsContinuous(tool))
        {
            painting = true;
            paintingPrimary = primary;
            session.BeginStroke();
        }
        ApplyToolAt(tile, primary);
    }

    private void EndAction(Vector2I tile, bool primary)
    {
        if (painting && primary == paintingPrimary)
        {
            painting = false;
            session.EndStroke();
        }
        if (tool == MapEditorTool.Room && primary && dragStart is { } start)
        {
            dragStart = null;
            canvas.SetDragRect(null);
            PlaceRoom(start, tile);
        }
    }

    /// <summary>Applies the active tool to one tile. Exposed so the headless smoke drives the same path as the mouse.</summary>
    public bool ApplyToolAt(Vector2I tile, bool primary)
    {
        int x = tile.X, y = tile.Y;
        switch (tool)
        {
            case MapEditorTool.Wall:
                return session.Edit(map => primary ? map.SetWall(x, y) : map.ClearWall(x, y));
            case MapEditorTool.Floor:
                return session.Edit(map => map.ClearWall(x, y));
            case MapEditorTool.Spawn:
                return session.Edit(map => primary && !map.HasSpawnAt(x, y) ? map.AddSpawn(x, y) : map.RemoveSpawnAt(x, y));
            case MapEditorTool.Entity:
                if (!primary) return session.Edit(map => map.RemoveEntityAt(x, y));
                string kind = panel.SelectedKind;
                int amount = panel.SelectedAmount;
                return session.Edit(map => map.PlaceEntity(kind, x, y, amount) != null);
            case MapEditorTool.Room:
                return !primary && session.Edit(map => map.RemoveRoomAt(x, y));
            case MapEditorTool.Erase:
                if (!primary) return session.Edit(map => map.RemoveRoomAt(x, y));
                return session.Edit(map => map.RemoveEntityAt(x, y) || map.RemoveSpawnAt(x, y) || map.ClearWall(x, y));
            default:
                return false;
        }
    }

    public bool PlaceRoom(Vector2I from, Vector2I to)
    {
        var room = new StationMapRoom(panel.RoomName, from.X, from.Y, to.X, to.Y, panel.RoomFloorColor, panel.RoomLabelColor);
        return session.Edit(map => map.AddRoom(room));
    }

    private string DescribeTile(Vector2I tile)
    {
        StationMap map = session.Map;
        if (!map.Bounds.Contains(tile.X, tile.Y)) return $"({tile.X}, {tile.Y}) outside the map";
        var parts = new List<string> { map.IsWall(tile.X, tile.Y) ? "wall" : "floor" };
        if (map.HasSpawnAt(tile.X, tile.Y)) parts.Add("spawn");
        foreach (StationEntitySpawn entity in map.GetEntitiesAt(tile.X, tile.Y))
            parts.Add($"{entity.Kind}#{entity.Id}" + (entity.Amount > 0 ? $"×{entity.Amount}" : string.Empty));
        if (map.GetRoomAt(tile.X, tile.Y) is { } room && room.Name.Length > 0) parts.Add(room.Name);
        return $"({tile.X}, {tile.Y}) " + string.Join(" · ", parts);
    }

    private void SaveMap(string stem)
    {
        string fileStem = string.IsNullOrWhiteSpace(stem) ? session.Map.Name : stem;
        if (!StationMapLibrary.TrySave(session.Map, fileStem, out string path, out string error))
        {
            panel.SetStatus(error);
            GD.PrintErr($"MAP_EDITOR_SAVE_FAILED reason=\"{error}\"");
            return;
        }
        currentPath = path;
        session.MarkSaved();
        panel.RefreshMapList();
        panel.SetStatus($"Saved {path} ({ProjectSettings.GlobalizePath(path)})");
        GD.Print($"MAP_EDITOR_SAVED path={path}");
    }

    private void LoadMap(string path)
    {
        if (!StationMapLibrary.TryLoad(path, out StationMap map, out string error))
        {
            panel.SetStatus(error);
            return;
        }
        currentPath = path;
        session.Load(map);
        FrameMap();
        panel.SetStatus($"Loaded {path}");
    }

    private void NewMap(int width, int height)
    {
        session.Load(StationMaps.CreateBlank(width, height), dirty: true);
        currentPath = string.Empty;
        FrameMap();
        panel.SetStatus($"New {width}×{height} station. Place walls, at least one spawn, and some objects.");
    }

    private void ResizeMap(int width, int height)
    {
        int minX = -(width / 2), minY = -(height / 2);
        var bounds = new StationMapBounds(minX, minY, minX + width - 1, minY + height - 1);
        if (session.Edit(map => map.Bounds != bounds && map.Resize(bounds)))
        {
            FrameMap();
            panel.SetStatus($"Resized to {width}×{height}. Content outside the new bounds was dropped.");
        }
    }

    private void HostMap()
    {
        if (problems.Any(problem => problem.IsError))
        {
            panel.SetStatus("Fix the validation errors before hosting this map.");
            return;
        }
        if (session.IsDirty) SaveMap(string.Empty);
        PlaytestLaunchOptions.Map = session.Map.Clone();
        PlaytestLaunchOptions.MapPath = currentPath.Length > 0 ? currentPath : null;
        PlaytestLaunchOptions.IsHost = true;
        GetTree().ChangeSceneToFile("res://World/StationPlaytest.tscn");
    }

    /// <summary>Drives the editor through a scripted authoring session so CI can prove it works without a display.</summary>
    private void RunHeadlessSmoke()
    {
        try
        {
            NewMap(15, 11);
            SelectTool(MapEditorTool.Wall);
            session.BeginStroke();
            for (int x = -4; x <= 4; x++) ApplyToolAt(new Vector2I(x, -2), primary: true);
            session.EndStroke();
            int undoDepthAfterStroke = session.UndoDepth;
            SelectTool(MapEditorTool.Floor);
            ApplyToolAt(new Vector2I(0, -2), primary: true);
            SelectTool(MapEditorTool.Spawn);
            ApplyToolAt(new Vector2I(3, 3), primary: true);
            SelectTool(MapEditorTool.Entity);
            panel.SelectKind(StationEntityKind.Wrench, 0);
            ApplyToolAt(new Vector2I(2, 2), primary: true);
            panel.SelectKind(StationEntityKind.MetalSheets, 2);
            ApplyToolAt(new Vector2I(2, 3), primary: true);
            panel.SelectKind(StationEntityKind.Girder, 0);
            ApplyToolAt(new Vector2I(-3, 3), primary: true);
            SelectTool(MapEditorTool.Room);
            panel.SetRoomOptions("SMOKE BAY", "#475b64", "#b8efff");
            PlaceRoom(new Vector2I(-5, -4), new Vector2I(5, 4));
            SelectTool(MapEditorTool.Erase);
            ApplyToolAt(new Vector2I(-3, 3), primary: true);

            bool undone = session.Undo() && session.Map.GetEntityAt(-3, 3) != null;
            bool redone = session.Redo() && session.Map.GetEntityAt(-3, 3) == null;
            session.Edit(map => { map.Name = "Smoke Map"; map.Author = "ci"; return true; });

            SaveMap("smoke_map");
            string savedPath = StationMapLibrary.UserPathFor("smoke_map");
            bool reloaded = StationMapLibrary.TryLoad(savedPath, out StationMap loaded, out string loadError);
            bool roundTrip = reloaded && loaded.ContentEquals(session.Map);
            int errors = problems.Count(problem => problem.IsError);
            bool ok = undoDepthAfterStroke == 1 && undone && redone && roundTrip && errors == 0 &&
                      session.Map.WallCount == 8 + 2 * (15 + 11 - 2) && session.Map.Spawns.Count == 2 &&
                      session.Map.Entities.Count == 2 && session.Map.Rooms.Count == 1;
            GD.Print($"{(ok ? "MAP_EDITOR_SMOKE_OK" : "MAP_EDITOR_SMOKE_FAILED")} walls={session.Map.WallCount} spawns={session.Map.Spawns.Count} " +
                     $"entities={session.Map.Entities.Count} rooms={session.Map.Rooms.Count} errors={errors} warnings={problems.Count - errors} " +
                     $"undo_depth={undoDepthAfterStroke} undo={undone} redo={redone} roundtrip={roundTrip} path={savedPath}" +
                     (reloaded ? string.Empty : $" load_error=\"{loadError}\""));
        }
        catch (Exception exception)
        {
            GD.PrintErr($"MAP_EDITOR_SMOKE_FAILED exception={exception}");
        }
        GetTree().Quit();
    }
}
