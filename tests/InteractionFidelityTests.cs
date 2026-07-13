using System;
using System.Numerics;
using Godotstation.Common.Interaction;
using Godotstation.Common.Interaction.Types;
using Godotstation.Common.Interaction.Validation;
using Xunit;

namespace Godotstation.Tests;

/// <summary>
/// Fidelity Contract tests for the Interaction System.
/// These tests enforce the behaviors that define SS13 "game feel."
/// </summary>
public class InteractionFidelityTests
{
    // --- Test Helpers ---

    private class TestPerformer : IPerformer
    {
        public Vector2 WorldPosition { get; set; } = Vector2.Zero;
        public bool IsGhosting { get; set; } = false;
        public bool HasTelekinesis { get; set; } = false;
        public bool IsAlive { get; set; } = true;
        public IInteractable? ActiveHandItem { get; set; } = null;
        public Intent CurrentIntent { get; set; } = Intent.Help;
    }

    private class TestInteractable : IInteractable
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public Vector2 WorldPosition { get; set; } = Vector2.Zero;
    }

    private class TestHandApplyHandler : IInteractionHandler<HandApply>
    {
        public int Priority { get; set; } = 0;
        public Intent? LastReceivedIntent { get; private set; }
        public bool LastReceivedAltClick { get; private set; }
        public bool ShouldAccept { get; set; } = true;
        public int CallCount { get; private set; } = 0;

        public bool WillInteract(HandApply interaction, NetworkSide side)
        {
            return ShouldAccept;
        }

        public void ServerPerformInteraction(HandApply interaction)
        {
            LastReceivedIntent = interaction.Intent;
            LastReceivedAltClick = interaction.IsAltClick;
            CallCount++;
        }
    }

    private class TestPositionalHandler : IInteractionHandler<PositionalHandApply>
    {
        public int Priority { get; set; } = 0;
        public bool ShouldAccept { get; set; } = true;
        public int CallCount { get; private set; } = 0;

        public bool WillInteract(PositionalHandApply interaction, NetworkSide side) => ShouldAccept;
        public void ServerPerformInteraction(PositionalHandApply interaction) => CallCount++;
    }

    private class TestTileHandler : IInteractionHandler<TileApply>
    {
        public int Priority { get; set; } = 0;
        public bool ShouldAccept { get; set; } = true;
        public int CallCount { get; private set; } = 0;

        public bool WillInteract(TileApply interaction, NetworkSide side) => ShouldAccept;
        public void ServerPerformInteraction(TileApply interaction) => CallCount++;
    }

    private class TestOwnedHandApplyHandler : IOwnedInteractionHandler<HandApply>
    {
        public required IInteractable Owner { get; init; }
        public bool IsFirstInteractable { get; init; } = false;
        public int Priority { get; set; } = 0;
        public bool ShouldAccept { get; set; } = true;
        public int CallCount { get; private set; } = 0;

        public bool WillInteract(HandApply interaction, NetworkSide side) => ShouldAccept;
        public void ServerPerformInteraction(HandApply interaction) => CallCount++;
    }

    // --- FC-1: Intent-Dependent Interaction Resolution ---

    [Theory]
    [InlineData(Intent.Help)]
    [InlineData(Intent.Disarm)]
    [InlineData(Intent.Grab)]
    [InlineData(Intent.Harm)]
    public void FC1_IntentIsPassedToHandler(Intent intent)
    {
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0), CurrentIntent = intent };
        var target = new TestInteractable { WorldPosition = new Vector2(1, 0) };
        var handler = new TestHandApplyHandler();

        var dispatcher = new InteractionDispatcher();
        dispatcher.RegisterHandler(handler);

        dispatcher.TryResolveClick(performer, null, target, target.WorldPosition, intent, false, NetworkSide.Server);

        Assert.Equal(intent, handler.LastReceivedIntent);
    }

    // --- FC-2: Reach Validation — Standard Range ---

    [Fact]
    public void FC2_StandardReach_AdjacentTile_Passes()
    {
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0) };
        var target = new TestInteractable { WorldPosition = new Vector2(1, 0) };
        var interaction = new HandApply(performer, null, target);

        Assert.True(InteractionValidator.CanInteract(interaction, NetworkSide.Server));
    }

    [Fact]
    public void FC2_StandardReach_TwoTilesAway_Fails()
    {
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0) };
        var target = new TestInteractable { WorldPosition = new Vector2(2, 0) };
        var interaction = new HandApply(performer, null, target);

        Assert.False(InteractionValidator.CanInteract(interaction, NetworkSide.Server));
    }

    // --- FC-3: Telekinesis Reach Extension (Empty Hand Only) ---

    [Fact]
    public void FC3_Telekinesis_EmptyHand_ExtendedReach()
    {
        var performer = new TestPerformer
        {
            WorldPosition = new Vector2(0, 0),
            HasTelekinesis = true,
            ActiveHandItem = null // Empty hand
        };
        var target = new TestInteractable { WorldPosition = new Vector2(3, 0) };
        var interaction = new HandApply(performer, null, target);

        Assert.True(InteractionValidator.CanInteract(interaction, NetworkSide.Server));
    }

    [Fact]
    public void FC3_Telekinesis_ItemInHand_StandardReach()
    {
        var heldItem = new TestInteractable();
        var performer = new TestPerformer
        {
            WorldPosition = new Vector2(0, 0),
            HasTelekinesis = true,
            ActiveHandItem = heldItem // Item in hand
        };
        var target = new TestInteractable { WorldPosition = new Vector2(3, 0) };
        var interaction = new HandApply(performer, heldItem, target);

        Assert.False(InteractionValidator.CanInteract(interaction, NetworkSide.Server));
    }

    // --- FC-4: Interaction Type Resolution Order ---

    [Fact]
    public void FC4_ResolutionOrder_PositionalFirst_ThenHandApply_ThenTile()
    {
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0) };
        var target = new TestInteractable { WorldPosition = new Vector2(1, 0) };

        var positionalHandler = new TestPositionalHandler { ShouldAccept = true };
        var handApplyHandler = new TestHandApplyHandler { ShouldAccept = true };
        var tileHandler = new TestTileHandler { ShouldAccept = true };

        var dispatcher = new InteractionDispatcher();
        dispatcher.RegisterHandler(positionalHandler);
        dispatcher.RegisterHandler(handApplyHandler);
        dispatcher.RegisterHandler(tileHandler);

        dispatcher.TryResolveClick(performer, null, target, target.WorldPosition, Intent.Help, false, NetworkSide.Server);

        // Only the highest priority (PositionalHandApply) should fire
        Assert.Equal(1, positionalHandler.CallCount);
        Assert.Equal(0, handApplyHandler.CallCount);
        Assert.Equal(0, tileHandler.CallCount);
    }

    [Fact]
    public void FC4_ResolutionOrder_PositionalRejects_FallsToHandApply()
    {
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0) };
        var target = new TestInteractable { WorldPosition = new Vector2(1, 0) };

        var positionalHandler = new TestPositionalHandler { ShouldAccept = false };
        var handApplyHandler = new TestHandApplyHandler { ShouldAccept = true };

        var dispatcher = new InteractionDispatcher();
        dispatcher.RegisterHandler(positionalHandler);
        dispatcher.RegisterHandler(handApplyHandler);

        dispatcher.TryResolveClick(performer, null, target, target.WorldPosition, Intent.Help, false, NetworkSide.Server);

        Assert.Equal(0, positionalHandler.CallCount);
        Assert.Equal(1, handApplyHandler.CallCount);
    }

    // --- FC-5: Ghost State Blocks All Interactions ---

    [Fact]
    public void FC5_GhostState_BlocksHandApply()
    {
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0), IsGhosting = true };
        var target = new TestInteractable { WorldPosition = new Vector2(1, 0) };
        var interaction = new HandApply(performer, null, target);

        Assert.False(InteractionValidator.CanInteract(interaction, NetworkSide.Server));
    }

    [Fact]
    public void FC5_GhostState_DispatcherReturnsfalse()
    {
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0), IsGhosting = true };
        var target = new TestInteractable { WorldPosition = new Vector2(1, 0) };
        var handler = new TestHandApplyHandler();

        var dispatcher = new InteractionDispatcher();
        dispatcher.RegisterHandler(handler);

        var result = dispatcher.TryResolveClick(performer, null, target, target.WorldPosition, Intent.Help, false, NetworkSide.Server);

        Assert.False(result);
        Assert.Equal(0, handler.CallCount);
    }

    // --- FC-6: Alt-Click Produces Distinct Behavior ---

    [Fact]
    public void FC6_AltClick_FlagPassedToHandler()
    {
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0) };
        var target = new TestInteractable { WorldPosition = new Vector2(1, 0) };
        var handler = new TestHandApplyHandler();

        var dispatcher = new InteractionDispatcher();
        dispatcher.RegisterHandler(handler);

        // Simulate: positional handler not registered, so falls to HandApply
        dispatcher.TryResolveClick(performer, null, target, target.WorldPosition, Intent.Help, true, NetworkSide.Server);

        Assert.True(handler.LastReceivedAltClick);
    }

    [Fact]
    public void FC6_NormalClick_AltClickFlagIsFalse()
    {
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0) };
        var target = new TestInteractable { WorldPosition = new Vector2(1, 0) };
        var handler = new TestHandApplyHandler();

        var dispatcher = new InteractionDispatcher();
        dispatcher.RegisterHandler(handler);

        dispatcher.TryResolveClick(performer, null, target, target.WorldPosition, Intent.Help, false, NetworkSide.Server);

        Assert.False(handler.LastReceivedAltClick);
    }

    // --- FC-7: MouseDrop Requires Both Objects in Mutual Reach ---

    [Fact]
    public void FC7_MouseDrop_ObjectsFarApart_Fails()
    {
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 1) };
        var droppedObject = new TestInteractable { WorldPosition = new Vector2(0, 0) };
        var targetObject = new TestInteractable { WorldPosition = new Vector2(5, 5) };

        var interaction = new MouseDrop(performer, droppedObject, targetObject);

        // Performer is adjacent to dropped object, but dropped and target are far apart
        Assert.False(InteractionValidator.CanInteract(interaction, NetworkSide.Server));
    }

    [Fact]
    public void FC7_MouseDrop_ObjectsAdjacent_Passes()
    {
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0) };
        var droppedObject = new TestInteractable { WorldPosition = new Vector2(0, 0) };
        var targetObject = new TestInteractable { WorldPosition = new Vector2(1, 0) };

        var interaction = new MouseDrop(performer, droppedObject, targetObject);

        Assert.True(InteractionValidator.CanInteract(interaction, NetworkSide.Server));
    }

    // --- FC-8: Held Item Gets First Refusal ---

    [Fact]
    public void FC8_HeldItemHandler_FiresBeforeTargetHandler()
    {
        var heldItem = new TestInteractable();
        var target = new TestInteractable { WorldPosition = new Vector2(1, 0) };
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0), ActiveHandItem = heldItem };

        var itemHandler = new TestOwnedHandApplyHandler { Owner = heldItem, ShouldAccept = true };
        var targetHandler = new TestOwnedHandApplyHandler { Owner = target, ShouldAccept = true };

        var dispatcher = new InteractionDispatcher();
        dispatcher.RegisterHandler(itemHandler);
        dispatcher.RegisterHandler(targetHandler);

        dispatcher.TryResolveClick(performer, heldItem, target, target.WorldPosition, Intent.Help, false, NetworkSide.Server);

        Assert.Equal(1, itemHandler.CallCount);
        Assert.Equal(0, targetHandler.CallCount);
    }

    [Fact]
    public void FC8_NoItemHeld_TargetHandlerFiresNormally()
    {
        var target = new TestInteractable { WorldPosition = new Vector2(1, 0) };
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0), ActiveHandItem = null };

        var targetHandler = new TestOwnedHandApplyHandler { Owner = target, ShouldAccept = true };

        var dispatcher = new InteractionDispatcher();
        dispatcher.RegisterHandler(targetHandler);

        dispatcher.TryResolveClick(performer, null, target, target.WorldPosition, Intent.Help, false, NetworkSide.Server);

        Assert.Equal(1, targetHandler.CallCount);
    }

    // --- FC-9: First-Interactable Priority Override ---

    [Fact]
    public void FC9_FirstInteractable_FiresBeforeHeldItem()
    {
        var heldItem = new TestInteractable();
        var target = new TestInteractable { WorldPosition = new Vector2(1, 0) };
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0), ActiveHandItem = heldItem };

        var firstHandler = new TestOwnedHandApplyHandler { Owner = target, IsFirstInteractable = true, ShouldAccept = true };
        var itemHandler = new TestOwnedHandApplyHandler { Owner = heldItem, ShouldAccept = true };

        var dispatcher = new InteractionDispatcher();
        dispatcher.RegisterHandler(firstHandler);
        dispatcher.RegisterHandler(itemHandler);

        dispatcher.TryResolveClick(performer, heldItem, target, target.WorldPosition, Intent.Help, false, NetworkSide.Server);

        Assert.Equal(1, firstHandler.CallCount);
        Assert.Equal(0, itemHandler.CallCount);
    }

    [Fact]
    public void FC9_FirstInteractableDeclines_FallsThroughToHeldItem()
    {
        var heldItem = new TestInteractable();
        var target = new TestInteractable { WorldPosition = new Vector2(1, 0) };
        var performer = new TestPerformer { WorldPosition = new Vector2(0, 0), ActiveHandItem = heldItem };

        var firstHandler = new TestOwnedHandApplyHandler { Owner = target, IsFirstInteractable = true, ShouldAccept = false };
        var itemHandler = new TestOwnedHandApplyHandler { Owner = heldItem, ShouldAccept = true };

        var dispatcher = new InteractionDispatcher();
        dispatcher.RegisterHandler(firstHandler);
        dispatcher.RegisterHandler(itemHandler);

        dispatcher.TryResolveClick(performer, heldItem, target, target.WorldPosition, Intent.Help, false, NetworkSide.Server);

        Assert.Equal(0, firstHandler.CallCount);
        Assert.Equal(1, itemHandler.CallCount);
    }
}
