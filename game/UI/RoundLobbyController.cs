using Godot;
using Godotstation.Common.Interactions;
using Godotstation.Game.Round;

namespace Godotstation.Game.UI;

/// <summary>Small playable job-selection entry screen for the offline prototype.</summary>
public partial class RoundLobbyController : Control
{
    private int selectedJobId = -1;
    private Label selectedLabel = null!;
    private Button startButton = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect
        {
            Color = new Color("#101923"),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(520, 340) };
        center.AddChild(panel);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 16);
        panel.AddChild(content);

        var title = new Label
        {
            Text = "GODOTSTATION",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 30);
        content.AddChild(title);
        content.AddChild(new Label
        {
            Text = "Select a job to begin the station round.",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var lobby = GetNode<LocalLobbyServiceNode>("/root/Lobby");
        foreach (JobData job in lobby.GetAvailableJobs())
        {
            var jobButton = new Button
            {
                Text = $"{job.Name}  ·  {job.Department}\n{job.Description}",
                CustomMinimumSize = new Vector2(440, 72),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            jobButton.Pressed += () => SelectJob(job.JobId, job.Name);
            content.AddChild(jobButton);
        }

        selectedLabel = new Label
        {
            Text = "No job selected",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        content.AddChild(selectedLabel);

        startButton = new Button
        {
            Text = "Start Round",
            CustomMinimumSize = new Vector2(220, 44),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            Disabled = true,
        };
        startButton.Pressed += StartRound;
        content.AddChild(startButton);
    }

    private void SelectJob(int jobId, string jobName)
    {
        selectedJobId = jobId;
        selectedLabel.Text = $"Selected: {jobName}";
        startButton.Disabled = false;
    }

    private void StartRound()
    {
        if (selectedJobId < 0) return;
        UiServices.Lobby.SelectJob(selectedJobId);
        GetTree().ChangeSceneToFile("res://World/Wave8Station.tscn");
    }
}
