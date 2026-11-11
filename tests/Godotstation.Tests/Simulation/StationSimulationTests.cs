using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Interactions;
using Godotstation.Engine.Movement;
using Godotstation.Engine.Rounds;
using Godotstation.Engine.Simulation;
using Xunit;

namespace Godotstation.Tests.Simulation;

public sealed class StationSimulationTests
{
    private static readonly Vector2 GhostMin = new(StationPlaytestLayout.MinX, StationPlaytestLayout.MinY);
    private static readonly Vector2 GhostMax = new(StationPlaytestLayout.MaxX, StationPlaytestLayout.MaxY);

    private static (StationSimulation Sim, SharedStationInventoryWorld World) Station()
    {
        var world = new SharedStationInventoryWorld();
        var sim = new StationSimulation(StationPlaytestLayout.CreateCollisionMap(), world);
        return (sim, world);
    }

    private static void Run(StationSimulation sim, double seconds)
    {
        for (double t = 0; t < seconds - 1e-9; t += 0.05)
            sim.Tick(0.05, 0, GhostMin, GhostMax);
    }

    [Fact]
    public void InputMovesTheBodyAndItsCollisionObjectFollows()
    {
        (StationSimulation sim, SharedStationInventoryWorld world) = Station();
        Assert.True(world.AddActor(1, new Vector2(-6, 0)));
        sim.AddPlayer(1, new Vector2(-6, 0));
        Assert.True(sim.SetInput(1, 1, 0));

        Run(sim, 1);

        SimulatedPlayer body = sim.GetPlayer(1)!;
        Assert.True(body.Position.X > -6);
        Assert.Equal(body.Obstacle, sim.Map.GetObject(
            (int)System.MathF.Round(body.Position.X), (int)System.MathF.Round(body.Position.Y)));
    }

    [Fact]
    public void WallsStopAWalkingBody()
    {
        (StationSimulation sim, SharedStationInventoryWorld world) = Station();
        Assert.True(world.AddActor(1, new Vector2(-6, 2)));
        sim.AddPlayer(1, new Vector2(-6, 2));
        sim.SetInput(1, 1, 0);

        Run(sim, 6);

        // The partition at x = -4 blocks row 2, so the body cannot pass it.
        Assert.True(sim.GetPlayer(1)!.Position.X < -3.4f);
    }

    [Fact]
    public void SetPositionClearsInputAndMovesTheObstacle()
    {
        (StationSimulation sim, SharedStationInventoryWorld world) = Station();
        Assert.True(world.AddActor(1, new Vector2(-6, 0)));
        sim.AddPlayer(1, new Vector2(-6, 0));
        sim.SetInput(1, 1, 1);

        Assert.True(sim.SetPosition(1, new Vector2(-6, -6)));

        SimulatedPlayer body = sim.GetPlayer(1)!;
        Assert.Equal(0, body.DeltaX);
        Assert.Equal(0, body.DeltaY);
        Assert.Equal(-6, body.Obstacle.Y);
        Assert.False(sim.SetPosition(99, Vector2.Zero));
    }

    [Fact]
    public void RemovingAPlayerClearsTheirCollision()
    {
        (StationSimulation sim, SharedStationInventoryWorld world) = Station();
        Assert.True(world.AddActor(1, new Vector2(-6, 0)));
        sim.AddPlayer(1, new Vector2(-6, 0));

        Assert.True(sim.RemovePlayer(1));

        Assert.Null(sim.Map.GetObject(-6, 0));
        Assert.Null(sim.GetPlayer(1));
        Assert.False(sim.RemovePlayer(1));
    }

    [Fact]
    public void SolidEntitiesBecomeObstaclesAndPushedFixturesAreReplicated()
    {
        (StationSimulation sim, SharedStationInventoryWorld world) = Station();
        sim.SyncEntityObstacles(world.CaptureAll());

        Assert.NotNull(sim.Map.GetObject(0, 2)); // the service cart
        Assert.Null(sim.Map.GetObject(-1, 2));   // the wrench is not solid
    }

    [Fact]
    public void AStandingBodyBlocksAndALyingBodyDoesNot()
    {
        (StationSimulation sim, SharedStationInventoryWorld world) = Station();
        world.CanCrewEnterTile = _ => false;
        Assert.True(world.AddActor(1, new Vector2(-1, 0)));
        Assert.True(world.AddActor(2, new Vector2(-1, 1)));
        sim.AddPlayer(1, new Vector2(-1, 0));
        sim.AddPlayer(2, new Vector2(-1, 1));

        Assert.True(world.TryInteract(2, 1, Intent.Disarm, false)); // blocked shove knocks crew 1 down
        Assert.True(world.IsLyingDown(1));

        sim.Tick(0.05, 0, GhostMin, GhostMax);
        Assert.False(sim.GetPlayer(1)!.ObstacleActive);
        Assert.Null(sim.Map.GetObject(-1, 0));

        Run(sim, 2.5); // the knockdown lasts two seconds
        Assert.False(world.IsLyingDown(1));
        Assert.True(sim.GetPlayer(1)!.ObstacleActive);
        Assert.Equal(sim.GetPlayer(1)!.Obstacle, sim.Map.GetObject(-1, 0));
    }

    [Fact]
    public void APulledBodyFollowsIntoTheTileItsPullerLeft()
    {
        (StationSimulation sim, SharedStationInventoryWorld world) = Station();
        Assert.True(world.AddActor(2, new Vector2(-2, 2)));  // puller
        Assert.True(world.AddActor(1, new Vector2(-1, 2)));  // pulled
        sim.AddPlayer(2, new Vector2(-2, 2));
        sim.AddPlayer(1, new Vector2(-1, 2));
        Assert.True(world.TryTogglePull(2, 1));

        sim.SetInput(2, 0, -1); // step down, leaving the pulled body diagonal
        for (int i = 0; i < 200 && System.MathF.Round(sim.GetPlayer(2)!.Position.Y) > 1; i++)
            sim.Tick(0.05, 0, GhostMin, GhostMax);

        Assert.Equal(new Vector2(-2, 2), sim.GetPlayer(1)!.Position);
        Assert.Equal(2, world.GetPuller(1));
    }

    [Fact]
    public void MoveEntityMovesAFixtureAndItsCollision()
    {
        (StationSimulation sim, SharedStationInventoryWorld world) = Station();
        sim.SyncEntityObstacles(world.CaptureAll());
        var cart = sim.Map.GetObject(0, 2)!;

        sim.MoveEntity(StationEntityCatalog.ServiceCartId, new Vector2(0, 3));

        Assert.Null(sim.Map.GetObject(0, 2));
        Assert.Equal(cart, sim.Map.GetObject(0, 3));
        Assert.Equal(new Vector2(0, 3), world.GetState(StationEntityCatalog.ServiceCartId)!.Position);
    }
}
