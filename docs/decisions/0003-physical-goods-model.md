# 0003 - Location-Based Physical Goods

Date: 2026-09-22

Status: accepted product and technical contract; implementation pending.

## Context

The GDD requires physical, location-specific inventory, hands-on work, constrained
loading and transport, and spoilage. It also targets thousands of goods across
multiple sites. Decision 0002 established server-owned logical goods with
selective presentation but deferred the exact physical goods rules. The project
owner selected the revised model in GDD section 28.

## Decision

- Goods are server-owned lots with stable domain IDs. Each lot has an item type,
  quantity, owner, binary condition, retained spoilage history, and exactly one
  location. A location identifies a storage space, machine buffer, character
  carrier, vehicle cargo space, transport stage, or placed-world position.
- Lots can be split for work and transport. A split preserves the source goods'
  spoilage history and other relevant state; merge only equivalent goods. A
  container's displayed count may aggregate lots without merging their state.
- Every move follows the authoritative command and operation contract in
  decision 0002: validate permission, source quantity, reservations, destination
  capacity, and route or handling constraints. A committed transition removes
  goods from the source and places them at exactly one destination or explicit
  in-transit location. Failure, cancellation, and recovery must not lose or
  duplicate them.
- Spoilage is evaluated using authoritative simulation time and each lot's
  exposure to its storage or transport conditions. Refrigeration changes future
  exposure; moving or stacking goods does not erase prior exposure. Food remains
  edible or spoiled, with no graded quality score.
- Local GameObjects, network objects, and rigidbodies are optional projections.
  Visible packages may represent multiple units but must correspond to the
  authoritative goods available for interaction. Presentation loading, hiding,
  or destruction cannot mutate the goods. Remote sites use the same simulation
  rules without requiring a client to load those visuals.

This selects location-based lots with selective visuals over both an always
networked object per unit and a location-free inventory count. It does not
prescribe a persistence backend, package size, art style, interaction range,
or special-case physics behavior.

## Consequences and verification

The server must persist stable lot IDs, locations, quantities, spoilage state,
reservations, and in-flight moves. Clients receive authorized views and send
action requests using stable IDs; network object IDs are never durable goods IDs.

Implementation evidence must include domain tests for split/merge, spoilage
across refrigeration and transport, capacity rejection, cancellation and retry
without loss or duplication, and snapshot recovery. PlayMode and multiplayer
checks must show pickup/loading and visual projection matching server state,
including when a site has no observing client. Scale performance remains
unverified until representative goods counts and hardware are established.
