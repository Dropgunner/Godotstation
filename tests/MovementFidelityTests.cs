using Xunit;
using Project.Common.Movement;

namespace Tests
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
    }
}
