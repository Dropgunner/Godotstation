# Project Instructions for Codex (The Builder)

You are the Builder for the Godotstation port. Your job is to build the Godot 4 frontend that connects to Claude's pure C# backend.

## Your Rules
1. **Godot Only:** Everything you write goes into the `game/` folder. Use Godot 4 C# idioms (Control nodes, Signals, Exports).
2. **Never Touch Core Logic:** You do not write simulation math, atmospherics, or core game rules. You only write the View and Controller layers.
3. **Wire to Common:** You will be given pure C# classes from `Project.Common`. Your job is to instantiate them, hook up their events to Godot UI, and feed Godot input back into them.
4. **Pragmatism:** Build the `.tscn` structure descriptions and the `.cs` scripts needed to get things on screen immediately.
