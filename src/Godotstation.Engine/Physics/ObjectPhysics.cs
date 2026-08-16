using System;
using System.Numerics;

namespace Godotstation.Engine.Physics
{
	/// <summary>
	/// Replaces Mirror's [SyncVar]. The attribute is metadata only: the
	/// Godotstation.Net layer reflects over it at startup to build sync
	/// delta tables. The engine core never reads it at runtime — sync is
	/// the net layer's problem, simulation is ours.
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class NetworkedAttribute : Attribute
	{
	}

	/// <summary>
	/// Extraction of the NetworkBehaviour surface UniversalObjectPhysics
	/// actually used: "this state changed, replicate it". Implemented by
	/// Godotstation.Net on the server, by a no-op in offline/test runs.
	/// </summary>
	public interface INetworkSync
	{
		void SyncBool(string field, bool value);
		void SyncUInt(string field, uint value);
		void SyncVector2(string field, Vector2 value);
	}

	/// <summary>
	/// Outbound gameplay events the physics core raises but must not handle
	/// itself (damage, sound, VFX are Common/game-layer concerns).
	/// </summary>
	public interface IPhysicsEventSink
	{
		/// <summary>Object hit an impassable tile at or above HIGH_SPEED_COLLISION_THRESHOLD. Damage both parties here.</summary>
		void OnHighSpeedCollision(ObjectPhysics obj, Vector3Int impactTile, float impactSpeed);

		/// <summary>Object reflected off an impassable tile (any speed).</summary>
		void OnBounce(ObjectPhysics obj, Vector3Int impactTile);

		/// <summary>Newtonian flight ended; object is settling onto a tile.</summary>
		void OnThrowEnd(ObjectPhysics obj);
	}

	/// <summary>
	/// Port of Unitystation's UniversalObjectPhysics (US13.Core.Physics),
	/// stripped of MonoBehaviour/NetworkBehaviour and rebuilt as a plain
	/// deterministic object driven by an external fixed-rate tick.
	///
	/// The original is a 1,962-line partial class whose Update() ran at
	/// render framerate with variable deltaTime — meaning throws literally
	/// landed on different tiles at different framerates. We keep the same
	/// integration math (it defines the feel) but require the caller to tick
	/// with a FIXED delta. Determinism falls out: same inputs, same floats,
	/// same trajectory, every machine.
	///
	/// Field names deliberately keep the original's casing (newtonianMovement,
	/// airTime, ...) even where it violates C# convention, so the port can be
	/// diffed side-by-side against the Unity source while Golden Traces are
	/// being validated. Rename in one mechanical pass after B1 sign-off.
	///
	/// Two movement modes coexist, exactly as upstream:
	///  1. Tile stepping — discrete tile-to-tile moves (walking, being
	///     pushed). LocalTargetPosition holds the destination; AnimationUpdate
	///     slides the visual position toward it at the step speed.
	///  2. Newtonian flight — continuous velocity (newtonianMovement) applied
	///     when thrown, pushed hard, or drifting in space. Friction only
	///     exists where gravity does.
	/// </summary>
	public sealed class ObjectPhysics
	{
		// ---- The constants that define the feel. Do not "tune" these. ----
		public const float MAX_SPEED = 25;
		public const int HIGH_SPEED_COLLISION_THRESHOLD = 13;
		public const float DEFAULT_Friction = 15f;
		public const float DEFAULT_SLIDE_FRICTION = 9f;

		/// <summary>
		/// Below this speed newtonian movement is treated as stopped. The
		/// original gates its flying update on magnitude &gt; 0.001f; the stop
		/// snap uses a slightly larger epsilon to avoid a tail of subnormal
		/// drift ticks that upstream's framerate-coupled loop never exhibited.
		/// </summary>
		public const float STOP_SPEED_EPSILON = 0.01f;

		/// <summary>
		/// Seconds to cross one tile when stepping. Source:
		/// MovementSynchronisation.DefaultTime = 0.5f.
		/// </summary>
		public float TileMoveTime = 0.5f;

		public float ObjectBouncyness = 0.75f;

