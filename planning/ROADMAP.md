# Godotstation Roadmap

## Shipped foundations

- **Waves 1–5:** Interaction, movement, atmospherics, inventory, and the first Godot
  presentation slices.
- **Wave 6:** Stack support and HUD stack counts.
- **Wave 7:** Station atmosphere and fire visualization.

These waves are present on `main`. A local macOS debug package was also produced, but it
does not yet represent a release build or a complete round-based game.

## Wave 8 — playable single-player round

**Goal:** turn the current systems demo into one short, repeatable round that a player can
start, play, finish, and restart.

**Player path:** entry screen → choose an available job → spawn into one small station area
→ complete one objective → see success or failure → restart into a fresh round.

**Scope:** offline single-player; one test area; one job or a small fixed set; one deterministic
objective with visible progress; explicit terminal result and reset. Reuse the existing
lobby/job-selection UI and authoritative engine systems. Add a fidelity contract before
introducing any new backend outcome rules. Keep simulation decisions in the headless engine
and rendering/input in `game/`.

**Acceptance checks:**

1. A clean checkout builds and passes the complete test suite and project preflight.
2. The configured Godot entry scene loads headlessly without missing-resource or script errors.
3. A deterministic engine test proves objective progress, success/failure resolution, and
   round reset to initial state.
4. A smoke path selects a job, spawns the player, completes the objective, displays the result,
   and restarts without relaunching the application.
5. The same smoke path can be repeated after restart; no state from the prior round leaks into
   the new round.
6. Any missing art, sound, fidelity source, or export limitation is recorded as a specific
   follow-up rather than hidden behind placeholder behavior.

**Deferred:** multiplayer and production networking; broad job/crew systems; multiple maps or
objectives; economy/progression; full release signing, notarization, and distribution.

## Wave 9 — multiplayer station playtest foundation

The Wave 8 protocol exposed a gap in the prototype: it is an offline systems demo, not a
playtest for the intended 2D multiplayer Space Station 13-style game. The next playtest must
let two people join one round, see a station map, and move their own crew characters around it.

**Goal:** deliver a host-and-join LAN slice with server-resolved character movement on a small
walkable station layout. Preserve Wave 8's offline round as a separate demo path.

**Acceptance checks:**

1. The lobby offers host and join-by-address paths, with connection status and useful errors.
2. Two Godot instances can join one host; each sees both distinct crew characters and movement
   from either peer updates on the other instance.
3. WASD movement is resolved on the server by the existing headless movement processor; solid
   room boundaries block movement on host and clients.
4. The station layout is readable at launch and includes a corridor, rooms, walls, and visible
   spawn points; no placeholder relay objective is required to evaluate locomotion.
5. Deterministic tests cover network input authorization and station-map collision layout;
   preflight, full .NET tests, and Godot startup smoke checks pass.
6. The playtest instructions explain how to host locally and join over LAN and record known
   limits. This milestone does not claim full Unitystation parity.

**Scope boundary:** no combat, jobs/crew systems, inventory interactions, atmos simulation,
round objective, internet matchmaking, or production server hardening. These remain follow-up
work and must be sourced from Unitystation before implementation.

## Gameplay parity beyond Wave 9

Wave 9 is a locomotion/network foundation only. It does not meet the game's intended SS13
playtest target. Continue against the dependency-ordered milestones in
[`UNITYSTATION_PARITY_ROADMAP.md`](UNITYSTATION_PARITY_ROADMAP.md): shared shift lifecycle and
crew roles, spatial in-world speech, SS13 item/intent interactions, one source-backed job
loop, survival/recovery, and round modes. Player playtests should validate and refine these
systems, not decide whether core SS13 gameplay belongs in the project.

Do not call a build a Unitystation-style playable build until it passes the two-client gate
in that roadmap. Keep the current LAN movement smoke as evidence for networking/locomotion
only.

## Release track

- **Release track:** define supported macOS/Windows/Linux targets, reproducible exports, signing,
  and distribution requirements before calling a build release-ready.
