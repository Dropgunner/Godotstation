# Fidelity Contract: Girder Plating

**Status:** implemented in the LAN playtest adapter with headless domain coverage; not yet a complete Engineer job loop.

**Source check:** Unitystation commit `c911581e8354958d664b1e32087053ce43638790`, checked 2026-10-17.

## Upstream behavior and evidence

- `Girder.ServerPerformInteraction` accepts a held metal-sheet item on a girder. It requires at least two sheets, runs `ToolUtils.ServerUseToolWithActionMessages` for four seconds, then constructs a wall. An unsecured girder instead becomes a false wall. The source also accepts plasteel, titanium, wrench, and screwdriver paths, which are outside this slice. [Girder.cs](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Objects/Construction/Girder.cs)
- The Engineer job asset lists station repairs, power maintenance, and hull-breach repair among its duties. It does not make the girder handler Engineer-only. [Engineer.asset](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/ScriptableObjects/Occupations/Engineering/Engineer.asset)

## Contract implemented in Godotstation

1. The host owns a two-sheet metal stack and one girder in the shared station world. A crew member picks the stack up into the active hand through the engine inventory and interaction dispatcher.
2. The server accepts a plating request only when the actor is connected, has the stack in the active hand, has at least two sheets, and passes the shared interaction reach and obstruction checks.
3. Construction progress advances on the server for four seconds. Leaving reach or switching away from the active hand interrupts and resets the action. The server consumes both sheets only on completion.
4. Completion changes the shared girder state to a plated wall and replicates its state, remaining material count, worker, and progress to every playtest client. The rendered object remains a blocking station object.
5. The source does not gate this interaction by job, so any crew role with the material can perform it. The playtest defaults to Engineer to make the sourced job responsibility the first visible task.

## Known gaps before parity claims

- The material stack and girder are fixed playtest objects, not yet part of a complete station construction map or general item-trait system.
- The upstream unanchored false-wall path, reinforced/plasteel/titanium paths, tool action messages, tool/item traits, wall construction damage/deconstruction, floor and tile updates, and full interruption causes remain open.
- Engineer clothing, toolbelt contents, clearance/access, power machinery, and a complete role loadout are not implemented. This task advances roadmap P3 but does not complete its role/equipment/machinery exit evidence.
- The playtest is host-authoritative LAN code. Its two-client smoke is runtime evidence for this path, not broad Unitystation parity evidence.

## Regression coverage

`GirderPlatingTaskTests` covers the two-sheet prerequisite, reach, four-second completion, interruption reset, and worker ownership. `SharedGirderConstructionWorldTests` covers active-hand material use, consumption, authoritative shared completion, role-neutral construction, and interruption by movement or hand switch. `tools/multiplayer_smoke.sh --construction` exercises the two-client request and replication path.
