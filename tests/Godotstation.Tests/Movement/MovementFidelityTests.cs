using Xunit;
using Godotstation.Engine.Movement;

namespace Godotstation.Tests.Movement
{
    public class MovementFidelityTests
    {
        [Fact]
        public void PlayerMovement_BaseSpeeds_MatchUnitystation()
        {
            var state = new PlayerMovementState();

            state.CurrentType = MovementType.Running;
            Assert.Equal(6.0f, state.CurrentSpeed); // 3.0 * 2 legs

            state.CurrentType = MovementType.Walking;
            Assert.Equal(3.0f, state.CurrentSpeed); // 1.5 * 2 legs

            state.CurrentType = MovementType.Crawling;
            Assert.Equal(1.0f, state.CurrentSpeed); // 0.5 * 2 legs
        }

        [Fact]
        public void PlayerMovement_CuffedOrBuckled_CannotMove()
        {
            var state = new PlayerMovementState();

            state.IsCuffed = true;
            Assert.Equal(0f, state.CurrentSpeed); // Wait, original code says IsCuffed blocks TryMove, not speed directly, but we modeled it here.
            // Actually, in our implementation, TryMove checks IsCuffed.

            var map = new CollisionMap();
            var processor = new MovementProcessor(map);
            float x = 0, y = 0;
            var intent = new MoveIntent { DeltaX = 1, DeltaY = 0 };

            bool moved = processor.TryMove(state, ref x, ref y, intent, 1.0f);
            Assert.False(moved);
            Assert.Equal(0f, x);
        }

        [Fact]
        public void MovementProcessor_WallCollision_BlocksMovement()
        {
            var map = new CollisionMap();
            map.SetTile(1, 0, new SolidWall());

            var processor = new MovementProcessor(map);
            var state = new PlayerMovementState { CurrentType = MovementType.Walking }; // speed 3.0

            float x = 0, y = 0;
            var intent = new MoveIntent { DeltaX = 1, DeltaY = 0 };

            // Move 0.5 seconds (distance 1.5)
            bool moved = processor.TryMove(state, ref x, ref y, intent, 0.5f);

            // Should be blocked because tile (1,0) is a wall
            Assert.False(moved);
            Assert.Equal(0f, x);
        }

        [Fact]
        public void MovementProcessor_DiagonalSlide_SlidesAlongWall()
        {
            var map = new CollisionMap();
            map.SetTile(1, 1, new SolidWall()); // Wall diagonally up-right
            map.SetTile(1, 0, new SolidWall()); // Wall to the right
            // Up is open

            var processor = new MovementProcessor(map);
            var state = new PlayerMovementState { CurrentType = MovementType.Walking };

            float x = 0, y = 0;
            var intent = new MoveIntent { DeltaX = 1, DeltaY = 1 }; // Try to move up-right

            bool moved = processor.TryMove(state, ref x, ref y, intent, 0.5f);

            // Should fail to move right, but succeed moving up
            Assert.True(moved);
            Assert.Equal(0f, x); // Blocked right
            Assert.True(y > 0f); // Slid up
        }

        [Fact]
        public void MovementProcessor_FastMove_DoesNotTunnelThroughWall()
        {
            // Rule 4: collision resolves against the ADJACENT tile, so a large moveDist cannot
            // skip over a wall on the very next tile.
            var map = new CollisionMap();
            map.SetTile(1, 0, new SolidWall());

            var processor = new MovementProcessor(map);
            var state = new PlayerMovementState { CurrentType = MovementType.Running }; // speed 6.0

            float x = 0, y = 0;
            var intent = new MoveIntent { DeltaX = 1, DeltaY = 0 };

            // moveDist = 6.0 * 1.0 = 6 tiles — would round to tile 6 and skip the wall at tile 1.
            bool moved = processor.TryMove(state, ref x, ref y, intent, 1.0f);

            Assert.False(moved);
            Assert.Equal(0f, x);
        }

        // ---------------- Rule 5: Momentum & Ice Sliding ----------------

        private static float SlideToRest(MovementProcessor processor, PlayerMovementState state, ref float x, ref float y)
        {
            float start = x;
            int guard = 0;
            while (processor.ApplyMomentum(state, ref x, ref y, 0.02f))
            {
                if (++guard > 10000) break; // safety net
            }
            return System.Math.Abs(x - start);
        }

