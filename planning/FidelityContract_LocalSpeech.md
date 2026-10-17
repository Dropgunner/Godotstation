# Fidelity Contract: Local Speech Routing

## Source

- Unitystation commit: `c911581e8354958d664b1e32087053ce43638790`
- `UnityProject/Assets/Scripts/US13/Core/Chat/ChatRelay.cs`

`ChatRelay.PropagateChatToClients` routes Local, Action, and Combat messages by each
listener's hearing range and rejects listeners behind walls. The default local range is 14
world units when the listener has no `LocalChatRange` stat. The originator always receives
their own message. Unitystation has additional chat channels and special listeners; this
slice implements only player local speech and does not claim radio, whisper, ghost, or system
channel parity.

## Godot contract

1. The server accepts one-line speech only from an assigned crew peer in a started shift.
2. Speech is limited to 256 characters and is routed by authoritative server positions.
3. A speaker always receives their own speech. Other crew hear it only inside their hearing
   range and with a clear grid line through the station map.
4. Closed wall tiles block speech; an open corridor/door tile does not.
5. Speech stays on the local channel; it is never broadcast to every peer by default.
6. The UI renders player text without interpreting it as BBCode.

## Verification

- Engine tests cover default range boundaries, speaker echo, wall occlusion, and an open
  doorway line.
- `tools/multiplayer_smoke.sh` verifies two connected crew members each hear each other's
  locally routed speech in addition to role assignment and movement replication.
