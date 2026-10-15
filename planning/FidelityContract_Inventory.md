# Fidelity Contract: Inventory & Item Storage

## Core Architecture
Ported from `US13/Systems/Inventory/` (`Inventory.cs`, `ItemSlot.cs`, `ItemStorage.cs`,
`InventoryMoveType.cs`, `SlotIdentifier.cs`, `NamedSlot.cs`). Inventory is a tree: storages
hold slots, slots hold items, items may themselves be storages (backpack in a backpack).
All mutation flows through a single gauntlet — `InventorySystem.ServerPerform(InventoryMove)`
— which validates, mutates slot/item state, and delegates world side-effects (drop physics,
despawn, containers) to the `IInventoryWorld` seam. The backend never touches the world
directly; it decides WHAT happens, the Godot frontend decides how it looks.

## Rule 1: Slot Identity
There is only ever ONE `ItemSlot` instance for a given (storage, identifier) pair. Every
getter returns the same pooled instance, so reference identity is reliable. Requesting a
slot the storage does not define returns `null`, never a fresh slot. `NamedSlot` enum
ordinals are part of the serialized format and MUST NOT change (preserved byte-for-byte,
including the gap at 4 and `none = 2048`).

## Rule 2: The Add Gauntlet
`Add` moves an item that is in NO slot into one. In order, the move fails if:
1. the item is buckled to something in the world;
2. the item is already in a slot (use Transfer instead);
3. the target slot is null;
4. the target slot is occupied — unless the occupant is a compatible stack (Rule 5), the
   `ReplacementStrategy` decides: `Cancel` (default) fails the move, `DropOther` drops the
   occupant into the world first, `DespawnOther` destroys it first;
5. the slot's `CanFit` rejects the item (capacity policy, hierarchy loop) — unless the move
   was created with `ignoreRestraints` (one-way containers like the syndicate box).
Only after all gates pass does the item disappear from the world and enter the slot.

## Rule 3: The Lock (ItemNotRemovable)
A slot flagged `ItemNotRemovable` never releases its item — Drop, Throw, Despawn, Vanish,
Transfer-out and Swap all fail. Upstream quirk, preserved: an OCCUPIED locked destination
also rejects incoming transfers before any stacking or replacement logic runs.

## Rule 4: No Inventory Loops
`ItemSlot.CanFit` walks the slot's ancestor storages and rejects the item if any ancestor
IS the item (putting a backpack inside itself, or inside a bag that is inside it). The walk
aborts (rejecting the fit) after 5 ancestor levels, exactly like upstream.

## Rule 5: Stacking Wins Over Replacement
When an Add or Transfer targets an occupied slot whose occupant can accommodate the
incoming stack, the stacks MERGE (`ServerCombine`) and the move reports success — the
`ReplacementStrategy` is never consulted. An occupied slot's `CanFit` is true ONLY via
stack compatibility.

## Rule 6: Throw Ballistics
Throw decisions are computed in the backend before the world seam is invoked:
- Throw distance is clamped to the item's `ThrowRange`.
- Airtime: sticky movement (magboots) flies the full `distance / ThrowSpeed`; otherwise
  friction eats the tail: `airtime = d/v − (v² / 2f) / v` with
  `f = MovementProcessor.DEFAULT_FRICTION (15)` — the same constant the Movement port uses.
  Negative airtime (short throws of fast items) is passed through unmodified, as upstream.
- Throwing while the holder is inside an object container degrades to a Drop, and a Drop
  inside a container stores the item IN the container instead of on the floor.

## Rule 7: Deterministic Slot Order (port improvement)
Storage slots iterate in declaration order, so `GetNextEmptySlot` / first-free queries are
deterministic and replay-stable. Upstream iterated a `HashSet` (unspecified order); a
deterministic engine cannot tolerate that, and no upstream behavior depends on hash order.

## Documented Deviations
- `ServerVanishStackable` on a non-stackable item: upstream read `fromSlot.Item` AFTER the
  vanish emptied the slot (null / NRE). The port captures the removed item first and
  returns it — the evident intent.
- `SlotIdentifier` is a value type with value equality instead of a globally pooled
  reference type; observable semantics (dictionary keys, equality) are identical.
- Server authority checks (`CustomNetworkManager.IsServer`) are the caller's concern in a
  headless engine and are not ported.

## Deferred (frontend / later waves)
Observer players + net messages (`UpdateItemNSlotMessage`), client prediction
(`ClientRequestTransfer`), UI slot linkage, populators/spawning, trait-based
`GetBestSlotFor` (BestSlotForTrait), storage blacklists (`StorageIdentifier` deny flags),
EMP propagation, inventory sounds, `Stackable.ServerStackOnGround` ground-merge.
