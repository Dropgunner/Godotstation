using System.Linq;
using Godotstation.Engine.Access;
using Godotstation.Engine.Interactions;
using Godotstation.Engine.Inventory;
using Godotstation.Engine.Movement;
using Godotstation.Engine.Rounds;
using Xunit;

namespace Godotstation.Tests.Rounds;

public sealed class CrewLoadoutTests
{
    [Fact]
    public void EngineerProfileMatchesPinnedUpstreamEquipmentAndClearance()
    {
        CrewLoadoutProfile profile = Assert.IsType<CrewLoadoutProfile>(
            CrewLoadoutCatalog.GetForJob(StationCrewJobCatalog.EngineerJobId));

        Assert.Equal(9, profile.Equipment.Count);
        Assert.Contains(profile.Equipment, item => item.Slot == NamedSlot.id &&
            item.ItemId == "a964a6c1326a47c40a104866a6ebce52");
        Assert.Contains(profile.Equipment, item => item.Slot == NamedSlot.belt &&
            item.ItemId == "3e81def4ec9dcac419a017125d9ce991");
        Assert.Equal(new[]
        {
            StationClearance.Construction,
            StationClearance.Engine,
            StationClearance.EngineEquip,
            StationClearance.ExternalAirlocks,
            StationClearance.MaintTunnels,
        }, profile.StandardClearance);
        Assert.Equal(new[] { StationClearance.Atmospherics }, profile.LowPopulationClearance);
    }

    [Fact]
    public void EngineerSpawnsEquippedAndItsIdCardOpensEngineeringAirlock()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(7, new System.Numerics.Vector2(8, -3),
            StationCrewJobCatalog.EngineerJobId));

        Assert.True(world.HasClearance(7, StationClearance.Engine));
        Assert.True(world.HasClearance(7, StationClearance.Atmospherics, lowPopulation: true));
        Assert.False(world.HasClearance(7, StationClearance.Atmospherics));
        string summary = world.GetCrewLoadoutSummary(7);
        Assert.Contains("Engineer kit:", summary);
        Assert.Contains("Workboots", summary);
        Assert.Contains("Engineer ID card", summary);

        bool? observedDoorState = null;
        world.EngineeringAirlockStateChanged += state => observedDoorState = state;
        Assert.True(world.TryInteract(7, SharedStationInventoryWorld.EngineeringAirlockId,
            Godotstation.Common.Interactions.Intent.Help, false));

        Assert.True(world.IsEngineeringAirlockOpen);
        Assert.True(world.EngineeringAirlockPassability.CanPass(new PlayerMovementState()));
        Assert.True(observedDoorState);
    }

    [Fact]
    public void AssistantCannotOpenEngineeringAirlock()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(8, new System.Numerics.Vector2(8, -3),
            StationCrewJobCatalog.AssistantJobId));

        Assert.False(world.HasClearance(8, StationClearance.Engine));
        Assert.False(world.TryInteract(8, SharedStationInventoryWorld.EngineeringAirlockId,
            Godotstation.Common.Interactions.Intent.Help, false));
        Assert.False(world.IsEngineeringAirlockOpen);
        Assert.False(world.EngineeringAirlockPassability.CanPass(new PlayerMovementState()));
    }
}