		// ---- Simulation state (the original's [PlayModeOnly] block) ----
		public Vector2 newtonianMovement;
		public Vector3 LocalTargetPosition;
		public bool CorrectingCourse = false;
		public bool Animating = false;
		public float airTime;
		public float slideTime;
		public float spinMagnitude = 0;
		public bool ChangesDirectionPush = false;

		/// <summary>Authoritative position in local (matrix) space. Tile centers are integers.</summary>
		public Vector3 LocalPosition;

		// ---- Networked state (was [SyncVar]) ----
		// NetworkIdentity references become raw netIds: the engine core has no
		// object registry and must not — resolution happens in Common/Net.
		[Networked] private uint thrownBy;
		[Networked] private uint thrownProtection;
		[Networked] private bool intangible = false;

		public uint ThrownBy
		{
			get => thrownBy;
			set
			{
				thrownBy = value;
				sync?.SyncUInt(nameof(ThrownBy), value);
			}
		}

		public uint ThrownProtection
		{
			get => thrownProtection;
			set
			{
				thrownProtection = value;
				sync?.SyncUInt(nameof(ThrownProtection), value);
			}
		}

		/// <summary>Intangible objects ignore all collision (ghosts, projectiles mid-spawn).</summary>
		public bool Intangible
		{
			get => intangible;
			set
			{
				intangible = value;
				sync?.SyncBool(nameof(Intangible), value);
			}
		}

		public CollisionType CollisionType = CollisionType.Object;

		/// <summary>Object we are pulling; it steps into the tile we vacate.</summary>
		public ObjectPhysics? Pulling;

		private readonly ITileGridService grid;
		private readonly ICollisionService collision;
		private readonly IPhysicsEventSink? events;
		private readonly INetworkSync? sync;
		private readonly bool isServer;

		public ObjectPhysics(
			ITileGridService grid,
			ICollisionService collision,
			IPhysicsEventSink? events = null,
			INetworkSync? sync = null,
			bool isServer = true)
		{
			this.grid = grid;
			this.collision = collision;
			this.events = events;
			this.sync = sync;
			this.isServer = isServer;
		}

		public Vector3Int LocalTile => Vector3Int.RoundFrom(LocalPosition);

		public bool IsMovingNewtonian => newtonianMovement.LengthSquared() > STOP_SPEED_EPSILON * STOP_SPEED_EPSILON;

		// =====================================================================
		// Impulse entry point
		// =====================================================================

		/// <summary>
		/// Apply a newtonian impulse. Signature mirrors the original
		/// NewtonianPush(worldDirection, speed, nairTime, inSlideTime, inAim,
		/// inThrownBy, spinFactor) minus the body-part aim, which belongs to
		/// combat, not physics.
		///
		/// NaN sentinels for the timers reproduce upstream's "don't touch the
		/// existing timer unless the caller supplied one" contract — a second
		/// shove mid-flight must not reset a throw's air time.
		/// </summary>
		public void NewtonianPush(
			Vector2 worldDirection,
			float speed,
			float nairTime = float.NaN,
			float inSlideTime = float.NaN,
			float spinFactor = 0,
			uint inThrownBy = 0)
		{
			// Note: intangibility does NOT gate impulses. Intangible objects
			// (projectiles, ghosts) still carry momentum — they just skip
			// collision in FlyingUpdate.
			if (float.IsNaN(nairTime) == false)
			{
				airTime = nairTime;
			}

			if (float.IsNaN(inSlideTime) == false)
			{
				slideTime = inSlideTime;
			}

			if (spinFactor != 0)
			{
				spinMagnitude = spinFactor;
			}

			if (inThrownBy != 0)
			{
				ThrownBy = inThrownBy;
				// You can't be hit by your own throw the frame it leaves your hand.
				ThrownProtection = inThrownBy;
			}

			var lengthSquared = worldDirection.LengthSquared();
			if (lengthSquared > 0)
			{
				newtonianMovement += worldDirection / MathF.Sqrt(lengthSquared) * speed;
			}

			ClampToMaxSpeed();
			sync?.SyncVector2(nameof(newtonianMovement), newtonianMovement);
		}

		private void ClampToMaxSpeed()
		{
			var magnitude = newtonianMovement.Length();
			if (magnitude > MAX_SPEED)
			{
				newtonianMovement *= MAX_SPEED / magnitude;
			}
		}

