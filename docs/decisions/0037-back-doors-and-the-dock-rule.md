# 0037 - Back Doors and the Restaurant Dock Rule

Date: 2026-10-06

Status: **implemented; verified in part** (piece P1 of [the starting-loop plan](../starting-loop-plan.md)). Evidence: the test runs
below and [running-game captures](../verification/back-door-20261006/). An independent visual review is still open. Values
marked PROTOTYPE are open.

## Owner decisions (confirmed)

From 2026-10-06, already in the GDD (sections 5, 7 and 9):

1. Every generated restaurant comes with a back door on a wall away from the street. The player may move or remove it with
   free walls, but the restaurant keeps at least one back door.
2. Every dock stands outside the shell next to a back door and stays reachable from the street. The building comes with one dock
   already there. This replaces 0034's "any number, anywhere in the lot".
3. Back doors are for staff and goods only. Customers enter and leave by customer (front) doors only. Seat and register
   reachability counts only customer doors.

Accepted on 2026-10-06 ("implement P1 with your recommendations"), from the plan's owner decisions:

4. **Where the dock fits (plan decision 1): a side service yard.** Every restaurant lot gets a strip beside the shell, from the
   street edge to the building's rear, with the back door on that side.
5. **Existing saves (plan decision 2): keep and mark.** Docks placed before this rule keep working where they stand, and the
   dock screen marks them "not beside a back door". New docks follow the rule. Nothing is moved or deleted.

## Implementation

- **Door role (goods snapshot schema v18).** `GoodsStructure.Role`: empty is a customer door, `GoodsWorld.ServiceDoorRole`
  (`"service"`) a back door. A back door is a door record on an outer wall: it must have a doorstep (`SiteGrid.Doorstep`, the
  neighbour across the wall that is not interior). While a free-walled shell is open, it has no interior; then the doorstep is
  the side outside the footprint, so taking an outer wall out and redrawing it keeps the back door. On a rectangular shell the back door
  is also in `Doors` (an opening), as perimeter doors always were. `SiteGrid.IsServiceDoor`, `ServiceDoors`. The v17 to v18
  upgrade is in memory: every door record reads as a customer door.
- **Shell orders.** New order `backdoor` (`ShellOrder.BackDoor`): a door (PROTOTYPE price = door price) on a wall with a
  doorstep on the lot (`invalid-cell` otherwise, for example on a wall between two rooms). Two guards, checked after any order:
  - `no-back-door`: a restaurant that had a back door must still have one. Moving the last one works as "place the new one,
    then remove the old one".
  - `dock-attached`: a placed dock that stood beside a back door must still do so. Its back door stays, and its doorstep stays
    open ground.
  When both apply (removing the only back door, with a dock beside it), `no-back-door` is reported. Refunds stay exact: cash
  plus recorded charges never changes.
- **Dock rule (`RestaurantRules.PlacementProblem` = `DoorstepProblem` then `DockProblem`).** Restaurant sites only. A dock
  must lie wholly outside every interior and touch, edge to edge without covering it, the open doorstep of a back door
  (`not-beside-back-door`). The 0034 street rule still applies after that (`no-street-access`). Any other object-layer piece may
  not cover a back-door doorstep (`doorstep`). Docks on other sites keep the 0022/0023 rules. The same rules run in the server's
  place and buy-and-place, the build-mode preview and the hand-placement ghost. Truck service is unchanged: a dock placed before
  this rule keeps serving trucks.
- **Customers.** For customers, `RestaurantRules.Walkable(..., customers: true)` treats back doors as walls. The seat and
  register search (`Diners`) now starts only from the lot's street edge (`StreetCells`; every edge of a dev site). Players and
  employees are unaffected. Customer figures route the same way (`SiteWalk`) and never open a back door (`DoorOpener.Customer`,
  `DoorSwing` service-only leaves).
- **Generator v5, layout format 4.** Every restaurant lot is its footprint plus the street apron plus a service yard beside the
  shell (PROTOTYPE `WorldSettings.ServiceYardWidth` = 4 cells), on a seeded side. The yard runs from the lot's street edge to the
  building's rear. The back door is the yard-side wall cell two cells in from the rear corner. The starter dock (2 x 1, along the
  yard) touches its doorstep on the street side. `WorldBuilding.ServiceYard`, `BackDoor`, `ServiceDock`; a `service` line per
  restaurant in the layout text; validator checks (yard in the lot and beside the shell, reaching the street edge; back door on
  the yard side; dock in the yard touching the doorstep). Fewer restaurants fit a city: seed `piece-two` has 157 competitors
  (178 with v4), `p0-second` 138 (166). Stored worlds keep their layouts, so older worlds have no yard.
- **Property.** `PropertyOffer.BackDoors`, `HasDock`, `DockX/Z/Rotation` come from the layout. A site's shell is created with its
  back door (kitchen leaf, nothing charged). A bought restaurant gets its dock (`dock:<site>`) free, from the supplier's dock
  content, so selling it refunds nothing. A new world's starting restaurant gets `start-dock`, only when the world is created,
  so a sold starter dock is never re-added.
- **Dev site.** The dev restaurant gets a back door on its west wall at (9, 4) (doorstep (8, 4)), so dev docks can be placed. Its
  seeded dock at (0-1, 18) predates the rule and keeps working. This deviates from the plan's wording ("a back door beside its
  seeded dock"): moving that dock would change the dev truck tests and old dev saves. Older dev saves keep their shell and have no
  back door until the owner adds one.
- **Presentation.** Build mode has a Back door tool (door styles, kitchen leaf by default). The dock screen marks a restaurant dock
  that is not beside a back door. The hand-placement ghost reports the restaurant rules. Back doors use the art kit's kitchen
  door; **the planned "staff" sign is not made** (art, deferred).

## PROTOTYPE values (open)

| Value | Where |
|---|---|
| Service yard 4 cells wide, on a seeded side | `WorldSettings.ServiceYardWidth`, `WorldGenerator` |
| Back door two cells from the rear corner; starter dock beside it toward the street | `WorldGeometry.AddServiceYard` |
| Back door price = door price ($150 + order fee, district-scaled) | `GoodsWorld.ShellDoorCents` |
| A bought restaurant's dock is free and refunds nothing | `GoodsWorld.PlaceStarterDock` |
| Doorsteps stay clear of object-layer pieces; belts are not checked | `RestaurantRules.DoorstepProblem` |

## Owner questions

1. A restaurant bought in a world created before generator v5 has no back door, so no new dock can be placed there until
   the owner draws one. Should such restaurants get a back door automatically?
2. Should belts be kept off back-door doorsteps too?
3. Should removing the last customer door be refused, as removing the last back door is? Today a shell needs at least one
   door of any kind (`no-door`).

## Verification (2026-10-06)

- EditMode: `FoodFactoryGame.World.EditModeTests` 28/28, `FoodFactoryGame.Goods.EditModeTests` 229/229 (including the new
  `BackDoorTests`, 5), `FoodFactoryGame.Session.EditModeTests` 124 passed and 1 skipped (the flag-gated P0 measurement),
  `CityCustomerBenchmarkTests` 2/2, `TruckRoadBenchmarkTests` 1/1.
- PlayMode: `FoodFactoryGame.Session.PlayModeTests`, final counts in [the record](../verification/back-door-20261006.md).
- Running game: a Pass I playthrough on `piece-two` (steps S1, S2, S11 of the P0 fixture). Build mode, driven with the virtual
  mouse, placed a dock beside the back door; 146 of 162 outdoor anchors were refused as `not-beside-back-door` and 5 were
  accepted. Captures are in `docs/verification/back-door-20261006/`.
