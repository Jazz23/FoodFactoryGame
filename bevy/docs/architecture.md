# Bevy port architecture

## Scope and authority

The port targets the **implemented Unity game**, using `../../Food_Factory_Restaurant_GDD.md`, accepted decisions in `../../docs/decisions/`, and the actual Unity source as references. Undecided and proposed GDD features are not silently selected by this port. Unity source, project settings, packages, scenes, and original art remain intact. All port implementation and converted assets live under `bevy/`.

## Required contracts

- The server owns world state. Presentation queues commands and displays accepted baselines; UI, camera position and visibility cannot create or stop simulation.
- Stable identities, reservations, goods, payments and jobs survive restart. Commands need replay protection and durable acknowledgment; a failed commit must leave the previously committed state usable.
- SQLite is the sole persisted-data store. Converted art and authored content are assets, not saved gameplay. Tests and captures use explicit isolated database paths.
- A new Rust snapshot format must not be assumed compatible with Unity's v24 snapshots. Never point the port at an existing Unity application save without a separately verified, dry-run migration.
- Device inputs are mapped into named actions before gameplay reads them.
- Models are handled through Blender CLI; generated models must be loadable assets, not Git LFS pointer files.

## Presentation

The existing character animation and orbit rig are retained. `actions.rs` supplies named actions, `camera.rs` adds a blended grid-aligned overhead mode, and `player.rs` supports server-baseline-derived solid collision boxes. `ui.rs` projects a view model into native Bevy widgets and emits string intents; it has no authority to mutate inventory or company cash.

## Acceptance

A compile is not functional parity. Domain tests must exercise state and failure invariants; network evidence must use independent peers; visual acceptance requires running-game captures and independent review. Implementation and verification gaps must remain explicit in the port's README and verification records.
