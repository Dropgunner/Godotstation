# Wave 9 Multiplayer Station Playtest

## What this build tests

This is the first real two-peer Godotstation playtest slice: a small 2D station map, crew
characters, WASD movement, room-wall collision, LAN host/join with server-resolved movement,
and locally routed in-world speech. It also includes one shared wrench: a crew member can pick
it up, use it to unanchor the service cart, push the loose cart, and drop the wrench for another
nearby crew member.
Players can choose an Assistant or Engineer crew role; the server assigns the selected job
before spawning the avatar and displays the job on both clients. This establishes role-aware
occupancy of one shared station space, with local speech routed by hearing range and walls.
The wrench is a single-item interaction fixture, not a general inventory. This build does not
yet provide job duties, broader interaction intents, atmospherics, antagonist, or complete round
systems, and it is not full Unitystation gameplay parity.

## Start a local two-instance playtest

1. Run the exported macOS debug app, or build from the project with Godot .NET and matching
   export templates.
2. Choose a **LAN crew role**. For a varied smoke test, select Engineer on the host and
   Assistant on the joining client.
3. Open two instances. In the first, choose **Host Station**. It listens on UDP port 7777
   and waits in pre-round with no crew spawned.
4. In the second, enter `127.0.0.1` and choose **Join Station**. Once both are connected, the
   host presses **Start shift**.
5. Each instance should then show two crew characters with the server-confirmed jobs. Use WASD
   to move; walls and room
   boundaries should stop movement. Both players should see movement from the other instance.
6. Type a short message into **Speak locally…** and press Enter. The other crew member should
   see it while nearby with an unobstructed path; the server does not send it to distant or
   wall-separated crew.
7. For a second Mac on the same LAN, use the host Mac's local IPv4 address instead of
   `127.0.0.1`. Allow UDP port 7777 through the host firewall if macOS asks.
8. For the interaction check, click the yellow wrench, click the cart,
   push it with WASD, then press Q to drop the wrench. Both clients should see the cart become
   loose and the wrench return to the floor.
9. Use **Return to lobby** to leave the playtest.

Command-line smoke mode (used by the repository test script):

```sh
Godot --headless --path game -- --playtest-host --playtest-jobid=20 --playtest-smoke-move
Godot --headless --path game -- --playtest-join=127.0.0.1 --playtest-jobid=2 --playtest-smoke-move
```

`tools/multiplayer_smoke.sh` starts both processes and checks that the host assigns Engineer
and the client Assistant, both avatars are replicated with those roles, and server-resolved
movement reaches the client.
`tools/multiplayer_smoke.sh --interactions` checks the remote pickup, wrench use, drop, and
authoritative snapshots. A local macOS app can be exported with
`GODOT_BIN=/path/to/Godot bash tools/export_playtest_macos.sh /path/to/Godotstation.app`.

## Feedback to collect

- Did both players appear, and was it obvious which character you controlled?
- Did movement feel responsive on host and client?
- Did any wall or doorway block you unexpectedly?
- Did the host/join and address instructions work without help?
- What is the first interaction or station system you expected to use?

Record host/client operating systems, build commit, connection method (same machine or LAN),
and any failure steps. Do not record player names or contact details.
