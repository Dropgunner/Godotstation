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
- Integrated Wave 9 slices now cover server-gated role spawn, local speech, a shared wrench/cart,
  metal pickup and timed girder plating, Engineer equipment/access, and client click targets
  driven by replicated entity state. The power loop adds an Engineer fuel/generator/storage/APC
  lamp failure-and-recovery task, verified by a two-peer smoke. It is an isolated playtest model,
  not a station grid or source-game rate model. A survival slice adds one brute pool per crew
  member with Unitystation conscious-state thresholds, harm-intent unarmed hits through the
  dispatcher, server-side movement/click refusal while unconscious or dead, replicated crew status,
  and a casualty count in the round report, verified by a two-peer smoke
  ([FidelityContract_SurvivalConsciousness.md](FidelityContract_SurvivalConsciousness.md)). It is
  playtest-scaled (25 damage per hit) with no body parts, healing, ghosts, or crawling.
  Mode-driven round outcome/report, broader inventory/intent, full machinery parity, and a
  two-person ordinary-controls gate remain open. The [bounded SS13 source comparison](SS13_ESSENCE_REA.md) uses tgstation as the gameplay
  criterion and Unitystation as the porting bridge.

## Delivery workflow and outcome ledger

1. Merge a dependent PR chain in order and verify each merge. Build one app from the
   integrated code commit before starting the next feature slice. Open PRs are not delivery.
2. Exercise the player path with ordinary controls as well as automated checks: two people
   join, move, speak, pick up equipment, complete an Engineer task, see the shared result,
   and end or restart. Log observed bugs, then fix the narrow architecture boundary they expose.
3. Use .NET tests to prove rules, a two-client smoke to prove transport, and the exported app
   to assess playability. Jev is for a remaining bounded decision only. Review new subsystem
   boundaries more deeply than mechanical edits.
4. Record the integrated build, new player action, full-round gate, and playtest bugs here.

