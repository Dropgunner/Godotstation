# Fidelity Contract: Shared-World Pickup and Wrench Interaction

**Status:** first playable vertical slice; intentionally narrower than Unitystation inventory and construction parity.

**Source check:** Unitystation source at commit `c911581e8354958d664b1e32087053ce43638790`, checked 2026-10-17.

## Upstream behavior and evidence

- `HandApply` models a click as applying the active-hand object, or an empty hand, to a clicked target. It captures the hand item, active slot, intent, and alt-click state at interaction creation. [HandApply.cs](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Core/Input%20System/InteractionV2/Interactions/HandApply.cs)
- `Pickupable.WillInteract` requires the item to be pickable, the click target to be the item, and an empty hand. It checks default interaction validity plus pickup reach; the server later inserts the item into the requested active slot and rolls back if insertion fails. [Pickupable.cs](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Systems/Inventory/Pickupable.cs)
- `WrenchSecurable.WillInteract` applies default `HandApply` validation, requires the target to be itself, and requires a held item with the wrench trait. The server unanchors an anchored object; anchoring requires a floor and an unblocked anchor position. [WrenchSecurable.cs](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Systems/Construction/WrenchSecurable.cs)

## Contract implemented in Godotstation

1. A connected, spawned crew member can pick up the one shared wrench only with an empty hand and within the standard validated reach.
2. A crew member holding that wrench can click the service cart within standard reach to toggle its anchored state. The handler is routed through `InteractionDispatcher`; no client-supplied owner ID or position is trusted.
3. The host resolves the requesting peer from the transport sender, reads that peer's authoritative position, checks interaction range and the station wall line, and replicates wrench ownership and cart anchored/position state.
4. An unanchored cart is pushable through the existing server movement processor; its new position is replicated as shared state.
5. The wrench owner can drop it at the authoritative player position with Q; another nearby crew member can then pick it up. A non-owner cannot drop it.
6. Unknown peers, unknown targets, out-of-range clicks, blocked click lines, an occupied pickup, or wrench use without ownership leave the world state unchanged.

## Known gaps before parity claims

- The playtest's wrench is a single shared fixture item, not a general item-storage system. It does not yet model multiple hand slots, inventory UI, storage slots, pickup animation/prediction, or failed inventory insertion. Q is a single-item world drop, not the full inventory drop flow.
- The fixture is placed on a valid floor tile. Floor absence, exposed-plating requirements, timed tool use, anchor blockers, wrench traits as data, examine feedback, and action messages are not implemented.
- The input currently uses the Help intent for this world interaction. Although `HandApply` transports intent and alt-click, this slice does not implement the wider intent actions or an examine action. The cited wrench source does not branch on intent or alt-click in `WillInteract`.
- The multiplayer path is a LAN playtest adapter with host authority. This contract does not establish production server hosting, cheat resistance beyond the request checks listed above, or overall Unitystation gameplay parity.

## Regression coverage

`SharedStationInteractionWorldTests` covers shared pickup ownership, held-tool use, drop and re-pickup, reach and obstruction refusal, invalid peers/targets, and state-change snapshots. Movement pushability remains covered by the existing movement fidelity tests. Runtime two-client verification is still required to establish end-to-end replication behavior.
