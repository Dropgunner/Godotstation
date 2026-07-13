using System.Numerics;
using Godotstation.Common.Interaction;
using Xunit;

namespace Godotstation.Common.Tests.Interaction;

/// <summary>
/// FC-3: telekinesis extends reach to 15 tiles (Euclidean) but only with an empty hand, and
/// individual interactions can veto it.
/// </summary>
public class FC3_TelekinesisTests
{
	private static (FakePlayer player, FakeWorld world, FakeEntity target) Setup(
		float targetX, float targetY, bool hasTelekinesis = true)
	{
		var player = new FakePlayer(0, 0) { HasTelekinesis = hasTelekinesis };
		var world = new FakeWorld { LocalPlayer = player };
		var target = new FakeEntity(targetX, targetY);
		return (player, world, target);
	}

	private static HandApply EmptyHand(FakePlayer player, FakeEntity target) =>
		HandApply.ByClient(player, null, target, BodyPartType.Chest, new FakeSlot(), Intent.Help, false);

	private static HandApply HoldingItem(FakePlayer player, FakeEntity target)
	{
		var item = new FakeEntity(0, 0);
		return HandApply.ByClient(player, item, target, BodyPartType.Chest,
			new FakeSlot { Item = item }, Intent.Help, false);
	}

	[Fact]
	public void EmptyHand_WithTelekinesis_ReachesFarTargets()
	{
		var (player, world, target) = Setup(10, 0);
		Assert.True(DefaultWillInteract.Default(EmptyHand(player, target), NetworkSide.Server, world));
	}

	[Fact]
	public void TelekinesisRange_IsEuclidean_AndInclusiveAt15()
	{
		var (player, world, atLimit) = Setup(0, 15);
		Assert.True(DefaultWillInteract.Default(EmptyHand(player, atLimit), NetworkSide.Server, world));

		var (player2, world2, beyond) = Setup(0, 15.1f);
		Assert.False(DefaultWillInteract.Default(EmptyHand(player2, beyond), NetworkSide.Server, world2));

		// (11, 11) has Chebyshev 11 but Euclidean ~15.56: telekinesis uses the Euclidean metric.
		var (player3, world3, diagonal) = Setup(11, 11);
		Assert.False(DefaultWillInteract.Default(EmptyHand(player3, diagonal), NetworkSide.Server, world3));
	}

	[Fact]
	public void ItemInHand_RevertsToStandardReach()
	{
		var (player, world, target) = Setup(3, 0);
		Assert.False(DefaultWillInteract.Default(HoldingItem(player, target), NetworkSide.Server, world));

		var (player2, world2, adjacent) = Setup(1, 0);
		Assert.True(DefaultWillInteract.Default(HoldingItem(player2, adjacent), NetworkSide.Server, world2));
	}

	[Fact]
	public void WithoutTelekinesis_StandardReachApplies()
	{
		var (player, world, target) = Setup(3, 0, hasTelekinesis: false);
		Assert.False(DefaultWillInteract.Default(EmptyHand(player, target), NetworkSide.Server, world));
	}

	[Fact]
	public void Interaction_CanVetoTelekinesis()
	{
		var (player, world, target) = Setup(3, 0);
		Assert.False(DefaultWillInteract.Default(EmptyHand(player, target), NetworkSide.Server, world,
			allowTelekinesis: false));
	}

	[Fact]
	public void PositionalHandApply_TelekinesisMeasuresToTargetObject()
	{
		// The telekinesis branch validates against the target object's position, not the
		// clicked position - preserved from US13, where CanApply's telekinesis case ignores
		// targetPosition.
		var (player, world, target) = Setup(10, 0);
		var positional = PositionalHandApply.ByClient(player, null, target, new Vector2(10, 0),
			new FakeSlot(), Intent.Help, BodyPartType.Chest, false);

		Assert.True(DefaultWillInteract.Default(positional, NetworkSide.Server, world));
	}
}
