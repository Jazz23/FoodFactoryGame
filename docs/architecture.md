# Architecture and Implementation Status

## Authority

Gameplay requirements and open product decisions are in [the GDD](../Food_Factory_Restaurant_GDD.md). Resource facts are in [dev_resources.md](../dev_resources.md). This document distinguishes implementation from intended architecture.

## Implemented Baseline

- Unity `6000.5.9f1`, URP `17.5.0`, and Input System `1.20.0`.
- `Assets/Scenes/SampleScene.unity` is the enabled starter build scene. It is not a restaurant prototype.
- `Assets/InputSystem_Actions.inputactions` is imported starter input authoring, not completed gameplay controls.
- FishNet `4.7.3` is vendored under `Assets/FishNet`, including its original metadata, demo references, and license files. See [decision 0001](decisions/0001-reproducible-baseline.md).
- `Assets/DefaultPrefabObjects.asset` currently references FishNet demo prefabs. It is not the project's final network spawn catalog.
- `Assets/Tests/EditMode` contains four authoring/dependency checks in `FoodFactoryGame.Baseline.EditModeTests`. Tests open build scenes as isolated preview scenes and do not touch application saves.
- No project gameplay simulation, network session flow, inventory system, persistence contract, or remote-site system is established by this baseline.

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

## Required Constraints for Future Implementation

- The server owns gameplay state; clients request validated actions through the command contract in decision 0002.
- Site operations continue independently of client cameras, interest, or presentation scene loading while the world simulation runs.
- Persistent identities, inventory transfers, payments, and job reservations must survive failure/cancellation without silent loss or duplication.
- Player and employee operational rules should be shared; input and AI choose actions through those rules.
- Visual objects must not become the sole owners of authoritative simulation state.

These are accepted contracts. No runtime implementation or final public
interface is established yet.

## Planned / Undecided

- Domain assembly boundaries, simulation scheduling, command interfaces, replication interest, and persistence schema: defined in decision 0002; implementation pending.
- Player count, hosting/disconnect behavior, and exact performance hardware: GDD decisions pending.
- Physical goods model: selected in GDD section 28 and decision 0003; runtime
  inventory, transport, spoilage, projection, and recovery remain unimplemented.
- Offline progression, host migration, discovery/join flow, and the shipped hosting model remain undecided.
- SQLite and MoonSharp are existing declared dependencies, with their gameplay roles undecided. Neither is selected merely by being installed.
- Multiplayer smoke tests and representative scale benchmarks follow implementation; current tests do not establish replication correctness or the 60 FPS target.

## Baseline Test Evolution

The starter tests intentionally check the current scene/input/catalog authoring. When real bootstrap/additive scenes or a game-specific prefab catalog replace it, update the tests to validate the new accepted contract. Do not put cameras into intentionally camera-free scenes or restore demo prefabs simply to retain these starter assumptions.
