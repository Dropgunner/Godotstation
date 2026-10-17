# Unitystation Gameplay Parity Roadmap

## Target

Godotstation is a 2D multiplayer remake of Unitystation, itself a Space Station 13
experience. A successful playtest must feel like a shared station shift: players join a
round, take crew roles, move around a station, communicate, manipulate people and objects
through SS13-style interactions, operate job-relevant station systems, and experience the
round consequences together. A map with movable avatars is a prerequisite, not a playable
SS13 loop or a parity milestone.

“Parity” means matching the observable rules and player loop of the selected Unitystation
systems, with a fidelity contract and tests for each one. It does not mean copying Unity
implementation structure into Godot. Before behavior is implemented, record the exact
upstream source path and commit and the Godot contract clauses that describe it. Resolve
ambiguous behavior from source rather than inventing a substitute objective.

## Upstream behavior anchors

These references are pinned to Unitystation commit
[`c911581e8354958d664b1e32087053ce43638790`](https://github.com/unitystation/unitystation/tree/c911581e8354958d664b1e32087053ce43638790):

- [GUI_PlayerJobs.cs](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/UI/Systems/Jobs/GUI_PlayerJobs.cs)
  lets a player request a job, waits for the server response/spawn, and surfaces job
  request failures such as a filled position or a round that is not ready.
- [HandApply.cs](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Core/Input%20System/InteractionV2/Interactions/HandApply.cs)
  defines a click as applying the active-hand item (or an empty hand) to the target, carrying
  intent, active hand slot, target body part, and alt-click state. The existing Godot
  interaction fidelity contracts cover several of these rules, but the multiplayer
  playtest does not yet connect them to shared-world entities.
- [ChatRelay.cs](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Core/Chat/ChatRelay.cs)
  routes local speech according to player distance and wall line-of-sight, with distinct
  chat channels. A multiplayer station without in-world communication misses a core social
  part of the experience.
- The [Unitystation multiplayer test guide](https://github.com/unitystation/unitystation/wiki/Building-And-Testing)
  describes testing two instances and exercising chat, combat, dropping items, opening
  objects, and movement.

These are starting anchors, not a complete inventory of Unitystation. Each milestone must
trace additional upstream systems it actually implements.

## Current state

- Waves 1–8 provide headless movement, interaction, inventory, atmospherics, presentation,
  and a small offline objective round.
- Wave 9 adds a station-shaped map, crew avatars, movement, and LAN host/join with
  server-resolved position updates.
- The Wave 9 playtest now has networked job selection, capacity checks, and spawn gating for
  two sourced crew roles. It still has no in-world chat, shared item pickup/use, job-specific
  station task, or connected round lifecycle. Role assignment does not yet make the role's
  job duties playable; the build remains useful primarily for checking the multiplayer
  movement foundation.

## Dependency-ordered milestones

### P0 — Shared shift lifecycle and crew roles

Connect lobby, server-owned round start, job preference/assignment, spawn, late join,
disconnect cleanup, round end, and restart for multiple peers. Job requests and capacity
must be authoritative; clients must receive clear rejection and state updates. Keep the
existing offline round as a separate development path until the shared path replaces it.

**Exit evidence:** two clients select/request roles, see server-confirmed roles and distinct
spawns, handle a full role or not-ready rejection, and can finish/reset the same round without
relaunching. Tests prove assignment/capacity and lifecycle transitions.

### P1 — Shared-world speech and social feedback

Add server-routed in-world speech and action feedback. Start with local speech range and
wall/door line-of-sight, sourced from `ChatRelay`; keep channel identity explicit so radio,
system, combat, and action messages can be added without treating all text as global chat.

**Exit evidence:** two clients can speak; a nearby unobstructed crew member hears it, a
distant or wall-separated crew member does not, and both can see appropriate action/system
feedback. Deterministic tests cover routing boundaries.

### P2 — SS13 interaction loop in the shared world

Connect the canonical interaction dispatcher, intent, active hand, inventory, reach checks,
and replicated world entities to the multiplayer frontend. Implement the sourced click path:
active-hand item or empty hand applied to a target; preserve first-interactable and held-item
ordering; reject unauthorized or out-of-range client commands on the server. Add pickup,
drop, examine/alt-click, and at least one useful station object interaction.

**Exit evidence:** two clients observe the same item ownership and object state; tests prove
ownership, reach, intent/handler ordering, and rejection of forged client state. A playtester
can pick up and use a tool on a nearby station object.

### P3 — One complete crew job loop

Choose one real Unitystation job and follow its source-backed responsibilities, equipment,
access, and station machinery through a complete task. Prefer an engineering/power or
atmospherics loop only after tracing the actual upstream machine and job behavior. Do not use
the current generic wrench-relay objective as a stand-in for Unitystation gameplay.

**Exit evidence:** a selected role spawns with the right access/equipment; players use real
station objects and the existing engine systems; shared state changes are visible and
recoverable; tests cover the domain outcome; a two-client playtest can complete the task.

### P4 — Survival, incapacitation, and recovery

Connect health/status changes, harm/disarm/grab/help intent outcomes, death, ghost
observation, and any recovery path that the traced Unitystation mode provides. Keep all
outcomes server authoritative and preserve the existing interaction contracts.

**Exit evidence:** two clients can trigger and observe sourced outcomes; dead/ghost players
cannot act on the physical world; tests cover state transitions and interaction restrictions.

### P5 — Round identity and replayable social chaos

Add the selected round mode's crew/antagonist or other role objectives, round announcements,
and end conditions based on Unitystation source. Then expand jobs, departments, station
systems, and content by source-backed slices. This is where the project moves beyond one
job-specific vertical slice toward broader feature parity.

**Exit evidence:** a complete multiplayer round starts from the lobby, supports its sourced
roles and objectives, reaches a valid round outcome, and resets cleanly. Maintain a published
parity matrix for each system rather than declaring parity from one successful build.

## Playtest gate for calling the game playable

A build qualifies as an SS13-style multiplayer playtest only when two separate clients can:

1. Join one shared round, choose/request crew roles, and spawn into a recognizable 2D station.
2. Move and communicate in-world with appropriate local audibility.
3. Pick up and use an item on a world object using sourced intent, reach, and hand rules.
4. Participate in at least one source-backed job task that changes shared station state.
5. Observe the result of their actions, including a meaningful failure/incapacitation path,
   and finish or restart the round.

Every item needs deterministic engine/network tests plus a documented two-client smoke path.
An exported app is a delivery artifact; it is not evidence of gameplay parity by itself.
