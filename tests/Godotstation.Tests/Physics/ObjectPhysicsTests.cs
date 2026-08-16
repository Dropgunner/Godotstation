using System;
using System.Collections.Generic;
using System.Numerics;
using Godotstation.Engine.Physics;
using Xunit;

namespace Godotstation.Tests.Physics
{
	/// <summary>
	/// Two layers of testing, deliberately separate:
	///
	/// 1. INVARIANT tests (run now, always green): properties that must hold
	///    for any correct implementation — clamps, conservation, determinism,
	///    Newton's first law in space. These catch regressions in the port
	///    itself.
	///
	/// 2. GOLDEN TRACE tests (skipped until capture): frame-by-frame
	///    position/velocity logs recorded from an instrumented Unity build of
	///    Unitystation running the same scenario. Parity means every frame of
	///    our trace matches the capture within float tolerance. This is the
	///    only honest definition of "feels identical" — invariants can all
	///    pass while a throw still lands one tile short.
	/// </summary>
	public class ObjectPhysicsTests
	{
		private const float Dt = 1f / 60f; // fixed tick, matches planned server rate

		// =====================================================================
		// Test doubles
		// =====================================================================

		private sealed class FakeGrid : ITileGridService
		{
			public bool Gravity = true;
			public bool Slippery = false;
			public readonly HashSet<Vector3Int> Walls = new HashSet<Vector3Int>();

			public bool IsPassableAt(Vector3Int origin, Vector3Int target, CollisionType type, bool isServer)
				=> Walls.Contains(target) == false;

			public bool HasGravityAt(Vector3Int position) => Gravity;
			public bool IsSlipperyAt(Vector3Int position) => Slippery;
			public bool IsSpaceAt(Vector3Int position) => Gravity == false;
		}

		private sealed class FakeCollision : ICollisionService
		{
			public readonly Dictionary<Vector3Int, List<ObjectPhysics>> Registry
				= new Dictionary<Vector3Int, List<ObjectPhysics>>();

			private static readonly List<ObjectPhysics> Empty = new List<ObjectPhysics>();

			public IReadOnlyList<ObjectPhysics> GetPushablesAt(Vector3Int tile)
				=> Registry.TryGetValue(tile, out var list) ? list : Empty;

			public void Register(ObjectPhysics obj, Vector3Int tile)
			{
				if (Registry.TryGetValue(tile, out var list) == false)
				{
					Registry[tile] = list = new List<ObjectPhysics>();
				}

				list.Add(obj);
			}

			public void UpdateRegistration(ObjectPhysics obj, Vector3Int from, Vector3Int to)
			{
				if (Registry.TryGetValue(from, out var list))
				{
					list.Remove(obj);
				}

				Register(obj, to);
			}
		}

		private sealed class RecordingEvents : IPhysicsEventSink
		{
			public readonly List<(Vector3Int tile, float speed)> HighSpeedCollisions = new List<(Vector3Int, float)>();
			public int Bounces;
			public int ThrowEnds;

			public void OnHighSpeedCollision(ObjectPhysics obj, Vector3Int tile, float speed)
				=> HighSpeedCollisions.Add((tile, speed));

			public void OnBounce(ObjectPhysics obj, Vector3Int tile) => Bounces++;
			public void OnThrowEnd(ObjectPhysics obj) => ThrowEnds++;
		}

		private static (ObjectPhysics obj, FakeGrid grid, FakeCollision collision, RecordingEvents events) Make(
			bool gravity = true)
		{
			var grid = new FakeGrid { Gravity = gravity };
			var collision = new FakeCollision();
			var events = new RecordingEvents();
			var obj = new ObjectPhysics(grid, collision, events);
			return (obj, grid, collision, events);
		}

		// =====================================================================
		// Invariants: impulse and speed cap
		// =====================================================================

		[Fact]
		public void NewtonianPush_ClampsSpeed_ToMaxSpeed()
		{
			var (obj, _, _, _) = Make();

			obj.NewtonianPush(new Vector2(1, 0), 100f);

			Assert.Equal(ObjectPhysics.MAX_SPEED, obj.newtonianMovement.Length(), 3);
		}

		[Fact]
		public void NewtonianPush_AccumulatesImpulses_LikeMomentumNotTeleport()
		{
			var (obj, _, _, _) = Make();

			obj.NewtonianPush(new Vector2(1, 0), 5f);
			obj.NewtonianPush(new Vector2(0, 1), 5f);

			// Two orthogonal 5-unit shoves sum vectorially, not replace.
			Assert.Equal(5f, obj.newtonianMovement.X, 3);
			Assert.Equal(5f, obj.newtonianMovement.Y, 3);
		}

