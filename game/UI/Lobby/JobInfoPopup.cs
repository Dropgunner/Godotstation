using Godot;
using Godotstation.Common.Interactions;

namespace Godotstation.Game.UI.Lobby
{
	/// <summary>
	/// Translation of GUI_JobInfo: the popup shown when a job button is
	/// clicked — description, slot availability, department banner, and the
	/// Confirm button that actually commits the choice.
	/// </summary>
	public partial class JobInfoPopup : PanelContainer
	{
		[Signal]
		public delegate void JobConfirmedEventHandler(int jobId);

		private Label titleLabel = null!;
		private ColorRect departmentBanner = null!;
		private Label slotsLabel = null!;
		private RichTextLabel descriptionLabel = null!;
		private Button confirmButton = null!;
		private Button closeButton = null!;

		private int currentJobId = -1;

		public override void _Ready()
		{
			titleLabel = GetNode<Label>("%JobTitle");
			departmentBanner = GetNode<ColorRect>("%DepartmentBanner");
			slotsLabel = GetNode<Label>("%SlotsLabel");
			descriptionLabel = GetNode<RichTextLabel>("%Description");
			confirmButton = GetNode<Button>("%ConfirmButton");
			closeButton = GetNode<Button>("%CloseButton");

			confirmButton.Pressed += OnConfirmPressed;
			closeButton.Pressed += Hide;

			Visible = false;
		}

		public void Show(JobData job)
		{
			currentJobId = job.JobId;

			titleLabel.Text = job.Name;
			slotsLabel.Text = $"Positions: {job.SlotsOpen} of {job.SlotsTotal} open";
			descriptionLabel.Text = job.Description;
			departmentBanner.Color = Color.HtmlIsValid(job.DepartmentColorHex)
				? Color.FromHtml(job.DepartmentColorHex)
				: Colors.DimGray;

			confirmButton.Disabled = job.IsAvailable == false || job.SlotsOpen <= 0;
			confirmButton.Text = confirmButton.Disabled ? "Unavailable" : $"Play as {job.Name}";

			Visible = true;
		}

		private void OnConfirmPressed()
		{
			if (currentJobId < 0)
			{
				return;
			}

			Visible = false;
			EmitSignal(SignalName.JobConfirmed, currentJobId);
		}
	}
}
