# Fidelity Contract: Inventory & Item Storage

## Core Architecture

Inventory is a tree: storages contain slots, slots contain items, and an item may provide
nested storages (for example, a backpack inside a backpack). All slot membership changes and
world-moving operations MUST pass through `InventorySystem.ServerPerform(InventoryMove)`.
The headless backend owns validation and inventory state; `IInventoryWorld` receives the
side effects that require a world (pickup disappearance, drops, throws, despawns, and
container storage). Stack amount consumption is handled by the item's `IStackable` behavior
through `InventorySystem.ServerConsume`.

The Godot frontend MUST treat backend slot contents as authoritative. It may request moves
and render slot changes, but MUST NOT directly mutate `ItemSlot.Item` or reimplement the
backend's validation or throw calculations.

Stack presentation is part of the frontend contract: occupied slot data exposes the
authoritative stack amount, and the HUD shows a count only when the amount exceeds one.
Stack identity and capacity remain item-owned through `IStackable`; merge, consume, split,
source-slot bookkeeping, and despawn behavior remain owned by `InventorySystem`.

## FC-1: Slot Identity

For a given `(ItemStorage, SlotIdentifier)` pair, every successful lookup MUST return the
same `ItemSlot` object for the lifetime of that storage. A lookup for an identifier the
storage does not define MUST return `null` and MUST NOT create a slot.

`NamedSlot` numeric values are serialized data: their existing ordinals, including the gap
at `4` and `none = 2048`, MUST remain unchanged.

**Tests:** repeated lookup uses reference equality; undefined lookups return null; enum
ordinal assertions pin the serialized values.

## FC-2: Add Gauntlet

Adding an item that is not in a slot to a destination slot MUST fail, in this order, when:

1. the item is buckled to something in the world;
2. the item is already in any slot;
3. the destination slot is null;
4. the destination is occupied, its occupant cannot accept the incoming stack (FC-5), and
   the replacement strategy is `Cancel`;
5. the destination's `CanFit` rejects the item.

For an occupied destination with a non-stackable or incompatible occupant, `DropOther` MUST
drop the occupant before adding the incoming item, and `DespawnOther` MUST despawn the
occupant before adding the incoming item. Capacity/hierarchy checks in step 5 MAY be skipped
only when the move explicitly sets `ignoreRestraints`; that flag MUST NOT bypass steps 1–4.
On a rejected move, slot contents and item-to-slot bookkeeping MUST remain unchanged.
On success, the incoming item MUST be assigned to the destination and its world presence
removed through `IInventoryWorld`.

**Tests:** one test per rejection gate and replacement strategy; verify ordering and that a
failed move leaves slots and item bookkeeping unchanged; verify `ignoreRestraints` bypasses
capacity only.

## FC-3: Lock (ItemNotRemovable)

A slot with `ItemNotRemovable = true` MUST NOT release its item through Drop, Throw,
Despawn, Vanish, Transfer-out, or Swap. An occupied non-removable destination MUST reject
an incoming Transfer before stack merging or replacement, even when `DropOther` or another
replacement strategy was requested. Rejected operations MUST leave the affected slots,
items, and world-side-effect log unchanged.

**Tests:** exercise each removal operation against a locked source; exercise transfer into
an occupied locked destination with a non-cancel replacement strategy.

## FC-4: No Inventory Loops

`ItemSlot.CanFit(item)` MUST reject an item if that item is the slot's storage or any
ancestor storage's `AsPickupable` item. The ancestor walk MUST reject the fit if it reaches
more than five ancestor levels, matching the source behavior. A valid item placed in a
storage MUST remain discoverable through the nested storage tree, and its root storage MUST
be the outermost storage in that tree.

**Tests:** reject a storage item inserted into itself; reject insertion into a descendant
that would create a cycle; verify the five-level boundary; verify nested slots and root
storage resolution.

## FC-5: Stacking Wins Over Replacement

When an Add or Transfer targets an occupied slot and the occupant's `IStackable` reports
that it can accommodate the incoming item, the move MUST merge the incoming stack into the
occupant and report success. It MUST NOT consult or perform the replacement strategy, and
the existing occupant MUST remain in the destination slot. An occupied slot's `CanFit`
MUST return true only when this stack-compatibility check succeeds.

**Tests:** compatible Add and Transfer merge before replacement; assert that the original
occupant remains in the slot, the incoming object is despawned once, and no replacement drop
occurs; incompatible and non-stackable items do not fit an occupied slot.

Successful merges consume the incoming item through the world seam, clear a transferred
source slot, and notify the surviving stack's slot observers. Consuming a partial amount
notifies observers; consuming the final amount removes and despawns the item.
Removing one from a stack of more than one returns and vanishes a split item while leaving
the original item in its slot and notifying its updated amount; a one-item stack uses the
ordinary vanish path.

**Regression tests:** verify incoming-item despawn/source bookkeeping and partial/final
consume notifications and cleanup, plus split-one bookkeeping and notification.

## FC-6: Throw Ballistics

For a Throw, the backend MUST normalize the requested world-target vector into the trajectory
and clamp travel distance to the item's `ThrowRange`. It MUST pass the item `ThrowSpeed`,
aimed body part, trajectory, and computed airtime to `IInventoryWorld.Throw`.

- With sticky movement, airtime MUST be `distance / throwSpeed`.
- Otherwise, airtime MUST be `distance / throwSpeed - throwSpeed / (2 * f)`, where
  `f = MovementProcessor.DEFAULT_FRICTION` (15).
- Negative airtime MUST be passed through unchanged.
- If the holder is inside an object container, Throw MUST degrade to a drop into that
  container via `StoreInHolderContainer`; it MUST NOT invoke world Throw or world Drop.
