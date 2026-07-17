# Codex Wave 6 Prompt: Inventory Frontend Wiring

You are the Builder. The Architect has landed the pure C# inventory backend in
`src/Project.Common/Inventory/` (branch `wave6/inventory-core`). Your job is the Godot 4
side. Do NOT modify anything in `src/Project.Common/` — build against it.

## What the backend gives you

- `InventorySystem` — construct ONE with your `IInventoryWorld` implementation. All
  mutations go through it: `ServerAdd`, `ServerTransfer`, `ServerDrop`, `ServerThrow`,
  `ServerDespawn`, `ServerSwap`, `ServerTryAdd(storage, item)`, `ServerDropAll(storage)`.
  Subscribe to `InventorySystem.OnMovePerformed` for sounds/UI refresh.
- `ItemStorage` — pure state container. Create one per player/backpack/crate with its
  slot list (`SlotIdentifier.Named(...)` / `.Indexed(...)`), a capacity policy, and (for
  items that ARE storages, like backpacks) the item's `IPickupable` as `asPickupable`.
- `ItemSlot` — subscribe to `OnSlotContentsChangeServer` to update slot sprites.
  `ItemStorage.ServerInventoryItemSlotSet` fires (removed, added) for storage-level UI.

## What you must implement (Godot side)

1. **`IPickupable`** on your item node/component: identity, `ItemSlot` bookkeeping
   (`SetItemSlot` just records), `IsBuckled`, `Stackable` (null unless stackable),
   `ItemStorages` (nested storages if the item is a bag), `MoveHooks`, `ThrowSpeed`,
   `ThrowRange`, `StickyMovement`.
2. **`IInventoryWorld`** — the world effects:
   - `DisappearFromWorld(item)`: hide the node / remove from tile map when picked up.
   - `Drop(item, fromSlot, worldTargetVector)`: reparent the node into the world at the
     root holder's tile (+ vector if given), inheriting holder momentum when vector is null.
   - `Throw(item, fromSlot, trajectory, speed, airTime, aim)`: kick the node along
     `trajectory * speed` for `airTime` seconds (airtime ≤ 0 → lands immediately). Feed it
     to the Movement backend's momentum system.
   - `Despawn` / `Vanish`: free the node / keep it hidden.
   - `IsHolderInContainer(fromSlot)` + `StoreInHolderContainer`: locker/crate support.
3. **`IItemStorageCapacity`** — start with a size-based policy (w_class vs slot limits).
4. **UI**: hand slots + backpack grid. Click = transfer between slots via
   `InventorySystem.ServerTransfer`; drag onto world = `ServerDrop` with target vector;
   throw mode = `ServerThrow`.

## Contract you must not break

Read `planning/FidelityContract_Inventory.md`. In particular: never mutate `ItemSlot.Item`
yourself (Rule 1 pooling means the backend's slot IS the truth), respect `ItemNotRemovable`
in UI affordances (Rule 3), and don't re-implement throw math in the frontend — the
backend already clamps range and computes airtime (Rule 6).
