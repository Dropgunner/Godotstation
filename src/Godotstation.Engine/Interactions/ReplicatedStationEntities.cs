using System;
using System.Collections.Generic;
using System.Numerics;

namespace Godotstation.Engine.Interactions;

/// <summary>
/// The client's last replicated view of interactable station objects. This is
/// presentation and hit-test state; the server still validates every request.
/// </summary>
public sealed class ReplicatedStationEntities
{
    private readonly Dictionary<int, Entity> entities = new();

    public ReplicatedStationEntities()
    {
        Set(SharedStationInventoryWorld.WrenchId, new Vector2(-1, 2), true);
        Set(SharedStationInventoryWorld.FixtureId, new Vector2(0, 2), true);
        Set(SharedStationInventoryWorld.MetalSheetsId, new Vector2(1, 2), true);
        Set(SharedStationInventoryWorld.GirderId, new Vector2(2, 2), true);
        Set(SharedStationInventoryWorld.EngineeringAirlockId, new Vector2(8, -4), true);
        Set(SharedStationInventoryWorld.GeneratorFuelId, new Vector2(7, -7), true);
        Set(SharedStationInventoryWorld.GeneratorId, new Vector2(8, -7), true);
        Set(SharedStationInventoryWorld.PowerStorageId, new Vector2(9, -7), true);
        Set(SharedStationInventoryWorld.PoweredLampId, new Vector2(10, -7), false);
    }

    public int? WrenchOwner { get; private set; }
    public bool FixtureAnchored { get; private set; } = true;
    public int? MetalOwner { get; private set; }
    public int MetalSheetCount { get; private set; } = 2;
    public bool GirderPlated { get; private set; }
    public int? ConstructionWorker { get; private set; }
    public double ConstructionProgress { get; private set; }
    public bool EngineeringAirlockOpen { get; private set; }
    public int? GeneratorFuelOwner { get; private set; }
    public int GeneratorFuelCount { get; private set; } = 3;
    public bool GeneratorRunning { get; private set; }
    public double GeneratorFuelSeconds { get; private set; }
    public double StoredEnergy { get; private set; }
    public bool PowerOutputEnabled { get; private set; }
    public bool PoweredLampOn { get; private set; }

    public Entity Get(int id) => entities[id];

    public void ApplyShared(int? wrenchOwner, Vector2 wrenchPosition, Vector2 fixturePosition, bool fixtureAnchored)
    {
        WrenchOwner = wrenchOwner;
        FixtureAnchored = fixtureAnchored;
        Set(SharedStationInventoryWorld.WrenchId, wrenchPosition, wrenchOwner is null);
        Set(SharedStationInventoryWorld.FixtureId, fixturePosition, true);
    }

    public void ApplyConstruction(int? metalOwner, Vector2 metalPosition, int sheetCount,
        Vector2 girderPosition, bool plated, int? workerPeerId, double progressSeconds)
    {
        MetalOwner = metalOwner;
        MetalSheetCount = sheetCount;
        GirderPlated = plated;
        ConstructionWorker = workerPeerId;
        ConstructionProgress = progressSeconds;
        Set(SharedStationInventoryWorld.MetalSheetsId, metalPosition, metalOwner is null && sheetCount > 0);
        Set(SharedStationInventoryWorld.GirderId, girderPosition, !plated);
    }

    public void ApplyEngineeringAirlock(bool isOpen) => EngineeringAirlockOpen = isOpen;

    public void ApplyPower(int? fuelOwner, Vector2 fuelPosition, int fuelCount, bool generatorRunning,
        double fuelSeconds, double storedEnergy, bool outputEnabled, bool lampOn)
    {
        GeneratorFuelOwner = fuelOwner;
        GeneratorFuelCount = fuelCount;
        GeneratorRunning = generatorRunning;
        GeneratorFuelSeconds = fuelSeconds;
        StoredEnergy = storedEnergy;
        PowerOutputEnabled = outputEnabled;
        PoweredLampOn = lampOn;
        Set(SharedStationInventoryWorld.GeneratorFuelId, fuelPosition, fuelOwner is null && fuelCount > 0);
        Set(SharedStationInventoryWorld.GeneratorId, new Vector2(8, -7), true);
        Set(SharedStationInventoryWorld.PowerStorageId, new Vector2(9, -7), true);
    }

    public int? FindNearestTarget(Vector2 position, float radius)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(radius) || radius <= 0)
            return null;
        int? nearest = null;
        float nearestSquared = radius * radius;
        foreach (Entity entity in entities.Values)
        {
            if (!entity.IsTargetable) continue;
            float distanceSquared = Vector2.DistanceSquared(position, entity.Position);
            if (distanceSquared < nearestSquared ||
                (distanceSquared == nearestSquared && (nearest is null || entity.Id < nearest)))
            {
                nearest = entity.Id;
                nearestSquared = distanceSquared;
            }
        }
        return nearest;
    }

    private void Set(int id, Vector2 position, bool isTargetable) =>
        entities[id] = new Entity(id, position, isTargetable);

    public readonly record struct Entity(int Id, Vector2 Position, bool IsTargetable);
}
