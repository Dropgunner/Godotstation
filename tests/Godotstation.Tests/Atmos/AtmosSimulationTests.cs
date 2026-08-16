using System;
using System.Collections.Generic;
using Godotstation.Engine.Atmos;
using Godotstation.Engine.Physics;
using Xunit;

namespace Godotstation.Tests.Atmos
{
	/// <summary>
	/// Invariant tests (conservation, thresholds, queue discipline) plus the
	/// Golden Trace parity structure for atmos. Atmos parity is defined on
	/// aggregate state, not frames: after N Run() passes on an identical
	/// starting grid, every tile's (pressure, temperature, per-gas moles)
	/// must match the Unity capture within tolerance.
	/// </summary>
	public class AtmosSimulationTests
	{
		// =====================================================================
		// Test double
		// =====================================================================

		private sealed class FakeNode : IAtmosNode
		{
			private GasMix mix;
			public readonly List<FakeNode> Linked = new List<FakeNode>();

			public FakeNode(GasMix initial)
			{
				mix = initial;
			}

			public bool Exists { get; set; } = true;
			public bool IsSetUp { get; set; } = true;
			public bool IsOccupied { get; set; }
			public bool IsIsolatedNode { get; set; }
			public bool IsSpace { get; set; }
			public Vector3Int Position { get; set; }
			public ref GasMix GasMix => ref mix;
			public float SolidTemperature { get; set; } = GasMix.StandardTemperature;
			public float SolidHeatCapacity { get; set; } = 0f; // default: no solid exchange in tests
			public float ThermalConductivity { get; set; } = 0.05f;

			public void AddNeighborsToList(List<(IAtmosNode node, bool doEqualise)> nodes)
			{
				foreach (var neighbor in Linked)
				{
					nodes.Add((neighbor, neighbor.IsOccupied == false && neighbor.IsIsolatedNode == false));
				}
			}

			public IReadOnlyList<IAtmosNode> Neighbors => Linked;

			public static void Link(FakeNode a, FakeNode b)
			{
				a.Linked.Add(b);
				b.Linked.Add(a);
			}
		}

		private sealed class RecordingVisuals : IAtmosVisualService
		{
			public readonly HashSet<IAtmosNode> OnFire = new HashSet<IAtmosNode>();
			public readonly HashSet<IAtmosNode> Iced = new HashSet<IAtmosNode>();
			public int OverlayUpdates;

			public void UpdateGasOverlays(IAtmosNode node, in GasMix mix) => OverlayUpdates++;

			public void SetFire(IAtmosNode node, bool burning)
			{
				if (burning) OnFire.Add(node);
				else OnFire.Remove(node);
			}

			public void SetIce(IAtmosNode node, bool frozen)
			{
				if (frozen) Iced.Add(node);
				else Iced.Remove(node);
			}
		}

		// =====================================================================
		// GasMix fundamentals
		// =====================================================================

		[Fact]
		public void StandardAtmosphere_IsExactlyStandardPressure()
		{
			var mix = GasMix.StandardAtmosphere();

			// Moles derived from P = nRT/V must round-trip to 101.325 kPa.
			Assert.Equal(101.325f, mix.Pressure, 2);
			Assert.Equal(293.15f, mix.Temperature, 3);
		}

		[Fact]
		public void GasMix_IsValueType_CopiesDoNotAlias()
		{
			var a = GasMix.StandardAtmosphere();
			var b = a; // struct copy — this line is the whole point of the port
			b.SetMoles(Gas.Oxygen, 0f);

			Assert.True(a.GetMoles(Gas.Oxygen) > 0f,
				"mutating a copy must never affect the original (the Unity GasData aliasing bug class)");
		}

		[Fact]
		public void InternalEnergy_RoundTripsThroughTemperature()
		{
			var mix = GasMix.StandardAtmosphere();
			var energy = mix.InternalEnergy;

			mix.InternalEnergy = energy * 2f;

			Assert.Equal(GasMix.StandardTemperature * 2f, mix.Temperature, 1);
		}

		// =====================================================================
		// Queue discipline
		// =====================================================================

		[Fact]
		public void UniqueQueue_DropsDuplicateEnqueues()
		{
			var queue = new UniqueQueue<FakeNode>();
			var node = new FakeNode(GasMix.StandardAtmosphere());

			Assert.True(queue.Enqueue(node));
			Assert.False(queue.Enqueue(node));
			Assert.Equal(1, queue.Count);

			Assert.True(queue.TryDequeue(out _));
			Assert.True(queue.Enqueue(node)); // re-eligible once dequeued
		}

