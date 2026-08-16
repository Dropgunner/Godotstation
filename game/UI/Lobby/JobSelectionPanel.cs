using System.Collections.Generic;
using Godot;
using Godotstation.Common.Interactions;

namespace Godotstation.Game.UI.Lobby
{
	/// <summary>
	/// Translation of GUI_PlayerJobs' job grid. Buttons are built in code
	/// from ILobbyService data (the Unity original instantiated buttonPrefab
	/// per job); departments become colored section headers, matching the
	/// original's department grouping.
	/// </summary>
	public partial class JobSelectionPanel : ScrollContainer
	{
		[Signal]
		public delegate void JobChosenEventHandler(int jobId);

		/// <summary>Optional themed button scene; falls back to plain Buttons when unset so the panel works before art exists.</summary>
		[Export] private PackedScene? jobButtonScene;

		[Export] private int columnsPerDepartment = 3;

		private VBoxContainer departmentList = null!;

		public override void _Ready()
		{
			departmentList = GetNode<VBoxContainer>("%DepartmentList");
		}

		public void Populate(IReadOnlyList<JobData> jobs)
		{
			foreach (Node child in departmentList.GetChildren())
			{
				child.QueueFree();
			}

			// Preserve backend order within departments; department order is
			// first-appearance order, same as the original's layout data.
			var grids = new Dictionary<string, GridContainer>();

			foreach (JobData job in jobs)
			{
				if (grids.TryGetValue(job.Department, out GridContainer? grid) == false)
				{
					grid = AddDepartmentSection(job);
					grids[job.Department] = grid;
				}

				grid.AddChild(MakeJobButton(job));
			}
		}

		private GridContainer AddDepartmentSection(JobData firstJob)
		{
			var header = new Label { Text = firstJob.Department };
			if (Color.HtmlIsValid(firstJob.DepartmentColorHex))
			{
				header.AddThemeColorOverride("font_color", Color.FromHtml(firstJob.DepartmentColorHex));
			}

			var grid = new GridContainer { Columns = columnsPerDepartment };

			departmentList.AddChild(header);
			departmentList.AddChild(grid);
			return grid;
		}

		private Button MakeJobButton(JobData job)
		{
			Button button = jobButtonScene != null
				? jobButtonScene.Instantiate<Button>()
				: new Button();

			button.Text = $"{job.Name}  ({job.SlotsOpen}/{job.SlotsTotal})";
			button.Disabled = job.IsAvailable == false || job.SlotsOpen <= 0;
			button.TooltipText = button.Disabled && string.IsNullOrEmpty(job.UnavailableReason) == false
				? job.UnavailableReason
				: job.Description;
			button.SizeFlagsHorizontal = SizeFlags.ExpandFill;

			if (string.IsNullOrEmpty(job.IconPath) == false
			    && ResourceLoader.Exists(job.IconPath))
			{
				button.Icon = ResourceLoader.Load<Texture2D>(job.IconPath);
			}

			int capturedJobId = job.JobId; // capture per-iteration, not the loop variable
			button.Pressed += () => EmitSignal(SignalName.JobChosen, capturedJobId);
			return button;
		}
	}
}
