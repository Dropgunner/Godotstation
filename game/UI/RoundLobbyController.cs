using Godot;
using Godotstation.Common.Interactions;
using Godotstation.Game.Playtest;
using Godotstation.Game.Round;

namespace Godotstation.Game.UI;

/// <summary>Small playable job-selection entry screen for the offline prototype.</summary>
public partial class RoundLobbyController : Control
{
    private int selectedJobId = -1;
    private Label selectedLabel = null!;
    private Button startButton = null!;
    private LineEdit addressInput = null!;

    public override void _Ready()
    {
        if (PlaytestLaunchOptions.TryReadCommandLine())
        {
            CallDeferred(nameof(OpenPlaytestScene));
            return;
        }

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

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(600, 480) };
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

        content.AddChild(new HSeparator());
        content.AddChild(new Label
        {
            Text = "2D multiplayer station playtest · LAN",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        addressInput = new LineEdit
        {
            PlaceholderText = "Host address (example: 192.168.1.20)",
            Text = "127.0.0.1",
            CustomMinimumSize = new Vector2(440, 38),
        };
        content.AddChild(addressInput);

        var playtestButtons = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        content.AddChild(playtestButtons);
        var hostButton = new Button
        {
            Text = "Host Station",
            CustomMinimumSize = new Vector2(190, 44),
        };
        hostButton.Pressed += () => StartPlaytest(host: true);
        playtestButtons.AddChild(hostButton);
        var joinButton = new Button
        {
            Text = "Join Station",
            CustomMinimumSize = new Vector2(190, 44),
        };
        joinButton.Pressed += () => StartPlaytest(host: false);
        playtestButtons.AddChild(joinButton);
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

    private void StartPlaytest(bool host)
    {
        PlaytestLaunchOptions.IsHost = host;
        PlaytestLaunchOptions.Address = string.IsNullOrWhiteSpace(addressInput.Text)
            ? "127.0.0.1"
            : addressInput.Text.Trim();
        GetTree().ChangeSceneToFile("res://World/StationPlaytest.tscn");
    }

    private void OpenPlaytestScene() => GetTree().ChangeSceneToFile("res://World/StationPlaytest.tscn");
}
