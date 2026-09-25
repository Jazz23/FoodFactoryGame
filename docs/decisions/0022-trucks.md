# 0022 - Trucks, Loading Docks and a Second Site

Date: 2026-09-24

Status: requested by the project owner ("start working on trucks"). **Owner choices (2026-09-24):** the first slice connects
two owned sites; roads are abstract (a site map position and a travel time from the road distance) with visible roads
later; goods get on and off trucks through loading-dock equipment; the slice is fully playable. Sizes, prices, speeds,
rates, the map positions, the L key, the dock and truck models and the rules marked PROTOTYPE below were chosen by the
implementer. Implemented. See [architecture status](../architecture.md#implemented-trucks-and-loading-docks-2026-09-24).

## Context

GDD section 9 (confirmed): trucks do flexible point-to-point delivery on public roads the player never builds; the player
configures each route (pickup, dropoff, allowed cargo, assigned vehicles) and assigned vehicles repeat it; loading and
unloading capacity can bottleneck; perishability makes route time matter. Section 17 puts truck logistics in the MVP.
Decision 0003 already reserved vehicle cargo and in-transit locations. Until now there was one site, no map and no
cross-site movement, and supplier purchases arrive instantly (decision 0014, "revisit with trucks").

## Decision

- **Sites on a map.** `GoodsSite { Id, Name, MapX, MapZ }` (whole metres) is server data beside the site layout. Road
  distance is the Manhattan distance between two sites (the public roads form a grid); a trip takes
  `ceil(distance / speed)` seconds, at least 1 between different sites. No congestion yet.
- **Docks.** A dock is ordinary equipment of kind `dock` (placed, picked up, bought, validated like any machine) with no
  recipes. Its input buffer is **Outgoing** (players, employees and later belts fill it; trucks load from it) and its
  output buffer is **Incoming** (trucks unload into it; it only gives, like any output).
- **Trucks.** `GoodsTruck { Id, CompanyId, Name, CargoSlots, SpeedMetresPerSecond, LoadUnitsPerSecond, PickupDockId,
  DropoffDockId, AllowedItemIds, State, SiteId, DestinationSiteId, RemainingSeconds }`, states `Parked`, `ToPickup`,
  `Loading`, `ToDropoff`, `Unloading`. Stats are copied from content when the truck is created.
- **Cargo is a location nobody else reaches.** Each truck has `<id>:cargo` (kind `vehicle`, capacity `CargoSlots`, not
  refrigerated) on the reserved site `road`, which can never be granted or owned. So transfers, reservations, belts and
  employees cannot touch it (`Transfer` also refuses vehicle locations as `invalid-route`); only the truck moves goods in
  and out. Lots keep their IDs, exposure and owner aboard, age in transit, and take the dropoff site as owner when
  unloaded.
- **The cycle.** Each simulated second a truck at its pickup loads up to `LoadUnitsPerSecond` allowed, unreserved units
  from the dock's Outgoing buffer, most exposed first, as far as its slots allow. A second in which it loads nothing and it
  has cargo sends it to the dropoff; with no cargo it waits. At the dropoff it unloads up to the same rate into Incoming;
  once empty it drives back. A full Incoming buffer, or a dock that is not placed, makes it wait with its cargo. Nothing is
  dropped or duplicated.
- **Step independence.** Trucks run inside `Advance`, one second at a time in truck-ID order (so trucks sharing a dock
  share it the same way at any step size), skipping ahead while every active truck is driving. Tested: one 47 s step equals
  47 one-second steps.
- **Routes are a player command.** `SetTruckRouteDurably(player, request, truck, pickupDock, dropoffDock, items)` checks,
  in order: identity and replay; the truck (`forbidden`); both docks (`invalid-dock`); the player's grants on both dock
  sites and the truck's company owning both (`forbidden`); different sites (`same-site`); the cargo filter, at most 16
  distinct item IDs, empty for any (`invalid-cargo`); map records for both (`no-road`). Like purchases, rejections are
  answered but not recorded or committed; the accepted change replays. The truck then heads for the dropoff if it carries
  anything, otherwise for the pickup. PROTOTYPE: a truck redirected while driving restarts from the site it left; no
  position between sites is kept.
- **Remote management.** A site baseline now carries the company's trucks (with their cargo lots and locations, appended
  after the site's own locations so the first still names the site) and the map records of the company's sites. The bridge
  lets one connection watch several granted sites. Admission also grants every existing remote dev site, without an
  inventory there, so a player can move stock between a remote site's storage and dock from afar (GDD section 15 "remote
  task assignment"). Physically travelling to another site is not part of this slice.
- **Schema.** Goods snapshot **v11** adds `Sites` and `Trucks`; a v10 save upgrades with neither.

## Dev content (PROTOTYPE)

- Map: the dev site "Restaurant" at (0, 0); a new remote site "Warehouse" (`dev-warehouse`, 10x10 grid, 30-slot storage
  with 200 dough, owned by the dev company) at (100, 50): 150 m by road.
- Docks: 2x1 cells, 8 Outgoing and 8 Incoming slots, $60.00 at the supplier. One placed at the warehouse (4, 4) and one on
  the restaurant grid at (0, 18), its back against the north edge.
- Truck 1: 4 cargo slots, 15 m/s (10 s each way), 5 units a second, starting at the warehouse on the warehouse-to-restaurant
  route with any cargo.
- A save from before this decision gains whatever of these it lacks once, committed before serving; a restaurant dock
  whose cells are taken, or a dock the players already placed there, is kept and the truck then starts parked.
- The dev sites' map positions are seed content, like machine slot counts: every server start moves them to the current
  values, so a save made with older positions follows them (a truck already driving keeps its remaining time).

## Presentation

- **L** (`Player/Logistics`) opens the logistics screen: each truck's live status and cargo, a route editor (Load at /
  Deliver to / Cargo with arrow buttons, then Apply route), and for each remote site its storage and dock stock with Ship,
  Unstage and Store buttons that move one stack per click. The UI offers one cargo item or Any; the domain accepts a list.
- Opening a dock shows Outgoing and Incoming grids and the trucks that serve it.
- While a truck loads or unloads at a dock on the local site, a placeholder truck stands behind the dock. Trucks on the
  road have no scene presence.

## Alternatives rejected

- **Truck pickup and dropoff at any machine or storage**: fewer clicks, but the owner chose dedicated docks so loading
  throughput is a real bottleneck.
- **Players walk between sites carrying goods**: the player inventory is site-bound; carrying goods across sites would
  itself be untracked transport. Remote management was chosen for this slice.
- **Cargo on the truck's current site**: players with a grant there could reach it with ordinary transfers; the reserved
  road site closes every such path at once.

## Open

Selling trucks and route scheduling (buying trucks, several vehicles per route and parking: decision 0023), visible roads and driving trucks,
congestion, truck running costs, supplier deliveries by truck (decision 0014), refrigerated trucks, belts or employees
feeding docks, travelling between sites, and the GDD's open player/ownership questions.
