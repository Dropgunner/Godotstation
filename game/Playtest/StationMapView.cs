using Godot;
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
        static Vector2 Position(ReplicatedStationEntities state, int id)
        {
            var position = state.Get(id).Position;
            return new Vector2(position.X, position.Y);
        }
        Vector2 wrenchPosition = Position(entities, SharedStationInventoryWorld.WrenchId);
        Vector2 fixturePosition = Position(entities, SharedStationInventoryWorld.FixtureId);
        Vector2 metalPosition = Position(entities, SharedStationInventoryWorld.MetalSheetsId);
        Vector2 girderPosition = Position(entities, SharedStationInventoryWorld.GirderId);
        bool fixtureAnchored = entities.FixtureAnchored;
        bool engineeringAirlockOpen = entities.EngineeringAirlockOpen;
        int? wrenchOwner = entities.WrenchOwner;
        int? metalOwner = entities.MetalOwner;
        int metalSheetCount = entities.MetalSheetCount;
        bool girderPlated = entities.GirderPlated;
        int? constructionWorker = entities.ConstructionWorker;
        double constructionProgress = entities.ConstructionProgress;
        bool generatorRunning = entities.GeneratorRunning;
        int fuelCount = entities.GeneratorFuelCount;
        double fuelSeconds = entities.GeneratorFuelSeconds;
        double storedEnergy = entities.StoredEnergy;
        bool outputEnabled = entities.PowerOutputEnabled;
        bool lampOn = entities.PoweredLampOn;
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

        Vector2 airlockPosition = Position(entities, SharedStationInventoryWorld.EngineeringAirlockId);
        DrawRect(new Rect2(airlockPosition - new Vector2(0.43f, 0.43f), new Vector2(0.86f, 0.86f)),
            engineeringAirlockOpen ? new Color("#65aa83") : new Color("#bb8b4c"), true);
        DrawRect(new Rect2(airlockPosition - new Vector2(0.34f, 0.34f), new Vector2(0.68f, 0.68f)),
            engineeringAirlockOpen ? new Color("#b1e5c2") : new Color("#55452f"), false, 0.07f);
        DrawString(ThemeDB.FallbackFont, airlockPosition + new Vector2(-1.1f, 0.67f),
            engineeringAirlockOpen ? "ENGINEERING · OPEN" : "ENGINEERING · ID LOCK", HorizontalAlignment.Left, -1, 1,
            engineeringAirlockOpen ? new Color("#b8f2cb") : new Color("#ffe1a8"));

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

        if (metalSheetCount > 0)
        {
            Vector2 position = metalOwner == null ? metalPosition : metalPosition + new Vector2(0.35f, 0.15f);
            DrawRect(new Rect2(position - new Vector2(0.22f, 0.16f), new Vector2(0.44f, 0.32f)),
                new Color("#aebbc2"), true);
            DrawString(ThemeDB.FallbackFont, position + new Vector2(-0.5f, 0.47f),
                $"METAL ×{metalSheetCount}", HorizontalAlignment.Left, -1, 1, new Color("#e3edf1"));
        }

        DrawRect(new Rect2(girderPosition - new Vector2(0.39f, 0.39f), new Vector2(0.78f, 0.78f)),
            girderPlated ? new Color("#728c98") : new Color("#8e7b5a"), true);
        DrawRect(new Rect2(girderPosition - new Vector2(0.32f, 0.32f), new Vector2(0.64f, 0.64f)),
            new Color("#293941"), false, 0.08f);
        string girderLabel = girderPlated ? "PLATED WALL" : constructionWorker.HasValue
            ? $"PLATING {constructionProgress:0.0}/4.0s" : "GIRDER · 2 SHEETS";
        DrawString(ThemeDB.FallbackFont, girderPosition + new Vector2(-0.88f, 0.65f),
            girderLabel, HorizontalAlignment.Left, -1, 1,
            girderPlated ? new Color("#d4e7ef") : new Color("#ffe1a8"));

        Vector2 fuelPosition = Position(entities, SharedStationInventoryWorld.GeneratorFuelId);
        Vector2 generatorPosition = Position(entities, SharedStationInventoryWorld.GeneratorId);
        Vector2 storagePosition = Position(entities, SharedStationInventoryWorld.PowerStorageId);
        Vector2 lampPosition = Position(entities, SharedStationInventoryWorld.PoweredLampId);
        if (fuelCount > 0)
        {
            DrawRect(new Rect2(fuelPosition - new Vector2(0.24f, 0.18f), new Vector2(0.48f, 0.36f)),
                new Color("#d5aa59"), true);
            DrawString(ThemeDB.FallbackFont, fuelPosition + new Vector2(-0.48f, 0.48f),
                $"FUEL ×{fuelCount}", HorizontalAlignment.Left, -1, 1, new Color("#ffe5a8"));
        }
        DrawRect(new Rect2(generatorPosition - new Vector2(0.38f, 0.36f), new Vector2(0.76f, 0.72f)),
            generatorRunning ? new Color("#e18b54") : new Color("#667984"), true);
        DrawString(ThemeDB.FallbackFont, generatorPosition + new Vector2(-0.65f, 0.62f),
            generatorRunning ? $"GENERATOR · {fuelSeconds:0.0}s" : "FUEL GENERATOR · OFF",
            HorizontalAlignment.Left, -1, 1, new Color("#ffe1a8"));
        DrawRect(new Rect2(storagePosition - new Vector2(0.38f, 0.36f), new Vector2(0.76f, 0.72f)),
            outputEnabled ? new Color("#6397a4") : new Color("#52616b"), true);
        DrawString(ThemeDB.FallbackFont, storagePosition + new Vector2(-0.62f, 0.62f),
            $"STORAGE · {storedEnergy:0.0} · {(outputEnabled ? "OUT" : "OFF")}",
            HorizontalAlignment.Left, -1, 1, new Color("#d5eff4"));
        DrawCircle(lampPosition, lampOn ? 0.34f : 0.22f,
            lampOn ? new Color("#fff09a") : new Color("#60656a"));
        if (lampOn) DrawCircle(lampPosition, 0.48f, new Color(1f, 0.89f, 0.45f, 0.16f));
        DrawString(ThemeDB.FallbackFont, lampPosition + new Vector2(-0.55f, 0.66f),
            lampOn ? "APC LAMP · ON" : "APC LAMP · OUTAGE",
            HorizontalAlignment.Left, -1, 1, lampOn ? new Color("#fff1a8") : new Color("#bdc7cd"));
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
