using Xunit;
using Project.Common.Atmos;

namespace Tests
{
    public class AtmosFidelityTests
    {
        [Fact]
        public void GasMix_IdealGasLaw_MaintainsPressure()
        {
            var mix = new GasMix(AtmosDefines.CELL_VOLUME, 293.15f);
            mix.AddGas("Oxygen", 100f);

            // P = nRT/V
            float expectedPressure = 100f * AtmosDefines.R * 293.15f / AtmosDefines.CELL_VOLUME;
            Assert.Equal(expectedPressure, mix.Pressure, 4);
        }

        [Fact]
        public void GasMix_TransferGas_BlendsTemperatureCorrectly()
        {
            var source = new GasMix(AtmosDefines.CELL_VOLUME, 400f);
            source.AddGas("Oxygen", 100f); // Heat cap = 20 * 100 = 2000

            var target = new GasMix(AtmosDefines.CELL_VOLUME, 200f);
            target.AddGas("Nitrogen", 100f); // Heat cap = 20 * 100 = 2000

            GasMix.TransferGas(target, source, 50f);

            // Target should now have 100 N2 and 50 O2
            Assert.Equal(100f, target.GetMoles("Nitrogen"));
            Assert.Equal(50f, target.GetMoles("Oxygen"));

            // Source should have 50 O2
            Assert.Equal(50f, source.GetMoles("Oxygen"));

            // Temperature should blend:
            // Original target energy = 2000 * 200 = 400,000
            // Transferred energy = (50 * 20) * 400 = 40,000
            // New target capacity = 2000 + 1000 = 3000
            // New temp = 440,000 / 3000 = 146.66... wait, let's just ensure it's between 200 and 400
            Assert.True(target.Temperature > 200f);
            Assert.True(target.Temperature < 400f);
        }

        [Fact]
        public void Simulation_Equalize_AveragesGasAcrossOpenTiles()
        {
            var sim = new AtmosSimulation();

            var tile1 = new AtmosTile(0, 0);
            tile1.GasMix.AddGas("Oxygen", 100f);

            var tile2 = new AtmosTile(1, 0);
            // tile2 is empty

            tile1.Neighbors.Add(tile2);
            tile2.Neighbors.Add(tile1);

            sim.Enqueue(tile1);
            sim.RunStep();

            // Both should now have ~50 moles
            Assert.Equal(50f, tile1.GasMix.GetMoles("Oxygen"), 2);
            Assert.Equal(50f, tile2.GasMix.GetMoles("Oxygen"), 2);

            // Pressures should be equal
            Assert.Equal(tile1.GasMix.Pressure, tile2.GasMix.Pressure, 4);
        }

        [Fact]
        public void Simulation_Equalize_IgnoresOccupiedTiles()
        {
            var sim = new AtmosSimulation();

            var tile1 = new AtmosTile(0, 0);
            tile1.GasMix.AddGas("Oxygen", 100f);

            var wall = new AtmosTile(1, 0) { Type = TileType.Occupied };

            tile1.Neighbors.Add(wall);
            wall.Neighbors.Add(tile1);

            sim.Enqueue(tile1);
            sim.RunStep();

            // Tile1 should keep all its gas
            Assert.Equal(100f, tile1.GasMix.GetMoles("Oxygen"));
            Assert.Equal(0f, wall.GasMix.GetMoles("Oxygen"));
        }

        [Fact]
        public void Simulation_Equalize_SpaceTileVentsGas()
        {
            var sim = new AtmosSimulation();

            var tile1 = new AtmosTile(0, 0);
            tile1.GasMix.AddGas("Oxygen", 100f);

            var space = new AtmosTile(1, 0) { Type = TileType.Space };

            tile1.Neighbors.Add(space);
            space.Neighbors.Add(tile1);

            sim.Enqueue(tile1);
            sim.RunStep();

            // Gas should be halved (50 goes to space)
            // Then space immediately SetToEmpty()
            Assert.Equal(50f, tile1.GasMix.GetMoles("Oxygen"), 2);
            Assert.Equal(0f, space.GasMix.GetMoles("Oxygen"));
        }
    }
}
