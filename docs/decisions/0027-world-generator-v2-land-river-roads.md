# 0027 - World Generator v2: Land, River, Road Features, Trees and Density

Date: 2026-09-28

Status: **implemented** 2026-09-28 (see [architecture status](../architecture.md#implemented-world-generator-v2-2026-09-28) and the
[verification record](../verification/worldgen-20260928.md)). Amends [0026](0026-procedural-world-layout.md); everything 0026
decided and did not list here is unchanged, including the escalated site question. Every generator number is PROTOTYPE.

## Requirements (owner request, 2026-09-28)

"Increase density; add railroad crossings; add trees; add different elevations; add stoplights/stop signs; add river and
bridges. Use Blender." The owner did not specify quantities, placement rules or any gameplay effect; the choices below are the
implementer's and are open to change.

## Decision: data, not presentation

Elevation, the river, bridges, level crossings, junction controls and trees are **layout data** produced by the generator,
validated, hashed, stored and replicated like the rest of the layout, rather than invented by the client presenter. Reasons:
land height matters to future building interiors and traffic, junction controls and level crossings to a future traffic model,
and the validator can only guarantee "no building in the river, every crossing of the water is bridged" if the data exists.
Trees are scenery but are placed with the generator's overlap indices, so they cannot land on buildings, roads or water.

## Decision: layout format 2, generator version 2

- `WorldLayout.CurrentFormat` = 2; `WorldGenerator.Version` = 2 (seed 20260927's hash re-pinned). `WorldLayoutText` still
  reads and writes **format 1 byte for byte** (a test round-trips a literal v1 layout), so stored v1 worlds keep their hash and
  load unchanged; they present as flat land with no river, controls or trees. A stored world is never regenerated (0026).
- Additions: `node` lines gain a `JunctionControl` (None, StopSign, TrafficLight); `building` lines gain `ElevationCm`;
  new sections `terrain`/`heights` (land heights in centimetres every 20 m, 96 x 96 samples, sampled bilinearly by
  `WorldTerrain`), `river` (centreline points about 20 m apart, width, water surface drop below the land), `bridge`
  (the road segment or rail line carried, the river, and the carried centreline's endpoints), `crossing` (road segment,
  rail line, point) and `trees` (cell, `TreeKind`, scale percent; 200 per line). A layout grew from ~126 KB to ~368 KB of
  text for the known seed; it is stored once and sent gzip-compressed, never in the goods payload.

## Decision: generator rules (all PROTOTYPE numbers in `WorldSettings`)

- **Density.** More local streets per superblock (downtown 3, residential 2-3, wealthy 2, industrial 1-2), per-district gaps
  between lots (downtown 0-1 m), taller downtown blocks (apartments 4-8, offices 6-16 storeys), and east/west block edges now
  get lots (v1 left them almost empty: north/south lots reserved the whole depth). Farms: 70% chance per slot, 6-20 m gaps.
  Known seed: 1,010 buildings in v1, 1,782 in v2; seeds 1-30 have at least 1,697.
- **Land.** Three octaves of integer value noise (480/200/80 m lattices, 24/6.5/1.5 m amplitude), damped to 22% inside the
  city (full relief 220 m outside it) and to 20% along the rail lines; the valley along the river is flattened to a floor
  1.5 m below the averaged land. Known seed spans -26 m to +15.5 m. A building's entrance level is the land height at its
  first door; a footprint whose corners or centre rise or fall more than 1.5 m from it is left empty (farms exempt, their
  fields drape). No lot was lost to slope for the seeds checked.
- **River.** One river crosses the whole map west to east (before the seeded quarter turns) inside one superblock row other
  than the industrial one, meandering between control points every 160 m while staying clear of that row's arterials; east-west
  local streets that would run beside it are dropped. Width 22 m, surface 1.6 m below the land. Every north-south road
  segment and the city rail line that cross it get a bridge spanning the water plus a 12 m bank; buildings keep off the
  water plus bank.
- **Level crossings.** Every at-grade meeting of a rail line and a road segment is recorded (20 for the known seed).
- **Junction controls.** Nodes with at most two segments: none. Otherwise traffic lights where three or more arterial
  segments meet, or where a downtown four-way junction includes an arterial; stop signs everywhere else. Who stops is a
  shared rule (`WorldJunctions.Stops`): the approaches of the lowest road kind, or every approach when all kinds are equal.
- **Trees.** Sidewalk trees on city streets (0.5 m inside the road's outer edge, clear of junctions, doorways, bridges and
  crossings; chance by district), a yard lattice in city blocks, countryside woodland where a woodland noise is high plus
  lone trees, and riverbank trees; never within the water plus 8 m.
- **Validation** (`WorldLayoutValidator`, format 2 only): terrain covers the map; entrance elevations equal the land height
  and footprints respect the rise limit; controls match junction degree; rivers are valid and clear of buildings; every road
  or rail crossing of a river is covered by a bridge on that way's centreline reaching at least half the water width each
  side; level crossings match the actual rail/road intersections exactly; trees are on the map and not in buildings, rails,
  water, carriageways or junctions (only within 1.5 m of a city street's edge).

## Decision: presentation (`WorldLayoutPresenter`, art from Blender)

- The land is a 5 m terrain mesh in 25 chunks with mesh colliders (walkable), cut 0.15 m under roads and rails and cut into
  the river channel (bed 1.6 m below the surface at the water's edge, sloping out over a 10 m bank). The channel exists
  only in presentation: roads over a river follow the smooth land surface, which is the bridge deck.
- Road, junction, rail, crossing and bridge tiles are draped vertex by vertex over the land (Blender tiles are cut into
  segments along their length and have skirts that hide seams); signs, lights, signals and trees stand rigidly at the land
  height of their origin. Merged meshes are chunked at 250 m per material for culling; roads, rails and bridges get mesh
  colliders.
- Buildings stand at `ElevationCm` on a concrete plinth reaching below the lowest land under them; barns on a plinth levelled
  to the highest ground under them. The map is lowered so the land at the dev site stays at its floor (y = 0) and the land
  within 30 m of it is levelled.
- New Blender pieces (`ArtSource/World/build_world_models.py`): `Rail_Crossing`, `Rail_Signal` (crossbuck, twin red lamps,
  raised gate), `Stop_Sign`, `Traffic_Light_Arterial`/`_Local` (mast arm over the approach lanes, three-lamp heads),
  `Bridge_Deck`, `Bridge_Railing`, `Bridge_Pier`, `Foundation`, `Tree_Broadleaf_a/b`, `Tree_Conifer`, `Tree_Poplar`,
  `Tree_Bush`; new textures for water, river bank, foliage, bark, the stop sign and crossbuck lettering; signal lamps glow
  (URP emission). Signals are static: no light phases or gate motion (nothing simulates traffic or trains yet).

## Open

Traffic and trains using controls and crossings; whether elevation affects building interiors, deliveries or walking speed;
more than one river, lakes, or water crossings by ferry; bridge clearance for boats; seasonal or district-specific trees;
lot-level grading instead of plinths; the world's edge beyond the map square.
