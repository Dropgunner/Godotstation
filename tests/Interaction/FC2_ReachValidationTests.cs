using System.Numerics;
using Godotstation.Common.Interaction;
using Xunit;

namespace Godotstation.Common.Tests.Interaction;

/// <summary>
/// FC-2: standard reach is Chebyshev distance strictly under 1.5 tiles with an unblocked click
/// path; hidden players reach only their container; items in storage are reachable only by the
/// storage's owner.
/// </summary>
public class FC2_ReachValidationTests
{
	private static bool CanApplyAt(float targetX, float targetY, NetworkSide side = NetworkSide.Server,
		FakeWorld? world = null)
	{
		var player = new FakePlayer(0, 0);
		world ??= new FakeWorld();
		world.LocalPlayer ??= player;
		var target = new FakeEntity(targetX, targetY);
		return Validations.CanApply(player, target, world, side);
	}

	[Theory]
	[InlineData(0f, 0f)]     // same tile
	[InlineData(1f, 0f)]     // cardinal neighbour
	[InlineData(0f, -1f)]
	[InlineData(1f, 1f)]     // diagonal neighbour - Chebyshev 1 < 1.5
	[InlineData(-1f, 1f)]
	[InlineData(1.4f, 1.4f)] // Euclidean ~1.98 but Chebyshev 1.4: still in reach
	public void WithinChebyshevRange_InReach(float x, float y)
	{
		Assert.True(CanApplyAt(x, y));
	}

	[Theory]
	[InlineData(2f, 0f)]
	[InlineData(0f, 2f)]
	[InlineData(2f, 2f)]
	[InlineData(1.5f, 0f)] // the bound is strict: exactly 1.5 is out of reach
	public void BeyondChebyshevRange_OutOfReach(float x, float y)
	{
		Assert.False(CanApplyAt(x, y));
	}

	[Fact]
	public void BlockedPath_OutOfReach_EvenWhenAdjacent()
	{
		var world = new FakeWorld { PassableRule = (_, _) => false };
		Assert.False(CanApplyAt(1f, 0f, world: world));
	}

	[Fact]
	public void SamePosition_BypassesBlockageCheck()
	{
		// A degenerate zero-length linecast must not reject the interaction.
		var world = new FakeWorld { PassableRule = (_, _) => false };
		Assert.True(CanApplyAt(0f, 0f, world: world));
	}

	[Fact]
	public void HiddenPlayer_Server_CanOnlyReachItsContainer()
	{
		var locker = new FakeEntity(0, 0);
		var other = new FakeEntity(0, 0);
		var player = new FakePlayer(0, 0) { IsHidden = true, ContainedIn = locker };
		var world = new FakeWorld { LocalPlayer = player };

		Assert.True(Validations.CanApply(player, locker, world, NetworkSide.Server));
		Assert.False(Validations.CanApply(player, other, world, NetworkSide.Server));
	}

	[Fact]
	public void HiddenPlayer_Client_DefersToServer()
	{
		// The client does not know its own container, so it validates optimistically.
		var player = new FakePlayer(0, 0) { IsHidden = true, ContainedIn = null };
		var world = new FakeWorld { LocalPlayer = player };
		var anything = new FakeEntity(10, 10);

		Assert.True(Validations.CanApply(player, anything, world, NetworkSide.Client));
	}

	[Fact]
	public void TargetInOwnInventory_ReachableAtAnyDistance()
	{
		var player = new FakePlayer(0, 0);
		var item = new FakeEntity(50, 50);
		var world = new FakeWorld { LocalPlayer = player };
		world.ContainingSlots[item] = new FakeSlot { Item = item, RootPlayer = player };

		Assert.True(Validations.CanApply(player, item, world, NetworkSide.Server));
	}