		// =====================================================================
		// Newtonian flight tick (was FlyingUpdateMe, driven by UpdateManager)
		// =====================================================================

		/// <summary>
		/// Advance newtonian movement by one fixed timestep. Call order per
		/// tick matches upstream: timers → integrate → collide/bounce →
		/// friction → stop check. Reordering any of these changes where
		/// throws land; the Golden Trace suite pins the order down.
		/// </summary>
		public void FlyingUpdate(float deltaTime)
		{
			if (IsMovingNewtonian == false)
			{
				return;
			}

			// --- Phase 1: contact/friction classification ------------------
			// Air time counts down first and suppresses ALL friction: a thrown
			// object doesn't scrape the floor until it lands. Then slide time
			// (knocked-down players skidding) applies the lower slide
			// coefficient. Only then does normal floor friction exist — and
			// only under gravity. In space nothing decays: Newton's first law
			// is the space-drift feature, not a special case.
			float appliedFriction = 0f;
			bool hasGravity = grid.HasGravityAt(LocalTile);

			if (airTime > 0)
			{
				airTime -= deltaTime;
			}
			else if (slideTime > 0)
			{
				slideTime -= deltaTime;
				if (hasGravity)
				{
					appliedFriction = DEFAULT_SLIDE_FRICTION;
				}
			}
			else if (hasGravity)
			{
				appliedFriction = grid.IsSlipperyAt(LocalTile) ? DEFAULT_SLIDE_FRICTION : DEFAULT_Friction;
			}

			// --- Phase 2: integrate -----------------------------------------
			var impactSpeed = newtonianMovement.Length();
			var startTile = LocalTile;
			var targetPosition = LocalPosition + new Vector3(newtonianMovement.X, newtonianMovement.Y, 0) * deltaTime;
			var targetTile = Vector3Int.RoundFrom(targetPosition);

			// --- Phase 3: tile collision ------------------------------------
			if (intangible == false
			    && targetTile != startTile
			    && grid.IsPassableAt(startTile, targetTile, CollisionType, isServer) == false)
			{
				// High-speed collision fires BEFORE the bounce, at pre-impact
				// speed. Upstream compares against the threshold with '>'
				// (13.0 exactly does not hurt); preserve that.
				if (impactSpeed > HIGH_SPEED_COLLISION_THRESHOLD)
				{
					events?.OnHighSpeedCollision(this, targetTile, impactSpeed);
				}

				Bounce(startTile, targetTile);
			}
			else
			{
				if (targetTile != startTile)
				{
					collision.UpdateRegistration(this, startTile, targetTile);
				}

				LocalPosition = targetPosition;
			}

			// --- Phase 4: friction decay ------------------------------------
			// Friction is a flat deceleration (units/s²), not proportional
			// drag. That is why thrown items in SS13 stop dead rather than
			// asymptotically gliding: the decay curve is linear in time.
			if (appliedFriction > 0)
			{
				var magnitude = newtonianMovement.Length();
				var newMagnitude = magnitude - appliedFriction * deltaTime;
				if (newMagnitude <= STOP_SPEED_EPSILON)
				{
					newtonianMovement = Vector2.Zero;
				}
				else
				{
					newtonianMovement *= newMagnitude / magnitude;
				}
			}

			// --- Phase 5: stop ------------------------------------------------
			if (IsMovingNewtonian == false)
			{
				StopFlight();
			}
		}

		/// <summary>
		/// Reflect off an impassable tile. Each axis is tested independently
		/// so a diagonal impact against a flat wall keeps its tangential
		/// component — objects skim along walls instead of stopping dead,
		/// which is very visible with thrown items in corridors. Only a true
		/// corner reverses both axes. Bounce energy loss is ObjectBouncyness
		/// per reflected axis.
		/// </summary>
		private void Bounce(Vector3Int from, Vector3Int to)
		{
			var delta = to - from;

			bool blockedX = delta.X != 0
				&& grid.IsPassableAt(from, new Vector3Int(from.X + Math.Sign(delta.X), from.Y, from.Z), CollisionType, isServer) == false;
			bool blockedY = delta.Y != 0
				&& grid.IsPassableAt(from, new Vector3Int(from.X, from.Y + Math.Sign(delta.Y), from.Z), CollisionType, isServer) == false;

			// Diagonal target blocked but both cardinals open: outside corner.
			// Reflect fully — the original does the same via its collider normal.
			if (blockedX == false && blockedY == false)
			{
				blockedX = true;
				blockedY = true;
			}

			newtonianMovement = new Vector2(
				blockedX ? -newtonianMovement.X * ObjectBouncyness : newtonianMovement.X,
				blockedY ? -newtonianMovement.Y * ObjectBouncyness : newtonianMovement.Y);

			events?.OnBounce(this, to);
		}

