# Wave 8 Smoke Test

1. Run the Godot project with `game/` as the project path.
2. On the entry screen, select **Station Engineer** and press **Start Round**.
3. Confirm the station loads with the inventory HUD, player, relay objective text, and a
   90-second countdown.
4. Click each of the three orange relay panels while holding the wrench. Confirm progress
   advances once per panel and the third action shows **STATION RESTORED**.
5. Press **Return to Job Selection**, start another round, and confirm progress is `0/3` and
   time is reset to 90 seconds.
6. For the failure path, begin a fresh round and wait until the timer reaches zero. Confirm
   **TIME EXPIRED** appears, then restart and verify the fresh values again.

Automated end-to-end check: `GODOT_BIN=/path/to/Godot bash tools/wave8_smoke.sh`. It briefly
enables the smoke runner autoload, restores `game/project.godot` on exit, selects the job in
the actual lobby scene, dispatches simulated clicks through the same player interaction method,
and checks success, timeout failure, and a clean restart after each outcome. Also run
`bash tools/preflight.sh` and a headless editor scan.
