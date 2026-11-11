using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Interactions;
using Godotstation.Engine.Movement;

namespace Godotstation.Engine.Simulation;

/// <summary>One player's body in the station's movement simulation.</summary>
public sealed class SimulatedPlayer
{
    public SimulatedPlayer(int peerId, Vector2 position, DynamicObject obstacle)
    {
        PeerId = peerId;
        Position = position;
        Obstacle = obstacle;
    }

    public int PeerId { get; }
    public Vector2 Position { get; set; }
    public DynamicObject Obstacle { get; }
    /// <summary>False while the body lies on the floor and others may walk over it.</summary>
    public bool ObstacleActive { get; set; } = true;
    public PlayerMovementState Movement { get; } = new();
    /// <summary>The latest movement input from the owning client, each axis in -1..1.</summary>
    public int DeltaX { get; set; }
    public int DeltaY { get; set; }
}

/// <summary>
/// The authoritative movement and collision step for one station, with no engine types. The host
/// runs <see cref="Tick"/> every physics frame. Clients construct the same object so they can keep
/// collision in step with replicated entities, but never tick it.
/// </summary>
public sealed class StationSimulation
{
    /// <summary>Longest step taken in one tick. Longer frames are clamped so movement stays stable.</summary>
    public const double MaxStepSeconds = 0.1;

    private readonly SharedStationInventoryWorld world;
    private readonly MovementProcessor movement;
    private readonly Dictionary<int, SimulatedPlayer> players = new();
    private readonly Dictionary<int, DynamicObject> entityObstacles = new();

