using System;
using System.Collections.Generic;

namespace Godotstation.Engine.Atmos
{
	/// <summary>
	/// Tuning constants for the atmospherics pass. Values trace back to
	/// Unitystation's AtmosConstants / Reactions (themselves inherited from
	/// /tg/station). These numbers ARE the game design — a plasma fire that
	/// burns 2% hotter is a different game.
	/// </summary>
	public static class AtmosConstants
	{
		/// <summary>kPa. Below this delta, neighboring tiles are "equal enough" and the wave stops.</summary>
		public const float MinPressureDifference = 0.005f;

		/// <summary>K. Below this delta no conduction occurs (kills infinite heat-shuffling).</summary>
		public const float MinTemperatureDifference = 0.01f;

		/// <summary>J/K. Mixes with less heat capacity than this can't meaningfully hold energy.</summary>
		public const float MinimumHeatCapacity = 0.0003f;

		// ---- Plasma fire (tg-lineage constants Unitystation inherited) ----
		public const float PlasmaMinimumBurnTemperature = 373.15f;   // 100°C
		public const float PlasmaUpperTemperature = 1643.15f;        // 1370°C — full burn rate
		public const float PlasmaOxygenFullburn = 10f;               // O₂:plasma ratio for unthrottled burn
		public const float PlasmaBurnRateDelta = 9f;
		public const float OxygenBurnRateBase = 1.4f;
		public const float FirePlasmaEnergyReleased = 3_000_000f;    // J per mole of plasma burnt

		/// <summary>K. Water vapour freezes to ice overlays below this.</summary>
		public const float WaterFreezingTemperature = 273.15f;

		/// <summary>Minimum water vapour moles before ice is rendered.</summary>
		public const float IceMinimumMoles = 0.01f;
	}

	/// <summary>
	/// Facade for everything the player sees but the simulation must not
	/// touch: gas overlays, fire sprites, ice. Called by the sim, implemented
	/// by the Godot layer. A null implementation must be legal — the
	/// simulation's math cannot depend on whether anyone is watching.
	/// </summary>
	public interface IAtmosVisualService
	{
		void UpdateGasOverlays(IAtmosNode node, in GasMix mix);
		void SetFire(IAtmosNode node, bool burning);
		void SetIce(IAtmosNode node, bool frozen);
	}

	/// <summary>
	/// Queue that silently drops re-enqueues of an element already waiting.
	/// Port of Unitystation's UniqueQueue: the atmos wavefront touches each
	/// dirty tile once per pass no matter how many neighbors dirtied it.
	/// (The original is lock-based for its background thread; Godotstation
	/// runs atmos on the deterministic main tick, so no locks.)
	/// </summary>
	public sealed class UniqueQueue<T> where T : class
	{
		private readonly Queue<T> queue = new Queue<T>();
		private readonly HashSet<T> contents = new HashSet<T>();

		public int Count => queue.Count;

		public bool Enqueue(T element)
		{
			if (contents.Add(element) == false)
			{
				return false;
			}

			queue.Enqueue(element);
			return true;
		}

		public bool TryDequeue(out T element)
		{
			if (queue.TryDequeue(out element!) == false)
			{
				return false;
			}

			contents.Remove(element);
			return true;
		}
	}

	/// <summary>
	/// Port of Unitystation's AtmosSimulation
	/// (US13.Tilemaps.Behaviours.Meta.Atmospherics, 586 lines).
	///
	/// The architecture is the interesting part and is preserved exactly:
	/// atmos is NOT a full-grid cellular automaton. It is a dirty-queue
	/// wavefront — only tiles whose state recently changed are visited, and a
	/// visit re-enqueues its neighbors only if it actually changed something.
	/// A sealed, settled station costs zero atmos CPU; a hull breach grows a
	/// processing front that follows the physical pressure wave. Break the
	/// dirty-queue discipline and a large map melts the tick budget.
	///
	/// Determinism: nodes are processed in dequeue order, neighbors in
	/// AddNeighborsToList order, gases in enum order. No randomness, no
	/// threads, float32 throughout. Same queue state in, same gas state out.
	/// </summary>
	public sealed class AtmosSimulation
	{
		public int UpdateListCount => updateList.Count;

		private readonly List<(IAtmosNode node, bool doEqualise)> nodes = new List<(IAtmosNode, bool)>(5);
		private readonly UniqueQueue<IAtmosNode> updateList = new UniqueQueue<IAtmosNode>();
		private readonly IAtmosVisualService? visuals;

		public AtmosSimulation(IAtmosVisualService? visuals = null)
		{
			this.visuals = visuals;
		}

