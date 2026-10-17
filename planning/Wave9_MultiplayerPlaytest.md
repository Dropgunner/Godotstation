# Wave 9 Multiplayer Station Playtest

## What this build tests

This is the first real two-peer Godotstation playtest slice: a small 2D station map, crew
characters, WASD movement, room-wall collision, and LAN host/join with server-resolved movement.
Players can choose an Assistant or Engineer crew role; the server assigns the selected job
before spawning the avatar and displays the job on both clients. This establishes role-aware
occupancy of one shared station space, but it does not yet provide job equipment or duties,
in-world chat, shared-world interactions, inventory, atmospheric, antagonist, or round systems.

## Start a local two-instance playtest

1. Build/run the project from Godot 4.4.1 .NET or export a local debug app.
2. Choose a **LAN crew role**. For a varied smoke test, select Engineer on the host and
   Assistant on the joining client.
3. Open two instances. In the first, choose **Host Station**. It listens on UDP port 7777.
4. In the second, enter `127.0.0.1` and choose **Join Station**.
5. Each instance should show two crew characters with the same server-assigned jobs. Use WASD
   to move; walls and room
   boundaries should stop movement. Both players should see movement from the other instance.
6. For a second Mac on the same LAN, use the host Mac's local IPv4 address instead of
   `127.0.0.1`. Allow UDP port 7777 through the host firewall if macOS asks.
7. Use **Return to lobby** to leave the playtest.

Command-line smoke mode (used by the repository test script):

```sh
Godot --headless --path game -- --playtest-host --playtest-jobid=20 --playtest-smoke-move
Godot --headless --path game -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-move
```

`tools/multiplayer_smoke.sh` starts both processes and checks that the host assigns Engineer
and the client Assistant, both avatars are replicated with those roles, and server-resolved
movement reaches the client.

## Feedback to collect

- Did both players appear, and was it obvious which character you controlled?
- Did movement feel responsive on host and client?
- Did any wall or doorway block you unexpectedly?
- Did the host/join and address instructions work without help?
- What is the first interaction or station system you expected to use?

Record host/client operating systems, build commit, connection method (same machine or LAN),
and any failure steps. Do not record player names or contact details.
