using Godot;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Movement;
using Godotstation.Engine.Interactions;

namespace Godotstation.Game.Playtest;

/// <summary>Renders the shared tile layout as a compact station with labeled departments.</summary>
public partial class StationMapView : Node2D
{
    private ReplicatedStationEntities entities = new();

    public void SetEntities(ReplicatedStationEntities currentEntities)
    {
        entities = currentEntities;
        QueueRedraw();
    }

    public void RefreshEntities() => QueueRedraw();

    public override void _Ready()
    {
        AddRoomLabel("MEDICAL", new Vector2(-8f, -6.3f), new Color("#b8efff"));
        AddRoomLabel("SECURITY", new Vector2(0f, -6.3f), new Color("#ffd1bd"));
        AddRoomLabel("ENGINEERING", new Vector2(8f, -6.3f), new Color("#ffe1a8"));
        AddRoomLabel("CREW QUARTERS", new Vector2(0f, 6.3f), new Color("#ded9ff"));
        AddRoomLabel("CENTRAL HALL", new Vector2(0f, 0.6f), new Color("#d1e2e8"));
    }

    public override void _Draw()
    {
        for (int y = StationPlaytestLayout.MinY; y <= StationPlaytestLayout.MaxY; y++)
        for (int x = StationPlaytestLayout.MinX; x <= StationPlaytestLayout.MaxX; x++)
        {
            Color floor = y < -4
                ? (x < -4 ? new Color("#475b64") : x < 4 ? new Color("#54525d") : new Color("#5b5848"))
                : y > 4 ? new Color("#484d5a")
                : ((x + y) & 1) == 0 ? new Color("#485965") : new Color("#44545f");
            DrawRect(new Rect2(x - 0.5f, y - 0.5f, 1f, 1f), floor, true);
        }

        foreach (var (x, y) in StationPlaytestLayout.GetWallTiles())
        {
            DrawRect(new Rect2(x - 0.49f, y - 0.49f, 0.98f, 0.98f), new Color("#24323b"), true);
            DrawRect(new Rect2(x - 0.49f, y - 0.49f, 0.98f, 0.12f), new Color("#6c8591"), true);
        }

        foreach (StationEntityState entity in entities.All)
        {
            Vector2 position = new(entity.Position.X, entity.Position.Y);
            switch (entity.Kind)
            {
                case StationEntityKind.EngineeringAirlock: DrawAirlock(entity, position); break;
                case StationEntityKind.ServiceCart: DrawCart(entity, position); break;
                case StationEntityKind.Wrench: DrawWrench(entity, position); break;
                case StationEntityKind.MetalSheets: DrawMetal(entity, position); break;
                case StationEntityKind.Girder: DrawGirder(entity, position); break;
                case StationEntityKind.GeneratorFuel: DrawFuel(entity, position); break;
                case StationEntityKind.FuelGenerator: DrawGenerator(entity, position); break;
                case StationEntityKind.PowerStorage: DrawStorage(entity, position); break;
                case StationEntityKind.ApcLamp: DrawLamp(entity, position); break;
                case StationEntityKind.Crew: break; // StationCrewAvatar draws crew bodies
                default: DrawUnknown(entity, position); break;
            }
        }
    }

    private void DrawAirlock(StationEntityState entity, Vector2 position)
    {
        bool open = entity.Flag(StationEntityField.Open);
        DrawRect(new Rect2(position - new Vector2(0.43f, 0.43f), new Vector2(0.86f, 0.86f)),
            open ? new Color("#65aa83") : new Color("#bb8b4c"), true);
        DrawRect(new Rect2(position - new Vector2(0.34f, 0.34f), new Vector2(0.68f, 0.68f)),
            open ? new Color("#b1e5c2") : new Color("#55452f"), false, 0.07f);
        DrawString(ThemeDB.FallbackFont, position + new Vector2(-1.1f, 0.67f),
            open ? "ENGINEERING · OPEN" : "ENGINEERING · ID LOCK", HorizontalAlignment.Left, -1, 1,
            open ? new Color("#b8f2cb") : new Color("#ffe1a8"));
    }

