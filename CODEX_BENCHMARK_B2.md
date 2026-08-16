# BENCHMARK B2: The Sweeney Test — Godotstation Port

## Your Identity

You are the lead systems integrator and UI/UX developer for the Godotstation project. Your archetype is **Tim Sweeney**: breadth, efficiency, ecosystem integration, fast shipping. You own the Godot frontend layer.

## Project Context

Godotstation is a port of **Unitystation** (a Space Station 13 remake, 19,919 commits, 96.5% C#, Unity 6) to **Godot 4 (.NET/C#)**. This port follows the methodology proven by VortexArena (Xonotic → Godot, 316 commits, 2,984 tests, Claude Opus 4.8 as primary developer).

**Reference architecture** (from VortexArena):
- `src/Godotstation.Common/` — Gameplay, interactions, protocol (NOT your domain to write).
- `src/Godotstation.Engine/` — Physics, atmos (NOT your domain to write).
- `game/` — **YOUR DOMAIN.** Godot-side rendering, UI, input, menus.
- `game/UI/` — All Control-node-based UI scenes and scripts.
- `game/Scenes/` — Godot scene files.

**Repository references:**
- Source: https://github.com/unitystation/unitystation (branch: develop)
- Architecture model: https://github.com/bryankruman/VortexArena
- Target: https://github.com/Godotic/XonoticGodot

## Your Constraints (The Sweeney Rules)

1. **Godot-native code.** You write `using Godot;` code. Your scripts extend `Control`, `Node`, `Panel`, etc.
2. **Consume, don't create, game logic.** You call interfaces from `Godotstation.Common` — you never implement game rules.
3. **Visual parity first.** The UI must look and behave like the Unity original. Speed of delivery matters.
4. **Godot idioms.** Use `PackedScene`, `ResourceLoader`, signals, `_Ready()`, `_Process()`. No Unity patterns.

## Your Task

Translate the following Unity uGUI systems into Godot 4 C# + scene structures:

### System 1: Lobby UI (LobbyManager + GUI_PlayerJobs)

The lobby is the first screen players see. It handles server connection, character selection, and job picking.

**Unity structure (to translate):**
- `LobbyManager` (MonoBehaviour singleton) — manages lobby state, server history, UI scale
- `GUI_PlayerJobs` (MonoBehaviour) — job selection buttons, job info panel, spectate option
- `GUI_LobbyDialogue` — the main lobby dialog with tabs
- `CharacterCustomization` — character creation/editing (1,379 lines)

**Key UI elements to reproduce:**
- Server list with connection history
- Character preview panel
- Job selection grid (buttons with icons per department)
- Job info popup (description, requirements)
- "Ready" / "Spectate" footer buttons
- Loading/wait message after job selection

### System 2: Inventory UI (DynamicItemStorage frontend)

The inventory is the core gameplay UI. Players interact with it constantly.

**Unity structure (to translate):**
- `DynamicItemStorage` (1,445 lines) — backend storage system (you consume its interface)
- `UI_DynamicItemSlot` — individual slot UI element
- Slot types: hands, clothing, belt, backpack, pockets, ID
- Drag-and-drop between slots
- Right-click context menus on items

**Key UI elements to reproduce:**
- Paper-doll layout (body slots arranged around a character silhouette)
- Hand slots (left/right, with active hand indicator)
- Backpack/belt expandable storage panels
- Item tooltip on hover
- Drag-and-drop with ghost sprite following cursor
- Right-click → context menu with interaction options

## Backend Interfaces You Consume

These interfaces are defined by the engine team (you don't implement them, you call them):

```csharp
// From Godotstation.Common — you wire UI events to these
namespace Godotstation.Common.Interactions
{
    public interface IInteractionService
    {
        void RequestHandApply(int performerId, int targetId, BodyPart targetPart, bool isAltClick);
        void RequestInventoryTransfer(int fromSlotId, int toSlotId);
        void RequestDrop(int slotId);
        void RequestPickup(int itemId);
    }

    public interface IInventoryProvider
    {
        IReadOnlyList<SlotData> GetPlayerSlots(int playerId);
        SlotData GetSlot(int slotId);
        event Action<int, SlotData> OnSlotChanged;
    }

    public interface ILobbyService
    {
        IReadOnlyList<JobData> GetAvailableJobs();
        IReadOnlyList<ServerInfo> GetServerHistory();
        void SelectJob(int jobId);
        void RequestSpectate();
        event Action<LobbyState> OnLobbyStateChanged;
    }
}
```

## Unity Source Code (Key Excerpts)

### LobbyManager.cs (517 lines)

```csharp
namespace US13.Managers.LobbyManager
{
    public class LobbyManager : SingletonManager<LobbyManager>
    {
        [SerializeField] private GameObject characterSettings;
        [SerializeField] private GUI_LobbyDialogue lobbyDialogue;
        [SerializeField] public GUI_ServerPassword LobbyPasswordGUI { get; private set; } = null;

        public bool WasDisconnected { get; set; } = false;
        public List<ConnectionHistory> ServerJoinHistory { get; private set; }
        private static readonly int MaxJoinHistory = 20;

        public override void Start()
        {
            base.Start();
            LoadHistoricalServers();
            DetermineUIScale();
            UIManager.Display.SetScreenForLobby();
        }
    }
}
```

### GUI_PlayerJobs.cs (343 lines)

```csharp
namespace US13.UI.Systems.Jobs
{
    public class GUI_PlayerJobs : MonoBehaviour
    {
        public GameObject buttonPrefab;
        public GameObject screen_Jobs = null;
        public GUI_JobInfo jobInfo = null;
        public GameObject footer = null;
        [SerializeField] private GameObject errorInfoWindow = null;
        [SerializeField] private Text errorReasonText = null;
        [SerializeField] private GameObject waitMessage = null;
        // Job buttons are instantiated from buttonPrefab, one per available job
        // Clicking a job button shows jobInfo panel
        // Footer has "Spectate" and "Ready" buttons
    }
}
```

### DynamicItemStorage.cs (1,445 lines — backend, you consume it)

```csharp
namespace US13.Systems.Inventory
{
    public class DynamicItemStorage : NetworkBehaviour
    {
        public Dictionary<NamedSlot, List<ItemSlot>> ServerContents;
        public Dictionary<NamedSlot, List<ItemSlot>> ClientContents;
        public List<ItemSlot> ClientTotal = new List<ItemSlot>();
        // Named slots: LeftHand, RightHand, Head, Suit, Belt, Back, Pocket1, Pocket2, ID, etc.
        // Each ItemSlot has: Item (GameObject), NamedSlot, IsOccupied, ItemAttributes
    }
}
```

## Expected Output

Produce the following files:

1. **`game/UI/Lobby/LobbyScreen.cs`** — Main lobby Control script (extends `Control`)
2. **`game/UI/Lobby/LobbyScreen.tscn.md`** — Description of the Godot scene tree (node hierarchy)
3. **`game/UI/Lobby/JobSelectionPanel.cs`** — Job selection grid with button instantiation
4. **`game/UI/Lobby/JobInfoPopup.cs`** — Job info display panel
5. **`game/UI/Inventory/InventoryUI.cs`** — Main inventory Control script
6. **`game/UI/Inventory/InventoryUI.tscn.md`** — Description of the inventory scene tree
7. **`game/UI/Inventory/ItemSlotUI.cs`** — Individual slot (drag source/drop target)
8. **`game/UI/Inventory/DragPreview.cs`** — Ghost sprite that follows cursor during drag

## Translation Reference Table

| Unity (uGUI) | Godot 4 Equivalent |
|---|---|
| `Canvas` | Root `Control` node (full rect) |
| `RectTransform` | `Control` with anchors/margins |
| `VerticalLayoutGroup` | `VBoxContainer` |
| `HorizontalLayoutGroup` | `HBoxContainer` |
| `GridLayoutGroup` | `GridContainer` |
| `Image` | `TextureRect` or `Panel` with StyleBox |
| `Text` / `TMP_Text` | `Label` or `RichTextLabel` |
| `Button` | `Button` |
| `ScrollRect` | `ScrollContainer` |
| `Toggle` | `CheckButton` or `CheckBox` |
| `InputField` | `LineEdit` |
| `EventSystem` | Built-in (Godot handles input routing) |
| `OnClick.AddListener()` | `button.Pressed += handler;` |
| `DragHandler` | `_GuiInput()` + `InputEventMouseMotion` |
| `[SerializeField]` | `[Export]` |
| `Instantiate(prefab)` | `scene.Instantiate<T>()` |
| `SetActive(bool)` | `Visible = bool` |

## Success Criteria

Your output will be evaluated on:
1. **Speed**: How quickly and completely did you produce the translation?
2. **Visual parity**: Does the node hierarchy faithfully reproduce the Unity layout?
3. **Godot idioms**: Are you using signals, exports, PackedScenes correctly?
4. **Backend wiring**: Are UI events correctly routed to the `IInteractionService` / `ILobbyService` interfaces?
5. **Pragmatism**: Did you avoid over-engineering and focus on shipping?

Begin.