		/// <summary>Mark a tile dirty (gas changed, door opened, wall destroyed...).</summary>
		public void RequestUpdate(IAtmosNode node)
		{
			updateList.Enqueue(node);
		}

		/// <summary>
		/// One atmos pass. Snapshotting Count before the loop is upstream's
		/// trick and it is load-bearing: nodes re-enqueued during this pass
		/// (the propagating wave) run NEXT pass, so one Run() call is bounded
		/// work and a pressure wave advances one ring of tiles per pass —
		/// that propagation speed is part of the game's feel.
		/// </summary>
		public void Run()
		{
			int count = updateList.Count;
			for (int i = 0; i < count; i++)
			{
				if (updateList.TryDequeue(out IAtmosNode node))
				{
					if (node.Exists == false) continue;
					if (node.IsSetUp == false) continue;
					Update(node);
				}
			}
		}

		private void Update(IAtmosNode node)
		{
			nodes.Clear();
			var doEqualise = node.IsOccupied == false && node.IsIsolatedNode == false;
			nodes.Add((node, doEqualise));
			node.AddNeighborsToList(nodes);

			if (doEqualise && NeedsEqualisation(node))
			{
				Equalise();
				EnqueueParticipantsAndNeighbors();
			}

			ThermalConduction(node);
			React(node);
			UpdateVisuals(node);
		}

