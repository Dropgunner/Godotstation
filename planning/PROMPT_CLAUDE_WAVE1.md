**COPY-PASTE THIS TO CLAUDE:**

```text
Please read the CLAUDE.md file in this repository to understand your role as the Architect.

We are beginning Wave 1 of the Godotstation port. I need you to extract the core interaction logic from Unitystation into pure, engine-agnostic C#.

Here are the Unitystation source files for the Interaction System:
[ATTACH UnityProject/Assets/Scripts/US13/Core/Input_System/InteractionV2/DefaultWillInteract.cs]
[ATTACH UnityProject/Assets/Scripts/US13/Core/Input_System/InteractionV2/Interactions/HandApply.cs]

Your tasks:
1. Write the Fidelity Contract for the Interaction System (focusing on resolution order, reach validation, and intent).
2. Write the pure C# engine-agnostic implementation for `src/Project.Common/`.
3. Write the xUnit tests that verify the Fidelity Contract.

Do not write any Godot code.
```
