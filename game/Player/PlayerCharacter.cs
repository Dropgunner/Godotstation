using Godot;
using Godotstation.Common.Interactions;
using Godotstation.Game.Inventory;
using Godotstation.Game.World;
using NumericsVector2 = System.Numerics.Vector2;

namespace Godotstation.Game.Player;

public partial class PlayerCharacter : CharacterBody2D, IPerformer
{
	private readonly PlayerEntity interactionPlayer = new PlayerEntity { Id = 0, IsLocalPlayer = true };

	[Export] public bool IsGhosting { get; set; }
	[Export] public bool HasTelekinesis { get; set; }
	[Export] public bool IsAlive { get; set; } = true;
	[Export] public InteractableNode2D? ActiveHandItem { get; set; }
	[Export] public Intent CurrentIntent { get; set; } = Intent.Help;

	IInteractable? IPerformer.ActiveHandItem => ActiveItemForInteractions;
	public InteractableNode2D? ActiveItemForInteractions
	{
		get
		{
			var inventory = GetNodeOrNull<InventoryServiceNode>("/root/Inventory");
			return inventory?.IsRegisteredPlayer(this) == true
				? inventory.GetActiveHandItem(this)
				: ActiveHandItem;
		}
	}
	public NumericsVector2 WorldPosition => new NumericsVector2(GlobalPosition.X, GlobalPosition.Y);
	public PlayerEntity InteractionPlayer
	{
		get
		{
			interactionPlayer.PlaceAt(WorldPosition);
			interactionPlayer.HasTelekinesis = HasTelekinesis;
			interactionPlayer.ConsciousState = IsAlive ? ConsciousState.Conscious : ConsciousState.Dead;
			return interactionPlayer;
		}
	}

	public override void _Ready()
	{
		GetNode<InventoryServiceNode>("/root/Inventory").RegisterPlayer(this);
	}

	public override void _ExitTree()
	{
		GetNodeOrNull<InventoryServiceNode>("/root/Inventory")?.UnregisterPlayer(this);
	}

	/// <summary>Routes a selected world target through the shared interaction dispatcher.</summary>
	public bool TryInteractWith(InteractableNode2D target, Vector2 targetPosition, bool isAltClick = false)
	{
		var dispatcher = GetNode<Godotstation.Game.Input.InteractionServiceNode>("/root/Interaction").Dispatcher;
		var worldPosition = new NumericsVector2(targetPosition.X, targetPosition.Y);
		return dispatcher.TryResolveClick(this, ActiveItemForInteractions, target, worldPosition,
			CurrentIntent, isAltClick, NetworkSide.Server);
	}
}
