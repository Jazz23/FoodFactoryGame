# Bevy port architecture

The port reproduces the **implemented Unity game** (Unity source under `../Assets/Scripts`, the authoritative design in
`../Food_Factory_Restaurant_GDD.md`, accepted decisions in `../docs/decisions/`). It lives entirely under `bevy/`; the
Unity project, its settings, scenes, art and saves are untouched. Proposals and open decisions in the GDD are not
selected here.

Status labels below: **implemented** (code exists and was exercised as stated), **implemented, untested** (code exists,
not yet exercised in a running game), **simplified** (works, but deliberately less than Unity), **missing**.

## Contracts (unchanged from the Unity game)

- The server owns gameplay state. Clients send `protocol::Command`s; the server validates every one against its own
  state (inventory transfers, payments, job claims, placement) and answers with an `Outcome`. Previews in the client
  (ghosts, prices, hints) use the same pure rule functions as the server but never change state.
- Durable commands: the server clones state, applies, saves to SQLite, and restores the clone if the save fails.
  Accepted requests replay by `(player, request id)`, so a retry never charges twice.
- All persisted data is SQLite (decision 0011): `persistence::WorldStore` (snapshots and the world layout),
  `PlayerRegistry` (identities, poses, per-player settings), `ClientSettings` (client preferences), `client_secret`
  (this client's identity). No other save format, no `PlayerPrefs`. Content (`assets/content/catalog.json`) and art are
  assets, not saved data.
- Client visibility, camera position and which sites are drawn never decide whether a site's simulation exists or runs.
- Tests use isolated directories under the system temp directory and never the application data root; capture runs set
  `FF_DATA_ROOT` to an isolated folder.

## Library (`src/lib.rs`, headless, no Bevy)

| Module | Unity source | Status |
| --- | --- | --- |
| `world` (random, settings, layout, geometry, terrain, generator v8, validator, roads, clock) | `FoodFactoryGame.World` | implemented; the generator is a faithful port, the **validator is reduced** (access, lots, start, farms/stations, land) |
| `goods` (state, grid, rules, equipment, production, belts, company, buildings, employees, property, trucks, farms, customers, readiness, layout_content) | `FoodFactoryGame.Goods` (`GoodsWorld.*`) | implemented; covered by `tests/server.rs` for buying, hiring, customer sales and employee scripts |
| `employees` (tasks, navigation, Luau scripts) | `EmployeeWorker`, `EmployeeScript`, `EmployeeTasks` | implemented with **Luau (mlua) instead of MoonSharp** and **grid pathfinding instead of NavMesh** |
| `content` | ScriptableObject content | implemented; exported by `tools/export_content.py` |
| `persistence` | `GoodsStore`, `PlayerRegistry`, `ClientSettings`, `ClientIdentity` | implemented; **no import of Unity saves** and no compatibility with Unity's snapshot format |
| `protocol`, `net`, `server` | `GoodsNetworkBridge`, `SessionRoot` server half, `SessionAdmission` | implemented: newline JSON over TCP, 1 s ticks, commits every 10 s, baselines every 200 ms per watched site when its revision changes, avatars and employees at 10 Hz |

IDs the Unity game made from GUIDs (jobs, splits, belts) come from a deterministic counter (`next_unique`).

## Client (`src/main.rs` and modules, Bevy 0.20)

| Module | Unity source | Status |
| --- | --- | --- |
| `session` | `SessionRoot` client half, `ClientSiteSubscription`, `DrawnSites` | implemented: host (singleplayer or listening) or join; the layout is regenerated from the seed and its hash checked |
| `space` | `SitePlacement`, `SiteGridSpace` | implemented (map +X east, +Z north; scene north is -Z) |
| `models` | `EquipmentModel` fitting, model merging | implemented; world materials rebuilt from `assets/catalog/world_materials.json` |
| `map` | `WorldLayoutPresenter` | implemented: terrain with covers, water, lots, fields, road/rail tiles, junctions, bridges, signals, buildings, trees; merged per material per 250 m chunk |
| `sites` | `BuildingPresenter`, `RestaurantShellModel`, `EquipmentPresenter`, `BeltPresenter`, `DoorSwing` | implemented: art-kit restaurant walls, doors that swing open, windows, storeys, roofs, ceilings, equipment mounts, belts and riding goods, collision and pick boxes |
| `player`, `camera`, `animation`, `actions` | `PlayerAvatar`, `OrbitCameraRig`, `PlayerAnimation`, input actions | implemented; the camera keeps out of walls and under ceilings |
| `interaction`, `belt_controls`, `hud` | `EquipmentInteraction` (+ Hover, Belts), `PlayerHud` | implemented: aim, reach, hotbar, machine placement ghost, belt drag/turn/lift/remove, items on and off belts |
| `panels`, `phone_map` | `PlayerHud` screens, `PhonePanel`, `PhoneMapView`, `LogisticsPanel`, `EmployeeScriptPanel`, `PropertyPanel` | **simplified layout** (lists and slot rows, not Unity's UXML screens); inventory, machine, phone (drawn map without zoom or pan, shop, staff, places, ledger, trucks and routes, farms), employee tasks editor and Luau program editor, property; the map picture, the task editor (tasks saved and resumed after a restart) and settings were checked in captures |
| `build` | `BuildMode`, `BuildCatalog` | implemented: catalog, walls, doors, back doors, windows, finishes, furnishing fills, belts, add floor, hold-to-remove sweep, pick block |
| `characters` | remote avatars, employee visuals, `CustomerPresenter`, `AmbientPedestrians` | **simplified**: customers queue at registers and sit at tables but do not walk the site's paths; pedestrians walk straight through junctions; pedestrians use the employee model tinted (the street characters have no clips of their own) |
| `vehicles` | `TruckPresenter`, `RoadPose`, `CityCars`, `CityTrafficPresenter` | trucks on legs, docks and yards implemented; **city cars simplified**: deterministic streams that do not queue at stop lines or lights |
| `audio` (feature `audio`) | `GameAudio`, `AmbienceDirector`, `AudioZones`, `EquipmentRunningAudio`, `FootstepAudio` | implemented; loads without errors in a running build, not yet listened to |
| `screens`, `settings`, `ui` | `SessionPanel`, `ClientSettings` | implemented: main menu, resume last session, pause, settings (name, sensitivity, volumes, anti-aliasing, key rebinding) |

## Missing compared with the Unity game

- The phone's recipe page (`PhoneRecipePage`); recipes show on each machine's screen.
- Zooming, panning and clicking lots on the phone's map picture.
- Script dry run and the script assistant.
- Distance culling of far shells and machines (Bevy's frustum culling only).
- The hover outline and cursor tooltip.
- Vehicles bumping the avatar aside (their bodies block it, implemented, untested).
- A verified two-machine multiplayer session (the TCP path is implemented; only the in-process link is tested).
