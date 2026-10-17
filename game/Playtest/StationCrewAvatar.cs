using Godot;
using Godotstation.Engine.Movement;

namespace Godotstation.Game.Playtest;

/// <summary>Draws a crew member and attaches the camera only to the local player's avatar.</summary>
public partial class StationCrewAvatar : Node2D
{
    private int peerId;
    private bool isLocal;
    private string displayName = string.Empty;

    public void Configure(int ownerPeerId, bool local, string playerName)
    {
        peerId = ownerPeerId;
        isLocal = local;
        displayName = playerName;
    }

    public override void _Ready()
    {
        Color uniform = peerId == 1 ? new Color("#58c9db") : new Color("#e2935f");
        var body = new Polygon2D
        {
            Color = uniform,
            Polygon = new Vector2[]
            {
                new(-0.25f, -0.28f), new(0.25f, -0.28f), new(0.3f, 0.18f),
                new(0.18f, 0.3f), new(-0.18f, 0.3f), new(-0.3f, 0.18f)
            }
        };
        AddChild(body);
        var head = new Polygon2D
        {
            Color = new Color("#f4c7a1"),
            Polygon = MakeCircle(0.12f, 16),
            Position = new Vector2(0, -0.24f),
        };
        AddChild(head);

        var nameLabel = new Label
        {
            Text = displayName,
            Position = new Vector2(-48f, -40f),
            Size = new Vector2(96f, 20f),
            Scale = new Vector2(0.04f, 0.04f),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 13);
        nameLabel.AddThemeColorOverride("font_color", Colors.White);
        AddChild(nameLabel);

        if (!isLocal) return;
        var camera = new Camera2D
        {
            Name = "StationCamera",
            Zoom = new Vector2(32, 32),
            Enabled = true,
        };
        camera.LimitLeft = StationPlaytestLayout.MinX;
        camera.LimitTop = StationPlaytestLayout.MinY;
        camera.LimitRight = StationPlaytestLayout.MaxX;
        camera.LimitBottom = StationPlaytestLayout.MaxY;
        AddChild(camera);
    }

    private static Vector2[] MakeCircle(float radius, int segments)
    {
        var points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }
}
