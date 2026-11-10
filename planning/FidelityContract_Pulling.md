# Fidelity Contract: Pulling

**Status:** implemented in the engine and LAN adapter with deterministic tests and a two-peer `--pull` smoke in CI. A hands-on ordinary-controls playtest is still open.

**Source check:** tgstation [`f37d36c`](https://github.com/tgstation/tgstation/tree/f37d36c295c4bf991b72647281352f3bd471b335), read directly on 2026-11-10.

## Upstream behavior and evidence

- [`code/_onclick/click_ctrl.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/_onclick/click_ctrl.dm): `mob/living/base_click_ctrl` pulls a movable target the mob can act on from an adjacent tile.
- [`code/game/atoms_movable.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/game/atoms_movable.dm):
  - `can_be_pulled` refuses the puller itself and anything anchored.
  - `start_pulling` with the default state drops an existing pull instead of starting a new one, and takes the object from any other puller.
  - `check_pulling` ends a pull when the two are more than one tile apart or the object becomes anchored.
  - After the puller moves, a pulled object that is diagonal or out of reach is moved into the tile the puller left.
  - A pulled mob in soft crit or worse gets `TRAIT_IMMOBILIZED`.
- `update_pull_movespeed` in [`living_movement.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/modules/mob/living/living_movement.dm) slows a mob that drags someone lying down by `PULL_PRONE_SLOWDOWN` (1.5).
- Pulling a mob shows "X grabs Y passively."

## Contract implemented in Godotstation

1. Ctrl-clicking an adjacent crew member or an unanchored service cart starts a pull. The puller needs working hands and a clear reach.
2. A ctrl-click while already pulling drops the pull, whatever it targets. The H key also lets go.
3. Each object has one puller. A new puller takes it from the previous one.
4. When the puller changes tile and the pulled thing is left diagonal or more than one tile away, it moves into the tile the puller vacated.
5. The pull ends when the two are more than one tile apart, the puller loses the use of their hands, the cart is anchored, or either one leaves the round. A conscious crew member can break free by walking away.
6. A critical crew member who is being pulled cannot crawl. Dragging someone who is lying down halves the puller's speed.
7. The pull is replicated on the puller's crew entity. Both players see it on the HUD, and nearby crew see "X grabs Y passively."

## Known gaps before parity claims

- Only crew and the service cart can be pulled. tgstation lets you pull loose items and most unanchored objects.
- There are no grab stages beyond the passive pull: no aggressive grab, neck grab, or resisting out of a grab.
- The half-speed drag is playtest-scaled. It is not a conversion of tgstation's 1.5 move-delay slowdown.
- A pulled thing jumps tile by tile on the server; only client-side smoothing softens it.
- Pulling is not a separate click cooldown, and a pulled object does not swap places with the puller when clicked onto the puller's tile.

## Regression coverage

`PullingTests` cover starting and replicating a pull, dropping it with a second ctrl-click, refusing self, distant, anchored and fixed targets, pulling an unanchored cart and losing it when anchored, one puller per object, following into the vacated tile for straight and diagonal steps, breaking by distance and by incapacitation, immobilizing a critical crew member and slowing the puller, and ending pulls when someone leaves.

`tools/multiplayer_smoke.sh --pull` runs an Engineer host and an Assistant client. The client ctrl-pulls the host and walks two tiles; the host must end one tile behind. The host then walks away until the pull breaks by distance, and the client sees its pull cleared.
