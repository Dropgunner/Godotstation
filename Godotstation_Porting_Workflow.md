# Godotstation: Porting Unitystation to Godot 4 with Dual-AI Agents

**Author**: Manus AI for Prayer
**Date**: 13 July 2026
**Project**: Godotstation (Godotic/XonoticGodot)
**License**: AGPL-3.0 (inherited from upstream)

---

## Executive Summary

This document defines the complete workflow for porting Unitystation — a 19,919-commit, 96.5% C# Space Station 13 remake running on Unity 6 — to Godot 4 (.NET/C#). The methodology is modeled directly on the proven VortexArena project [1], which successfully ported Xonotic from DarkPlaces/QuakeC to Godot with ~2,950 automated tests and 316 commits, using Claude Opus 4.8 as its primary AI developer.

The key innovation here is a **dual-AI agent strategy**: Claude Fable and OpenAI Codex (Sol) are deployed to complementary domains based on their architectural archetypes, with a structured benchmarking framework to validate which model performs best on which class of porting task.

---

## 1. The Carmack/Sweeney Benchmarking Framework

Your conversation with Claude Fable revealed a fundamental insight: both AI models assigned themselves the "Carmack" role and relegated the other to "Sweeney." This narcissistic bias is itself the first data point. The honest resolution — as Fable correctly noted — is to **run the same coding task through both and judge for yourself**.

### 1.1 The Archetypes Defined

| Dimension | John Carmack (Depth) | Tim Sweeney (Breadth) |
|---|---|---|
| **Core strength** | Hand-optimizes the critical path; rewrites from first principles | Builds ecosystems that scale to everyone; pragmatic efficiency |
| **Porting analogue** | Fidelity Contract enforcement, physics parity, netcode prediction | Bulk UI translation, asset pipeline, boilerplate generation |
| **Risk profile** | Over-engineers; slower on "good enough" tasks | May miss subtle parity bugs in feel-critical code |
| **Game dev legacy** | id Tech engines, DOOM, Quake | Unreal Engine, Epic ecosystem |

### 1.2 The Benchmark Tasks

Rather than abstract comparisons, we will benchmark Fable and Sol on **three real porting tasks** extracted from Unitystation's codebase. Each task is designed to test a specific axis of the Carmack/Sweeney split.

| Benchmark | Task Description | Primary Metric | Secondary Metric |
|---|---|---|---|
| **B1: The Carmack Test** | Port `PlayerMovement.cs` + `AtmosSimulation.cs` to engine-agnostic C# with Golden Trace parity | Mathematical correctness (trace divergence < 0.001) | Zero Godot dependencies in output |
| **B2: The Sweeney Test** | Translate the full Inventory UI (uGUI prefabs → Godot Control nodes) | Time to functional completion | Visual parity score (manual 1-10) |
| **B3: The Architect Test** | Given `InteractionSystem.cs`, produce a Fidelity Contract (5-10 testable assertions) | Quality of assertions (are they testable? Do they capture "feel"?) | Avoidance of implementation details |

### 1.3 Execution Protocol

Each benchmark task is given to both models with identical context:

1. Provide the source Unity C# file(s) plus the VortexArena `CLAUDE.md` as architectural reference.
2. Provide the target `Godotstation.Common` project structure.
3. Measure: compilation success, test pass rate, time-to-completion, and human review score.

The results populate a **Model Fitness Matrix** that determines which agent leads which porting wave.

---

## 2. Project Architecture

Following the VortexArena separation principle [1], the game simulation must compile and run without any Godot dependency:

