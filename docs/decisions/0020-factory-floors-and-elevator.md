# 0020 - Factory Floors and the Freight Elevator

Date: 2026-09-24

Status: requested by the project owner ("work on adding floors"). **Owner choices (2026-09-24):** only factories can add
floors (GDD section 5); characters move between floors by freight elevator (GDD section 27); a paid floor exists as soon as
the order is accepted (no build time yet); conveyor lifts come in a later slice. Buildings are procedurally generated and
some are purchasable, and adding floors is a construction option; fully custom buildings are undecided (GDD sections 5, 18,
29). The first floor's elevator position (the avatar's cell), prices, height limit and the dev factory's size and position
are PROTOTYPE values chosen by the implementer. Implemented. See
[architecture status](../architecture.md#implemented-factory-floors-and-the-freight-elevator-2026-09-24) and the
[verification record](../verification/floors-20260924.md).

## Context

Decision 0019 made building shells server data on a flat site grid. GDD section 5 lets factories add floors when land is
limited, section 27 moves workers and bulk loads by freight elevator and goods by conveyor lift, and construction is
outsourced for money only. Everything placed on the grid (equipment, belts) had only a cell, so a second storey needed a
level in the shared grid rule and in the save.

## Decision

- **Buildings have a kind and floors.** `GoodsBuilding` gains `Kind` (`restaurant` or `factory`), `Floors` (storeys including
  the ground, at least 1) and `ElevatorX/ElevatorZ` (an interior cell, meaningful while `Floors > 1`). Only a factory may
  have more than one floor. Goods snapshot schema **v8**; a v7 save upgrades in memory with every building a one-storey
  restaurant (the only kind that existed).
- **Levels on the grid.** `GoodsEquipment.Level` (while placed; 0 when held) and `GoodsBelt.Level`. `SiteGrid.CellProblem`
  takes a level: level 0 is the ground; a level above 0 exists only where the whole footprint lies inside the interior of a
  building with more floors than that level (`no-floor` otherwise). Overlap is checked only against things on the same
  level. Walls surround every storey (upper storeys have no doors). The elevator cell is `blocked` on every storey of its
  building, so the shaft always stays clear. Placement, belt placement, the client preview and recovery keep sharing this
  one rule.
- **Belts per floor.** Each floor of each site is its own belt network; a belt never feeds a belt on another level.
- **Adding a floor is a paid construction order.** `AddFloorDurably(player, request, building, elevatorX, elevatorZ)` checks
  the grant, that the building is a factory, the registered `FloorOffer` (price per interior cell, maximum storeys), the site
  company, the elevator cell (first floor only: an interior cell with nothing on it) and funds, then debits the company and
  adds the floor in one commit. Like a purchase (decision 0014), rejections are answered but not recorded, and the accepted
  order is keyed by player and request ID, so a retry replays it and never charges twice. Floors are never removed, so
  nothing placed upstairs can be stranded.
- **Content.** `FloorOffer` is registered by the server at start, like supplier offers, and never saved. Dev values:
  500 cents per interior cell, 3 storeys.
- **The elevator is a local ride.** Avatar position is presentation only (decision 0005), so riding is a client-side move:
  standing on the elevator cell, `Player/FloorUp` (PgUp) and `Player/FloorDown` (PgDn) teleport the owned avatar one storey.
  No server rule depends on which level a player stands on; a placement request names its level and the server checks the
  level, not the player.
- **Presentation.** `BuildingPresenter` builds each storey: the ground storey with its doorway and floor tint; each upper
  storey with a solid slab over the interior (avatars stand on it) and a closed ring of walls; an elevator pad on every
  storey. The local avatar's height gives its level (`SiteGridSpace.LevelHeight` = 3 m per storey). In the building the
  avatar stands in, every storey above its level, that storey's machines, belts and riding goods, and other players up there
  are hidden (with their colliders) for this client only. Placement and aiming use the avatar's level.
- **UI.** Inside a factory, the inventory screen shows a Factory window: floors built out of the maximum, the next floor's
  price and a Build button. Before the first extra floor it says the elevator goes where the player stands.
- **Dev content.** `dev-factory`: 5x10 cells at (15, 10) with a doorway at (16, 10) and (17, 10) in its south wall. It clears
  the seeded oven and counter, the spawn points, every PlayMode test cell and everything placed in the owner's current dev
  save. `DevWorld.EnsureFactory` adds it once to an existing save (committed before serving; warns and skips if blocked).
  `EnsureBuilding` now keys on the restaurant's ID, so an older save still gains the restaurant when the factory exists.

## Alternatives rejected

- **Floors for every building kind:** not chosen by the owner; GDD section 5 names factories.
- **Stairs, or a temporary floor-switch key:** not chosen by the owner; GDD section 27 selects elevators for workers.
- **Build time or rush payment:** not chosen by the owner for this slice; the general timing question (GDD 29.2) stays open.
- **Conveyor lifts now:** deferred by the owner. Goods reach upper floors carried by a player riding the elevator.
- **Server-tracked player level:** position is presentation only (decision 0005); a server rule would first need that
  authority model to change.
- **A separate grid per floor:** one grid with a level keeps a single placement rule for every caller and every save.

## Consequences and open questions

- Conveyor lifts (GDD section 27) are the next slice: goods moving between floors by belt.
- Employees, when they exist, will need the elevator as a route between levels.
- Build time, cancellation and disruption for construction (GDD 29.2, 29.3, 29.7), buying buildings (29.5) and whether a
  factory can grow beyond its generated footprint remain open.
- The first floor's elevator position is picked by where the player stands; a proper build-mode placement preview is not
  implemented. The shaft cannot be moved.
- No gamepad binding rides the elevator yet: D-pad up and down already place and take belt items.
- Only the local client hides storeys; a remote camera sees another player's view independently.
