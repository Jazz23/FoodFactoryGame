# 0028 - Sites for Generated Buildings

Date: 2026-09-29

Status: **accepted, partly implemented**. Resolves the question escalated in [0026](0026-procedural-world-layout.md) ("does each
purchasable building become its own site?"). Owner decisions of 2026-09-29; GDD section 3 "World Generation".

Implemented (piece 1, 2026-09-29; see `docs/architecture.md`): lots and reserved site IDs in layout format 3 (generator v3),
their validation, the property catalog, the server-side purchase (`BuyPropertyDurably`) with ownership as its own record
(goods schema v14), and a server-only bootstrap for the starting restaurant. Piece 2 (2026-09-29): generated worlds start
in the starting restaurant's own site (the map is moved so its lot sits at the scene origin), `RequestBuyProperty` over the
network, teammates' access in the purchase's commit (players granted any of the company's sites; not employees), public
ownership in every site view, ownership colours and a buy panel. Owner decisions for piece 2: PROTOTYPE starting cash
$5,000, raised to $1,000,000 the same day; bought sites are managed remotely only until several sites can be drawn at once (piece 3). Not yet: walking into
bought sites, customers in generated worlds, competitors linked to lots. Resolved open items:
format 3 / generator v3; existing format 1 and 2 worlds get no lots and are treated as unsupported development data; PROTOTYPE
apron depths (factories 12 m, farms 8 m, others the 2 m setback); a lot costs its building's layout price for now.

## Options considered

- A. One site per building, shell only: the site's grid is exactly the building's footprint.
- B. One site per building plus its lot: the grid covers the shell and a paved lot around it reaching the street. **[SELECTED]**
- C. One continuous world grid (possibly chunked) with buildings as shells on it; land ownership gates placement.

C was rejected for now: site IDs key equipment, belts, buildings, employees, jobs, truck stops, grants, customers and
replication (`SiteId` has about 365 uses in 32 files), so C is a rewrite of placement, interest/replication and truck
routing plus a save migration, and ~1 km of cells conflicts with per-site snapshots and distant-site operation. A
is simpler but leaves docks, parking and outdoor equipment nowhere to go. B keeps the per-site model and gives trucks,
docks and farm fields a real place in the world.

## Decision: one site per purchasable building and its lot

- Every purchasable building (restaurant and factory shells, farms, stations) has a **lot**: the building's footprint plus a
  paved apron that reaches its street. Lots are layout data: produced by the generator, validated (stable IDs, no overlap
  between lots, road access), hashed, stored write-once and replicated with the rest of the layout. Scenery buildings have no
  lot.
- A purchasable building's site covers exactly its lot. The shell becomes the site's `GoodsBuilding` (decision 0019) and the
  apron is ordinary outdoor cells for docks, trucks, storage and belts.
- Only buildings are sold (GDD 29.5 D, unchanged): the lot comes with its building and is never sold separately.

## Decision: IDs reserved at world creation, sites created on first purchase

- The generator gives each lot a deterministic **reserved site ID** in the layout (for example `site-<lotId>`; the exact
  format is an implementation detail). Routes, competitor records and any other reference to a building that may not be
  owned yet use the lot or its reserved site ID, never a runtime-only ID.
- The `GoodsSite` record is **created on first purchase**, in the same commit that debits the buyer and records ownership. A
  rejected or failed purchase leaves no site, no ownership change and no debit. Unowned buildings cost nothing in the goods
  snapshot, validation or replication.
- **A created site is never deleted**, including if its building is later sold (resale itself is still open, GDD 29.6), so no
  goods or equipment can vanish with a site.
- Exception: sites the world itself needs to receive goods (for example distributor or supplier docks) may be created at
  world creation. Which ones, and how many, is open; it is expected to be a small fixed number.

Reasons: creating a site for every purchasable building up front would put hundreds of empty sites (seed 20260927 has
1,782 buildings in total) into a snapshot committed every tick, against a 20-site target, with no benchmark covering it.
Reserving IDs keeps the stable-identity advantage of up-front creation at on-purchase cost.

## Required constraints (keep a later move toward C cheap)

1. **World coordinates.** Each lot and site records its origin (and quarter-turn facing, if needed) on the world grid, so any
   site cell maps to exactly one world cell. Trucks' `GoodsSite.MapX/Z` come from this, not from unrelated dev values.
2. **Ownership is its own record** ("company owns lot L"), not a property that only exists on a site.
3. **A site may hold more than one lot or building** in the data model and validator, even though the first slice only
   creates single-lot sites.
4. **Cross-site goods movement goes through one transfer path** (today's truck cargo transfer inside the clock tick), not
   scattered site-ID checks.

## Decision: the dev site stays off the map

The DevSite scene, its seed and its `dev-world` save remain a test and development fixture. Generated worlds will not contain
it; the player starts in the generator's starting restaurant (GDD section 3), which becomes a site like any other purchased
building. The current PROTOTYPE presentation that places the city 40 m north of the dev site in `WorldGen.unity` is
replaced once the starting restaurant is a site (done in piece 2 for format 3 worlds; format 1 and 2 worlds keep it).

## Decision: competitors stay records, linked to lots

- Competitors remain lightweight `GoodsCompetitor` records (decision 0024) with no production. Each record is tied to its
  building's lot and reserved site ID instead of a raw `MapX/Z`.
- On acquisition (GDD section 11) the site is created as for any purchase and the competitor record is retired in the same
  commit. Whether the acquired building arrives empty or with equipment derived from the record is open.
- **Planned, not decided:** when external sales are built, competitors may gain an ingredient stock consumed by each sale and
  restocked abstractly or by player deliveries to their lot's dock, so goods delivered to them stay conserved. This is
  additive to the record model.
- Rejected for now: competitors as full production sites (needs kitchen-running AI and hundreds of active sites).

## Planned follow-up (proposal, not decided)

Merging adjacent owned lots into one site (a factory campus with belts between buildings) as a later step, enabled by
constraint 3. It relates to GDD 29.5 option C and needs an owner decision before implementation.

## Open

- Lot shape rules (apron depth, corner lots, farms' field extent, stations' platform) and lot prices versus building prices.
- Layout format and generator version for lots and reserved IDs, and what existing format 1/2 worldgen saves do (derive
  lots on load under a fixed rule, or treat them as unsupported development data). Stored layouts are never regenerated
  (0026).
- Which world-owned sites exist at creation; the ingredient supplier near the start (0026).
- Whether an acquired competitor's building starts empty.
- Customer districts from the layout versus decision 0024's dev district, and visual customers at competitor buildings
  (presentation only).
