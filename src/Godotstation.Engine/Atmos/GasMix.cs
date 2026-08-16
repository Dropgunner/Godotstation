using System;
using System.Runtime.CompilerServices;

namespace Godotstation.Engine.Atmos
{
	/// <summary>
	/// The gases the simulation tracks. Indices are wire format and save
	/// format — append only, never reorder.
	/// </summary>
	public enum Gas
	{
		Oxygen = 0,
		Nitrogen = 1,
		CarbonDioxide = 2,
		Plasma = 3,
		NitrousOxide = 4,
		Hydrogen = 5,
		WaterVapour = 6,
		Miasma = 7
	}

	/// <summary>
	/// Fixed-size inline mole storage. The Unity original kept gas amounts in
	/// a heap-allocated GasData with a list of GasValues objects — one
	/// allocation per gas per tile, chased through two pointers per read, and
	/// shared by reference so mixes aliased each other in subtle ways. A tile
	/// station runs ~50k of these. Eight floats inline gives value semantics
	/// (assignment copies, no aliasing bugs possible), zero GC pressure, and
	/// the whole mix in a single cache line.
	/// </summary>
	[InlineArray(GasMix.GasCount)]
	public struct GasMoles
	{
		private float element0;
	}

	/// <summary>
	/// A quantity of gas: per-gas moles, temperature and volume. Pure data,
	/// pure value type — port of Unitystation's GasMix minus the reference
	/// semantics. Pressure is derived (ideal gas law), never stored, so it
	/// can never go stale.
	/// </summary>
	public struct GasMix
	{
		public const int GasCount = 8;

		/// <summary>J/(mol·K). Must match Unitystation's Gas.R to the last digit — pressure parity depends on it.</summary>
		public const float GasConstant = 8.3144626f;

		/// <summary>m³ of one station tile.</summary>
		public const float TileVolume = 2.5f;

		/// <summary>Kelvin. Cosmic microwave background — the temperature of space.</summary>
		public const float SpaceTemperature = 2.7f;

		/// <summary>kPa at station standard.</summary>
		public const float StandardPressure = 101.325f;

		/// <summary>20°C in Kelvin, station standard.</summary>
		public const float StandardTemperature = 293.15f;

		/// <summary>
		/// Molar heat capacity per gas, J/(mol·K), indexed by Gas. Plasma's
		/// absurd 200 is load-bearing: it is why plasma floods soak up heat
		/// and why plasma fires sustain themselves.
		/// </summary>
		private static readonly float[] MolarHeatCapacities =
		{
			20f,  // Oxygen
			20f,  // Nitrogen
			30f,  // CarbonDioxide
			200f, // Plasma
			40f,  // NitrousOxide
			15f,  // Hydrogen
			40f,  // WaterVapour
			20f   // Miasma
		};

		public GasMoles Moles;
		public float Temperature;
		public float Volume;

		public GasMix(float volume, float temperature)
		{
			Moles = default;
			Volume = volume;
			Temperature = temperature;
		}

		public float GetMoles(Gas gas) => Moles[(int)gas];

		public void SetMoles(Gas gas, float moles) => Moles[(int)gas] = MathF.Max(0f, moles);

		public void AddGas(Gas gas, float moles) => Moles[(int)gas] += moles;

		/// <summary>Remove up to <paramref name="moles"/> of a gas; clamps at zero like the original (no negative moles, ever).</summary>
		public void RemoveGas(Gas gas, float moles) => Moles[(int)gas] = MathF.Max(0f, Moles[(int)gas] - moles);

		public float TotalMoles
		{
			get
			{
				float total = 0f;
				for (int i = 0; i < GasCount; i++)
				{
					total += Moles[i];
				}

				return total;
			}
		}

		/// <summary>
		/// kPa via ideal gas law: P = nRT/V (Pa), /1000 for kPa. Same formula,
		/// same guard as GasMix.CalcPressure upstream.
		/// </summary>
		public float Pressure
		{
			get
			{
				if (Volume <= 0f)
				{
					return 0f;
				}

				return TotalMoles * GasConstant * Temperature / Volume / 1000f;
			}
		}

		/// <summary>Σ nᵢ·cᵢ — J/K of the whole mix.</summary>
		public float WholeHeatCapacity
		{
			get
			{
				float capacity = 0f;
				for (int i = 0; i < GasCount; i++)
				{
					capacity += Moles[i] * MolarHeatCapacities[i];
				}

				return capacity;
			}
		}

		/// <summary>
		/// Thermal energy in Joules. Setting it back-solves temperature —
		/// this is how energy-conserving heat exchange is written without
		/// ever mentioning temperature directly.
		/// </summary>
		public float InternalEnergy
		{
			get => WholeHeatCapacity * Temperature;
			set
			{
				var capacity = WholeHeatCapacity;
				if (capacity > 0f)
				{
					Temperature = value / capacity;
				}
			}
		}

		public static float MolarHeatCapacityOf(Gas gas) => MolarHeatCapacities[(int)gas];

		/// <summary>Hard vacuum at 2.7 K.</summary>
		public static GasMix Space(float volume = TileVolume)
		{
			return new GasMix(volume, SpaceTemperature);
		}

		/// <summary>
		/// Station-standard air: 101.325 kPa at 293.15 K, 21/79 O₂/N₂. Mole
		/// count is derived from the ideal gas law rather than hardcoded so
		/// it is exactly consistent with Pressure.
		/// </summary>
		public static GasMix StandardAtmosphere(float volume = TileVolume)
		{
			var mix = new GasMix(volume, StandardTemperature);
			float totalMoles = StandardPressure * 1000f * volume / (GasConstant * StandardTemperature);
			mix.SetMoles(Gas.Oxygen, totalMoles * 0.21f);
			mix.SetMoles(Gas.Nitrogen, totalMoles * 0.79f);
			return mix;
		}

		/// <summary>Scale every gas by a factor (vent losses, splitting).</summary>
		public void MultiplyGases(float factor)
		{
			for (int i = 0; i < GasCount; i++)
			{
				Moles[i] *= factor;
			}
		}

		public override string ToString()
		{
			return $"{Pressure:0.##} kPa, {Temperature:0.##} K, {TotalMoles:0.##} mol in {Volume} m³";
		}
	}
}
