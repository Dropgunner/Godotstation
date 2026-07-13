# Wave 5: Interaction Dispatch Frontend

You are acting as the Builder (Sweeney). Your job is to wire Claude's Interaction
Dispatch backend into Godot's input and scene system.

## Context

Claude has already built the backend in `src/Project.Common/Interaction/`. It
contains, among other things:

- `IPerformer`, `IInteractable`, `IInteractionHandler<T>` — the core
  engine-agnostic interfaces (`Interfaces.cs`)
- `IOwnedInteractionHandler<T>` — new in Wave 5. Extends `IInteractionHandler<T>`
  with `IInteractable Owner` and `bool IsFirstInteractable`, so the dispatcher
  can tell whether a handler belongs to the interaction's target or to whatever
  the performer is holding.
- `InteractionDispatcher` — call `TryResolveClick(performer, handObject,
  targetObject, targetPosition, intent, isAltClick, side)` to resolve a click.
  Handlers are registered ahead of time via `RegisterHandler(...)`.

Resolution order for `HandApply`/`PositionalHandApply` (this is now
contract-bearing — see FC-8 and FC-9 in
`planning/FidelityContract_InteractionSystem.md`):

1. The target's handler, if it's `IsFirstInteractable` — fires unconditionally
2. The held item's handler
3. The target's other (non-first) handlers
4. Handlers with no `Owner` (none of yours should hit this bucket)

**`Priority` only orders handlers within one of those buckets** — it does not
let a low-priority target handler jump ahead of the held item. Don't try to use
`Priority` to fake first-interactable behavior; set `IsFirstInteractable = true`
instead.

**Important — there is no `game/` project in this repo yet.** No prior Codex
wave has actually been executed here (the `planning/PROMPT_CODEX_WAVE1-3.md`
files are drafts). You're setting up the Godot project structure for the first
time. Keep it minimal — just enough to make the interaction wiring below
runnable.

## Your Tasks

### 0. Bootstrap the Godot project
1. `game/project.godot` — a minimal Godot 4 project with Mono/C# enabled.
2. `game/Godotstation.Game.csproj` — references
   `../src/Project.Common/Project.Common.csproj`. No Godot references belong in
   `Project.Common`, but `game/` obviously references both Godot and
   `Project.Common`.

### 1. Engine adapters
3. `game/Player/PlayerCharacter.cs` — `CharacterBody2D` implementing
   `IPerformer`. `WorldPosition` reads from `GlobalPosition`; back the rest
   (`IsGhosting`, `HasTelekinesis`, `IsAlive`, `ActiveHandItem`,
   `CurrentIntent`) with exported fields or stub state for now — a later wave
   owns the real health/mind/inventory systems.
4. `game/World/InteractableNode2D.cs` — a base `Node2D` implementing
   `IInteractable` (`Id` from a stable export or generated GUID, `WorldPosition`
   from `GlobalPosition`) that any clickable world object can extend.

### 2. Example handlers (prove FC-8 and FC-9 against real nodes)
5. `game/Items/WrenchHandler.cs` — attach to a wrench item's node. Implements
   `IOwnedInteractionHandler<HandApply>` with `Owner` = its own
   `InteractableNode2D`, `IsFirstInteractable = false`. `WillInteract` accepts
   when the target is tagged as pipe-like (a simple exported bool/group check is
   fine — no real pipe system exists yet). `ServerPerformInteraction` can just
   log/emit a signal for now.
6. `game/World/AirlockHandler.cs` — attach to a door node. Implements
   `IOwnedInteractionHandler<HandApply>` with `Owner` = its own
   `InteractableNode2D`, `IsFirstInteractable = true`. `WillInteract` always
   returns true — an airlock always claims the click. `ServerPerformInteraction`
   toggles an open/closed state (visual only is fine for now).

   These two together are the manual proof of FC-8/FC-9: clicking the airlock
   with the wrench in hand must open/close the door (AirlockHandler wins,
   IsFirstInteractable), while clicking a plain pipe with the wrench in hand
   must wrench it (WrenchHandler wins over any non-first target handler).

### 3. Input wiring
7. `game/Input/InteractionInputController.cs` — owns (or resolves via autoload)
   a single shared `InteractionDispatcher` instance. On a left-click
   `InputEventMouseButton`, build the call: performer = local
   `PlayerCharacter`, `handObject` = `performer.ActiveHandItem`, `targetObject`
   = whatever `InteractableNode2D` is under the cursor (physics pick, or `null`
   if nothing), `targetPosition` = mouse position in world space, `intent` =
   current UI-selected `Intent`, `isAltClick` = whether Alt was held,
   `side` = `NetworkSide.Server` (there's no netcode wave yet — everything is
   authoritative-only for now; a future wave will split client prediction out).
   Call `TryResolveClick` with those values.
8. Each `WrenchHandler`/`AirlockHandler` registers itself with the shared
   dispatcher in `_Ready()` via `RegisterHandler(this)`.

### 4. Scene layout
9. Provide the `.tscn` node hierarchy as a markdown outline (same convention as
   prior waves): a scene with the `PlayerCharacter`, one `AirlockHandler` node,
   one item node with `WrenchHandler` sitting in the player's hand slot, and
   the `InteractionInputController` wired to the player.

## Constraints

- **Never touch `src/Project.Common/`.** If the wiring doesn't fit, the gap
  belongs in a note back to Claude, not a workaround in `Project.Common`.
- Use `InteractionDispatcher.TryResolveClick` as the single entry point for
  clicks — don't hand-roll click resolution in `game/`.
- `Godot`-namespace types stay entirely inside `game/`; `IPerformer`/
  `IInteractable` implementations translate between `Godot.Vector2` and
  `System.Numerics.Vector2` (`Project.Common`'s type) at the adapter boundary.
- Commit your changes to the repository on a `wave5/dispatch-frontend` branch
  (branch from `wave5/dispatch-order`, since this depends on the backend
  landing first).