        [Fact]
        public void Momentum_SlidesFartherOnIceThanOnFloor()
        {
            // Same initial momentum, different ground. Ice (SLIDE_FRICTION=9) must carry the body
            // measurably farther than a grippy floor (DEFAULT_FRICTION=15).
            var floorMap = new CollisionMap();
            var floorProc = new MovementProcessor(floorMap);
            var floorState = new PlayerMovementState();
            floorState.SetMomentum(6f, 0f);
            float fx = 0, fy = 0;
            float floorDist = SlideToRest(floorProc, floorState, ref fx, ref fy);

            var iceMap = new CollisionMap();
            for (int i = 0; i <= 10; i++) iceMap.SetSlippery(i, 0, true);
            var iceProc = new MovementProcessor(iceMap);
            var iceState = new PlayerMovementState();
            iceState.SetMomentum(6f, 0f);
            float ix = 0, iy = 0;
            float iceDist = SlideToRest(iceProc, iceState, ref ix, ref iy);

            Assert.True(iceDist > floorDist, $"ice {iceDist} should exceed floor {floorDist}");
            Assert.True(floorState.MomentumSquared == 0f); // both come to rest
            Assert.True(iceState.MomentumSquared == 0f);
            Assert.True(iceDist > 1.5f);   // slides several tiles on ice
            Assert.True(floorDist < 1.5f); // dies within ~a tile on floor
        }

        [Fact]
        public void Momentum_MovingOnIce_RetainsVelocityAfterInput()
        {
            // Rule 5: self-propelled motion over slippery ground is retained as momentum.
            var map = new CollisionMap();
            for (int i = 0; i <= 5; i++) map.SetSlippery(i, 0, true);
            var processor = new MovementProcessor(map);
            var state = new PlayerMovementState { CurrentType = MovementType.Running }; // speed 6.0

            float x = 0, y = 0;
            var intent = new MoveIntent { DeltaX = 1, DeltaY = 0 };
            processor.TryMove(state, ref x, ref y, intent, 0.1f);

            // Input released, but the body is now gliding.
            Assert.Equal(6f, state.MomentumX);
            Assert.True(state.IsSliding);
        }

        [Fact]
        public void Momentum_SlidingBody_StopsAtWallWithoutTunnelling()
        {
            var map = new CollisionMap();
            map.SetTile(2, 0, new SolidWall());
            var processor = new MovementProcessor(map);
            var state = new PlayerMovementState();
            state.SetMomentum(6f, 0f);

            float x = 0, y = 0;
            // Huge dt: raw displacement is 6 tiles, but the wall is at tile 2.
            processor.ApplyMomentum(state, ref x, ref y, 1.0f);

            Assert.True(x < 2f);               // never entered/passed the wall tile
            Assert.Equal(1f, x, 3);            // halted centred on the last free tile
            Assert.Equal(0f, state.MomentumX); // momentum killed on the blocked axis
        }

        // ---------------- Rule 6: Object Pushing ----------------

        [Fact]
        public void Pushing_MovingIntoCrate_ShovesItOneTile()
        {
            var map = new CollisionMap();
            var crate = new DynamicObject(1, 0, isPushable: true, tag: "crate");
            map.AddObject(crate);

            var processor = new MovementProcessor(map);
            var state = new PlayerMovementState { CurrentType = MovementType.Walking };

            float x = 0, y = 0;
            var intent = new MoveIntent { DeltaX = 1, DeltaY = 0 };
            bool moved = processor.TryMove(state, ref x, ref y, intent, 0.5f);

            Assert.True(moved);
            Assert.True(x > 0f);
            Assert.Equal(2, crate.X); // pushed one tile in the movement direction
            Assert.Equal(0, crate.Y);
            Assert.Same(crate, map.GetObject(2, 0));
            Assert.Null(map.GetObject(1, 0));
        }

        [Fact]
        public void Pushing_CrateAgainstWall_IsBlocked()
        {
            var map = new CollisionMap();
            map.AddObject(new DynamicObject(1, 0, isPushable: true));
            map.SetTile(2, 0, new SolidWall()); // no room for the crate to move into

            var processor = new MovementProcessor(map);
            var state = new PlayerMovementState { CurrentType = MovementType.Walking };

            float x = 0, y = 0;
            var intent = new MoveIntent { DeltaX = 1, DeltaY = 0 };
            bool moved = processor.TryMove(state, ref x, ref y, intent, 0.5f);

            Assert.False(moved);
            Assert.Equal(0f, x);
            Assert.NotNull(map.GetObject(1, 0)); // crate did not move
        }

