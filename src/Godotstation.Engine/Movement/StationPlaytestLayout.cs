using System.Collections.Generic;
using System.Numerics;

namespace Godotstation.Engine.Movement
{
    /// <summary>Shared, deterministic wall and spawn layout for the LAN movement playtest.</summary>
    public static class StationPlaytestLayout
    {
        public const int MinX = -12;
        public const int MaxX = 12;
        public const int MinY = -9;
        public const int MaxY = 9;

        private static readonly Vector2[] SpawnPoints =
        [
            new Vector2(-9f, 0f),
            new Vector2(9f, 0f),
            new Vector2(-1f, 0f),
            new Vector2(1f, 0f)
        ];

        private static readonly IReadOnlyList<Vector2> ReadOnlySpawnPoints =
            System.Array.AsReadOnly(SpawnPoints);

        public static IReadOnlyList<Vector2> Spawns => ReadOnlySpawnPoints;

        public static IReadOnlyList<(int X, int Y)> GetWallTiles()
        {
            var walls = new HashSet<(int X, int Y)>();
            for (int x = MinX; x <= MaxX; x++)
            {
                walls.Add((x, MinY));
                walls.Add((x, MaxY));
            }
            for (int y = MinY; y <= MaxY; y++)
            {
                walls.Add((MinX, y));
                walls.Add((MaxX, y));
            }

            AddHorizontalPartition(walls, -4);
            AddHorizontalPartition(walls, 4);
            AddDepartmentPartitions(walls, -4);
            AddDepartmentPartitions(walls, 4);
            AddVerticalPartition(walls, -4);
            AddVerticalPartition(walls, 4);

            var ordered = new List<(int X, int Y)>(walls);
            ordered.Sort(static (a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            return ordered.AsReadOnly();
        }

        public static CollisionMap CreateCollisionMap()
        {
            var map = new CollisionMap();
            foreach (var (x, y) in GetWallTiles())
                map.SetTile(x, y, new SolidWall());
            return map;
        }

        private static void AddHorizontalPartition(HashSet<(int X, int Y)> walls, int y)
        {
            for (int x = MinX + 1; x < MaxX; x++)
                if (x is not -8 and not -1 and not 8)
                    walls.Add((x, y));
        }

        private static void AddVerticalPartition(HashSet<(int X, int Y)> walls, int x)
        {
            for (int y = -3; y <= 3; y++)
                if (y != 0)
                    walls.Add((x, y));
        }

        private static void AddDepartmentPartitions(HashSet<(int X, int Y)> walls, int x)
        {
            for (int y = MinY + 1; y <= -5; y++)
                if (y != -6)
                    walls.Add((x, y));
        }
    }

    /// <summary>Pure validation for untrusted client movement commands.</summary>
    public static class MovementCommandAuthorization
    {
        public static bool IsValid(long ownerPeerId, long senderPeerId, int deltaX, int deltaY)
        {
            return ownerPeerId > 0 && senderPeerId == ownerPeerId &&
                deltaX is >= -1 and <= 1 && deltaY is >= -1 and <= 1;
        }
    }
}
