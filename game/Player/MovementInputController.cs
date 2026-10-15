using Godot;
using Godotstation.Engine.Movement;

namespace Godotstation.Game.Player;

/// <summary>Adapts Godot input and node position to the headless movement resolver.</summary>
public partial class MovementInputController : Node
{
	private static readonly (string Name, Key Key)[] MovementKeys =
	[
		("move_left", Key.A),
		("move_right", Key.D),
		("move_up", Key.W),
		("move_down", Key.S)
	];

	private PlayerCharacter player = null!;
	private readonly PlayerMovementState movementState = new();
	private MovementProcessor processor = null!;

	[Export] public MovementType MovementType { get; set; } = MovementType.Running;
	[Export] public bool IsCuffed { get; set; }
	[Export] public bool IsBuckled { get; set; }
	[Export] public bool IsSlipping { get; set; }

	public override void _Ready()
	{
		player = GetParent<PlayerCharacter>();
		var collisionMap = new CollisionMap();
		foreach (var obstacle in GetTree().GetNodesInGroup("solid"))
		{
			if (obstacle is not Node2D solid) continue;
			var tile = new Vector2I(Mathf.RoundToInt(solid.GlobalPosition.X), Mathf.RoundToInt(solid.GlobalPosition.Y));
			collisionMap.SetTile(tile.X, tile.Y, new SolidWall());
		}
		processor = new MovementProcessor(collisionMap);
		EnsureMovementActions();
	}

	public override void _PhysicsProcess(double delta)
	{
		movementState.CurrentType = MovementType;
		movementState.IsCuffed = IsCuffed;
		movementState.IsBuckled = IsBuckled;
		movementState.IsSlipping = IsSlipping;

		var direction = Godot.Input.GetVector("move_left", "move_right", "move_up", "move_down");
		var x = player.GlobalPosition.X;
		var y = player.GlobalPosition.Y;
		var intent = new MoveIntent
		{
			DeltaX = (int)Mathf.Sign(direction.X),
			DeltaY = (int)Mathf.Sign(direction.Y),
			Timestamp = Time.GetTicksMsec() / 1000.0
		};

		if (intent.DeltaX != 0 || intent.DeltaY != 0)
			processor.TryMove(movementState, ref x, ref y, intent, (float)delta);
		else
			processor.ApplyMomentum(movementState, ref x, ref y, (float)delta);

		player.GlobalPosition = new Vector2(x, y);
	}

	private static void EnsureMovementActions()
	{
		foreach (var (action, key) in MovementKeys)
		{
			if (!InputMap.HasAction(action)) InputMap.AddAction(action);
			var keyEvent = new InputEventKey { PhysicalKeycode = key };
			InputMap.ActionAddEvent(action, keyEvent);
		}
	}
}
