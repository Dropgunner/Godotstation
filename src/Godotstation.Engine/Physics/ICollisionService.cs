using System.Collections.Generic;

namespace Godotstation.Engine.Physics
{
	/// <summary>
	/// Facade over object-vs-object collision queries. Replaces the
	/// BoxCollider2D / Physics2D usage and the RegisterTile object-layer
	/// lookups in UniversalObjectPhysics.
	///
	/// SS13 collision is tile-quantised, not continuous: "what is standing on
	/// tile T" is the primitive, not a swept AABB. The original code only used
	/// Unity colliders as a spatial index into the object layer, so the facade
	/// exposes exactly that index and nothing more. The Godot implementation
	/// will back it with the same tile registry the renderer uses; the test
	/// implementation is a Dictionary&lt;Vector3Int, List&lt;ObjectPhysics&gt;&gt;.
	/// </summary>
	public interface ICollisionService
	{
		/// <summary>
		/// All pushable/collidable objects currently registered on a tile.
		/// Order must be deterministic (registration order) — push chains
		/// resolve in list order and Golden Trace parity depends on it.
		/// </summary>
		IReadOnlyList<ObjectPhysics> GetPushablesAt(Vector3Int tile);

		/// <summary>
		/// Notify the index that an object moved between tiles so subsequent
		/// queries stay coherent within the same simulation tick.
		/// </summary>
		void UpdateRegistration(ObjectPhysics obj, Vector3Int from, Vector3Int to);
	}
}
