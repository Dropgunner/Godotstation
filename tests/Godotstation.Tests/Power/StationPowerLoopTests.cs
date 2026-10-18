using Godotstation.Engine.Power;
using Xunit;

namespace Godotstation.Tests.Power;

public sealed class StationPowerLoopTests
{
    [Fact]
    public void GeneratorChargesStorageAndOutputPowersTheDevice()
    {
        var power = new StationPowerLoop();

        Assert.True(power.TryLoadFuel(1));
        Assert.True(power.TryToggleGenerator());
        power.ToggleOutput();
        Assert.False(power.DevicePowered);

        Assert.True(power.Advance(1));
        Assert.True(power.DevicePowered);
        Assert.Equal(1, power.StoredEnergy, 6);
    }

    [Fact]
    public void FuelExhaustionCausesOutageAndASecondFuelLoadRecoversIt()
    {
        var power = new StationPowerLoop();
        Assert.True(power.TryLoadFuel(1));
        Assert.True(power.TryToggleGenerator());
        power.ToggleOutput();
        power.Advance(4);
        Assert.False(power.GeneratorRunning);
        Assert.True(power.DevicePowered);

        Assert.True(power.Advance(4));
        Assert.False(power.DevicePowered);

        Assert.True(power.TryLoadFuel(1));
        Assert.True(power.TryToggleGenerator());
        Assert.True(power.Advance(1));
        Assert.True(power.DevicePowered);
    }

    [Fact]
    public void RejectsInvalidFuelTimeAndStartingWithoutFuel()
    {
        var power = new StationPowerLoop();

        Assert.False(power.TryToggleGenerator());
        Assert.False(power.TryLoadFuel(4));
        Assert.False(power.Advance(double.NaN));
        Assert.Equal(0, power.StoredEnergy);
    }
}