		[Fact]
		public void Run_SkipsDestroyedAndUninitialisedNodes()
		{
			var sim = new AtmosSimulation();
			var destroyed = new FakeNode(GasMix.StandardAtmosphere()) { Exists = false };
			var notReady = new FakeNode(GasMix.StandardAtmosphere()) { IsSetUp = false };
			var space = new FakeNode(GasMix.Space()) { IsSpace = true };
			FakeNode.Link(destroyed, space);
			FakeNode.Link(notReady, space);

			sim.RequestUpdate(destroyed);
			sim.RequestUpdate(notReady);
			sim.Run();

			// Neither vented: they were skipped before Update().
			Assert.True(destroyed.GasMix.TotalMoles > 0f);
			Assert.True(notReady.GasMix.TotalMoles > 0f);
		}

		[Fact]
		public void Run_ProcessesOnlyCurrentWavefront_ReenqueuesForNextPass()
		{
			// A pressurised node next to a vacuum node next to another vacuum:
			// one Run() must equalise the first pair and QUEUE the spread, not
			// chase the whole wave in a single pass.
			var a = new FakeNode(GasMix.StandardAtmosphere());
			var b = new FakeNode(new GasMix(GasMix.TileVolume, GasMix.StandardTemperature));
			var c = new FakeNode(new GasMix(GasMix.TileVolume, GasMix.StandardTemperature));
			FakeNode.Link(a, b);
			FakeNode.Link(b, c);

			var sim = new AtmosSimulation();
			sim.RequestUpdate(a);
			sim.Run();

			Assert.True(b.GasMix.TotalMoles > 0f, "first ring equalised");
			Assert.Equal(0f, c.GasMix.TotalMoles, 3); // second ring untouched this pass
			Assert.True(sim.UpdateListCount > 0, "wavefront queued for next pass");
		}

		// =====================================================================
		// Equalisation
		// =====================================================================

		[Fact]
		public void Equalisation_ConservesMoles_AndReachesEqualPressure()
		{
			var high = new FakeNode(GasMix.StandardAtmosphere());
			var low = new FakeNode(new GasMix(GasMix.TileVolume, GasMix.StandardTemperature));
			FakeNode.Link(high, low);

			float molesBefore = high.GasMix.TotalMoles + low.GasMix.TotalMoles;

			var sim = new AtmosSimulation();
			sim.RequestUpdate(high);
			sim.Run();

			float molesAfter = high.GasMix.TotalMoles + low.GasMix.TotalMoles;
			Assert.Equal(molesBefore, molesAfter, 2); // nothing created or destroyed
			Assert.Equal(high.GasMix.Pressure, low.GasMix.Pressure, 2);
		}

		[Fact]
		public void Equalisation_MixesGasComposition_NotJustPressure()
		{
			var oxygenRoom = new FakeNode(GasMix.StandardAtmosphere());
			var plasmaMix = new GasMix(GasMix.TileVolume, GasMix.StandardTemperature);
			plasmaMix.SetMoles(Gas.Plasma, 50f);
			var plasmaRoom = new FakeNode(plasmaMix);
			FakeNode.Link(oxygenRoom, plasmaRoom);

			var sim = new AtmosSimulation();
			sim.RequestUpdate(oxygenRoom);
			sim.Run();

			// Both tiles now hold both gases (equal volumes → equal split).
			Assert.True(oxygenRoom.GasMix.GetMoles(Gas.Plasma) > 0f);
			Assert.True(plasmaRoom.GasMix.GetMoles(Gas.Oxygen) > 0f);
			Assert.Equal(oxygenRoom.GasMix.GetMoles(Gas.Plasma), plasmaRoom.GasMix.GetMoles(Gas.Plasma), 3);
		}

		[Fact]
		public void OccupiedNode_NeverEqualises()
		{
			var wall = new FakeNode(GasMix.StandardAtmosphere()) { IsOccupied = true };
			var vacuum = new FakeNode(new GasMix(GasMix.TileVolume, GasMix.StandardTemperature));
			FakeNode.Link(wall, vacuum);

			var sim = new AtmosSimulation();
			sim.RequestUpdate(wall);
			sim.Run();

			// Airtight means airtight.
			Assert.Equal(101.325f, wall.GasMix.Pressure, 2);
			Assert.Equal(0f, vacuum.GasMix.TotalMoles, 3);
		}

