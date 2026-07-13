# Fidelity Contract: Interaction System (Wave 1)

*Claude (Architect) — grounded against `unitystation/unitystation` @ `develop`, 2026-07-13.*
*Supersedes the Benchmark B3 draft, which was written without source access and stated reach as
"Manhattan distance > 1"; the real metric is Chebyshev, strictly < 1.5 (see FC-2).*

**Source files ported** (from `UnityProject/Assets/Scripts/US13/Core/Input System/InteractionV2/`):

- `DefaultWillInteract.cs` → [src/Project.Common/Interaction/DefaultWillInteract.cs](../src/Project.Common/Interaction/DefaultWillInteract.cs)
- `Validations.cs` (CanInteract/CanApply/reach core) → [src/Project.Common/Interaction/Validations.cs](../src/Project.Common/Interaction/Validations.cs)
- `Interactions/HandApply.cs` + the `Internal/` base hierarchy, `PositionalHandApply`, `HandActivate`, `InventoryApply`, `MouseDrop`, `AimApply` → `src/Project.Common/Interaction/Interactions/`
- Supporting enums `NetworkSide`, `ReachRange`, `Intent`, `PlayerTypes`, `ConsciousState`, `BodyPartType`, `MouseButtonState` — **values preserved verbatim** (they are serialized).

All logic is pure .NET 8, deterministic, and engine-blind. Unity singletons became injected
seams: `IInteractionWorld` (MatrixManager/PlayerManager), `IClientInteractionContext`
(PlayerManager/UIManager/KeyboardInputManager), `IWorldEntity`/`IPlayerEntity`/`IItemSlot`
(GameObject/PlayerScript/ItemSlot).

---

## The five assertions

### FC-1: Actor gating, with client-side trust deferral

**Assertion.** An interaction is attempted only by a performer whose `PlayerTypes` flag is in the
interaction's allowed mask, who is not cuffed (unless `allowCuffed`), whose input is enabled, and
who is `CONSCIOUS` — or `BARELY_CONSCIOUS` when the interaction opts into `allowSoftCrit`.
`UNCONSCIOUS`/`DEAD` never interact. On the **client**, the conscious-state check applies only to
the local player; remote performers are assumed conscious and the server re-validates with
authority.

**Tests.** [FC1_ActorGatingTests.cs](../tests/Interaction/FC1_ActorGatingTests.cs)

**Rationale.** This is the outer gate of every interaction; the client/server asymmetry is the
IF2 trust model and must survive the port or prediction will desync from authority.

### FC-2: Standard reach — Chebyshev < 1.5, unblocked path, closet and inventory rules

**Assertion.** Standard reach passes iff `max(|dx|, |dy|) < 1.5` tiles (so diagonal neighbours
are in reach; exactly 1.5 or two tiles away is not; the metric is Chebyshev — `(1.4, 1.4)` is in
reach despite Euclidean ≈ 1.98) **and** the click path is unblocked (zero-length paths are never
blocked). A hidden (closeted) player reaches only their own container on the server, and
validates optimistically on the client. A target inside item storage skips reach entirely: it is
interactable iff the storage roots at the performer — inside anyone else's storage it is
unreachable even from adjacent tiles. `Unlimited` ignores range; `ExtendedServer` is
client-trusted and, on the server, also accepts an object at the position it held before its
current newtonian movement (1.75 allowance) so you can catch what was just thrown past you.

**Tests.** [FC2_ReachValidationTests.cs](../tests/Interaction/FC2_ReachValidationTests.cs)

**Rationale.** Reach is the spatial law of SS13; the strict-inequality Chebyshev bound and the
closet/inventory special cases are exactly what players' muscle memory (and the security model)
depend on.

### FC-3: Telekinesis extends reach only for an empty hand

**Assertion.** A performer with telekinesis (a brain that grants it) clicking with an **empty**
active hand is validated at `ReachRange.Telekinesis`: Euclidean distance ≤ 15 between assumed
server positions (inclusive at exactly 15, and genuinely Euclidean — Chebyshev-close diagonals
beyond 15 fail). With an item in hand, reach reverts to Standard. Interactions can veto the
extension via `allowTelekinesis: false`. For `PositionalHandApply`, telekinesis measures to the
target *object*, not the clicked position.

**Tests.** [FC3_TelekinesisTests.cs](../tests/Interaction/FC3_TelekinesisTests.cs)

**Rationale.** The empty-hand restriction is the balance point of psychic characters; the metric
switch (Chebyshev for standard, Euclidean for telekinesis) is an easy porting mistake with real
gameplay consequences.

