using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Maps;
using Godotstation.Game.Maps;

namespace Godotstation.Game.MapEditor;

/// <summary>The editor's side panel: tool palette, entity and room options, metadata, file actions and the problem list.</summary>
public partial class MapEditorPanel : PanelContainer
{
    private readonly Dictionary<MapEditorTool, Button> toolButtons = new();
    private LineEdit nameInput = null!;
    private LineEdit authorInput = null!;
    private LineEdit descriptionInput = null!;
    private SpinBox widthInput = null!;
    private SpinBox heightInput = null!;
    private OptionButton kindOption = null!;
    private SpinBox amountInput = null!;
    private Label kindSummary = null!;
    private LineEdit roomNameInput = null!;
    private ColorPickerButton roomFloorColor = null!;
    private ColorPickerButton roomLabelColor = null!;
    private Button undoButton = null!;
    private Button redoButton = null!;
    private LineEdit fileStemInput = null!;
    private OptionButton loadOption = null!;
    private RichTextLabel problemsLabel = null!;
    private Label statusLabel = null!;
    private Label hoverLabel = null!;
    private bool suppressMetadataEvents;

    public event Action<MapEditorTool>? ToolSelected;
    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action<string>? SaveRequested;
    public event Action<string>? LoadRequested;
    public event Action<int, int>? NewRequested;
    public event Action<int, int>? ResizeRequested;
    public event Action? HostRequested;
    public event Action? BackRequested;
    public event Action<string, string, string>? MetadataChanged;

    public string SelectedKind => kindOption.Selected >= 0 ? kindOption.GetItemMetadata(kindOption.Selected).AsString() : StationEntityKind.Wrench;
    public int SelectedAmount => (int)amountInput.Value;
    public string RoomName => roomNameInput.Text.Trim();
    public string RoomFloorColor => "#" + roomFloorColor.Color.ToHtml(false);
    public string RoomLabelColor => "#" + roomLabelColor.Color.ToHtml(false);

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(360, 0);
        SetAnchorsAndOffsetsPreset(LayoutPreset.RightWide);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(content);

