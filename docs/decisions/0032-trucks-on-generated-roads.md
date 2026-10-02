# 0032 - Trucks on Generated Roads Between Sites

Date: 2026-09-30 (plan), 2026-10-02 (implemented)

Status: **accepted and implemented** 2026-10-02 (owner: "Complete all pieces"), with every owner question answered (see
[Owner decisions needed](#owner-decisions-needed)). The plan below is kept as written; what was built, and where it differs
from the plan, is under [Implementation](#implementation-2026-10-02). Every number is PROTOTYPE. See the
[architecture status](../architecture.md#implemented-trucks-on-generated-roads-and-city-traffic-2026-10-02) and the
[verification record](../verification/trucks-roads-20261002.md).

## Context

Confirmed (GDD section 9): trucks do point-to-point delivery over public roads the player never builds or modifies; travel
is "affected by road distance and congestion"; routes are configured manually and repeated; loading capacity and
perishability make route time matter. GDD section 26: roads are generated with the world and fixed.

Implemented today:

- Trucks, docks, routes and fleet ([0022](0022-trucks.md), [0023](0023-truck-routes-and-fleet.md)): a trip takes
  `ceil(Manhattan(MapX/Z) / speed)` seconds (`GoodsWorld.RoadMetres`/`RoadSeconds`), the truck has no position between
  sites, a redirected or parked truck restarts from the site it last left, and a driving truck has no scene presence
  (`TruckPresenter` only shows trucks standing at a local dock).
- Generated worlds ([0026](0026-procedural-world-layout.md), [0027](0027-world-generator-v2-land-river-roads.md),
  [0028](0028-sites-for-generated-buildings.md)): an immutable, replicated road graph (`RoadNode` with `JunctionControl`,
  axis-aligned `RoadSegment` with `RoadKind`, `Width`, `CapacityPerHour`), bridges, terrain height, and a `WorldLot` per
  purchasable building whose `Access` cell is "the street cell trucks drive to". A bought site's `MapX/Z` is that access
  point (0028 constraint 1). `FoodFactoryGame.Goods` already references `FoodFactoryGame.World`.
- Several owned sites drawn in place and walking between them ([0031](0031-several-sites-drawn-at-once.md)).

So the road network the trucks should use already exists as data; the truck simulation simply ignores it.

## Goal

Trucks in generated worlds travel along the generated roads between their sites' access points: trip time comes from the
real route, a driving truck has a position on the road that survives saves and redirects, and players see trucks driving
on the streets and pulling up to their docks. Dev worlds (no layout) keep today's Manhattan behaviour unchanged.

## Proposed plan

Four pieces, each separately shippable and verified. Piece 1 is the foundation; 2 needs 1; 3 needs 2; 4 is optional
depth that needs an owner decision.

### Piece 0 - Baseline (before touching the truck sim)

- Fix the known flaky `TruckTests.StepSizeDoesNotChangeTheOutcome` (split lots get random GUID IDs used as an ordering
  tie-break). Make split-lot IDs deterministic for ordering, or break ties on a stable key. Every later piece depends on
  step-size independence tests being trustworthy.
- Record a green baseline of the Goods, Session and World EditMode assemblies and the PlayMode suites.

### Piece 1 - Road routing in the domain (sim only, no visuals)

- **Road network as registered content.** `RoadNetwork` (World assembly, no Unity references) built once from the stored
  layout: nodes, segments with length in metres, a per-segment travel cost, and adjacency. Registered with the goods world
  the same way property offers are (`RegisterRoadNetwork`, `SessionRoot.StartServer`, only when a layout exists); never
  saved, since the layout is write-once and hashed.
- **Entry points.** A site's road position is its lot's `Access` cell projected onto the segment that cell lies on
  (distance along the segment from `FromId`). Sites without a lot (dev sites) have no road position.
- **Shortest route.** Deterministic Dijkstra over the segment graph from the origin's entry point to the destination's,
  ties broken by node/segment ID. Cost = travel time, not length (see speeds below). The result is a `RoadPath`: an
  ordered list of segment IDs with direction, plus entry/exit offsets. Cached in memory per (from, to) pair; ~350 segments
  makes a query cheap, and the cache is derived data that can be dropped at any time.
- **Travel time.** PROTOTYPE speed by `RoadKind` (for example Arterial 15 m/s, Local 10 m/s, Rural 20 m/s), capped by the
  truck's own `SpeedMetresPerSecond`, plus a fixed delay at junctions where `WorldJunctions.Stops` says the approach stops
  (stop sign ~3 s, traffic light ~8 s average). These are deterministic, so trip time is still known at departure and the
  skip-ahead in `MoveTrucks` is unchanged.
- **Fallback.** `RoadSeconds` uses the road path when both sites have road positions, otherwise today's Manhattan rule.
  `no-road` now also means "no connected path" (the validator already guarantees every lot's access road is connected, so
  this should not occur in valid worlds; it is a guard).
- **Tests (Goods/World EditMode):** path determinism and ID tie-breaks; road time ≥ straight Manhattan time on a pinned
  seed; a river crossing uses a bridge segment; dev worlds unchanged; the existing step-size independence holds with road
  times.

### Piece 2 - A truck's position on the road (sim + schema)

- **Trip record.** A driving truck stores where its trip started instead of only the site it left: `TripStart` (a road
  position: segment ID + offset, or a site), `TripSeconds` (total), and the existing `DestinationSiteId` and
  `RemainingSeconds`. Its current position is a pure function of (layout, path from `TripStart` to the destination,
  `TripSeconds - RemainingSeconds`), so nothing per-second needs saving and replay stays exact.
- **Redirect and park mid-trip** (replaces the 0022/0023 PROTOTYPE "restart from the site it left"): the truck's current
  road position becomes the new `TripStart`. Proposal: it continues to the next node in its direction of travel, then
  routes from there (no U-turn mid-segment). Parking a driving truck finishes the trip to the nearest of the two route
  sites (0023 deferred this) — or parks at its current origin site as now; see owner questions.
- **Schema.** Goods snapshot **v16** adds the trip fields, in the per-segment form 4a needs (see "Trip record change"). A v15 driving truck upgrades with `TripStart` = its `SiteId`
  and `TripSeconds` = its `RemainingSeconds` (it simply appears to have just left; no goods change). Cargo stays on the
  reserved `road` site exactly as today: only the truck's position becomes richer, not where its goods live
  (0028 constraint 4 and 0003 are unchanged).
- **Tests:** save/load mid-trip round-trips the position; redirect mid-trip conserves cargo and is step-size independent;
  v15 upgrade; validation rejects a trip start that is not on the network.

### Piece 3 - Visible trucks driving between drawn sites (presentation)

- **Deterministic client-side position.** The truck view in each baseline already carries the company's trucks. Add the
  trip fields and the server clock second of the baseline; the client computes the same `RoadPath` from the replicated
  layout (same World-assembly code) and advances the position locally between baselines. Baselines are only sent on
  change, so no per-frame replication is added.
- **`TruckPresenter` → road trucks.** Draw every company truck whose road position is near any drawn area (0031's 300 m
  radius, PROTOTYPE), following the segment centreline offset to the right-hand lane, heights from `WorldTerrain` and
  bridges, smoothed turns at nodes, stopping briefly at stop/light junctions (matching the sim delay). Visuals are keyed by
  truck ID and never own state, as now.
- **Docking leg.** From the access cell, the visual pulls onto the apron and reverses to stand behind the dock it serves
  (today's placeholder pose). This leg is cosmetic and inside the loading time; the apron is not pathfound in the sim.
  Proposal: a dock placement warning (not a refusal) when the dock's back is not open to the apron's street edge.
- **Model.** Replace the placeholder truck prefab with a Blender low-poly truck in the world art style
  (`ArtSource/World`, same pipeline as `WorldArt.fbx`).
- **No collision with players** in this piece (trucks have no collider today); trucks overlapping each other on one lane
  are offset cosmetically.
- **Tests and evidence:** PlayMode test that a truck's drawn position matches the sim's position within tolerance at
  several times along a trip between two drawn sites; loopback multiplayer check that a teammate sees the same truck at
  the same place; running-game capture of a truck driving, turning, crossing the bridge and docking; frame-time check with
  ~20 trucks drawn (compare with 0031's 9.9 ms).

### Piece 4 - Congestion with city traffic (option C, owner 2026-10-02)

Options considered (smallest first): A, static traffic (district and time-of-day multipliers, trucks do not interact);
B, player trucks congest each other through segment capacity; C, B plus ambient city cars. **The owner selected C**, with
the vehicle models made in Blender. Everything below is the implementer's proposal for how to build C.

**Principle: traffic is a server flow model, cars are deterministic presentation.** Simulating hundreds of individual
cars authoritatively would put them in the snapshot and the 10 s commit (0016, 0025). Instead the server owns an aggregate
flow per segment, which decides truck times, and every client draws cars that are a deterministic function of that flow, so
all players see the same cars without replicating them. No car owns gameplay state (architecture constraint: visual objects
never own authoritative state).

#### 4a - Segment flow and truck congestion (domain)

- **Background flow.** Each segment's city traffic in vehicles per hour = `CapacityPerHour` x a base load by `RoadKind` x the
  district's `TrafficPercent` (the district containing the segment's midpoint; rural segments use a rural default) x a
  time-of-day curve (morning, lunch and dinner peaks, PROTOTYPE). This is a pure function of layout and clock, so it needs no
  saving. Prerequisite: a world time of day derived from `ClockSeconds` (shared with the customer schedule of 0024 if it has
  one; otherwise added here with a PROTOTYPE day length).
- **Player trucks add load.** Each truck currently on a segment adds a PCE (passenger-car equivalent, PROTOTYPE 2.5) to that
  segment's flow, counted per direction.
- **Segment time.** Free-flow time (length / min(road speed, truck speed)) x a standard volume-delay curve,
  `1 + 0.15 (v / c)^4` (the BPR function, PROTOTYPE constants), plus the junction delay at its end; the light/stop delay also
  grows with the cross street's load. Fixed when the truck enters the segment, so a truck's whole trip is not known at
  departure but each segment is.
- **Hard queue (B's rule).** Above a PROTOTYPE `v / c` (for example 1.2) a segment admits no more trucks until one leaves;
  arrivals wait at its entrance in truck-ID order. This is what makes a jam visible and routable.
- **Routing.** Paths are chosen at departure and at each junction using current segment times (deterministic, ID
  tie-breaks), so trucks route around a jam they can see. Route-time caching from piece 1 becomes per time-of-day bucket.
- **Simulation step.** `MoveTrucks` becomes event-driven: the next event is the earliest of a segment exit, a load/unload
  second, or a time-of-day bucket boundary. Step-size independence (one N-second step equals N one-second steps) remains a
  required test.
- **Trip record change (amends piece 2).** Because segment times vary, a driving truck stores its current segment, direction,
  the second it entered and that segment's fixed duration, plus the remaining path (or recomputes it), instead of only
  `TripStart` and `TripSeconds`. Position on the segment is still a pure function of those fields and the clock. Schema v16
  is designed for this from the start, so pieces 2 and 4 do not need two migrations; pieces 2-3 simply leave every segment at
  free-flow time until 4a lands.
- **Replication.** A truck's view changes at each segment entry, more often than today's arrival-only changes. Proposal: a
  small truck-movement message (truck ID, segment, entered-at, duration) instead of a full baseline; to be measured.
- **Tests:** flow is deterministic for (layout, clock); BPR time increases with load; the queue admits and releases in ID
  order; trucks reroute around a saturated segment; step-size independence with congestion; save/load mid-segment and in a
  queue loses and duplicates nothing; benchmark of `MoveTrucks` with 100 trucks (GDD scale table) on seed 20260927.

#### 4b - Visible city cars (presentation)

- **Deterministic cars.** For each drawn segment and direction, cars are generated from a hash of (world seed, segment ID,
  direction, time slot), with spacing set by the server flow for that slot. A car's position is a function of the clock, so
  every client shows the same cars in the same places, and nothing is replicated beyond the clock and truck movements.
- **Behaviour.** Right-hand lanes; stop at stop signs and red lights (light phases also a deterministic function of node ID
  and clock); queue behind each other and behind player trucks; turn at junctions by a hashed choice. When the server says a
  segment is queued, the drawn cars bunch to match. Cars are cosmetic: they never block the sim, players or customers, and
  no collider (PROTOTYPE).
- **Scope.** Only near drawn areas and the local camera (0031's radius), capped at a PROTOTYPE 150 cars, pooled, with simple
  LOD. Rural and distant segments are not drawn.
- **Tests and evidence:** EditMode tests that car placement is identical for the same inputs and respects spacing; a loopback
  check that two clients show the same cars; running-game captures of a lunch-rush jam downtown, a quiet night street and a
  truck rerouting; frame time with the cap reached, compared with 0031's numbers.

#### 4c - Vehicle models (Blender)

- New `ArtSource/Vehicles/build_vehicle_models.py` in the world art style (`ArtSource/World/README.md`: low-poly, detail in
  procedural textures, shared palette), exported to `Assets/Art/Vehicles/Models/Vehicles.fbx` and installed by an
  `AgentScripts/BuildVehicleArt.cs` into a `VehicleArtCatalog`, like `WorldArtCatalog`.
- Models (proposal): the player box truck (replacing piece 3's placeholder, with a company colour panel), and city cars:
  hatchback, sedan, van, pickup, taxi and city bus, each with a few tint variants; wheels as separate parts if they are to
  turn. Target 300-1,500 triangles per vehicle so 150 cars stay cheap.
- Authoring test that the catalog references every model; a capture of the line-up for review.
- This piece has no code dependency on 4a/4b and can be built alongside pieces 1-3 by a separate owner (disjoint files:
  `ArtSource/Vehicles`, `Assets/Art/Vehicles`).

#### Order

0 → 1 → 2 (with the v16 trip fields designed for 4a) → 3 → 4a → 4b; 4c alongside, needed by 3 for the truck and by 4b for
the cars.

## Out of scope (stay open)

Supplier deliveries by truck and world-owned supplier docks (0014, 0028 open item); trains; truck running costs and fuel;
refrigerated trucks; selling trucks; other companies (their trucks are drawn once they exist, answer 3); players driving
trucks; trucks or cars blocking players or customers physically; pedestrians; offline progression.

## Owner decisions needed

Answered by the owner on 2026-10-02:

1. **Speed depends on the road** (by `RoadKind`, capped by the truck's own speed) plus junction delays, as in piece 1.
   The values stay PROTOTYPE.
2. **As proposed:** a truck redirected while driving goes on to the next junction in its direction of travel and reroutes
   from there. Parking a driving truck finishes its trip to the nearer of the route's two sites (the implementer's reading
   of "your proposal"; correct this if wrong).
3. **Yes:** teammates, and other companies once they exist, see each other's trucks on the road. Piece 3 therefore draws
   every company's driving trucks, so site baselines need a public truck view (ID, company, trip fields, model; not cargo),
   like the public ownership view of 0028 piece 2.
4. **Warn only** when a dock's back is not open to the street; placement is never refused for this.
6. **Right-hand driving.**

Answered on 2026-10-02 after explanation:

5. **C: congestion with ambient city traffic**, vehicle models made in Blender (piece 4).

## Implementation (2026-10-02)

All pieces were built. Differences from the plan, and choices the plan left open:

- **Schema v17, not v16** (decision 0034 took v16 first). A v16 truck gets empty leg fields: one already driving finishes its
  abstract trip and drives the roads from its next departure.
- **Trip record: one leg at a time, no stored path.** A driving truck stores the leg it drives (`LegSegmentId`, `LegFrom`,
  `LegTo` in metres from the segment's From node, `LegDriveSeconds`, `LegSeconds` including the wait at its end node) and
  `RemainingSeconds` in that leg; `QueuedSeconds` counts a wait to enter the next segment, and while queued `NextSegmentId`,
  `NextFrom`, `NextTo`, `NextFinal` keep the leg it waits for, so a retry only asks the segment again. The way on is planned
  at each leg end (A* over the network with an admissible straight-grid estimate, ties by node index); nothing derived is
  cached across saves, so a reloaded world continues exactly as one that was never saved (tested). `TripStart`/`TripSeconds`
  were not needed.
- **Leg times.** Speed by road kind (arterial 14, local 10, rural 20 m/s) capped by the truck's speed; the BPR curve on load
  = city background + 250% of a car per truck per vehicle the direction holds at capacity (lanes x length / 20 m). A leg's
  time is fixed when it starts. Traffic lights run a fixed 30 s cycle per node (X axis green 0-13 s, Z axis 15-28 s, offset
  by a hash of the node ID); a truck reaching a red light waits for green plus a second, a stop sign costs 2 s plus the cross
  street's load. Route planning uses 6 s per light on average.
- **Time of day.** There was none, so one was added: a game hour is a real minute (24-minute days), with morning, lunch and
  evening peaks.
- **Queueing.** A segment direction above 120% load admits no further truck unless it has none or the truck has waited 30 s
  (so gridlock cannot last); queued trucks retry each second in truck-ID order. Background traffic alone is capped at 90%.
- **Parking a driving truck** sends it on (state `ToPark`) to whichever of its route's two sites is quicker from the end of its
  leg, where it parks. Trucks on abstract trips (dev worlds) keep the 0022/0023 rules.
- **Replication: no new message.** Baselines already reach subscribers every clock second; each carries the company's trucks
  with their legs and, view-only, `RoadTrucks`: every other company's truck driving the roads, without route or cargo.
- **Docks.** Restaurant docks keep decision 0034's refusal (`no-street-access`), which the owner chose there; the warn-only
  answer applies to docks on other generated lots (factories, farms): the dock screen warns when no walkable path reaches the
  dock from the lot's street edge, and trucks still use it.
- **Lot merging (scale).** A truck moving part of a lot now adds it to an equivalent lot already at the destination (same
  item, owner, condition and spoilage history, unreserved: decision 0003's merge rule) instead of leaving a new lot behind
  every loading second; whole lots keep their IDs. Split lots that do not merge get an ID hashed from the lot, destination and
  truck second (piece 0). Without this, 100 trucks left 8,000 lots and the clock second took 181 ms.
- **Presentation.** `TruckPresenter` draws trucks on legs (right-hand kerb lane, eased between frames, nose to tail at a
  leg end), at docks and parked at the kerb, painted in the company colour. `CityTrafficPresenter` draws `CityCars`: per lane a
  stream of cars at the hour's headway that halts at stop lines, queues at red lights and leaves one by one on green, and
  waits behind trucks; the 150 nearest within 300 m, pooled. Cars do not turn at junctions: each lane's stream ends at its
  node (accepted simplification; cars entering and leaving at junctions is visible up close).
- **Models** are flat-coloured (no textures) and installed as prefabs by `AgentScripts/BuildVehicleArt.cs` rather than a
  catalog asset.

Not done: a separate-process multiplayer check, independent review of the captures, and visible traffic-light lamp states
(lights switch only in the simulation; the lamp models stay static).

## Verification per piece (AGENTS.md)

Domain EditMode tests for every sim rule (1, 2), PlayMode for Unity integration and presentation (3), a loopback and then
separate-process multiplayer check for replicated truck positions (3), running-game captures for visual acceptance with
review by someone other than the implementer (3), and a frame-time measurement (3, and 4 if B). Each run reports filter,
run identity, matched count and artifact path; isolated save paths only.
