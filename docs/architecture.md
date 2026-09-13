# Architecture Map

## Runtime Ownership

- `GameSceneManager` adapts FishNet scene callbacks and portal/elevator requests to the transition service.
- `FloorTransitionCoordinator` owns transition phases, sequence validation, pending floor loads, loaded-floor reuse, return-floor state, and unload guards.
- `NAIStateManager` owns the authoritative factory snapshot, simulation, and runtime persistence coordination.
- `FactoryWorldState` owns building, floor, entity, connection, route, and truck records.
- `FactoryBuildingEditService` owns validated topology edits, deterministic equipment relocation, and endpoint rebinding.
- `FactoryBuildingLayoutAsset` stores compact authored building topology (identity, footprint, stories, and doors); generated shell children remain rebuildable output.
- `TestBuildingCreator` and `GameSceneManager` consume the compact layout when it is assigned; `BuildingShellAssembler` is the deterministic geometry adapter.
- `FactoryWorldSqliteStore` is the persistence adapter. Database roles are application save, authoring data, test fixture, and temporary preview.
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
