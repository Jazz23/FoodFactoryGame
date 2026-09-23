# Architecture and Implementation Status

## Authority

Gameplay requirements and open product decisions are in [the GDD](../Food_Factory_Restaurant_GDD.md). Resource facts are in [dev_resources.md](../dev_resources.md). This document distinguishes implementation from intended architecture.

## Implemented Baseline

- Unity `6000.5.9f1`, URP `17.5.0`, and Input System `1.20.0`.
- `Assets/Scenes/DevSite.unity` is the only enabled build scene (session bootstrap, below). `Assets/Scenes/SampleScene.unity` and its oven prototype remain in the project but are no longer built.
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

- `FoodFactoryGame.Goods` holds server-instantiated stable-ID lots and locations, integer quantities, one location per lot, owner/site grants, binary spoilage, elapsed exposure, active reservations, terminal command outcomes, and an integer-second authoritative clock. Ambient time accumulates exposure; refrigerated time does not. Moves retain prior exposure. Equivalent lots merge only when owner, location, item, condition, and exposure/threshold match; reserved lots cannot merge.
- `Transfer` validates a same-site route, grant, source owner, unreserved quantity or owned reservation, and destination unit capacity before locked mutation. A partial transfer splits with a new ID. Duplicate request IDs replay stored terminal outcomes; another actor cannot replay someone else's outcome. `Cancel` releases an unconsumed reservation; committed transfers are not reversible. No in-flight transport or cross-site route has been implemented.
- `GoodsSnapshotStore` serializes (schema v3 since equipment placement, below) world ID, time/revision, lots, locations, grants, reservations, and outcomes with a checksum. An explicit path is required; it writes/flushed a temporary file then atomically replaces the latest with a previous-version fallback. Recovery validates invariants; unknown newer schemas fail. Concurrent saves reject older/conflicting revisions. Every acknowledged mutation has a durable boundary: `TransferDurably`, `ReserveDurably`, `CancelDurably`, and the clock tick `TryAdvanceDurably` commit the snapshot before acknowledging and restore the pre-command state if the commit fails. Terminal outcomes are keyed per actor (player ID + request ID), so one actor cannot claim or poison another actor’s request ID. This is a goods-slice snapshot, **not** the full-world decision-0002 persistence contract; no production snapshot cadence is implemented; the only migrations are the in-memory v1→v2→v3 upgrades.
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

- `SessionRoot` (in `DevSite`) is the server composition root, with Host, Client and Server-only modes. Server start creates the save directory, loads `world.snapshot` or creates and commits the **development seed** (`DevWorld`: world `dev-world`, site `dev-site`, one storage location; placeholder content), opens the SQLite `players.db`, configures the authenticator, and only then starts FishNet. Once listening, it spawns `GoodsNetworkBridge` and calls `InitializeServer` with `DevAuthenticator.PlayerIdOf` as the only connection→player resolver. The bridge ticks the world whenever the server runs, including with no clients (`-server`).
- `PlayerRegistry` (SQLite, server-only) maps SHA-256(client secret) to a stable `player-<guid>` ID; raw secrets are never stored. `SessionAdmission` resolves or creates the identity first, then commits the `dev-site` grant with `GoodsWorld.TryGrantDurably`; either failure rejects the join. Registry schema is `user_version` 1; newer is refused. The goods snapshot stayed at v2 in this step (v3 since equipment placement).
- `DevAuthenticator` (FishNet `Authenticator`): clients send `{DisplayName, Secret}`; the server answers `{Accepted, Reason, PlayerId}` before passing or failing the connection. Rejection reasons: `invalid-name`, `invalid-secret`, `persistence-unavailable`, `already-connected`, `server-full` (cap 8), `server-not-ready`. `already-connected` is checked with a read-only lookup before any write. A rejected client disconnects itself after reading the reason; the server kicks it only after a 2 s grace, because FishNet's forced close raced the reply over real UDP. The connection→player map is server memory only and is cleared on disconnect.
- `ClientIdentity` keeps the client secret at `persistentDataPath/Identity/client.secret` (`-identity <file>` override).
- FishNet sends start scenes only after authentication, so `SessionRoot` spawns one `Player` per connection on `OnClientLoadedStartScenes`, owned by that connection and with a server-set display name. FishNet despawns it on disconnect; the world keeps running.
- `Player.prefab`: `NetworkObject`, client-authoritative `NetworkTransform`, `CharacterController`, `PlayerAvatar` (camera-yaw-relative `Player/Move`), a capsule placeholder tinted per display name, and a disabled `CameraRig` (`OrbitCameraRig`, camera, audio listener) that only the owning client enables. It orbits with `Player/Look` (always on since 0007; formerly while `Player/Orbit` was held); `Player/Zoom` (scroll) steps distance. Pitch is limited to 10–80° and distance to 3–20 m.
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

Prototype, labelled in code or content: free placement, no range or line-of-sight check, dev grid size and footprint, dev inventory capacity, and grid-to-scene mapping centred on the origin. Scene landmarks and walls are not placement blockers.

Not yet: buying/selling equipment, other machines and belts, and moving equipment between sites. Starting jobs, the running display and the hotbar came with the working oven (below).

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

## Required Constraints for Future Implementation

- The server owns gameplay state; clients request validated actions through the command contract in decision 0002.
- Site operations continue independently of client cameras, interest, or presentation scene loading while the world simulation runs.
- Persistent identities, inventory transfers, payments, and job reservations must survive failure/cancellation without silent loss or duplication.
- Player and employee operational rules should be shared; input and AI choose actions through those rules.
- Visual objects must not become the sole owners of authoritative simulation state.

These remain accepted contracts; only the bounded goods slice, its station jobs, equipment placement and the working oven above have a runtime interface.

## Planned / Undecided

- Full-world simulation scheduling, command interfaces outside goods, replication interest/deltas, and persistence of other systems: defined in decision 0002; implementation pending.
- Player count, hosting/disconnect behavior, and exact performance hardware: GDD decisions pending.
- Physical goods model: selected in GDD section 28 and decision 0003; a logical lot/condition/transfer/recovery slice is implemented. Transport staging, actual placed-world positions, carrier/vehicle handling constraints, and visual projection remain pending.
- Offline progression, host migration, discovery/lobbies/relay, and the shipped hosting model remain undecided. A direct-IP development host/join flow exists (decision 0005).
- SQLite stores the prototype player identity registry (decision 0005); its role for other persistence is undecided. MoonSharp remains a declared dependency with no selected role.
- Multiplayer smoke tests and representative scale benchmarks follow implementation; current tests do not establish replication correctness or the 60 FPS target.

## Baseline Test Evolution

The starter tests intentionally check the current scene/input/catalog authoring. When real bootstrap/additive scenes or a game-specific prefab catalog replace it, update the tests to validate the new accepted contract. Do not put cameras into intentionally camera-free scenes or restore demo prefabs simply to retain these starter assumptions.
