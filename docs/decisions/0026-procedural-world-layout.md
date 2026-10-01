# 0026 - Procedural World Layout: Generator, Storage and Presentation

Date: 2026-09-27

Status: first slice **implemented** 2026-09-27; generator v2 and layout format 2 amended by [0027](0027-world-generator-v2-land-river-roads.md) (2026-09-28) (see [architecture status](../architecture.md#implemented-procedural-world-layout-first-slice-2026-09-27)
and the [verification record](../verification/worldgen-20260927.md)). The GDD section 3 "World Generation" owner decisions of
2026-09-27 are the requirements. The storage choice below was the implementer's, as the task allowed; it amends
[0012](0012-company-cash.md)'s "no mixed storage" rule for write-once data and needs owner confirmation. Every generator number
(sizes, weights, prices, capacities, demand values) is PROTOTYPE. One question was escalated (last section); the owner resolved it on 2026-09-29 in
[0028](0028-sites-for-generated-buildings.md).

## Requirements (GDD section 3, owner 2026-09-27)

About 1 km city with four districts (downtown, residential, wealthy, industrial on the edge between city and farms), farmland
outside it, fixed roads and rail with generated stations, restaurant/factory shells on the building grid with street doors,
premade model slots for farms/houses/apartments/offices, for-sale and competitor restaurants, purchasable factories/farms/
stations, a small cheap starting restaurant in residential; generated once at world creation, stored in SQLite with stable
IDs, never regenerated on load.

## Decision: the generator

- **Pure C#.** Assembly `FoodFactoryGame.World` (`Assets/Scripts/World`, `noEngineReferences: true`): the compiler rejects any
  Unity API. `WorldGenerator.Generate(requestedSeed, seed, settings)` returns a `WorldLayout` plus a report of every attempt.
- **Determinism.** One explicit PRNG (`WorldRandom`, xoshiro256** seeded by SplitMix64). Each phase forks its own stream from
  the seed by a fixed label (`arterials`, `districts`, `streets`, `stations`, `buildings`, `farms`, `orientation`), so a
  change in one phase cannot shift another's draws. Lists are built in fixed loops or sorted (`OrderBy` is stable); no
  `Guid`, `System.Random`, `string.GetHashCode` or hash-set order. Geometry is integer (doubled coordinates for half metres).
  IDs are `<category>-NNNN`, `node-NNNN`, `road-NNNN`, `rail-city`, `rail-farm`, assigned in generation order.
- **Seeds.** Typed text: a whole number is used as is, other text is hashed with FNV-1a; blank picks a random 64-bit value,
  which is then recorded as the requested seed (`WorldSeed`). `-seed <text>` and the host panel's "World seed" field set it.
- **Shape** (canonical frame, then turned 0-3 quarter turns by the seed): 6x6 arterials (jittered inside) make 5x5
  superblocks over [-500, 500]; the edge row is industrial, the centre superblock plus one neighbour downtown, a northern
  corner's Manhattan-2 neighbourhood wealthy, the rest residential. Local streets per superblock by district. Rural spurs
  continue every arterial to an outer ring road at 910 m; the map is 1900 m square. Road graph: every span crossing is a
  node; segments are axis-aligned with a kind, width and capacity per hour. Rail: a city line from the farm line through the
  city to the far farmland, and a farm line 110 m beyond the industrial edge. Stations are platforms at level crossings (one
  per zone on the city line, three spread along the farm line), doors facing the crossing road.
- **Buildings.** Lots line all four street edges of each block (north/south lots take at most half the depth; east/west lots
  fill the band between), doors on the street wall 2 m from the pavement. Restaurants (district weights; 30% for sale, the
  rest competitor-owned) and factories (industrial only, for sale) are shells in the decision-0019 model: footprint including
  walls, two non-corner door cells on the street side, one storey. Houses, apartments and offices are premade slots: a model
  key (`house/small-a`, `office/tower-b`, ...), footprint and `Facing` (transform = footprint centre, yaw = Facing x 90), one
  entrance cell, scenery storeys, `Ownership.Scenery`, no population. Farms (premade `farm/barn-*`, for sale) line both sides
  of the rural spurs, denser beyond the industrial edge. Prices = footprint cells x category cents x district percent.
- **Start.** The smallest (then cheapest) residential restaurant of at most 160 cells becomes `Ownership.Player`.
- **Validation** (`WorldLayoutValidator`, recomputed from the data, not trusted from the generator): unique IDs; exactly one
  district of each kind tiling the city; industrial alone on one city edge with farms beyond it; downtown holding the centre;
  axis-aligned roads/rails; shell rules; doors on the street wall; no overlaps of buildings, roads and rails; every building's
  door reaches pavement within the setback and that road is connected (breadth-first from the start restaurant); start rules;
  ownership per category; at least one farm and station, and a station within 300 m of a farm. A failing attempt is logged
  and generation retries with `WorldRandom.DeriveSeed(seed, attempt)` up to 8 attempts; the layout records the requested
  seed, the seed used and the attempt. Exhausting attempts throws and the server refuses to start.
- **Versioning.** `WorldGenerator.Version` = 1. Any change that can alter output for a seed must bump it; a test pins the
  hash of seed 20260927's layout.

## Decision: storage (implementer's choice, needs owner confirmation)

**A write-once `world_layout` table in `world.db`**, not a field of the goods snapshot payload.

- Database layout (`user_version`) 2 adds `world_layout (id = 1, world_id, format_version, generator_version, requested_seed,
  seed, attempt, payload, sha256, created_utc)`. The payload is `WorldLayoutText`, a strict canonical line format (integers
  and escaped tokens only); its SHA-256 is the layout's identity for tests, storage and replication.
- **Write once.** `WorldLayoutStore.Create` refuses a database with a layout row or any snapshot row, in one `BEGIN
  IMMEDIATE` transaction. When a world is created the server writes the layout first and the first snapshot second, so a
  crash between them leaves a layout-only database, which the next start treats as a world still being created (it keeps the
  stored layout and creates the snapshot). `GoodsSnapshotStore.HasSnapshots` distinguishes the two; `DevWorld.LoadOrCreate`
  now creates a world when the database has no snapshot rows rather than when the file is missing.
- **Never regenerated.** Loading reads the stored row; a later seed is ignored with a log line. A row that fails its checksum
  stops the server start instead of being replaced.
- **Older saves** (layout 1, no table) load unchanged with no layout; reading never upgrades them. The next commit's writable
  open upgrades the file to layout 2 (empty table). An existing world never gains a layout.

Why not the payload (the alternative the task offered, schema bump with an in-memory upgrade):

- Size. A layout is about 125 KB of canonical text (1,000+ buildings, 350 road segments). The payload is rewritten and synced
  on every command and every 10 s tick commit (decisions 0012, 0016); adding 125 KB to each commit costs time and pushes the
  payload toward decision 0012's 1 MB signal for data that never changes.
- 0012's objection does not apply. 0012 ruled out mixing payload and tables because a table has no revision history, so a
  fallback to the previous snapshot row could roll back goods but not cash. The layout is immutable: every snapshot revision
  sees the same layout, so no fallback can make them disagree. Mutable property state (ownership changes, purchases) must
  still go in the payload, keyed by the layout's building IDs; the layout records only the initial state.

This is an exception to 0012 for write-once world definition data, not a general permission to add tables.

## Decision: replication and presentation

- `WorldLayoutBridge` (Goods.Network, prefab `Assets/Prefabs/Network/WorldLayoutBridge.prefab`) is spawned by `SessionRoot`
  when the scene sets `worldLayoutBridgePrefab`. Each client requests the layout on start; the server answers the requesting
  authenticated connection with the gzip-compressed canonical text and its SHA-256, which the client verifies. It accepts no
  commands and carries no simulation state. Layout visibility is public map data.
- `WorldLayoutPresenter` (Session.WorldMap) builds the city from the received layout only, with the world art from
  `ArtSource/World` (amended 2026-09-27, owner request, replacing the first box blockout): tiled roads with sidewalks,
  crosswalks and junction patches, rail track, grass/paving/yard ground cover, a textured low-poly model per building
  fitted to its footprint and facing (premade model keys choose the variant; shells pick one by ID), stacked storeys for
  apartments and offices, field plus barn for farms, and restaurant awnings coloured by owner (player green, for sale
  yellow, competitors by ID). Buildings and barns have colliders; nothing in the simulation reads the presenter.
- Scene: `Assets/Scenes/WorldGen.unity`, a copy of DevSite plus the bridge and presenter, with its own spawnable catalog
  `Assets/Network/WorldGenPrefabs.asset`; built by `AgentScripts/BuildWorldGenScene.cs`. Not a build scene. DevSite, its
  catalog and the dev seed are unchanged, and the dev world is still seeded in WorldGen. WorldGen saves under its own
  folder (`Saves/worldgen`; amended 2026-09-27 after the owner found Host loading the DevSite save, which can never gain a
  layout); the panel's World field and New world button choose or create a world folder, and a Saved worlds list (folders
  holding a world save, most recently played first; added 2026-09-30) fills the World field on a click. PROTOTYPE placement: the city's
  edge starts 40 m north of DevSite's 40 m floor, pieces over that floor are not drawn, and local cameras draw to about
  2.4 km while a layout is shown.

