# Fidelity Contract — Server-Owned Crew Job Assignment

## Source

Unitystation behavior is traced at commit
`c911581e8354958d664b1e32087053ce43638790`:

- `UnityProject/Assets/Scripts/US13/UI/Systems/Jobs/GUI_PlayerJobs.cs` sends a selected job
  preference to the server and waits for the spawn result.
- `UnityProject/Assets/Scripts/US13/Messages/Client/NewPlayer/ClientRequestJobMessage.cs`
  validates that a connected player sent the request, checks the round and remaining job
  positions, and rejects a full position instead of spawning the player.
- `UnityProject/Assets/Scripts/US13/Systems/Occupations/JobType.cs` assigns stable numeric
  identifiers (`ASSISTANT = 2`, `ENGINEER = 20`).
- `UnityProject/Assets/ScriptableObjects/Occupations/Assistant.asset` defines Assistant with
  a limit of 99. `UnityProject/Assets/ScriptableObjects/Occupations/Engineering/Engineer.asset`
  defines Engineer with a limit of 5 and responsibilities including maintaining station
  power and repairs.

This contract implements assignment and spawn gating for the LAN playtest. It does not claim
to implement Unitystation character sheets, job bans, character validation, job priority
allocation, per-map spawn points, equipment population, or full round-state validation.

## Rules

1. A joining peer requests one job by its Unitystation `JobType` identifier. The server uses
   the transport sender as the player identity; a client cannot request assignment for
   another peer.
2. The server accepts only catalogued crew jobs with an available slot. An unknown or full
   job is rejected, and the rejected peer remains unspawned.
3. A peer receives at most one assignment. Retrying an already completed request must not
   consume another slot or create a duplicate avatar.
4. A disconnected peer releases its job position. A later request can use the released slot.
5. Spawn state includes the server-assigned job identifier. Every peer displays the same job
   for each crew avatar; clients do not choose or overwrite another player's assigned role.
6. This playtest uses the upstream job IDs and position limits for Assistant and Engineer.
   The server's overall four-player network limit still applies.

## Verification

- Engine tests prove valid assignment, unknown/full rejection, one assignment per peer,
  disconnect release, and upstream job identifiers/limits.
- Multiplayer smoke proves clients do not spawn before server acceptance and both peers show
  the assigned job.
- Invalid and full-job requests produce a useful client status and do not create a character.