| Outcome | Current evidence | Status |
| --- | --- | --- |
| Integrated build | `c1d2b64dc77ce4fb1f276ca782714a7b903c43d1` exported as `/private/tmp/Godotstation-Integrated-c1d2b64-Playtest.app`; universal runtime bundle passed headless startup. 2026-11-05: `543fab1` (PR #45 stacked on #44, not integrated) cross-exported on Linux with an ad-hoc signature as `Godotstation-543fab1-Playtest-macOS.zip` (universal binary, both C# runtimes present); not yet launched on a Mac. 2026-11-06: `83313c9` (adds ghost observer and disarm) cross-exported the same way as `Godotstation-83313c9-Playtest-macOS.zip`; not yet launched on a Mac. | Built; manual full-round gate open |
| New player action | Wrench/cart and construction interactions remain; Engineer can also fuel/start a generator, enable storage output, observe APC lamp outage, and recover power. Two-peer smoke passed. | Integrated in PRs #32 and #37; ordinary-control two-player path unverified |
| Full round | Host can start, end, and restart a shared shift; Engineer can plate a girder, access the engineering airlock, and complete the scripted power failure/recovery loop. End sends both peers a fact-only report of tracked crew and station state, now including crew casualties. Harm intent (key `4`) on a crew body applies 25 brute per hit; at 125/175/225 brute the body is BarelyConscious/Unconscious/Dead on every peer, and the server refuses the incapacitated peer's movement and clicks. `tools/multiplayer_smoke.sh --survival` passed on `534f4e2` (2026-11-04, Linux, Godot 4.4.1 mono); restart rebuilds fresh bodies. | **Open:** no mode-specific outcome/report, no revival path (on the 2026-11-05 branch `claude/project-thread-vq7vru`, bruise-pack treatment of unconscious crew passed `--recovery` and the ghost observer passed `--survival`), full power grid or machinery parity, or two-person ordinary-controls pass (kit: [PLAYTEST_KIT_Survival.md](PLAYTEST_KIT_Survival.md)) |
| Bugs found during play | A client could click stale construction targets; the HUD covered the station after spawn. | Fixed in PRs #32 and #33; repeat human playtest needed |

The export script can also cross-export from Linux: it then signs ad-hoc and skips the macOS-only
startup check, printing `MACOS_PLAYTEST_STARTUP_SKIPPED`. The export script now uses the tracked game solution, creates a
temporary unsigned debug preset only when no local preset exists, refuses to overwrite an
existing app, and validates the universal C# bundle with a headless startup. Godot still reports
a temporary publish-directory cleanup exception after writing the app. The repository's test
project targets .NET 10; the installed .NET 8 SDK cannot run that target directly. A temporary
retarget ran 255 tests successfully with 8 skipped, then restored the project file. These
environment and exporter limitations remain visible; the manual full-round gate is still open.

## Dependency-ordered milestones

### P0 — Shared shift lifecycle and crew roles

Connect lobby, server-owned round start, job preference/assignment, spawn, late join,
disconnect cleanup, round end, and restart for multiple peers. Job requests and capacity
must be authoritative; clients must receive clear rejection and state updates. Keep the
existing offline round as a separate development path until the shared path replaces it.

**Exit evidence:** two clients select/request roles, see server-confirmed roles and distinct
spawns, handle a full role or not-ready rejection, and can finish/reset the same round without
relaunching. Tests prove assignment/capacity and lifecycle transitions.

**Completed slices:** host-controlled pre-round/start gate, authoritative spawn gating,
and a host-driven end/restart that resets the shared world while clients stay connected
are covered by [`FidelityContract_SharedRoundStart.md`](FidelityContract_SharedRoundStart.md).
A fact-only report of the tracked prototype state is covered by
[`FidelityContract_RoundEndReport.md`](FidelityContract_RoundEndReport.md). Mode-driven
outcomes, sourced objective/antagonist reports, and manual full-round evidence remain open.

### P1 — Shared-world speech and social feedback

Add server-routed in-world speech and action feedback. Start with local speech range and
wall/door line-of-sight, sourced from `ChatRelay`; keep channel identity explicit so radio,
system, combat, and action messages can be added without treating all text as global chat.

**Exit evidence:** two clients can speak; a nearby unobstructed crew member hears it, a
distant or wall-separated crew member does not, and both can see appropriate action/system
feedback. Deterministic tests cover routing boundaries.

**Completed slice:** local player speech now routes by the source-backed 14-tile default
hearing range and station-wall line of sight under
[`FidelityContract_LocalSpeech.md`](FidelityContract_LocalSpeech.md). Radios, whispers,
and Action/Combat channel integration remain open; dead-speaker routing and ghost listeners are covered by FC-LS-7..9 and FC-GH-3.

### P2 — SS13 interaction loop in the shared world

Connect the canonical interaction dispatcher, intent, active hand, inventory, reach checks,
and replicated world entities to the multiplayer frontend. Implement the sourced click path:
active-hand item or empty hand applied to a target; preserve first-interactable and held-item
ordering; reject unauthorized or out-of-range client commands on the server. Add pickup,
drop, examine/alt-click, and at least one useful station object interaction.

**Exit evidence:** two clients observe the same item ownership and object state; tests prove
ownership, reach, intent/handler ordering, and rejection of forged client state. A playtester
can pick up and use a tool on a nearby station object.

**Implemented slice in `codex/wave9-shared-interactions`:** the shared wrench now moves through
the engine `InventorySystem` into real left/right hand slots. Tab switches the active hand; tool
use and Q-drop consult that selected slot. The common interaction dispatcher, authoritative peer
position, reach and wall checks, and replicated snapshots remain in the path. Headless domain
tests cover slot ownership, inactive-hand rejection, drop/recovery, range, obstruction, and
disconnect cleanup. The existing two-client runtime smoke verifies shared request and snapshot
replication. A separate construction slice now uses two shared metal sheets and four seconds of
server progress to plate a girder. These are interaction foundations and one construction task,
not a complete inventory or playable SS13 round. Inventory UI/storage, broader intent behavior,
examine/alt-click, and full upstream anchor rules remain open.

### P3 — One complete crew job loop

Choose one real Unitystation job and follow its source-backed responsibilities, equipment,
access, and station machinery through a complete task. Prefer an engineering/power or
atmospherics loop only after tracing the actual upstream machine and job behavior. Do not use
the current generic wrench-relay objective as a stand-in for Unitystation gameplay.

**Exit evidence:** a selected role spawns with the right access/equipment; players use real
station objects and the existing engine systems; shared state changes are visible and
recoverable; tests cover the domain outcome; a two-client playtest can complete the task.

**Implemented slices:** the playtest has a source-grounded girder-plating task: pick up two
metal sheets, hold them in the active hand for a four-second server action, and replicate the
resulting wall to both clients. The Engineer spawn also receives the nine source-mapped named
slots and ID clearances; an Assistant cannot open the shared engineering airlock, while an
Engineer with Engine clearance can. This establishes role equipment and one access-controlled
station object, but does not close P3: functional tool/equipment behavior, power machinery,
broader construction, and a complete engineering objective remain open. See [Fidelity Contract:
Girder Plating](FidelityContract_GirderPlating.md) and [Fidelity Contract: Engineer Loadout and
Engineering Airlock](FidelityContract_EngineerLoadoutAccess.md). The next vertical slice should
restore an observable shared station power dependency as scoped in
[SS13 gameplay reconstruction](SS13_ESSENCE_REA.md), rather than add another isolated task.

### P4 — Survival, incapacitation, and recovery

Connect health/status changes, harm/disarm/grab/help intent outcomes, death, ghost
observation, and any recovery path that the traced Unitystation mode provides. Keep all
outcomes server authoritative and preserve the existing interaction contracts.

**Exit evidence:** two clients can trigger and observe sourced outcomes; dead/ghost players
cannot act on the physical world; tests cover state transitions and interaction restrictions.

**Completed slice (2026-11-04, PR #44, branch `wave10/survival-consciousness`):** one brute
pool per crew member with Unitystation's health thresholds, harm-intent unarmed hits resolved by
the shared dispatcher, hand items dropped on falling, server-side refusal of movement and clicks
while unconscious or dead, replicated crew status on every peer, casualties in the round report,
and fresh bodies on restart. Automated evidence: 33 clause tests and the `--survival` two-peer
smoke. Follow-up slice (2026-11-05): a dead crew member's speech is carried on the Ghost
channel and reaches only dead crew, and dead crew stop hearing living Local speech (FC-LS-7/8,
`IncapacitatedSpeechTests`, `--survival` smoke). A second follow-up adds the first recovery
path: a six-use bruise pack applied on help intent heals 40 brute, so an unconscious crew member
can be treated back to Conscious and moving (FC-HL-1..5, `BruisePackHealingTests`, `--recovery`
smoke). A third follow-up adds the ghost observer: a dead player's move leaves the body as a ghost
that flies through walls at 8 tiles/s, hears all Local speech, and cannot interact (FC-GH-1..5,
`GhostObserverTests`, `--survival` smoke). A fourth follow-up (2026-11-06) adds disarm intent: 5 %
knockdown for 6 s, otherwise 50 % to drop both hands, 0.5 s apart (FC-DA-1..4, `DisarmTests`,
`--disarm` smoke). Still open for P4: grab/pull, help-up, crawling,
self-treatment, revival of the dead, ghost re-entry/orbit/roles, the crit-speech question in FC-LS-9, and the
human ordinary-controls pass.

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
