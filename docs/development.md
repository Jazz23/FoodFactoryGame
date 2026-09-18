# Development Workflow

## Editor Rules

1. Confirm the connected Editor with `unity status --json --no-banner --project-path E:\Projects\Unity\FoodFactoryGame`.
2. Discover project commands with `unity list --json --no-banner --project-path E:\Projects\Unity\FoodFactoryGame`.
3. Use registered `factory_*` commands for recurring factory workflows.
4. Serialize live mutations, compilation, and test execution. Parallelize independent reads only.

## Visual Defect Verification

For a visual defect such as the Building 13 dark triangle:

1. Capture a baseline with the exact scene, camera, zoom, lighting, gizmos, resolution, and region of interest recorded.
2. Use `Food Factory/Visual Regression/Capture Building 13 Baseline` and keep the generated manifest beside the PNG. The default artifact directory is `TestResults/VisualRegression/Building13`.
3. When delegated work is supported, assign independent evidence gathering or review with explicit ownership and a disjoint scope.
4. Make one controlled change at a time and keep Unity mutations, compilation, and tests serialized under one owner.
5. Use `Food Factory/Visual Regression/Capture Building 13 After + Compare` to restore the recorded view, capture the after image, and compare the declared ROI. A changed pixel inside the ROI fails the comparison unless the manifest tolerance explicitly allows it.
6. Treat a still-visible artifact as a failed acceptance result. Stop speculative edits.
7. Use `Food Factory/Visual Regression/Write Building 13 Evidence Packet` to package the screenshots, comparison, hierarchy path, renderer/material/shader names, mesh bounds, attempted change, console state, and view configuration for independent review or escalation.

The hard acceptance test is: **“In this exact Building 13 view, the dark triangle is absent.”** The implementer must not be the sole authority for visual pass/fail. An independent reviewer must record a name, verdict, and notes in the evidence packet; a pixel comparison pass without that review remains pending. Recurring defects should use the deterministic editor capture and comparison with documented tolerances.

The default capture resolves Building 13 from `Assets/Authoring/OutsideTestBuildingLayout.asset` / `OutsideTest.unity` by stable building ID, records the active SceneView camera and view flags, and renders both images at the same dimensions. The captured renderer evidence includes hierarchy paths, renderer/material/shader names, and world mesh bounds. If the intended camera framing or ROI is unknown, stop at evidence collection and request those values; do not infer a visual pass from an unrelated camera.

The artifact contract is `baseline.png`, `after.png`, `view-manifest.json`, `comparison.json`, and `evidence.json`. `comparison.json` is the machine-readable verdict and includes dimensions, requested/clamped ROI, tolerance, differing-pixel count, maximum channel delta, and failure reason. `evidence.json` is not accepted until `reviewGate.verdict` is `approved` with reviewer notes; the utility leaves it `pending-review` by default.

## Verification Contract

`factory_verify` starts one named verification profile and returns a run ID. Poll its status command until it reaches `passed`, `failed`, or `infrastructure_failed`.

Every result records:

- Requested profile and filter.
- Run ID and source revision when available.
- Matched test count.
- Summary counts and failed test messages.
- Full-result artifact path.

Zero matched tests, compile failures, runner initialization failures, and timeouts are not successful verification. A test failure is distinct from an infrastructure failure.

## Failure Workflow

1. Establish or record the baseline.
2. Run one explicit profile with an isolated save path for stateful tests.
3. Capture structured player, floor, transition, presentation, UI, and relevant console state on failure.
4. State the evidence-backed hypothesis before changing code or adding waits.
5. Re-run the smallest profile that tests the hypothesis.

## Test Boundaries

- Domain tests call application commands directly and use isolated `FactoryTestWorld` state.
- `FactoryElevatorTransferTests` verifies paired-floor elevator buffering and item conservation in EditMode without opening or writing a save database.
- Scene/network tests cover ownership, transitions, sharing, isolation, and disconnect behavior.
- UI tests deliberately verify binding, readiness, and displayed state.
- PlayMode fixtures own temporary database paths, network lifecycle, static/UI reset, stable identities, and cleanup. `FactoryTestWorld` provisions the two-floor building-2 transition fixture in the isolated runtime world instead of depending on authored OutsideTest building IDs.
- Runtime database-path overrides are session-scoped: `NAIStateManager` resets the static override at `SubsystemRegistration`; tests configure their isolated path afterward. When checking bootstrap against project data, verify the selected path first and use a consistent isolated database backup for any operation that can write.
- `PlayerInventory` writes to `Application.persistentDataPath/food-factory-inventory.db`; stateful inventory play-mode checks require a fixture-provided isolated path. All 80 grid slots are ordinary storage, and the 1–9 / 0 hotbar shortcuts are blank until assigned by clicking an inventory item and then a hotbar slot. The held item icon follows the cursor; shortcut bindings persist separately and reference item types without moving stacks out of inventory.
- Tests must not resize gameplay buildings or modify the application database.