	[Fact]
	public void TargetInAnotherPlayersInventory_Unreachable_EvenAdjacent()
	{
		// Being inside any storage replaces the reach check entirely: if the storage is not
		// rooted at the performer the interaction fails, no matter how close they stand.
		var player = new FakePlayer(0, 0);
		var thief = new FakePlayer(1, 0);
		var item = new FakeEntity(1, 0);
		var world = new FakeWorld { LocalPlayer = player };
		world.ContainingSlots[item] = new FakeSlot { Item = item, RootPlayer = thief };

		Assert.False(Validations.CanApply(player, item, world, NetworkSide.Server));
	}

	[Fact]
	public void UnlimitedRange_IgnoresDistance()
	{
		var player = new FakePlayer(0, 0);
		var world = new FakeWorld { LocalPlayer = player };
		var target = new FakeEntity(100, 100);

		Assert.True(Validations.CanApply(player, target, world, NetworkSide.Server,
			reachRange: ReachRange.Unlimited));
	}

	[Fact]
	public void ExtendedServer_NeverCheckedClientSide()
	{
		var player = new FakePlayer(0, 0);
		var world = new FakeWorld { LocalPlayer = player };
		var target = new FakeEntity(100, 100);

		Assert.True(Validations.CanApply(player, target, world, NetworkSide.Client,
			reachRange: ReachRange.ExtendedServer));
	}

	[Fact]
	public void ExtendedServer_AllowsObjectThatJustFlewOutOfReach()
	{
		var player = new FakePlayer(0, 0);
		var world = new FakeWorld { LocalPlayer = player };
		var thrown = new FakeEntity(3, 0);
		// Official position 3 tiles away (out of reach), but before its newtonian movement of
		// +1.4 it sat at 1.6 - within the extended 1.75 allowance.
		world.Physics[thrown] = new PhysicsSnapshot(new Vector2(3, 0), new Vector2(1.4f, 0));

		Assert.True(Validations.CanApply(player, thrown, world, NetworkSide.Server,
			reachRange: ReachRange.ExtendedServer));
	}

	[Fact]
	public void ExtendedServer_RejectsObjectFarBeyondBothPositions()
	{
		var player = new FakePlayer(0, 0);
		var world = new FakeWorld { LocalPlayer = player };
		var faraway = new FakeEntity(10, 0);
		world.Physics[faraway] = new PhysicsSnapshot(new Vector2(10, 0), new Vector2(1, 0));

		Assert.False(Validations.CanApply(player, faraway, world, NetworkSide.Server,
			reachRange: ReachRange.ExtendedServer));
	}

	[Fact]
	public void PositionalClick_MeasuresReachToClickedPosition_NotTargetObject()
	{
		// A PositionalHandApply on an adjacent object still fails when the clicked position
		// itself is out of reach.
		var player = new FakePlayer(0, 0);
		var world = new FakeWorld { LocalPlayer = player };
		var adjacentObject = new FakeEntity(1, 0);

		Assert.False(Validations.CanApply(player, adjacentObject, world, NetworkSide.Server,
			targetPosition: new Vector2(5, 0)));
		Assert.True(Validations.CanApply(player, adjacentObject, world, NetworkSide.Server,
			targetPosition: new Vector2(1, 0)));
	}

	[Fact]
	public void TargetVector_MeasuresReachAlongTheVector()
	{
		var player = new FakePlayer(0, 0);
		var world = new FakeWorld { LocalPlayer = player };

		Assert.True(Validations.CanApply(player, null, world, NetworkSide.Server,
			targetVector: new Vector2(1, 1)));
		Assert.False(Validations.CanApply(player, null, world, NetworkSide.Server,
			targetVector: new Vector2(2, 0)));
	}

	[Fact]
	public void NullTarget_WithoutPositionOrVector_OutOfReach()
	{
		var player = new FakePlayer(0, 0);
		var world = new FakeWorld { LocalPlayer = player };

		Assert.False(Validations.CanApply(player, null, world, NetworkSide.Server));
	}
}