		[Fact]
		public void SpaceNeighbor_DrainsTileGeometrically_NotInstantly()
		{
			var tile = new FakeNode(GasMix.StandardAtmosphere());
			var space = new FakeNode(GasMix.Space()) { IsSpace = true };
			FakeNode.Link(tile, space);

			float startMoles = tile.GasMix.TotalMoles;

			var sim = new AtmosSimulation();
			sim.RequestUpdate(tile);
			sim.Run();

			// One pass, one equal-volume space neighbor: exactly half remains.
			// The hiss of a hull breach is this curve.
			Assert.Equal(startMoles * 0.5f, tile.GasMix.TotalMoles, 2);
			Assert.Equal(0f, space.GasMix.TotalMoles, 3); // space stays empty

			// And it keeps draining on subsequent passes until below threshold.
			for (int i = 0; i < 50; i++)
			{
				sim.Run();
			}

			Assert.True(tile.GasMix.Pressure < 1f, $"tile should be near-vacuum, was {tile.GasMix.Pressure} kPa");
		}

		// =====================================================================
		// Thermal conduction
		// =====================================================================

		[Fact]
		public void ThermalConduction_SolidAndGas_MoveTowardEquilibrium_ConservingEnergy()
		{
			var node = new FakeNode(GasMix.StandardAtmosphere())
			{
				SolidHeatCapacity = 10000f,
				SolidTemperature = 500f, // hot plating
				ThermalConductivity = 0.5f
			};
			// isolated node: no equalisation partners
			float gasBefore = node.GasMix.Temperature;
			float energyBefore = node.GasMix.InternalEnergy + node.SolidHeatCapacity * node.SolidTemperature;

			var sim = new AtmosSimulation();
			sim.RequestUpdate(node);
			sim.Run();

			Assert.True(node.GasMix.Temperature > gasBefore, "gas heats up from hot plating");
			Assert.True(node.SolidTemperature < 500f, "plating cools down");

			float energyAfter = node.GasMix.InternalEnergy + node.SolidHeatCapacity * node.SolidTemperature;
			Assert.Equal(1f, energyBefore / energyAfter, 3); // conserved within float noise
		}

		// =====================================================================
		// Reactions: plasma fire
		// =====================================================================

		private static FakeNode MakePlasmaOxygenNode(float temperature)
		{
			var mix = new GasMix(GasMix.TileVolume, temperature);
			mix.SetMoles(Gas.Plasma, 20f);
			mix.SetMoles(Gas.Oxygen, 20f);
			return new FakeNode(mix);
		}

		[Fact]
		public void PlasmaFire_BelowIgnitionTemperature_DoesNothing()
		{
			var node = MakePlasmaOxygenNode(300f); // room temp, below 373.15 K
			var visuals = new RecordingVisuals();
			var sim = new AtmosSimulation(visuals);

			sim.RequestUpdate(node);
			sim.Run();

			Assert.Equal(20f, node.GasMix.GetMoles(Gas.Plasma), 3);
			Assert.Empty(visuals.OnFire);
		}

		[Fact]
		public void PlasmaFire_AboveIgnition_ConsumesReactants_ProducesCO2_ReleasesHeat()
		{
			var node = MakePlasmaOxygenNode(500f);
			var visuals = new RecordingVisuals();
			var sim = new AtmosSimulation(visuals);
			float temperatureBefore = node.GasMix.Temperature;

			sim.RequestUpdate(node);
			sim.Run();

			Assert.True(node.GasMix.GetMoles(Gas.Plasma) < 20f, "plasma consumed");
			Assert.True(node.GasMix.GetMoles(Gas.Oxygen) < 20f, "oxygen consumed");
			Assert.True(node.GasMix.GetMoles(Gas.CarbonDioxide) > 0f, "CO₂ produced");
			Assert.True(node.GasMix.Temperature > temperatureBefore, "fire releases heat");
			Assert.Contains(node, visuals.OnFire);
		}

