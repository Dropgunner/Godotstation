using System.Collections.Generic;

namespace Project.Common.Movement
{
    public interface IPassable
    {
        bool CanPass(PlayerMovementState state);
    }

    public class CollisionMap
    {
        private readonly Dictionary<(int x, int y), IPassable> _grid = new Dictionary<(int, int), IPassable>();

        public void SetTile(int x, int y, IPassable tile)
        {
            _grid[(x, y)] = tile;
        }

        public bool IsPassable(int x, int y, PlayerMovementState state)
        {
            if (_grid.TryGetValue((x, y), out var tile))
            {
                return tile.CanPass(state);
            }
            return true; // Empty space is passable
        }
    }

    public class SolidWall : IPassable
    {
        public bool CanPass(PlayerMovementState state) => false;
    }

    public class DirectionalPassable : IPassable
    {
        public bool CanPass(PlayerMovementState state) 
        {
            // Simplified for benchmark: imagine windoors
            return true; 
        }
    }
}
