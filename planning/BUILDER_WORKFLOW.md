# Builder Workflow (Codex)

This is the step-by-step loop for the Builder lane. Follow it in order. If a step fails, stop and report the failure in your reply. Don't work around it.

## 0. Start clean

```bash
git fetch origin
git checkout -B <your-branch> origin/main
git status            # must show no local changes
```

Work only from `origin/main`. If your working tree is behind or has edits you didn't make, don't continue on it. Use a new branch from `origin/main`.

Check that the files you need exist before you go further:

- `AGENTS.md`
- `planning/UNITYSTATION_PARITY_ROADMAP.md`

If either is missing, stop and report it. Don't guess at the rules.

## 1. Check what's already in flight

- List open PRs and open branches on the repo.
- If a PR or branch already touches your candidate item or the same files, pick a different item or ask.
- Note in your reply which PRs and branches you checked.

## 2. Pick one roadmap item

- Choose one row from the roadmap's outcome table or one item from its milestones.
- Write the row name in your reply. If you can't name a roadmap row for your work, don't start it.
- Keep the slice small enough for one PR.

## 3. Decide the lane

- UI work goes in `game/` and `tools/`.
- If the item needs a game rule that doesn't exist yet, build the UI part only. Add a `needs-engine` note to the PR listing the rule, with its inputs and outputs. Don't write the rule yourself.

## 4. Build

- Keep changes in `game/Playtest/StationPlaytestController.cs` under about 100 lines. For more than that, move the new code into a new file first.
- Don't change public engine APIs. If one has to change, say so in the PR.

## 5. Check (all must pass before you open the PR)

```bash
bash tools/preflight.sh
```

- The two-client smoke for your feature must be added to `tools/multiplayer_smoke.sh` and to the CI matrix in `.github/workflows/ci.yml`, and it must pass in CI.
- Update the matching `planning/FidelityContract_*.md` file.
- Update the roadmap row for your item.

## 6. Open the PR

Use this exact structure in the description:

```
Roadmap item: <row name>
Lane: UI | UI + needs-engine
Contract updated: planning/FidelityContract_<name>.md
Proof: <preflight result>, <smoke name> in CI
Files over 100 lines in StationPlaytestController: <yes/no, and where split>
Open PRs/branches checked: <list>
needs-engine: <rule needed, or "none">
```

Don't merge your own PR. Don't edit `AGENTS.md` or the roadmap's rules section.

## 7. Report back

Reply with the PR link, the roadmap row, and the result of each check. If any step failed, say which one and what the error was.
