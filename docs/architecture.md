# Architecture and Implementation Status

## Authority

Gameplay requirements and open product decisions are in [the GDD](../Food_Factory_Restaurant_GDD.md). Resource facts are in [dev_resources.md](../dev_resources.md). This document distinguishes implementation from intended architecture.

## Implemented Baseline

- Unity `6000.5.9f1`, URP `17.5.0`, and Input System `1.20.0`.
- `Assets/Scenes/DevSite.unity` is the only enabled build scene (session bootstrap, below). `Assets/Scenes/SampleScene.unity` and its oven prototype remain in the project but are no longer built. `Assets/Scenes/WorldGen.unity` (world generation, decision 0026) is also not a build scene.
- `Assets/InputSystem_Actions.inputactions` is imported starter input authoring, not completed gameplay controls.
- FishNet `4.7.3` is vendored under `Assets/FishNet`, including its original metadata, demo references, and license files. See [decision 0001](decisions/0001-reproducible-baseline.md).
- `Assets/DefaultPrefabObjects.asset` references FishNet demo prefabs and is auto-maintained by FishNet's prefab generator, which also appends the project's spawnable prefabs. No game NetworkManager uses it; `DevSite` uses the project-owned `Assets/Network/GamePrefabs.asset`.
- `Assets/Tests/EditMode` contains four authoring/dependency checks in `FoodFactoryGame.Baseline.EditModeTests`. Tests open build scenes as isolated preview scenes and do not touch application saves.
- A limited goods domain, a FishNet transport bridge, and a prototype session bootstrap (direct-IP host/join, SQLite player identity, networked avatar) exist. See below.

## Accepted Foundational Multiplayer Design

[Decision 0002](decisions/0002-authoritative-multiplayer-foundation.md) is the
accepted technical starting design. It is a contract for feature work, not a
claim that the runtime exists.

- A server owns gameplay state and validates client requests. A listen server
  is the initial development path, with a headless-compatible simulation that
  is independent of the host camera, local player, and presentation scenes.
- The server uses one explicit world simulation clock, with subsystem-specific
  fixed-step or scheduled updates. All sites use the same model initially.
- Replication is interest-based. Remote management requires an explicit,
  server-validated connection subscription; presentation visibility does not
  control simulation.
- Persistence is made of server-owned, versioned snapshots with stable domain
  IDs and explicit recovery of reservations and in-flight operations.
- Goods are server-owned, location-based lots with selective visual
  representation. Splits preserve spoilage history; only compatible lots merge.
  [Decision 0003](decisions/0003-physical-goods-model.md) records the selected
  model and its still-pending implementation.

The first network integration targets the vendored FishNet `4.7.3` snapshot.
The existing demo prefab catalog remains baseline authoring only.

## Implemented: bounded goods slice (2026-09-22)