		/// <summary>
		/// Change detection — the guard that lets the wavefront die out.
		/// A tile equalises only when some equalisable neighbor differs in
		/// pressure or temperature beyond the minimum deltas.
		/// </summary>
		private bool NeedsEqualisation(IAtmosNode node)
		{
			ref var mix = ref node.GasMix;

			for (int i = 1; i < nodes.Count; i++) // 0 is the node itself
			{
				var (neighbor, neighborEqualises) = nodes[i];
				if (neighborEqualises == false)
				{
					continue;
				}

				ref var other = ref neighbor.GasMix;
				if (MathF.Abs(mix.Pressure - other.Pressure) > AtmosConstants.MinPressureDifference)
				{
					return true;
				}

				if (MathF.Abs(mix.Temperature - other.Temperature) > AtmosConstants.MinTemperatureDifference)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Pressure equalisation across the node + eligible neighbors:
		/// combine everything into one virtual volume, then redistribute by
		/// volume share at a common temperature (energy-conserving mean, the
		/// GasMixes.NewGasMix approach upstream).
		///
		/// Space tiles participate with their volume but contribute nothing
		/// and are re-voided afterwards — gas that flows into space is
		/// destroyed. One space neighbor therefore drains a tile
		/// geometrically (half, quarter, eighth... with one neighbor), never
		/// instantly. That decay curve is the "air hisses out" feel of a hull
		/// breach and is pinned by test.
		/// </summary>
		private void Equalise()
		{
			GasMoles totalMoles = default;
			float totalEnergy = 0f;
			float totalVolume = 0f;
			int participants = 0;

			for (int i = 0; i < nodes.Count; i++)
			{
				var (node, doEqualise) = nodes[i];
				if (doEqualise == false)
				{
					continue;
				}

				participants++;
				ref var mix = ref node.GasMix;
				totalVolume += mix.Volume;

				if (node.IsSpace)
				{
					continue; // vacuum contributes volume, no gas, no energy
				}

				totalEnergy += mix.InternalEnergy;
				for (int g = 0; g < GasMix.GasCount; g++)
				{
					totalMoles[g] += mix.Moles[g];
				}
			}

			if (participants < 2 || totalVolume <= 0f)
			{
				return;
			}

			// Common temperature of the combined mix.
			float combinedCapacity = 0f;
			for (int g = 0; g < GasMix.GasCount; g++)
			{
				combinedCapacity += totalMoles[g] * GasMix.MolarHeatCapacityOf((Gas)g);
			}

			float commonTemperature = combinedCapacity > AtmosConstants.MinimumHeatCapacity
				? totalEnergy / combinedCapacity
				: GasMix.SpaceTemperature;

			for (int i = 0; i < nodes.Count; i++)
			{
				var (node, doEqualise) = nodes[i];
				if (doEqualise == false)
				{
					continue;
				}

				ref var mix = ref node.GasMix;

				if (node.IsSpace)
				{
					// Whatever flowed here is gone.
					mix = GasMix.Space(mix.Volume);
					continue;
				}

				float share = mix.Volume / totalVolume;
				for (int g = 0; g < GasMix.GasCount; g++)
				{
					mix.Moles[g] = totalMoles[g] * share;
				}

				mix.Temperature = commonTemperature;
			}
		}

		private void EnqueueParticipantsAndNeighbors()
		{
			for (int i = 0; i < nodes.Count; i++)
			{
				var (node, doEqualise) = nodes[i];
				if (doEqualise && node.IsSpace == false)
				{
					updateList.Enqueue(node);
				}
			}
		}

		/// <summary>
		/// Solid↔gas heat exchange on one tile, energy-conserving via the
		/// reduced heat capacity (the same series-capacitor form upstream
		/// uses): both sides move toward equilibrium, neither past it,
		/// regardless of how mismatched their capacities are. A cold wall
		/// chills the room; a plasma fire heats the plating until it glows.
		/// </summary>
		private void ThermalConduction(IAtmosNode node)
		{
			if (node.IsSpace || node.SolidHeatCapacity <= AtmosConstants.MinimumHeatCapacity)
			{
				return;
			}

			ref var mix = ref node.GasMix;
			var gasCapacity = mix.WholeHeatCapacity;
			if (gasCapacity <= AtmosConstants.MinimumHeatCapacity)
			{
				return;
			}

			var deltaT = node.SolidTemperature - mix.Temperature;
			if (MathF.Abs(deltaT) <= AtmosConstants.MinTemperatureDifference)
			{
				return;
			}

			var reducedCapacity = gasCapacity * node.SolidHeatCapacity / (gasCapacity + node.SolidHeatCapacity);
			var energy = node.ThermalConductivity * deltaT * reducedCapacity;

			node.SolidTemperature -= energy / node.SolidHeatCapacity;
			mix.InternalEnergy += energy;

			updateList.Enqueue(node); // temperature moved: neighbors may care next pass
		}

		/// <summary>
		/// Gas reactions. Only plasma fire is ported in B1 — it is the
		/// reaction that defines atmos gameplay; the rest follow the same
		/// pattern. Branch structure and constants follow the tg-lineage
		/// PlasmaFireReaction Unitystation uses.
		/// </summary>
		private void React(IAtmosNode node)
		{
			ref var mix = ref node.GasMix;

			var plasma = mix.GetMoles(Gas.Plasma);
			var oxygen = mix.GetMoles(Gas.Oxygen);

			bool burning = false;
			if (plasma > 0f && oxygen > 0f && mix.Temperature > AtmosConstants.PlasmaMinimumBurnTemperature)
			{
				// 0..1 throttle: barely-ignited fires smoulder, white-hot fires rage.
				var temperatureScale = MathF.Min(1f,
					(mix.Temperature - AtmosConstants.PlasmaMinimumBurnTemperature)
					/ (AtmosConstants.PlasmaUpperTemperature - AtmosConstants.PlasmaMinimumBurnTemperature));

				if (temperatureScale > 0f)
				{
					var oxygenBurnRate = AtmosConstants.OxygenBurnRateBase - temperatureScale;

					// Oxygen-rich burns are limited by plasma; oxygen-starved
					// burns are throttled by the O₂ ratio. This asymmetry is
					// why venting a burning room actually works.
					float plasmaBurnRate;
					if (oxygen > plasma * AtmosConstants.PlasmaOxygenFullburn)
					{
						plasmaBurnRate = plasma * temperatureScale / AtmosConstants.PlasmaBurnRateDelta;
					}
					else
					{
						plasmaBurnRate = temperatureScale * (oxygen / AtmosConstants.PlasmaOxygenFullburn)
							/ AtmosConstants.PlasmaBurnRateDelta;
					}

					if (plasmaBurnRate > AtmosConstants.MinimumHeatCapacity)
					{
						plasmaBurnRate = MathF.Min(plasmaBurnRate, MathF.Min(plasma, oxygen / oxygenBurnRate));

						var energyBefore = mix.InternalEnergy;
						mix.RemoveGas(Gas.Plasma, plasmaBurnRate);
						mix.RemoveGas(Gas.Oxygen, plasmaBurnRate * oxygenBurnRate);
						mix.AddGas(Gas.CarbonDioxide, plasmaBurnRate);

						var energyReleased = AtmosConstants.FirePlasmaEnergyReleased * plasmaBurnRate;
						mix.InternalEnergy = energyBefore + energyReleased;

						burning = true;
						updateList.Enqueue(node); // fire keeps its own tile hot in the queue
					}
				}
			}

			visuals?.SetFire(node, burning);
		}

		private void UpdateVisuals(IAtmosNode node)
		{
			if (visuals == null)
			{
				return;
			}

			ref var mix = ref node.GasMix;
			visuals.UpdateGasOverlays(node, in mix);
			visuals.SetIce(node,
				mix.Temperature < AtmosConstants.WaterFreezingTemperature
				&& mix.GetMoles(Gas.WaterVapour) > AtmosConstants.IceMinimumMoles);
		}
	}
}
