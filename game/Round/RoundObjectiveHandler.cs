using Godot;
using Godotstation.Common.Interactions;
using Godotstation.Game.Input;
using Godotstation.Game.Inventory;
using Godotstation.Game.World;

namespace Godotstation.Game.Round;

/// <summary>Forwards an in-range wrench interaction to the authoritative round session.</summary>
public partial class RoundObjectiveHandler : Node, IOwnedInteractionHandler<HandApply>
{
    private InteractionServiceNode dispatcherService = null!;
    private RoundSessionServiceNode round = null!;
    private InteractableNode2D objective = null!;

    WorldEntity IOwnedInteractionHandler<HandApply>.Owner => objective.InteractionEntity;
    public bool IsFirstInteractable => true;
    public int Priority => 0;

    public override void _Ready()
    {
        objective = (InteractableNode2D)GetParent();
        dispatcherService = GetNode<InteractionServiceNode>("/root/Interaction");
        round = GetNode<RoundSessionServiceNode>("/root/Round");
        dispatcherService.Dispatcher.RegisterHandler(this);
    }

    public override void _ExitTree() => dispatcherService?.Dispatcher.UnregisterHandler(this);

    public bool WillInteract(HandApply interaction, NetworkSide side)
        => round.Session.Outcome == Godotstation.Engine.Rounds.RoundOutcome.InProgress
            && InteractableNode2D.FromEntity(interaction.HandObject) is InventoryItemNode2D
            { Id: "demo-wrench" };

    public void ServerPerformInteraction(HandApply interaction)
    {
        if (round.TryPerformObjectiveAction(Godotstation.Engine.Rounds.RoundSession.RequiredToolId))
            objective.Modulate = Colors.LightGreen;
    }
}
