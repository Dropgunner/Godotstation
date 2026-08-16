using System.Collections.Generic;
using Godotstation.Engine.Physics;

namespace Godotstation.Engine.Atmos
{
	/// <summary>
	/// Facade replacing Unitystation's MetaDataNode. The original node was a
	/// grab-bag tied to the tilemap MonoBehaviours (MetaDataSystem, room
	/// numbers, hotspots, damage, radiation...); this interface keeps exactly
	/// the surface AtmosSimulation reads and writes, nothing else. The Godot
	/// layer implements it over its tile metadata; tests implement it in ten
	/// lines.
	/// </summary>
	public interface IAtmosNode
	{
		/// <summary>Tile still exists (was node.Exists — destroyed tiles linger in the queue).</summary>
		bool Exists { get; }

		/// <summary>Metadata initialisation finished (was node.MetaDataSystem.SetUpDone).</summary>
		bool IsSetUp { get; }

		/// <summary>Blocked by a wall/airtight structure: participates in conduction but never equalises.</summary>
		bool IsOccupied { get; }

		/// <summary>Explicitly excluded from equalisation (isolation markers, e.g. under airlocks mid-cycle).</summary>
		bool IsIsolatedNode { get; }

		/// <summary>Open space: an infinite vacuum sink. Gas equalised into it is destroyed.</summary>
		bool IsSpace { get; }

		Vector3Int Position { get; }

		/// <summary>
		/// The tile's gas, by ref: equalisation and reactions mutate it in
		/// place. GasMix is a value type — handing out copies here would make
		/// every write silently vanish, so ref access is a correctness
		/// requirement, not an optimisation.
		/// </summary>
		ref GasMix GasMix { get; }

		/// <summary>Temperature of the solid tile itself (plating, wall), Kelvin.</summary>
		float SolidTemperature { get; set; }

		/// <summary>Heat capacity of the solid tile, J/K. Zero for open space.</summary>
		float SolidHeatCapacity { get; }

		/// <summary>Solid↔gas conduction coefficient, dimensionless fraction per update.</summary>
		float ThermalConductivity { get; }

		/// <summary>
		/// Append this node's atmos neighbors as (node, doEqualise) pairs.
		/// Mirrors MetaDataNode.AddNeighborsToList including its contract:
		/// order must be deterministic (N/E/S/W registration order) because
		/// equalisation iterates the list and parity depends on it.
		/// </summary>
		void AddNeighborsToList(List<(IAtmosNode node, bool doEqualise)> nodes);

		/// <summary>Neighbor list for re-queueing after a change propagates.</summary>
		IReadOnlyList<IAtmosNode> Neighbors { get; }
	}
}
