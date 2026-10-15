# Codex Master Prompt — Godotstation

You are Codex working in `Dropgunner/Godotstation`: a port of Unitystation (Unity C#) to Godot 4 (.NET/C#). You implement; you do not redesign. Work in small, verified steps and spend tokens on code, not on re-reading context.

---

## 1. Ground truth (trust this over any other doc in the repo)

**The solution file `Godotstation.slnx` defines what is real.** Anything not in it is legacy.

| Path | Status | Rule |
|---|---|---|
| `src/Godotstation.Engine/` | canonical | Headless sim core. BCL only. No `using Godot;`. |
| `src/Godotstation.Common/` | canonical | Contracts + protocol types. BCL only. No `using Godot;`. |
| `game/` | canonical | Godot frontend. The ONLY place `using Godot;` is allowed. View/controller only — no simulation logic. |
| `tests/Godotstation.Tests/` | canonical | xUnit, headless, no Godot. |
| `planning/FidelityContract_*.md` | canonical spec | Numbered clauses (FC-n) are acceptance criteria. |
| `src/Project.Common/`, `src/Project.Engine/` | **legacy** | Contains the Wave 5 dispatcher that must be migrated (Phase 0). Otherwise don't read. |
| `Godotstation/` (nested copy) | **legacy duplicate** | Never read, never edit. Deleted in Phase 0. |
| `tests/*.cs`, `tests/Tests.csproj` (root) | **legacy** | Deleted in Phase 0. Note: `tests/Tests.csproj` globs `tests/Godotstation.Tests/**` by SDK default and will break the build if kept. |
| `*BENCHMARK*.md`, `Godotstation_*.md`, `carmack-directive.md`, `*_prompt.txt`, `CLAUDE.md`, `CODEX.md` | historical | Don't read. Superseded by this file. |
| `planning/PROMPT_*_WAVE*.md` | historical drafts | Read only the one file named in your current task, if any. |

Known inconsistencies you will hit (already diagnosed — don't re-investigate):
- Two interaction namespaces: `Godotstation.Common.Interaction` (legacy tree, has `InteractionDispatcher`, `IOwnedInteractionHandler<T>`, `TryResolveClick`) and `Godotstation.Common.Interactions` (canonical tree, used by `game/UI`). ~12 types exist in both (`HandApply`, `Intent`, `NetworkSide`, `ReachRange`, …).
- `game/` has no `project.godot` yet.
- `src/Project.Engine` uses `Godot.NET.Sdk` — violates the boundary; it's empty and gets deleted.
- Branch `wave5/dispatch-order` referenced by old prompts does not exist. Branch from `main`. Leave the other `wave*/` branches alone.

---

## 2. Read budget

Before writing code, read **only**:
1. This file.
2. Output of `bash tools/preflight.sh --fast` (one JSON line).
3. The files your task names, plus at most the interfaces they depend on (`Interfaces.cs`-style files, not implementations).
4. The specific FC clauses your task cites — `grep -n -A8 '## FC-8' planning/FidelityContract_InteractionSystem.md`, not the whole contract.

Use `grep`/`rg` to locate symbols before opening files. Open a file with a line range when you only need one type. If you notice yourself reading a third file "for context", stop and ask whether a grep would answer it.

---

## 3. Phase 0 — consolidate the tree (do once, before any wave work)

Branch: `chore/consolidate-tree` from `main`.

1. Migrate the newest interaction backend from `src/Project.Common/Interaction/**` into `src/Godotstation.Common/Interactions/`:
   - Namespace becomes `Godotstation.Common.Interactions` (plural, matching `game/`).
   - Where a type exists in both trees, keep the **canonical** file's shape and add only what the legacy one has that the canonical one lacks (`IOwnedInteractionHandler<T>`, `InteractionDispatcher`, `TryResolveClick`, `RegisterHandler`, the FC-8/FC-9 bucket ordering). Never keep two definitions of one type.
   - Canonical csproj has `ImplicitUsings=disable` and `TreatWarningsAsErrors=true`: add explicit `using` lines and fix every warning.
2. Migrate Atmos/Movement only if the canonical `src/Godotstation.Engine/` lacks the equivalent (`PlasmaFireReaction`/`IAtmosReaction` exist only in the nested copy — move them into `Godotstation.Engine.Atmos` if absent). Port their tests into `tests/Godotstation.Tests/<Area>/`.
3. Port `tests/InteractionFidelityTests.cs` cases that cover FC-8/FC-9 into `tests/Godotstation.Tests/Interactions/`. Dedupe against existing tests by test name and assertion, not by reading both files end to end.
4. `git rm -r` every path listed under `legacy_paths` in preflight output, plus the historical docs in §1 (keep `README.md`, `AGENTS.md`, `planning/FidelityContract_*.md`).
5. Replace `AGENTS.md` content with a pointer: `See CODEX_MASTER_PROMPT.md.` (Codex auto-loads `AGENTS.md`; keep it tiny.)
6. Gate: `bash tools/preflight.sh` → `"ok":true`. If `dotnet` is missing, `--fast` must be green and you report `build:"skipped"`.

Phase 0 deletes files. It is on a branch, so it's reversible, but **stop and report instead of deleting** if migration would require changing a public signature that `game/` already uses in a way you can't update in the same commit.

---

## 4. The per-task loop

Every task (a wave, a fix, a feature) runs this loop. Steps marked **CODE** are deterministic — run the command, don't reason about them. Steps marked **JUDGE** are the only places you think hard, and each has a closed answer set.

| # | Step | Kind | Do |
|---|---|---|---|
| 1 | Preflight | CODE | `bash tools/preflight.sh --fast`. If `ok:false` and the failure isn't one your task fixes → stop, report. |
| 2 | Route | JUDGE · Choice | Which side does this change belong to? `game` \| `common` \| `engine` \| `handoff`. `handoff` = the frontend needs a backend change → write a note in `planning/HANDOFF.md`, don't work around it in `game/`. |
| 3 | Plan | — | ≤10 lines: files to create/edit, the FC clauses they satisfy, the test that proves each. No prose essays. |
| 4 | Implement | — | Smallest diff that satisfies the plan. One concern per commit. |
| 5 | Verify | CODE | `bash tools/preflight.sh` (full). Fix until green. Don't summarize logs — grep for `error`. |
| 6 | Contract check | JUDGE · Noul | For each cited FC clause, answer **yes/no**: is there a test that would fail if this clause were violated? `no` → write the test, return to 5. |
| 7 | Report | CODE-shaped | Emit the JSON in §7. Nothing else. |

Rules for the loop:
- Max 3 Verify iterations on the same error. On the 4th, stop and report the error verbatim with your best hypothesis.
- Never edit files outside your routed side except `planning/HANDOFF.md` and tests.
- Don't re-run full preflight after doc-only changes.

---

## 5. Hard rules

1. `using Godot;` only in `game/`. Godot types never cross into `src/`. Convert `Godot.Vector2` ↔ `System.Numerics.Vector2` at the adapter boundary in `game/`.
2. `game/` never decides game outcomes. All clicks go through `InteractionDispatcher.TryResolveClick` — no hand-rolled resolution.
3. `Priority` orders handlers *within* a bucket only. First-interactable behaviour uses `IsFirstInteractable = true`, never priority tricks.
4. New backend behaviour that affects feel needs an FC clause in `planning/` before code. If none exists, `handoff`.
5. No new NuGet packages in `src/`. `game/` may use only `Godot.NET.Sdk`.
6. Don't invent Unitystation behaviour. If the FC doesn't specify it and no Unity source is provided, stub it with a `// TODO(fidelity): <question>` and list it in the report.
7. Commit messages: `<area>: <imperative summary>` (e.g. `interactions: migrate dispatcher to canonical tree`).

---

## 6. Current task — Wave 5: interaction dispatch frontend

Prerequisite: Phase 0 merged (or on the same branch, as earlier commits). Branch: `wave5/dispatch-frontend` from `main` (or from `chore/consolidate-tree` if not merged yet).

Cited clauses: **FC-8, FC-9** (read only those, via grep).

Deliver:
1. `game/project.godot` — minimal Godot 4 project, C# enabled, main scene = `res://World/Wave5Test.tscn`.
2. `game/Player/PlayerCharacter.cs` — `CharacterBody2D : IPerformer`. `WorldPosition` from `GlobalPosition`; `IsGhosting`, `HasTelekinesis`, `IsAlive`, `ActiveHandItem`, `CurrentIntent` as `[Export]` fields / stub state.
3. `game/World/InteractableNode2D.cs` — `Node2D : IInteractable`; `Id` from an exported stable string, falling back to a GUID generated once in `_Ready`.
4. `game/Items/WrenchHandler.cs` — `IOwnedInteractionHandler<HandApply>`, `Owner` = its `InteractableNode2D`, `IsFirstInteractable = false`. `WillInteract` true when target is in group `pipe`. `ServerPerformInteraction` emits a `Wrenched` signal.
5. `game/World/AirlockHandler.cs` — same interface, `IsFirstInteractable = true`, `WillInteract` always true, toggles open/closed (visual only).
6. `game/Input/InteractionInputController.cs` — holds the single shared `InteractionDispatcher` (autoload `Interaction`). On left click: performer = local `PlayerCharacter`, hand = `ActiveHandItem`, target = `InteractableNode2D` under cursor via physics point query (or `null`), position = world-space mouse, intent = UI intent, alt = Alt held, side = `NetworkSide.Server`. Calls `TryResolveClick`.
7. Handlers register in `_Ready()` via `RegisterHandler(this)`.
8. `game/World/Wave5Test.tscn` — player with wrench in hand, one airlock, one pipe (group `pipe`), the controller wired to the player.
9. Headless proof in `tests/Godotstation.Tests/Interactions/DispatchOrderTests.cs` using plain C# fakes of performer/interactable (no Godot):
   - wrench in hand + click airlock → airlock handler fires, wrench doesn't (FC-9)
   - wrench in hand + click pipe that has a non-first handler → wrench fires first (FC-8)
   - airlock handler declines → falls through to held item (FC-9 fallthrough)

If the canonical `Godotstation.Common` API after Phase 0 doesn't fit any of the above, route `handoff` — don't patch `src/` from this branch.

---

## 7. Report format (your final message, verbatim shape)

```json
{
  "task": "wave5/dispatch-frontend",
  "branch": "wave5/dispatch-frontend",
  "commits": ["<sha> <message>"],
  "route": "game",
  "preflight": { "ok": true, "build": "pass", "test": "pass", "test_summary": "Passed! 31/31" },
  "contract": { "FC-8": "tests/.../DispatchOrderTests.cs::HeldItemBeatsNonFirstTarget", "FC-9": "..." },
  "handoffs": [],
  "fidelity_todos": [],
  "blocked": null
}
```

`blocked` is `null` or one sentence naming what you need from a human. No extra prose before or after the JSON.
