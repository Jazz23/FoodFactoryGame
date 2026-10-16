# Building domain integration contract

Port reference: `Assets/Scripts/Goods/GoodsWorld.Buildings.cs`, `GoodsWorld.Shell.cs`, and `SiteGrid.cs`. This module owns the building state rules; the primary domain module owns command dispatch, world storage, authorization, transactional cloning/replay, and persistence.

## Required persisted state

Keep existing `Building { id, site, kind, x, z, width, depth, floors, walls, doors }` and add serde-compatible state:

```rust
pub elevator_x: i32,
pub elevator_z: i32,
pub free_walls: bool,
pub structures: Vec<BuildingStructure>,
pub wall_style: String,
```

`walls` and `doors` are site-grid `(x,z)` coordinates; walls are occupied cells and doors are ground-level openings. Structure kinds: `floor`, `wall`, `partition`, `door`, `window`. A structure stores `kind`, `x`, `z`, `axis` (window axis: 0=X, 1=Z), `style`, `charged_cents`, and `role` (`service` for a back door, otherwise empty). Preserve every record and charged amount across snapshots/recovery. Missing fields from older serialized snapshots must default to empty/false/zero.

## Command data and ownership

`FloorOffer { cents_per_cell: i64, max_floors: u8 }` belongs to non-persisted catalog content. Add `Command::AddFloor { building, elevator_x, elevator_z }` and `Command::Shell { building, order }`. Shell order carries `kind` (`resize`, `partition`, `door`, `backdoor`, `window`, `remove`, `finish`), `x,z,width,depth,axis,style,cells`. Dispatch from `World::execute`; the normal `World.apply` clone/validate/replay boundary remains the only mutation/acknowledgement path. Building handlers must require `World::allowed(actor, site)`, company ownership of that site, and reject without changing candidate state on any failure. Do not bypass durable commit or request replay.

## Preserved Unity rules

- Rectangular shells are server-created, never moved/deleted, have dimensions at least 3, valid site bounds, no footprint overlap with another building, perimeter walls except recorded ground-floor doors, and no wall/elevator shaft collision with placed equipment or belts.
- Only factories add floors. Price is interior cells `(width-2)*(depth-2)` times the authored offer. Respect `max_floors`; select a clear interior elevator cell on the first addition; later floors extend the same shaft. Upper-level placements require an actual floor and are blocked by the shaft/walls. Charge and floor increment are one accepted operation.
- Restaurant shell editing follows Unity `PlanShell`: orders except resize convert rectangular walls to free walls; free-wall footprint is the bounds of partition records; resize is not available after conversion. Partitions, doors and windows are validated against existing walls/lot; openings cannot be placed at a junction; windows cover adjacent cells along one wall and cannot overlap doors/windows. Removal is one layer at a time. Free walls cannot become empty.
- Every newly charged structure records its full charge on the piece; removing it refunds that recorded amount. Original/unpaid shell pieces refund zero. Shell order fee rides on the first added piece. Net charge/refund and building update are atomic; a rejected order changes neither cash nor structure. Preserve minimum customer door, and if present minimum service door; never obstruct a service-door doorstep or disconnect a dock from its back door. Validate new walls against equipment/belts and other buildings before applying.

## Price status / open GDD detail

The Unity prototype's shell base amounts (floor cell 2,000 cents, wall cell 3,000, door 15,000, window 25,000, order fee 10,000) and floor offer amounts are data/prototype values, not universal design constants. GDD decision 0034 explicitly leaves price scaling and values open. Do not present those shell values as confirmed gameplay design; if implemented in this slice use the Unity defaults and isolate them as constants until authored catalog prices are integrated. Floor price uses its authored `FloorOffer`, not a guessed hard-coded price.

## Required integration checks

`World::validate` must invoke building validation; placement footprint checks must enforce floor level, perimeter/interior wall, shaft, and building overlap rules before equipment/belt placement. Tests should cover floor expansion/payment and elevator stability, shell conversion/refund/cash atomicity, invalid wall/door/window edits, and preservation of occupied goods/equipment on rejection. Test fixtures may construct isolated site/building state; do not modify authored production content to satisfy tests.
