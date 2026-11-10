using System.Collections.Generic;
using System.Numerics;

namespace Godotstation.Engine.Entities;

/// <summary>Entity kinds the shared station knows how to spawn, replicate and draw.</summary>
public static class StationEntityKind
{
    public const string Wrench = "wrench";
    public const string ServiceCart = "service-cart";
    public const string MetalSheets = "metal-sheets";
    public const string Girder = "girder";
    public const string EngineeringAirlock = "engineering-airlock";
    public const string GeneratorFuel = "generator-fuel";
    public const string FuelGenerator = "fuel-generator";
    public const string PowerStorage = "power-storage";
    public const string ApcLamp = "apc-lamp";
    public const string BruisePack = "bruise-pack";
    /// <summary>A crew body. Its entity id is the owning peer id.</summary>
    public const string Crew = "crew";
}

/// <summary>Names of the small per-kind state values carried in <see cref="StationEntityState.Fields"/>.</summary>
public static class StationEntityField
{
    public const string Amount = "amount";
    public const string Anchored = "anchored";
    public const string Plated = "plated";
    public const string Worker = "worker";
    public const string Progress = "progress";
    public const string Open = "open";
    public const string Running = "running";
    public const string FuelSeconds = "fuel_seconds";
    public const string StoredEnergy = "stored_energy";
    public const string OutputEnabled = "output_enabled";
    public const string Powered = "powered";
    public const string Health = "health";
    public const string Stat = "stat";
    public const string BruteLoss = "brute_loss";
    public const string OxyLoss = "oxy_loss";
    public const string Job = "job";
    public const string TreatmentHealer = "treatment_healer";
    public const string TreatmentPatient = "treatment_patient";
    /// <summary>Peer this crew member is performing CPR on.</summary>
    public const string CprPatient = "cpr_patient";
    /// <summary>Set on a dead crew body whose player has left it as a ghost.</summary>
    public const string Ghost = "ghost";
    public const string GhostX = "ghost_x";
    public const string GhostY = "ghost_y";
    /// <summary>Entity id this crew member is pulling, when they pull something.</summary>
    public const string Pulling = "pulling";
}

/// <summary>Where one entity starts the round. Stackable kinds use <see cref="Amount"/>.</summary>
public sealed record StationEntitySpawn(int Id, string Kind, Vector2 Position, int Amount = 0);

/// <summary>The playtest station's entities. Adding an object means one entry here plus its kind's behaviour.</summary>
public static class StationEntityCatalog
{
    public const int WrenchId = 9001;
    public const int ServiceCartId = 9002;
    public const int MetalSheetsId = 9003;
    public const int GirderId = 9004;
    public const int EngineeringAirlockId = 9005;
    public const int GeneratorFuelId = 9006;
    public const int FuelGeneratorId = 9007;
    public const int PowerStorageId = 9008;
    public const int ApcLampId = 9009;
    public const int BruisePackId = 9010;

    public static IReadOnlyList<StationEntitySpawn> Playtest { get; } =
    [
        new(WrenchId, StationEntityKind.Wrench, new Vector2(-1, 2)),
        new(ServiceCartId, StationEntityKind.ServiceCart, new Vector2(0, 2)),
        new(MetalSheetsId, StationEntityKind.MetalSheets, new Vector2(1, 2), Amount: 2),
        new(GirderId, StationEntityKind.Girder, new Vector2(2, 2)),
        new(EngineeringAirlockId, StationEntityKind.EngineeringAirlock, new Vector2(8, -4)),
        new(GeneratorFuelId, StationEntityKind.GeneratorFuel, new Vector2(7, -7), Amount: 3),
        new(FuelGeneratorId, StationEntityKind.FuelGenerator, new Vector2(8, -7)),
        new(PowerStorageId, StationEntityKind.PowerStorage, new Vector2(9, -7)),
        new(ApcLampId, StationEntityKind.ApcLamp, new Vector2(10, -7)),
        new(BruisePackId, StationEntityKind.BruisePack, new Vector2(-8, -7), Amount: 6),
    ];
}
