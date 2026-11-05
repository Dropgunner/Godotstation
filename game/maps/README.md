# Station maps

Godotstation rounds run on a **station map**: a small JSON file that holds the tile
bounds, wall tiles, ordered crew spawns, starting objects and labelled rooms. The host
picks a map; every joining client receives the same file over the network before it
spawns, so community maps need no separate download.

- Bundled maps live here in `game/maps/` (`res://maps` inside the game).
- Maps you save in the editor go to `user://maps` (shown in the editor's status line after a
  save; on macOS this is `~/Library/Application Support/Godot/app_userdata/Godotstation/maps`,
  on Linux `~/.local/share/godot/app_userdata/Godotstation/maps`, on Windows
  `%APPDATA%\Godot\app_userdata\Godotstation\maps`).
- To share a map, send the `.json` file or open a pull request adding it to this folder.
  Every bundled map is parsed and validated by the test suite.

## Using the editor

Open the game and press **Map Editor** in the lobby, or launch with `--map-editor`.

| Control | Action |
| --- | --- |
| `1` Wall | Left-drag paints walls, right-drag clears them. |
| `2` Floor | Drag clears walls back to floor. |
| `3` Spawn | Click toggles a crew spawn. Spawn order is join order; the host takes spawn 1. |
| `4` Entity | Click places the selected kind (set the stack amount for sheets or fuel); right-click removes the top entity on a tile. |
| `5` Room | Drag a rectangle, release to add a named, tinted room; right-click removes a room. Rooms are cosmetic. |
| `6` Erase | Click removes entity, then spawn, then wall; right-click removes a room. |
| `WASD` / arrows / middle-drag | Pan. Mouse wheel zooms. `F` frames the whole map. |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo. A drag is one undo step. |
| `Ctrl+S` | Save to `user://maps/<file name>.json`. |

The **Validation** panel updates after every change. Errors (red) stop the map from being
hosted: no spawn, a spawn or object inside a wall, an unknown object kind, a stack with no
amount, duplicate ids, or two solid objects on one tile. Warnings (yellow) are advice: an
open edge tile (the round seals the boundary for you) or an object or spawn the crew cannot
walk to from spawn 1. **Host a LAN shift on this map** saves, then starts a host on it.

To host a saved map from the command line:

```bash
Godotstation -- --playtest-host --playtest-map=user://maps/my_station.json
```

Clients join as usual; they get the map from the host.

## File format

```json
{
  "format": 1,
  "name": "Example Outpost",
  "author": "you",
  "description": "One line about the station.",
  "bounds": { "minX": -10, "minY": -6, "maxX": 10, "maxY": 6 },
  "tiles": [
    "#####################",
    "#.........#.........#",
    "#...................#",
    "#####################"
  ],
  "spawns": [[-5, 0], [-7, 0]],
  "rooms": [
    { "name": "DOCK", "minX": -9, "minY": -5, "maxX": -1, "maxY": 5,
      "floorColor": "#475b64", "labelColor": "#b8efff" }
  ],
  "entities": [
    { "id": 1, "kind": "wrench", "x": -3, "y": -3 },
    { "id": 3, "kind": "metal-sheets", "x": -3, "y": 3, "amount": 2 }
  ]
}
```

- `bounds` is inclusive. Sides must be 3 to 256 tiles.
- `tiles` has exactly `maxY - minY + 1` rows of exactly `maxX - minX + 1` characters, top row
  first (`minY`), left column first (`minX`). `#` is a wall, `.` is floor. Anything outside
  the bounds is sealed automatically.
- `spawns` is an ordered list of `[x, y]` floor tiles.
- `rooms` are optional labelled rectangles. Colors are `#rrggbb`. Later rooms draw over earlier ones.
- `entities` need a unique positive `id`, a known `kind`, a floor tile, and an `amount` of at
  least 1 for stackable kinds.

Known kinds: `wrench`, `service-cart`, `metal-sheets` (stackable), `girder`,
`engineering-airlock`, `generator-fuel` (stackable), `fuel-generator`, `power-storage`,
`apc-lamp`. The list is `StationEntityKinds.All` in
`src/Godotstation.Engine/Entities/StationEntityCatalog.cs`; adding an object to the game means
adding it there and in the shared world, after which the editor palette picks it up.

## Where the rules live

- Model, JSON codec, validator and undo history: `src/Godotstation.Engine/Maps/` (pure C#, no Godot).
- Tests: `tests/Godotstation.Tests/Maps/`. `playtest_station.json` must equal the code default;
  run `GODOTSTATION_UPDATE_BUNDLED_MAPS=1 dotnet test tests/Godotstation.Tests` to regenerate it.
- Editor scene and Godot file access: `game/MapEditor/`, `game/Maps/StationMapLibrary.cs`.
- Smokes: `bash tools/map_editor_smoke.sh` drives the editor headlessly;
  `bash tools/multiplayer_smoke.sh --custom-map` hosts `example_outpost.json` for two clients.
