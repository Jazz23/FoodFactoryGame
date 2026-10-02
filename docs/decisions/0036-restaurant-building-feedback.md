# 0036 - Restaurant Building: Owner Feedback Round 1

Date: 2026-10-02

Status: **implemented; verified in part ([record](../verification/restaurant-feedback-20261002.md)).** Owner feedback on the restaurant building slices
of [0034](0034-restaurant-building.md) / [0035](0035-restaurant-building-implementation.md). The owner's requests are confirmed
requirements; everything marked PROTOTYPE is an implementation choice that stays open.

## Owner requests (confirmed, 2026-10-02)

1. Wall clocks must not face the wrong way when placed on a wall.
2. Doors open automatically when someone approaches and have a hitbox (you bump into a closed door).
3. Outside the top-down view, the ceiling is visible from inside.
4. In build mode, Cancel clears the cursor's selection, and so does X; all input goes through Input System actions.
5. Sinks stand against the wall.
6. Restaurants are larger.
7. No "Resize shell": outer walls are placed and removed like interior walls. (This replaces the 0034 rule "the player resizes
   the rectangular shell"; restaurants stay inside their purchased lot.)
8. In build mode WASD pans, the wheel zooms, and holding the right mouse button tilts the camera.
9. In a small room the third-person camera moves in with the player instead of looking in from outside.
10. Customers walk in through the front door (it opens for them unless they are seated), never through walls.
11. Distant objects are not drawn (LOD); machines, trucks and so on keep working.
12. Working or leaving a register updates its screen immediately.
13. The ceiling panel is removed from the catalog.

## Implementation

- **Free walls (7).** `GoodsBuilding.FreeWalls` (goods snapshot schema **v17**; v16 loads with `false`). The first shell order on
  a rectangular restaurant (anything but a legacy resize) converts it (`GoodsWorld.ToFreeWalls`): every perimeter cell becomes a
  `partition` wall record in the outer finish, keeping what a paid perimeter wall cost; perimeter doors become door records
  (unpaid ones record 0); paid floor records are refunded by that order, since the footprint now follows the walls. Afterwards
  walls go anywhere in the lot that has no wall (`invalid-cell` otherwise), the footprint is the walls' bounding box, and the
  interior is what the walls enclose (`SiteGrid.IsInterior`: cells a four-way walk from outside the box cannot reach without
  crossing a wall, door or window cell; cached per building object). An open shell has no interior. A shell needs one wall
  (`no-walls`) and one door (`no-door`); a door may not stand on a corner or junction (`invalid-cell`). Wall finish restyles the
  named walls (all walls when none are named), still free. `Resize` is rejected on free walls (`invalid-order`) and is no longer
  offered by build mode. Exact refunds and the "cash plus recorded charges never changes" ledger hold as before.
- **Bigger restaurants (6).** Generated restaurants are 16-24 x 14-20 cells (were 9-16 x 8-14) and the starting restaurant may be
  up to 400 cells (was 160); `WorldGenerator.Version` 4. Existing saved worlds keep their stored layouts. Fewer restaurants fit a
  city (seed tests: 178 competitors, was over 250). The dev site's 11x9 test restaurant is unchanged (it is a 20x20 test fixture);
  its owner can now draw it larger with free walls.
- **Ceiling (3).** Every one-storey shell has a plaster ceiling just under its walls' tops over its interior, shown only to a local
  avatar inside it who is not in the top-down view (`BuildingPresenter.CeilingVisible`). Free-walled shells draw their floor
  tint, roof and ceiling over the enclosed cells.
- **Camera (8, 9).** `OrbitCameraRig`: the third-person arm sphere-casts from the avatar and stops in front of any collider at
  least 1.5 m tall (not avatars or furniture), the shoulder pivot does too, and indoors the camera stays 0.25 m under the ceiling;
  it eases back out over 0.25 s. In build mode Player/Move pans (scaled by distance), Player/Zoom zooms (4-90 m) and the new
  Player/CameraTilt (right mouse) held with Look tilts (30-90 degrees) and turns; the avatar does not walk while building.
- **Build-mode input (4).** A "nothing selected" tool; the Cancel button and Player/ClearCursor (X) clear the tool or item. A right
  click removes on release only if the pointer moved at most 8 px; a right drag tilts instead. No input bypasses the action maps.
- **Facing (1, 5).** Wall decor is placed facing away from its wall (into a room if it can). A new `EquipmentMount.Backed` (the
  sink) stands at the back of its footprint, is turned round when that puts a wall behind it, and is pushed onto the wall's face
  when every cell behind it is a wall.
- **Doors (2, 10).** Each kit door leaf has a `DoorSwing`: it opens (95 degrees, away from the opener) while a `DoorOpener` is within
  1.8 m of the doorway and closes after; while nearly shut it has a box collider. Openers: every player avatar, employees, and
  customer figures while walking (not standing or seated). Presentation only.
- **Customers (10).** Site figures route over the site's walkable cells (`SiteWalk`, the same walk rule as the seat rule), so
  they enter by a door; the register's service spot is the reachable cell beside it (the side it faces first) and the queue runs
  back along the way in. Where no cell route exists the NavMesh is used, and an incomplete NavMesh path is no longer replaced by
  a straight line through walls.
- **LOD (11).** `DistanceCulling` puts one Unity LOD level on equipment, belts and riding goods, trucks, customers, employees
  (cull below 1.2% of screen height, roughly 70 m for a 1 m piece) and building shells (0.6%). Only rendering stops.
- **Register screen (12).** The HUD's rebuild signature now includes the open register's staff and pending state, and the row
  shows "Updating..." with the button disabled while the request is out.
- **Ceiling panel (13).** Its offer, definition, prefab and icon are deleted; the kit model stays in `Assets/Art` (the content test
  lists it as withdrawn). A saved ceiling panel no longer has a definition, so it is not drawn (a warning is logged) but can still be
  sold by its ID.

## PROTOTYPE values (open)

Door reach 1.8 m, swing 300 deg/s, blocking below 35 degrees; camera wall height 1.5 m, cast radius 0.2 m, ceiling gap 0.25 m;
build zoom 4-90 m, pitch 30-90 degrees, pan 12 m/s at 20 m; right-click slop 8 px; LOD cull heights 1.2% and 0.6%; restaurant
size ranges and start area; free floor area (no per-floor-cell price with free walls).

## Owner questions

1. With free walls the floor-cell price of 0035 no longer applies (only walls, doors and windows cost). Keep it that way?
2. Should the dev test restaurant also be enlarged?
