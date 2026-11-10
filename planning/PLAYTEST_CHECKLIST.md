# Two-Player Playtest Checklist

This is the hands-on test the roadmap gate needs: two people play one full round in an exported build with ordinary controls. Automated checks cover the rules and the network transport. They cannot tell whether the game is playable.

## Get the build

1. On GitHub, open the repository's **Actions** tab and pick the latest **Playtest build** run on `main`.
2. Download the artifact for your system from the run's **Artifacts** section. You need to be signed in to GitHub.
   - **Windows:** unzip twice (GitHub wraps the zip) and run `Godotstation.exe`.
   - **macOS:** unzip twice and open `Godotstation.app`. The app is not notarized. The first time, right-click it and choose **Open**, or run `xattr -dr com.apple.quarantine Godotstation.app` in Terminal.
   - **Linux:** extract the `.tar.gz` and run `./Godotstation.x86_64`.
3. Both players need the same build.

## Connect

1. Each player picks a **LAN crew role**. Use Engineer for the host and Assistant for the guest.
2. The host chooses **Host Station**. The game listens on UDP port 7777. Allow it through the firewall if asked.
3. The guest enters the host's local IP address (shown on the host's HUD) and chooses **Join Station**. On one machine, use `127.0.0.1`.
4. When both are connected, the host presses **Start shift**.

## Controls

| Action | Control |
| --- | --- |
| Move | WASD |
| Use the active hand on something, or pick it up | Left-click |
| Switch active hand | Tab |
| Drop the active hand's item | Q |
| Choose intent: help, disarm, grab, harm | 1, 2, 3, 4 |
| Examine | Shift + left-click |
| Pull a crew member or loose cart, or let go | Ctrl + left-click (H also lets go) |
| Speak | Type in **Speak locally…** and press Enter |

## The round to play

Tick each step that works for both players. Note anything confusing, slow or broken, even when the step passes.

1. **Join and spawn.** Both players see two crew members with the right roles.
2. **Move.** Walls stop you. Each player sees the other move smoothly.
3. **Speak.** A message reaches the other player nearby. It does not reach them behind a wall.
4. **Examine.** Shift-click the wrench and the other player. The description appears in your chat log only.
5. **Pick up and use.** Pick up the wrench, click the cart to unanchor it, and push it. Ctrl-click the loose cart and walk; it follows.
6. **Engineer access.** The Assistant is refused at the Engineering airlock. The Engineer opens it.
7. **Engineer power task.** Pick up fuel, click the generator twice to load it, press Tab, click the generator to start it, then click the storage unit. The APC lamp lights for both players. Wait for the outage, then load the reserve fuel and restart. The lamp recovers.
8. **Construction.** Pick up the metal sheets, click the girder, and stay close for four seconds. Both players see a wall.
9. **Injury.** One player holds the wrench, presses 4 for harm, and hits the other. Health drops by 5 per hit on the victim's HUD. At 0 the victim collapses, can only crawl and whisper, and cannot use their hands.
10. **Rescue.** Ctrl-click the casualty and drag them to Medical. Pick up a bruise pack and click them. After two seconds they stand up.
11. **Death and ghost.** Repeat the injury without treating it, or keep hitting. The victim dies. When they press a movement key, they leave the body as a ghost, pass through walls, and hear nearby speech. The living player cannot see the ghost or the ghost's messages.
12. **End and restart.** The host presses **End shift**. Both players get the report, including crew condition. The host presses **Restart shift** and then **Start shift**. Both players spawn into a fresh station.

## What to report

- The build you used (the commit shown on the Actions run) and each player's system.
- Which steps failed or felt wrong, and what you expected instead.
- Any moment you did not know what to do next. Those matter as much as bugs.

Record the result in the "Outcome ledger" table in [`UNITYSTATION_PARITY_ROADMAP.md`](UNITYSTATION_PARITY_ROADMAP.md). The gate closes only when every step passes for two people.
