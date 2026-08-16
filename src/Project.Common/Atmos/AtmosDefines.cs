using System.Collections.Generic;

namespace Project.Common.Atmos
{
    public static class AtmosDefines
    {
        public const float MINIMUM_MOLE_COUNT = 0.01f;
        public const float FIRE_MINIMUM_TEMPERATURE_TO_EXIST = 373.15f;

        public const float MINIMUM_TEMPERATURE_DELTA_TO_CONSIDER = 0.5f;
        public const float MINIMUM_TEMPERATURE_FOR_SUPERCONDUCTION = 373.15f;
        public const float MINIMUM_TEMPERATURE_START_SUPERCONDUCTION = 693.15f;

        public const float ONE_ATMOSPHERE = 101.325f;
        public const float CELL_VOLUME = 2500f;

        public const float SPACE_TEMPERATURE = 2.7f;
        public const float SPACE_HEAT_CAPACITY = 700000f;
        public const float SPACE_THERMAL_CONDUCTIVITY = 0.4f;

        public const float R = 8.314f; // Ideal gas constant

        public const float MIN_PRESSURE_DIFFERENCE = 0.1f;
        public const float MINIMUM_HEAT_CAPACITY = 0.0003f;

        // Default specific heat capacities for common gases
        private static readonly Dictionary<string, float> SpecificHeats = new Dictionary<string, float>
        {
            { "Oxygen", 20f },
            { "Nitrogen", 20f },
            { "Plasma", 200f },
            { "CarbonDioxide", 30f },
            { "Tritium", 10f },
            { "WaterVapor", 40f }
        };

        public static float GetGasHeatCapacity(string gasId)
        {
            return SpecificHeats.TryGetValue(gasId, out float heat) ? heat : 20f;
        }
    }
}