### FC-4: Resolution is by declared type, per-type rules, fail-closed

**Assertion.** `DefaultWillInteract.Default<T>` dispatches on the **declared** type parameter,
not the runtime type: a `PositionalHandApply` routed to a component that declared `HandApply` is
validated as a plain `HandApply` (target-object reach), while the same instance validated as
`PositionalHandApply` measures reach to the clicked position. Per-type rules hold:
`HandActivate`/`InventoryApply` check only actor state (no reach); `MouseDrop` requires the
performer to reach the dropped object **and** the dropped object and drop target to be within
standard reach of each other (server positions); `AimApply` requires actor state plus not being
hidden. Any interaction type without ported logic returns `false` and reports it — never allows.

**Tests.** [FC4_TypeResolutionTests.cs](../tests/Interaction/FC4_TypeResolutionTests.cs)

**Rationale.** IF2 components declare the interaction type they handle; changing dispatch to
runtime-type "polymorphism" would silently change which validation runs for every subclassed
interaction on the station.

### FC-5: Payload integrity and the Invalid sentinel

**Assertion.** The interaction object delivers the player's `Intent` (all four values),
alt-click flag, targeted body part, hand slot and hand object to handlers exactly as captured at
creation, on both the client (`ByLocalPlayer`) and server (`ByClient`) paths. Ineligible local
players get the `Invalid` sentinel instead of a live interaction — ghosting minds for
`HandApply`, non-Normal players for `HandActivate`, ghosts for `InventoryApply`, storage-less
players for `PositionalHandApply` — and `Invalid` always fails `DefaultWillInteract`.

**Tests.** [FC5_PayloadIntegrityTests.cs](../tests/Interaction/FC5_PayloadIntegrityTests.cs)

**Rationale.** Intent switching is the core SS13 paradigm — if Help ever reaches a handler as
Harm, the game is broken. The ghost gate is what keeps dead players from touching the round.

---

## Porting notes

### Constants (verbatim)

| Constant | Value | Upstream source |
|---|---|---|
| `Validations.InteractionDistance` | `1.5f` | `PlayerScript.INTERACTION_DISTANCE` |
| `Validations.InteractionDistanceExtended` | `1.75f` | `PlayerScript.INTERACTION_DISTANCE_EXTENDED` |
| `Validations.TelekinesisInteractionDistance` | `15f` | `Validations.TELEKINESIS_INTERACTION_DISTANCE` |

### Upstream quirks preserved deliberately

- The target-in-storage branch of `CanApply` **replaces** the reach checks (`else if` chain)
  rather than falling through — an item in someone else's backpack is unreachable at any range.
- `InventoryApply.IsToHandSlot` checks ownership of **FromSlot** while checking hand-ness of
  TargetSlot, exactly as upstream.
- Static-type dispatch in `Default<T>` (see FC-4).
- `BodyPartType.Ears` and `LeftHand` share value 9 upstream; preserved.
- The client assumes remote performers are conscious and hidden clients defer container checks
  to the server (the IF2 optimistic-prediction model).

### Deviations (all documented in code)

- **World-space positions at the boundary.** Upstream passes matrix-local positions and converts
  through the performer's matrix (`.ToWorld(matrix)`); the matrix system is engine-side, so
  `PositionalHandApply.TargetPosition`, `AimApply.TargetPosition` and `CanApply(targetPosition:)`
  take world space. The Godot adapter converts before calling in. Upstream's several position
  sources (RegisterTile client/server, transform, physics OfficialPosition) coalesce into
  `IWorldEntity.GetWorldPosition(side)` + `AssumedWorldPositionServer`.
- **Fail closed instead of NRE.** Upstream would throw on a null target in the Telekinesis
  branch and on null dropped/target objects in the MouseDrop check; the port returns `false`.
- **Singletons → injected context.** `Default<T>` and the validations take `IInteractionWorld`;
  `ByLocalPlayer` factories take `IClientInteractionContext`. Signatures otherwise match.
- **`Invalid` sentinels are public** (private upstream) so callers and tests can detect them.
- **Deferred to later waves** (their `Default` branches fail closed until then): `TileApply`
  (tile system), `ConnectionApply` (wiring), `ContextMenuApply`, `AiActivate` (AI camera
  linecasts), and the `Validations` helpers for item traits, stacking, slot fit and stripping
  (inventory/health waves). `Mind`/account identity on `Interaction` is deferred to the mind
  system wave; ghost-gating meanwhile lives in `IClientInteractionContext.IsGhosting`.
- `PositionalHandApply.TargetVector` became `GetTargetVector(NetworkSide)` since the port keeps
  one position per network side.