    private void DrawCart(StationEntityState entity, Vector2 position)
    {
        bool anchored = entity.Flag(StationEntityField.Anchored);
        DrawRect(new Rect2(position - new Vector2(0.37f, 0.27f), new Vector2(0.74f, 0.54f)),
            anchored ? new Color("#c87955") : new Color("#70cfa6"), true);
        DrawRect(new Rect2(position - new Vector2(0.31f, 0.21f), new Vector2(0.62f, 0.42f)),
            new Color("#26353b"), false, 0.07f);
        DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.7f, 0.62f),
            anchored ? "ANCHORED CART" : "LOOSE CART", HorizontalAlignment.Left, -1, 1,
            anchored ? new Color("#ffd0b3") : new Color("#a9f3cf"));
    }

    private void DrawWrench(StationEntityState entity, Vector2 position)
    {
        if (entity.Holder != null)
        {
            DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.3f, -0.55f),
                "WRENCH", HorizontalAlignment.Left, -1, 1, new Color("#ffe39a"));
            return;
        }
        DrawCircle(position, 0.22f, new Color("#f3c85d"));
        DrawLine(position + new Vector2(-0.13f, 0.13f), position + new Vector2(0.15f, -0.15f),
            new Color("#533f1b"), 0.10f);
        DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.35f, 0.43f),
            "WRENCH", HorizontalAlignment.Left, -1, 1, new Color("#ffe39a"));
    }

    private void DrawMetal(StationEntityState entity, Vector2 position)
    {
        int sheets = entity.Count(StationEntityField.Amount);
        if (sheets <= 0) return;
        if (entity.Holder != null) position += new Vector2(0.35f, 0.15f);
        DrawRect(new Rect2(position - new Vector2(0.22f, 0.16f), new Vector2(0.44f, 0.32f)),
            new Color("#aebbc2"), true);
        DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.5f, 0.47f),
            $"METAL ×{sheets}", HorizontalAlignment.Left, -1, 1, new Color("#e3edf1"));
    }

    private void DrawGirder(StationEntityState entity, Vector2 position)
    {
        bool plated = entity.Flag(StationEntityField.Plated);
        DrawRect(new Rect2(position - new Vector2(0.39f, 0.39f), new Vector2(0.78f, 0.78f)),
            plated ? new Color("#728c98") : new Color("#8e7b5a"), true);
        DrawRect(new Rect2(position - new Vector2(0.32f, 0.32f), new Vector2(0.64f, 0.64f)),
            new Color("#293941"), false, 0.08f);
        string label = plated ? "PLATED WALL" : entity.Count(StationEntityField.Worker) != 0
            ? $"PLATING {entity.Get(StationEntityField.Progress):0.0}/4.0s" : "GIRDER · 2 SHEETS";
        DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.88f, 0.65f),
            label, HorizontalAlignment.Left, -1, 1, plated ? new Color("#d4e7ef") : new Color("#ffe1a8"));
    }

    private void DrawFuel(StationEntityState entity, Vector2 position)
    {
        int fuel = entity.Count(StationEntityField.Amount);
        if (fuel <= 0) return;
        DrawRect(new Rect2(position - new Vector2(0.24f, 0.18f), new Vector2(0.48f, 0.36f)),
            new Color("#d5aa59"), true);
        DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.48f, 0.48f),
            $"FUEL ×{fuel}", HorizontalAlignment.Left, -1, 1, new Color("#ffe5a8"));
    }

    private void DrawGenerator(StationEntityState entity, Vector2 position)
    {
        bool running = entity.Flag(StationEntityField.Running);
        DrawRect(new Rect2(position - new Vector2(0.38f, 0.36f), new Vector2(0.76f, 0.72f)),
            running ? new Color("#e18b54") : new Color("#667984"), true);
        DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.65f, 0.62f),
            running ? $"GENERATOR · {entity.Get(StationEntityField.FuelSeconds):0.0}s" : "FUEL GENERATOR · OFF",
            HorizontalAlignment.Left, -1, 1, new Color("#ffe1a8"));
    }

    private void DrawStorage(StationEntityState entity, Vector2 position)
    {
        bool output = entity.Flag(StationEntityField.OutputEnabled);
        DrawRect(new Rect2(position - new Vector2(0.38f, 0.36f), new Vector2(0.76f, 0.72f)),
            output ? new Color("#6397a4") : new Color("#52616b"), true);
        DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.62f, 0.62f),
            $"STORAGE · {entity.Get(StationEntityField.StoredEnergy):0.0} · {(output ? "OUT" : "OFF")}",
            HorizontalAlignment.Left, -1, 1, new Color("#d5eff4"));
    }

    private void DrawLamp(StationEntityState entity, Vector2 position)
    {
        bool on = entity.Flag(StationEntityField.Powered);
        DrawCircle(position, on ? 0.34f : 0.22f, on ? new Color("#fff09a") : new Color("#60656a"));
        if (on) DrawCircle(position, 0.48f, new Color(1f, 0.89f, 0.45f, 0.16f));
        DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.55f, 0.66f),
            on ? "APC LAMP · ON" : "APC LAMP · OUTAGE",
            HorizontalAlignment.Left, -1, 1, on ? new Color("#fff1a8") : new Color("#bdc7cd"));
    }

    /// <summary>Placeholder so a newly catalogued kind is visible before it gets its own art.</summary>
    private void DrawUnknown(StationEntityState entity, Vector2 position)
    {
        if (entity.Holder != null) return;
        DrawRect(new Rect2(position - new Vector2(0.3f, 0.3f), new Vector2(0.6f, 0.6f)), new Color("#b06ab3"), true);
        DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.6f, 0.6f),
            entity.Kind.ToUpperInvariant(), HorizontalAlignment.Left, -1, 1, new Color("#f1d3f2"));
    }

    private void AddRoomLabel(string text, Vector2 worldPosition, Color color)
    {
        var anchor = new Node2D { Position = worldPosition };
        AddChild(anchor);
        var label = new Label
        {
            Text = text,
            Position = new Vector2(-120f, -20f),
            Size = new Vector2(240f, 40f),
            Scale = new Vector2(0.03f, 0.03f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", color);
        anchor.AddChild(label);
    }
}
