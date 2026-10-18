# Fidelity Contract: Shared Round Lifecycle

## Source

- Unitystation commit: `c911581e8354958d664b1e32087053ce43638790`
- [`ClientRequestJobMessage.cs`](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Messages/Client/NewPlayer/ClientRequestJobMessage.cs)
- `UnityProject/Assets/Scripts/US13/Systems/GameModes/GameMode.cs`
- [`GameManager.cs`](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Managers/GameManager.cs)

Unitystation has a pre-round state before `RoundState.Started`. The authoritative job
request handler rejects requests unless the shared round state is `Started`; round startup
is a server-controlled transition. At the same pinned commit, `GameManager.EndRound` is
server-only, checks the current round ID, enters `Ended`, broadcasts the end event, and
waits before restart. `RestartRound` is server-only, enters `Restarting`, increments the
round ID, and performs cleanup. Source rechecked 2026-10-18. These are confirmed source
behaviors; no upstream issue or PR cause is established for Godotstation's missing LAN
restart. The old adapter had no end/restart transition.

This slice implements a host-driven end and fresh round in one LAN session. It does not
port Unitystation's mode-specific end conditions, report, vote, timer, shuttle, or
process/scene restart behavior.

## Godot contract

1. A newly hosted shared round begins in `PreRound` and has no spawned crew avatars.
2. Only the server host (peer 1 in this LAN prototype) may change the phase to `Started`.
3. A second start attempt or a non-host start request cannot change the phase.
4. Crew spawn/job requests are rejected during `PreRound` and handled only after `Started`.
5. Connected clients learn the authoritative phase on join; clients already connected receive
   the start transition and then request their selected role.
6. A headless smoke-only host may auto-start to keep deterministic integration checks
   unattended; interactive play requires the host to press **Start shift**.
7. Only the host can end a started shift. Both peers stop movement and shared actions and
   see the ended phase; a late joiner learns it too.
8. Only the host can restart an ended shift. The round ID increments, roster assignments,
   avatars, inventory/world objects, construction, airlock, and collision state reset, and
   both peers return to pre-round without dropping the LAN connection.
9. The host can start the next shift; connected peers can request roles and spawn again.
   Requests carrying an earlier round ID cannot act on the new round.

## Verification

- Pure C# tests cover initial phase, host authorization, transition, and repeated start.
- `tools/multiplayer_smoke.sh` verifies the start transition precedes both role spawns and
  movement replication.
- Pure C# lifecycle tests cover host-only end/restart, invalid phases, round ID increments,
  and stale end attempts. `tools/multiplayer_smoke.sh --round-restart` completes girder
  plating in round one, verifies both peers observe the end and reset, then verifies two
  respawns and fresh world state in round two. Manual normal-controls verification
  remains required for the full playtest gate.
