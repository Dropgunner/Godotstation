using Godot;
using Godotstation.Engine.Rounds;

namespace Godotstation.Game.Round;

/// <summary>Displays engine round state and routes the in-app replay action.</summary>
public partial class RoundHudController : Node
{
    private RoundSessionServiceNode round = null!;
    private Label progressLabel = null!;
    private Label outcomeLabel = null!;
    private Button restartButton = null!;
    private double refreshElapsed;

    public override void _Ready()
    {
        round = GetNode<RoundSessionServiceNode>("/root/Round");
        BuildHud();
        round.StateChanged += Refresh;
        round.StartRound();
        Refresh();
    }

    public override void _ExitTree()
    {
        if (round != null) round.StateChanged -= Refresh;
    }

    public override void _Process(double delta)
    {
        refreshElapsed += delta;
        if (refreshElapsed < 0.1) return;
        round.AdvanceTime(refreshElapsed);
        refreshElapsed = 0;
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer { Layer = 10 };
        AddChild(layer);

        var panel = new PanelContainer
        {
            Position = new Vector2(18, 18),
            CustomMinimumSize = new Vector2(340, 116),
        };
        layer.AddChild(panel);
        var content = new VBoxContainer();
        panel.AddChild(content);

        var lobby = GetNode<LocalLobbyServiceNode>("/root/Lobby");
        var title = new Label
        {
            Text = $"STATION RELAY ARRAY · {lobby.SelectedJobName}",
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(title);
        content.AddChild(new Label { Text = "Wrench the three orange relay panels." });
        progressLabel = new Label();
        content.AddChild(progressLabel);

        var resultCenter = new CenterContainer();
        resultCenter.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        resultCenter.MouseFilter = Control.MouseFilterEnum.Ignore;
        resultCenter.Visible = false;
        layer.AddChild(resultCenter);
        outcomeLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        outcomeLabel.AddThemeFontSizeOverride("font_size", 30);
        var resultPanel = new PanelContainer { CustomMinimumSize = new Vector2(520, 220) };
        resultCenter.AddChild(resultPanel);
        var resultContent = new VBoxContainer();
        resultContent.AddThemeConstantOverride("separation", 18);
        resultPanel.AddChild(resultContent);
        resultContent.AddChild(outcomeLabel);
        restartButton = new Button
        {
            Text = "Return to Job Selection",
            CustomMinimumSize = new Vector2(280, 48),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        restartButton.Pressed += ReturnToLobby;
        resultContent.AddChild(restartButton);
        this.resultCenter = resultCenter;
    }

    private void Refresh()
    {
        RoundSession session = round.Session;
        progressLabel.Text = $"Relay panels: {session.ObjectiveProgress}/{session.ObjectiveActionsRequired}    Time: {(int)System.Math.Ceiling(session.TimeRemainingSeconds)}s";
        bool terminal = session.Outcome is RoundOutcome.Success or RoundOutcome.Failure;
        resultCenter.Visible = terminal;
        outcomeLabel.Text = session.Outcome switch
        {
            RoundOutcome.Success => "STATION RESTORED",
            RoundOutcome.Failure => "TIME EXPIRED",
            _ => string.Empty,
        };
    }

    private void ReturnToLobby()
    {
        round.Session.Reset();
        GetTree().ChangeSceneToFile("res://UI/RoundLobby.tscn");
    }

    private CenterContainer resultCenter = null!;
}
