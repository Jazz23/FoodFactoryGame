# Architecture Map

## Runtime Ownership

- `GameSceneManager` adapts FishNet scene callbacks and portal/elevator requests to the transition service.
- `GameSceneManager` resets OutsideTest load and reconciliation state at each server start/stop so disabled Enter Play Mode reload options cannot retain stale scene topology.
- `FloorTransitionCoordinator` owns transition phases, sequence validation, pending floor loads, loaded-floor reuse, return-floor state, and unload guards.
- `NAIStateManager` owns the authoritative factory snapshot, simulation, and runtime persistence coordination.
- `FactoryWorldState` owns building, floor, entity, connection, route, and truck records.
- `FactoryBuildingEditService` owns validated topology edits, deterministic equipment relocation, and endpoint rebinding.
- `FactoryBuildingEditService.TryCreateTopologyPlan` compares compact authored records with one selected schema-9 save; plans contain `Create`, `Update`, or destructive `Delete` operations plus authored/database fingerprints. `TryApplyTopologyPlan` applies the exact confirmed plan or rejects it as stale.
- `FactoryBuildingTopologyResolver` owns authored-versus-persisted source selection and conflict comparison; saved topology remains authoritative once loaded.
- `FactoryBuildingLayoutAsset` stores compact authored building topology (identity, footprint, stories, and doors); generated shell children remain rebuildable output.
- `TestBuildingCreator` and `GameSceneManager` consume the compact layout when it is assigned; `BuildingShellAssembler` is the deterministic geometry adapter and reports rebuild success separately from whether geometry changed.
- `FactoryWorldSqliteStore` is the persistence adapter. Database roles are application save, authoring data, test fixture, and temporary preview.
- `TestBuildingCreatorEditor` mutates the compact asset first and reconciles generated preview shells by stable building ID. Successful topology edits immediately apply the changed building to an explicit full-path database target through the fingerprinted, atomic topology service; immediate edits use the direct database path and avoid creating a duplicate preview plan. Preview, inspector selection, repaint, undo/redo, and generated-shell refresh remain read-only; whole-building deletion and clear operations require destructive confirmation and are blocked when the target is active in Play Mode.
- The default `factory-world.db` is project-local while running in the Unity Editor and uses `Application.persistentDataPath` in production. The project-local database is development state and is not source-controlled.
- `FactoryWorldSqliteStore.Inspect`, `PlanMigration`, `ApplyMigration`, `Read`, and `Save` keep schema inspection, migration, loading, and writing explicit.
- `OutsideTestFloorPresentation` and related views render explicit building/floor state; they are not authoritative state owners.
- `OutsideTestFloorDebugPanel` is a UI client of the current player/floor binding and should not be used as the primary API for domain tests.
- `Virtual3DSize` uses the player's transform origin as the stable bottom-pivot foot anchor. A shallow horizontal capsule extends upward from that anchor with independent world-space width and ground depth (player prefab: 0.6 × 0.3); animation bounds never move the collider. Transitions, interior containment, and depth sorting consume that same anchor.
- `SceneGrid.CellSize` scales world-space presentation only. `IndoorGrid`, `InsideFactoryVisuals`, and factory entity views continue to use saved logical cell coordinates and configured building sizes.

## Canonical Assets

- Runtime bootstrap: `Assets/Scenes/Bootstrap.unity`
- Gameplay authoring scene: `Assets/Scenes/OutsideTest.unity`
- Shared interior template: `Assets/Scenes/insidefactory0.unity`
- Authoring and persistence commands: `Assets/Editor/FactoryPipelineCommands.cs`

## Identity Rules

- Building and floor identities are stable gameplay data, not scene-object instance IDs.
- Large Unity identifiers in command output must be serialized as strings.
- Shell buildings include wall cells; usable interior size is `max(footprint - (2, 2), zero)`.
- Interior-only buildings retain their configured dimensions.
- Reconciliation and test teardown must preserve recoverable equipment and must not silently delete state.

## Change Boundaries

- Keep authored building intent compact and reproducible; do not hand-edit generated scene YAML when live Editor commands can apply the change.
- Extract bounded application services before introducing new assembly boundaries.
- Keep database inspection, migration planning, migration application, and saving as distinct operations.
- Saved topology has runtime precedence over authored shell dimensions. Authoring deletion removes the authored record and generated shell, then removes that building and its dependent state from the selected database after destructive confirmation; other saves retain their buildings. Creator IDs use a persisted high-water mark and are not recycled after deletion.