        [Fact]
        public void Pushing_ChainOfCrates_AllShoveTogether()
        {
            var map = new CollisionMap();
            var a = new DynamicObject(1, 0, isPushable: true, tag: "A");
            var b = new DynamicObject(2, 0, isPushable: true, tag: "B");
            map.AddObject(a);
            map.AddObject(b);
            // tile (3,0) is empty, so the chain has exactly one tile of room.

            var processor = new MovementProcessor(map);
            var state = new PlayerMovementState { CurrentType = MovementType.Walking };

            float x = 0, y = 0;
            var intent = new MoveIntent { DeltaX = 1, DeltaY = 0 };
            bool moved = processor.TryMove(state, ref x, ref y, intent, 0.5f);

            Assert.True(moved);
            Assert.Equal(2, a.X); // A shoved into B's old tile
            Assert.Equal(3, b.X); // B shoved one further
        }

        [Fact]
        public void Pushing_ChainBlockedByWall_NothingMoves()
        {
            var map = new CollisionMap();
            var a = new DynamicObject(1, 0, isPushable: true, tag: "A");
            var b = new DynamicObject(2, 0, isPushable: true, tag: "B");
            map.AddObject(a);
            map.AddObject(b);
            map.SetTile(3, 0, new SolidWall()); // chain has nowhere to go

            var processor = new MovementProcessor(map);
            var state = new PlayerMovementState { CurrentType = MovementType.Walking };

            float x = 0, y = 0;
            var intent = new MoveIntent { DeltaX = 1, DeltaY = 0 };
            bool moved = processor.TryMove(state, ref x, ref y, intent, 0.5f);

            Assert.False(moved);
            Assert.Equal(0f, x);
            Assert.Equal(1, a.X); // atomic: neither crate moved
            Assert.Equal(2, b.X);
        }

        [Fact]
        public void Pushing_NonPushableObject_ActsLikeWall()
        {
            var map = new CollisionMap();
            var bolted = new DynamicObject(1, 0, isPushable: false, tag: "bolted-locker");
            map.AddObject(bolted);

            var processor = new MovementProcessor(map);
            var state = new PlayerMovementState { CurrentType = MovementType.Walking };

            float x = 0, y = 0;
            var intent = new MoveIntent { DeltaX = 1, DeltaY = 0 };
            bool moved = processor.TryMove(state, ref x, ref y, intent, 0.5f);

            Assert.False(moved);
            Assert.Equal(0f, x);
            Assert.Equal(1, bolted.X); // immovable
        }

        [Fact]
        public void Pushing_SlidingBodyShovesCrate_ThenStopsAtWall()
        {
            // A body sliding under momentum shoves a crate, then the chain jams against a wall.
            var map = new CollisionMap();
            var crate = new DynamicObject(1, 0, isPushable: true, tag: "crate");
            map.AddObject(crate);
            map.SetTile(3, 0, new SolidWall());

            var processor = new MovementProcessor(map);
            var state = new PlayerMovementState();
            state.SetMomentum(6f, 0f);

            float x = 0, y = 0;
            processor.ApplyMomentum(state, ref x, ref y, 1.0f);

            Assert.Equal(2, crate.X);          // crate shoved one tile
            Assert.True(x >= 1f && x < 2f);     // body advanced into crate's old tile, then jammed
            Assert.Equal(0f, state.MomentumX);  // momentum killed by the wall
        }

        [Fact]
        public void Push_DirectApi_PushesAndChains()
        {
            var map = new CollisionMap();
            var crate = new DynamicObject(5, 5, isPushable: true);
            map.AddObject(crate);
            var processor = new MovementProcessor(map);
            var state = new PlayerMovementState();

            bool pushed = processor.Push(5, 5, 0, 1, state); // push north
            Assert.True(pushed);
            Assert.Equal(6, crate.Y);
            Assert.Same(crate, map.GetObject(5, 6));

            // Pushing an immovable object fails.
            var bolted = new DynamicObject(0, 0, isPushable: false);
            map.AddObject(bolted);
            Assert.False(processor.Push(0, 0, 1, 0, state));
            Assert.Equal(0, bolted.X);
        }
    }
}
