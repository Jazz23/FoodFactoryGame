# Development Workflow

## Editor Rules

1. Confirm the connected Editor with `unity status --json --no-banner --project-path E:\Projects\Unity\FoodFactoryGame`.
2. Discover project commands with `unity list --json --no-banner --project-path E:\Projects\Unity\FoodFactoryGame`.
3. Use registered `factory_*` commands for recurring factory workflows.
4. Serialize live mutations, compilation, and test execution. Parallelize independent reads only.

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
- Scene/network tests cover ownership, transitions, sharing, isolation, and disconnect behavior.
- UI tests deliberately verify binding, readiness, and displayed state.
- PlayMode fixtures own temporary database paths, network lifecycle, static/UI reset, stable identities, and cleanup. `FactoryTestWorld` provisions the two-floor building-2 transition fixture in the isolated runtime world instead of depending on authored OutsideTest building IDs.
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
- Select the full database path in the creator's `Selected Save Topology` section; successful building creation, door placement, story changes, and topology updates are saved there immediately.
- `Preview Changes...` remains available for inspecting differences that existed before an edit. `Apply Selected Save...` is for manually resolving those pre-existing differences.
- Immediate writes use the atomic, reload-verified topology service, preserve unrelated save-only buildings for selected create/update operations, and reject stale authored/database fingerprints.
- Authored story removal is labelled `Delete Authored Top Story`; entity state is relocated when possible and the edit fails when it cannot be retained. Authored building removal is labelled `Delete Authored Building` and, after destructive confirmation, purges that building's dependent floors, entities, connections, routes, and trucks from the selected database.
- `Clear Authored Buildings` applies the same confirmed purge to all exterior buildings in the selected database. Inspector selection, repaint, startup migration, undo/redo, and generated-shell refresh are not persistence operations.
- For an isolated database copy, use `FactoryWorldSqliteStore.CreateConsistentBackup` or SQLite `VACUUM INTO`; do not copy only the main file while WAL data may be active.
- The Unity Editor default database is `<project-root>/factory-world.db`; production uses `Application.persistentDataPath/factory-world.db`. Keep the project-local database out of source control.
- `factory_reconcile_save` is position-only reconciliation. Run it with an explicit isolated path and dry-run first; it does not apply authored dimensions.

## Presentation-Scale Verification

- `InteriorSizingTests` covers logical-to-world scaling plus generated interior floor-cell and doorway proportions.
- The `outside_floor` profile covers isolated-save transition, movement, interaction, and equipment tests across `OutsideTest` and `insidefactory0`.
- `InteractEntersBuildingFromExteriorArrival` exercises the configured E binding after physics settles at the exterior arrival, enters and exits through portal selection/RPCs, and verifies the return position remains usable. Direct scene-manager transition calls do not cover interaction reachability. Captures are `Temp/player-ground-interior.png` and `Temp/player-ground-exterior.png`.
- The `truck_route` profile covers the isolated-save equipment, dock, transition, reload, and delivery route smoke test.
