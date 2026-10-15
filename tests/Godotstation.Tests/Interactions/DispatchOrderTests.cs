using System.Numerics;
using Godotstation.Common.Interactions;
using Xunit;

namespace Godotstation.Tests.Interactions;

public sealed class DispatchOrderTests
{
	[Fact]
	public void HeldItemBeatsNonFirstTarget()
	{
		var (player, held, target) = Setup();
		var itemHandler = new Handler(held, priority: 20, accepts: true);
		var targetHandler = new Handler(target, priority: 0, accepts: true);
		var dispatcher = new InteractionDispatcher();
		dispatcher.RegisterHandler(itemHandler);
		dispatcher.RegisterHandler(targetHandler);

		Assert.True(dispatcher.TryResolveClick(player, held, target, target.WorldPositionServer, Intent.Help, false, NetworkSide.Server));
		Assert.Equal(1, itemHandler.CallCount);
		Assert.Equal(0, targetHandler.CallCount);
	}

	[Fact]
	public void FirstInteractableBeatsHeldItemRegardlessOfPriority()
	{
		var (player, held, target) = Setup();
		var first = new Handler(target, priority: 20, accepts: true, first: true);
		var item = new Handler(held, priority: 0, accepts: true);
		var dispatcher = new InteractionDispatcher();
		dispatcher.RegisterHandler(item);
		dispatcher.RegisterHandler(first);

		Assert.True(dispatcher.TryResolveClick(player, held, target, target.WorldPositionServer, Intent.Help, false, NetworkSide.Server));
		Assert.Equal(1, first.CallCount);
		Assert.Equal(0, item.CallCount);
	}

	[Fact]
	public void FirstInteractableDeclines_FallsThroughToHeldItem()
	{
		var (player, held, target) = Setup();
		var first = new Handler(target, priority: 0, accepts: false, first: true);
		var item = new Handler(held, priority: 0, accepts: true);
		var dispatcher = new InteractionDispatcher();
		dispatcher.RegisterHandler(first);
		dispatcher.RegisterHandler(item);

		Assert.True(dispatcher.TryResolveClick(player, held, target, target.WorldPositionServer, Intent.Help, false, NetworkSide.Server));
		Assert.Equal(1, first.AttemptCount);
		Assert.Equal(0, first.CallCount);
		Assert.Equal(1, item.CallCount);
	}

	[Fact]
	public void DecliningFirstAndHeldHandlers_FallsThroughToNonFirstTarget()
	{
		var (player, held, target) = Setup();
		var first = new Handler(target, priority: 0, accepts: false, first: true);
		var item = new Handler(held, priority: 0, accepts: false);
		var targetDefault = new Handler(target, priority: 1, accepts: true);
		var dispatcher = new InteractionDispatcher();
		dispatcher.RegisterHandler(targetDefault);
		dispatcher.RegisterHandler(first);
		dispatcher.RegisterHandler(item);

		Assert.True(dispatcher.TryResolveClick(player, held, target, target.WorldPositionServer, Intent.Help, false, NetworkSide.Server));
		Assert.Equal(1, first.AttemptCount);
		Assert.Equal(0, first.CallCount);
		Assert.Equal(1, item.AttemptCount);
		Assert.Equal(0, item.CallCount);
		Assert.Equal(1, targetDefault.CallCount);
	}

	[Fact]
	public void PriorityOrdersHandlersWithinTheSameOwnershipBucket()
	{
		var (player, held, target) = Setup();
		var lowPriority = new Handler(held, priority: 0, accepts: true);
		var highPriority = new Handler(held, priority: 5, accepts: true);
		var dispatcher = new InteractionDispatcher();
		dispatcher.RegisterHandler(highPriority);
		dispatcher.RegisterHandler(lowPriority);

		Assert.True(dispatcher.TryResolveClick(player, held, target, target.WorldPositionServer, Intent.Help, false, NetworkSide.Server));
		Assert.Equal(1, lowPriority.AttemptCount);
		Assert.Equal(1, lowPriority.CallCount);
		Assert.Equal(0, highPriority.AttemptCount);
	}

