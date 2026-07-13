using System;

namespace Project.Common.Atmos
{
    /// <summary>
    /// The canonical SS13 plasma fire. Port of Unitystation's <c>PlasmaFireReaction</c>.
    ///
    /// Plasma burns in the presence of oxygen once the mix reaches
    /// <see cref="AtmosDefines.FIRE_MINIMUM_TEMPERATURE_TO_EXIST"/> (373.15 K). The burn
    /// rate scales linearly with temperature up to <see cref="PlasmaUpperTemperature"/>,
    /// consumes oxygen at a temperature-dependent ratio, produces carbon dioxide, and
    /// dumps a large amount of energy back into the mix — which raises the temperature and
    /// makes the reaction self-sustaining until a reactant is exhausted.
    ///
    /// All arithmetic uses <see cref="System.Math"/>; there is no engine dependency.
    /// </summary>
    public sealed class PlasmaFireReaction : IAtmosReaction
    {
        // Upper bound of the burn-rate ramp: 1370 C expressed in Kelvin.
        public const float PlasmaUpperTemperature = 1370f + 273.15f;

        // Oxygen:plasma mole ratio above which plasma (rather than oxygen) limits the burn.
        private const float PlasmaOxygenFullburn = 10f;

        // Divisor that tempers how much of the available fuel burns per tick.
        private const float PlasmaBurnRateDelta = 9f;

        // Base oxygen-per-plasma consumption ratio; reduced as the fire gets hotter.
        private const float OxygenBurnRateBase = 1.4f;

        // Energy released per mole of plasma burned (Joules-equivalent).
        public const float FirePlasmaEnergyReleased = 3_000_000f;

        public bool Satisfies(GasMix gasMix)
        {
            return gasMix.GetMoles(AtmosDefines.GAS_PLASMA) > 0f
                && gasMix.GetMoles(AtmosDefines.GAS_OXYGEN) > 0f;
        }

        public ReactionResult React(GasMix gasMix, AtmosTile tile)
        {
            float temperature = gasMix.Temperature;
            if (temperature < AtmosDefines.FIRE_MINIMUM_TEMPERATURE_TO_EXIST)
            {
                return ReactionResult.NoReaction;
            }

            // Linear ramp of burn intensity from the ignition temperature up to the cap.
            float temperatureScale;
            if (temperature > PlasmaUpperTemperature)
            {
                temperatureScale = 1f;
            }
            else
            {
                temperatureScale =
                    (temperature - AtmosDefines.FIRE_MINIMUM_TEMPERATURE_TO_EXIST) /
                    (PlasmaUpperTemperature - AtmosDefines.FIRE_MINIMUM_TEMPERATURE_TO_EXIST);
            }

            if (temperatureScale <= 0f)
            {
                return ReactionResult.NoReaction;
            }

            float plasma = gasMix.GetMoles(AtmosDefines.GAS_PLASMA);
            float oxygen = gasMix.GetMoles(AtmosDefines.GAS_OXYGEN);

            // Hotter fires burn proportionally less oxygen per unit of plasma.
            float oxygenBurnRate = OxygenBurnRateBase - temperatureScale;

            float plasmaBurnRate;
            if (oxygen > plasma * PlasmaOxygenFullburn)
            {
                // Oxygen-rich: plasma is the limiting reactant.
                plasmaBurnRate = plasma * temperatureScale / PlasmaBurnRateDelta;
            }
            else
            {
                // Oxygen-limited: throttle by available oxygen.
                plasmaBurnRate = temperatureScale * (oxygen / PlasmaOxygenFullburn) / PlasmaBurnRateDelta;
            }

            if (plasmaBurnRate <= AtmosDefines.MINIMUM_HEAT_CAPACITY)
            {
                return ReactionResult.NoReaction;
            }

            // Never consume more than what is present of either reactant.
            plasmaBurnRate = Math.Min(plasmaBurnRate, Math.Min(plasma, oxygen / oxygenBurnRate));
            if (plasmaBurnRate <= 0f)
            {
                return ReactionResult.NoReaction;
            }

            float oldHeatCapacity = gasMix.WholeHeatCapacity;

            gasMix.RemoveGas(AtmosDefines.GAS_PLASMA, plasmaBurnRate);
            gasMix.RemoveGas(AtmosDefines.GAS_OXYGEN, plasmaBurnRate * oxygenBurnRate);
            gasMix.AddGas(AtmosDefines.GAS_CARBON_DIOXIDE, plasmaBurnRate);

            float energyReleased = FirePlasmaEnergyReleased * plasmaBurnRate;

            // Blend the released energy into the (now recomposed) mix by heat capacity so
            // temperature tracks the ideal-gas energy balance rather than a linear average.
            float newHeatCapacity = gasMix.WholeHeatCapacity;
            if (newHeatCapacity > AtmosDefines.MINIMUM_HEAT_CAPACITY)
            {
                gasMix.SetTemperature(
                    (temperature * oldHeatCapacity + energyReleased) / newHeatCapacity);
            }

            return ReactionResult.Reacted;
        }
    }
}