		[Fact]
		public void PlasmaFire_SelfSustains_ByRequeueingItsOwnTile()
		{
			var node = MakePlasmaOxygenNode(500f);
			var sim = new AtmosSimulation();

			sim.RequestUpdate(node);
			sim.Run();

			// A burning tile must stay in the update list without external help.
			Assert.True(sim.UpdateListCount > 0, "burning tile re-queued itself");

			// Let it burn out over many passes: reactants deplete, fire stops.
			for (int i = 0; i < 500; i++)
			{
				sim.Run();
			}

			var plasmaLeft = node.GasMix.GetMoles(Gas.Plasma);
			var oxygenLeft = node.GasMix.GetMoles(Gas.Oxygen);
			Assert.True(plasmaLeft < 0.5f || oxygenLeft < 0.5f,
				$"fire must exhaust a reactant (plasma {plasmaLeft}, O₂ {oxygenLeft})");
		}

		[Fact]
		public void Ice_FormsOnlyBelowFreezing_WithWaterVapourPresent()
		{
			var coldWet = new GasMix(GasMix.TileVolume, 250f);
			coldWet.SetMoles(Gas.WaterVapour, 5f);
			coldWet.SetMoles(Gas.Nitrogen, 50f);
			var node = new FakeNode(coldWet);

			var visuals = new RecordingVisuals();
			var sim = new AtmosSimulation(visuals);
			sim.RequestUpdate(node);
			sim.Run();

			Assert.Contains(node, visuals.Iced);
		}

		// =====================================================================
		// Determinism
		// =====================================================================

		[Fact]
		public void Simulation_IsDeterministic_AcrossRuns()
		{
			var stateA = RunBreachScenario();
			var stateB = RunBreachScenario();

			Assert.Equal(stateA.Count, stateB.Count);
			for (int i = 0; i < stateA.Count; i++)
			{
				Assert.Equal(stateA[i].pressure, stateB[i].pressure); // bitwise
				Assert.Equal(stateA[i].temperature, stateB[i].temperature);
			}
		}

		private static List<(float pressure, float temperature)> RunBreachScenario()
		{
			// 1×4 corridor, one end breached to space.
			var tiles = new FakeNode[4];
			for (int i = 0; i < 4; i++)
			{
				tiles[i] = new FakeNode(GasMix.StandardAtmosphere()) { Position = new Vector3Int(i, 0, 0) };
				if (i > 0)
				{
					FakeNode.Link(tiles[i - 1], tiles[i]);
				}
			}

			var space = new FakeNode(GasMix.Space()) { IsSpace = true };
			FakeNode.Link(tiles[3], space);

			var sim = new AtmosSimulation();
			sim.RequestUpdate(tiles[3]);
			for (int pass = 0; pass < 20; pass++)
			{
				sim.Run();
			}

			var result = new List<(float, float)>();
			foreach (var tile in tiles)
			{
				result.Add((tile.GasMix.Pressure, tile.GasMix.Temperature));
			}

			return result;
		}

		// =====================================================================
		// Golden Trace parity
		// =====================================================================

		private const string GoldenTraceSkip =
			"Golden trace not yet captured. Requires the instrumented Unitystation build to dump " +
			"per-tile (pressure, temperature, per-gas moles) JSON snapshots every atmos update for " +
			"the reference scenarios into tests/Godotstation.Tests/GoldenTraces/. Tolerance: " +
			"pressure ±0.1 kPa, temperature ±0.5 K per tile per snapshot; total station moles must " +
			"match within 0.1% at every snapshot (conservation drift compounds).";

		[Fact(Skip = GoldenTraceSkip)]
		public void GoldenTrace_HullBreach_TileDrainCurves_MatchUnity()
		{
			// Scenario: 3×3 room at standard atmosphere, one tile breached to
			// space at t=0. Compare per-tile pressure every pass for 100 passes.
			// This pins equalisation share math AND wavefront propagation speed.
		}

		[Fact(Skip = GoldenTraceSkip)]
		public void GoldenTrace_PlasmaFire_TemperatureAndConsumptionCurves_MatchUnity()
		{
			// Scenario: sealed tile, 20 mol plasma + 20 mol O₂ ignited at 500 K.
			// Compare temperature, plasma, O₂, CO₂ every pass until burnout.
			// This pins the burn-rate formula, energy release, and heat capacity table.
		}

		[Fact(Skip = GoldenTraceSkip)]
		public void GoldenTrace_DoorOpen_TwoRoomMixing_MatchesUnity()
		{
			// Scenario: 101 kPa room adjacent to 203 kPa room, door opens.
			// Compare both rooms' pressure/composition until the wave settles
			// below MinPressureDifference. Pins the settle threshold feel.
		}
	}
}