		[Fact]
		public void NewtonianPush_NaNTimers_DoNotDisturbExistingTimers()
		{
			var (obj, _, _, _) = Make();
			obj.NewtonianPush(new Vector2(1, 0), 5f, nairTime: 1.5f);

			// A second shove without timer args must not reset air time.
			obj.NewtonianPush(new Vector2(1, 0), 2f);

			Assert.Equal(1.5f, obj.airTime, 3);
		}

		// =====================================================================
		// Invariants: friction model
		// =====================================================================

		[Fact]
		public void Friction_IsLinearDeceleration_AtDefaultRate()
		{
			var (obj, _, _, _) = Make(gravity: true);
			obj.NewtonianPush(new Vector2(1, 0), 10f);

			obj.FlyingUpdate(Dt);

			// Flat decel: speed drops by exactly DEFAULT_Friction * dt per tick.
			Assert.Equal(10f - ObjectPhysics.DEFAULT_Friction * Dt, obj.newtonianMovement.Length(), 3);
		}

		[Fact]
		public void Friction_BringsObjectToCompleteStop()
		{
			var (obj, _, _, events) = Make(gravity: true);
			obj.NewtonianPush(new Vector2(1, 0), 10f);

			for (int i = 0; i < 120; i++)
			{
				obj.PhysicsTick(Dt);
			}

			Assert.Equal(Vector2.Zero, obj.newtonianMovement);
			Assert.Equal(1, events.ThrowEnds);
			// Landed exactly on a tile center, never between tiles.
			Assert.Equal(obj.LocalTile.ToCenter(), obj.LocalPosition);
		}

		[Fact]
		public void AirTime_SuspendsFriction_UntilLanding()
		{
			var (obj, _, _, _) = Make(gravity: true);
			obj.NewtonianPush(new Vector2(1, 0), 10f, nairTime: 1f);

			obj.FlyingUpdate(Dt);

			// Airborne: full speed retained despite gravity underneath.
			Assert.Equal(10f, obj.newtonianMovement.Length(), 3);
		}

		[Fact]
		public void SlideTime_UsesSlideFriction_WhichIsGentlerThanDefault()
		{
			var (sliding, _, _, _) = Make(gravity: true);
			sliding.NewtonianPush(new Vector2(1, 0), 10f, inSlideTime: 5f);
			sliding.FlyingUpdate(Dt);

			var (standing, _, _, _) = Make(gravity: true);
			standing.NewtonianPush(new Vector2(1, 0), 10f);
			standing.FlyingUpdate(Dt);

			Assert.Equal(10f - ObjectPhysics.DEFAULT_SLIDE_FRICTION * Dt, sliding.newtonianMovement.Length(), 3);
			Assert.True(sliding.newtonianMovement.Length() > standing.newtonianMovement.Length(),
				"sliding must decay slower than standing friction");
		}

		[Fact]
		public void SpaceDrift_NoGravity_VelocityNeverDecays()
		{
			var (obj, _, _, _) = Make(gravity: false);
			obj.NewtonianPush(new Vector2(1, 0), 5f);

			for (int i = 0; i < 600; i++) // ten seconds adrift
			{
				obj.FlyingUpdate(Dt);
			}

			// Newton's first law is the space-movement mechanic. Any decay here
			// breaks EVA gameplay.
			Assert.Equal(5f, obj.newtonianMovement.Length(), 3);
			Assert.Equal(5f * 600 * Dt, obj.LocalPosition.X, 2);
		}

		// =====================================================================
		// Invariants: collision and bounce
		// =====================================================================

		[Fact]
		public void Bounce_ReflectsBlockedAxis_ScaledByBouncyness()
		{
			var (obj, grid, _, events) = Make(gravity: false); // space isolates friction
			grid.Walls.Add(new Vector3Int(1, 0, 0));
			obj.NewtonianPush(new Vector2(1, 0), 10f);

			while (events.Bounces == 0)
			{
				obj.FlyingUpdate(Dt);
			}

			// Head-on X impact: X reverses at 0.75 restitution, Y untouched.
			Assert.Equal(-10f * 0.75f, obj.newtonianMovement.X, 2);
			Assert.Equal(0f, obj.newtonianMovement.Y, 3);
		}

