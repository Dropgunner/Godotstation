# The Godotstation Operational Playbook: Carmack & Sweeney in Tandem

Based on our empirical benchmark (where Claude produced abstract architectural contracts and Codex immediately generated 7 UI files with zero compile errors), we have confirmed the **VortexArena Dual-Agent Methodology**. 

Here is exactly how you use them together to port Unitystation to Godot.

## The Core Philosophy

You do not ask one AI to port the game. You run a **Pipeline**:
1. **Claude Fable (Carmack)** acts as the Architect. It strips away Unity, extracts the pure logic, and writes the tests.
2. **OpenAI Codex (Sweeney)** acts as the Builder. It takes Claude's pure logic and wires it into Godot nodes, UI, and signals.

---

## Phase 1: The Engine-Agnostic Core (Claude's Domain)

Unitystation's core logic (Atmospherics, Power, Interaction, Chemistry) is heavily tangled with Unity's `MonoBehaviour` and `Update()` loops. **Do not give this to Codex.** Codex will try to translate `MonoBehaviour` directly to Godot `Node`, resulting in a bloated, unmaintainable mess.

**Your Workflow with Claude:**

1. **Input:** Give Claude a specific Unity subsystem (e.g., `AtmosSimulation.cs`).
2. **Prompt:** *"Extract this into a pure C# engine-agnostic library. Remove all Unity dependencies. Write a Fidelity Contract (5 testable assertions) that defines how this system MUST behave. Write xUnit tests for those assertions."*
3. **Output:** Claude gives you a clean `.NET` library and a suite of tests.
4. **Verification:** Run the tests locally. If they pass, the logic is ported.

**Why Claude?** Claude is patient. It will read the 2,000-line Atmos file, understand the gas mixture math, and rewrite it without touching a game engine.

---

## Phase 2: The Godot Frontend (Codex's Domain)

Once Claude has built the pure C# logic, you need to connect it to the screen. This means UI, input handling, sprites, and Godot `_Process()` loops. **Do not give this to Claude.** Claude will over-engineer the UI architecture. 

**Your Workflow with Codex:**

1. **Input:** Give Codex Claude's pure C# library, PLUS the original Unity UI scripts (e.g., `InventoryUI.cs`).
2. **Prompt:** *"Here is the pure backend logic. Here is how the old Unity UI worked. Build the Godot 4 UI for this. Use Control nodes, Signals, and wire it to the backend library. Give me the `.cs` files and the `.tscn` hierarchy."*
3. **Output:** Codex gives you exactly what it did in Benchmark B2: `LobbyScreen.cs`, `InventoryUI.cs`, drag-and-drop logic, and scene hierarchies.
4. **Verification:** Drop the files into Godot. Connect the signals in the editor based on Codex's `.tscn` markdown map.

**Why Codex?** Codex is pragmatic. As we saw, it doesn't hesitate—it just builds the 7 files you need to get things on screen immediately.

---

## The Step-by-Step Porting Loop

To port a feature (e.g., "The Medbay"), you execute this loop:

### Step 1: The Handoff (You)
Find the Unitystation files for the feature. Separate them into "Logic" and "View".

### Step 2: The Architecture Session (Claude)
- **Open Claude**
- Upload `CLAUDE.md` and the "Logic" files.
- **Prompt:** "De-Unity this logic. Give me pure C# and the Fidelity Contract."
- **Result:** You now have `MedbayLogic.dll` (conceptually) and passing tests.

### Step 3: The Implementation Session (Codex)
- **Open Codex / ChatGPT**
- Upload `CODEX.md`, the "View" files, AND Claude's output from Step 2.
- **Prompt:** "Build the Godot frontend for this logic."
- **Result:** You get the Godot scenes and scripts.

### Step 4: The Integration (You)
Open Godot. Compile the C# project. Run the game. If it feels wrong, check Claude's Fidelity Contract. If it crashes, tell Codex to fix the Godot specific error.

---

## Repository Structure to Support This

To make this work, your `Godotstation` repo MUST be split into two projects:

```text
Godotstation/
├── CoreLogic/               # CLAUDE'S DOMAIN
│   ├── CoreLogic.csproj     # Pure .NET Standard / .NET 8 (NO Godot references)
│   ├── Atmospherics/
│   ├── Interaction/
│   └── Tests/               # xUnit tests enforcing the Fidelity Contracts
│
├── GodotClient/             # CODEX'S DOMAIN
│   ├── GodotClient.csproj   # References CoreLogic.csproj AND Godot
│   ├── project.godot
│   ├── UI/
│   └── Scenes/
│
├── CLAUDE.md                # Rules for Claude (Pure C#, Contracts)
└── CODEX.md                 # Rules for Codex (Godot nodes, Signals, UI)
```

## Summary

By separating concerns, you bypass the weaknesses of both models. Claude never gets bogged down trying to guess Godot UI node names. Codex never accidentally breaks the math of the Atmos system because it's safely locked behind Claude's tests. 

You act as the **Director**, moving the pure logic from Claude's desk to Codex's desk.
