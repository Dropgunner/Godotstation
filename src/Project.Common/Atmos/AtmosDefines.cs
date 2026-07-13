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

        // Canonical gas identifiers. Kept as string ids to match the data-driven
        // gas registry used elsewhere in the port and to avoid an enum churn point.
        public const string GAS_OXYGEN = "Oxygen";
        public const string GAS_NITROGEN = "Nitrogen";
        public const string GAS_PLASMA = "Plasma";
        public const string GAS_CARBON_DIOXIDE = "CarbonDioxide";
        public const string GAS_TRITIUM = "Tritium";
        public const string GAS_WATER_VAPOR = "WaterVapor";
        public const string GAS_HYPER_NOBLIUM = "HyperNoblium";

        // Rule 6 (Reaction Oppression): a tile holding this many moles of HyperNoblium
        // suppresses ALL chemical reactions on that tile, regardless of temperature.
        public const float REACTION_OPPRESSION_MOLES = 5f;

        // Default specific heat capacities for common gases
        private static readonly Dictionary<string, float> SpecificHeats = new Dictionary<string, float>
        {
            { GAS_OXYGEN, 20f },
            { GAS_NITROGEN, 20f },
            { GAS_PLASMA, 200f },
            { GAS_CARBON_DIOXIDE, 30f },
            { GAS_TRITIUM, 10f },
            { GAS_WATER_VAPOR, 40f },
            { GAS_HYPER_NOBLIUM, 2000f }
        };

        public static float GetGasHeatCapacity(string gasId)
        {
            return SpecificHeats.TryGetValue(gasId, out float heat) ? heat : 20f;
        }
    }
}
