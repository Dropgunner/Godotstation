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
}

/// <summary>What a map author needs to know about one entity kind.</summary>
public sealed record StationEntityKindInfo(string Kind, string DisplayName, string Summary, bool Stackable, int DefaultAmount);

/// <summary>The catalogue of placeable entity kinds, in palette order.</summary>
public static class StationEntityKinds
{
    public static IReadOnlyList<StationEntityKindInfo> All { get; } =
    [
        new(StationEntityKind.Wrench, "Wrench", "Hand tool; anchors or frees a service cart.", false, 0),
        new(StationEntityKind.ServiceCart, "Service cart", "Pushable cart; wrench it to anchor it.", false, 0),
        new(StationEntityKind.MetalSheets, "Metal sheets", "Stackable construction material for plating girders.", true, 2),
        new(StationEntityKind.Girder, "Girder", "Solid frame; two sheets and four seconds plate it into a wall.", false, 0),
        new(StationEntityKind.EngineeringAirlock, "Engineering airlock", "Door that opens only for Engine clearance.", false, 0),
        new(StationEntityKind.GeneratorFuel, "Generator fuel", "Stackable fuel for the generator.", true, 3),
        new(StationEntityKind.FuelGenerator, "Fuel generator", "Burns fuel to charge the storage unit.", false, 0),
        new(StationEntityKind.PowerStorage, "Power storage", "Stores generator output; toggle its output on.", false, 0),
        new(StationEntityKind.ApcLamp, "APC lamp", "Lit while the storage unit outputs power.", false, 0),
    ];

    public static bool IsKnown(string kind) => TryGet(kind, out _);

    public static bool TryGet(string kind, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out StationEntityKindInfo? info)
    {
        foreach (StationEntityKindInfo candidate in All)
        {
            if (candidate.Kind == kind)
            {
                info = candidate;
                return true;
            }
        }
        info = null;
        return false;
    }
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
    ];
}
