using System;
using System.Numerics;

namespace Godotstation.Engine.Physics
{
	/// <summary>
	/// Integer tile coordinate. Replaces UnityEngine.Vector3Int.
	///
	/// Unitystation rounds a float local position to its tile with
	/// Mathf.RoundToInt, which is banker's rounding-free: 0.5 always rounds
	/// up. MathF.Round with MidpointRounding.AwayFromZero reproduces that for
	/// positive values; for negative values Unity's RoundToInt(-0.5) == 0
	/// (rounds toward +infinity), so we must NOT use AwayFromZero blindly.
	/// RoundToInt(x) == floor(x + 0.5) exactly, for all x. We use that form.
	/// Getting this wrong shifts every tile boundary by one ULP worth of
	/// gameplay: throws land one tile short. This is the kind of detail
	/// Golden Trace tests exist to catch.
	/// </summary>
	public readonly struct Vector3Int : IEquatable<Vector3Int>
	{
		public readonly int X;
		public readonly int Y;
		public readonly int Z;

		public Vector3Int(int x, int y, int z)
		{
			X = x;
			Y = y;
			Z = z;
		}

		/// <summary>Unity Mathf.RoundToInt semantics: floor(x + 0.5).</summary>
		public static int RoundToInt(float value)
		{
			return (int)MathF.Floor(value + 0.5f);
		}

		public static Vector3Int RoundFrom(Vector3 position)
		{
			return new Vector3Int(RoundToInt(position.X), RoundToInt(position.Y), RoundToInt(position.Z));
		}

		public Vector3 ToCenter()
		{
			return new Vector3(X, Y, Z);
		}

		public static Vector3Int operator +(Vector3Int a, Vector3Int b) => new Vector3Int(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
		public static Vector3Int operator -(Vector3Int a, Vector3Int b) => new Vector3Int(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
		public static bool operator ==(Vector3Int a, Vector3Int b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
		public static bool operator !=(Vector3Int a, Vector3Int b) => !(a == b);

		public bool Equals(Vector3Int other) => this == other;
		public override bool Equals(object? obj) => obj is Vector3Int other && this == other;
		public override int GetHashCode() => HashCode.Combine(X, Y, Z);
		public override string ToString() => $"({X}, {Y}, {Z})";

		public static readonly Vector3Int Zero = new Vector3Int(0, 0, 0);
		public static readonly Vector3Int Up = new Vector3Int(0, 1, 0);
		public static readonly Vector3Int Down = new Vector3Int(0, -1, 0);
		public static readonly Vector3Int Left = new Vector3Int(-1, 0, 0);
		public static readonly Vector3Int Right = new Vector3Int(1, 0, 0);
	}

	/// <summary>
	/// What kind of collider the moving object presents to the world.
	/// Mirrors Unitystation's CollisionType (used by MatrixManager passability
	/// checks): players fit through things objects don't, and shuttles collide
	/// with almost everything.
	/// </summary>
	public enum CollisionType
	{
		None,
		Player,
		Object,
		Shuttle
	}

	/// <summary>
	/// Facade over the tile world. Replaces every direct MatrixManager /
	/// Matrix / MetaTileMap call in UniversalObjectPhysics.
	///
	/// The Godot layer implements this against its tilemaps; tests implement
	/// it with a dictionary. The physics core never knows the difference —
	/// that is the entire point. Matrix (shuttle) local-to-world transforms
	/// live behind this interface too: the engine core reasons in local tile
	/// space only.
	/// </summary>
	public interface ITileGridService
	{
		/// <summary>
		/// Can an object of the given collision type move from one tile to an
		/// adjacent tile? This is the single question 90% of SS13 physics
		/// reduces to. Mirrors MatrixManager.IsPassableAtAllMatricesV2.
		/// </summary>
		bool IsPassableAt(Vector3Int origin, Vector3Int target, CollisionType collisionType, bool isServer);

		/// <summary>
		/// Does this tile provide gravity (floor plating under a functioning
		/// gravity generator)? No gravity means no friction: newtonian drift.
		/// </summary>
		bool HasGravityAt(Vector3Int position);

		/// <summary>Is the tile slippery (water, lube, ice)? Slippery tiles use slide friction.</summary>
		bool IsSlipperyAt(Vector3Int position);

		/// <summary>Is the tile open space (no floor, no lattice)?</summary>
		bool IsSpaceAt(Vector3Int position);
	}
}
