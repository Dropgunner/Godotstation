using System;

namespace Godotstation.Engine.Power;

/// <summary>
/// Small deterministic generator -> storage -> powered-device loop for the LAN playtest.
/// Rates are deliberately playtest-scaled; they are not claimed as tgstation or Unitystation
/// numerical parity. State changes remain server-owned by the caller.
/// </summary>
public sealed class StationPowerLoop
{
    private const double FuelSecondsPerUnit = 4.0;
    private const double GenerationPerSecond = 2.0;
    private const double LoadPerSecond = 1.0;
    private const double StorageCapacity = 8.0;
    private double fuelSeconds;
    private double storedEnergy;

    public bool GeneratorRunning { get; private set; }
    public bool OutputEnabled { get; private set; }
    public bool DevicePowered => OutputEnabled && storedEnergy > 0;
    public double FuelSeconds => fuelSeconds;
    public double StoredEnergy => storedEnergy;

    public bool CanLoadFuel(int units) => units > 0 && fuelSeconds + units * FuelSecondsPerUnit <= 3 * FuelSecondsPerUnit;

    public bool TryLoadFuel(int units)
    {
        if (!CanLoadFuel(units)) return false;
        fuelSeconds += units * FuelSecondsPerUnit;
        return true;
    }

    public bool TryToggleGenerator()
    {
        if (!GeneratorRunning && fuelSeconds <= 0) return false;
        GeneratorRunning = !GeneratorRunning;
        return true;
    }

    public void ToggleOutput() => OutputEnabled = !OutputEnabled;

    /// <returns>True if externally visible power state changed.</returns>
    public bool Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return false;
        double previousFuelSeconds = fuelSeconds;
        double previousStoredEnergy = storedEnergy;
        bool wasRunning = GeneratorRunning;

        if (GeneratorRunning)
        {
            double generatedSeconds = Math.Min(elapsedSeconds, fuelSeconds);
            fuelSeconds = Math.Max(0, fuelSeconds - generatedSeconds);
            storedEnergy = Math.Min(StorageCapacity, storedEnergy + generatedSeconds * GenerationPerSecond);
            if (fuelSeconds <= 0) GeneratorRunning = false;
        }

        if (OutputEnabled)
            storedEnergy = Math.Max(0, storedEnergy - elapsedSeconds * LoadPerSecond);

        return previousFuelSeconds != fuelSeconds || previousStoredEnergy != storedEnergy ||
            wasRunning != GeneratorRunning;
    }
}
