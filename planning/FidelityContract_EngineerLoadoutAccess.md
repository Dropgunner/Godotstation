# Fidelity Contract: Engineer Loadout and Engineering Airlock

## Upstream evidence

Evidence is pinned to Unitystation commit [`c911581e8354958d664b1e32087053ce43638790`](https://github.com/unitystation/unitystation/tree/c911581e8354958d664b1e32087053ce43638790), checked 2026-10-17.

- `Engineer.asset` assigns job type 20, five positions, station repair/power/hull responsibilities, standard clearance ordinals `[31, 10, 11, 13, 12]`, and low-population clearance 23.
- `EngineerPopulator.asset` populates nine named inventory slots: feet, uniform, ear, head, belt, back, storage01, id, and storage02. The port preserves each serialized item GUID in `CrewLoadoutCatalog`.
- `NamedSlot.cs` defines the named-slot ordinals; `Clearance.cs` maps clearance 10 to Engine, 11 to EngineEquip, 12 to MaintTunnels, 13 to ExternalAirlocks, 23 to Atmospherics, and 31 to Construction.
- `ClearanceRestricted.cs` searches the active hand, ID, belt, and suit storage for clearance sources. `IDCard.cs` initializes the card when first moved into a player's inventory from the occupation's standard and low-population clearance lists.

Relevant pinned source files: [`Engineer.asset`](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/ScriptableObjects/Occupations/Engineering/Engineer.asset), [`EngineerPopulator.asset`](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/ScriptableObjects/Inventory/Populators/Occupations/EngineerPopulator.asset), [`Clearance.cs`](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Systems/Clearance/Clearance.cs), [`ClearanceRestricted.cs`](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Systems/Clearance/ClearanceRestricted.cs), and [`IDCard.cs`](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Items/IDCard.cs).

## Port rules

1. An Engineer spawn receives the nine source-mapped items in their named slots. The playtest profile is intentionally limited to this role; other jobs do not inherit Engineer equipment.
2. The Engineer ID item carries standard and low-population clearance separately. Standard Engineer access includes Construction, Engine, EngineEquip, ExternalAirlocks, and MaintTunnels; low-population access includes Atmospherics.
3. Restricted-door authorization reads clearance from the active hand, ID, belt, or suit-storage items. A job ID or displayed role name is not itself authorization.
4. The engineering airlock is closed and blocks walking until a server-resolved click succeeds. Only a crew member carrying Engine clearance can toggle it, and the changed passability is replicated to every client.
5. This slice models one engineering door and the source-derived access lookup. It does not claim that the complete Unitystation access catalog, all ID initialization behavior, access configuration UI, equipment behavior, or engineering machinery has been ported.

## Regression coverage and status

`CrewLoadoutTests` verifies the nine-item catalog shape and item IDs used for the ID and belt, exact standard and low-population clearances, Engineer inventory and access at spawn, successful airlock use and passability change, and Assistant denial. The two-client Godot smoke must additionally verify denial on the Assistant peer and replicated opening after the Engineer's authorized request. These checks demonstrate this narrow role/access slice, not a complete Engineer job loop or overall Unitystation parity.
