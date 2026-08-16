using System;
using Godot;
using Godotstation.Common.Interactions;

namespace Godotstation.Game.UI.Lobby
{
	/// <summary>
	/// Root of the lobby UI (translation of GUI_LobbyDialogue + the UI half
	/// of LobbyManager). Owns the tab flow Servers → Character → Jobs, the
	/// Ready/Spectate footer, the post-selection wait message, and the
	/// disconnect error window.
	///
	/// State handling is intentionally dumb: the backend's LobbyState is the
	/// single source of truth and this node just shows/hides panels when
	/// ILobbyService.OnLobbyStateChanged fires. No lobby logic lives here.
	/// </summary>
	public partial class LobbyScreen : Control
	{
		private TabContainer tabs = null!;
		private VBoxContainer serverHistoryList = null!;
		private JobSelectionPanel jobPanel = null!;
		private JobInfoPopup jobInfo = null!;
		private Button readyButton = null!;
		private Button spectateButton = null!;
		private PanelContainer waitMessage = null!;
		private Label waitMessageText = null!;
		private AcceptDialog errorWindow = null!;

		private ILobbyService lobby = null!;

		public override void _Ready()
		{
			lobby = UiServices.Lobby;

			tabs = GetNode<TabContainer>("%Tabs");
			serverHistoryList = GetNode<VBoxContainer>("%ServerHistoryList");
			jobPanel = GetNode<JobSelectionPanel>("%JobSelectionPanel");
			jobInfo = GetNode<JobInfoPopup>("%JobInfoPopup");
			readyButton = GetNode<Button>("%ReadyButton");
			spectateButton = GetNode<Button>("%SpectateButton");
			waitMessage = GetNode<PanelContainer>("%WaitMessage");
			waitMessageText = GetNode<Label>("%WaitMessageText");
			errorWindow = GetNode<AcceptDialog>("%ErrorWindow");

			readyButton.Pressed += OnReadyPressed;
			spectateButton.Pressed += OnSpectatePressed;
			jobPanel.JobChosen += OnJobChosen;
			jobInfo.JobConfirmed += OnJobConfirmed;

			// Backend events may arrive off the main thread; hop onto it.
			lobby.OnLobbyStateChanged += state =>
				Callable.From(() => ApplyLobbyState(state)).CallDeferred();

			PopulateServerHistory();
			ApplyLobbyState(LobbyState.ServerSelect);
		}

		private void PopulateServerHistory()
		{
			foreach (Node child in serverHistoryList.GetChildren())
			{
				child.QueueFree();
			}

			foreach (ServerInfo server in lobby.GetServerHistory())
			{
				var row = new Button
				{
					Text = $"{server.Name}  —  {server.Address}:{server.Port}",
					TooltipText = $"Last joined {server.LastJoinedUtc:u}"
						+ (server.WasDisconnected ? "\n(disconnected last session)" : ""),
					Alignment = HorizontalAlignment.Left
				};
				serverHistoryList.AddChild(row);
			}
		}

		private void ApplyLobbyState(LobbyState state)
		{
			waitMessage.Visible = state == LobbyState.WaitingToSpawn;

			switch (state)
			{
				case LobbyState.ServerSelect:
					tabs.CurrentTab = 0;
					break;

				case LobbyState.CharacterSelect:
					tabs.CurrentTab = 1;
					break;

				case LobbyState.JobSelect:
					tabs.CurrentTab = 2;
					jobPanel.Populate(lobby.GetAvailableJobs());
					break;

				case LobbyState.WaitingToSpawn:
					waitMessageText.Text = "Please wait...\nAssigning your job and spawning.";
					break;

				case LobbyState.Disconnected:
					errorWindow.DialogText = "Disconnected from server.";
					errorWindow.PopupCentered();
					tabs.CurrentTab = 0;
					PopulateServerHistory();
					break;
			}
		}

		private void OnReadyPressed()
		{
			// Ready = advance to job selection; the actual spawn request only
			// happens once a job is confirmed.
			tabs.CurrentTab = 2;
			jobPanel.Populate(lobby.GetAvailableJobs());
		}

		private void OnSpectatePressed()
		{
			lobby.RequestSpectate();
			waitMessage.Visible = true;
			waitMessageText.Text = "Entering as observer...";
		}

		private void OnJobChosen(int jobId)
		{
			foreach (JobData job in lobby.GetAvailableJobs())
			{
				if (job.JobId == jobId)
				{
					jobInfo.Show(job);
					return;
				}
			}
		}

		private void OnJobConfirmed(int jobId)
		{
			lobby.SelectJob(jobId);
			waitMessage.Visible = true;
			waitMessageText.Text = "Please wait...\nAssigning your job and spawning.";
		}
	}
}
