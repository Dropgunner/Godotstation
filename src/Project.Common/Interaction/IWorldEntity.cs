using System.Numerics;

namespace Godotstation.Common.Interaction;

/// <summary>
/// A thing that exists at a position in the station world - the engine-agnostic stand-in for a
/// Unity GameObject in interaction logic. The Godot layer (or a test fake) implements this.
///
/// US13 draws positions from several sources (RegisterTile.WorldPosition/WorldPositionServer,
/// transform.position, UniversalObjectPhysics.OfficialPosition) which only diverge mid-movement
/// on moving matrices. This port coalesces them into one authoritative position per network side;
/// the engine adapter is responsible for answering with the equivalent source.
/// </summary>
public interface IWorldEntity
{
	/// <summary>
	/// World position as known by the given side (client prediction vs. server authority).
	/// </summary>
	Vector2 GetWorldPosition(NetworkSide side);

	/// <summary>
	/// The entity's best-known server position - on the server this is authoritative, on the
	/// client it is the last synced server state. Mirrors US13 AssumedWorldPosServer().
	/// </summary>
	Vector2 AssumedWorldPositionServer { get; }
}
