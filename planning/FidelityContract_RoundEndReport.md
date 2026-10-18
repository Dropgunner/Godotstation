# Fidelity Contract: Shared Round-End Report

**Status:** implemented for the tracked LAN playtest state; mode-specific outcomes and full SS13 reports remain open.

## Source behavior

- At pinned `/tg/station` commit [`f37d36c`](https://github.com/tgstation/tgstation/tree/f37d36c295c4bf991b72647281352f3bd471b335), the ticker ends play when the selected mode or a round-ending condition finishes, then calls `declare_completion`. The completion procedure gathers round-end feedback, displays a report, sets the dynamic mode result, and proceeds to reboot. [`ticker.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/controllers/subsystem/ticker.dm#L170-L180), [`roundend.dm`](https://github.com/tgstation/tgstation/blob/f37d36c295c4bf991b72647281352f3bd471b335/code/__HELPERS/roundend.dm#L214-L258)
- At pinned Unitystation commit [`c911581`](https://github.com/unitystation/unitystation/tree/c911581e8354958d664b1e32087053ce43638790), `GameManager.EndRound` validates the round ID and server authority, sets the round to ended, calls the current `GameMode.EndRoundReport`, broadcasts the end event, and schedules a restart. The base report invokes antagonist and station-objective reports and announces the restart delay. [`GameManager.cs`](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Managers/GameManager.cs#L670-L768), [`GameMode.cs`](https://github.com/unitystation/unitystation/blob/c911581e8354958d664b1e32087053ce43638790/UnityProject/Assets/Scripts/US13/Systems/GameModes/GameMode.cs#L534-L570)

These sources establish that round completion is paired with a report; they do not imply that the prototype's manual host end is a sourced game-mode condition.

## Godot contract

1. Only the host can end a started shift, as already enforced by `SharedRoundLifecycle`.
2. At that transition, the host snapshots the round ID, elapsed time, assigned crew roles, generator/storage/APC-lamp state, engineering airlock state, and girder/material state from authoritative runtime objects.
3. The report is deterministic for a given snapshot, uses invariant numeric formatting, and is delivered to both connected clients. A late joiner to an ended shift receives the same cached report with the phase snapshot.
4. Restart clears the displayed and cached report before the next shift begins.
5. The report describes only modeled facts. It explicitly leaves game-mode victory, antagonist outcomes, evacuation, survival, and deaths unreported because those systems are not implemented.

## Verification and remaining gaps

- `RoundEndReportTests` covers exact tracked values, deterministic role ordering/formatting, absence of invented outcomes, and invalid input rejection.
- `tools/multiplayer_smoke.sh --round-restart` checks that host and client receive the report before both observe a clean restart.
- The current end control is still host-requested. There is no selected sourced round mode, automatic end condition, shuttle, antagonist/objective outcome, survival/death accounting, or full `/tg/station` report. A successful report smoke is not a hands-on round-playability or parity claim.
