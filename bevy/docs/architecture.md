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
| `world` (random, settings, layout, geometry, terrain, generator v8, validator, roads, clock) | `FoodFactoryGame.World` | implemented; the generator is a faithful port and the validator runs all of Unity's checks (identity, districts, roads, rails, buildings, lots and service yards, access, start, farms and stations, land, junction controls, rivers and bridges, level crossings, trees); generated seeds pass them all on the first attempt |
| `goods` (state, grid, rules, equipment, production, belts, company, buildings, employees, property, trucks, farms, customers, readiness, layout_content) | `FoodFactoryGame.Goods` (`GoodsWorld.*`) | implemented; covered by `tests/server.rs` for buying, hiring, customer sales and employee scripts |
| `employees` (tasks, navigation, Luau scripts) | `EmployeeWorker`, `EmployeeScript`, `EmployeeTasks` | implemented with **Luau (mlua) instead of MoonSharp** and **grid pathfinding instead of NavMesh** |
| `content` | ScriptableObject content | implemented; exported by `tools/export_content.py` |
| `site_walk` | `Session.Customers.SiteWalk` | presentation-only, headless route helper: customer walkability, street-connected service queues, four-way paths and blocked-route handling; eight geometry tests pass |
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
| `interaction`, `belt_controls`, `hud` | `EquipmentInteraction` (+ Hover, Belts), `PlayerHud` | implemented (HUD after the "Food Factory UI" design: site card, cash with its recent net rate and the clock, alerts for not selling, wages, spoiling stock and notices, the key-cap prompt under the crosshair, key hints): aim, reach, hotbar, machine placement ghost, belt drag/turn/lift/remove, items on and off belts |
| `panels`, `phone_map` | `PlayerHud` screens, `PhonePanel`, `PhoneMapView`, `LogisticsPanel`, `EmployeeScriptPanel`, `PropertyPanel` | implemented with a **simplified layout**: one generic bevy_ui panel in the "Food Factory UI" design's style (dark card, ten-wide slot grids, icon cards, buttons) laid out from each screen's `UiModel`, not the design's bespoke machine, staff and task boards. Inventory with Unity's slot grids (capacity slots, stacks split by max stack, empty slots to drop into), cursor stack (click, right click half, Shift+click across), the Supplier and a Staff summary; machine screens by kind (storage grid alone; sale station "To sell" with its sale value; dock with outgoing and incoming grids, the trucks and farm orders serving it and a back-door warning; table with seats taken and the restaurant's standing; machines input/output and recipes), and inside a factory the Construction section (floors and the next floor's price); phone: map (zoom, pan, lot selection, the Map key), shop, staff for this place (cap, wages, status, Tasks, confirmed Fire), places with readiness, ledger (newest first, readable kinds and what was sold, place, totals), trucks (status with road ETA, destination, route, cargo; route editors with cargo, Apply, Delete; per-site storage and dock stock with Ship/Unstage/Store; confirmed sale), farms (open orders with their trucks, repeating orders with Cancel, dock, delivery or pickup by a parked truck, repeat every N h), recipes grouped by machine with the calculator; property with Buy, rename and Teleport for own places; employee tasks with world picking and the Luau editor with dry run. Slots can also be dragged onto another container (moves the stack) or within one (reorders it; the order is remembered while the client runs); the phone map draws the fleet and clicking a truck opens the Trucks tab with it selected. **Simplified**: Unity remembers exact slot positions with gaps, the port only the stack order |
| `build` | `BuildMode`, `BuildCatalog` | implemented: catalog, walls, doors, back doors, windows, finishes, furnishing fills, belts, add floor, hold-to-remove sweep, pick block |
| `characters` | remote avatars, employee visuals, `CustomerPresenter`, `CompetitorFrontage`, `CharacterFigures`, `AmbientPedestrians` | implemented: site customers enter from the street through customer doors, queue along open cells, walk register to table, sit on the table model's own seats (or generated places beside it; a seat in a wall row moves to the next open one) with legs bent after the animation, and walk back out to the street on departure. Competitor customers arrive along kerb and apron, queue along the facade, go inside to order or eat and come back out. Customers and pedestrians are drawn as the employee-derived model or one of two street characters (driven by the employee clips through name-path animation targets), picked by a stable hash of their ID. Geometry tests pass; captures show each of these (see verification report). **Simplified**: two diners whose seats are all blocked can share a spot |
| `vehicles` | `TruckPresenter`, `RoadPose`, `CityCars`, `CityTrafficPresenter` | trucks on legs, docks and yards implemented; city cars are deterministic streams that queue at stop lines and lights (see below) |
| `audio` (feature `audio`) | `GameAudio`, `AmbienceDirector`, `AudioZones`, `EquipmentRunningAudio`, `FootstepAudio` | implemented; loads without errors in a running build, not yet listened to |
| `screens`, `settings`, `ui` | `SessionPanel`, `ClientSettings` | implemented: main menu, world select (saved worlds with day, cash and sites read read-only from each save; new world with name and seed; play or host), connect to a server, resume last session, pause, settings (name, sensitivity, volumes, anti-aliasing, key rebinding; alternates shown, not rebindable) |
| `front` | (Unity UI Toolkit) | implemented: the bevy_ui front end (`src/front/`, replacing the earlier superui page). One view per surface (`menu_view`, `settings_view`, `hud_view`, `panel_view`) under its own root node (HUD, tooltip, panel, menus, settings, back to front). The game writes one frame resource per surface (`MenuFrame`, `SettingsFrame`, `HudFrame`, `TooltipFrame`) with `set_if_neq`, and panels still describe themselves as a `UiModel`; a surface despawns and rebuilds its subtree only when its frame changes (the panel waits while a slot drag is under way). Buttons push the action strings panels and screens already handle into `UiIntents`; text fields (`EditableText`) write `UiFields`, take back values the game sets while unfocused, keep focus across rebuilds by field id, and set `UiTyping` while focused. Right clicks (`|alt`) and slot drags (`drag>A>B`) are read from bevy_picking on panel buttons. The world-select page, play mode and dropdown are UI-local state (`MenuLocal`). Sliders move in place while dragged and send `s:<id>-set|<value>` on release. Hover colours ease toward their targets (the designs' 0.12 s transitions); scroll offsets survive rebuilds. Passive nodes are `Pickable::IGNORE`, so `pointer_over_ui` counts only buttons, fields and panel cards. The UI scales so its 1600 x 900 design frame fits the window. **Not verified in a running game yet** |

## Recently ported (2026-10-16/17)

- Phone recipe page (`recipes`, `PhoneRecipePage`): whole batches, spare output, expandable ingredient tree, cycle stop.
  Headless test passes; checked in a capture.
- Phone map zoom (buttons and scroll at the pointer), pan (buttons and drag) and lot selection by click. Zoom and pan
  checked in captures; the click hit-test has a unit test against the drawn picture (a real mouse click is not captured).
- Employee script dry run (the program editor's Dry run button): runtime errors, bounded infinite loops, stand-ins for the employee API.
  Headless tests pass.
- Hover outline (bounds wireframe, no shadow) on aimed equipment and employees, checked in a capture; the free-pointer
  cursor tooltip is **implemented, untested** (captures run with the pointer locked, where the HUD hint shows instead).
- Distance culling: shells beyond 140 m and machines beyond 70 m of the avatar are hidden (an approximation of Unity's
  size-based LOD cutoffs). Unit test passes; **not yet measured in a running game**.
- Vehicles bump the avatar aside (Unity `VehicleBump`): out of the body the short way, shoved with the vehicle's
  speed, and knocked to the vehicle's nearer side when hit by a bumper. Unit tests pass; a capture shows a passing car
  moving the avatar from the road onto the sidewalk.
- Customers walk out on departure, sit on table seat parts with a seated leg pose, and competitor customers walk their
  frontage (above). Street-character models for customers and pedestrians.
- Avatar elevator rides (Unity `ElevatorRider`): Floor up / Floor down on the elevator cell moves to the same cell one storey
  up or down. Unit test passes; not yet ridden in a running game (no multi-storey factory in the captures).
- HUD restaurant card (Unity `PlayerHud.Readiness`): what stops sales or that it is open, warnings, last 10 minutes of sales
  and spending, under the clock while no screen is open. Checked in captures.
- Aiming at a purchasable map building within reach and pressing E (or left click) opens its property panel (Unity
  `PropertyMarker`). Implemented, untested in a running game.
- Storage shelves show their box stages as the input fills (Unity `StorageFillDisplay`); stage order assumed `Boxes_1`
  bottom first. Unit test for the stage count; not yet seen filled in a running game.
- Employee task targets can be picked in the world (Unity's pick mode): the screen hides, valid machines are outlined,
  click or E chooses, Esc cancels. Checked in a capture.
- Vehicle engine loops (Unity `VehicleEngineAudio`): the ten nearest vehicles within hearing play the engine clip with
  speed-driven pitch and level, Unity's rolloff and a mild Doppler. Rolloff test passes; not yet listened to.
- Inventory cursor stack, Shift quick transfer, the Map key, and confirmations before firing an employee or selling a truck.
  Stack transfer unit test; the fire confirmation and the Map key checked in captures; slot clicks not yet driven in a capture.
- Build catalog structure cards carry icons rendered at runtime from the art-kit models (Unity `StructureIcons`). Checked
  in a capture.
- City cars queue nose-to-tail at stop lines and lights and hold for trucks and crossing pedestrians; pedestrians cross
  only on a green long enough to finish (`traffic`, `pedestrian_routes`). Headless tests pass; continuous motion in a
  running game is **not yet verified** (still images cannot show it).

## Missing compared with the Unity game

- The script assistant (absent from the active Unity UI by decision 0037; not selected for the port).
- A verified two-machine multiplayer session (localhost TCP between two clients is tested; two physical machines are not).
