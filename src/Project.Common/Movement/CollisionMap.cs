using System.Collections.Generic;

namespace Project.Common.Movement
{
    public interface IPassable
    {
        bool CanPass(PlayerMovementState state);
    }

    /// <summary>
    /// A dynamic obstacle occupying a single tile: another player, a crate, a cart, furniture.
    /// Mirrors the pushable half of Unitystation's <c>UniversalObjectPhysics</c> /
    /// <c>UniversalObjectPhysics.PushPull</c>. A non-pushable object (bolted-down machinery,
    /// secured lockers) behaves like a wall; a pushable one can be shoved one tile at a time
    /// and can chain-push other pushables ahead of it.
    /// </summary>
    public class DynamicObject
    {
        public int X;
        public int Y;

        /// <summary>Maps to <c>isNotPushable == false</c> upstream.</summary>
        public bool IsPushable { get; set; } = true;

        /// <summary>Optional tag for tests / gameplay (e.g. "crate", "player").</summary>
        public string? Tag { get; set; }

        public DynamicObject(int x, int y, bool isPushable = true, string? tag = null)
        {
            X = x;
            Y = y;
            IsPushable = isPushable;
            Tag = tag;
        }
    }

    public class CollisionMap
    {
        private readonly Dictionary<(int x, int y), IPassable> _grid = new Dictionary<(int, int), IPassable>();

        // Frictionless / slippery tiles (ice, space). A body on one of these keeps its momentum
        // (see MovementProcessor friction selection) and slides.
        private readonly HashSet<(int x, int y)> _slippery = new HashSet<(int, int)>();

        // Dynamic obstacles keyed by their current tile. Only one object per tile.
        private readonly Dictionary<(int x, int y), DynamicObject> _objects = new Dictionary<(int, int), DynamicObject>();

        // ---- Static terrain (walls, windoors) ----

        public void SetTile(int x, int y, IPassable tile)
        {
            _grid[(x, y)] = tile;
        }

        /// <summary>
        /// Passability of the static terrain only (walls/windoors). Dynamic objects are handled
        /// separately by the MovementProcessor so it can decide whether to push them. Empty space
        /// is passable.
        /// </summary>
        public bool IsPassable(int x, int y, PlayerMovementState state)
        {
            if (_grid.TryGetValue((x, y), out var tile))
            {
                return tile.CanPass(state);
            }
            return true; // Empty space is passable
        }

        // ---- Slippery (ice) tiles ----

        public void SetSlippery(int x, int y, bool slippery = true)
        {
            if (slippery) _slippery.Add((x, y));
            else _slippery.Remove((x, y));
        }

        public bool IsSlippery(int x, int y) => _slippery.Contains((x, y));

        // ---- Dynamic obstacles ----

        public void AddObject(DynamicObject obj)
        {
            _objects[(obj.X, obj.Y)] = obj;
        }

        public DynamicObject? GetObject(int x, int y)
        {
            return _objects.TryGetValue((x, y), out var obj) ? obj : null;
        }

        /// <summary>
        /// Relocate an object already registered on the map from its current tile to a new one.
        /// The destination must be empty of other objects; callers (the push logic) guarantee this.
        /// </summary>
        public void MoveObject(DynamicObject obj, int newX, int newY)
        {
            _objects.Remove((obj.X, obj.Y));
            obj.X = newX;
            obj.Y = newY;
            _objects[(newX, newY)] = obj;
        }

        /// <summary>
        /// True if the tile hard-blocks entry regardless of pushing: a wall, or a non-pushable
        /// object. Pushable objects are NOT considered blocked here — the processor resolves them.
        /// </summary>
        public bool IsHardBlocked(int x, int y, PlayerMovementState state)
        {
            if (!IsPassable(x, y, state)) return true;
            var obj = GetObject(x, y);
            return obj != null && !obj.IsPushable;
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
