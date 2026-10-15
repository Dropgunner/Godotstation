# Inventory HUD scenes

The playable inventory HUD is mounted in `Wave5Test.tscn` under a `CanvasLayer`.
It stays fixed to the lower-left corner while the world camera moves. ItemSlotUI
instances are created from `ItemSlotUI.tscn`, because clothing may add or remove
slots at runtime.

```
InventoryUI (Control, anchored bottom-left)          [script: InventoryUI.cs]
│   [Export] itemSlotScene = ItemSlotUI.tscn
├── %PaperDoll (Control, 240×220)                    — body slots at fixed offsets
│   └── Silhouette (TextureRect, centered, 30% alpha) — character outline art
│       (ItemSlotUI children placed by code at PaperDollLayout offsets)
├── %HandsRow (HBoxContainer, centered below doll)
│       (LeftHand / RightHand ItemSlotUI by code; active hand tinted gold)
├── %BackpackPanel (PanelContainer, above doll, hidden)
│   └── ScrollContainer (max height 200)
│       └── %BackpackGrid (GridContainer, columns 7)
└── %BeltPanel (PanelContainer, above doll, hidden)
    └── %BeltGrid (GridContainer, columns 7)
```

# ItemSlotUI.tscn — scene tree (instantiated per slot)

```
ItemSlotUI (PanelContainer, min 44×44)               [script: ItemSlotUI.cs]
│   theme override: StyleBoxFlat, dark translucent, 1px border
├── %Icon (TextureRect, full rect, keep aspect centered)
└── %SlotLabel (Label, centered, small, 50% alpha)   — slot name when empty
└── ContextMenu (PopupMenu)                          — Drop / Examine
```

## Behaviour parity notes

- **Paper-doll layout** uses fixed offsets (`PaperDollLayout` in code), matching
  the Unity HUD arrangement: head column center-top, eyes/mask/ear row, suit
  row, bottom bar Back/Belt/Pocket1/Pocket2/ID. Offsets are per-slot constants,
  not a container, because the doll is not a grid.
- **Active hand**: gold `SelfModulate` tint on the active hand slot; clicking
  either hand switches hands locally (no server round-trip).
- **Drag & drop**: Godot-native (`_GetDragData` → `DragPreview.For(texture)` at
  70% alpha as the ghost, `_DropData` → `RequestInventoryTransfer`). Cancelled
  drags need no code — the engine drops the preview.
- **Right-click** on an occupied slot opens the scene-owned PopupMenu
  (Drop / Examine) at the cursor, routed to `RequestDrop` / `RequestHandApply(alt)`.
- **Expandable storage**: clicking an occupied Back/Belt slot toggles its panel;
  panel grids are filled via `PopulateStorage` from the provider's slot list
  for that container.
- **Threading**: `OnSlotChanged` is marshalled to the main thread with
  `Callable.From(...).CallDeferred()` before touching any node — backend events
  may arrive from the net thread.
