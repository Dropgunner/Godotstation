using System;

namespace Godotstation.Engine.Atmos
{
	/// <summary>Plasma combustion shared by the simulation and headless contract tests.</summary>
	public sealed class PlasmaFireReaction : IAtmosReaction
	{
		public bool Satisfies(GasMix gasMix) =>
			gasMix.GetMoles(Gas.Plasma) > 0f && gasMix.GetMoles(Gas.Oxygen) > 0f;

		public bool React(ref GasMix gasMix)
		{
			var temperature = gasMix.Temperature;
			if (temperature < AtmosConstants.PlasmaMinimumBurnTemperature || !Satisfies(gasMix)) return false;

			var temperatureScale = MathF.Min(1f,
				(temperature - AtmosConstants.PlasmaMinimumBurnTemperature) /
				(AtmosConstants.PlasmaUpperTemperature - AtmosConstants.PlasmaMinimumBurnTemperature));
			if (temperatureScale <= 0f) return false;

			var plasma = gasMix.GetMoles(Gas.Plasma);
			var oxygen = gasMix.GetMoles(Gas.Oxygen);
			var oxygenBurnRate = AtmosConstants.OxygenBurnRateBase - temperatureScale;
			var plasmaBurnRate = oxygen > plasma * AtmosConstants.PlasmaOxygenFullburn
				? plasma * temperatureScale / AtmosConstants.PlasmaBurnRateDelta
				: temperatureScale * (oxygen / AtmosConstants.PlasmaOxygenFullburn) / AtmosConstants.PlasmaBurnRateDelta;
			if (plasmaBurnRate <= AtmosConstants.MinimumHeatCapacity) return false;

			plasmaBurnRate = MathF.Min(plasmaBurnRate, MathF.Min(plasma, oxygen / oxygenBurnRate));
			if (plasmaBurnRate <= 0f) return false;

			var energyBefore = gasMix.InternalEnergy;
			gasMix.RemoveGas(Gas.Plasma, plasmaBurnRate);
			gasMix.RemoveGas(Gas.Oxygen, plasmaBurnRate * oxygenBurnRate);
			gasMix.AddGas(Gas.CarbonDioxide, plasmaBurnRate);
			gasMix.InternalEnergy = energyBefore + AtmosConstants.FirePlasmaEnergyReleased * plasmaBurnRate;
			return true;
		}
	}
}