## Transition Verification

- `FloorTransitionCoordinatorTests` verifies phase sequencing, stale-sequence rejection, loaded-floor reuse, failed-load cleanup, and building return state without a live network connection.
- Scene/network transition tests must additionally verify player arrival, scene unloading, disconnect cleanup, and client transition-state reset.

## Compact Layout Workflow

- `factory_export_building_layout --dry_run true` previews an OutsideTest layout export.
- `factory_export_building_layout --confirm true --assign true` writes `Assets/Authoring/OutsideTestBuildingLayout.asset` and assigns it to `TestBuildingCreator`.
- `factory_validate_building_layout` checks the asset schema, topology, and generated-layout match.
- `factory_rebuild_outside_shells --dry_run true` verifies deterministic shell output without changing the scene.

## Immediate Topology Workflow

- Use the Test Building Creator list to inspect ID, anchor, exterior size, usable interior, and stories.
- Use `Rebuild Shells` in the Test Building Creator inspector to manually regenerate its visual, collision, and door shell output.
- Select the full database path in the creator's `Selected Save Topology` section; successful building creation, door placement, story changes, and topology updates are saved there immediately.
- `Preview Changes...` remains available for inspecting differences that existed before an edit. `Apply Selected Save...` is for manually resolving those pre-existing differences.
- Immediate writes use the direct atomic, reload-verified topology path, preserve unrelated save-only buildings for selected create/update operations, track newly created floors/equipment/migration mappings during story additions, and avoid the extra preview-plan database load. Preview/apply still uses authored and database fingerprints to reject stale changes.
- Authored story removal is labelled `Delete Authored Top Story`; entity state is relocated when possible and the edit fails when it cannot be retained. Authored building removal is labelled `Delete Authored Building` and, after destructive confirmation, purges that building's dependent floors, entities, connections, routes, and trucks from the selected database.
- `Clear Authored Buildings` applies the same confirmed purge to all exterior buildings in the selected database. Inspector selection, repaint, startup migration, undo/redo, and generated-shell refresh are not persistence operations.
- For an isolated database copy, use `FactoryWorldSqliteStore.CreateConsistentBackup` or SQLite `VACUUM INTO`; do not copy only the main file while WAL data may be active.
- The Unity Editor default database is `<project-root>/factory-world.db`; production uses `Application.persistentDataPath/factory-world.db`. Keep the project-local database out of source control.
- `factory_reconcile_save` is position-only reconciliation. Run it with an explicit isolated path and dry-run first; it does not apply authored dimensions.

## Fixed-Camera 3D Prototype

- Open `Assets/Scenes/Factory3DPrototype.unity` to evaluate the reversible 3D slice. It uses a two-floor in-memory fixture, adapter-based logical picking, Input System movement, and discrete floor cycling through the existing Build/Rotate action.
- The prototype's save/load check writes only to `Application.temporaryCachePath/food-factory-3d-prototype.db`; it must never use the application database. The edit-mode acceptance test is `Factory3DPrototypeFixtureTests.TwoFloorFixturePreservesTopologyThroughIsolatedSaveLoad`.
- Keep the 2D systems authoritative while comparing the slice. Continuous vertical movement, ramps, NavMesh, voxel occupancy, and free-camera controls are intentionally outside this first experiment.

## 3D Test Building Creator Companion

- Open `Food Factory/3D Test Building Creator` while `OutsideTest.unity` is active. The window discovers the scene's `TestBuildingCreator`, or accepts one through its Object Field.
- Select a building from the list or Scene View, choose its active floor, and enable Move to preview a snapped footprint drag. Commit Move records the new logical anchor with Undo support and rebuilds the existing generated shell; Cancel Move discards the preview.
- Use `Frame Selected` for the fixed-angle orthographic Scene View. Floor elevation and 3D handles are derived presentation; authored building IDs, anchors, footprints, stories, and doors remain the source of truth.
- `Apply Authored Topology to Selected Save` is the only database write in this companion. Point it at an isolated save for experiments; selection, camera framing, preview, shell rebuild, and Undo do not write a database.
- Changing or clearing the assigned creator, losing its grid, changing scenes, or closing the window disposes the transient proxy root; the editor-update hook checks those identities before its authority no-churn fast path, so cleanup does not require a Scene View repaint or play-mode transition. Scene View, play-mode transition, and editor-update subscriptions are idempotent across editor enable/disable cycles and are removed on disable/destroy. Entering or leaving Play Mode clears the proxy before runtime authority is ready, and the update hook rebuilds it once an initialized `NAIStateManager` is available without falling back to authored records.

