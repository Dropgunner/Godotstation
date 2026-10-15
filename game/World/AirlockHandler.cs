using Godot;
using Godotstation.Common.Interactions;
using Godotstation.Game.Input;

namespace Godotstation.Game.World;

public partial class AirlockHandler : Node, IOwnedInteractionHandler<HandApply>
{
	private InteractionDispatcher dispatcher = null!;
	public new InteractableNode2D Owner { get; private set; } = null!;
	WorldEntity IOwnedInteractionHandler<HandApply>.Owner => Owner.InteractionEntity;
	public bool IsFirstInteractable => true;
	public int Priority => 0;
	public bool IsOpen { get; private set; }

	public override void _Ready()
	{
		Owner = (InteractableNode2D)GetParent();
		dispatcher = GetNode<InteractionServiceNode>("/root/Interaction").Dispatcher;
		dispatcher.RegisterHandler(this);
	}

	public override void _ExitTree() => dispatcher?.UnregisterHandler(this);
	public bool WillInteract(HandApply interaction, NetworkSide side) => true;

	public void ServerPerformInteraction(HandApply interaction)
	{
		IsOpen = !IsOpen;
		Owner.Modulate = IsOpen ? Colors.LightGreen : Colors.OrangeRed;
	}
}
