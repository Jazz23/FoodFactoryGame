# First recipe-driven production line verification

## Implementation

- Added scene-independent FactoryEntityDefinitions capabilities and item IDs.
- Added test-processor: 2 test-product input items become 1 packed-product after 1 second.
- Persisted processor input count through entity records, snapshots, cloning, save/load, and floor snapshots.
- Added packed storage, capability/item-compatible connection validation, one-item-per-connection transport, and directional disconnect controls.
- Advanced all entity production before transport each simulation tick.
- Kept BuildingCoordinates, logical positions, SceneGrid, scene instances, physics, and rendering responsibilities unchanged.
- Save version is 7. Versions 1-6 remain loadable; pre-v7 input state migrates to zero.

## Verification

- Unity Editor tests: 147 passed, 0 failed.
- FactoryRecipeSimulationTests: 6 passed, 0 failed.
- OutsideTestFloorStateOwnerTests: 34 passed, 0 failed.
- PlayMode MachineRequestsPersistAcrossTravelAndRestart: passed.

The existing inter-floor PlayMode checks remain baseline failures unrelated to this change:

- InterFloorTransferPersistsWhenInteriorsUnloadAndRestart: fails before storage creation because CanEditCurrentFloorEntities is false at the existing test point.
- TwoFloorSceneLifecycleKeepsRecordsProducing: existing transient label mismatch (Transient test edit vs. Building 2 Ground Proof).

No save migration was performed on project data, no save version was changed beyond the required v7 schema, and no interior scene coordinates or travel endpoints were repositioned.
