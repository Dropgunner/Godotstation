# Fidelity Contract: Shared Round Start Gate

## Source

- Unitystation commit: `c911581e8354958d664b1e32087053ce43638790`
- `UnityProject/Assets/Scripts/US13/Messages/Client/NewPlayer/ClientRequestJobMessage.cs`
- `UnityProject/Assets/Scripts/US13/Systems/GameModes/GameMode.cs`
- `UnityProject/Assets/Scripts/US13/Managers/GameManager.cs`

Unitystation has a pre-round state before `RoundState.Started`. The authoritative job
request handler rejects requests unless the shared round state is `Started`; round startup
is a server-controlled transition. This contract implements only that observable boundary,
not a countdown, round rules, objectives, or end conditions.

## Godot contract

1. A newly hosted shared round begins in `PreRound` and has no spawned crew avatars.
2. Only the server host (peer 1 in this LAN prototype) may change the phase to `Started`.
3. A second start attempt or a non-host start request cannot change the phase.
4. Crew spawn/job requests are rejected during `PreRound` and handled only after `Started`.
5. Connected clients learn the authoritative phase on join; clients already connected receive
   the start transition and then request their selected role.
6. A headless smoke-only host may auto-start to keep deterministic integration checks
   unattended; interactive play requires the host to press **Start shift**.

## Verification

- Pure C# tests cover initial phase, host authorization, transition, and repeated start.
- `tools/multiplayer_smoke.sh` verifies the start transition precedes both role spawns and
  movement replication.
