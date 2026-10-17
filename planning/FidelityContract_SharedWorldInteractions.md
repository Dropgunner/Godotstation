# Fidelity Contract: Shared-World Pickup and Wrench Interaction

**Status:** shared pickup/tool-use slice extended with a fixed metal stack and timed girder construction; still narrower than Unitystation inventory and construction parity. See [Girder Plating](FidelityContract_GirderPlating.md) for the new task contract.

**Source check:** Unitystation source at commit `c911581e8354958d664b1e32087053ce43638790`, checked 2026-10-17.

## Upstream behavior and evidence

- `HandApply` models a click as applying the active-hand object, or an empty hand, to a clicked target. It captures the hand item, active slot, intent, and alt-click state at interaction creation. [HandApply.cs](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Core/Input%20System/InteractionV2/Interactions/HandApply.cs)
- `Pickupable.WillInteract` requires the item to be pickable, the click target to be the item, and an empty hand. It checks default interaction validity plus pickup reach; the server later inserts the item into the requested active slot and rolls back if insertion fails. [Pickupable.cs](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Systems/Inventory/Pickupable.cs)
- `WrenchSecurable.WillInteract` applies default `HandApply` validation, requires the target to be itself, and requires a held item with the wrench trait. The server unanchors an anchored object; anchoring requires a floor and an unblocked anchor position. [WrenchSecurable.cs](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Systems/Construction/WrenchSecurable.cs)

## Contract implemented in Godotstation

1. A connected, spawned crew member can pick up the one shared wrench only with an empty hand and within the standard validated reach.
2. Each crew inventory has real left- and right-hand slots. Tab switches the active slot without moving either item's contents; interaction dispatch reads the currently selected slot.
3. Pickup uses `InventorySystem.ServerAdd` to move the world item into the active hand. A crew member can click the service cart to toggle its anchored state only when that wrench is in the active hand.
4. Q uses `InventorySystem.ServerDrop` on the active slot. An item held in the inactive slot is neither used nor dropped by the active-hand actions.
5. The host resolves the requesting peer from the transport sender, reads that peer's authoritative position and active hand, checks interaction range and the station wall line, and replicates wrench ownership and cart anchored/position state.
6. An unanchored cart is pushable through the existing server movement processor; its new position is replicated as shared state.
7. A nearby crew member can pick up the dropped wrench. Disconnecting while holding it returns it to the world at the last authoritative crew position.
8. Unknown peers, unknown targets, out-of-range clicks, blocked click lines, an occupied pickup, or wrench use without the wrench in the active hand leave the world state unchanged.

## Known gaps before parity claims

- The playtest now includes a wrench, a two-sheet metal stack, a girder, and a service cart. It still does not provide a general inventory UI, storage slots, pickup animation/prediction, a full inventory screen, or data-driven item traits.
- The fixture is placed on a valid floor tile. Floor absence, exposed-plating requirements for wrench anchoring, anchor blockers, wrench traits as data, examine feedback, and action messages are not implemented. Timed girder construction is covered by the separate contract.
- The input currently uses the Help intent for this world interaction. Although `HandApply` transports intent and alt-click, this slice does not implement the wider intent actions or an examine action. The cited wrench source does not branch on intent or alt-click in `WillInteract`.
- The multiplayer path is a LAN playtest adapter with host authority. This contract does not establish production server hosting, cheat resistance beyond the request checks listed above, or overall Unitystation gameplay parity.

## Regression coverage

`SharedStationInventoryWorldTests` verifies the wrench occupies the actual active-hand slot, switching to the empty hand prevents use, active-hand drop returns it to the world, a second crew member can recover it, and range, obstruction, unknown requests, and disconnect are handled. Movement pushability remains covered by the existing movement fidelity tests. The separate two-client smoke verifies the LAN request and replication path; a headless domain test is not end-to-end parity evidence. The project SDK remains pinned to 4.4.1. These checks verify this slice's runtime path, not overall Unitystation parity.
