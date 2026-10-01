# 0032 - Trucks on Generated Roads Between Sites (plan)

Date: 2026-09-30

Status: **proposed, not accepted.** Implementation plan requested by the project owner ("write a plan for trucks on roads
between sites"). Nothing here is decided until the owner accepts it; the questions under [Owner decisions needed](#owner-decisions-needed)
come first. Every number is PROTOTYPE.

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
- **Schema.** Goods snapshot **v16** adds the trip fields. A v15 driving truck upgrades with `TripStart` = its `SiteId`
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

### Piece 4 - Congestion (needs an owner decision; not proposed for the first slice)

GDD confirms trucks are affected by congestion, but no model is chosen. Options, smallest first:

- A. **Static traffic.** Segment time multiplied by district `TrafficPercent` and a time-of-day curve (lunch/dinner).
  Deterministic, cheap, no interaction between trucks. Visuals unchanged.
- B. **Player trucks congest each other.** Each segment admits trucks up to a share of `CapacityPerHour`; excess trucks
  queue at the segment entrance. Real interaction and an emergent reason to spread routes, but trip time is no longer known
  at departure, so `MoveTrucks`' skip-ahead becomes event-driven per segment entry. Needs its own scale benchmark.
- C. **Ambient city traffic** (non-player cars) feeding B. Highest cost; presentation-heavy.

Recommendation: A first if congestion is wanted soon; B only if truck-vs-truck interaction is a desired depth mechanic
(it fits the "simple or deep" logistics goal: one rule — segment capacity — creating routing choices).

## Out of scope (stay open)

Supplier deliveries by truck and world-owned supplier docks (0014, 0028 open item); trains; truck running costs and fuel;
refrigerated trucks; selling trucks; other companies' trucks; players driving trucks; trucks blocking players physically;
offline progression.

## Owner decisions needed

1. Speeds by road kind and junction delays: accept the PROTOTYPE values, or should every truck drive at its own speed
   everywhere?
2. Redirect/park while driving: continue to the next junction then reroute (proposed), or keep "restart from the site it
   left"? Should parking a driving truck finish its trip?
3. Should teammates (and later other companies) see each other's trucks on the road? Piece 3 shows only the viewer's
   company's trucks, which is everyone today (single company).
4. Dock placement: warn only (proposed), or require a clear path from the dock's back to the street?
5. Congestion: none yet, A, or B (piece 4)?
6. Drive side (right-hand proposed).

## Verification per piece (AGENTS.md)

Domain EditMode tests for every sim rule (1, 2), PlayMode for Unity integration and presentation (3), a loopback and then
separate-process multiplayer check for replicated truck positions (3), running-game captures for visual acceptance with
review by someone other than the implementer (3), and a frame-time measurement (3, and 4 if B). Each run reports filter,
run identity, matched count and artifact path; isolated save paths only.
