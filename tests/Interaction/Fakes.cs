using System;
using System.Collections.Generic;
using System.Numerics;
using Godotstation.Common.Interaction;

namespace Godotstation.Common.Tests.Interaction;

internal class FakeEntity : IWorldEntity
{
	public Vector2 Position { get; set; }

	/// <summary>Set to simulate client/server desync; defaults to <see cref="Position"/>.</summary>
	public Vector2? ServerPosition { get; set; }

	public FakeEntity(float x = 0, float y = 0) => Position = new Vector2(x, y);

	public Vector2 GetWorldPosition(NetworkSide side) =>
		side == NetworkSide.Server ? ServerPosition ?? Position : Position;

	public Vector2 AssumedWorldPositionServer => ServerPosition ?? Position;
}

internal class FakePlayer : FakeEntity, IPlayerEntity
{
	public PlayerTypes PlayerType { get; set; } = PlayerTypes.Normal;
	public bool IsCuffed { get; set; }
	public bool AllowInput { get; set; } = true;
	public ConsciousState ConsciousState { get; set; } = ConsciousState.CONSCIOUS;
	public bool IsHidden { get; set; }
	public IWorldEntity? ContainedIn { get; set; }
	public bool HasTelekinesis { get; set; }

	public FakePlayer(float x = 0, float y = 0) : base(x, y) { }
}

internal class FakeSlot : IItemSlot
{
	public IWorldEntity? Item { get; set; }
	public IPlayerEntity? RootPlayer { get; set; }
	public bool IsHandSlot { get; set; } = true;
}

internal class FakeWorld : IInteractionWorld
{
	public IPlayerEntity? LocalPlayer { get; set; }

	/// <summary>Reach linecast result; defaults to "nothing blocks".</summary>
	public Func<Vector2, Vector2, bool> PassableRule { get; set; } = (_, _) => true;

	public Dictionary<IWorldEntity, IItemSlot> ContainingSlots { get; } = new();
	public Dictionary<IWorldEntity, PhysicsSnapshot> Physics { get; } = new();

	public bool IsPassableForReach(Vector2 from, Vector2 to, bool isServer,
		IWorldEntity? context = null) => PassableRule(from, to);

	public IItemSlot? GetContainingSlot(IWorldEntity entity) =>
		ContainingSlots.TryGetValue(entity, out var slot) ? slot : null;

	public bool TryGetPhysics(IWorldEntity? entity, out PhysicsSnapshot physics)
	{
		physics = default;
		return entity != null && Physics.TryGetValue(entity, out physics);
	}
}

internal class FakeClientContext : IClientInteractionContext
{
	public FakePlayer Player { get; set; } = new();
	public IPlayerEntity LocalPlayer => Player;
	public bool IsGhosting { get; set; }
	public bool HasItemStorage { get; set; } = true;
	public IItemSlot? ActiveHandSlot { get; set; } = new FakeSlot();
	public Intent CurrentIntent { get; set; } = Intent.Help;
	public BodyPartType TargetedBodyPart { get; set; } = BodyPartType.Chest;
	public bool IsAltActionKeyPressed { get; set; }
}
