using Godot;
using Godotstation.Common.Interactions;
using Godotstation.Game.Input;
using Godotstation.Game.World;

namespace Godotstation.Game.Items;

public partial class WrenchHandler : Node, IOwnedInteractionHandler<HandApply>
{
	[Signal] public delegate void WrenchedEventHandler();

	private InteractionDispatcher dispatcher = null!;
	public new InteractableNode2D Owner { get; private set; } = null!;
	WorldEntity IOwnedInteractionHandler<HandApply>.Owner => Owner.InteractionEntity;
	public bool IsFirstInteractable => false;
	public int Priority => 0;

	public override void _Ready()
	{
		Owner = (InteractableNode2D)GetParent();
		dispatcher = GetNode<InteractionServiceNode>("/root/Interaction").Dispatcher;
		dispatcher.RegisterHandler(this);
	}

	public override void _ExitTree() => dispatcher?.UnregisterHandler(this);

	public bool WillInteract(HandApply interaction, NetworkSide side)
		=> InteractableNode2D.FromEntity(interaction.TargetObject)?.IsInGroup("pipe") == true;

	public void ServerPerformInteraction(HandApply interaction) => EmitSignal(SignalName.Wrenched);
}
