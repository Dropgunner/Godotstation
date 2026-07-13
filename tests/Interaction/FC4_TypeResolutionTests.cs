using System.Numerics;
using Godotstation.Common.Interaction;
using Xunit;

namespace Godotstation.Common.Tests.Interaction;

/// <summary>
/// FC-4: DefaultWillInteract dispatches on the declared interaction type, applies each type's
/// validation rules (inventory interactions skip reach, MouseDrop needs mutual reach), and fails
/// closed on types it does not know.
/// </summary>
public class FC4_TypeResolutionTests
{
	private static (FakePlayer player, FakeWorld world) HealthyPlayer(float x = 0, float y = 0)
	{
		var player = new FakePlayer(x, y);
		var world = new FakeWorld { LocalPlayer = player };
		return (player, world);
	}

	[Fact]
	public void HandActivate_IgnoresReach_OnlyChecksActorState()
	{
		var (player, world) = HealthyPlayer();
		var farAwayItem = new FakeEntity(100, 100);
		var activate = HandActivate.ByClient(player, farAwayItem, new FakeSlot { Item = farAwayItem },
			Intent.Help, false);

		Assert.True(DefaultWillInteract.Default(activate, NetworkSide.Server, world));

		player.IsCuffed = true;
		Assert.False(DefaultWillInteract.Default(activate, NetworkSide.Server, world));
	}

	[Fact]
	public void InventoryApply_IgnoresReach_OnlyChecksActorState()
	{
		var (player, world) = HealthyPlayer();
		var apply = InventoryApply.ByClient(player, new FakeSlot(), new FakeSlot(), Intent.Help, false);

		Assert.True(DefaultWillInteract.Default(apply, NetworkSide.Server, world));

		player.ConsciousState = ConsciousState.UNCONSCIOUS;
		Assert.False(DefaultWillInteract.Default(apply, NetworkSide.Server, world));
	}

	[Fact]
	public void Dispatch_UsesDeclaredType_NotRuntimeType()
	{
		// A PositionalHandApply clicking a far-away position on an adjacent object: validated
		// as PositionalHandApply the clicked position is out of reach; routed to a component
		// that declared plain HandApply, only the target object matters. IF2 components declare
		// the type they handle, so the declared type must win.
		var (player, world) = HealthyPlayer();
		var adjacentObject = new FakeEntity(1, 0);
		var positional = PositionalHandApply.ByClient(player, null, adjacentObject,
			new Vector2(5, 0), new FakeSlot(), Intent.Help, BodyPartType.Chest, false);

		Assert.False(DefaultWillInteract.Default(positional, NetworkSide.Server, world));
		Assert.True(DefaultWillInteract.Default<HandApply>(positional, NetworkSide.Server, world));
	}

	[Fact]
	public void MouseDrop_RequiresMutualReach_BetweenDroppedAndTarget()
	{
		// Performer adjacent to the dropped object, but the drop target is far from it:
		// the drop must fail even though the performer can reach the dropped object.
		var (player, world) = HealthyPlayer();
		var dropped = new FakeEntity(1, 0);
		var farTarget = new FakeEntity(5, 5);
		var nearTarget = new FakeEntity(1, 1);

		var tooFar = MouseDrop.ByClient(player, world, dropped, farTarget, Intent.Help);
		Assert.False(DefaultWillInteract.Default(tooFar, NetworkSide.Server, world));

		var ok = MouseDrop.ByClient(player, world, dropped, nearTarget, Intent.Help);
		Assert.True(DefaultWillInteract.Default(ok, NetworkSide.Server, world));
	}

	[Fact]
	public void MouseDrop_RequiresPerformerToReachDroppedObject()
	{
		var (player, world) = HealthyPlayer();
		var dropped = new FakeEntity(5, 5);
		var target = new FakeEntity(5, 6);

		var drop = MouseDrop.ByClient(player, world, dropped, target, Intent.Help);
		Assert.False(DefaultWillInteract.Default(drop, NetworkSide.Server, world));
	}

	[Fact]
	public void AimApply_RequiresActorState_AndNotHidden()
	{
		var (player, world) = HealthyPlayer();
		var aim = AimApply.ByClient(player, new Vector2(3, 0), null, new FakeSlot(),
			MouseButtonState.PRESS, BodyPartType.Chest, Intent.Harm, player.Position, null);

		Assert.True(DefaultWillInteract.Default(aim, NetworkSide.Server, world));

		player.IsHidden = true;
		Assert.False(DefaultWillInteract.Default(aim, NetworkSide.Server, world));
	}

	private sealed class UnknownInteraction : Godotstation.Common.Interaction.Interaction
	{
		public UnknownInteraction(IPlayerEntity performer)
			: base(performer, null, Intent.Help) { }
	}

	[Fact]
	public void UnknownInteractionType_FailsClosed_AndReportsIt()
	{
		var (player, world) = HealthyPlayer();
		string? reported = null;
		DefaultWillInteract.OnUnrecognizedInteraction = message => reported = message;
		try
		{
			Assert.False(DefaultWillInteract.Default(new UnknownInteraction(player),
				NetworkSide.Server, world));
			Assert.NotNull(reported);
		}
		finally
		{
			DefaultWillInteract.OnUnrecognizedInteraction = null;
		}
	}
}
