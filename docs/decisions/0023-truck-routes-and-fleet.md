# 0023 - Truck Routes, Several Trucks per Route, Parking and Buying Trucks

Date: 2026-09-24

Status: requested by the project owner ("start with step 1" of the truck follow-up plan: route records with several trucks,
clearing a route, buying trucks). Sizes, the truck price and the UI layout were chosen by the implementer and are marked
PROTOTYPE. Implemented. See [architecture status](../architecture.md#implemented-truck-routes-and-fleet-2026-09-24).

## Context

GDD section 9 (confirmed): "The player chooses pickup/dropoff points, allowed cargo, and assigns vehicles. Once configured,
assigned vehicles repeat the route automatically." Decision 0022 stored the route on each truck, so a route could serve
only one truck, a truck could not be taken off its route, and trucks existed only through the dev seed. Its Open list names
buying trucks, several vehicles per route and clearing a route.

## Decision

- **Routes are records.** `GoodsRoute { Id, CompanyId, PickupDockId, DropoffDockId, AllowedItemIds }` in
  `GoodsSnapshot.Routes`. A route belongs to the company that owns both docks (the same rules as 0022: two docks on
  different mapped sites of one company, at most 16 distinct cargo item IDs, empty for any). A route may have no trucks.
- **Trucks point at a route.** `GoodsTruck.RouteId` replaces the truck's own pickup, dropoff and cargo filter. Empty means
  parked; `Parked` is exactly the state with no route. A route's trucks must belong to its company.
- **Commands** (all durable, keyed by player and request ID; rejections answered but not recorded, as for purchases):
  - `CreateRoute(player, request, pickupDock, dropoffDock, items)` makes route `route:<player>:<request>` with no trucks.
    Checks: both docks (`invalid-dock`); grants on both dock sites and one company owning both (`forbidden`); `same-site`;
    `invalid-cargo`; `no-road`.
  - `SetRoute(player, request, route, pickupDock, dropoffDock, items)` edits a route (`forbidden` if unknown, then the
    create checks against the route's company). Every assigned truck is re-dispatched as 0022's route change did: loaded to
    the new dropoff, empty to the new pickup, restarting from the site it stands at or last left (PROTOTYPE, unchanged).
  - `DeleteRoute(player, request, route)` parks every assigned truck and removes the route. Needs grants on both of the
    route's dock sites (`forbidden`).
  - `AssignTruck(player, request, truck, route)` puts a truck on a route (grants on both dock sites and the same company, or
    `forbidden`) and dispatches it; a truck already on that route is left as it is (accepted, no restart). An empty route
    parks the truck, which needs a grant on any site of the truck's company.
- **Parking.** A parked truck stands at the site it stands at or, if it was driving, the site it last left (PROTOTYPE, like
  a redirect). Its cargo stays aboard, still unreachable; assigning it to a route delivers that cargo to the new dropoff.
  Nothing is dropped.
- **Buying trucks.** `TruckOffer { Id, PriceCents, Name, CargoSlots, SpeedMetresPerSecond, LoadUnitsPerSecond }` is content
  in the supplier's offer ID space. `Buy(player, request, site, offer)` with a truck offer checks the grant (`forbidden`), the
  offer, the site's company (`no-company`), the site's map record (`no-road`) and cash (`insufficient-funds`); it needs no
  inventory. It charges once and creates truck `buy:<player>:<request>`, named `<offer name> <n>` (`n` = the company's truck
  count + 1, names are not identities), parked at the site with an empty cargo location. The outcome's `EquipmentId`
  carries the new truck ID.
- **Views.** A site baseline carries the company's routes beside its trucks.
- **Schema.** Goods snapshot **v12** adds `Routes` and `GoodsTruck.RouteId`, and drops the truck's route fields. A v11 truck
  with a route becomes route `route:<truck id>` (same docks and cargo filter) with that truck on it; a parked one stays parked.

## Content (PROTOTYPE)

- `TruckDefinition` asset `Assets/Content/Vehicles/Truck.asset`: "Truck", 4 cargo slots, 15 m/s, 5 units a second (the
  dev truck's values). Offer `Truck1` (`supplier-truck`) costs $250.00.
- The dev seed makes route `dev-route-1` (warehouse dock to restaurant dock, any cargo) with `dev-truck-1` on it.

## Presentation

- The logistics screen (L) lists **Routes** (Load at / Deliver to / Cargo choosers, Apply and Delete, and the trucks on
  it), a **New route** draft with Create, and **Trucks**, each with status, cargo and a Route chooser (a route or Parked)
  with Assign, plus a Buy button for each truck offer. Truck offers are left out of the inventory screen's supplier window.
- The dock screen names each truck serving the dock through its route.

## Alternatives rejected

- **Keep the route on the truck and copy it to others**: several trucks would drift apart when one is edited; the GDD's
  "assigns vehicles" to a route implies a shared record.
- **Parking a driving truck finishes its trip first**: needs a new state; deferred with the position-between-sites work.
- **Trucks bought through the inventory screen's supplier window**: a truck never enters an inventory, and the logistics
  screen is where it is used.

## Open

Selling trucks, route names, schedules, running costs, dock tiers, and everything else 0022 leaves open.
