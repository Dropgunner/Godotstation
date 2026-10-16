using Godot;
using Godotstation.Game.Player;
using Godotstation.Game.World;

namespace Godotstation.Game.Input;

public partial class InteractionInputController : Node
{
	[Export] public NodePath PlayerPath { get; set; } = null!;
	private PlayerCharacter player = null!;

	public override void _Ready()
	{
		player = GetNode<PlayerCharacter>(PlayerPath);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click) return;

		var mouse = player.GetCanvasTransform().AffineInverse() * click.Position;
		var query = new PhysicsPointQueryParameters2D
		{
			Position = mouse,
			CollideWithAreas = true,
			CollideWithBodies = true
		};
		InteractableNode2D? target = null;
		foreach (var hit in player.GetWorld2D().DirectSpaceState.IntersectPoint(query))
		{
			var collider = hit["collider"].AsGodotObject();
			if (collider is InteractableNode2D direct) target = direct;
			else if (collider is Node node) target = node.GetParent() as InteractableNode2D;
			if (target != null) break;
		}

		if (target != null)
			player.TryInteractWith(target, mouse, Godot.Input.IsKeyPressed(Key.Alt));
		GetViewport().SetInputAsHandled();
	}
}
