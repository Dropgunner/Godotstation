using System.Collections.Generic;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Interactions;
using Xunit;

namespace Godotstation.Tests.Interactions;

public sealed class IntentSelectionTests
{
	[Fact]
	public void StartsOnHelp()
	{
		Assert.Equal(Intent.Help, new IntentSelection().Current);
	}

	[Theory]
	[InlineData('1', Intent.Help)]
	[InlineData('2', Intent.Disarm)]
	[InlineData('3', Intent.Grab)]
	[InlineData('4', Intent.Harm)]
	public void NumberHotkeysSelectEachIntentDirectly(char key, Intent expected)
	{
		var selection = new IntentSelection();
		selection.Select(Intent.Grab == expected ? Intent.Harm : Intent.Grab);

		Assert.True(selection.TryApplyHotkey(key));
		Assert.Equal(expected, selection.Current);
	}

	[Fact]
	public void CyclingVisitsAllFourIntentsAndWrapsBothWays()
	{
		var selection = new IntentSelection();
		var forward = new List<Intent>();
		for (int i = 0; i < 4; i++) forward.Add(selection.Cycle(forward: true));
		Assert.Equal(new[] { Intent.Disarm, Intent.Grab, Intent.Harm, Intent.Help }, forward);

		Assert.Equal(Intent.Harm, selection.Cycle(forward: false));
		Assert.True(selection.TryApplyHotkey('f'));
		Assert.Equal(Intent.Grab, selection.Current);
		Assert.True(selection.TryApplyHotkey('G'));
		Assert.Equal(Intent.Harm, selection.Current);
	}

	[Fact]
	public void NoneAndOtherKeysLeaveTheSelectionAlone()
	{
		var selection = new IntentSelection();
		selection.Select(Intent.Disarm);
		int changes = 0;
		selection.Changed += _ => changes++;

		Assert.False(selection.Select(Intent.None));
		Assert.False(selection.Select((Intent)7));
		Assert.False(selection.TryApplyHotkey('5'));
		Assert.False(selection.TryApplyHotkey('Q'));
		Assert.Equal(Intent.Disarm, selection.Current);
		Assert.Equal(0, changes);
	}

	[Fact]
	public void ChangedFiresOnlyWhenTheIntentActuallyChanges()
	{
		var selection = new IntentSelection();
		var seen = new List<Intent>();
		selection.Changed += seen.Add;

		selection.Select(Intent.Help);
		selection.TryApplyHotkey('4');
		selection.TryApplyHotkey('4');

		Assert.Equal(new[] { Intent.Harm }, seen);
	}

	[Theory]
	[InlineData('1', Intent.Help)]
	[InlineData('2', Intent.Disarm)]
	[InlineData('3', Intent.Grab)]
	[InlineData('4', Intent.Harm)]
	public void ClickCarriesTheSelectedIntentAndLaterSwitchesDoNotRewriteIt(char key, Intent expected)
	{
		var selection = new IntentSelection();
		selection.TryApplyHotkey(key);
		var player = new PlayerEntity { Id = 1 };
		player.PlaceAt(Vector2.Zero);
		var target = new WorldEntity { Id = 2 };
		target.PlaceAt(Vector2.One);
		var handler = new RecordingHandler(target);
		var dispatcher = new InteractionDispatcher();
		dispatcher.RegisterHandler(handler);

		Assert.True(dispatcher.TryResolveClick(player, null, target, target.WorldPositionServer,
			selection.Current, false, NetworkSide.Server));
		selection.Cycle(forward: true);

		Assert.Equal(expected, handler.Received!.Intent);
	}

	[Theory]
	[InlineData(Intent.Help)]
	[InlineData(Intent.Disarm)]
	[InlineData(Intent.Grab)]
	[InlineData(Intent.Harm)]
	public void SharedStationAcceptsClicksUnderEverySelectableIntent(Intent intent)
	{
		var world = new SharedStationInventoryWorld();
		Assert.True(world.AddActor(1, new Vector2(-1, 1)));

		Assert.True(world.TryInteract(1, SharedStationInventoryWorld.WrenchId, intent, false));
	}

	[Fact]
	public void SharedStationRejectsTheNoneIntent()
	{
		var world = new SharedStationInventoryWorld();
		Assert.True(world.AddActor(1, new Vector2(-1, 1)));

		Assert.False(world.TryInteract(1, SharedStationInventoryWorld.WrenchId, Intent.None, false));
	}

	private sealed class RecordingHandler : IOwnedInteractionHandler<HandApply>
	{
		public RecordingHandler(WorldEntity owner) => Owner = owner;
		public WorldEntity Owner { get; }
		public int Priority => 0;
		public bool IsFirstInteractable => false;
		public HandApply? Received { get; private set; }
		public bool WillInteract(HandApply interaction, NetworkSide side) => true;
		public void ServerPerformInteraction(HandApply interaction) => Received = interaction;
	}
}
