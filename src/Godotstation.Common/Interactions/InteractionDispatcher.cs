using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Godotstation.Common.Interactions
{
	/// <summary>Handles interactions in fidelity order without depending on Godot.</summary>
	public class InteractionDispatcher
	{
		private readonly IReachabilityMap _map;
		private readonly List<IInteractionHandler<PositionalHandApply>> _positional = new();
		private readonly List<IInteractionHandler<HandApply>> _handApply = new();
		private readonly List<IInteractionHandler<TileApply>> _tileApply = new();
		private readonly List<IInteractionHandler<MouseDrop>> _mouseDrop = new();
		private readonly List<IInteractionHandler<HandActivate>> _handActivate = new();
		private readonly List<IInteractionHandler<InventoryApply>> _inventoryApply = new();

		public InteractionDispatcher(IReachabilityMap? map = null) => _map = map ?? UnobstructedMap.Instance;

		public event Action<Interaction>? OnInteractionResolved;

		public void RegisterHandler(IInteractionHandler<PositionalHandApply> handler) => InsertSorted(_positional, handler);
		public void RegisterHandler(IInteractionHandler<HandApply> handler) => InsertSorted(_handApply, handler);
		public void RegisterHandler(IInteractionHandler<TileApply> handler) => InsertSorted(_tileApply, handler);
		public void RegisterHandler(IInteractionHandler<MouseDrop> handler) => InsertSorted(_mouseDrop, handler);
		public void RegisterHandler(IInteractionHandler<HandActivate> handler) => InsertSorted(_handActivate, handler);
		public void RegisterHandler(IInteractionHandler<InventoryApply> handler) => InsertSorted(_inventoryApply, handler);

		public void UnregisterHandler(IInteractionHandler<PositionalHandApply> handler) => _positional.Remove(handler);
		public void UnregisterHandler(IInteractionHandler<HandApply> handler) => _handApply.Remove(handler);
		public void UnregisterHandler(IInteractionHandler<TileApply> handler) => _tileApply.Remove(handler);
		public void UnregisterHandler(IInteractionHandler<MouseDrop> handler) => _mouseDrop.Remove(handler);
		public void UnregisterHandler(IInteractionHandler<HandActivate> handler) => _handActivate.Remove(handler);
		public void UnregisterHandler(IInteractionHandler<InventoryApply> handler) => _inventoryApply.Remove(handler);

		/// <summary>
		/// Resolves positional hand apply, hand apply, then tile apply. Within each
		/// bucket, priority is stable and ownership groups are considered in FC-9/FC-8 order.
		/// </summary>
		public bool TryResolveClick(PlayerEntity performer, WorldEntity? handObject, WorldEntity? targetObject,
			Vector2 targetPosition, Intent intent, bool isAltClick, NetworkSide side)
		{
			var local = new LocalPlayerContext
			{
				Player = performer,
				ActiveHandSlot = handObject == null ? null : new ItemSlotRef { Item = handObject },
				CurrentIntent = intent,
				IsAltActionKeyPressed = isAltClick
			};
			return TryResolveClick(local, targetObject, targetPosition, side);
		}

		/// <summary>Frontend bridge: retain node identity through stable canonical entities.</summary>
		public bool TryResolveClick(IPerformer performer, IInteractable? handObject, IInteractable? targetObject,
			Vector2 targetPosition, Intent intent, bool isAltClick, NetworkSide side)
		{
			var player = performer.InteractionPlayer;
			var hand = handObject?.InteractionEntity;
			var target = targetObject?.InteractionEntity;
			player.PlaceAt(performer.WorldPosition);
			player.HasTelekinesis = performer.HasTelekinesis;
			player.ConsciousState = performer.IsAlive ? ConsciousState.Conscious : ConsciousState.Dead;
			if (hand != null && handObject != null) hand.PlaceAt(handObject.WorldPosition);
			if (target != null && targetObject != null) target.PlaceAt(targetObject.WorldPosition);
			var local = new LocalPlayerContext
			{
				Player = player,
				IsGhosting = performer.IsGhosting,
				ActiveHandSlot = hand == null ? null : new ItemSlotRef { Item = hand, RootPlayer = player },
				CurrentIntent = intent,
				IsAltActionKeyPressed = isAltClick
			};
			return TryResolveClick(local, target, targetPosition, side);
		}

		public bool TryResolveClick(LocalPlayerContext local, WorldEntity? targetObject, Vector2 targetPosition, NetworkSide side)
		{
			if (local.IsGhosting) return false;
			var handObject = local.ActiveHandSlot?.Item;

			var positional = PositionalHandApply.ByLocalPlayer(local, targetObject, targetPosition);
			if (!ReferenceEquals(positional, PositionalHandApply.Invalid) &&
				DefaultWillInteract.Default(positional, side, _map) &&
				TryDispatch(positional, _positional, side, handObject, targetObject)) return true;

			var handApply = HandApply.ByLocalPlayer(local, targetObject);
			if (!ReferenceEquals(handApply, HandApply.Invalid) &&
				DefaultWillInteract.Default(handApply, side, _map) &&
				TryDispatch(handApply, _handApply, side, handObject, targetObject)) return true;

			if (targetObject == null) return false;
			var tileApply = new TileApply(local.Player, handObject, targetObject, targetPosition, local.CurrentIntent);
			return DefaultWillInteract.Default(tileApply, side, _map) && TryDispatch(tileApply, _tileApply, side, handObject, targetObject);
		}

		public bool TryResolveMouseDrop(PlayerEntity performer, WorldEntity droppedObject, WorldEntity? targetObject,
			ItemSlotRef? fromSlot, Intent intent, NetworkSide side)
		{
			var local = new LocalPlayerContext { Player = performer, CurrentIntent = intent, ActiveHandSlot = fromSlot };
			var interaction = MouseDrop.ByLocalPlayer(local, droppedObject, targetObject, fromSlot);
			return !ReferenceEquals(interaction, MouseDrop.Invalid) && DefaultWillInteract.Default(interaction, side, _map) &&
				TryDispatch(interaction, _mouseDrop, side);
		}

		public bool TryResolveHandActivate(LocalPlayerContext local, NetworkSide side)
		{
			var interaction = HandActivate.ByLocalPlayer(local);
			return !ReferenceEquals(interaction, HandActivate.Invalid) && DefaultWillInteract.Default(interaction, side, _map) &&
				TryDispatch(interaction, _handActivate, side);
		}

		public bool TryResolveInventoryApply(LocalPlayerContext local, ItemSlotRef? targetSlot, ItemSlotRef? fromSlot,
			NetworkSide side)
		{
			var interaction = InventoryApply.ByLocalPlayer(local, targetSlot, fromSlot);
			return !ReferenceEquals(interaction, InventoryApply.Invalid) && DefaultWillInteract.Default(interaction, side, _map) &&
				TryDispatch(interaction, _inventoryApply, side);
		}

		private bool TryDispatch<T>(T interaction, List<IInteractionHandler<T>> handlers, NetworkSide side,
			WorldEntity? handObject = null, WorldEntity? targetObject = null) where T : Interaction
		{
			IEnumerable<IInteractionHandler<T>> ordered = handlers;
			if (interaction is HandApply)
				ordered = ResolveOrder(handlers, handObject, targetObject);

			foreach (var handler in ordered)
			{
				if (!handler.WillInteract(interaction, side)) continue;
				handler.ServerPerformInteraction(interaction);
				OnInteractionResolved?.Invoke(interaction);
				return true;
			}
			return false;
		}

		private static IEnumerable<IInteractionHandler<T>> ResolveOrder<T>(List<IInteractionHandler<T>> handlers,
			WorldEntity? handObject, WorldEntity? targetObject) where T : Interaction
		{
			var owned = handlers.OfType<IOwnedInteractionHandler<T>>().ToList();
			var unowned = handlers.Where(handler => handler is not IOwnedInteractionHandler<T>);
			foreach (var handler in owned.Where(handler => ReferenceEquals(handler.Owner, targetObject) && handler.IsFirstInteractable)) yield return handler;
			foreach (var handler in owned.Where(handler => ReferenceEquals(handler.Owner, handObject))) yield return handler;
			foreach (var handler in owned.Where(handler => ReferenceEquals(handler.Owner, targetObject) && !handler.IsFirstInteractable)) yield return handler;
			foreach (var handler in unowned) yield return handler;
		}

		private static void InsertSorted<T>(List<IInteractionHandler<T>> handlers, IInteractionHandler<T> handler)
			where T : Interaction
		{
			var index = handlers.FindIndex(existing => existing.Priority > handler.Priority);
			if (index < 0) handlers.Add(handler);
			else handlers.Insert(index, handler);
		}
	}
}
