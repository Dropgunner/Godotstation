using System.Numerics;

namespace Godotstation.Common.Interaction;

/// <summary>
/// Environment queries the interaction rules need answered. Replaces the US13 singletons
/// (MatrixManager, PlayerManager) with an injected context so the logic stays headless and
/// deterministic. The Godot layer implements this against the real tilemap; tests implement fakes.
/// </summary>
public interface IInteractionWorld
{
	/// <summary>
	/// The player this process controls, or null on a dedicated server.
	/// US13: PlayerManager.LocalPlayerScript.
	/// </summary>
	IPlayerEntity? LocalPlayer { get; }

	/// <summary>
	/// True if a click can pass between the two world positions. Implements the US13
	/// MatrixManager.IsPassableAtAllMatrices reach linecast: CollisionType.Click, players not
	/// included, with Walls/Windows/Grills excluded on the destination tile only (so you can
	/// click a window, but not through it).
	/// </summary>
	/// <param name="context">If not null, collisions caused by this entity are ignored.</param>
	bool IsPassableForReach(Vector2 from, Vector2 to, bool isServer, IWorldEntity? context = null);

	/// <summary>
	/// The item slot the entity currently sits in, or null if it is not inside any item storage.
	/// US13: Pickupable.ItemSlot (when ItemSlot.ItemStorage is non-null).
	/// </summary>
	IItemSlot? GetContainingSlot(IWorldEntity entity);

	/// <summary>
	/// Physics state for an entity, used by <see cref="ReachRange.ExtendedServer"/> to catch
	/// objects that just flew out of reach. Returns false if the entity is null or has no
	/// physics body (US13: no UniversalObjectPhysics component).
	/// </summary>
	bool TryGetPhysics(IWorldEntity? entity, out PhysicsSnapshot physics);
}

/// <summary>
/// Snapshot of the physics fields ExtendedServer reach needs. US13: UniversalObjectPhysics.
/// </summary>
public readonly struct PhysicsSnapshot
{
	/// <summary>US13: UniversalObjectPhysics.OfficialPosition.</summary>
	public Vector2 OfficialPosition { get; }

	/// <summary>Velocity from a throw/push, in tiles per tick. US13: NewtonianMovement.</summary>
	public Vector2 NewtonianMovement { get; }

	public PhysicsSnapshot(Vector2 officialPosition, Vector2 newtonianMovement)
	{
		OfficialPosition = officialPosition;
		NewtonianMovement = newtonianMovement;
	}
}