		/// <summary>
		/// Flight is over: clear throw state and settle onto the nearest tile
		/// center via the normal tile-step animation, exactly like upstream's
		/// landing behaviour (objects never rest between tiles).
		/// </summary>
		private void StopFlight()
		{
			newtonianMovement = Vector2.Zero;
			airTime = 0;
			slideTime = 0;
			spinMagnitude = 0;
			ThrownProtection = 0;

			SetLocalTarget(LocalTile);
			events?.OnThrowEnd(this);
		}

		// =====================================================================
		// Tile stepping (walk / push / pull)
		// =====================================================================

		/// <summary>
		/// Attempt a discrete one-tile move in a cardinal/diagonal direction,
		/// pushing whatever stands in the destination tile ahead of us,
		/// recursively (crate trains). Returns false — and moves nothing — if
		/// the destination or anything in the push chain is blocked. The
		/// all-or-nothing contract matters: a half-resolved push chain would
		/// desync client prediction instantly.
		/// </summary>
		public bool TryTilePush(Vector3Int worldDirection, float speed = 1f)
		{
			var origin = LocalTile;
			var target = origin + worldDirection;

			if (intangible == false
			    && grid.IsPassableAt(origin, target, CollisionType, isServer) == false)
			{
				return false;
			}

			var occupants = collision.GetPushablesAt(target);
			for (int i = 0; i < occupants.Count; i++)
			{
				var occupant = occupants[i];
				if (ReferenceEquals(occupant, this) || occupant.Intangible)
				{
					continue;
				}

				if (occupant.TryTilePush(worldDirection, speed) == false)
				{
					return false;
				}
			}

			collision.UpdateRegistration(this, origin, target);
			SetLocalTarget(target);

			// The pulled object steps into the tile we just vacated. Guard
			// against pull cycles (A pulls B pulls A) by checking the pulled
			// object isn't already where it's going.
			if (Pulling != null && ReferenceEquals(Pulling, this) == false)
			{
				var pullDirection = origin - Pulling.LocalTile;
				if (pullDirection != Vector3Int.Zero)
				{
					Pulling.TryTilePush(pullDirection, speed);
				}
			}

			return true;
		}

		private void SetLocalTarget(Vector3Int tile)
		{
			LocalTargetPosition = tile.ToCenter();
			Animating = true;
		}

		/// <summary>
		/// Advance the tile-step animation by one fixed timestep (was
		/// AnimationUpdateMe). Moves LocalPosition toward LocalTargetPosition
		/// at one tile per TileMoveTime seconds; arrival is exact, never
		/// overshot, so a step always ends bit-identically on the tile center.
		/// </summary>
		public void AnimationUpdate(float deltaTime)
		{
			if (Animating == false)
			{
				return;
			}

			var remaining = LocalTargetPosition - LocalPosition;
			var remainingLength = remaining.Length();
			var step = deltaTime / TileMoveTime;

			if (remainingLength <= step)
			{
				LocalPosition = LocalTargetPosition;
				Animating = false;
			}
			else
			{
				LocalPosition += remaining / remainingLength * step;
			}
		}

		/// <summary>
		/// Combined fixed tick, in upstream's UpdateManager registration
		/// order: flight first, then step animation. One object is only ever
		/// in one mode, but the ordering still matters on the transition tick
		/// (flight ending queues a settle-step the same tick).
		/// </summary>
		public void PhysicsTick(float fixedDeltaTime)
		{
			FlyingUpdate(fixedDeltaTime);
			AnimationUpdate(fixedDeltaTime);
		}
	}
}
