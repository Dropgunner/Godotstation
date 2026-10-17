# Wave 9 Multiplayer Station Playtest

## What this build tests

This is the first real two-peer Godotstation playtest slice: a small 2D station map, crew
characters, WASD movement, room-wall collision, and LAN host/join with server-resolved movement.
It establishes that players can occupy one shared station space. It does not yet provide the
full Space Station 13 job, interaction, inventory, atmospheric, antagonist, or round systems.

## Start a local two-instance playtest

1. Build/run the project from Godot 4.4.1 .NET or export a local debug app.
2. Open two instances. In the first, choose **Host Station**. It listens on UDP port 7777.
3. In the second, enter `127.0.0.1` and choose **Join Station**.
4. Each instance should show two labeled crew characters. Use WASD to move; walls and room
   boundaries should stop movement. Both players should see movement from the other instance.
5. For a second Mac on the same LAN, use the host Mac's local IPv4 address instead of
   `127.0.0.1`. Allow UDP port 7777 through the host firewall if macOS asks.
6. Use **Return to lobby** to leave the playtest.

Command-line smoke mode (used by the repository test script):

```sh
Godot --headless --path game -- --playtest-host --playtest-smoke-move
Godot --headless --path game -- --playtest-join=127.0.0.1 --playtest-smoke-move
```

`tools/multiplayer_smoke.sh` starts both processes and checks that both peers spawn and that a
server-resolved position update reaches the client.

## Feedback to collect

- Did both players appear, and was it obvious which character you controlled?
- Did movement feel responsive on host and client?
- Did any wall or doorway block you unexpectedly?
- Did the host/join and address instructions work without help?
- What is the first interaction or station system you expected to use?

Record host/client operating systems, build commit, connection method (same machine or LAN),
and any failure steps. Do not record player names or contact details.
