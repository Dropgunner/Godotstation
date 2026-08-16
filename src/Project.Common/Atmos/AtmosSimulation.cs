using System;
using System.Collections.Generic;

namespace Project.Common.Atmos
{
    public class AtmosSimulation
    {
        private readonly Queue<AtmosTile> _updateQueue = new Queue<AtmosTile>();
        private readonly HashSet<AtmosTile> _inQueue = new HashSet<AtmosTile>();

        private readonly GasMix _meanGasMix = new GasMix();
        private readonly List<AtmosTile> _activeNodes = new List<AtmosTile>(5);

        public void Enqueue(AtmosTile tile)
        {
            if (_inQueue.Add(tile))
            {
                _updateQueue.Enqueue(tile);
            }
        }

        public void RunStep()
        {
            int count = _updateQueue.Count;
            for (int i = 0; i < count; i++)
            {
                var tile = _updateQueue.Dequeue();
                _inQueue.Remove(tile);
                UpdateTile(tile);
            }
        }

        private void UpdateTile(AtmosTile tile)
        {
            _activeNodes.Clear();
            _activeNodes.Add(tile);

            foreach (var neighbor in tile.Neighbors)
            {
                _activeNodes.Add(neighbor);
            }

            // 1. Open to Solid conductivity
            if (tile.Type == TileType.Occupied || tile.IsIsolated)
            {
                // We'd use meanGasMix here in a full port, simplified for benchmark
            }

            // 2. Gas Equalization
            bool doEqualise = tile.CanPassGas();
            if (doEqualise)
            {
                if (TryPressureChanged(tile, out int windX, out int windY, out float windForce))
                {
                    tile.WindDirectionX = windX;
                    tile.WindDirectionY = windY;
                    tile.WindForce = windForce;

                    Equalize();

                    // Re-queue neighbors because state changed
                    for (int i = 1; i < _activeNodes.Count; i++)
                    {
                        Enqueue(_activeNodes[i]);
                    }
                }
            }

            // 3. Solid Conductivity
            Conductivity(tile);

            // 4. Reactions would go here
        }

        private void Equalize()
        {
            if (_activeNodes.Count <= 1) return;

            CalcMeanGasMix();

            for (int i = 0; i < _activeNodes.Count; i++)
            {
                var node = _activeNodes[i];
                if (!node.CanPassGas()) continue;

                if (node.Type != TileType.Space)
                {
                    node.GasMix.CopyFrom(_meanGasMix);
                }
                else
                {
                    node.GasMix.SetToEmpty();
                }
            }
        }

        private void CalcMeanGasMix()
        {
            _meanGasMix.Clear();
            int targetCount = 0;

            for (int i = 0; i < _activeNodes.Count; i++)
            {
                var node = _activeNodes[i];
                if (node.CanPassGas())
                {
                    _meanGasMix.Volume += node.GasMix.Volume;
                    GasMix.TransferGas(_meanGasMix, node.GasMix, node.GasMix.Moles, true);
                    targetCount++;
                }
            }

            if (targetCount == 0) return;

            _meanGasMix.Volume /= targetCount;

            var keys = new List<string>(_meanGasMix.Gases.Keys);
            foreach (var key in keys)
            {
                float totalMoles = _meanGasMix.Gases[key];
                _meanGasMix.Gases[key] = totalMoles / targetCount;
            }

            _meanGasMix.RecalculatePressure();
        }

        private bool TryPressureChanged(AtmosTile tile, out int windX, out int windY, out float windForce)
        {
            windX = 0;
            windY = 0;
            windForce = 0;
            bool changed = false;

            for (int i = 1; i < _activeNodes.Count; i++)
            {
                var neighbor = _activeNodes[i];
                if (!neighbor.CanPassGas()) continue;

                float pressureDiff = tile.GasMix.Pressure - neighbor.GasMix.Pressure;
                float absDiff = Math.Abs(pressureDiff);

                if (absDiff > AtmosDefines.MIN_PRESSURE_DIFFERENCE)
                {
                    changed = true;
                    if (absDiff > windForce) windForce = absDiff;

                    int offsetX = neighbor.X - tile.X;
                    int offsetY = neighbor.Y - tile.Y;

                    if (pressureDiff > 0)
                    {
                        windX += offsetX;
                        windY += offsetY;
                    }
                    else if (pressureDiff < 0)
                    {
                        windX -= offsetX;
                        windY -= offsetY;
                    }
                    continue;
                }

                // Check mole differences
                foreach (var kvp in tile.GasMix.Gases)
                {
                    float myMoles = kvp.Value;
                    float neighborMoles = neighbor.GasMix.GetMoles(kvp.Key);
                    if (Math.Abs(myMoles - neighborMoles) > AtmosDefines.MIN_PRESSURE_DIFFERENCE)
                    {
                        changed = true;
                        break;
                    }
                }
            }

            windX = Math.Clamp(windX, -1, 1);
            windY = Math.Clamp(windY, -1, 1);
            return changed;
        }

        private void Conductivity(AtmosTile tile)
        {
            if (!tile.StartingSuperConduct && !tile.AllowedToSuperConduct) return;

            if (tile.ConductivityTemperature < (tile.StartingSuperConduct ?
                AtmosDefines.MINIMUM_TEMPERATURE_START_SUPERCONDUCTION :
                AtmosDefines.MINIMUM_TEMPERATURE_FOR_SUPERCONDUCTION))
            {
                tile.AllowedToSuperConduct = false;
                tile.StartingSuperConduct = false;
                return;
            }

            for (int i = 1; i < _activeNodes.Count; i++)
            {
                var neighbor = _activeNodes[i];
                float tempDelta = tile.ConductivityTemperature;

                if (neighbor.Type == TileType.Space)
                {
                    if (tile.ConductivityTemperature <= 273.15f) continue;
                    tempDelta -= AtmosDefines.SPACE_TEMPERATURE;

                    float heat = neighbor.ThermalConductivity * tempDelta *
                        (tile.HeatCapacity * AtmosDefines.SPACE_HEAT_CAPACITY /
                        (tile.HeatCapacity + AtmosDefines.SPACE_HEAT_CAPACITY));

                    tile.ConductivityTemperature -= heat / tile.HeatCapacity;
                }
                else
                {
                    tempDelta -= neighbor.ConductivityTemperature;
                    if (Math.Abs(tempDelta) <= AtmosDefines.MINIMUM_TEMPERATURE_DELTA_TO_CONSIDER) continue;

                    float heat = neighbor.ThermalConductivity * tempDelta *
                        (tile.HeatCapacity * neighbor.HeatCapacity /
                        (tile.HeatCapacity + neighbor.HeatCapacity));

                    tile.ConductivityTemperature -= heat / tile.HeatCapacity;
                    neighbor.ConductivityTemperature += heat / neighbor.HeatCapacity;

                    if (neighbor.ConductivityTemperature >= AtmosDefines.MINIMUM_TEMPERATURE_FOR_SUPERCONDUCTION)
                    {
                        neighbor.AllowedToSuperConduct = true;
                        Enqueue(neighbor);
                    }
                }
            }
        }
    }
}
