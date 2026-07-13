using System.Numerics;
using Godotstation.Common.Interaction;
using Xunit;

namespace Godotstation.Common.Tests.Interaction;

/// <summary>
/// FC-5: the interaction object carries the player's intent, alt-click flag, targeted body part
/// and hand contents unchanged from creation to handler, and ghosting/ineligible local players
/// get the Invalid sentinel instead of a live interaction.
/// </summary>
public class FC5_PayloadIntegrityTests
{
	private static FakeClientContext Context()
	{
		var item = new FakeEntity(0, 0);
		return new FakeClientContext
		{
			Player = new FakePlayer(0, 0),
			ActiveHandSlot = new FakeSlot { Item = item },
			CurrentIntent = Intent.Harm,
			TargetedBodyPart = BodyPartType.LeftArm,
			IsAltActionKeyPressed = true
		};
	}

	[Fact]
	public void ByLocalPlayer_CarriesFullPayload()
	{
		var client = Context();
		var target = new FakeEntity(1, 0);

		var apply = HandApply.ByLocalPlayer(client, target);

		Assert.Same(client.LocalPlayer, apply.Performer);
		Assert.Same(target, apply.TargetObject);
		Assert.Same(client.ActiveHandSlot, apply.HandSlot);
		Assert.Same(client.ActiveHandSlot!.Item, apply.HandObject);
		Assert.Equal(Intent.Harm, apply.Intent);
		Assert.Equal(BodyPartType.LeftArm, apply.TargetBodyPart);
		Assert.True(apply.IsAltClick);
	}

	[Theory]
	[InlineData(Intent.Help)]
	[InlineData(Intent.Disarm)]
	[InlineData(Intent.Grab)]
	[InlineData(Intent.Harm)]
	public void EveryIntent_IsPreservedVerbatim(Intent intent)
	{
		var client = Context();
		client.CurrentIntent = intent;

		var apply = HandApply.ByLocalPlayer(client, new FakeEntity(1, 0));

		Assert.Equal(intent, apply.Intent);
	}

	[Fact]
	public void EmptyHand_YieldsNullHandObject()
	{
		var client = Context();
		client.ActiveHandSlot = new FakeSlot { Item = null };

		var apply = HandApply.ByLocalPlayer(client, new FakeEntity(1, 0));

		Assert.Null(apply.HandObject);
	}

	[Fact]
	public void GhostingLocalPlayer_GetsInvalidHandApply()
	{
		var client = Context();
		client.IsGhosting = true;

		var apply = HandApply.ByLocalPlayer(client, new FakeEntity(1, 0));

		Assert.Same(HandApply.Invalid, apply);
		Assert.Null(apply.Performer);
	}

	[Fact]
	public void InvalidHandApply_FailsDefaultWillInteract()
	{
		var world = new FakeWorld();
		Assert.False(DefaultWillInteract.Default(HandApply.Invalid, NetworkSide.Server, world));
	}

	[Fact]
	public void NonNormalLocalPlayer_GetsInvalidHandActivate()
	{
		var client = Context();
		client.Player.PlayerType = PlayerTypes.Ghost;

		Assert.Same(HandActivate.Invalid, HandActivate.ByLocalPlayer(client));
	}

	[Fact]
	public void GhostLocalPlayer_GetsInvalidInventoryApply()
	{
		var client = Context();
		client.Player.PlayerType = PlayerTypes.Ghost;

		Assert.Same(InventoryApply.Invalid,
			InventoryApply.ByLocalPlayer(client, new FakeSlot(), new FakeSlot()));
	}

	[Fact]
	public void MissingItemStorage_GetsInvalidPositionalHandApply()
	{
		var client = Context();
		client.HasItemStorage = false;

		Assert.Same(PositionalHandApply.Invalid,
			PositionalHandApply.ByLocalPlayer(client, new FakeEntity(1, 0), new Vector2(1, 0)));
	}

	[Fact]
	public void ByClient_ReconstructsIdenticalPayloadServerSide()
	{
		// The server rebuilds the interaction from the network message; the payload the handler
		// sees must match what the client sent.
		var player = new FakePlayer(0, 0);
		var hand = new FakeEntity(0, 0);
		var slot = new FakeSlot { Item = hand, RootPlayer = player };
		var target = new FakeEntity(1, 0);

		var apply = HandApply.ByClient(player, hand, target, BodyPartType.Eyes, slot,
			Intent.Grab, isAltClick: true);

		Assert.Same(player, apply.Performer);
		Assert.Same(hand, apply.HandObject);
		Assert.Same(target, apply.TargetObject);
		Assert.Same(slot, apply.HandSlot);
		Assert.Equal(BodyPartType.Eyes, apply.TargetBodyPart);
		Assert.Equal(Intent.Grab, apply.Intent);
		Assert.True(apply.IsAltClick);
	}

	[Fact]
	public void MouseDrop_FromWorld_HasNoFromSlot()
	{
		var player = new FakePlayer(0, 0);
		var world = new FakeWorld { LocalPlayer = player };
		var dropped = new FakeEntity(1, 0);

		var drop = MouseDrop.ByClient(player, world, dropped, new FakeEntity(1, 1), Intent.Help);

		Assert.False(drop.IsFromInventory);
		Assert.Same(dropped, drop.DroppedObject);
	}

	[Fact]
	public void MouseDrop_FromInventory_CarriesItsSlot()
	{
		var player = new FakePlayer(0, 0);
		var world = new FakeWorld { LocalPlayer = player };
		var dropped = new FakeEntity(0, 0);
		var slot = new FakeSlot { Item = dropped, RootPlayer = player };
		world.ContainingSlots[dropped] = slot;

		var drop = MouseDrop.ByClient(player, world, dropped, new FakeEntity(1, 1), Intent.Help);

		Assert.True(drop.IsFromInventory);
		Assert.Same(slot, drop.FromSlot);
	}
}