### Derived OutsideTest Proxy

- `Factory3DOutsideTestProxyAssembler` consumes the current logical building records and optional authoritative floor/entity records. It creates a disposable `Factory 3D OutsideTest Proxies` hierarchy with `Building <id>` and `Floor <index>` nodes, full shell-footprint slabs, topology-derived perimeter walls, wall-ID-derived door markers, and interior-to-exterior equipment markers. Building-level doors are emitted only on `Floor 0`, because `BuildingRecord.DoorPlacement` has no floor semantics in the current 2D model. Runtime proxy sources use the read-only `NAIStateManager.TryGetOutsideTestBuildingInteriorSemantics` query; missing IDs, an absent manager, or a manager that is not ready clear the proxy instead of silently defaulting, while editor preview continues to use authored records.
- Proxy floor elevation is `BuildingCoordinates.GetFloorElevation(floorIndex, wallHeight)` and all planar positions go through `FactorySpatialAdapter`; slab bounds span `elevation - FloorSlabThickness` through `elevation`, with top/bottom prism winding facing up/down for backface-culling materials. No 3D position, elevation, proxy ID, or proxy state is saved.
- Reconciliation is sorted by stable building identity, reuses existing named nodes and unchanged slab meshes, removes stale or duplicate nodes plus generated mesh objects, and ignores invalid topology without modifying the source records. A shell footprint includes its wall cells; equipment presentation applies the one-cell interior inset only to the derived marker position. The `FactoryWorldState` overload and façade-sourced overload use the authoritative interior-only classification to skip that inset for interior-only records. Slabs span `elevation - thickness` through `elevation` with deterministic upward top and downward bottom winding.
- The editor window feeds a pending snapped move into the disposable proxy as a cloned record, including `TestBuildingCreator.TryTranslateWallSpanId` door translation. Commit/cancel/Undo and the explicit save-application boundary remain unchanged.
- `Factory3DOutsideTestProxyPlayModeLifecycleTests.ActualPlayModeLifecycleUsesCallbacksAndUpdateWithoutSceneViewRepaint` is the real transition integration check. It is intentionally run by the Editor test runner: a PlayMode-run test is aborted by Unity when its body yields `ExitPlayMode`, so this test uses actual `EnterPlayMode`/`ExitPlayMode` instructions and the production `EditorApplication.playModeStateChanged` and `EditorApplication.update` callbacks instead. It creates and removes a temporary fixture scene asset, seeds a temporary database under `Application.temporaryCachePath`, and never calls the proxy refresh helper or repaints SceneView.

## Presentation-Scale Verification

- `InteriorSizingTests` covers logical-to-world scaling plus generated interior floor-cell and doorway proportions.
- Press `L` in an interior to toggle the generated test grid lines. The toggle is an Input System action and leaves the interior boundary collision enabled.
- Truck route presentation should show a solid marker in front of building surfaces and a wireframe marker when the route carries it behind a building; the truck route play-mode smoke test remains the gameplay regression check.
- The `outside_floor` profile covers isolated-save transition, movement, interaction, and equipment tests across `OutsideTest` and `insidefactory0`.
- `InteractEntersBuildingFromExteriorArrival` exercises the configured E binding after physics settles at the exterior arrival, enters and exits through portal selection/RPCs, and verifies the return position remains usable. Direct scene-manager transition calls do not cover interaction reachability. Captures are `Temp/player-ground-interior.png` and `Temp/player-ground-exterior.png`.
- The `truck_route` profile covers the isolated-save equipment, dock, transition, reload, and delivery route smoke test.

## Cross-Floor 3D Route Presentation

