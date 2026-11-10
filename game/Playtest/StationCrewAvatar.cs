using Godot;
using Godotstation.Engine.Health;
using Godotstation.Engine.Movement;

namespace Godotstation.Game.Playtest;

/// <summary>Draws a crew member and attaches the camera only to the local player's avatar.</summary>
public partial class StationCrewAvatar : Node2D
{
    private int peerId;
    private bool isLocal;
    private string displayName = string.Empty;
    private string jobName = string.Empty;
    private Node2D? bodyRoot;
    private Label? conditionLabel;
    private CrewStat condition = CrewStat.Stable;

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
        bodyRoot = new Node2D { Name = "Body" };
        AddChild(bodyRoot);
        var body = new Polygon2D
        {
            Color = uniform,
            Polygon = new Vector2[]
            {
                new(-0.25f, -0.28f), new(0.25f, -0.28f), new(0.3f, 0.18f),
                new(0.18f, 0.3f), new(-0.18f, 0.3f), new(-0.3f, 0.18f)
            }
        };
        bodyRoot.AddChild(body);
        var head = new Polygon2D
        {
            Color = new Color("#f4c7a1"),
            Polygon = MakeCircle(0.12f, 16),
            Position = new Vector2(0, -0.24f),
        };
        bodyRoot.AddChild(head);

        var nameLabel = new Label
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

        conditionLabel = new Label
        {
            Position = new Vector2(-48f, 14f),
            Size = new Vector2(96f, 20f),
            Scale = new Vector2(0.04f, 0.04f),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        conditionLabel.AddThemeFontSizeOverride("font_size", 13);
        AddChild(conditionLabel);
        ApplyCondition(CrewHealth.MaxHealth);

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

    /// <summary>Floors the avatar in any critical stat and greys it out in death, as every peer sees it.</summary>
    public void SetCondition(CrewStat stat, double health)
    {
        condition = stat;
        ApplyCondition(health);
    }

    private void ApplyCondition(double health)
    {
        if (bodyRoot == null || conditionLabel == null) return;
        bodyRoot.Rotation = condition.IsFloored() ? Mathf.Pi / 2 : 0;
        bodyRoot.Modulate = condition switch
        {
            CrewStat.Dead => new Color(0.45f, 0.47f, 0.5f),
            CrewStat.HardCrit => new Color(0.8f, 0.6f, 0.6f),
            _ => Colors.White,
        };
        conditionLabel.Visible = condition != CrewStat.Stable || health < CrewHealth.MaxHealth;
        conditionLabel.Text = condition == CrewStat.Stable ? $"{health:0}%" : condition.DisplayName().ToUpperInvariant();
        conditionLabel.AddThemeColorOverride("font_color", condition switch
        {
            CrewStat.Stable => new Color("#d6f5d6"),
            CrewStat.Dead => new Color("#b5bcc0"),
            _ => new Color("#ff8a7a"),
        });
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