    public StationSimulation(CollisionMap map, SharedStationInventoryWorld world)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        movement = new MovementProcessor(map);
    }

    public CollisionMap Map { get; }

    public IReadOnlyDictionary<int, SimulatedPlayer> Players => players;

    public SimulatedPlayer? GetPlayer(int peerId) => players.TryGetValue(peerId, out SimulatedPlayer? player) ? player : null;

    /// <summary>Adds a body at <paramref name="position"/>. It blocks other bodies until it lies down.</summary>
    public SimulatedPlayer AddPlayer(int peerId, Vector2 position)
    {
        if (players.TryGetValue(peerId, out SimulatedPlayer? existing)) return existing;
        var obstacle = new DynamicObject(Round(position.X), Round(position.Y), isPushable: false, tag: "player");
        Map.AddObject(obstacle);
        var player = new SimulatedPlayer(peerId, position, obstacle);
        players.Add(peerId, player);
        return player;
    }

    public bool RemovePlayer(int peerId)
    {
        if (!players.Remove(peerId, out SimulatedPlayer? player)) return false;
        Map.RemoveObject(player.Obstacle);
        return true;
    }

    /// <summary>Records a movement request. Callers validate it before this point.</summary>
    public bool SetInput(int peerId, int deltaX, int deltaY)
    {
        if (!players.TryGetValue(peerId, out SimulatedPlayer? player)) return false;
        player.DeltaX = deltaX;
        player.DeltaY = deltaY;
        return true;
    }

    /// <summary>Places a body directly and clears its input. Used by smoke scenarios and by a host that restages players.</summary>
    public bool SetPosition(int peerId, Vector2 position)
    {
        if (!players.TryGetValue(peerId, out SimulatedPlayer? player)) return false;
        player.DeltaX = 0;
        player.DeltaY = 0;
        player.Position = position;
        if (player.ObstacleActive) Map.MoveObject(player.Obstacle, Round(position.X), Round(position.Y));
        return true;
    }

    /// <summary>
    /// Advances the station by one frame: power, crew health, ghosts, movement, pulls, construction,
    /// and pushed fixtures, then drops any pull that is no longer valid. The caller publishes the
    /// results.
    /// </summary>
    public void Tick(double elapsedSeconds, double timestampSeconds, Vector2 ghostMinimum, Vector2 ghostMaximum)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return;
        float step = (float)Math.Min(elapsedSeconds, MaxStepSeconds);
        world.AdvancePower(step);
        world.AdvanceHealth(step);

        foreach (SimulatedPlayer player in players.Values.ToList())
            StepPlayer(player, step, timestampSeconds, ghostMinimum, ghostMaximum);

        world.ValidatePulls();
    }

    /// <summary>
    /// Moves a pulled crew member, a shoved crew member, or a pushed fixture to <paramref name="destination"/>
    /// and keeps its collision in step. Used for pull-follow and for shoves.
    /// </summary>
    public void MoveEntity(int entityId, Vector2 destination)
    {
        int x = Round(destination.X);
        int y = Round(destination.Y);
        if (players.TryGetValue(entityId, out SimulatedPlayer? pulled))
        {
            pulled.Position = new Vector2(x, y);
            if (pulled.ObstacleActive) Map.MoveObject(pulled.Obstacle, x, y);
            world.SetActorPosition(entityId, destination);
        }
        else if (entityObstacles.TryGetValue(entityId, out DynamicObject? obstacle))
        {
            Map.MoveObject(obstacle, x, y);
            world.SetEntityPosition(entityId, destination);
        }
    }

    /// <summary>A shoved crew member may enter a tile with no wall, closed door, object or other body on it.</summary>
    public bool CanCrewEnterTile(Vector2 tile)
    {
        int x = Round(tile.X);
        int y = Round(tile.Y);
        return Map.IsPassable(x, y, new PlayerMovementState()) && Map.GetObject(x, y) == null;
    }

    /// <summary>Keeps one collision object per solid replicated entity, pushable when the entity allows it.</summary>
    public void SyncEntityObstacles(IEnumerable<StationEntityState> states)
    {
        foreach (StationEntityState state in states)
        {
            if (!state.IsSolid) continue;
            int x = Round(state.Position.X);
            int y = Round(state.Position.Y);
            if (!entityObstacles.TryGetValue(state.Id, out DynamicObject? obstacle))
            {
                obstacle = new DynamicObject(x, y, isPushable: state.IsPushable, tag: state.Kind);
                entityObstacles.Add(state.Id, obstacle);
                Map.AddObject(obstacle);
                continue;
            }
            obstacle.IsPushable = state.IsPushable;
            obstacle.X = x;
            obstacle.Y = y;
        }
    }

    private void StepPlayer(SimulatedPlayer player, float step, double timestampSeconds,
        Vector2 ghostMinimum, Vector2 ghostMaximum)
    {
        int id = player.PeerId;
        CrewStat stat = world.GetCrewStat(id) ?? CrewStat.Stable;
        int moveX = player.DeltaX;
        int moveY = player.DeltaY;
        if (stat == CrewStat.Dead)
            world.TryMoveGhost(id, moveX, moveY, step, ghostMinimum, ghostMaximum);
        if (!stat.CanMove() || world.IsImmobilizedByPull(id))
        {
            moveX = 0;
            moveY = 0;
        }

        player.Movement.SpeedModifier = world.IsDraggingProne(id) ? SharedStationInventoryWorld.ProneDragSpeedFactor : 1f;
        bool lying = world.IsLyingDown(id);
        player.Movement.CurrentType = lying ? MovementType.Crawling : MovementType.Running;
        SyncCrewDensity(player, lying);

        float x = player.Position.X;
        float y = player.Position.Y;
        var intent = new MoveIntent { DeltaX = moveX, DeltaY = moveY, Timestamp = timestampSeconds };
        if (intent.DeltaX != 0 || intent.DeltaY != 0)
            movement.TryMove(player.Movement, ref x, ref y, intent, step);
        else
            movement.ApplyMomentum(player.Movement, ref x, ref y, step);

        if (player.ObstacleActive && (Round(x) != Round(player.Position.X) || Round(y) != Round(player.Position.Y)))
            Map.MoveObject(player.Obstacle, Round(x), Round(y));

        var vacatedTile = new Vector2(Round(player.Position.X), Round(player.Position.Y));
        player.Position = new Vector2(x, y);
        world.SetActorPosition(id, player.Position);
        if (world.TryGetPullFollow(id, vacatedTile, out int pulledId, out Vector2 pullDestination))
            MoveEntity(pulledId, pullDestination);
        world.AdvanceConstruction(id, step);
        SyncPushedEntities();
    }

    /// <summary>
    /// Lying bodies do not block movement (tgstation adds TRAIT_UNDENSE when lying down), so a casualty
    /// cannot seal a doorway. A body that stands back up blocks again once its tile is free.
    /// </summary>
    private void SyncCrewDensity(SimulatedPlayer player, bool lying)
    {
        bool dense = !lying;
        if (dense == player.ObstacleActive) return;
        if (!dense)
        {
            Map.RemoveObject(player.Obstacle);
            player.ObstacleActive = false;
            return;
        }
        int x = Round(player.Position.X);
        int y = Round(player.Position.Y);
        if (Map.GetObject(x, y) != null) return;
        player.Obstacle.X = x;
        player.Obstacle.Y = y;
        Map.AddObject(player.Obstacle);
        player.ObstacleActive = true;
    }

    /// <summary>Hands physics-driven displacement (a pushed cart) back to the authoritative world.</summary>
    private void SyncPushedEntities()
    {
        foreach ((int id, DynamicObject obstacle) in entityObstacles)
        {
            Vector2? known = world.GetState(id)?.Position;
            if (known is Vector2 position && (obstacle.X != Round(position.X) || obstacle.Y != Round(position.Y)))
                world.SetEntityPosition(id, new Vector2(obstacle.X, obstacle.Y));
        }
    }

    /// <summary>Rounds like Godot's Mathf.RoundToInt (midpoints to even), so tiles match the client.</summary>
    private static int Round(float value) => (int)MathF.Round(value);
}
