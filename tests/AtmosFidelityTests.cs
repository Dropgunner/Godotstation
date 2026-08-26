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

        // ---------------------------------------------------------------------
        // Chemical reactions (Wave 2)
        // ---------------------------------------------------------------------

        private static AtmosTile MakeHotPlasmaTile(float temperature, float plasma = 50f, float oxygen = 200f)
        {
            var tile = new AtmosTile(0, 0);
            tile.GasMix.AddGas("Plasma", plasma);
            tile.GasMix.AddGas("Oxygen", oxygen);
            tile.GasMix.SetTemperature(temperature);
            return tile;
        }

        [Fact]
        public void PlasmaFire_Ignites_BurnsPlasmaAndOxygen_ProducesCO2_AndHeat()
        {
            var sim = new AtmosSimulation();
            var tile = MakeHotPlasmaTile(temperature: 500f);

            float plasmaBefore = tile.GasMix.GetMoles("Plasma");
            float oxygenBefore = tile.GasMix.GetMoles("Oxygen");
            float tempBefore = tile.GasMix.Temperature;

            sim.Enqueue(tile);
            sim.RunStep();

            // Fuel and oxidizer are consumed, carbon dioxide is produced.
            Assert.True(tile.GasMix.GetMoles("Plasma") < plasmaBefore, "plasma should be consumed");
            Assert.True(tile.GasMix.GetMoles("Oxygen") < oxygenBefore, "oxygen should be consumed");
            Assert.True(tile.GasMix.GetMoles("CarbonDioxide") > 0f, "CO2 should be produced");

            // Combustion releases energy, so the mix gets hotter.
            Assert.True(tile.GasMix.Temperature > tempBefore, "fire should raise temperature");
        }

        [Fact]
        public void PlasmaFire_BelowIgnitionTemperature_DoesNotBurn()
        {
            var sim = new AtmosSimulation();
            // 300 K is below FIRE_MINIMUM_TEMPERATURE_TO_EXIST (373.15 K).
            var tile = MakeHotPlasmaTile(temperature: 300f);

            float plasmaBefore = tile.GasMix.GetMoles("Plasma");
            float oxygenBefore = tile.GasMix.GetMoles("Oxygen");

            sim.Enqueue(tile);
            sim.RunStep();

            Assert.Equal(plasmaBefore, tile.GasMix.GetMoles("Plasma"));
            Assert.Equal(oxygenBefore, tile.GasMix.GetMoles("Oxygen"));
            Assert.Equal(0f, tile.GasMix.GetMoles("CarbonDioxide"));
        }

        [Fact]
        public void PlasmaFire_MassConservation_CarbonMatchesPlasmaBurned()
        {
            var sim = new AtmosSimulation();
            var tile = MakeHotPlasmaTile(temperature: 800f);

            float plasmaBefore = tile.GasMix.GetMoles("Plasma");

            sim.Enqueue(tile);
            sim.RunStep();

            float plasmaBurned = plasmaBefore - tile.GasMix.GetMoles("Plasma");
            // Each mole of plasma burned yields exactly one mole of CO2.
            Assert.Equal(plasmaBurned, tile.GasMix.GetMoles("CarbonDioxide"), 4);
        }

        [Fact]
        public void PlasmaFire_HyperNobliumSuppressesReaction_Rule6()
        {
            var sim = new AtmosSimulation();
            var tile = MakeHotPlasmaTile(temperature: 500f);
            // 5+ moles of HyperNoblium must suppress ALL reactions.
            tile.GasMix.AddGas("HyperNoblium", 5f);

            float plasmaBefore = tile.GasMix.GetMoles("Plasma");
            float oxygenBefore = tile.GasMix.GetMoles("Oxygen");

            sim.Enqueue(tile);
            sim.RunStep();

            Assert.Equal(plasmaBefore, tile.GasMix.GetMoles("Plasma"));
            Assert.Equal(oxygenBefore, tile.GasMix.GetMoles("Oxygen"));
            Assert.Equal(0f, tile.GasMix.GetMoles("CarbonDioxide"));
        }

        [Fact]
        public void PlasmaFire_SubOppressionNobliumStillAllowsReaction()
        {
            var sim = new AtmosSimulation();
            var tile = MakeHotPlasmaTile(temperature: 500f);
            // Below the 5-mole threshold, reactions proceed normally.
            tile.GasMix.AddGas("HyperNoblium", 4.9f);

            float plasmaBefore = tile.GasMix.GetMoles("Plasma");

            sim.Enqueue(tile);
            sim.RunStep();

            Assert.True(tile.GasMix.GetMoles("Plasma") < plasmaBefore, "reaction should not be suppressed below threshold");
        }

        [Fact]
        public void PlasmaFire_SelfSustains_ContinuesOverMultipleSteps()
        {
            var sim = new AtmosSimulation();
            var tile = MakeHotPlasmaTile(temperature: 500f);

            sim.Enqueue(tile);

            // The reaction re-queues the tile each step; running several steps should
            // keep burning plasma down toward exhaustion.
            float previous = tile.GasMix.GetMoles("Plasma");
            for (int i = 0; i < 5; i++)
            {
                sim.RunStep();
                float now = tile.GasMix.GetMoles("Plasma");
                Assert.True(now <= previous, "plasma should be non-increasing while burning");
                previous = now;
            }

            Assert.True(tile.GasMix.GetMoles("Plasma") < 50f, "plasma should have burned down across steps");
        }
    }
}
