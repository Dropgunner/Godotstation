using System;
using System.Collections.Generic;

namespace Project.Common.Atmos
{
    public class GasMix
    {
        public Dictionary<string, float> Gases { get; } = new Dictionary<string, float>();
        
        private float _pressure;
        public float Pressure
        {
            get => _pressure;
            set => _pressure = Math.Max(0, value);
        }

        public float Volume { get; set; } = AtmosDefines.CELL_VOLUME;

        private float _temperature;
        public float Temperature
        {
            get => _temperature;
            set => _temperature = Math.Max(0, value);
        }

        public float Moles
        {
            get
            {
                float total = 0;
                foreach (var moles in Gases.Values)
                {
                    total += moles;
                }
                return total;
            }
        }

        public float WholeHeatCapacity
        {
            get
            {
                float capacity = 0;
                foreach (var kvp in Gases)
                {
                    float specificHeat = AtmosDefines.GetGasHeatCapacity(kvp.Key);
                    capacity += specificHeat * kvp.Value;
                }
                return capacity;
            }
        }

        public float InternalEnergy
        {
            get => WholeHeatCapacity * Temperature;
            set
            {
                float cap = WholeHeatCapacity;
                if (cap > 0)
                {
                    Temperature = value / cap;
                }
                else
                {
                    Temperature = 0;
                }
                RecalculatePressure();
            }
        }

        public GasMix(float volume = AtmosDefines.CELL_VOLUME, float temperature = 293.15f)
        {
            Volume = volume;
            Temperature = temperature;
            Pressure = 0;
        }

        public void SetTemperature(float newTemp)
        {
            Temperature = newTemp;
            RecalculatePressure();
        }

        public void RecalculatePressure()
        {
            if (Volume > 0)
            {
                Pressure = Moles * AtmosDefines.R * Temperature / Volume;
            }
            else
            {
                Pressure = 0;
            }
        }

        public float GetMoles(string gasId)
        {
            return Gases.TryGetValue(gasId, out float moles) ? moles : 0;
        }

        public void SetMoles(string gasId, float moles)
        {
            if (moles <= 0)
            {
                Gases.Remove(gasId);
            }
            else
            {
                Gases[gasId] = moles;
            }
            RecalculatePressure();
        }

        public void AddGas(string gasId, float moles)
        {
            if (moles <= 0) return;
            float current = GetMoles(gasId);
            Gases[gasId] = current + moles;
            RecalculatePressure();
        }

        public void RemoveGas(string gasId, float moles)
        {
            if (moles <= 0) return;
            float current = GetMoles(gasId);
            float remaining = current - moles;
            if (remaining <= 0)
            {
                Gases.Remove(gasId);
            }
            else
            {
                Gases[gasId] = remaining;
            }
            RecalculatePressure();
        }

        public void Clear()
        {
            Gases.Clear();
            Temperature = 0;
            Pressure = 0;
        }

        public void CopyFrom(GasMix other)
        {
            Gases.Clear();
            foreach (var kvp in other.Gases)
            {
                Gases[kvp.Key] = kvp.Value;
            }
            Volume = other.Volume;
            Temperature = other.Temperature;
            Pressure = other.Pressure;
        }

        public void SetToEmpty()
        {
            Clear();
            Temperature = AtmosDefines.SPACE_TEMPERATURE;
            RecalculatePressure();
        }

        public static void TransferGas(GasMix target, GasMix source, float molesToTransfer, bool blendTemperature = true)
        {
            if (molesToTransfer <= 0 || source.Moles <= 0) return;

            float transferRatio = Math.Min(1.0f, molesToTransfer / source.Moles);
            
            float heatCapacityTransfer = 0;
            
            List<KeyValuePair<string, float>> toTransfer = new List<KeyValuePair<string, float>>();
            foreach (var kvp in source.Gases)
            {
                float amount = kvp.Value * transferRatio;
                toTransfer.Add(new KeyValuePair<string, float>(kvp.Key, amount));
                heatCapacityTransfer += amount * AtmosDefines.GetGasHeatCapacity(kvp.Key);
            }

            float energyTransfer = heatCapacityTransfer * source.Temperature;

            foreach (var kvp in toTransfer)
            {
                source.RemoveGas(kvp.Key, kvp.Value);
                
                // Add directly without recalculating pressure yet
                float current = target.GetMoles(kvp.Key);
                target.Gases[kvp.Key] = current + kvp.Value;
            }

            if (blendTemperature)
            {
                float targetHeatCapacity = target.WholeHeatCapacity;
                if (targetHeatCapacity > 0)
                {
                    float currentEnergy = (targetHeatCapacity - heatCapacityTransfer) * target.Temperature;
                    target.Temperature = (currentEnergy + energyTransfer) / targetHeatCapacity;
                }
            }

            source.RecalculatePressure();
            target.RecalculatePressure();
        }
    }
}
