using Godotstation.Engine.Movement;
using Xunit;

namespace Godotstation.Tests.Movement
{
    public sealed class StationPlaytestTests
    {
        [Fact]
        public void MovementCommand_RequiresMatchingPeerAndUnitDirection()
        {
            Assert.True(MovementCommandAuthorization.IsValid(2, 2, 1, -1));
            Assert.True(MovementCommandAuthorization.IsValid(2, 2, 0, 0));
            Assert.False(MovementCommandAuthorization.IsValid(2, 3, 1, 0));
            Assert.False(MovementCommandAuthorization.IsValid(2, 2, 2, 0));
            Assert.False(MovementCommandAuthorization.IsValid(2, 2, 0, -2));
            Assert.False(MovementCommandAuthorization.IsValid(0, 0, 0, 1));
        }

        [Fact]
        public void StationLayout_HasWalkableHallAndRoomDoorsInsideSolidBounds()
        {
            var map = StationPlaytestLayout.CreateCollisionMap();
            var state = new PlayerMovementState();

            Assert.False(map.IsPassable(StationPlaytestLayout.MinX, 0, state));
            Assert.False(map.IsPassable(StationPlaytestLayout.MaxX, 0, state));
            Assert.False(map.IsPassable(0, -4, state));
            Assert.False(map.IsPassable(-4, -1, state));
            Assert.False(map.IsPassable(-4, -7, state));
            Assert.True(map.IsPassable(-9, 0, state));
            Assert.True(map.IsPassable(-4, 0, state));
            Assert.True(map.IsPassable(-4, -6, state));
            Assert.All(StationPlaytestLayout.Spawns, spawn =>
                Assert.True(map.IsPassable((int)spawn.X, (int)spawn.Y, state)));
            Assert.Contains((-7, -4), StationPlaytestLayout.GetWallTiles());
            Assert.DoesNotContain((-8, 0), StationPlaytestLayout.GetWallTiles());
        }

        [Fact]
        public void LateJoiner_AfterDisconnect_SpawnsOnAFreeTile()
        {
            var spawns = StationPlaytestLayout.Spawns;
            // Host, A and B joined at spawns 0, 1 and 2; A then disconnected.
            var stillConnected = new[] { spawns[0], spawns[2] };

            Assert.True(StationPlaytestLayout.TryFindFreeSpawn(stillConnected, out var spawn));
            Assert.Equal(spawns[1], spawn);
            Assert.DoesNotContain(spawn, stillConnected);
        }

        [Fact]
        public void FreeSpawn_IgnoresSpawnsVacatedByMovementAndRefusesWhenAllAreOccupied()
        {
            var spawns = StationPlaytestLayout.Spawns;
            var movedOff = new[] { new System.Numerics.Vector2(-3.4f, 0.2f), spawns[1], spawns[2], spawns[3] };
            Assert.True(StationPlaytestLayout.TryFindFreeSpawn(movedOff, out var spawn));
            Assert.Equal(spawns[0], spawn);

            var nearlyOnSpawns = new[] { spawns[0] + new System.Numerics.Vector2(0.3f, -0.2f), spawns[1], spawns[2], spawns[3] };
            Assert.False(StationPlaytestLayout.TryFindFreeSpawn(nearlyOnSpawns, out _));
        }

        [Fact]
        public void Disconnect_RemovesNonPushablePlayerObstacle()
        {
            var map = new CollisionMap();
            var disconnectedPlayer = new DynamicObject(3, 4, isPushable: false, tag: "player");
            map.AddObject(disconnectedPlayer);

            Assert.Same(disconnectedPlayer, map.GetObject(3, 4));
            Assert.True(map.RemoveObject(disconnectedPlayer));
            Assert.Null(map.GetObject(3, 4));
            Assert.False(map.RemoveObject(disconnectedPlayer));
        }
    }
}
