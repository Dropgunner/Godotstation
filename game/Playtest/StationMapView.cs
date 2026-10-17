using Godot;
using Godotstation.Engine.Movement;

namespace Godotstation.Game.Playtest;

/// <summary>Renders the shared tile layout as a compact station with labeled departments.</summary>
public partial class StationMapView : Node2D
{
    private int? wrenchOwner;
    private Vector2 wrenchPosition = new(-1, 2);
    private Vector2 fixturePosition = new(0, 2);
    private bool fixtureAnchored = true;

    public void SetSharedObjects(int? currentWrenchOwner, Vector2 currentWrenchPosition,
        Vector2 currentFixturePosition, bool isFixtureAnchored)
    {
        wrenchOwner = currentWrenchOwner;
        wrenchPosition = currentWrenchPosition;
        fixturePosition = currentFixturePosition;
        fixtureAnchored = isFixtureAnchored;
        QueueRedraw();
    }

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

        DrawRect(new Rect2(fixturePosition - new Vector2(0.37f, 0.27f), new Vector2(0.74f, 0.54f)),
            fixtureAnchored ? new Color("#c87955") : new Color("#70cfa6"), true);
        DrawRect(new Rect2(fixturePosition - new Vector2(0.31f, 0.21f), new Vector2(0.62f, 0.42f)),
            new Color("#26353b"), false, 0.07f);
        DrawString(ThemeDB.FallbackFont, fixturePosition + new Vector2(-0.7f, 0.62f),
            fixtureAnchored ? "ANCHORED CART" : "LOOSE CART", HorizontalAlignment.Left, -1, 1,
            fixtureAnchored ? new Color("#ffd0b3") : new Color("#a9f3cf"));

        if (wrenchOwner == null)
        {
            DrawCircle(wrenchPosition, 0.22f, new Color("#f3c85d"));
            DrawLine(wrenchPosition + new Vector2(-0.13f, 0.13f), wrenchPosition + new Vector2(0.15f, -0.15f),
                new Color("#533f1b"), 0.10f);
            DrawString(ThemeDB.FallbackFont, wrenchPosition + new Vector2(-0.35f, 0.43f),
                "WRENCH", HorizontalAlignment.Left, -1, 1, new Color("#ffe39a"));
        }
        else
            DrawString(ThemeDB.FallbackFont, wrenchPosition + new Vector2(-0.3f, -0.55f),
                "WRENCH", HorizontalAlignment.Left, -1, 1, new Color("#ffe39a"));
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