		[Fact]
		public void Bounce_DiagonalIntoFlatWall_KeepsTangentialVelocity()
		{
			var (obj, grid, _, events) = Make(gravity: false);
			// Wall spans x=1 at every y: only the X axis is blocked.
			for (int y = -5; y <= 5; y++)
			{
				grid.Walls.Add(new Vector3Int(1, y, 0));
			}

			obj.NewtonianPush(Vector2.Normalize(new Vector2(1, 1)), 10f);

			while (events.Bounces == 0)
			{
				obj.FlyingUpdate(Dt);
			}

			// Skims along the wall: Y component untouched, X reflected.
			Assert.True(obj.newtonianMovement.X < 0, "X must reflect");
			Assert.Equal(10f / MathF.Sqrt(2f), obj.newtonianMovement.Y, 2);
		}

		[Fact]
		public void HighSpeedCollision_FiresOnlyAboveThreshold()
		{
			// Above threshold → event (damage).
			var (fast, fastGrid, _, fastEvents) = Make(gravity: false);
			fastGrid.Walls.Add(new Vector3Int(1, 0, 0));
			fast.NewtonianPush(new Vector2(1, 0), 15f);
			while (fastEvents.Bounces == 0)
			{
				fast.FlyingUpdate(Dt);
			}

			Assert.Single(fastEvents.HighSpeedCollisions);
			Assert.Equal(15f, fastEvents.HighSpeedCollisions[0].speed, 2);

			// At/below threshold → bounce but no damage event.
			var (slow, slowGrid, _, slowEvents) = Make(gravity: false);
			slowGrid.Walls.Add(new Vector3Int(1, 0, 0));
			slow.NewtonianPush(new Vector2(1, 0), ObjectPhysics.HIGH_SPEED_COLLISION_THRESHOLD);
			while (slowEvents.Bounces == 0)
			{
				slow.FlyingUpdate(Dt);
			}

			Assert.Empty(slowEvents.HighSpeedCollisions);
		}

		[Fact]
		public void Intangible_PassesThroughWalls()
		{
			var (obj, grid, _, events) = Make(gravity: false);
			grid.Walls.Add(new Vector3Int(1, 0, 0));
			obj.Intangible = true;
			obj.NewtonianPush(new Vector2(1, 0), 10f);

			for (int i = 0; i < 60; i++)
			{
				obj.FlyingUpdate(Dt);
			}

			Assert.Equal(0, events.Bounces);
			Assert.True(obj.LocalPosition.X > 1f, "must have passed through the wall tile");
		}

		// =====================================================================
		// Invariants: tile stepping, pushing, pulling
		// =====================================================================

		[Fact]
		public void TryTilePush_IntoWall_FailsAndMovesNothing()
		{
			var (obj, grid, _, _) = Make();
			grid.Walls.Add(new Vector3Int(1, 0, 0));

			Assert.False(obj.TryTilePush(Vector3Int.Right));
			Assert.False(obj.Animating);
		}

		[Fact]
		public void TryTilePush_ChainsThroughOccupants_AllOrNothing()
		{
			var grid = new FakeGrid();
			var collision = new FakeCollision();
			var pusher = new ObjectPhysics(grid, collision);
			var crate = new ObjectPhysics(grid, collision) { LocalPosition = new Vector3(1, 0, 0) };
			collision.Register(pusher, new Vector3Int(0, 0, 0));
			collision.Register(crate, new Vector3Int(1, 0, 0));

			// Open path: pushing into the crate shoves the crate one tile too.
			Assert.True(pusher.TryTilePush(Vector3Int.Right));
			Assert.Equal(new Vector3(2, 0, 0), crate.LocalTargetPosition);

			// Now wall off the crate's next tile: the whole chain must refuse.
			var grid2 = new FakeGrid();
			grid2.Walls.Add(new Vector3Int(2, 0, 0));
			var collision2 = new FakeCollision();
			var pusher2 = new ObjectPhysics(grid2, collision2);
			var crate2 = new ObjectPhysics(grid2, collision2) { LocalPosition = new Vector3(1, 0, 0) };
			collision2.Register(pusher2, new Vector3Int(0, 0, 0));
			collision2.Register(crate2, new Vector3Int(1, 0, 0));

			Assert.False(pusher2.TryTilePush(Vector3Int.Right));
			Assert.False(pusher2.Animating);
			Assert.False(crate2.Animating);
		}

