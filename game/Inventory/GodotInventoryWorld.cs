using System;
using System.Collections.Generic;
using Godot;
using Godotstation.Engine.Inventory;
using NumericsVector2 = System.Numerics.Vector2;

namespace Godotstation.Game.Inventory;

/// <summary>Godot scene-tree and physics effects for the engine's inventory moves.</summary>
public sealed class GodotInventoryWorld : IInventoryWorld
{
	public const string InsideObjectContainerGroup = "inside_object_container";

	private readonly Node2D worldRoot;
	private readonly Func<ItemSlot, Node2D?> holderForSlot;
	private readonly Dictionary<CollisionObject2D, (uint Layer, uint Mask)> savedCollision = new();

	public GodotInventoryWorld(Node2D worldRoot, Func<ItemSlot, Node2D?> holderForSlot)
	{
		this.worldRoot = worldRoot ?? throw new ArgumentNullException(nameof(worldRoot));
		this.holderForSlot = holderForSlot ?? throw new ArgumentNullException(nameof(holderForSlot));
	}

	/// <summary>Holders inside an object container join this group; removals then stay inside it.</summary>
	public bool IsHolderInContainer(ItemSlot fromSlot)
		=> holderForSlot(fromSlot)?.IsInGroup(InsideObjectContainerGroup) == true;

	public void DisappearFromWorld(IPickupable item)
	{
		var node = GetNode(item);
		node.Visible = false;
		SetProcessing(node, false);
		SetCollision(node, false);
	}

	public void Despawn(IPickupable item)
	{
		var node = GetNode(item);
		ForgetCollision(node);
		node.QueueFree();
	}

	public void Drop(IPickupable item, ItemSlot fromSlot, NumericsVector2? worldTargetVector)
	{
		var node = GetNode(item);
		var holder = holderForSlot(fromSlot);
		var position = holder?.GlobalPosition ?? worldRoot.GlobalPosition;
		if (worldTargetVector is { } offset)
			position += ToGodot(offset);

		PlaceInWorld(node, position);
	}

	public void StoreInHolderContainer(IPickupable item, ItemSlot fromSlot)
	{
		var node = GetNode(item);
		var holder = holderForSlot(fromSlot);
		var position = holder?.GlobalPosition ?? worldRoot.GlobalPosition;
		ReparentPreservingGlobalPosition(node, holder ?? worldRoot);
		node.GlobalPosition = position;
		node.Visible = true;
		SetProcessing(node, true);
		SetCollision(node, true);
	}

	public void Throw(IPickupable item, ItemSlot fromSlot, NumericsVector2 trajectory, float throwSpeed,
		float airTime, BodyPartType aim)
	{
		var node = GetNode(item);
		var holder = holderForSlot(fromSlot);
		var position = holder?.GlobalPosition ?? worldRoot.GlobalPosition;
		PlaceInWorld(node, position);

		if (node is RigidBody2D body)
		{
			var direction = ToGodot(trajectory).Normalized();
			body.LinearVelocity = direction * throwSpeed;
		}
		else
		{
			node.GlobalPosition += ToGodot(trajectory) * (throwSpeed * MathF.Max(airTime, 0f));
		}
	}

	public void Vanish(IPickupable item)
	{
		var node = GetNode(item);
		node.Visible = false;
		SetProcessing(node, false);
		SetCollision(node, false);
	}

	private static Node2D GetNode(IPickupable item) => item as Node2D
		?? throw new ArgumentException("Godot inventory effects require the pickupable to be a Node2D.", nameof(item));

	private void PlaceInWorld(Node2D node, Godot.Vector2 globalPosition)
	{
		ReparentPreservingGlobalPosition(node, worldRoot);
		node.GlobalPosition = globalPosition;
		node.Visible = true;
		SetProcessing(node, true);
		SetCollision(node, true);
	}

	private void SetCollision(Node node, bool enabled)
	{
		if (node is CollisionObject2D collisionObject)
		{
			if (enabled)
			{
				if (savedCollision.Remove(collisionObject, out var state))
				{
					collisionObject.CollisionLayer = state.Layer;
					collisionObject.CollisionMask = state.Mask;
				}
			}
			else if (!savedCollision.ContainsKey(collisionObject))
			{
				savedCollision.Add(collisionObject, (collisionObject.CollisionLayer, collisionObject.CollisionMask));
				collisionObject.CollisionLayer = 0;
				collisionObject.CollisionMask = 0;
			}
		}

		foreach (Node child in node.GetChildren()) SetCollision(child, enabled);
	}

	private void ForgetCollision(Node node)
	{
		if (node is CollisionObject2D collisionObject) savedCollision.Remove(collisionObject);
		foreach (Node child in node.GetChildren()) ForgetCollision(child);
	}

	private static void ReparentPreservingGlobalPosition(Node2D node, Node newParent)
	{
		if (node.GetParent() != newParent)
			node.Reparent(newParent, true);
	}

	private static void SetProcessing(Node node, bool enabled)
	{
		node.SetProcess(enabled);
		node.SetPhysicsProcess(enabled);
	}

	private static Godot.Vector2 ToGodot(NumericsVector2 vector) => new(vector.X, vector.Y);
}
