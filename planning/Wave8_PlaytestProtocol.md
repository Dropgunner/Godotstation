# Wave 8 Player Playtest Protocol

Use this protocol to gather evidence for the Wave 9 gameplay decision. The current round is a short single-player prototype with one job, one station area, a three-panel objective, a 90-second timer, and restart after either result. Treat feedback as exploratory; record what the player does and says before suggesting changes.

## Session setup

Record the game commit, operating system, Godot version, whether this is an editor run or exported build, display resolution, and whether the player has used Godotstation before. Use a fresh launch and do not explain the objective beyond the task prompts below. The observer should avoid coaching; note any hint that was needed.

## Run A: complete and restart

1. Ask the player: “Start a round and restore the station.”
2. Observe whether they find the available job and understand how to start. Record the time and any hesitation.
3. In the station, observe whether they can locate the inventory, equip the wrench, identify the relay panels, and understand how to interact. Record the first point where they pause or ask for help.
4. Let them complete the objective. Note whether each interaction produces clear progress feedback and whether the success result is understood.
5. Ask them to return to job selection and start another round. Confirm whether they can restart without help and whether progress returns to 0/3 and time to 90 seconds.

## Run B: timeout and restart

1. Start a fresh round and ask the player to wait without completing the objective.
2. Observe whether the timer is visible and whether the failure result is clear.
3. Ask the player to return to job selection and start again. Record whether the new round appears fresh.

## Questions after play

Ask these without suggesting answers:

- What did you think the objective was when you first entered the station?
- Which action or screen was hardest to understand?
- Did the progress, timer, success, and failure feedback arrive when you expected?
- Did anything feel unresponsive or unfair?
- If one thing could change before you play again, what would you choose?

Rate each item from 1 (unclear or frustrating) to 5 (clear and comfortable): job selection, controls and interaction, objective clarity, progress feedback, result clarity, restart. Keep the player’s words in the notes.

## Session record

Copy and fill one record per player. Do not include names or contact information.

- Session ID:
- Date:
- Game commit:
- OS / Godot version:
- Editor or exported build:
- Prior familiarity:
- Run A completed without help?:
- Run B completed without help?:
- Time to start the round:
- Time to identify the objective:
- Time to complete the objective:
- Hints or retries needed:
- Ratings (selection / controls / objective / progress / result / restart):
- Observed issue (steps, expected result, actual result, severity):
- Player’s requested change:
- Observer notes:

## Using the results

Keep observations separate from interpretations. Group repeated blockers and note how many sessions showed each one. Prioritize issues that stop players from starting, interacting, understanding the result, or restarting. Use the collected evidence to select a focused Wave 9 change; do not treat a single preference as proof that a broad feature is needed. Record the tested build and any environment-specific failures so they can be reproduced.