- `FoodFactoryGame.Goods` holds server-instantiated stable-ID lots and locations, integer quantities, one location per lot, owner/site grants, binary spoilage, elapsed exposure, active reservations, terminal command outcomes, and an integer-second authoritative clock. Ambient time accumulates exposure; refrigerated time does not (confirmed by the owner in [decision 0018](decisions/0018-spoilage-timing-and-refrigeration.md)). Moves retain prior exposure. Equivalent lots merge only when owner, location, item, condition, and exposure/threshold match; reserved lots cannot merge.
- `Transfer` validates a same-site route, grant, source owner, unreserved quantity or owned reservation, and destination unit capacity before locked mutation. A partial transfer splits with a new ID. Duplicate request IDs replay stored terminal outcomes; another actor cannot replay someone else's outcome. `Cancel` releases an unconsumed reservation; committed transfers are not reversible. No in-flight transport or cross-site route has been implemented.
- `GoodsSnapshotStore` serializes (schema v4 since conveyor belts, below) world ID, time/revision, lots, locations, grants, reservations, and outcomes with a checksum. An explicit path is required; each commit is one SQLite transaction that keeps the previous committed revision as a fallback (see [SQLite storage](#implemented-sqlite-storage-2026-09-23)). Recovery validates invariants; unknown newer schemas fail. Concurrent saves reject older/conflicting revisions. Every acknowledged mutation has a durable boundary: `TransferDurably`, `ReserveDurably`, `CancelDurably`, and the clock tick `TryAdvanceDurably` commit the snapshot before acknowledging and restore the pre-command state if the commit fails. Terminal outcomes are keyed per actor (player ID + request ID), so one actor cannot claim or poison another actor’s request ID. This is a goods-slice snapshot, **not** the full-world decision-0002 persistence contract; no production snapshot cadence is implemented; the only migrations are the in-memory v1→…→v9 upgrades (v5: company cash; v6: sale jobs; v7: building shells; v8: factory floors; v9: employee records; all below).
- `GoodsNetworkBridge` is compiled FishNet RPC transport for connection-resolved transfer intent/result and server-validated, revisioned full site baselines. It ticks the same world even with zero subscribers. Its `InitializeServer` requires a pre-existing matching committed save and an authenticated connection-to-player resolver supplied by a session owner. It also exposes reserve/cancel RPCs through the durable paths and rejects requests while clock persistence is failing. An Editor PlayMode listen-server fixture (host + separate loopback-UDP remote client, test-owned bootstrap and connection→player map) has demonstrated an authorized transfer, an unauthorized remote transfer and subscription rejection, a granted site baseline, and unsubscribed site progression persisted to the snapshot. There is still no production session owner or authenticator, player build, client UI, pickup visuals, or multi-process multiplayer evidence. Full baselines (not deltas) are used to avoid a partial replication protocol.
- EditMode tests with *test-only* restaurant/ingredient/storage/fridge/kitchen capacities and spoilage threshold verify domain and isolated file recovery without any camera or client. The PlayMode fixture above covers the live FishNet path within one Editor process; it does not prove pickup projections, real authentication, or separate-process play. See [verification record](verification/goods-20260922.md).

## Implemented: station jobs (domain) (2026-09-22)

Rules are in [decision 0004](decisions/0004-station-job-rules.md). This step was domain code only. The job network command, recipe content and the oven's running display came with the working oven (below). Placed stations now come from equipment (below).

- `GoodsWorld` is `partial`. Station and job code lives in `GoodsWorld.Production.cs` and shares the goods lock and snapshot, so input consumption, output creation and pickup refunds commit atomically with the goods.
- `RecipeDefinition` is content registered by the server with `RegisterRecipe`. It is not saved. `GoodsStation` (site, kind, input and output location) is created with the server-only `Bootstrap(GoodsStation)`.
- `StartJobDurably` goes through the existing `Commit` wrapper (replay, rollback, `persistence-unavailable`). It checks identity/replay, then station and grant (`forbidden`), recipe and kind (`invalid-recipe`), then one job per station (`station-busy`), then edible unreserved inputs (`missing-inputs`). Only after all checks does it consume the inputs, most-exposed first, and record `GoodsOutcome.JobId`. The job keeps copies of its recipe output and duration and of the consumed input slices.
- `Advance` (and therefore `TryAdvanceDurably`) progresses jobs. Completion happens at exactly `StartedAt + Duration` and emits `<jobId>:out`, owned by the station's site. Ambient overshoot counts as exposure. A full output location marks the job `Blocked`, and it retries on each step.
- Machine pickup was first modelled as `RemoveStationDurably`. Since equipment placement (below) it is part of `PickUpDurably`, and stations are created only with placed equipment.
- Snapshot schema v2 adds `Stations` and `Jobs`. `GoodsSnapshotStore` upgrades v1 in memory and rejects anything newer than v2. `Validate` adds rules for station identity and same-site locations, and for job/station references: at most one job per station, remaining time within range, blocked means zero remaining, and no output or input ID colliding with a live lot. `View` includes the site's stations and their jobs.
- The recipes, capacities and durations in `StationJobTests` are test-only. See [verification record](verification/station-jobs-20260922.md).

## Implemented: session bootstrap and networked player (2026-09-22)

Decisions are in [decision 0005](decisions/0005-session-bootstrap-and-player-identity.md). Assembly `FoodFactoryGame.Session` (`Assets/Scripts/Session`).

- `SessionRoot` (in `DevSite`) is the server composition root, with Host, Client and Server-only modes. Server start creates the save directory, loads `world.db` (importing a pre-SQLite `world.snapshot` once) or creates and commits the **development seed** (`DevWorld`: world `dev-world`, site `dev-site`, one storage location; placeholder content), opens the SQLite `players.db`, configures the authenticator, and only then starts FishNet. Once listening, it spawns `GoodsNetworkBridge` and calls `InitializeServer` with `DevAuthenticator.PlayerIdOf` as the only connection→player resolver. The bridge ticks the world whenever the server runs, including with no clients (`-server`).
- `PlayerRegistry` (SQLite, server-only) maps SHA-256(client secret) to a stable `player-<guid>` ID; raw secrets are never stored. `SessionAdmission` resolves or creates the identity first, then commits the `dev-site` grant with `GoodsWorld.TryGrantDurably`; either failure rejects the join. Registry schema is `user_version` 1; newer is refused. The goods snapshot stayed at v2 in this step (v3 since equipment placement).
- `DevAuthenticator` (FishNet `Authenticator`): clients send `{DisplayName, Secret}`; the server answers `{Accepted, Reason, PlayerId}` before passing or failing the connection. Rejection reasons: `invalid-name`, `invalid-secret`, `persistence-unavailable`, `already-connected`, `server-full` (cap 8), `server-not-ready`. `already-connected` is checked with a read-only lookup before any write. A rejected client disconnects itself after reading the reason; the server kicks it only after a 2 s grace, because FishNet's forced close raced the reply over real UDP. The connection→player map is server memory only and is cleared on disconnect.
- `ClientIdentity` keeps the client secret in the SQLite database `persistentDataPath/Identity/identity.db` (`-identity <file>` override; decision 0011).
- FishNet sends start scenes only after authentication, so `SessionRoot` spawns one `Player` per connection on `OnClientLoadedStartScenes`, owned by that connection and with a server-set display name. FishNet despawns it on disconnect; the world keeps running.
- `Player.prefab`: `NetworkObject`, client-authoritative `NetworkTransform`, `CharacterController`, `PlayerAvatar` (camera-yaw-relative `Player/Move` at 4 m/s, `Player/Sprint` (Left Shift) at 7 m/s, `Player/Jump` (Space) to 1.2 m while grounded; a ceiling ends the rise), the animated character `Model` (implemented 2026-09-29: `Assets/Art/Models/Player/Player.fbx`, built by `ArtSource/Player/build_player_model.py` and installed by `AgentScripts/BuildPlayerVisual.cs`; a factory worker (hard hat with goggles, rolled-sleeve work shirt, denim bib apron with leather strap, pocket, pen and ID badge, gloves, cargo pants with reflective bands, laced boots) as one ~22k-triangle skinned mesh with two materials: `PL_Shirt`, tinted per display name, and `PL_Palette`, whose faces sample flat cells of `Textures/Player_Palette.png` and its metallic/smoothness map; the apron skirt is weighted to the thighs so strides do not cut through it; `Player.controller` blends Idle/Walk/Run on `Speed` and plays Jump, Fall and Land from `Grounded` and `VerticalSpeed`), `PlayerAnimation` (presentation only: sets those parameters from the avatar's observed motion and a ground ray, so remote copies animate from the replicated pose with no animator replication; a per-frame jump over 1.5 m counts as a teleport), and a disabled `CameraRig` (`OrbitCameraRig`, camera, audio listener) that only the owning client enables. It orbits with `Player/Look` (always on since 0007; formerly while `Player/Orbit` was held); `Player/Zoom` (scroll) steps distance. Pitch is limited to 10–80° and distance to 3–20 m. `Player/SwitchCamera` (C; the C binding moved off the unused `Crouch`) toggles a top-down view that looks straight down on the avatar with yaw snapped to the nearest 90°, so the world-aligned site grid reads horizontal/vertical. The two views blend over 0.4 s (smoothstep), each keeps its own zoom distance, and `Yaw` follows the blend so movement stays screen-relative. The top-down view does not orbit; `EquipmentInteraction` leaves the pointer free there (no crosshair) and aims at the mouse position. Implemented; verified by authoring and placement tests, not yet by a running-game capture.
- `ClientSiteSubscription` subscribes an authenticated client to `dev-site` once the bridge is visible. `SessionPanel` (UI Toolkit, built in code, `Assets/UI/SessionPanelSettings.asset`) shows the name/address/Host/Join menu with status and rejection reasons, then a readout of mode, player ID, the replicated site clock and revision, and (on a server) the server clock, revision and player count.

Prototype, labelled in code: the authenticator (no encryption or accounts), owner movement authority (presentation only; no gameplay rule trusts position), the all-players `dev-site` grant, the 8-player cap, and the dev seed.

Still open: hosting model, host leaving/migration and disconnect grace, and player count (GDD); interaction range; the indoor camera; character art.

## Implemented: equipment placement (2026-09-22)

Decisions are in [decision 0006](decisions/0006-equipment-placement-and-inventory.md). Goods snapshot schema **v3**.

- Domain (GoodsWorld.Equipment.cs, SiteGrid.cs): `GoodsEquipment { Id, Kind, SiteId, State: Placed | Held, HolderId, CellX, CellZ, Rotation, Width, Depth, InputCapacity, OutputCapacity, OutputRefrigerated }` and `SiteLayout { SiteId, Width, Depth }`. The server-only `Bootstrap(GoodsEquipment)` creates a placed piece with its station and buffers `<id>:in`/`<id>:out`.
- `PickUpDurably(player, request, equipmentId)` checks identity/replay, grant (`forbidden`), placed (`not-placed`), the player's inventory `carried:<playerId>` on that site (`no-inventory`), no reservations on buffered lots (`reserved`), and that the job refund plus both buffers fit (`capacity`). It then moves the goods, removes the job, station and buffers, and marks the piece held, in one commit. `PlaceDurably(player, request, equipmentId, x, z, rotation)` checks grant, `not-held`, `invalid-rotation`, `out-of-bounds` and `blocked` through the shared `SiteGrid.PlacementProblem`, then recreates the station and buffers under the same IDs. Both use the existing `Commit` wrapper (replay, rollback, `persistence-unavailable`).
- `TryGrantDurably(..., inventoryCapacity)` creates the player's inventory together with the grant; `SessionAdmission` passes the dev capacity (10).
- `View` adds the site's equipment (placed and held) and layout. `Validate` enforces the rules listed in 0006.
- Network: `GoodsNetworkBridge.RequestPickUp` / `RequestPlace` ServerRPCs resolve the player from the connection and rebroadcast a full baseline to every subscriber after an accepted command. `ClientSiteSubscription` exposes its `Bridge` and forwards results (`ResultReceived`).
- Presentation (`Assets/Scripts/Session/Equipment`, in `DevSite`): `EquipmentPresenter` builds one local, non-networked visual per placed piece from its `EquipmentDefinition` prefab. The visual is centred on the footprint and stood on the floor using its rendered bounds, keyed by equipment ID. It is removed while the piece is held and never owns state. `EquipmentInteraction` (local player only) raycasts through the owned avatar's camera. As first built, `Player/Interact` (E) picked up; since 0007 the controls are those below. While a machine is on the cursor, a ghost footprint follows the aim point, green or red from the same `SiteGrid` rule, `Player/Rotate` (R / right shoulder) turns it, and `Player/Place` (left mouse / right trigger) sends the request. Nothing moves until the next baseline. The `SessionPanel` readout shows the hint and the last rejection reason. The grid is centred on the scene origin (`SiteGridSpace`).
- Content: `Assets/Content/Equipment/Oven.asset` (kind `oven`, 3×3, input 10, output 4, `Oven.prefab`). `SessionRoot.equipmentDefinitions` lists it; the dev seed places `dev-oven-1` at (12, 13) on a 20×20 grid in a **new** world only.
- `SessionRoot.Begin` now refuses while the transport is still stopping (`CanBegin`). Previously a same-frame Stop→Start let the old server's late `Stopped` event release the new world, so the bridge never initialized.

Prototype, labelled in code or content: free placement, no range or line-of-sight check, dev grid size and footprint, dev inventory capacity, and grid-to-scene mapping centred on the origin. Scene landmarks are not placement blockers; building walls are server data and are (decision 0019, below).

Not yet: buying/selling equipment, other machines, and moving equipment between sites. Starting jobs, the running display and the hotbar came with the working oven (below); belts came later (decision 0010).

## Implemented: Factorio-style controls and a working oven (2026-09-22)

Decisions are in [decision 0007](decisions/0007-player-controls-and-working-oven.md). No new server rule and no schema change.

- Controls (`EquipmentInteraction`, `OrbitCameraRig`): the pointer is locked to a centre crosshair while no screen is open, so `Player/Look` always orbits; the rig pivots 1 m beside the avatar. `Player/Inventory` (E) toggles the inventory screen, `Player/Hotbar` (1–9, one action whose bindings scale to the slot number) puts a held machine kind on the cursor, `Player/ClearCursor` (Q) empties it, `Player/Place` (left mouse) places the cursor item or opens the machine under the crosshair, `Player/Remove` (right mouse) picks the machine up, `Player/Rotate` (R) turns the ghost, `Player/CloseScreen` (Esc) closes a screen or releases the pointer. Aim rays skip player avatars. `Player/Orbit` is gone.
- HUD (`PlayerHud`, UI Toolkit built in code, its own `UIDocument` on the session panel settings): crosshair, 9-slot hotbar (kind and held count), the inventory screen (your machines and goods by item/spoiled beside `dev-site-storage`) and the machine screen (recipe picker, Start, progress, input, output). Every click is a request: stack moves use `GoodsNetworkBridge.RequestTransfer` (whole lots up to the destination's previewed free capacity), Start uses the new `RequestStartJob` → `StartJobDurably`, one batch per click.
- Content: `RecipeAsset` (`Assets/Content/Recipes/Bread.asset`: `oven-bread`, 1 dough → 1 bread, 10 s, bread spoils after 3600 s). `SessionRoot.recipes` lists it; `StartServer` registers every recipe with the world, including a recovered save.
- Dev ingredients: a new dev world's storage gets 20 dough; `TryGrantDurably(..., starterGoods)` adds 5 dough (`starter:<playerId>:0`) to a player's inventory only in the commit that creates it. Dough spoils after 7200 s. Existing saves and existing inventories get neither.
- Display: `EquipmentPresenter` sets `EquipmentVisual.Running` from the baseline (a running, not blocked, job on the station), which drives every `IEquipmentRunningDisplay` in the model; `OvenToggle` implements it (glow, light, fans).

Prototype, labelled in code or content: the dev recipe, dough stock and spoil times, hotbar slots in content order, the 1 m shoulder offset, and storage as the inventory screen's second panel. Open: any granted player can transfer out of another player's inventory location (a pre-existing transfer rule); no gamepad hotbar or screen navigation; reach is wherever the crosshair meets the floor.

## Implemented: slot-grid UI and automatic machines (2026-09-23)

Decisions are in [decision 0008](decisions/0008-slot-grid-ui-and-automatic-machines.md); it supersedes 0007's one-batch-per-click rule and list-style screens. No schema change.

- Server rule: `GoodsWorld.AutomaticJobs` (configuration, not saved; `SessionRoot.StartServer` turns it on). `StartReadyJobs` runs inside every accepted `Transfer` and every `Advance`: an idle station starts the lowest-ID recipe for its kind whose inputs are present and whose output fits now (`StartedBy = "automatic"`). `StartJob` and `RequestStartJob` remain for explicit starts.
- HUD (`PlayerHud`): inventory, storage, and machine input/output are slot grids of stacks (item × spoiled; held machines × kind) with icons and counts. The machine screen is input slot → progress bar → output slot plus a status line. Slot positions are client-only (`_arrangement`); a drop claims its slot until the server answers.
- Cursor (`EquipmentInteraction`): `CursorGoods` names a stack in a container; `DropGoods` sends ordinary transfers. `PickUpMachine` puts a held kind on the cursor from the grid. The cursor icon follows `Player/Point`.
- Ghost: `EquipmentModel.CreateGhost` copies the kind's visual prefab without behaviours, colliders or lights, with `EquipmentGhost.mat` (transparent URP Lit), tinted by the placement preview over the flat footprint. `EquipmentModel.Create` is the shared centring used by `EquipmentPresenter`.
- Content: `ItemDefinition` (`Assets/Content/Items/Dough.asset`, `Bread.asset`), `EquipmentDefinition.icon`, DEVELOPMENT icons in `Assets/Art/Icons` (drawn by `AgentScripts/DrawItemIcons.ps1`).

Prototype: icon art. Open: per-machine recipe choice, restricting inputs to ingredients, stack splitting. (Grid size, stack limits and quick transfer are now covered by decision 0009, below.)

## Implemented: slot capacity, max stacks and quick transfer (2026-09-23)

Decisions are in [decision 0009](decisions/0009-slot-capacity-and-quick-transfer.md). No schema change; `GoodsLocation.Capacity` now counts slots.

- Domain (`GoodsSlots`, `GoodsWorld`): each (item, spoiled) stack in a location takes `ceil(quantity / max stack)` slots. `GoodsWorld.RegisterItem(itemId, maxStack)` is content (not saved; unregistered items stack to 1, which keeps unit semantics for them). `Transfer`, lot bootstrap, starter goods, automatic starts, job output and equipment pickup check slots. `Validate` no longer checks capacity: over-fullness from spoilage or a smaller content max stack is legal state that blocks entries and never removes goods. `TryGrantDurably` enlarges an existing smaller inventory to the configured slot count (never shrinks it).
- Session: `SessionRoot.items` holds the `ItemDefinition`s (moved from `PlayerHud`); the server registers each `MaxStack` on start, and clients use `SessionRoot.MaxStack` for previews. Dev content: dough and bread stack to 20, inventories and new dev storage have 30 slots, the oven has 1 input and 1 output slot.
- HUD/controls: a grid has one slot per unit of capacity; a stack is split into slots of at most its max stack, and picking a slot carries just that slot's quantity (`CursorStack.Quantity`). `Player/QuickTransfer` (Shift) + click calls `PlayerHud.QuickTransferSlot` → `EquipmentInteraction.TransferStack`, which moves the slot's stack to the other open container (inventory ↔ storage; inventory → machine input; input/output → inventory), capped at the destination's free room for that stack. A slot click counts only if its press began after the press that opened the screen was released (`EquipmentInteraction.ScreenClicksArmed`, sampled by a trickle-down `PointerDownEvent` on the screen).

Existing saves keep their recorded capacities, now read as slots, except machine buffers: on every server start `GoodsWorld.ApplyEquipmentCapacitiesDurably` sets each saved machine to its `EquipmentDefinition` slot counts (committed before serving; over-full buffers keep their goods). An already-created dev storage keeps 100 slots. Slot presses are read from `PointerDownEvent`/`PointerUpEvent` on each slot because `Button.clicked` ignores modified (shift) clicks. Open: whether held machines should take inventory slots on the server (the HUD shows them in overflow slots).

## Implemented: conveyor belts (2026-09-23)

Decisions are in [decision 0010](decisions/0010-conveyor-belts.md). Goods snapshot schema **v4**.

- Domain (`GoodsWorld.Belts.cs`, `BeltRules.cs`): `GoodsBelt { Id, SiteId, CellX, CellZ, Direction }` with one location `<id>:items` (kind `belt`); `GoodsLot.BeltPosition` (0..239 path units) while riding. `PlaceBeltDurably(player, request, site, x, z, direction)` checks identity/replay, grant (`forbidden`), `invalid-rotation`, then turns an existing belt (`rotated`/`unchanged`, free) or checks `out-of-bounds`/`blocked` (`SiteGrid.CellProblem`, shared with equipment), the player's inventory (`no-inventory`) and an unreserved belt item (`no-belts`) before consuming one and creating the belt. `RemoveBeltDurably` returns the belt item and every riding lot to the remover's inventory all-or-nothing (`reserved`, `capacity`). `PlaceOnBeltDurably(player, request, lot, belt)` moves exactly one unit to the free position nearest the belt's middle (`belt-full`, `invalid-route` from a belt), splitting under a new ID. `TakeFromBeltDurably(player, request, lot)` moves one riding lot into the taker's inventory under the same ID (`not-on-belt`, `no-inventory`, `reserved`, `capacity`). `Transfer` refuses belt locations; `Merge` skips riding lots. `Advance` moves riding items (`MoveBeltItems`: 1 tile/s, 8 sub-steps/s, downstream first, 60-unit spacing, queuing, curves and side-loading). `Validate` checks belt cells, locations and single-unit riding lots; `View` includes the site's belts. v3 saves upgrade in memory.
- Network: `GoodsNetworkBridge.RequestPlaceBelt` / `RequestRemoveBelt` / `RequestPlaceOnBelt` / `RequestTakeFromBelt`, full-baseline rebroadcast after an accepted command.
- Presentation (`Assets/Scripts/Session/Belts`, `EquipmentInteraction.Belts.cs`): `BeltPresenter` builds one local belt per replicated belt from `BeltStraight`/`BeltCornerLeft`/`BeltCornerRight` prefabs (tile-centred, +Z travel, trigger collider, `BeltVisual`), picked by `BeltRules.Shape`; one shared runtime tread material scrolls at 0.5 UV/s so all arrows line up; riding goods are camera-facing icon sprites gliding along the path (`BeltPath`) toward the latest baseline. `EquipmentInteraction` adds the belt drag (R with the crosshair beside the line corners the head toward it and lays belts through the crosshair's cell; R over a belt mid-drag does nothing; holding R repeats this as the crosshair leaves the line), R on a belt, hold-right-click removal, pending-belt ghosts with their would-be shape, and the item ghost plus `Player/PlaceItem` (Z), and `Player/TakeItem` (F) taking the riding item nearest the crosshair. `CloseScreen` keeps an inventory stack on the cursor; `PlayerHud` shows it beside the crosshair.
- Content: `Assets/Content/Items/Belt.asset` (stack 100, icon from `DrawItemIcons.ps1`), belt models in `Assets/Art/Models/Belt` (the 1 m straight was cut from the 2 m module into `ArtSource/Belt/Conveyor_Straight_1m.*`), materials in `Assets/Materials/Belt`, `BeltItemSprite.mat`. `DevWorld`: 200 storage belts and 50 starter belts in new worlds/inventories; `EnsureBeltStock` adds the storage belts once to a save that has never had belts. `DevWorld.LoadOrCreate` now registers item max stacks before seeding.

Prototype: one lane, belt speed and spacing, dev belt stock, and trailing (non-predicted) item drawing. Not yet: belts feeding or emptying machines, splitters/undergrounds, gamepad belt building.

## Implemented: SQLite storage (2026-09-23)

Decision: [0011](decisions/0011-sqlite-for-all-data-storage.md). All persisted data uses SQLite; no new save-file formats.

- `GoodsSnapshotStore` keeps the same `Save(world, path)` / `Load(path)` contract on a SQLite database (`world.db`): `snapshots` rows hold the versioned JSON payload and its SHA-256. `Save` runs in one `BEGIN IMMEDIATE` transaction (`synchronous = FULL`), refuses stale/conflicting revisions, inserts the new revision and keeps only it and the prior valid row. `Load` returns the newest row that verifies; unverifiable rows are moved to `quarantined_snapshots` on the next save. Database layout is `user_version` 1 (2 since decision 0026 added `world_layout`); newer is refused. `ImportLegacy(legacy, database, dryRun)` reads a pre-SQLite `world.snapshot` (or `.previous`), upgrades it in memory, and writes a new database unless dry-run; it refuses an existing database and never touches the legacy file. `DevWorld.LoadOrCreate(..., legacyWorldPath)` runs the dry run and import once when `world.db` is missing.
- Commit cost ([decision 0015](decisions/0015-wal-and-held-world-connection.md), 2026-09-24): goods databases use WAL with `synchronous = FULL`. `GoodsSnapshotStore.Hold(path)` keeps one connection open for `Save` until `Release(path)`; the goods bridge holds the served save from `InitializeServer`, and `SessionRoot.Shutdown`, `OnStopServer` and the bridge's `OnDestroy` release it. A held connection is dropped after a failed commit and reopened by the next `Save`. Commits remain synchronous on the main thread.
- Held-connection fast save (2026-09-25): after a successful write, `GoodsSnapshotStore` caches that validated row in memory. A later save compares the database's latest revision, world ID, payload and checksum inside `BEGIN IMMEDIATE`; an exact match skips rereading and validating the previous world. Any difference uses `LatestValid` and its quarantine behavior. The cache is removed on release or failure; loads and unheld saves retain full validation. The SQLite layout, WAL `FULL` sync, previous-revision recovery and command acknowledgment rules are unchanged. [Verification](verification/fast-save-20260925.md). Decision 0025 is deferred after [the customer-scale rerun](verification/customer-scale-performance-20260925.md).
- Tick commits ([decision 0016](decisions/0016-periodic-tick-commits.md), amended 2026-09-25): the served clock advances in memory each second (`AdvanceUncommitted`) and is committed every 10 s (`TryCommitDurably`, a no-op when `HasUncommittedChanges` is false); player commands still commit before acknowledging, saving pending ticks with them; a clean stop saves pending ticks. A crash or tick exception can lose up to 10 s of simulation, rolled back as one revision. A tick exception restores the last committed payload held in memory; the initial save is required before uncommitted ticking and serving. The bridge broadcasts a new rollback epoch so subscribed clients accept the lower revision and reject late baselines from older epochs. Player commands retain their pre-command copy and retry behavior. While a periodic commit fails, commands are refused and the clock waits with unsaved memory intact.
- `ClientIdentity.LoadOrCreate(path, legacySecretPath)` stores the client secret in a one-row SQLite `identity` table (`INSERT OR IGNORE`, so concurrent first runs agree), importing the default `client.secret` into a new database so an existing client keeps its player ID.
- `PlayerRegistry` was already SQLite (decision 0005).
- The goods payload is still not relational; world and registry are still separate databases. Decision 0012 keeps it that way for now, rules out mixing payload and tables, and sets the signals for moving the whole world to tables.

## Implemented: company cash (2026-09-24)

Decision: [0012](decisions/0012-company-cash.md). Step 1 of the sell loop: cash exists, is saved and replicated; nothing earns or spends it yet.

- Domain (`GoodsWorld.Company.cs`): `GoodsCompany { Id, Cash (whole cents, long), SiteIds }` in `GoodsSnapshot.Companies`. Server-only `Bootstrap(GoodsCompany)` rejects blank/duplicate IDs, negative cash, unknown sites, and a site already owned. `CompanyOfSite(siteId)`. Private `TryCredit`/`TryDebit` (positive amounts only; debit refuses overdraw; `checked`) are for later commands to call inside their own commit. `internal AdjustCashDurably(company, delta, savePath)` returns null or `unknown-company` / `invalid-amount` / `insufficient-funds` / `persistence-unavailable`, restoring the prior state on any failure (overflow rethrows after restoring). `Validate` checks unique non-blank IDs, cash ≥ 0, existing sites, one owner per site.
- Persistence: payload schema **v5** (`world.db` `user_version` still 1); v4 upgrades in memory with no companies. `View` includes only the owning company of the viewed site, so cash reaches clients in the existing full site baseline with no new RPC.
- Dev seed (`DevWorld`): PROTOTYPE `dev-company` owns `dev-site` with 50000 cents. `EnsureCompany` adds it once to an older save, committed before serving.
- HUD: `PlayerHud` shows the site company's cash top right (`hud-cash`, `PlayerHud.FormatCash`), display only.
- Measurement: `GoodsSnapshotStore.Stats` (`GoodsCommitStats`) records each save that writes a new revision: its time (live-state validation and one JSON conversion under the world lock, the SQLite transaction before `COMMIT`, and `COMMIT` including WAL sync) and payload bytes. `LastTimings` exposes the last successful write's phase split for the benchmark (the copy phase is now zero); phase time excludes waits for the save lock and another writer. Failed saves and saves of an already-stored revision are not counted. `GoodsNetworkBridge.InitializeServer` resets the counters and one-time warnings, so they describe the served world only. The bridge logs `[Goods] commits=… avg=…ms max=…ms payload=…KB` every 60 s and warns once per served world past 50 ms or 1 MB. The [final 1,000-customer record](verification/customer-scale-performance-20260925.md) supports deferring [decision 0025](decisions/0025-save-cost-at-customer-scale.md); no storage migration is implemented.
- Every durable command, the clock tick, admission grants and `AdjustCashDurably` go through one private boundary, `GoodsWorld.Durably` (run, commit if the revision changed, restore the prior state on any failure).
Open: debt, several companies per world, member permissions, player-set prices; purchases (step 3). Sales: step 2, below.

## Implemented: sell counter (2026-09-24)

Decision: [0013](decisions/0013-sell-counter.md). Step 2 of the sell loop: edible goods placed in a counter earn company cash. PROTOTYPE stand-in for customers.

- Domain (`GoodsWorld.Production.cs`): `RecipeDefinition.SaleCents` / `StationJob.SaleCents` (`IsSale`). A sale recipe has no goods output (validated at registration: exactly one of goods or cash). Sale jobs use the unchanged job rules: edible unreserved inputs, consumed at start, one per station, automatic start, server-clock progress, refund on pickup. Completion removes the job and calls `TryCredit` for the site's company inside the same `Advance`, so the tick commit carries goods and cash together. Starts are refused without a company (`no-company` for explicit starts; automatic starts skip). `Validate` rejects a blocked sale job, a sale job with goods output, and a sale job on a site no company owns.
- Persistence: payload schema **v6** (v5 upgrades with no data change; `world.db` `user_version` still 1).
- Content: `RecipeAsset.saleCents`; `Assets/Content/Equipment/Counter.asset` (kind `counter`, 2x1, input 2 slots, unused output 1 slot), `Assets/Prefabs/Equipment/Counter.prefab` (primitives, one root box collider, `Assets/Materials/Counter`), `Assets/Art/Icons/Counter.png`, `Assets/Content/Recipes/SellBread.asset` (`counter-sell-bread`: 1 bread, 5 s, 250 cents). All authored by `AgentScripts/BuildDevSite.cs`; `SessionRoot` lists `[oven, counter]` and `[bread, sell bread]`.
- Dev seed (`DevWorld`): `dev-counter-1` placed at cells (6, 13) in new worlds; `EnsureCounter` adds it once to a save with no counter (committed before serving; warns and skips if the cells are taken).
- Presentation: the machine screen shows a sale station's prices (`hud-sale-prices`) instead of an output grid; progress reads "Serving a customer: <item> for <price>"; an ownerless site reads "no company to sell for"; the readout hint is counter-specific.
Superseded 2026-09-25 by customers (decision 0024, below): sale recipes are now menu items only customers buy; stations never start them. Purchases: step 3, below.

## Implemented: supplier purchases (2026-09-24)

Decision: [0014](decisions/0014-supplier-purchases.md). Step 3 of the sell loop: company cash buys inputs. PROTOTYPE stand-in for supply logistics.

- Domain (`GoodsWorld.Supply.cs`): `PurchaseOffer` content (`RegisterOffer`, never saved). `BuyDurably(player, request, site, offer)` through the existing `Commit` wrapper: identity/replay, `forbidden`, `invalid-offer`, `no-company`, `no-inventory`, `capacity`, `insufficient-funds`, then `TryDebit` and a fresh lot `buy:<player>:<request>` in the buyer's inventory, recorded as `bought` with `MovedLotId`. A retried accepted request replays; rejections are answered without being recorded or committed; a failed commit changes nothing. No schema change.
- Network: `GoodsNetworkBridge.RequestPurchase(request, site, offer)` → `ServerPurchase` (connection-resolved player, broadcast on accept).
- Content: `OfferAsset` (`Assets/Content/Offers/Dough5.asset` 5 dough $2.50, `Belt10.asset` 10 belts $5.00), authored by `BuildDevSite`; `SessionRoot.offers` registers them on every server start and exposes `Offers`.
- Presentation: `EquipmentInteraction.Buy(offer)`; the inventory screen's Supplier window (`hud-supplier`, `hud-offer-<id>` buttons, `PlayerHud.ClickOffer`).
- The dev starter goods and storage stock are kept alongside purchases (owner decision, 2026-09-24; decision 0014).
Open: member spending permissions, bulk quantities, supplier stock, delivery times. Equipment: below.

## Implemented: equipment purchases (2026-09-24)

Decision: [0017](decisions/0017-equipment-purchases.md). Company cash buys machines, which are delivered held by the buyer.

- Domain (`GoodsWorld.Supply.cs`): `EquipmentOffer { Id, PriceCents, Equipment template }` (`RegisterEquipmentOffer`, never saved; one ID space shared with `PurchaseOffer`). `Buy` accepts either kind. For a machine it checks `no-layout` in place of `capacity`, then `TryDebit` and a new `Held` `GoodsEquipment` `buy:<player>:<request>` (holder = buyer), recorded as `bought` with `GoodsOutcome.EquipmentId`. Replay, rejection and failed-commit rules are the same as for goods. No schema change.
- Content: `OfferAsset.equipment` (optional `EquipmentDefinition`; `RegisterWith` picks the kind); `EquipmentDefinition.CreateTemplate()`. `Assets/Content/Offers/Oven1.asset`: one oven for $150.00 (PROTOTYPE).
- Presentation: the Supplier window shows machine offers with the machine icon. A bought machine appears in the inventory and is placed like a picked-up one.
Open: resale/salvage, delivery or installation time, offering the counter.

## Implemented: spoilage timing and refrigeration (2026-09-24)

Decision: [0018](decisions/0018-spoilage-timing-and-refrigeration.md). Owner decision: refrigeration completely pauses spoilage. A fridge can be bought and placed, and the HUD shows when goods spoil. No payload schema change (still v6).

- Domain: unchanged spoilage rule. `Advance` adds exposure only to lots in unrefrigerated locations; a job finishing into a refrigerated output gets no overshoot exposure; moves keep exposure. New `GoodsEquipment.InputRefrigerated` makes the `<id>:in` buffer refrigerated (`OutputRefrigerated` already existed); `Validate` requires placed buffers to match. The field reads false in older saves, which matches every earlier machine.
- Content (`BuildDevSite`): `Assets/Content/Equipment/Fridge.asset` (kind `fridge`, 1x1, 8 refrigerated input slots, unused 1-slot output, no recipes), `Assets/Prefabs/Equipment/Fridge.prefab` (primitives, one root box collider, `Assets/Materials/Fridge`), `Assets/Art/Icons/Fridge.png` (`DrawItemIcons.ps1`), `Assets/Content/Offers/Fridge1.asset` ($80.00). `SessionRoot` lists `[oven, counter, fridge]`.
- Presentation: `PlayerHud` opens a machine with no recipes as storage (input grid, "Refrigerated n/m", "Goods in here do not spoil."). Edible goods slots show the ambient time left before the first lot spoils (`FormatDuration`): white counting down, orange in the last tenth, frozen in blue while refrigerated. The hover line gives the full time (`spoils in 1h 59m`, or `refrigerated: not spoiling (1h 59m left out of the cold)`). It is a fixed-width single line, so it never resizes the centred screen. `EquipmentInteraction` shows a storage hint for machines with no recipes.

PROTOTYPE: fridge size and price, placeholder art. Open: discarding spoiled goods (GDD section 6), refrigeration running cost and power, refrigerated transport, and GDD section 6 wording, which does not yet say that refrigeration pauses spoilage.

## Implemented: building shells and the indoor camera (2026-09-24)

Decision: [0019](decisions/0019-building-shells-and-indoor-camera.md). First slice of GDD section 5 buildings: a server-owned shell and the section 3 indoor camera. Goods snapshot schema **v7**.

- Domain (`GoodsWorld.Buildings.cs`, `SiteGrid.cs`): `GoodsBuilding { Id, SiteId, CellX, CellZ, Width, Depth, Doors: GridCell[] }` in `GoodsSnapshot.Buildings`. Perimeter cells are walls except doors (never corners); `SiteGrid.IsWall`, `IsDoorCell`, `IsInterior` (strictly inside; doors are thresholds), `CoversWall`. `CellProblem` returns `blocked` for a footprint on a wall, so equipment placement, belt placement, the client ghost preview and recovery all share the rule; interior and door cells are ordinary. Server-only `Bootstrap(GoodsBuilding)` rejects blank/duplicate IDs, unknown sites, shells under 3x3 or outside the layout, corner/off-perimeter/duplicate doors, overlap with another shell, and walls over placed equipment or belts. `Validate` applies the same rules; `View` includes the site's shells (full baseline, no new RPC). v6 saves upgrade in memory with no buildings. No player command creates, moves or removes a shell.
- Dev seed (`DevWorld`): PROTOTYPE `dev-restaurant`, 11x9 cells at (9, 0) with doors at (13, 8) and (14, 8), in new worlds; `EnsureBuilding` adds it once to a save whose dev site has no building (committed before serving; warns and skips if its wall cells are occupied). The seeded oven and counter stay outside it.
- Presentation (`Assets/Scripts/Session/Buildings/BuildingPresenter.cs`, in `DevSite`): one solid 3 m box per wall run, collider-free door lintels, interior floor tint and roof, from `Assets/Materials/Building` (authored by `BuildDevSite`). The local avatar's cell decides indoors: that building's roof hides for this client and `OrbitCameraRig.SetIndoors` switches to top-down on entering and back to orbit on leaving, only on a change, so `Player/SwitchCamera` (C) still overrides until the next crossing. The south landmark cube is no longer authored (it would stand inside the shell).

Prototype: the dev shell's size, position, doorway and materials. Open: construction and purchase of land/shells (section 5), wall and door editing, interior walls, floor area in any rule (customers, seating), reserving doorways, and whether indoors should affect server rules. Extra floors: see below.

## Implemented: factory floors and the freight elevator (2026-09-24)

Decision: [0020](decisions/0020-factory-floors-and-elevator.md). First construction command (GDD section 5) and the section 27 freight elevator. Goods snapshot schema **v8**.

- Domain (`GoodsWorld.Buildings.cs`, `SiteGrid.cs`): `GoodsBuilding` gains `Kind` (`restaurant`/`factory`), `Floors` (≥ 1; > 1 only for factories) and `ElevatorX/ElevatorZ` (interior cell, used while `Floors > 1`). `GoodsEquipment.Level` (0 while held) and `GoodsBelt.Level`. `CellProblem(..., level)`: level > 0 needs the footprint inside the interior of a building with more floors (`no-floor`); overlap is per level; walls surround every storey (doors only on the ground); the elevator cell is `blocked` on all its building's storeys. `Place`/`PlaceBelt` take an optional level (default 0). Belts link only within one site and level. `AddFloorDurably(player, request, building, elevatorX, elevatorZ)`: grant, factory kind, registered `FloorOffer` (`CentsPerCell`, `MaxFloors`; content, not saved), site company, first-floor elevator cell (interior, clear) and funds; debit and new floor in one commit; rejections unrecorded, accepted order replays. `Validate` checks kinds, floors, the elevator and upper-level placements. v7 saves upgrade in memory to one-storey restaurants.
- Network (`GoodsNetworkBridge`): `RequestAddFloor(request, building, elevatorX, elevatorZ)`; `RequestPlace` and `RequestPlaceBelt` carry a level.
- Dev seed (`DevWorld`): PROTOTYPE `dev-factory`, 5x10 at (15, 10), doors (16, 10) and (17, 10), in new worlds and once in existing saves (`EnsureFactory`); `FloorOffer` 500 cents per interior cell, 3 storeys, registered by `SessionRoot`. `EnsureBuilding` keys on the restaurant's ID.
- Presentation: `SiteGridSpace.LevelHeight` = 3 m per storey; `LevelAt` maps an avatar's height to a level. `BuildingPresenter` builds a storey object per level (ground: doorway, floor tint, lintels; upper: solid interior slab and a closed wall ring; each with an elevator pad), rebuilds a shell when its floors change, exposes `LocalLevel`, `LocalCell`, `LocalBuilding` and `HidesLevel`, and in the local avatar's building hides storeys above its level and other players there. `EquipmentPresenter` and `BeltPresenter` hide machines, belts and riding goods on hidden storeys; `BeltPresenter` computes shapes per level. `EquipmentInteraction` aims at the plane of the avatar's level and places equipment and belts there, shows an elevator hint on the shaft, and `AddFloor()` orders the next floor with the elevator on the avatar's cell. `ElevatorRider` (on the `BuildingPresenter` object) rides one storey with `Player/FloorUp` (PgUp) and `Player/FloorDown` (PgDn) by teleporting the owned avatar (`PlayerAvatar.Teleport`). `PlayerHud` adds a Factory window with floors, price and a Build button to the inventory screen inside a factory.

Prototype: floor price, height limit, storey height, the dev factory's size and position, elevator placed where the player stands, instant construction. Open: build time/cancellation/disruption (GDD 29.2, 29.3, 29.7), buying buildings, employees using the elevator, a gamepad elevator binding, and moving a shaft.

## Implemented: prototype scriptable employee (2026-09-24)

PROTOTYPE, no decision record yet. Owner requests (2026-09-24): employees run Lua scripts (MoonSharp) that move goods between inventories, the employee no longer follows players, and employees persist in SQLite. Goods snapshot schema **v9** (employee records).

- Persistence (`GoodsWorld.Employees.cs`): `GoodsEmployee { Id ("employee-" prefix), SiteId, Name, X, Y, Z, Yaw, Script, ScriptRunning }` lives in `GoodsSnapshot.Employees`, so an employee, the goods in its hands and its assignment commit and recover together in the SQLite world save (no new store). Server-only `Bootstrap(employee, handSlots)` adds the record with its site grant and carried inventory `carried:<id>` (reusing ones that exist). `SetEmployeePose` changes memory only; the pose is saved by the next tick commit (≤ 10 s) or command, and by a clean stop. `SetEmployeeScriptDurably` commits the script and running flag before returning (`unknown-employee`, `script-too-long` over 16000 characters, `persistence-unavailable` with state restored). `Validate` requires unique employee IDs, finite poses, and each record's grant and carried inventory on its site. `View` lists the site's employees with scripts blanked (scripts reach clients through the worker). v8 saves upgrade in memory with no employees.
- Seeding and spawning: `SessionRoot.employeePrefab` (set only in `SampleScene`, which has the NavMesh) makes `DevWorld.LoadOrCreate` seed `employee-1` ("Employee", (-5, 0, 3), 4 hand slots) in a new world, or once in a save without it (`EnsureEmployee`), and makes the server spawn one worker per saved record at its saved pose once the bridge serves (`SpawnEmployees`). Scenes without the prefab (DevSite) leave records untouched and spawn none. `SampleScene` uses its own spawnable catalog `Assets/Network/SampleScenePrefabs.asset` (DevSite's two prefabs plus the employee); `GamePrefabs.asset` is unchanged.
- Goods: goods in an employee's hands are ordinary lots, so stopping or replacing a script never deletes or duplicates them. Every move is `GoodsNetworkBridge.WorkerTransfer` → `TransferDurably` (the player path: grant, route, capacity, replay, commit), then a baseline broadcast. The bridge also exposes `IsServing`, `CanCommand(connection, site)`, `WorkerView`, `Employees`, `RecordWorkerPose` and `RecordWorkerScript`.
- Scripts (`Assets/Scripts/Session/Employees`): `EmployeeScript` runs one program in MoonSharp's soft sandbox (no io, `os.execute` or `load`; at most 16000 characters) as a coroutine the server steps once a frame. Blocking calls start a C# operation and yield until it ends; each resume is capped at 20000 VM instructions, so `while true do end` only stalls its own employee. API: `move_to(place)`, `take(place, item, amount)`, `put(place, item, amount)`, `wait(seconds)`, `count(place, item)`, `find(kind)`, `carrying()`, `say(text)`/`print`. A place is `"storage"`, a machine kind (nearest), a machine ID, `"<id>:in"`/`"<id>:out"` or `"hands"`. Machines give output then input and receive into input; `take` skips spoiled goods. `take`/`put` return the units moved and a reason when none moved; syntax and runtime errors stop the script with the line.
- Placement and paths (implemented 2026-09-25, PROTOTYPE): a cell is a Lua table `{x, z}` (grid cell, ground floor) and is also a place for walking. `move_to({x, z})`/`move_to(x, z)` walks onto the cell (nearest walkable point within half a cell, 0.2 m tolerance); `path(a, b, ...)` or `path({a, b, ...})` walks through cells and places in turn without stopping between cells and returns `false, "waypoint n: reason", n` on the first unreachable one. `place(machine, cell, rotation)` (also `place(machine, x, z, rotation)`) places a machine the employee holds (ID, or a kind for its first held one) with its minimum corner on the cell after walking beside the footprint; `pick_up(machine)` picks a placed ground-floor machine into the employee's holding (buffers swept into its hands); `place_belt(cell, direction)` lays a belt item from its hands. `holding(kind)` lists held machine IDs and `position()` returns the employee's cell as `{x, z}`, which since 2026-09-25 also has `.x`/`.z` (`EmployeeScript.Cell`). `take`/`put` refuse a cell. All go through `GoodsNetworkBridge.WorkerPlace`/`WorkerPickUp`/`WorkerPlaceBelt` (the player's `PlaceDurably`/`PickUpDurably`/`PlaceBeltDurably` with the employee as actor, then a broadcast); decision 0005 still applies, so the server does not check the employee's position for these. Machines reach an employee only through `GoodsWorld.Give`/`GiveDurably` (`RequestGive` RPC): the requesting player must hold the piece and the recipient must be an employee record on the piece's site (`not-held`, `unknown-employee`, `forbidden`); the equipment keeps its ID and only `HolderId` changes. There is no path back to a player other than the employee placing it. The walk's approach point now tries every side and corner of the footprint and keeps the shortest complete NavMesh path within reach, so places inside buildings are approached through the door.
- `EmployeeWorker` (replaces `EmployeeFollower`, same script GUID; `Configure(record)` before spawn, ID and name replicated): only the server runs scripts, steers the `NavMeshAgent` (re-paths every 0.5 s to a walkable point beside the place, 60 s timeout) and checks reach: a transfer needs the employee within 1.3 m (plus 0.25 m slack) of the place's footprint. `ServerRun`/`ServerStop` RPCs require a sender with a grant on the site. `SyncVar`s replicate `Carrying` (hands non-empty, rechecked every 0.5 s since players can also empty them), `Status` and the saved `Source`. Run saves the script as running before it starts (a failed save refuses it); Stop, finishing and errors save it as not running. The pose is recorded each second when it moved 5 cm or turned 2°. After a restart a running script starts again from its first line (interpreter state is not saved), so completed steps repeat; goods stay conserved. Every peer animates from observed speed plus `Carrying`.
- `SiteLocationMarker` gives the dev storage (no machine) a place in the scene: `SampleScene`'s `StorageShelf` at (-7, 0, 7), a 2x0.8 m rack. It is not on the site grid, so machines can still be placed over it.
- Presentation: `EquipmentInteraction.Hover.cs` outlines whatever openable thing is under the crosshair (an employee, a placed machine, or the dev storage, within `interactReach` of the avatar on its floor) with `HoverOutline`, which appends `Assets/Materials/Employee/EmployeeOutline.mat` (shader `FoodFactory/Outline`, an inverted hull grown from each part's mesh centre); E opens that target's screen (the inventory when there is none), and left click with an empty cursor opens the highlighted target's screen (an employee's `InteractionScreen.Employee`, a machine, or the storage; nothing when nothing is highlighted). While it is open the Player action map is suspended except `CloseScreen` and `Point`, so typing never moves the avatar or presses hotbar keys. `EmployeeScriptPanel` (own `UIDocument` on the HUD panel settings) has the text box, Run/Stop/Close, the replicated status and an API reference. The text box is not focused on opening (the player clicks into it): OS keyboard text reaches UI Toolkit after the Input System action fires, so a focused box would take the opening E as a typed "e". It gives up focus whenever the panel is hidden (closed or picking). Covered by `ScreenOpeningTests` (PlayMode, SampleScene). `PlayerHud` ignores the employee screen.
- Select world pos (implemented 2026-09-25): the script screen's `Select world pos` button switches to `InteractionScreen.PickPosition`, which hides the panel (keeping the draft), locks the pointer and re-enables only Move, Look, Jump, Sprint, Zoom and SwitchCamera plus Place/Point/CloseScreen. A placed machine or the storage under the crosshair gets a red outline (`HoverOutline.SetHighlighted(on, material)` with a red copy of the hover material); otherwise the ground cell under it on the avatar's floor gets a red tile (a collider-free copy of the placement ghost). Place inserts `"<id>"` (storage alias) or `{x, z}` at the text box's remembered caret, replacing any selection, and returns to the script screen; Esc returns without inserting. The text box no longer selects all on focus. Its caret is white with a 3 px white bar blinking every 0.5 s drawn over it (UI Toolkit draws its own caret 1 px wide), lit while the caret moves. The panel also lists "Give <kind> (n)" buttons for machines the local player holds. `PlayerHud` ignores `PickPosition` like the employee screen.
- Evidence (2026-09-25, SampleScene host with an isolated save under the session scratch directory, play mode, driven through `RequestPickUp`/`RequestGive`/`RequestRun` and the panel's public methods): the player picked up `dev-counter-1` and gave it to `employee-1`; its script walked `path({4, 8}, {8, 8}, {8, 5})`, placed the counter inside the factory at (10, 5) rotation 1 (through the door, after the approach fix), picked it up and placed it at (4, 9); `path({1, 1}, {99, 99})` failed with `waypoint 2: cell (99, 99) is off the site` and `take({3, 3})` was refused. Pick mode showed the red cell tile and the counter's red outline in captures, suspended every other action, and inserted `{7, 11}` at the caret (`path(, {1, 1})` became `path({7, 11}, {1, 1})`). EditMode 219/220 with 2 new `EquipmentTests`; the failure, `TruckTests.StepSizeDoesNotChangeTheOutcome`, is flaky (9 of 20 direct runs passed) because split truck lots get random GUID IDs used as an ordering tie-break. PlayMode tests and a separate-process multiplayer check were not run, and real mouse/keyboard input for the pick was not exercised (methods were called directly).
- Script assistant (implemented 2026-09-25, PROTOTYPE, no decision record yet): the script screen has **Script** and **Assistant** tabs. On the Assistant tab the player describes a task in English. `ScriptAssistant` asks a local model for one fenced Lua program (the system prompt holds the employee API, place/item names and two examples, plus the current draft unless it is the untouched starter). It checks the reply with `ScriptDryRun.Check` in two steps. First `EmployeeScript.CheckSyntax` parses it with MoonSharp. Then it runs for up to 400 steps against stand-ins for every `EmployeeWorker` call. The stand-ins finish at once with typical values (true, 1, one ID, `{0, 0}`) and touch no world state. This catches runtime errors a parse cannot, such as arithmetic on the table `position()` returns or a call to a made-up function. It happened in play on 2026-09-25, when the model wrote `local x, z = position()`. A test keeps the stand-ins' names equal to the real API. A reply that fails either step, or has no code, goes back to the model with the error and the failing line's text, for at most `MaxAttempts` = 3 replies. Branches the stand-in values do not take, and wrong argument values, are still found only on the server. The prompt also says that cells are 1 m, that movement is relative to `local p = position()`, and that `path(cells)` and `math` exist, with a 4 m square example. Each attempt's code and error are logged as `[Assistant]`. Output quality is measured by the Explicit PlayMode fixture `ScriptAssistantModelTests`. It runs a request 5 times on the real model and replays each program against recording stand-ins to check the walk's shape. Evidence (2026-09-25): "move in a 5m circle" passed 5/5, every time as a circle of radius 2.5 m (it reads "5m" as the diameter) around the start. The same prompt in a running host ran the lap on the server with no error in about 20 s. Then the error is shown and the draft is left unchanged. A program that passes replaces the Script tab's draft and that tab is shown. The player still presses Run, so the server's checks are unchanged, and the model is never involved in gameplay state. Model: Qwen2.5-Coder-1.5B-Instruct Q4_K_M GGUF (Apache-2.0), run in-process on the client by LLMUnity `ai.undream.llm` v3.0.3 (llama.cpp through LlamaLib; GPU layers fall back to the CPU). `LocalScriptModel` loads it the first time the Assistant tab opens, about 1.5 GB of memory. Weights are not in git (see development.md Setup). LLMUnity keeps its own Editor settings in `PlayerPrefs`; that is package-internal Editor state, not game data. Evidence (2026-09-25, Windows, GPU via tinyblas): EditMode 230/230 (10 new `ScriptAssistantTests` with a fake model). PlayMode `ScreenOpeningTests` 5/5, including the fake-model tab tests and the Explicit `LocalModelWritesAParsingScript`, which ran the real model: the request "repeat forever: carry 2 dough from the storage to the fridge, say how many, wait 3 s" gave a correct `while true` loop in about 7 s. Running-game captures (isolated save) show both tabs and a generated `for i = 1, 4` script in the Script tab. Not checked: a player build (LLMUnity's build processor and the 3.9 GB LlamaLib folder in builds), CPU-only speed, and real mouse/keyboard input.
- Art: `ArtSource/Employee/Employee_Asset.blend` adds `Employee_CarryBox`/`Employee_CarryBoxTape` (cardboard box parented to the spine bone) and `Employee_CarryWalk`/`Employee_CarryIdle` (arms holding the box, torso leaning back slightly), exported to `Employee.fbx` at 0.43 scale with all actions baked. Clips are `Idle`, `Walk`, `CarryIdle`, `CarryWalk`; the controller swaps pairs on `Carrying`, and the box renderers show only while carrying.
- Navigation as before: the AI Navigation `NavMeshSurface` baked from physics colliders to `Assets/Scenes/SampleScene/NavMesh-Navigation.asset` (now after the shelf is placed, before the employee), plus carving obstacles from `EquipmentPresenter`/`BuildingPresenter`. Obstacles come from client presentation, so a dedicated server with no presenters would not see them. Upper storeys have no NavMesh.
- `Assets/Scenes/SampleScene.unity` is `DevSite` plus the shelf, the script panel, the NavMesh, the employee prefab on `SessionRoot` and its own prefab catalog (no scene-placed employee); still not a build scene. Rebuild it with the body of `AgentScripts/BuildSampleScene.cs` through MCP `execute_code`.
- Evidence (2026-09-24, host with an isolated save under the session scratch directory, play mode): a script sent through `RequestRun` carried 3 dough storage → oven (the oven started baking); a three-round loop storage → oven → counter delivered 5 bread with matching goods totals; captures showed the carry walk with the box and the green hover outline; the script panel opened focused with only `Point`/`CloseScreen` enabled and restored all actions on close; the crosshair ray found the employee's trigger collider; a syntax error, an unknown place and `while true do end` (72 fps) were reported and stopped. EditMode 177/177 (7 new `EmployeeScriptTests`). PlayMode 15/17: `HotbarKeysOverAStackAndDropsOnTheHotbarAssignSlots` and `SlotButtonsTakeShiftClicksAndPlainClicks` fail the same way on unmodified HEAD (synthetic input with the Editor unfocused). The panel itself was not captured (the MCP capture omits UI Toolkit), and no separate-process multiplayer check was run.
- Persistence evidence (2026-09-24, isolated save, play mode): a new save seeded and spawned `employee-1`; after it took 4 dough and parked at the counter with its script running, a clean `Shutdown` left schema 9 in `world.db` with its pose, running 146-character script and 4 dough in hand; a restarted host spawned one worker at that pose, carrying, with the script restarted (it took 4 more: storage 12, hands 8, player 5, total 25 as before); after Stop and another restart the worker was idle with its script text kept. No console errors or warnings. EditMode 185/185 (8 new `EmployeeTests`); PlayMode 15/17 with the same two pre-existing failures.

Open: hiring, wages, firing and more employees; which players may command an employee (any site grant can now); memory limits on scripts; resuming a script where it stopped rather than from the top; spawning that does not depend on the scene (a server scene without the prefab keeps employees idle and unspawned); employees on upper floors or elevators; storage placement on the grid; obstacles that do not depend on the server running presenters.

## Implemented: conveyor lifts (2026-09-24)

Decision: [0021](decisions/0021-conveyor-lifts.md). GDD section 27 conveyor lifts between factory floors. Goods snapshot schema **v10**.

- Domain (`GoodsWorld.Belts.cs`, `BeltRules.cs`, `SiteGrid.cs`): `GoodsBelt.Lift` (0 flat, +1 up, -1 down) and `ExitLevel`. `PlaceLift`/`PlaceLiftDurably(player, request, site, x, z, direction, level, lift)` consume one `lift` item (`no-lifts`, `invalid-lift`); the same lift at the same cell and level turns for free. `RemoveBelt` returns a lift item for a lift. `CellProblem` counts a lift on both levels; `Validate` checks `Lift` is -1..1 and both cells. `BeltRules.ByCell` keys a site's belts by `(X, Z, Level)` with lifts at both ends; `Shape` takes a level and counts only neighbours whose exit is on it (a lift is always straight); `Link` looks in front on the exit level and never enters a lift's exit end. `MoveBeltItems` links a whole site at once. v9 saves upgrade with flat belts.
- Network (`GoodsNetworkBridge`): `RequestPlaceLift(request, site, x, z, direction, level, lift)`.
- Dev content: `Assets/Content/Items/Lift.asset` (stack 50, `Assets/Art/Icons/Lift.png` from `DrawItemIcons.ps1`) in `SessionRoot.items` (also in `BuildDevSite.cs`); `DevWorld` puts 20 in the storage (new worlds; once in older saves via `EnsureLiftStock`) and 10 in new inventories.
- Presentation: `BeltPresenter.LiftModel(lift)` builds an up and a down model from the straight belt prefab (two half belts and four frame posts, trigger colliders) and hides a lift's upper end with its storey; `BeltPath` draws a lift's path in, up/down and out, and `LevelAt` gives a riding item's storey. `EquipmentInteraction`: `LiftCursor`, `LiftDirection`, `FlipLift()` (`Player/FlipLift`, V, looked up in the Player map), a lift ghost, `PlaceLift`; R with an empty cursor turns an aimed lift; belt drags skip lifts; ghost shapes include lift ends.
- Evidence (2026-09-24, Editor 6000.5.9f1, isolated temp saves): EditMode all assemblies 192/192, including 7 new `LiftTests` (placement and occupancy, floor/shaft/direction refusals, items riding up and down, exit shapes and no feeding from an exit end, removal, save/recovery, v9 upgrade). PlayMode `FoodFactoryGame.Session.PlayModeTests` 16/17 with the new `BeltPlacementTests.ALiftCarriesAnItemUpToTheNextFloor` (host session: floor bought, lift carried on the cursor, V flips, lift placed and drawn with its top on the second floor, dough rides to the upper belt's end and is drawn above the second floor, removal returns the lift) passing; `HotbarKeysOverAStackAndDropsOnTheHotbarAssignSlots` and `SlotButtonsTakeShiftClicksAndPlainClicks` fail identically on unmodified HEAD (re-checked with the changes stashed). No running-game visual capture and no separate-process multiplayer check were made.

Prototype: lift stack size, dev stock, V key, runtime model, climb at belt speed. Open: buying lifts, lift shafts as construction (GDD 29.1), a gamepad FlipLift binding, openings in upper slabs, employees using lifts.

## Implemented: trucks and loading docks (2026-09-24)

Decision: [0022](decisions/0022-trucks.md). GDD section 9 trucks between two owned sites over abstract public roads. Goods snapshot schema **v11**.

- Domain (`GoodsWorld.Trucks.cs`): `GoodsSite { Id, Name, MapX, MapZ }` and `GoodsTruck` (states `Parked`, `ToPickup`, `Loading`, `ToDropoff`, `Unloading`) in `GoodsSnapshot.Sites`/`Trucks`. Server-only `Bootstrap(GoodsSite)`, `Bootstrap(GoodsTruck)` (creates `<id>:cargo`, kind `vehicle`, on the reserved site `road`; the whole snapshot must validate or nothing changes), `AddCompanySite`, `HasSite`. `RoadMetres` (Manhattan) and `RoadSeconds`. `SetTruckRoute`/`SetTruckRouteDurably(player, request, truck, pickupDock, dropoffDock, items)`: `forbidden`, `invalid-dock`, `same-site`, `invalid-cargo`, `no-road`; rejections unrecorded, accepted route replays. `Advance` calls `MoveTrucks` (per-second loading/unloading at `LoadUnitsPerSecond`, most exposed first, owner becomes the dropoff site; skip-ahead while all active trucks drive). `Transfer` refuses vehicle locations (`invalid-route`); `Grant`/`TryGrantDurably` refuse the road site. `View` adds the company's trucks, their cargo locations and lots (after the site's own locations) and its sites' map records. `Validate` checks sites, trucks, routes, cargo locations and that nothing else is on the road. v10 saves upgrade with no sites or trucks.
- Network (`GoodsNetworkBridge`): `RequestSetTruckRoute(request, truck, pickupDock, dropoffDock, items[])`; a connection may subscribe to several granted sites (full baselines per site). `ClientSiteSubscription.Watch(site)`/`Remote(site)` keep remote baselines apart from the primary one.
- Session: `SessionAdmission` also grants `DevWorld.RemoteSiteIds` that exist, without an inventory. `DevWorld.LoadOrCreate(..., dock)` seeds, or adds once to older saves (`AddLogistics`), the map records, the `dev-warehouse` site (storage with 200 dough, 10x10 layout, owned by `dev-company`), the warehouse dock, the restaurant dock at (0, 18) and `dev-truck-1` routed warehouse → restaurant. `SessionRoot` passes the `dock` definition.
- Content (`BuildDevSite.cs`): `Assets/Content/Equipment/Dock.asset` (kind `dock`, 2x1, 8 in, 8 out), `Assets/Prefabs/Equipment/Dock.prefab`, `Assets/Art/Icons/Dock.png` (`DrawItemIcons.ps1`), `Assets/Content/Offers/Dock1.asset` ($60.00), `Assets/Prefabs/Logistics/Truck.prefab` (no collider), materials under `Assets/Materials/Dock` and `Assets/Materials/Truck`. `SessionRoot` lists `[oven, counter, fridge, dock]`. `Player/Logistics` is bound to L.
- Presentation (`Assets/Scripts/Session/Logistics`, in `DevSite` and the rebuilt `SampleScene`): `LogisticsPanel` (own UIDocument, sorting order 2) opened by `InteractionScreen.Logistics` (`EquipmentInteraction.ToggleLogistics`); trucks with live status and cargo, a route editor (one cargo item or Any) and remote-site stock with Ship/Unstage/Store. `PlayerHud` opens a dock as Outgoing and Incoming grids with the trucks serving it. `TruckPresenter` stands a placeholder truck behind a local dock while one loads or unloads there.
- Evidence (2026-09-24, Editor 6000.5.9f1, isolated temp saves): EditMode `FoodFactoryGame.Goods.EditModeTests` 145/145 (15 new `TruckTests`), `FoodFactoryGame.Session.EditModeTests` 62/62 (new `DevWorldLogisticsTests` ×3 and a dock/truck authoring test), `FoodFactoryGame.Baseline.EditModeTests` 4/4; PlayMode `FoodFactoryGame.Session.PlayModeTests` 18/18 (new `LogisticsPanelTests.PanelShipsRemoteStockAndSetsTheTruckCargo`; two existing assertions updated for the new seed docks and the baseline's cargo locations) and `FoodFactoryGame.Goods.PlayModeTests` 1/1. No running-game visual capture of the dock, truck or logistics screen and no separate-process multiplayer check were made.

Prototype: map positions, dock and truck sizes, speed and rate, dock price, L key, placeholder models, redirect from the last site. Open: see decision 0022.

## Implemented: truck routes and fleet (2026-09-24)

Decision: [0023](decisions/0023-truck-routes-and-fleet.md). Supersedes the per-truck route of 0022 above. Goods snapshot schema **v12**.

- Domain (`GoodsWorld.Trucks.cs`): `GoodsRoute { Id, CompanyId, PickupDockId, DropoffDockId, AllowedItemIds }` in `GoodsSnapshot.Routes`; `GoodsTruck.RouteId` (empty exactly while `Parked`) replaces the truck's route fields. Commands, all durable, rejections unrecorded, accepted ones replay: `CreateRouteDurably(player, request, pickupDock, dropoffDock, items)` (ID `GoodsWorld.RouteIdFor(player, request)`), `SetRouteDurably(..., route, ...)` (re-dispatches the route's trucks), `DeleteRouteDurably(player, request, route)` (parks its trucks, cargo aboard), `AssignTruckDurably(player, request, truck, route)` (empty route parks; same route is a no-op). Reasons: `forbidden`, `invalid-dock`, `same-site`, `no-road`, `invalid-cargo`. Server-only `Bootstrap(GoodsRoute)`. `GoodsWorld.RouteOf(state, truck)` for presentation. `View` adds the company's routes. `Validate` checks routes (company owns both docks, filter) and that a routed truck's route exists in its company.
- Purchases (`GoodsWorld.Supply.cs`): `TruckOffer { Id, PriceCents, Name, CargoSlots, SpeedMetresPerSecond, LoadUnitsPerSecond }`, `RegisterTruckOffer`; `Buy` with a truck offer needs no inventory, refuses an unmapped site (`no-road`), charges once and parks truck `buy:<player>:<request>` ("<Name> <n>") at the site; the outcome's `EquipmentId` is the truck ID.
- Schema: `GoodsSnapshotStore` upgrades v11 by turning each routed truck into route `route:<truck id>` with that truck on it.
- Network (`GoodsNetworkBridge`): `RequestCreateRoute`, `RequestSetRoute`, `RequestDeleteRoute`, `RequestAssignTruck` replace `RequestSetTruckRoute`; trucks are bought with `RequestPurchase`.
- Session and content: `DevWorld` seeds route `dev-route-1` with `dev-truck-1` on it. `TruckDefinition` (`Assets/Content/Vehicles/Truck.asset`: 4 slots, 15 m/s, 5 units/s) and `OfferAsset.truck`; offer `Assets/Content/Offers/Truck1.asset` (`supplier-truck`, $250.00) is on `SessionRoot.offers` in `DevSite` and `SampleScene` and authored by `BuildDevSite.cs`.
- Presentation: `LogisticsPanel` has Routes (choosers, Apply, Delete, trucks on it), a New route draft with Create, and Trucks (status, cargo, route chooser with Assign/Park, Buy per truck offer). The supplier window leaves truck offers out. `PlayerHud`'s dock note and `TruckPresenter` read docks through the route.
- Evidence (2026-09-24, Editor 6000.5.9f1, isolated temp saves): EditMode `FoodFactoryGame.Goods.EditModeTests` 150/150 (20 `TruckTests`, new: several trucks per route, route deletion, parking, buying, v11 upgrade), `FoodFactoryGame.Session.EditModeTests` 64/64 (new `SupplierTruckOfferSellsTheDevTruckModel`), `FoodFactoryGame.Baseline.EditModeTests` 4/4; PlayMode `FoodFactoryGame.Session.PlayModeTests` 18/18 (`LogisticsPanelTests.PanelShipsStockEditsRoutesAndManagesTheFleet` buys, creates, assigns, parks and deletes through the panel) and `FoodFactoryGame.Goods.PlayModeTests` 1/1. No running-game visual capture of the new logistics screen and no separate-process multiplayer check were made.

Prototype: truck price, parking a driving truck at the site it left, panel layout. Open: see decision 0023.

## Implemented: customers (2026-09-25)

Decision: [0024](decisions/0024-customer-simulation.md). Replaces the sell counter's stand-in buyer (0013, above). Goods snapshot schema **v13**.

- Domain (`GoodsWorld.Customers.cs`): `GoodsDistrict`, `GoodsCompetitor`, `GoodsCustomer`, `GoodsDiner` in `GoodsSnapshot.Districts`/`Competitors`/`Customers`/`Diners`, plus `NextCustomerNumber` and `CustomerRandom`. Customers advance inside `Advance` in one-second sub-steps after the other systems: spawn, logit choice among restaurants in range (or stay home), travel, queue, purchase (edible item out of a counter's input + company credit + seat, one revision), eat or leave, walk out once (re-choose) or twice (home). Server-only `Bootstrap(GoodsDistrict)`, `Bootstrap(GoodsCompetitor)`, `Bootstrap(GoodsCustomer)`. `View` carries the site's customers and diner record only. `Validate` checks per-state customer shape, one customer per counter, seats per table/competitor, competitor servers. Implemented transient diner state tracks ticket-ordered queues, occupied seats, busy counters and server counts across clock seconds; structural restaurant changes rebuild it, and restore/rollback reconstruct it from the snapshot. Validation builds district, competitor, equipment and occupancy lookups once per pass.
- Production: sale recipes are menu items (`RecipeDefinition.Tier`, `Cuisine`); stations never start them (`customers-only`); a legacy sale job completes once. `GoodsEquipment.Seats` (kind `table` ⇔ seats > 0); `PickUp` answers `occupied` for a serving counter or an occupied table.
- Session and content: `DevWorld.AddCustomers` seeds or adds once the dev district, Corner Cafe, Noodle Bar and `dev-table-1` (cells (11, 3), inside the restaurant). `EquipmentDefinition.seats`, `RecipeAsset.tier`/`cuisine`; `Assets/Content/Equipment/Table.asset`, `Assets/Prefabs/Equipment/Table.prefab`, `Assets/Art/Icons/Table.png`, offer `Table1` (`supplier-table`, $40.00), all authored by `BuildDevSite.cs`/`DrawItemIcons.ps1`; `SessionRoot` in `DevSite` lists the table definition and offer (`SampleScene` was not updated).
- Presentation: the counter's progress line shows the customer being served and how many wait; a table opens a seats/standing window.
- Visual customers (2026-09-25, presentation only): `CustomerPresenter` (`Assets/Scripts/Session/Customers/`) in `DevSite` reads the client's replicated `ClientSite` every 0.5 s and draws up to `maxVisible` (100) of the subscribed site's customers with `Assets/Prefabs/Customers/Customer.prefab`. That prefab is the Employee's animated model without worker props or network components. Customers using the site take priority, and travelling customers appear only in their last 12 s. A new visual appears at a site edge sampled on the baked `DevSite` NavMesh (`Navigation`) outside the local camera's view (falling back to the first active camera because the rig's camera is untagged), then walks NavMesh path corners to the counter (ordering), a fixed queue grid (queued) or a table side (eating). A customer that leaves the record walks to an off-screen edge, or is removed after its deadline. Tint comes from the district and `Appearance`. The presenter never writes state; the server simulation is unchanged. `Bind(ClientSiteSubscription)` points it at another connection's subscription (used by the remote-client PlayMode test). Authored by `AgentScripts/BuildCustomerVisual.cs`, `BuildDevSiteNavigation.cs`, `InstallCustomerPresenter.cs` (`SampleScene` not updated).
- Evidence: [verification record](verification/customers-20260925.md); [visual customers](verification/customer-visuals-20260925.md).

Prototype: all district, competitor, choice-weight, patience, eating, reputation and table values, and the visual cap, queue layout, walk speed and tint. Visual spawning checks only the local camera (not other players'); visuals have no avoidance. Open: see decision 0024 (district appearance sets, menus, customer groups, competitor AI).

## Implemented: procedural world layout, first slice (2026-09-27)

Decision: [0026](decisions/0026-procedural-world-layout.md). GDD section 3 "World Generation". Verification:
[record](verification/worldgen-20260927.md). All generator values are PROTOTYPE.

- Generator (`Assets/Scripts/World`, assembly `FoodFactoryGame.World`, no Unity references): `WorldGenerator.Generate(requestedSeed,
  seed)` -> `WorldLayout` (districts with cuisine weights, minimum tier, customers per hour, traffic and price percent; road
  nodes and axis-aligned segments with capacity; two rail lines; buildings: restaurant and factory shells in the decision-0019
  model, premade slots for farms, houses, apartments and offices with model key and footprint/facing; stations at level
  crossings; ownership and prices; the starting restaurant). Seeded `WorldRandom` streams per phase, integer geometry, stable
  IDs. `WorldLayoutValidator` checks identity, shells, overlaps, road access and connectivity, start, farms and stations,
  and district placement; failures retry with `WorldRandom.DeriveSeed` (up to 8, all reported). `WorldLayoutText` is the
  canonical text (hash, storage, replication). `WorldGenerator.Version` = 1; a test pins seed 20260927's hash.
- Persistence: `world.db` database layout (`user_version`) **2** adds the write-once `world_layout` table (`WorldLayoutStore`),
  written before a new world's first snapshot; `GoodsSnapshotStore.HasSnapshots` tells a layout-only database (a world still
  being created) from a world, and `DevWorld.LoadOrCreate` now creates when there are no snapshot rows. Loading never
  regenerates; a damaged row stops the start. Layout 1 databases load unchanged with no layout and are upgraded (empty
  table) by the next commit. The goods payload schema is unchanged (v13). This is an exception to decision 0012's no-tables
  rule for immutable data; owner confirmation pending.
- Session: `SessionRoot.worldLayoutBridgePrefab` enables generation (`WorldGeneration.PrepareLayout` before the world save,
  `ServerLayout`, `GeneratesWorld`); `SessionOptions.WorldSeed` (`-seed <text>`; blank = random, recorded). WorldGen's
  `SessionRoot.saveFolder` is `worldgen` (DevSite keeps `dev-world`), so it never opens a DevSite world; the host panel has
  World (save folder name, `SelectWorld`), World seed and New world (`NextNewWorldName`, first unused `world-N`) and a
  world readout line. `WorldLayoutBridge` sends each authenticated client the gzip canonical text and
  SHA-256; `WorldLayoutPresenter` (`Assets/Scripts/Session/WorldMap`) draws it from that data only with the world art
  (2026-09-27, below).
- World art (2026-09-27, owner request): low-poly textured models and tileable road/rail tiles made in Blender by
  `ArtSource/World/build_world_textures.py` and `build_world_models.py` (see `ArtSource/World/README.md`; 2-134 triangles
  per asset, detail in procedural textures with normal maps and smoothness), exported to `Assets/Art/World/Models/WorldArt.fbx`,
  installed by `AgentScripts/BuildWorldArt.cs` (URP Lit materials in `Assets/Art/World/Materials`, `WorldArtCatalog`). The
  presenter tiles roads (10 m tiles with crosswalk ends and junction patches) and rail (6 m tiles) into one merged mesh per
  material, fits each building model to its footprint and facing (apartments and offices stacked from ground/middle/roof
  modules by storeys; farms as a field quad plus a barn with silo), tints restaurant awnings by owner (player green, for
  sale yellow, competitors by ID), adds box colliders, and static-batches the result. Replaces the earlier box blockout.
  `WorldArtAuthoringTests` checks the catalog reference and pieces.
- Scene: `Assets/Scenes/WorldGen.unity` (DevSite copy + bridge + presenter, catalog `Assets/Network/WorldGenPrefabs.asset`,
  material `Assets/Materials/World/WorldBlockout.mat`), authored by `AgentScripts/BuildWorldGenScene.cs`; not a build scene.
  DevSite, `GamePrefabs.asset` and the dev seed are unchanged, and the dev site and its seed still run in WorldGen.
  `WorldLayoutBridge.prefab` was also appended to the auto-maintained `DefaultPrefabObjects.asset` by FishNet's generator.
- The site question is decided in [0028](decisions/0028-sites-for-generated-buildings.md) (2026-09-29); its first piece
  (lots, reserved site IDs, the server-side purchase) is implemented below under "lots and buying buildings".

Planned / undecided: the rest of 0028 (see that section) and its open items;
customers from district densities and whether district values replace the dev district (decision 0024); competitor
behaviour; trains; the ingredient supplier near the start; traffic using road capacities; final Blender models for premade
slots; how existing `GoodsSite.MapX/Z` relate to layout coordinates (the presenter places the map beside the dev site as
PROTOTYPE presentation only: the city's edge starts 40 m north of the dev site, pieces over the dev site's floor are not
drawn, and local cameras draw to about 2.4 km while a layout is shown). Not verified: separate-process multiplayer, a player build.

## Implemented: world generator v2 (2026-09-28)

Decision: [0027](decisions/0027-world-generator-v2-land-river-roads.md) (amends 0026; owner request). Verification:
[record](verification/worldgen-20260928.md). All values are PROTOTYPE.

- Generator: `WorldGenerator.Version` = 2, `WorldLayout.CurrentFormat` = 2. New layout data: `WorldTerrain` (land heights in cm
  every 20 m; integer and float bilinear samplers, quarter turns), `WorldRiver`, `WorldBridge`, `LevelCrossing`,
  `RoadNode.Control` (`JunctionControl`), `WorldBuilding.ElevationCm`, `WorldTree` (`TreeKind`). `WorldJunctions.Stops` is the
  shared who-stops rule. Denser blocks (more local streets, per-district lot gaps, east/west block edges filled, taller
  downtown). `WorldLayoutText` still reads and writes format 1 unchanged, so v1 worlds load as flat land without the new data.
  `WorldLayoutValidator` adds the format 2 rules listed in 0027.
- Presentation: `WorldLayoutPresenter` draws a chunked 5 m terrain mesh with colliders (river channel and banks cut in,
  cut slightly under roads and rails), water ribbons, roads/rails/junctions/bridges draped over the land, level-crossing
  panels and signals, traffic lights and stop signs per `JunctionControl`, trees, building plinths, all merged per material
  per 250 m chunk; the land at the dev site is levelled to its floor. `WorldArtCatalog` gains `water` and `bank`.
- Art: 14 new Blender pieces and 6 new textures (0027), installed by `AgentScripts/BuildWorldArt.cs` (47 pieces, 50 materials;
  lamp materials use emission). Road and rail tiles are now cut into segments with skirts so they can follow the land.
- Not changed: storage (`world_layout` table, write once), replication, the site question, the dev site. Signals are
  static; nothing simulates traffic or trains.

## Implemented: lots and buying buildings, piece 1 (2026-09-29)

Decision: [0028](decisions/0028-sites-for-generated-buildings.md). Server domain only: no networking, UI or session wiring
beyond registering the catalog. Goods snapshot schema **v14**. Lot shape values are PROTOTYPE. Verification (batch
`unity test`, EditMode, run id 2 each, NUnit XML under `TestResults/lots-20260929/`): filter `FoodFactoryGame.World.Tests`
21/21 (`World-2.xml`; 120 seeds valid with lots, none retried; seed 20260927 re-pinned to `c8cef1dd…db11`),
`FoodFactoryGame.Goods.Tests` 176/176 including 9 `PropertyTests` (`Goods-2.xml`), `FoodFactoryGame.Session.Tests` 89/89
(`Session-2.xml`), `FoodFactoryGame.Baseline.Tests` 4/4 (`Baseline-2.xml`). Live Editor (Pipeline `run_tests`, async,
filter_type assembly; status JSON in the same folder): `FoodFactoryGame.World.EditModeTests` 21/21 (`editor-world-status.json`);
`FoodFactoryGame.Goods.EditModeTests` 175/176 (`editor-goods-status.json`), all 9 `PropertyTests` passing, the one failure
being the known nondeterministic `TruckTests.StepSizeDoesNotChangeTheOutcome` (GUID tie-break; it passed in the batch run
and in 1 of 3 `TruckTests` reruns, `editor-trucks-1..3.json`). Not run: PlayMode and multiplayer suites (no networking changed).

- Generator: `WorldGenerator.Version` = 3, `WorldLayout.CurrentFormat` = 3. Every building that `HasLot` (restaurant and
  factory shells, farms, stations; ownership ForSale, Competitor or Player) gets one `WorldLot { Id = lot-<building>,
  BuildingId, SiteId = site-<building>, X, Z, Width, Depth, Access }` and `WorldBuilding.SiteId` = the lot's site ID; scenery
  gets neither. A lot is the footprint extended forward to its street by `WorldSettings.SetbackFor(category)` (`Setback` 2 m;
  PROTOTYPE `FactorySetback` 12 m and `FarmSetback` 8 m, so factories and farms stand further back); `Access` is the road cell
  just past the lot's street edge, level with the first door. Site cell (x, z) = world cell (lot.X + x, lot.Z + z),
  translation only. Placement reserves whole lots, so no building or tree stands on one; side gaps between lots stay unowned.
- Format: `WorldLayoutText` format 3 adds `lot` lines after the buildings and a lot count on `end`. Formats 1 and 2 still read
  and write byte for byte and have no lots, so nothing in those worlds can be bought (development data).
- Validator (format 3): unique lot IDs and site IDs derived from the building; exactly one lot per property and none for
  scenery; `SiteId` equals the lot's (empty without one); each lot contains its building and overlaps no other lot,
  building, road, rail or water; its access cell borders its edge on a road reachable from the network; no tree on a lot.
  Road access from a door allows the category's setback.
- Goods (`GoodsWorld.Property.cs`, `WorldLayoutShells`): `GoodsSnapshot.Properties` (`GoodsProperty { LotId, SiteId,
  CompanyId }`, ownership as its own record; `GoodsCompany.SiteIds` still lists the site and must agree). v13 saves upgrade in
  memory with none; dev sites need none. `PropertyOffer` is content built from the stored layout by
  `WorldLayoutShells.PropertyOffers` (lot, site ID, building shape and doors in site cells, category, `ForSale`, price,
  access) and registered once with `RegisterPropertyOffers` (`SessionRoot.StartServer`, only when a layout exists); never
  saved. `BuyPropertyDurably(player, request, payingSite, lot, savePath)` checks, in order, `unknown-lot`, `not-for-sale`,
  `owned`, `no-grant`, `no-company`, `insufficient-funds`, then in one commit debits the price, creates the `GoodsSite`
  (`MapX/Z` = access point, name = building ID), the lot-sized `SiteLayout` and, for shells, the `GoodsBuilding`, adds the
  property and the company's site ID, and grants the buyer the new site (`property-bought`). Rejections change and record
  nothing; a replayed request returns the original outcome; a failed save restores everything (`persistence-unavailable`).
  Server-only `Bootstrap(PropertyOffer, companyId)` gives a lot without charge or grant (the starting restaurant; wired in piece 2).
- Site existence: a site exists when it has a `GoodsSite` record or any location (`SiteExists`), used by `Grant`,
  `TryGrantDurably`, `Bootstrap(GoodsCompany)`, `AddCompanySite`, `HasSite` and validation; `Bootstrap(GoodsSite)` no
  longer needs locations, so a freshly bought, empty site can be owned and granted.
- Validation: properties have unique lots and sites, a company that lists the site, a `GoodsSite` and a layout. With a
  catalog registered (checked at registration and before every save), each property is a listed lot whose site has the
  lot's size, and every listed lot whose site exists has its property (sites and properties are never removed).

Piece 2 is implemented below. Open: lot prices versus building prices (a lot costs its building's layout price), corner lots,
farm field extent, station platforms.

## Implemented: generated worlds start in their own restaurant, piece 2 (2026-09-29)

Decision: [0028](decisions/0028-sites-for-generated-buildings.md). Owner decisions of 2026-09-29: starting cash $5,000, raised the same day to $1,000,000
(PROTOTYPE, `GeneratedWorld.StartingCash` = 100,000,000 cents); bought sites are managed remotely only until piece 3. Evidence:
[verification record](verification/property-piece2-20260929.md).

- World creation (`GeneratedWorld`, Session): when the stored layout has lots (format 3), `SessionRoot.StartServer` calls
  `GeneratedWorld.LoadOrCreate(worldPath, layout, items)` instead of `DevWorld.LoadOrCreate`. It registers the property catalog,
  and a new world gets one company `company-1` with the starting cash, `Bootstrap(startOffer, company)` for the starting
  restaurant, and its first commit; no storage, belts, machines, employees, warehouse, district or competitors. No layout,
  format 1 or 2, or a format 3 save first opened as a dev world (no starting property; made between pieces 1 and 2) keeps
  the dev world unchanged.
- Joining: `SessionAdmission` takes the primary site (the starting site, or `dev-site`) and exposes it as `PrimarySiteId`;
  a player gets an inventory with the dev starter goods there and a grant on every other site of the company that owns it
  (PROTOTYPE: everyone joins that one company). The join answer (`JoinResponseBroadcast.SiteId`) names the primary site;
  `ClientSiteSubscription` subscribes only once it is known (`SetPrimary`), and presenters use `SessionRoot.ClientSiteId`
  instead of `DevWorld.SiteId`. Players spawn on the starting lot's apron two cells out from the first door, facing it
  (`SessionRoot.ApronSpawn`); dev worlds keep the scene's spawn points.
- Purchase over the network (Goods): `GoodsNetworkBridge.RequestBuyProperty(requestId, payingSiteId, lotId)` →
  `BuyPropertyDurably`, replied to the sender and broadcast when accepted. The same commit now grants the new site to every
  teammate: each player (not an `employee-` actor) granted any site of the buying company. Views keep all `Properties`
  (ownership is public map information; cash stays private to the owner's sites). `GoodsWorld.ViewSiteId` names a baseline
  by its layout's site, so a bought site with no locations still reaches clients.
- Presentation: `WorldLayoutPresenter` in a generated world puts the starting lot's grid centre at the scene origin and its
  ground floor at y = 0 (`_layoutOrigin`), levels the land over the lot, skips the starting building's model (its shell comes
  from site data) and trees or signs on that lot, paves every lot, and hides `devSiteOnly` (DevSite's floor, landmarks and
  NavMesh; shown again for dev-site worlds). Restaurant awnings tint from `Properties` first (this client's company green,
  other companies a competitor colour), else from the layout's for-sale or competitor state, and re-tint on each new
  baseline. Offers on clients come from the replicated layout (`WorldLayoutShells.PropertyOffers`), nothing extra is sent.
- Buy panel: purchasable buildings carry a `PropertyMarker`; aiming at one within 60 m and pressing Interact (E) opens
  `PropertyPanel` (`InteractionScreen.Property`): category, price, lot size, for sale / owned by your company / owned by
  another company / not for sale, company cash, Buy (pending and server rejection shown like `LogisticsPanel`). An accepted
  purchase makes the client watch the new site. `BuildWorldGenScene.cs` wires the panel and `devSiteOnly`.
- PROTOTYPE limits (until piece 3a, below): only the primary site is drawn; bought sites are reachable only through remote management
  (logistics panel). $1,000,000 buys many lots (seed `piece-two`: restaurants from $4,320, factories from $12,672, farms
  from $25,200, stations from $14,400).
  A restaurant's apron is the 2 m setback, so machines wider than 2 cells (the 3x3 oven) do not fit on it; the dock and
  table do. Ownership shows on the map only for restaurants (the only art with an awning); the panel shows it for all.

Planned (piece 3 and later): several sites drawn at once so players can walk into bought buildings, carrying goods by
[0029](decisions/0029-carrying-goods-between-owned-sites.md) (accepted, not implemented); the ingredient supplier near the
start, world-owned docks, merging lots, reselling, separate companies per player. Customers, districts and competitors
linked to lots are implemented below (decision 0030). (The piece 2 notes above describe new worlds as having no
district or competitors; that was true until 0030.)

## Implemented: customers in generated worlds (2026-09-30)

Decision: [0030](decisions/0030-customers-in-generated-worlds.md). Owner decision of 2026-09-30: the starting restaurant is
pre-equipped. Evidence: [verification record](verification/customers-worldgen-20260930.md).

- Derivation (`WorldLayoutCustomers`, Goods): a pure function of the stored layout. One `GoodsDistrict` per block of each
  layout district, `district-<id>-<block>`, spawning at the block's centre, with the district's customers per hour shared
  by area (largest remainder). Wealth, dine-in share and range come from a PROTOTYPE table per district kind, and liked
  cuisines are those weighted at least 25. One `GoodsCompetitor` per competitor-owned lot, `competitor-<building>`, with
  `LotId` and the lot's access point as its map position; cuisine is drawn from the district's weights, tier is the
  district's minimum, and price, servers, service time and seats come from an FNV-1a hash of the building ID. Seed
  `piece-two`: 25 districts, 314 competitors.
- Goods schema v15: `GoodsCompetitor.LotId` (`""` for dev competitors and upgraded v14 saves). A linked lot is unique
  among competitors and never a property's lot. With a catalog, it must be listed and not for sale (checked in
  `Bootstrap(GoodsCompetitor)` and before every save, and a catalog that disagrees is refused at registration).
- Choice: `Decide` scores only the restaurants within the district's range (the per-district candidate cache in
  `GoodsWorld.Customers.cs`, rebuilt with the diner catalog or when a district is added), in catalog order, so outcomes
  equal scoring every restaurant. Diner records are looked up by dictionary.
- World creation (`GeneratedWorld`): a new world gets the starting counter (`start-counter`) and table (`start-table`)
  placed inside the shell, plus every derived district and competitor, before its first commit. An existing generated
  world gains missing districts and competitors by ID, and a counter or table only if it has never had one of that kind,
  committed before serving. `SessionRoot` passes the counter and table definitions. Dev worlds are unchanged.
- Content: supplier offer `supplier-counter` ($50.00, `Counter1.asset`), in both scenes and in `BuildDevSite.cs`.
- Presentation: `SiteNavigation` (on the WorldGen scene, added by `BuildWorldGenScene.cs`) builds a runtime NavMesh for a
  generated lot. It covers the lot's floor and a 6 m street band reaching 12 m past each side, with the ground-floor walls
  built in. It is rebuilt when the site, size or buildings change, and equipment still carves it. `SiteStreet` finds the
  street side: the side on which the shell does not reach the lot's edge. On such lots, `CustomerPresenter` spawns and
  removes figures on the street band, out of view. Competitors' customers: drawn since decision 0033 (below).
- Scale (0025 budgets, `CityCustomerBenchmarkTests`): tick p99 at most 0.68 ms, a 100-customer decision burst 3.3 ms,
  commit maximum at most 31.7 ms, payload 157 KB.
- PROTOTYPE limits: about 1,870 customers an hour across about 300 restaurants, so the starting restaurant makes roughly 3
  sales an hour. The street band is flat. Queue and ordering spots are fixed in site axes.

## Implemented: owned sites drawn in place, piece 3a (2026-09-30)

Decision: [0031](decisions/0031-several-sites-drawn-at-once.md). Presentation only; nothing on the server or in saves changed.
Walking into a bought building (carrying goods by 0029) and spawning where the player logged out: piece 3b, below.

- Placement (`SitePlacement`, Session): the one rule for where sites stand. The starting lot keeps the scene origin; every
  other lot's grid centre stands at its map position relative to the starting lot's, at its building's elevation minus the
  starting building's (a shift only). `WorldLayoutPresenter` builds it from the shown layout and activates it;
  `SiteGridSpace` applies `SitePlacement.OriginOf(siteId)` in `FootprintCenter`, `AnchorAt`, `LevelAt(layout, height)` and
  `FloorHeight`, so dev worlds and the starting site are unchanged.
- `DrawnSites` (owned by `SessionRoot`, ticked every frame): watches every site the current site's company owns (from
  `Properties`), and lists the current site plus watched owned lots within 300 m of the local camera (dropped past 320 m).
  Its `Version` changes with the set, any drawn baseline or the placement.
- Presenters loop over drawn sites: `EquipmentPresenter`, `BuildingPresenter` (indoors in any drawn shell; `HidesLevel`
  now takes the site), `BeltPresenter` (`Layout` stays the current site's), `TruckPresenter`, `CustomerPresenter` (a
  bound second-client subscription gets its own `DrawnSites`), and `SiteNavigation` (one runtime NavMesh per drawn
  generated lot; `BuiltForSite`). Machines on a drawn site other than the current one are not hover or pickup targets.
- Map: every lot is levelled to its building's ground floor (8 m blend) and paved just under it; trees and signs stay off
  every lot. Restaurant and factory models (including the starting one) are hidden while their site is drawn
  (`ModelShown`).
- Evidence (live Editor, 2026-09-30): Session EditMode assembly 104/104 including 5 new `SitePlacementTests`; all PlayMode
  tests 34/34 including the new `WorldGenSessionTests.ABoughtRestaurantIsDrawnWhereItStands` (a bought restaurant's shell
  within one cell of its map building at its elevation, map model hidden, its own NavMesh). Not yet: visual captures, the
  presentation cost with ten equipped sites, multiplayer beyond the existing checks (piece 3c).

## Implemented: walking into owned buildings, piece 3b (2026-09-30)

Decisions: [0029](decisions/0029-carrying-goods-between-owned-sites.md), [0031](decisions/0031-several-sites-drawn-at-once.md).
Owner decisions of 2026-09-30: players rejoin where they logged out, and the inventory stays with the player (on the site
last entered). No goods schema change; the player registry is now schema v2.

- Goods (`GoodsWorld.Entering.cs`): `EnterSiteDurably(player, request, siteId, mapX, mapZ, path)` checks, in order,
  `forbidden`, `unknown-site` (not a listed lot or no site), `not-on-lot` (outside the lot plus `EnterMarginMetres` = 2 m,
  PROTOTYPE; NaN refused), `no-inventory`, `already-there`, `reserved`; then in one commit moves `carried:<player>` to the site,
  re-owns its lots by the site (as truck deliveries do) and moves every machine the player holds. Only accepted entries are
  recorded, so a retry replays; a failed save restores everything. `CarriedSiteOf(player)`.
- Invariant (`ValidateCarrying`, in `Validate`): every `carried:` location's holder is granted its site, and every held
  machine is on its holder's carried site. Dry run on copies of all five existing application saves: all load.
- Network: `GoodsNetworkBridge.RequestEnterSite(requestId, siteId)`. The position is the server's own copy of the
  connection's avatar, supplied by `SessionRoot` through `InitializeServer(..., mapPositionOf)` and converted with
  `SitePlacement.ToMap` (`no-position` without a generated world or avatar). Accepted entries are broadcast.
- Joining: the join answer names `SessionAdmission.CurrentSiteOf(player)`, the site holding the inventory (the primary site
  for a new player). Players spawn at their saved pose (`PlayerRegistry.PoseOf`, table `player_poses`, written on disconnect
  and server stop); new players at the apron or scene spawn points as before.
- Client: `SiteEntry` (owned by `SessionRoot`) sends one enter request when the local avatar stands on an owned lot other
  than the current site, and on acceptance `ClientSiteSubscription.SwitchTo` makes it current (the old site stays watched).
  A refused lot is not asked again until the avatar leaves it; the street keeps the last site. `EquipmentInteraction` closes
  screens and clears the cursor when the current site changes.
- Evidence (live Editor, 2026-09-30): Goods EditMode assembly 196 tests, 195 passed on the full run with the known flaky
  `TruckTests.StepSizeDoesNotChangeTheOutcome` failing and passing alone; 13 new `EnterSiteTests` pass. Session EditMode
  106/106 (new registry pose and v1 upgrade tests). World 21/21, Baseline 4/4. All PlayMode tests 36/36, including
  `WalkingIntoABoughtRestaurantCarriesTheGoodsThere` (enter, goods committed on the new site, a counter bought and placed
  there at its map cell, customer figures at the second site, walking back) and `RejoiningStartsWhereThePlayerLeftWithTheGoods`.
- Piece 3c ([record](verification/several-sites-20260930.md)): a loopback teammate sees the host enter, its goods move and a
  counter placed in the second site; frame time with 11 drawn sites and 53 ovens is 9.9 ms mean (1 site: 5.5 ms, Editor).
  Not yet: a separate-process multiplayer check and
  review of the captures by someone else. Employees still use the origin for their site (dev worlds only).

## Implemented: competitors' customers drawn (2026-09-30)

Decision: [0033](decisions/0033-drawing-competitors-customers.md). Presentation and replication only: the simulation, saves and
the goods schema (v15) are unchanged. Evidence: [verification record](verification/competitor-customers-20260930.md).

- Goods (`GoodsWorld.Crowd.cs`): `CrowdNear(mapX, mapZ, radius = CrowdRadiusMetres)` returns a read-only `GoodsCrowdView`.
  It lists competitors with a lot within 150 m (Manhattan, PROTOTYPE) that have customers to draw, in catalog order, with
  `Servers` and `Seats`. For each customer it carries ID, district, appearance, dine-in, state, ticket, travel time if
  travelling, and whether they walked out elsewhere. Travelling customers are included only in their last 12 s
  (`CrowdTravelSeconds`), unless they walked out of another restaurant. `CrowdSignature` decides whether a crowd changed.
  It is never stored, validated or read by the simulation; a test checks that asking for it changes nothing.
- Network (`GoodsNetworkBridge`): after each clock step, every connection with a site subscription gets
  `TargetCrowd(json, epoch)` for its avatar's map position (`mapPositionOf`, the server's copy), only when its signature
  changed. Without a position it gets an empty crowd. Crowds are dropped on an older epoch but never move it. Clients raise
  `CrowdReceived`; `ClientSiteSubscription.LatestCrowd` keeps the latest (cleared on reset), and `DrawnSites.Crowd` exposes
  it outside `Version`.
- Geometry (`CompetitorFrontage`, Session): for a lot from `SitePlacement.OfferOf(lotId)`, a door point just outside its first
  door, an apron point on the lot's street edge, a kerb point on the street band, the `SiteStreet.Points` band for any lot,
  and `QueueSpot(rank)`: 8 places (PROTOTYPE) along the facade toward the side with more room, bending back in a second row.
  `Arrive` and `Depart` give the fixed lines figures walk. Competitor lots get no NavMesh.
- Presenter (`CustomerPresenter`): candidates come from every drawn site (unchanged targets) and from the crowd (travelling:
  the apron point; queued: their queue place, none past 8; ordering or eating: the door, then hidden). Owner decision 4:
  the current site's customers take places first, then everyone else by camera distance to their restaurant. Within a
  restaurant, customers using it come before arriving ones, then by ticket. The cap stays 100. Hidden figures take no place.
  A figure that loses its place is removed only if off screen; otherwise it walks out of view first, and new figures wait
  for its place. Figures stay keyed by customer ID across both sources. New crowd figures appear at street points out of
  the local camera's view; a competitor whose whole street is on screen gets none until the camera turns.
- Evidence (2026-09-30): Goods EditMode 199/199, Session EditMode 110/110, World 21/21, Baseline 4/4, Goods PlayMode 2/2,
  Session PlayMode 38/38 (3 new `CompetitorCustomerTests`, including a loopback two-client check). Crowd view p99 0.195 ms,
  mean 1.1 KB per send. Frame time with the cap full: 19.1 ms mean with 92 mixed figures, 23.7 ms with 100 at the starting
  restaurant, 10.7 ms with none (Editor). **The 100-figure cap does not fit 16.7 ms in the Editor**, with or without this
  change. That is open for the owner, as is the independent review of the captures.

## Required Constraints for Future Implementation

- The server owns gameplay state; clients request validated actions through the command contract in decision 0002.
- Site operations continue independently of client cameras, interest, or presentation scene loading while the world simulation runs.
- Persistent identities, inventory transfers, payments, and job reservations must survive failure/cancellation without silent loss or duplication.
- Player and employee operational rules should be shared; input and AI choose actions through those rules.
- Visual objects must not become the sole owners of authoritative simulation state.

These remain accepted contracts; only the bounded goods slice, its station jobs, equipment placement, the working oven, conveyor belts, company cash, the sell counter, supplier purchases, equipment purchases, spoilage timing/refrigeration, building shells, factory floors, conveyor lifts, trucks with loading docks, truck routes and fleet, customers, and the procedural world layout above have a runtime interface. Customer purchases credit cash inside the clock tick; supplier purchases and floor orders are the player payment commands. Trucks move goods between sites only inside the clock tick, through their own cargo locations.

## Planned / Undecided

- Full-world simulation scheduling, command interfaces outside goods, replication interest/deltas, and persistence of other systems: defined in decision 0002; implementation pending.
- Player count, hosting/disconnect behavior, and exact performance hardware: GDD decisions pending.
- Physical goods model: selected in GDD section 28 and decision 0003; a logical lot/condition/transfer/recovery slice is implemented. Transport staging, actual placed-world positions, carrier/vehicle handling constraints, and visual projection remain pending.
- Offline progression, host migration, discovery/lobbies/relay, and the shipped hosting model remain undecided. A direct-IP development host/join flow exists (decision 0005).
- SQLite is the storage for all persisted data (decision 0011): the goods world (with its write-once world layout, decision 0026), player registry (with each player's last pose, decision 0031) and client identity. MoonSharp runs the prototype employee scripts (above); no wider scripting or modding role is selected.
- Customers: first build and local visual customers implemented (above, decision 0024), and in generated worlds with map districts and lot-linked competitors (decision 0030); multi-camera out-of-view spawning, menus, customer groups, competitor AI and demand balancing remain open; competitors' customers are drawn (decision 0033, above), with the 100-figure cap over the Editor frame budget open. The benchmarked choice model in the test assembly ([record](verification/customer-choice-benchmark-20260925.md)) is a separate prototype, not the runtime code.
- Multiplayer smoke tests and representative scale benchmarks follow implementation; current tests do not establish replication correctness or the 60 FPS target.

## Standalone restaurant art kit (2026-10-01)

Implemented **art only**: 59 modular restaurant models and 16 shared URP materials in `Assets/Art/Restaurant`, editable
source in `ArtSource/Restaurant/Restaurant_Kit.blend`. Import and authoring checks live outside the game under `AgentScripts`.
The kit supplements existing equipment and exterior art; it adds no gameplay components, catalog entries, or scene wiring.
Restaurant-building behavior from decision 0034 remains planned. Placement conventions and reuse inventory:
`ArtSource/Restaurant/README.md`; evidence: `docs/verification/restaurant-art-20261001.md`.

## Baseline Test Evolution

The starter tests intentionally check the current scene/input/catalog authoring. When real bootstrap/additive scenes or a game-specific prefab catalog replace it, update the tests to validate the new accepted contract. Do not put cameras into intentionally camera-free scenes or restore demo prefabs simply to retain these starter assumptions.