        var title = new Label { Text = "MAP EDITOR", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        content.AddChild(title);
        hoverLabel = new Label { Text = "Hover a tile", HorizontalAlignment = HorizontalAlignment.Center };
        content.AddChild(hoverLabel);

        content.AddChild(Heading("Tools  (keys 1–6 · drag paints · right-click erases)"));
        var toolGrid = new GridContainer { Columns = 3 };
        content.AddChild(toolGrid);
        foreach (MapEditorTool tool in Enum.GetValues<MapEditorTool>())
        {
            var button = new Button { Text = tool.ToString(), ToggleMode = true, CustomMinimumSize = new Vector2(100, 34) };
            MapEditorTool captured = tool;
            button.Pressed += () => ToolSelected?.Invoke(captured);
            toolButtons[tool] = button;
            toolGrid.AddChild(button);
        }

        content.AddChild(Heading("Entity tool"));
        kindOption = new OptionButton();
        int kindIndex = 0;
        foreach (StationEntityKindInfo info in StationEntityKinds.All)
        {
            kindOption.AddItem(info.DisplayName, kindIndex);
            kindOption.SetItemMetadata(kindIndex, info.Kind);
            kindIndex++;
        }
        kindOption.Select(0);
        kindOption.ItemSelected += _ => RefreshKindSummary();
        content.AddChild(kindOption);
        var amountRow = new HBoxContainer();
        content.AddChild(amountRow);
        amountRow.AddChild(new Label { Text = "Stack amount:" });
        amountInput = new SpinBox { MinValue = 0, MaxValue = 999, Value = 0, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        amountRow.AddChild(amountInput);
        kindSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        content.AddChild(kindSummary);
        RefreshKindSummary();

        content.AddChild(Heading("Room tool  (drag a rectangle)"));
        roomNameInput = new LineEdit { PlaceholderText = "Room name (e.g. MEDBAY)", Text = "NEW ROOM" };
        content.AddChild(roomNameInput);
        var colorRow = new HBoxContainer();
        content.AddChild(colorRow);
        colorRow.AddChild(new Label { Text = "Floor" });
        roomFloorColor = new ColorPickerButton { Color = new Color("#475b64"), CustomMinimumSize = new Vector2(90, 30), EditAlpha = false };
        colorRow.AddChild(roomFloorColor);
        colorRow.AddChild(new Label { Text = "Label" });
        roomLabelColor = new ColorPickerButton { Color = new Color("#b8efff"), CustomMinimumSize = new Vector2(90, 30), EditAlpha = false };
        colorRow.AddChild(roomLabelColor);

        content.AddChild(Heading("Map"));
        nameInput = new LineEdit { PlaceholderText = "Map name" };
        nameInput.TextChanged += _ => EmitMetadata();
        content.AddChild(nameInput);
        authorInput = new LineEdit { PlaceholderText = "Author" };
        authorInput.TextChanged += _ => EmitMetadata();
        content.AddChild(authorInput);
        descriptionInput = new LineEdit { PlaceholderText = "Description" };
        descriptionInput.TextChanged += _ => EmitMetadata();
        content.AddChild(descriptionInput);
        var sizeRow = new HBoxContainer();
        content.AddChild(sizeRow);
        sizeRow.AddChild(new Label { Text = "W" });
        widthInput = new SpinBox { MinValue = StationMap.MinimumSide, MaxValue = StationMap.MaximumSide, Value = 25, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sizeRow.AddChild(widthInput);
        sizeRow.AddChild(new Label { Text = "H" });
        heightInput = new SpinBox { MinValue = StationMap.MinimumSide, MaxValue = StationMap.MaximumSide, Value = 19, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sizeRow.AddChild(heightInput);
        var resizeButton = new Button { Text = "Resize" };
        resizeButton.Pressed += () => ResizeRequested?.Invoke((int)widthInput.Value, (int)heightInput.Value);
        sizeRow.AddChild(resizeButton);
        var newButton = new Button { Text = "New blank" };
        newButton.Pressed += () => NewRequested?.Invoke((int)widthInput.Value, (int)heightInput.Value);
        sizeRow.AddChild(newButton);

        var historyRow = new HBoxContainer();
        content.AddChild(historyRow);
        undoButton = new Button { Text = "Undo (Ctrl+Z)", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        undoButton.Pressed += () => UndoRequested?.Invoke();
        historyRow.AddChild(undoButton);
        redoButton = new Button { Text = "Redo (Ctrl+Y)", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        redoButton.Pressed += () => RedoRequested?.Invoke();
        historyRow.AddChild(redoButton);

        content.AddChild(Heading("Files  (saved to user://maps)"));
        fileStemInput = new LineEdit { PlaceholderText = "file name" };
        content.AddChild(fileStemInput);
        var saveButton = new Button { Text = "Save (Ctrl+S)" };
        saveButton.Pressed += () => SaveRequested?.Invoke(fileStemInput.Text);
        content.AddChild(saveButton);
        var loadRow = new HBoxContainer();
        content.AddChild(loadRow);
        loadOption = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        loadRow.AddChild(loadOption);
        var loadButton = new Button { Text = "Load" };
        loadButton.Pressed += () =>
        {
            if (loadOption.Selected >= 0) LoadRequested?.Invoke(loadOption.GetItemMetadata(loadOption.Selected).AsString());
        };
        loadRow.AddChild(loadButton);
        RefreshMapList();

        content.AddChild(Heading("Play"));
        var hostButton = new Button { Text = "Host a LAN shift on this map" };
        hostButton.Pressed += () => HostRequested?.Invoke();
        content.AddChild(hostButton);
        var backButton = new Button { Text = "Back to lobby" };
        backButton.Pressed += () => BackRequested?.Invoke();
        content.AddChild(backButton);

        content.AddChild(Heading("Validation"));
        problemsLabel = new RichTextLabel { FitContent = true, BbcodeEnabled = true, CustomMinimumSize = new Vector2(0, 120), ScrollActive = false };
        content.AddChild(problemsLabel);

        statusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Text = "Camera: WASD/arrows or middle-drag · wheel zooms" };
        content.AddChild(statusLabel);
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", new Color("#9fc2d0"));
        return label;
    }

    private void RefreshKindSummary()
    {
        if (!StationEntityKinds.TryGet(SelectedKind, out StationEntityKindInfo? info)) return;
        kindSummary.Text = info.Summary + (info.Stackable ? " Stackable." : string.Empty);
        amountInput.Editable = info.Stackable;
        amountInput.Value = info.Stackable ? Math.Max(1, info.DefaultAmount) : 0;
    }

    public void SelectKind(string kind, int amount)
    {
        for (int i = 0; i < kindOption.ItemCount; i++)
        {
            if (kindOption.GetItemMetadata(i).AsString() != kind) continue;
            kindOption.Select(i);
            RefreshKindSummary();
            if (amountInput.Editable) amountInput.Value = amount;
            return;
        }
    }

    public void SetRoomOptions(string name, string floorColor, string labelColor)
    {
        roomNameInput.Text = name;
        roomFloorColor.Color = new Color(floorColor);
        roomLabelColor.Color = new Color(labelColor);
    }

    private void EmitMetadata()
    {
        if (suppressMetadataEvents) return;
        MetadataChanged?.Invoke(nameInput.Text, authorInput.Text, descriptionInput.Text);
    }

    public void ShowTool(MapEditorTool tool)
    {
        foreach ((MapEditorTool candidate, Button button) in toolButtons)
            button.ButtonPressed = candidate == tool;
    }

    /// <summary>Reflects the map's metadata and size without echoing the change back as an edit.</summary>
    public void ShowMap(StationMap map)
    {
        suppressMetadataEvents = true;
        if (nameInput.Text != map.Name) nameInput.Text = map.Name;
        if (authorInput.Text != map.Author) authorInput.Text = map.Author;
        if (descriptionInput.Text != map.Description) descriptionInput.Text = map.Description;
        widthInput.Value = map.Bounds.Width;
        heightInput.Value = map.Bounds.Height;
        if (string.IsNullOrWhiteSpace(fileStemInput.Text) || !fileStemInput.HasFocus())
            fileStemInput.Text = StationMaps.ToFileStem(map.Name);
        suppressMetadataEvents = false;
    }

    public void SetHistory(bool canUndo, bool canRedo)
    {
        undoButton.Disabled = !canUndo;
        redoButton.Disabled = !canRedo;
    }

    public void ShowProblems(IReadOnlyList<StationMapProblem> problems)
    {
        if (problems.Count == 0)
        {
            problemsLabel.Text = "[color=#7be495]No problems. The map is playable.[/color]";
            return;
        }
        var lines = problems.OrderByDescending(problem => problem.IsError).Take(40).Select(problem =>
            $"[color={(problem.IsError ? "#ff5c5c" : "#ffd166")}]{(problem.IsError ? "ERROR" : "warn")}[/color] {problem.Message}" +
            (problem.Tile is { } tile ? $" ({tile.X}, {tile.Y})" : string.Empty));
        int errors = problems.Count(problem => problem.IsError);
        problemsLabel.Text = $"{errors} error(s), {problems.Count - errors} warning(s)\n" + string.Join("\n", lines);
    }

    public void SetStatus(string text) => statusLabel.Text = text;

    public void SetHover(string text) => hoverLabel.Text = text;

    public void RefreshMapList()
    {
        loadOption.Clear();
        int index = 0;
        foreach (StationMapEntry entry in StationMapLibrary.List())
        {
            loadOption.AddItem(entry.Label, index);
            loadOption.SetItemMetadata(index, entry.Path);
            index++;
        }
        if (loadOption.ItemCount > 0) loadOption.Select(0);
    }
}
