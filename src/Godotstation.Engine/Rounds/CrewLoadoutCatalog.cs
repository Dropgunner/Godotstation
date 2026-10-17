using System;
using System.Collections.Generic;
using Godotstation.Engine.Access;
using Godotstation.Engine.Inventory;

namespace Godotstation.Engine.Rounds;

public sealed record CrewLoadoutItem(NamedSlot Slot, string ItemId, string DisplayName);

public sealed record CrewLoadoutProfile(
    IReadOnlyList<CrewLoadoutItem> Equipment,
    IReadOnlyList<StationClearance> StandardClearance,
    IReadOnlyList<StationClearance> LowPopulationClearance);

/// <summary>
/// Source-mapped starting equipment and ID-card access for roles in the LAN playtest.
/// This ports only the Engineer profile; it is not the complete Unitystation job catalog.
/// </summary>
public static class CrewLoadoutCatalog
{
    private static readonly CrewLoadoutProfile Engineer = new(
        Array.AsReadOnly(new[]
        {
            new CrewLoadoutItem(NamedSlot.feet, "67eaf03d3dc08490c85f77c1aedf4a15", "Workboots"),
            new CrewLoadoutItem(NamedSlot.uniform, "669c8831af14a4225968399b65b23461", "Engineer uniform"),
            new CrewLoadoutItem(NamedSlot.ear, "be396f8db64a5174394321d7f7e266f8", "Engineering headset"),
            new CrewLoadoutItem(NamedSlot.head, "64e430f828cfc465ea88b8d6e6fecda4", "Engineering hard hat"),
            new CrewLoadoutItem(NamedSlot.belt, "3e81def4ec9dcac419a017125d9ce991", "Engineering toolbelt"),
            new CrewLoadoutItem(NamedSlot.back, "9ece9b1c0b01fa46ab120b1b423b1739", "Industrial backpack"),
            new CrewLoadoutItem(NamedSlot.storage01, "96ad6abb3b62be4408efbb9ffd870f4d", "Engineering PDA"),
            new CrewLoadoutItem(NamedSlot.id, "a964a6c1326a47c40a104866a6ebce52", "Engineer ID card"),
            new CrewLoadoutItem(NamedSlot.storage02, "cb11f80a7ec1323428cd508fccb1f311", "Tray scanner"),
        }),
        Array.AsReadOnly(new[]
        {
            StationClearance.Construction,
            StationClearance.Engine,
            StationClearance.EngineEquip,
            StationClearance.ExternalAirlocks,
            StationClearance.MaintTunnels,
        }),
        Array.AsReadOnly(new[] { StationClearance.Atmospherics }));

    public static CrewLoadoutProfile? GetForJob(int jobId) =>
        jobId == StationCrewJobCatalog.EngineerJobId ? Engineer : null;
}