- `Factory3DRouteFixture` creates the fixed-ID two-floor Source → Belt → Elevator → Belt → Storage route and saves only to a `food-factory-route-*` path under the OS temp directory.
- Build a read-only snapshot with `Factory3DRouteSnapshotBuilder.Build` or `NAIStateManager.BuildRoutePresentationSnapshot`. The logical `FactoryWorldState` remains authoritative; floor elevation, world coordinates, queue markers, transfer phase, and proxy identity are derived presentation data.
- Call `Factory3DRouteProxyView.Rebuild` after authority refreshes and `SetActiveFloor` when the selected floor changes. Passing a null snapshot or calling `ClearAuthority` removes all disposable route nodes. Hidden floors continue to simulate because visibility only changes proxy reconciliation.
- `Factory3DRouteAuthorityPresenter` is bootstrapped by `GameSceneManager`, refreshes in `LateUpdate` after the authority simulation tick, and clears the route child root whenever the initialized authority or OutsideTest grid is lost. `OutsideTestFloorDebugPanel.FloorSelected` and the player's current inside floor drive `SetActiveFloor`; no authored or cached gameplay state is used in Play Mode.
- `Factory3DRouteSelectionController` is optional and only supports Input System point-action hover/selection plus connected-route highlighting. It must not be extended with 3D placement or simulation commands.
- The focused acceptance filters are `Factory3DRoutePresentationTests` (8 EditMode tests), `Factory3DRoutePresentationPlayModeTests` (1 PlayMode test), and `Factory3DRouteVisualEvidenceTests` (1 PlayMode evidence test). The evidence test writes the five lifecycle artifacts under `TestResults/Factory3DRoute/<run-id>`; each record includes matched stage data, snapshot/hierarchy paths, PNG path, simulation tick, and an isolated save path.

## Runtime 3D Interior Presentation

- `Factory3DInteriorPresenter` reads only an initialized `NAIStateManager` route snapshot and the active floor from `InsideFactoryController`; it does not alter 2D movement, floor transitions, simulation, or persistence. Floor elevation is derived with `BuildingCoordinates.GetFloorElevation` and logical positions are projected through `FactorySpatialAdapter` (default story height 3).
- `GameSceneManager` creates one disposable presenter per loaded interior scene at runtime, so no scene or prefab YAML is required; unloading the interior scene disposes its generated proxy root.
- `Factory3DInteriorProxyAssembler` owns the disposable presenter root and stable `B<building>-F<floor>` / entity keys. It renders the active floor's slab, walls, and route/entity markers, reuses unchanged proxies, removes stale nodes, and leaves non-active floors represented by the authoritative snapshot.
- The focused commands are `unity command run_tests ... Factory3DInteriorPresentationTests` (4 EditMode tests) and `unity command run_tests ... Factory3DInteriorPresentationPlayModeTests` (1 PlayMode lifecycle test); nearest regression filters are `Factory3DOutsideTestProxyTests` (15 EditMode tests) and `Factory3DRoutePresentationTests` (8 EditMode tests). Stateful fixtures use in-memory or isolated temporary state and never target `factory-world.db`.

## Grid-Authoritative 3D Construction

- `FactoryConstructionService` is the reusable preview/commit boundary. Use `PreviewBuilding`, `PreviewEquipment`, `PreviewEntityMove`, `PreviewBuildingRemoval`, or `PreviewEntityRemoval`; commit only a valid preview with `TryCommit`, and call `Cancel` when a controller abandons a preview. Logical cells are authoritative; `SceneGrid.CellSize` affects only the derived world marker.
- `Factory3DConstructionController` is runtime-facing and is auto-created by `GameSceneManager` for the loaded `OutsideTest` scene. Public `BeginBuildingPlacement`, `BeginEquipmentPlacement`, `BeginSelectedBuildingMove`, `BeginSelectedEntityMove`, `TryCommitCurrentPreview`, `TryRemoveSelection`, and `CancelConstruction` APIs are suitable for UI/tooling. It clones the `Player` and `Build` Input System maps, so destroying the controller cannot disable other gameplay controllers.
- Building previews are centered from the full shell footprint while equipment previews use the shell-to-interior inset. The runtime supplies a validated default entrance when no door list is provided. Story elevation is read from `OutsideTestStoryHeight`; do not add a second height or placement scale in a tool.
- `OutsideTest` uses the `SceneGrid` logical bounds as the construction world boundary. A shell preview is invalid when any wall cell leaves that rectangle; isolated service tests may leave bounds unset or provide their own `BoundsInt`.
- Runtime commits flow through `GameSceneManager` → `NAIStateManager` → `FactoryConstructionService`/`FactoryWorldState`; saves remain the unified SQLite database boundary and proxy reconciliation remains derived. For tests, construct in-memory state or use a unique path under `Application.temporaryCachePath`, delete `-wal`/`-shm` sidecars, and assert the path is not `factory-world.db`.
- Focused construction coverage is `FactoryConstructionServiceTests` (14 EditMode cases) plus `Factory3DOutsideTestProxyTests` (15 EditMode cases): orthogonal/dimetric 3D picking, serialized/runtime scene bounds, footprint/default-door/overlap validation, equipment/conveyor bounds and occupancy, elevator alignment, move/cancel and stable identities, confirmed removal, top-story roof derivation, reversible active-floor occlusion, route-root preservation, proxy reconciliation, and committed isolated save/load topology/connection persistence. Deferred milestone items are arbitrary rotation, free camera, and NavMesh.
