# Fidelity Contract: Multiplayer Station Playtest Foundation

## Goal and boundary

This milestone turns the offline prototype into a small, real-time 2D multiplayer playtest:
two peers share a station-shaped map, see each other's crew characters, and move through the
map. The existing `FidelityContract_Movement.md` remains authoritative for movement speeds,
tile collision, momentum, and movement-state restrictions. This contract covers only the new
network boundary and the walkable test layout. It does not claim full Unitystation parity.

## Rules

1. **Server-owned movement:** clients send directional intent; the server resolves movement
   through `MovementProcessor` and publishes resulting positions. Clients cannot submit world
   positions or move another peer's character.
2. **Input ownership:** a movement command is accepted only when the transport sender matches
   the character's owning peer ID. Each directional component is limited to -1, 0, or 1.
3. **Shared collision layout:** host and clients render the same bounded corridor/room layout
   used to construct the server's `CollisionMap`. Boundary and room walls block movement.
4. **Disconnect cleanup:** a departed peer's character and its non-pushable movement obstacle
   are removed so it cannot leave an invisible blocker.
5. **Two-peer playtest:** a host and at least one client can join a round; both characters are
   visible and movement from each peer is reflected on the other peer.

## Verification

- Headless tests cover command ownership/range, the map's open routes and wall boundaries, and
  removal of a disconnected character obstacle.
- The local multiplayer smoke script launches a host and a joining client, checks both peers
  spawned, and checks that server-resolved movement was broadcast.
- Godot startup smoke verifies the scene scripts and station scene load.

## Known limits for this milestone

LAN host/join only; no internet matchmaking, encryption, anti-cheat, reconnect/session recovery,
prediction/reconciliation, combat, job assignment, inventory interactions, atmos simulation,
or objectives. A follow-up must trace the intended Unitystation source before adding those
behaviors.
