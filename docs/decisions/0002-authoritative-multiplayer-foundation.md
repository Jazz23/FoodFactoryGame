# 0002 - Authoritative Multiplayer Simulation Foundation

Date: 2026-09-21

Status: accepted as the starting technical design; runtime implementation pending.

## Context

The GDD requires multiplayer, operation across multiple sites, and continued
operation of distant sites while the world simulation is running. The current
repository baseline contains FishNet `4.7.3`, but it does not contain a
gameplay simulation, network session flow, inventory system, or persistence
implementation. Feature work needs stable boundaries before those systems are
built independently.

This decision establishes technical contracts without deciding product
questions that remain open in GDD section 28, such as maximum players, shipped
hosting, host-disconnect behavior, and the exact representation of physical
goods.

## Decision

### 1. Authority and hosting boundary

- The server is authoritative for all gameplay state and outcomes. It owns
  inventories, goods, transfers, payments, purchases, jobs, reservations,
  production, customers, workers, vehicles, routes, sites, and the simulation
  clock.
- A client owns input intent, camera state, presentation state, and local UI
  state only. A client requests an action; it never submits an authoritative
  result, quantity, location, payment, or timestamp for the server to trust.
- The initial development path is a listen server. The host process runs the
  server and may also run a local client, but the local client crosses the same
  request and replication boundary as a remote client.
- The simulation must be headless-compatible: its domain and scheduling code
  cannot require a camera, a loaded presentation scene, or a local player.
  Dedicated hosting is not required by this decision, but a later dedicated
  server must be able to run the same simulation boundary.
- The first network integration targets the vendored FishNet `4.7.3`
  installation. This avoids introducing a parallel networking stack; the
  existing FishNet demo prefab catalog is not thereby approved as the final
  game catalog. Discovery, connectivity services, and the shipped hosting
  model remain separate decisions.

The server continues simulating a site when no client is subscribed to or
presenting that site, as long as the authoritative server process is running.
This does not select offline progression, host migration, or behavior after a
listen-server host disconnects.

### 2. Domain identity and state ownership

- Gameplay entities use stable domain identifiers that survive network
  reconnects, scene changes, and persistence recovery. Network object IDs,
  GameObject references, and client-local instance IDs are transport or
  presentation details and are never the durable identity of a site, good,
  worker, vehicle, customer, job, reservation, or payment.
- The authoritative domain model is the source of truth. Unity scene objects
  and network objects project or transport that state; destroying a visual
  object must not destroy the corresponding gameplay entity.
- Shared definitions such as recipes and item definitions are versioned,
  read-only content. Runtime ownership, quantities, condition, location,
  reservations, and economic state remain server-owned records.
- Goods are represented first as logical inventory and transport state, with
  visual objects created selectively. Exact batching, interaction, and
  physical representation rules remain the GDD proposal and must not be
  silently locked by an implementation.

### 3. Command and validation contract

Every client-originated state change follows this contract:

1. The connection identity supplies the requesting player identity. The
   payload contains intent, stable target IDs, bounded values, and a unique
   request ID; client-reported state and time are informational only.
2. The server validates permission for the player and operation, existence
   and current ownership of every target, current state preconditions,
   quantities and capacities, available funds, and any relevant reservation or
   route constraints.
3. The server processes accepted commands in the authoritative simulation
   order. The domain mutation and its related reservation/payment/inventory
   changes are atomic from the domain model's perspective.
4. An accepted command produces an authoritative result and state changes. A
   rejected command produces a reason and no gameplay mutation.
5. Retrying the same request ID returns the original outcome instead of
   applying the mutation again. Terminal outcomes for inventory, payment, job,
   and reservation operations must be included in the persistence/recovery
   boundary needed to preserve this invariant.

Inventory transfers, purchases and payments, production starts/completions,
job claims, and remote management actions all use this contract. Server-side
AI and automation use the same domain rules rather than bypassing validation.
Reservations and in-flight operations have explicit state transitions; failed
or cancelled work releases or compensates state explicitly and cannot silently
delete goods/equipment or duplicate inventory/payments.

### 4. Simulation clock and scheduling

- The server owns one explicit simulation clock for the world. It advances
  only while the authoritative server process is running and is persisted and
  restored as domain state. A client frame clock cannot advance gameplay.
- A simulation coordinator processes accepted commands and due work in a
  stable order. Due work is ordered by simulation time and a server-assigned
  sequence, not by client arrival time or rendered frame order.
- Systems declare their own update policy instead of sharing one global
  frequency:
  - vehicle movement and other continuous movement use a fixed simulation
    step;
  - production and job progress use scheduled due events;
  - wages use scheduled payroll events;
  - spoilage is evaluated from authoritative elapsed time and due thresholds,
    processing state transitions when they become due rather than once per
    rendered frame;
  - each customer makes an individual choice at meaningful events or bounded
    decision intervals, never as a requirement of every rendered frame.