## Escalated (resolved 2026-09-29 by 0028): does each purchasable building become its own site?

Resolved by [0028](0028-sites-for-generated-buildings.md): one site per purchasable building and its lot, site IDs reserved
in the layout, sites created on first purchase, the dev site kept off the map, competitors kept as records linked to lots.
Piece 1 (lots, reserved site IDs, server-side purchase) implemented 2026-09-29. The original question follows.

Options included one site (with its own `SiteGrid` sized to the building) per purchasable building, one site per
owned plot or company campus, or buildings placed inside larger sites. It affects site IDs, grants, trucks' `GoodsSite.MapX/Z`
(which today are unrelated dev values, not layout coordinates), customer districts (decision 0024's `GoodsDistrict` is a
separate dev record) and where the dev site sits on the map.

Stub only: `WorldBuilding.SiteId` exists and is always empty (the validator enforces that), and
`WorldLayoutShells.ToGoodsBuilding/SiteLayoutFor` express a shell on a grid of its own size. Nothing calls them at runtime;
a test uses them to show every generated shell passes decision 0019's bootstrap rules.

## Out of scope and open

Customers from district densities, competitor behaviour, buying property, trains, final Blender models, the ingredient
supplier "reachable by road" from the start (not modelled yet), lunch/dinner demand curves, traffic simulation using
capacities, and interior layout of shells. Whether the district demand values replace decision 0024's dev district is open.
