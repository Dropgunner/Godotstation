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

## Later roadmap, pending Wave 8 results

- **Wave 9:** deepen one game loop based on playtest evidence (additional objectives, hazards,
  or failure recovery).
- **Wave 10:** harden multiplayer boundaries and session flow only after the offline round is
  stable.
- **Release track:** define supported macOS/Windows/Linux targets, reproducible exports,
  signing, and distribution requirements before calling a build release-ready.