- All sites use the same simulation model in the first implementation. A site
  being distant or unsubscribed changes delivery of presentation state, not
  the gameplay rules or progression model. Approximate distant-site
  simulation is deferred until profiling demonstrates a need.
- Exact step sizes, calendar periods, and catch-up limits are subsystem
  configuration decisions. They must be declared by the owning subsystem and
  tested against the shared clock; no feature may introduce an unrelated
  second gameplay clock.

### 5. Visibility and replication

- The server computes an interest set per connection and sends authoritative
  snapshots/deltas only for state the connection is allowed to observe. A
  client interpolates and presents received state; interpolation never feeds
  back into simulation.
- A connection receives its player/control state, command results, and the
  minimum authorized world/site summaries needed by the current UI. Physical
  presentation interest may add nearby detail, but it is not a prerequisite
  for a site to simulate.
- Remote management uses an explicit, server-validated subscription request
  for a site or management view. Subscriptions are connection-scoped
  visibility state, not ownership of the site and not durable gameplay state.
  A valid remote subscription can receive management data even when the site
  has no loaded presentation scene on that client.
- Initial subscription delivery establishes a full baseline with an
  authoritative revision. Subsequent deltas carry revisions; a gap or stale
  revision requires a fresh baseline rather than client-side invention or
  silent merging.
- Unauthorized entities and management views are not replicated. Absence of a
  replicated visual object never means that the server may stop, delete, or
  approximate the underlying gameplay state.

### 6. Persistence and recovery

- Persistence is server-owned. Clients do not write authoritative saves.
- A snapshot has a schema version, stable world/save identity, authoritative
  simulation time, snapshot sequence, and all domain records required to
  resume without changing ownership or duplicating state. This includes
  inventory and goods condition/location, vehicles and routes, jobs and
  reservations, production, customers, workers, sites, economy, and
  in-flight state transitions. Network IDs, client cameras, presentation
  objects, and connection-scoped subscriptions are excluded.
- State-changing operations have an explicit domain transaction boundary.
  Snapshot writes are atomic: a partial or invalid write must not replace the
  last valid snapshot. The persistence backend and snapshot cadence are still
  implementation choices; the declared SQLite dependency is not selected by
  this decision.
- On recovery, the server loads the latest valid snapshot, runs explicit
  schema migrations when needed, and only then accepts gameplay commands.
  Stable IDs are preserved. An unknown newer schema fails clearly rather than
  being destructively rewritten.
- Pending reservations and operations are recovered through their explicit
  state machines. A failed/cancelled operation is reconciled through an
  explicit release, refund, or compensating transition; recovery must not
  silently delete goods/equipment or apply a payment/inventory mutation twice.
- Save migration and reconciliation are dry-run capable and use an isolated
  save/database path in tests. Application saves are never used as test
  fixtures.

## Consequences

- Gameplay systems can be tested without a loaded scene or connected client,
  and the listen-server path does not create a second local-player authority
  model.
- Remote management and local presentation can evolve independently from
  world progression, at the cost of explicit subscription and resynchronizing
  snapshot/delta logic.
- Domain identifiers, request idempotency, operation state machines, and
  versioned snapshots add up-front design work but protect the economy and
  logistics state from retries, disconnects, and recovery.
- The server and client will need separate performance measurements. A 60 FPS
  render target does not determine a server step size or a customer/spoilage
  schedule.
- This is an architectural contract, not evidence that networking, gameplay,
  persistence, or scale targets are implemented. Feature work must add tests
  and runtime evidence before marking any contract complete.

## Deferred decisions

This record intentionally does not decide:

- maximum concurrent players and multiplayer ownership/cooperation rules;
- shipped dedicated hosting, listen-server shipping, host migration, or what
  happens after the host disconnects;
- offline progression while no authoritative server process is running;
- discovery, join flow, connectivity service, or target platform constraints;
- exact physical-goods batching and visual interaction rules;
- exact subsystem frequencies, benchmark hardware, or the final scale budget;
- the persistence backend and operational snapshot cadence.

Those decisions remain in the GDD or are owned by later technical records.

## Implementation acceptance evidence

The foundation is not complete until verification demonstrates, with isolated
artifacts:

- domain tests for command validation, atomic rejection, request idempotency,
  reservations, stable IDs, and operation recovery;
- a headless-compatible simulation test that advances a site with no camera,
  client, or presentation scene;
- a listen-server smoke test with a local and remote client, including an
  unauthorized command rejection and an explicit remote management
  subscription;
- proof that an unsubscribed/distant site progresses under the same model;
- snapshot save/load and migration tests using an isolated path, including
  failed/cancelled inventory and payment operations;
- a measured server/client scenario against the GDD scale targets once target
  hardware and population interpretation are approved.