```
Godotstation/
├── project.godot                    Godot 4.x (.NET) project
├── Godotstation.sln                 Full solution
├── src/
│   ├── Godotstation.Common/         Gameplay, interactions, protocol (NO Godot)
│   ├── Godotstation.Engine/         Tile grid, atmos sim, physics (NO Godot)
│   ├── Godotstation.Net/            Mirror replacement: authoritative netcode (NO Godot)
│   ├── Godotstation.Formats/        Unity asset parsers/converters (NO Godot)
│   ├── Godotstation.Server/         Dedicated server logic
│   └── Godotstation.SourceGen/      Roslyn source generators (registries, net serializers)
├── game/                            Godot-side: rendering, UI, input, menus
├── tests/Godotstation.Tests/        xUnit test suite (runs without Godot)
├── planning/                        ADRs, fidelity contract, parity specs, trackers
│   ├── upstream-watch/              Monitoring unitystation/unitystation for new commits
│   ├── fidelity-contract.md
│   └── ADR-0001-separation.md
├── .claude/workflows/               Claude Fable automation workflows
├── CLAUDE.md                        Claude Code/Fable project instructions
├── CODEX.md                         OpenAI Codex project instructions
└── assets/                          Converted game assets (gitignored)
```

> **The cardinal rule**: `Godotstation.Common` has zero Godot references — enforced at build time. This enables headless testing, dedicated servers, and shared prediction code between client and server.

---

## 3. The Fidelity Contract

For Unitystation, the feel-critical behaviors that must be reproduced exactly are:

| # | Behavior | Testable Assertion | Tolerance |
|---|---|---|---|
| 1 | Tile-grid movement | Player position snaps to grid centers; diagonal movement resolves identically | Exact match |
| 2 | Interaction matrix | Click(item_A, target_B, intent_C) → same outcome as Unity version | Exact match |
| 3 | Atmos simulation tick | Given gas mixture M in room R, after N ticks, pressures match | < 0.01 kPa |
| 4 | Server authority | Client prediction reconciles within 3 frames of server correction | 3 frames max |
| 5 | Construction system | Placing/deconstructing objects follows identical state machine transitions | Exact match |
| 6 | Health/damage model | Damage(type, amount, bodypart) → same HP/status result | Exact match |
| 7 | Power grid simulation | Wire network propagation matches Unity tick-for-tick | < 0.1W divergence |

Everything else — menus, rendering, audio, UI layout, shader effects — is modernized freely for Godot.

---

## 4. Agent Assignment & Porting Waves

Based on the benchmarking results (and the a priori archetype hypothesis), agents are assigned as follows:

### Wave Schedule

| Wave | Module | Lead Agent | Rationale |
|---|---|---|---|
| 0 | Architecture & CI setup | Human + Fable | Establish the engine-free zone, test harness, CLAUDE.md |
| 1 | Tile Grid & Movement | Fable (Carmack) | Feel-critical; requires mathematical parity |
| 2 | Atmos Simulation | Fable (Carmack) | Complex physics simulation; fidelity contract item |
| 3 | Asset Pipeline | Sol (Sweeney) | Bulk conversion; breadth over depth |
| 4 | Main Menu & Lobby UI | Sol (Sweeney) | Standard UI translation; speed matters |
| 5 | Interaction System | Fable (Carmack) | Core game feel; complex state machine |
| 6 | Inventory UI & HUD | Sol (Sweeney) | Complex but standard UI work |
| 7 | Networking (Logic) | Fable (Carmack) | Replacing Mirror; prediction/reconciliation |
| 8 | Networking (Godot RPC) | Sol (Sweeney) | Wiring the logic layer to Godot transport |
| 9 | Health/Damage/Combat | Fable (Carmack) | Fidelity contract item |
| 10 | Power/Construction | Joint | Both agents contribute to their strengths |
| 11 | Polish & Integration | Joint | Final assembly and parity testing |

### Agent Workflow Rules

**For Claude Fable (CLAUDE.md)**:
- Always produce engine-agnostic code in `Godotstation.Common` or `Godotstation.Engine`.
- Every ported module must include xUnit tests that run without Godot.
- Preserve gameplay-critical constants and branch order from the original.
- Comment intentional deviations with rationale.
- Co-Author commits: `Co-Authored-By: Claude <noreply@anthropic.com>`

