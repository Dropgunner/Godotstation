using System.Numerics;
using Godotstation.Common.Interactions;
using Xunit;

namespace Godotstation.Tests.Interactions;

/// <summary>FC-SC-4: the dispatcher reads the performer's live conscious state, not a two-valued IsAlive flag.</summary>
public sealed class LivePerformerStateTests
{
    private sealed class Performer : IPerformer
    {
        public Performer() => InteractionPlayer.PlaceAt(Vector2.Zero);
        public Vector2 WorldPosition => Vector2.Zero;
        public bool IsGhosting => false;
        public bool HasTelekinesis => false;
        public bool IsAlive => ConsciousState != ConsciousState.Dead;
        public ConsciousState ConsciousState { get; set; } = ConsciousState.Conscious;
        public IInteractable? ActiveHandItem => null;
        public Intent CurrentIntent => Intent.Help;
        public PlayerEntity InteractionPlayer { get; } = new PlayerEntity { Id = 7, Name = "p" };
    }

    private sealed class Target : WorldEntity, IInteractable
    {
        public Target() { Id = 500; Name = "target"; PlaceAt(Vector2.UnitX); }
        string IInteractable.Id => Name;
        public Vector2 WorldPosition => Vector2.UnitX;
        public WorldEntity InteractionEntity => this;
    }

    private sealed class AcceptAll(WorldEntity owner) : IOwnedInteractionHandler<HandApply>
    {
        public int Performed;
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;
        public bool WillInteract(HandApply interaction, NetworkSide side) => interaction.TargetObject == Owner;
        public void ServerPerformInteraction(HandApply interaction) => Performed++;
    }

    [Theory]
    [InlineData(ConsciousState.Conscious, true)]
    [InlineData(ConsciousState.BarelyConscious, false)]
    [InlineData(ConsciousState.Unconscious, false)]
    [InlineData(ConsciousState.Dead, false)]
    public void DispatcherUsesLiveConsciousState(ConsciousState state, bool expected)
    {
        var dispatcher = new InteractionDispatcher(UnobstructedMap.Instance);
        var target = new Target();
        var handler = new AcceptAll(target);
        dispatcher.RegisterHandler(handler);
        var performer = new Performer { ConsciousState = state };

        bool resolved = dispatcher.TryResolveClick(performer, null, target, target.WorldPosition, Intent.Help, false, NetworkSide.Server);

        Assert.Equal(expected, resolved);
        Assert.Equal(expected ? 1 : 0, handler.Performed);
        Assert.Equal(state, performer.InteractionPlayer.ConsciousState);
    }

    [Fact]
    public void DefaultConsciousStateStillFollowsIsAlive()
    {
        // Existing performers that only expose IsAlive keep the old two-valued mapping.
        IPerformer alive = new LegacyPerformer(true);
        IPerformer dead = new LegacyPerformer(false);
        Assert.Equal(ConsciousState.Conscious, alive.ConsciousState);
        Assert.Equal(ConsciousState.Dead, dead.ConsciousState);
    }

    private sealed class LegacyPerformer(bool alive) : IPerformer
    {
        public Vector2 WorldPosition => Vector2.Zero;
        public bool IsGhosting => false;
        public bool HasTelekinesis => false;
        public bool IsAlive => alive;
        public IInteractable? ActiveHandItem => null;
        public Intent CurrentIntent => Intent.Help;
        public PlayerEntity InteractionPlayer { get; } = new PlayerEntity();
    }
}
