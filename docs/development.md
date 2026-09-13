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
- PlayMode fixtures own temporary database paths, network lifecycle, static/UI reset, stable identities, and cleanup.
- Tests must not resize gameplay buildings or modify the application database.

## Transition Verification

- `FloorTransitionCoordinatorTests` verifies phase sequencing, stale-sequence rejection, loaded-floor reuse, failed-load cleanup, and building return state without a live network connection.
- Scene/network transition tests must additionally verify player arrival, scene unloading, disconnect cleanup, and client transition-state reset.

## Compact Layout Workflow

- `factory_export_building_layout --dry_run true` previews an OutsideTest layout export.
- `factory_export_building_layout --confirm true --assign true` writes `Assets/Authoring/OutsideTestBuildingLayout.asset` and assigns it to `TestBuildingCreator`.
- `factory_validate_building_layout` checks the asset schema, topology, and generated-layout match.
- `factory_rebuild_outside_shells --dry_run true` verifies deterministic shell output without changing the scene.

## Presentation-Scale Verification

- `InteriorSizingTests` covers logical-to-world scaling plus generated interior floor-cell and doorway proportions.
- The `outside_floor` profile covers isolated-save transition, movement, interaction, and equipment tests across `OutsideTest` and `insidefactory0`.
- `InteractEntersBuildingFromExteriorArrival` exercises the configured E binding after physics settles at the exterior arrival, enters and exits through portal selection/RPCs, and verifies the return position remains usable. Direct scene-manager transition calls do not cover interaction reachability. Captures are `Temp/player-ground-interior.png` and `Temp/player-ground-exterior.png`.
- The `truck_route` profile covers the isolated-save equipment, dock, transition, reload, and delivery route smoke test.