		[Fact]
		public void Pulling_PulledObjectStepsIntoVacatedTile()
		{
			var grid = new FakeGrid();
			var collision = new FakeCollision();
			var puller = new ObjectPhysics(grid, collision);
			var pulled = new ObjectPhysics(grid, collision) { LocalPosition = new Vector3(-1, 0, 0) };
			collision.Register(puller, new Vector3Int(0, 0, 0));
			collision.Register(pulled, new Vector3Int(-1, 0, 0));
			puller.Pulling = pulled;

			Assert.True(puller.TryTilePush(Vector3Int.Right));

			Assert.Equal(new Vector3(1, 0, 0), puller.LocalTargetPosition);
			Assert.Equal(new Vector3(0, 0, 0), pulled.LocalTargetPosition); // the tile the puller left
		}

		[Fact]
		public void AnimationUpdate_ArrivesExactlyOnTileCenter_NeverOvershoots()
		{
			var (obj, _, _, _) = Make();
			Assert.True(obj.TryTilePush(Vector3Int.Right));

			// 0.5s per tile at 60Hz = 30 ticks.
			for (int i = 0; i < 31; i++)
			{
				obj.AnimationUpdate(Dt);
			}

			Assert.False(obj.Animating);
			Assert.Equal(new Vector3(1, 0, 0), obj.LocalPosition); // exact, not approximately
		}

		// =====================================================================
		// Determinism
		// =====================================================================

		[Fact]
		public void Simulation_IsBitwiseDeterministic_AcrossRuns()
		{
			var traceA = RunThrowScenario();
			var traceB = RunThrowScenario();

			Assert.Equal(traceA.Count, traceB.Count);
			for (int i = 0; i < traceA.Count; i++)
			{
				// Bitwise, not approximate: prediction/replay requires it.
				Assert.Equal(traceA[i].Position, traceB[i].Position);
				Assert.Equal(traceA[i].Velocity, traceB[i].Velocity);
			}
		}

		private static List<TraceFrame> RunThrowScenario()
		{
			var (obj, grid, _, _) = Make(gravity: true);
			grid.Walls.Add(new Vector3Int(5, 0, 0));
			obj.NewtonianPush(new Vector2(1, 0.2f), 14f, nairTime: 0.5f, spinFactor: 8f);

			var trace = new List<TraceFrame>();
			for (int i = 0; i < 240; i++)
			{
				obj.PhysicsTick(Dt);
				trace.Add(new TraceFrame(obj.LocalPosition, obj.newtonianMovement));
			}

			return trace;
		}

		// =====================================================================
		// Golden Trace parity — the actual fidelity contract
		// =====================================================================

		/// <summary>
		/// One frame of a recorded trajectory. The Unity capture harness (a
		/// debug MonoBehaviour added to Unitystation that logs
		/// transform.localPosition and newtonianMovement each FixedUpdate)
		/// writes these as JSON lines; parity tests replay identical inputs
		/// here and compare frame-by-frame.
		/// </summary>
		public readonly record struct TraceFrame(Vector3 Position, Vector2 Velocity);

		private const string GoldenTraceSkip =
			"Golden trace not yet captured. Requires the instrumented Unitystation build " +
			"(tools/trace-capture branch) to record the reference trajectory JSON into " +
			"tests/Godotstation.Tests/GoldenTraces/. Tolerance: |Δpos| < 1e-4 per frame, " +
			"and the FINAL resting tile must match exactly — a throw that lands one tile " +
			"off is a gameplay difference, not a rounding error.";

		[Fact(Skip = GoldenTraceSkip)]
		public void GoldenTrace_StandardThrow_MatchesUnityFrameByFrame()
		{
			// Scenario: item thrown 5 tiles east at speed 14, airTime 1.0,
			// landing on plating with gravity. Compare all 240 frames.
		}

		[Fact(Skip = GoldenTraceSkip)]
		public void GoldenTrace_WallBounce_MatchesUnityImpactFrameAndRestitution()
		{
			// Scenario: throw at 20 into a wall 3 tiles away. The impact
			// frame index, reflected velocity, and final resting tile must all
			// match the capture.
		}

		[Fact(Skip = GoldenTraceSkip)]
		public void GoldenTrace_SpaceDrift_MatchesUnityPositionAfterSixtySeconds()
		{
			// Scenario: object shoved at 2.5 in zero gravity, 60 simulated
			// seconds. Pins down integration order and float behaviour over a
			// long horizon (error accumulates linearly if integration differs).
		}
	}
}
