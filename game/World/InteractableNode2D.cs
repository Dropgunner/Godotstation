using System;
using System.Collections.Generic;
using System.Threading;
using Godot;
using Godotstation.Common.Interactions;
using NumericsVector2 = System.Numerics.Vector2;

namespace Godotstation.Game.World;

public partial class InteractableNode2D : Node2D, IInteractable
{
	private static int nextEntityId;
	private static readonly Dictionary<WorldEntity, InteractableNode2D> nodesByEntity = new();
	private readonly WorldEntity interactionEntity = new WorldEntity { Id = Interlocked.Increment(ref nextEntityId) };

	[Export] public string Id { get; set; } = string.Empty;

	public NumericsVector2 WorldPosition => new NumericsVector2(GlobalPosition.X, GlobalPosition.Y);
	public WorldEntity InteractionEntity
	{
		get
		{
			interactionEntity.PlaceAt(WorldPosition);
			return interactionEntity;
		}
	}

	public override void _Ready()
	{
		if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
		nodesByEntity.Add(interactionEntity, this);
	}

	public override void _ExitTree() => nodesByEntity.Remove(interactionEntity);

	public static InteractableNode2D? FromEntity(WorldEntity? entity)
		=> entity != null && nodesByEntity.TryGetValue(entity, out var node) && GodotObject.IsInstanceValid(node)
			? node : null;
}