**For OpenAI Codex (CODEX.md)**:
- Work primarily in the `game/` directory (Godot-side code).
- Prioritize speed and completeness over micro-optimization.
- Translate Unity UI hierarchies to Godot Control node trees.
- Wire UI elements to the backend interfaces defined by Fable.
- Co-Author commits: `Co-Authored-By: Codex <noreply@openai.com>`

---

## 5. Upstream-Watch System

Because Unitystation is actively developed (last commit: 11 July 2026, 682 open issues), the port must track upstream changes:

The upstream-watch system (proven in VortexArena [1]) consists of three components:

**Harvester** (`tools/upstream-watch.py`): A weekly script that fetches new commits from `unitystation/unitystation`, lists active branches and open PRs, deduplicates against the existing ledger, and writes a dated worklist.

**Analyst** (`.claude/workflows/upstream-watch.js`): Claude Fable reads each diff and produces a structured assessment: port-worthiness score, relevance to Godotstation, and a recommended decision.

**Ledger** (`planning/upstream-watch/LEDGER.yaml`): The source of truth for all upstream triage decisions. Each entry carries: commit hash, description, relevance score, decision (Port / Adapt / Reject / Defer / N/A), and a link to a deep-dive analysis if warranted.

---

## 6. Testing Strategy

| Layer | What It Tests | When It Runs | Owner |
|---|---|---|---|
| Unit tests | Gameplay logic (engine-free) | Every commit | Both agents |
| Golden-trace tests | Interaction/movement results match Unity | Every commit | Fable |
| Atmos parity tests | Gas simulation trajectories match | Nightly | Fable |
| UI snapshot tests | Visual regression of Godot UI | Nightly | Sol |
| Network tests | Prediction reconciles under packet loss | Nightly | Fable |
| Integration tests | Headless Godot loads map + steps N ticks | Nightly | Joint |
| Manual A/B QA | Side-by-side with Unity build | Pre-milestone | Human |

---

## 7. Practical Next Steps

The immediate actions to begin this workflow are:

1. **Clone and audit** `unitystation/unitystation` — run `cloc` to get exact LOC counts per module, identify the top 20 most-coupled Unity dependencies.

2. **Run Benchmark B1** — extract `PlayerMovement.cs` from Unitystation, give it to both Fable and Sol with identical instructions, measure the results.

3. **Fork `unitystation/Godotstation`** into `Godotic/XonoticGodot` (or create a fresh repo) — the existing 2023 attempt has only 1 commit and no meaningful code.

4. **Write CLAUDE.md and CODEX.md** — project instruction files for each AI agent, defining their domain boundaries, coding conventions, and testing requirements.

5. **Establish CI** — GitHub Actions running `dotnet test` on every push, enforcing the "no Godot in Common" rule via a build-time analyzer.

---

## 8. The Meta-Insight: Why Benchmark First

Your conversation exposed something psychologically interesting: both AI models exhibit **in-group bias** when asked to self-evaluate. Fable called itself Carmack; Codex called itself Carmack. Neither has objective evidence — Sol was barely out of preview at the time of that conversation.

The benchmarking framework above is designed to produce **the only answer that isn't wearing a company badge**: empirical results on real porting tasks. The Carmack/Sweeney axis isn't a ranking — it's a style axis. The question isn't "who is better" but "who is better *at what*." The workflow above exploits that distinction by assigning each agent to its natural domain, then validating the assignment with data.

This is the same principle as competitive game balance: you don't ask the Quake player and the Unreal player who has the better engine. You measure frame times, netcode latency, and player retention. The engine that wins depends on what you're measuring.

---

## References

[1]: https://github.com/bryankruman/VortexArena "VortexArena — Xonotic ported to Godot 4 (.NET/C#)"
[2]: https://github.com/unitystation/unitystation "Unitystation — The original Unity SS13 remake"
[3]: https://github.com/unitystation/Godotstation "Godotstation — Abandoned 2023 porting attempt (1 commit)"
[4]: https://openai.com/de-DE/index/previewing-gpt-5-6-sol/ "OpenAI GPT-5.6 Sol Preview"
