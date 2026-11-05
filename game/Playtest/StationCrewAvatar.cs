using Godot;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Movement;

namespace Godotstation.Game.Playtest;

/// <summary>Draws a crew member and attaches the camera only to the local player's avatar.</summary>
public partial class StationCrewAvatar : Node2D
{
    private int peerId;
    private bool isLocal;
    private string displayName = string.Empty;
    private string jobName = string.Empty;
    private Label? nameLabel;
    private ConsciousState consciousState = ConsciousState.Conscious;
    private Camera2D? camera;
    private Node2D? ghost;

    public void Configure(int ownerPeerId, bool local, string playerName, string assignedJobName)
    {
        peerId = ownerPeerId;
        isLocal = local;
        displayName = playerName;
        jobName = assignedJobName;
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

        nameLabel = new Label
        {
            Text = $"{displayName}\n{jobName.ToUpperInvariant()}",
            Position = new Vector2(-48f, -40f),
            Size = new Vector2(96f, 20f),
            Scale = new Vector2(0.04f, 0.04f),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 13);
        nameLabel.AddThemeColorOverride("font_color", Colors.White);
        AddChild(nameLabel);
        ApplyConsciousState();

        if (!isLocal) return;
        camera = new Camera2D
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

    /// <summary>Presentation only: tint and caption follow the server's replicated conscious state (FC-SC-7).</summary>
    public void SetConsciousState(ConsciousState state)
    {
        consciousState = state;
        if (IsInsideTree()) ApplyConsciousState();
    }

    private void ApplyConsciousState()
    {
        (Color tint, string caption) = consciousState switch
        {
            ConsciousState.BarelyConscious => (new Color(1f, 0.85f, 0.55f), "\nBARELY CONSCIOUS"),
            ConsciousState.Unconscious => (new Color(0.6f, 0.6f, 0.7f), "\nUNCONSCIOUS"),
            ConsciousState.Dead => (new Color(0.45f, 0.2f, 0.2f), "\nDEAD"),
            _ => (Colors.White, string.Empty),
        };
        Modulate = tint;
        if (nameLabel != null) nameLabel.Text = $"{displayName}\n{jobName.ToUpperInvariant()}{caption}";
    }

    /// <summary>
    /// FC-GH-5 presentation: the local player's ghost is drawn at its replicated position and the camera
    /// follows it; the body stays where it fell. Other players' ghosts stay hidden, as upstream ghosts
    /// are invisible to the living.
    /// </summary>
    public void SetGhostPosition(Vector2? worldPosition)
    {
        if (!isLocal || !IsInsideTree()) return;
        if (worldPosition is not Vector2 target)
        {
            ghost?.QueueFree();
            ghost = null;
            if (camera != null) camera.Position = Vector2.Zero;
            return;
        }
        if (ghost == null)
        {
            ghost = new Node2D { Name = "Ghost", TopLevel = true, Modulate = new Color(0.8f, 0.9f, 1f, 0.6f) };
            ghost.AddChild(new Polygon2D { Color = new Color("#dce8ff"), Polygon = MakeCircle(0.26f, 20) });
            var label = new Label
            {
                Text = "GHOST",
                Position = new Vector2(-48f, -34f),
                Size = new Vector2(96f, 20f),
                Scale = new Vector2(0.04f, 0.04f),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            label.AddThemeFontSizeOverride("font_size", 13);
            ghost.AddChild(label);
            AddChild(ghost);
        }
        ghost.GlobalPosition = target;
        if (camera != null) camera.Position = ghost.GlobalPosition - GlobalPosition;
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