- A normal Drop from a holder inside a container MUST use `StoreInHolderContainer` rather
  than placing the item on the floor.

The backend MUST own the range clamp and airtime calculation; the frontend MUST pass the
requested target and MUST NOT duplicate this math.

**Tests:** verify distance below and above range, sticky and friction airtime (including a
negative result), trajectory/speed/aim passed to the world seam, and both container drop
paths.

## FC-7: Deterministic Slot Order

Enumerating storage slots and selecting the next empty/free slot MUST follow the order in
which identifiers were declared when the storage was constructed. This makes first-free
slot queries deterministic and replay-stable.

**Tests:** next-slot selection follows a mixed named/indexed declaration sequence.

## FC-8: Storage Category Deny Flags

A storage item MAY expose one `StorageItemName` category through `IStorageIdentifier`.
An `ItemStorage` MAY deny one or more categories with `DenyStorageOfStorageItems`.
`ItemSlot.CanFit` MUST reject an identified item when its category is denied by the
destination storage. The check applies to the direct destination storage only. Items with
no storage identifier and identified items whose category is allowed MUST remain eligible
under the other fit rules; an identifier with `None` has no category to deny. Denial applies
before occupied-stack merging. `ignoreRestraints`
MAY bypass this check as part of the existing capacity and hierarchy bypass.

Category flag values MUST match Unitystation's serialized values: CardboardBox=1, ToolBox=2,
MedKit=4, Backpack=8, Dufflebag=16, Belt=32, Briefcase=64, FirefirstAidKit=128, Satchel=256.

**Tests:** denied Add and Transfer leave slot/item bookkeeping and world effects unchanged;
denied items cannot merge into occupied stacks; allowed and unidentified items still fit; a
parent storage's deny flags do not affect nested storage; enum ordinals match the source;
`ignoreRestraints` bypasses the deny rule.

## FC-9: Trait-Based Best Slot Selection

`BestSlotForTrait` MUST examine configured mappings in declaration order. A mapping with a
trait applies only when the item implements `IItemTraitProvider` and has that trait; a mapping
without a trait matches any item that provides trait data. The first matching named slot that
passes `ItemSlot.CanFit` MUST be returned. If no preference fits, fallback MUST select the
first fitting slot in storage declaration order, excluding the named left and right hand
slots. Items without trait data skip preference mappings and use fallback. Null item/storage
inputs and storages with no eligible fitting slot return null. UI slot linkage is not part of
this engine contract.

Trait ids are ordinal, case-sensitive strings at this engine boundary; the Godot/content layer
is responsible for assigning stable ids to source traits.

**Tests:** verify mapping order and trait matching, continuation when a preferred slot cannot
fit, declaration-order fallback, hand-slot exclusion, missing trait data fallback, and null
when no slot fits.

## FC-10: One-Shot Storage Population

`InventorySystem.ServerPopulate` MUST attempt population at most once per `ItemStorage`,
including when spawn flags suppress the work. It MUST do nothing when `SpawnItems` is false,
or when `ManuallySpawnContent` is true and the context does not allow manual contents.
Otherwise it MUST invoke populators in declaration order and spawn each entry through
`IInventoryItemSpawner`. An entry without a target slot uses the next empty declared slot; an
explicit target uses its defined slot. A spawned item is added through `InventorySystem`
with capacity constraints bypassed, while the existing Add rejection gates still apply.
Failed spawns, undefined targets, and rejected Adds do not count as added items.

**Tests:** verify ordered spawning and slot assignment, initial-capacity bypass through the
ordinary Add path, spawn/manual flag gating, one-shot behavior, failed spawns, and undefined
target handling. Recursive population of nested storage is deferred.

## Additional move bookkeeping assertions

Successful moves MUST keep `ItemSlot.Item` and `IPickupable.ItemSlot` consistent, notify
the affected slot/storage change hooks, and invoke the moved item's inventory-move hooks
with the completed move. Remove operations MUST clear slot and item bookkeeping before
delegating the corresponding world effect. Swap MUST preserve both items and their final
slot associations while performing the source behavior's two world drops and re-adds.

**Tests:** verify Add/Transfer/Remove bookkeeping, slot/storage notifications, move hooks,
and Swap results and world calls.

## Documented deviations

- Upstream `ServerVanishStackable` reads `fromSlot.Item` after Vanish has emptied the slot,
  producing null/undefined behavior for a non-stackable item. The port captures the item
  before removal, returns it, and passes it to the vanish world effect.

**Regression test:** non-stackable vanish returns the removed item and invokes the vanish
world effect without reading the source slot after it has been emptied.
- `SlotIdentifier` is represented as a value type with value equality rather than a
  globally pooled reference type. Equality and dictionary-key behavior are the intended
  observable semantics.
- Server-authority checks (`CustomNetworkManager.IsServer`) belong to the caller in the
  headless engine and are not implemented by the inventory backend.

## Slot observer updates

The inventory system MUST support registering an observer for a slot, immediately sending
its current item and removal-lock state, and sending authoritative updates after item or
lock changes. Removing an observer MUST send a clear-item update and stop later updates to
that observer. Registration of the same observer more than once MUST NOT duplicate change
notifications. The backend exposes an update DTO and transport interface; serialization and
network delivery remain frontend responsibilities.

**Tests:** verify initial snapshots, item and lock updates, per-observer removal and clear,
duplicate registration, and operation without a configured transport.

## Deferred behavior

Client prediction and `ClientRequestTransfer`; UI slot linkage; recursive population of
nested storage; EMP propagation; inventory sounds; and `Stackable.ServerStackOnGround`
ground merging are outside this contract.