	[Fact]
	public void FrontendBridgeKeepsStableOwnerIdentityAndSnapshotsClickState()
	{
		var player = new FakePerformer();
		var held = new FakeInteractable("wrench", Vector2.Zero);
		var target = new FakeInteractable("pipe", Vector2.One);
		player.ActiveHandItem = held;
		var item = new Handler(held.InteractionEntity, priority: 10, accepts: true);
		var pipe = new Handler(target.InteractionEntity, priority: 0, accepts: true);
		var dispatcher = new InteractionDispatcher();
		dispatcher.RegisterHandler(pipe);
		dispatcher.RegisterHandler(item);

		Assert.True(dispatcher.TryResolveClick(player, player.ActiveHandItem, target,
			target.WorldPosition, player.CurrentIntent, false, NetworkSide.Server));
		Assert.Same(held.InteractionEntity, item.Owner);
		Assert.Equal(1, item.CallCount);
		Assert.Equal(0, pipe.CallCount);

		player.IsGhosting = true;
		Assert.False(dispatcher.TryResolveClick(player, player.ActiveHandItem, target,
			target.WorldPosition, player.CurrentIntent, false, NetworkSide.Server));
		Assert.Equal(1, item.CallCount);

		player.IsGhosting = false;
		player.IsAlive = false;
		Assert.False(dispatcher.TryResolveClick(player, player.ActiveHandItem, target,
			target.WorldPosition, player.CurrentIntent, false, NetworkSide.Server));
		Assert.Equal(1, item.CallCount);
	}

	[Fact]
	public void UnregisteredSceneHandlerCannotRunOnNextClick()
	{
		var (player, held, target) = Setup();
		var handler = new Handler(held, priority: 0, accepts: true);
		var dispatcher = new InteractionDispatcher();
		dispatcher.RegisterHandler(handler);
		Assert.True(dispatcher.TryResolveClick(player, held, target, target.WorldPositionServer, Intent.Help, false, NetworkSide.Server));
		dispatcher.UnregisterHandler(handler);
		Assert.False(dispatcher.TryResolveClick(player, held, target, target.WorldPositionServer, Intent.Help, false, NetworkSide.Server));
		Assert.Equal(1, handler.CallCount);
	}

	private static (PlayerEntity Player, WorldEntity Held, WorldEntity Target) Setup()
	{
		var player = new PlayerEntity { Id = 1 };
		player.PlaceAt(Vector2.Zero);
		var held = new WorldEntity { Id = 2 };
		held.PlaceAt(Vector2.Zero);
		var target = new WorldEntity { Id = 3 };
		target.PlaceAt(Vector2.One);
		return (player, held, target);
	}

	private sealed class Handler : IOwnedInteractionHandler<HandApply>
	{
		public Handler(WorldEntity owner, int priority, bool accepts, bool first = false)
		{
			Owner = owner;
			Priority = priority;
			Accepts = accepts;
			IsFirstInteractable = first;
		}

		public WorldEntity Owner { get; }
		public int Priority { get; }
		public bool IsFirstInteractable { get; }
		public bool Accepts { get; }
		public int AttemptCount { get; private set; }
		public int CallCount { get; private set; }
		public bool WillInteract(HandApply interaction, NetworkSide side)
		{
			AttemptCount++;
			return Accepts;
		}
		public void ServerPerformInteraction(HandApply interaction) => CallCount++;
	}

	private sealed class FakeInteractable : IInteractable
	{
		public FakeInteractable(string id, Vector2 position)
		{
			Id = id;
			InteractionEntity = new WorldEntity();
			InteractionEntity.PlaceAt(position);
		}
		public string Id { get; }
		public Vector2 WorldPosition => InteractionEntity.WorldPositionServer;
		public WorldEntity InteractionEntity { get; }
	}

	private sealed class FakePerformer : IPerformer
	{
		public FakePerformer() => InteractionPlayer.PlaceAt(Vector2.Zero);
		public Vector2 WorldPosition => InteractionPlayer.WorldPositionServer;
		public bool IsGhosting { get; set; }
		public bool HasTelekinesis => false;
		public bool IsAlive { get; set; } = true;
		public IInteractable? ActiveHandItem { get; set; }
		public Intent CurrentIntent => Intent.Help;
		public PlayerEntity InteractionPlayer { get; } = new PlayerEntity();
	}
}
