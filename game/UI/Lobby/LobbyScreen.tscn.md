# LobbyScreen.tscn — scene tree

Nodes marked `%` have "Access as Unique Name" enabled — that is the contract
between scene and script (`GetNode<T>("%Name")`), so artists can rearrange the
tree freely as long as unique names survive.

```
LobbyScreen (Control, full rect)                    [script: LobbyScreen.cs]
├── Background (TextureRect, full rect)             — station nightside art, stretch mode: keep aspect covered
├── CenterContainer (full rect)
│   └── MainDialog (PanelContainer, min 900×620)    — the GUI_LobbyDialogue equivalent
│       └── VBoxContainer
│           ├── TitleBar (HBoxContainer)
│           │   ├── Logo (TextureRect)
│           │   └── VersionLabel (Label)
│           ├── %Tabs (TabContainer, expand fill)
│           │   ├── Servers (VBoxContainer)                      — tab 0
│           │   │   ├── AddressRow (HBoxContainer)
│           │   │   │   ├── AddressEdit (LineEdit, expand)       — "ip:port"
│           │   │   │   └── ConnectButton (Button)
│           │   │   └── HistoryScroll (ScrollContainer, expand)
│           │   │       └── %ServerHistoryList (VBoxContainer)   — rows built by code
│           │   ├── Character (HBoxContainer)                    — tab 1
│           │   │   ├── PreviewPanel (PanelContainer, 260 wide)
│           │   │   │   └── CharacterPreview (SubViewportContainer)
│           │   │   │       └── SubViewport → PaperDollPreview (Node2D)
│           │   │   └── CustomizationForm (ScrollContainer)      — ported later with CharacterCustomization
│           │   └── Jobs (Control)                               — tab 2
│           │       └── %JobSelectionPanel (ScrollContainer)     [script: JobSelectionPanel.cs]
│           │           └── %DepartmentList (VBoxContainer)      — headers + GridContainers built by code
│           └── Footer (HBoxContainer)                           — the GUI_PlayerJobs footer
│               ├── %SpectateButton (Button, "Spectate")
│               ├── Spacer (Control, expand)
│               └── %ReadyButton (Button, "Ready")
├── %JobInfoPopup (PanelContainer, centered, 420×360, hidden)    [script: JobInfoPopup.cs]
│   └── VBoxContainer
│       ├── %DepartmentBanner (ColorRect, 6px tall)
│       ├── %JobTitle (Label, theme: header)
│       ├── %SlotsLabel (Label)
│       ├── %Description (RichTextLabel, expand fill)
│       └── ButtonRow (HBoxContainer)
│           ├── %CloseButton (Button, "Back")
│           └── %ConfirmButton (Button)
├── %WaitMessage (PanelContainer, centered, hidden)              — "Please wait..." after job pick
│   └── VBoxContainer
│       ├── Spinner (TextureRect + rotation AnimationPlayer)
│       └── %WaitMessageText (Label, centered)
└── %ErrorWindow (AcceptDialog, hidden)                          — disconnect / job-error dialog
```

## Layout notes (visual parity with Unity)

- The Unity lobby is a fixed-ratio centered dialog over full-screen art;
  `CenterContainer` + minimum size reproduces `DetermineUIScale()` behaviour —
  Godot's content scale (`canvas_items` + `expand` aspect) replaces the manual
  UI-scale math in LobbyManager entirely.
- Tab order (Servers, Character, Jobs) mirrors the original flow; LobbyScreen
  drives `CurrentTab` from `LobbyState`, tabs are never user-clickable into an
  invalid state (Jobs tab hidden until a server accepts the character).
- The job info panel is a sibling of the dialog, not a child of the Jobs tab,
  so it can overlay the footer — same stacking as GUI_JobInfo in Unity.
