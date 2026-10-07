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
  seeded dock"): moving that dock would change the dev truck tests and old dev saves. Older dev saves keep their shell; the
  v19 upgrade gives it a back door (owner answers below).
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
| Doorsteps stay clear of object-layer pieces and belts | `RestaurantRules.DoorstepProblem`, `BeltProblem` |
| Where an automatic back door goes (inside walkable, then farthest from customer doors, then lowest cell) | `GoodsWorld.AddBackDoor` |

## Owner answers (2026-10-06, confirmed)

The three questions this record left open, plus one change the owner added:

6. **Automatic back door: yes.** A restaurant from a world made before generator v5 gets a back door automatically.
7. **Belts off doorsteps: yes.** Belts and lifts are kept off back-door doorsteps, like other object-layer pieces.
8. **Last customer door: yes.** Removing the last customer door is refused, the way removing the last back door is.
9. **Dock orientation.** Docks turn so the upright door frame stands against the wall (the owner asked for "rotated 180
   degrees"; see the implementation note below).

Implemented the same day:

- **Automatic back door (goods snapshot schema v19).** `GoodsWorld.AddBackDoor` gives a restaurant without a back door one,
  free (nothing charged, so removing it refunds nothing), in the kitchen style. It goes on an outer wall cell that may hold a
  door (not a corner, door or window), whose doorstep is open, uncovered ground on the lot that a walk from the street edge
  reaches. Preferred: a walkable cell inside the door, then the doorstep farthest from every customer door, then the lowest cell
  (X, then Z). If no cell qualifies, nothing changes and the owner can still draw one. It runs (a) when a lot listed without a
  back door (a layout made before generator v5) is bought, and (b) in the v18 to v19 save upgrade for every restaurant already
  owned, older dev saves included. The upgrade has no lot listing, so a doorstep reached from any lot edge counts. Nothing else
  moves; docks placed before the rule keep their "not beside a back door" mark unless the new door happens to be beside them.
- **Belts (`RestaurantRules.BeltProblem`, reason `doorstep`).** A belt, or a lift with either end on the ground, may not be
  placed on a back-door doorstep. The server's belt and lift placement and the client's belt and lift previews check it. A shell
  order may not give a back door a doorstep that a belt or object-layer piece already covers (`doorstep`), whether a new back door
  or a wall change that moves one; this also closes the matching gap for equipment. Belts already on a doorstep in an old save
  stay where they are (no validation failure); turning such a belt is still allowed.
- **Customer doors (`SiteGrid.CustomerDoors`, reason `no-customer-door`).** A customer door is a door on an outer wall (it has
  a doorstep) that is not a back door; a door in an interior wall is neither. A restaurant that has a customer door keeps one;
  the old `no-door` check still applies first.
- **Dock orientation.** The dock model's upright door frame (and bumper) is on its front, local +Z, which is the way rotation 0
  faces. The generator used to give the starter dock rotation 0 or 1 whatever side of the shell its yard was on, so the frame met
  the wall only for yards on the low side (seed `piece-two`'s start is one) and faced away on the high side. Turning every dock
  180 degrees would have broken the low-side ones, so instead: `WorldLayoutShells.ToOffer` now gives the starter dock the rotation
  that faces the shell (0-3), and build mode turns a dock round when that puts more wall in front of it
  (`BuildMode.FacingRotation`). Hand placement keeps the rotation the player picks. Stored layouts derive the new rotation for
  docks placed from now on; starter docks already placed in saves keep the rotation they were saved with.

## Verification (2026-10-06)

- EditMode: `FoodFactoryGame.World.EditModeTests` 28/28, `FoodFactoryGame.Goods.EditModeTests` 229/229 (including the new
  `BackDoorTests`, 5), `FoodFactoryGame.Session.EditModeTests` 124 passed and 1 skipped (the flag-gated P0 measurement),
  `CityCustomerBenchmarkTests` 2/2, `TruckRoadBenchmarkTests` 1/1.
- PlayMode: `FoodFactoryGame.Session.PlayModeTests`, final counts in [the record](../verification/back-door-20261006.md).
- Running game: a Pass I playthrough on `piece-two` (steps S1, S2, S11 of the P0 fixture). Build mode, driven with the virtual
  mouse, placed a dock beside the back door; 146 of 162 outdoor anchors were refused as `not-beside-back-door` and 5 were
  accepted. Captures are in `docs/verification/back-door-20261006/`.

## Verification of the owner answers (2026-10-06)

Unity 6000.5.9f1, Editor test runner (MCP `run_tests`), isolated temp saves only:

- EditMode `FoodFactoryGame.Goods.EditModeTests`: 232 matched, 232 passed. New in `BackDoorTests`: `TheLastCustomerDoorStays`,
  `BeltsStayOffDoorsteps`, `ARestaurantListedWithoutABackDoorGetsOne`; the v17 save test now also checks the v19 back door
  at (2, 6), and `RestaurantShellTests.V15SavesUpgrade...` expects the restaurant's new back door.
- EditMode `FoodFactoryGame.World.EditModeTests`: 28/28. `FoodFactoryGame.Session.EditModeTests`: 125 matched, 124 passed,
  1 skipped (the flag-gated P0 measurement). `GeneratedWorldCustomersTests.ANewWorldStartsWithItsBackDoorAndTheDockBesideIt` now
  checks that every listed restaurant dock in the city faces its shell, with all four rotations in use.
- PlayMode `FoodFactoryGame.Session.PlayModeTests`: 48 matched, 42 passed, 0 failed, 6 skipped (flag-gated captures and
  playthroughs). No new console errors.
- **Not yet verified in the running game:** the dock orientation. The rotation-to-model mapping (`SiteGridSpace.Rotation`, frame
  at the model's +Z) was checked with an Editor render of the prefab, but no running-game capture of a high-side yard exists
  yet. The independent visual review is still open.
