# 0010 - Conveyor Belts

Date: 2026-09-23

Status: requested by the project owner; implemented on the dev site. See
[architecture status](../architecture.md#implemented-conveyor-belts-2026-09-23).

## Context

The owner asked for a fully working conveyor belt system that mimics
Factorio:

- click and drag places belts, linked together;
- R rotates the belt direction; pressing R while dragging turns a corner and
  the drag carries on in the new direction. Follow-up request: R mid-drag
  over any belt does nothing; with the crosshair off to the side of the line
  it corners the head toward the crosshair and lays belts out to and
  including the crosshair's cell; R may be held through the drag;
- the moving arrows of all belts line up;
- belts do not connect to the oven;
- an item picked from the inventory stays on the pointer after the
  inventory closes; aiming at a belt shows a ghost of the item on it, and a
  key puts exactly one item (not the stack) on the belt. Follow-up request:
  Z puts an item on a belt and F takes one off.

Factorio's own behaviour (forum reports, since there is no design document):
dragging lays belts in the facing direction; pressing R while dragging turns
the belt under the cursor toward the mouse and the line continues that way.
Sources: [1](https://forums.factorio.com/viewtopic.php?p=666881),
[2](https://forums.factorio.com/viewtopic.php?p=702764),
[3](https://forums.factorio.com/viewtopic.php?t=78585).

## Decision

1. **A belt is made from an item.** `belt` is an ordinary goods item
   (`ItemDefinition`, stack 100). Placing a belt consumes one from the
   player's inventory; removing it returns one, together with every item
   riding it, all-or-nothing against the inventory's free slots. Belts are
   never destroyed, so no belt can be lost or duplicated by placement.
2. **A placed belt is server state.** `GoodsBelt { Id, SiteId, CellX, CellZ,
   Direction }` occupies one grid cell, shares the grid with equipment
   (neither may overlap the other; `SiteGrid.CellProblem`) and owns one
   location `<id>:items` (kind `belt`). Its identity is stable: turning a belt
   keeps its ID. Snapshot schema **v4** adds `Belts` and
   `GoodsLot.BeltPosition`; v3 saves upgrade in memory.
3. **Items ride as single units.** A lot on a belt is one unit at an integer
   path position (`BeltRules.UnitsPerTile` = 240 per belt, whatever its
   shape). Goods enter a belt only with `PlaceOnBelt` (one unit at the free
   spot nearest the middle; `belt-full` otherwise) and leave with
   `TakeFromBelt` (one riding item, whatever belt it has moved to, into the
   taker's inventory under the same ID; `not-on-belt`, `capacity`) or when
   the belt is removed. `Transfer` refuses belt locations (`invalid-route`) and
   `Merge` never merges riding lots. Belts never feed or take from
   equipment.
4. **Belts move on the world clock.** Every `Advance` moves riding items at
   one tile per second in 8 sub-steps per second, processing belts
   downstream first and each belt's items front first. Items keep 60 units
   (a quarter tile) apart, across belt boundaries too, queue at the end of a
   line (resting half a gap before its edge), and survive save and reload
   where they were. A belt line runs with nobody watching, like the oven.
5. **Shapes and links follow Factorio.** A belt fed only from one side curves
   from that side; fed from behind, both sides or nowhere, it is straight.
   A belt hands items to the belt in front unless that belt faces back into
   it. Entering the back of a belt, or a curve from its curving side, starts
   at 0; entering the side of any other belt side-loads at its middle
   (waiting at the edge until the middle is clear). `BeltRules` holds these
   rules for both the server and the client previews.
6. **Placement is Factorio's drag.** With belts on the cursor, the ghost shows
   the cell under the crosshair in the cursor direction and the shape it
   would take. Pressing Place starts a drag; belts are laid forward along the
   direction, filling every cell the crosshair skips, never sideways or
   backwards. R while dragging turns the head belt toward the side the
   crosshair is on (clockwise when it is straight ahead), which makes a
   corner, and the drag continues in the new direction. Dragging over an
   existing belt only turns it, at no cost. R without a drag turns the cursor
   direction; with an empty cursor it turns the belt under the crosshair.
   Holding Remove takes up every belt the crosshair passes over.
7. **Each belt is one request.** A drag sends one `RequestPlaceBelt` per
   cell. Pending cells are drawn as ghosts until the baseline shows them or
   the server refuses; the server re-checks everything.
8. **Arrows share one clock.** The belt models map half a UV unit to one
   tile of travel on straights and corners alike. `BeltPresenter` gives every
   belt one runtime tread material whose texture offset is
   `-(time × 0.5 UV/s)`, so every arrow lines up and moves at the item
   speed. Riding items are drawn as icon sprites that glide along the belt
   path toward their latest replicated position at belt speed, trailing the
   server by up to one clock step.
9. **The cursor keeps inventory stacks.** Closing a screen keeps a stack
   picked from the player's inventory on the cursor (a stack from another
   container is still dropped). Outside a screen its icon and count sit
   beside the crosshair. Belts on the cursor build; any other item shows its
   ghost on the aimed belt, and `Player/PlaceItem` (Z) puts one unit on it.
   `Player/TakeItem` (F), whatever the cursor holds, takes the riding item
   nearest the crosshair on the aimed belt (by its latest replicated
   position) into the inventory.

## Consequences

- Dev content: new dev worlds get 200 belts in storage; new inventories get
  50 belts with their starter dough. A save from before belts (no belt items
  and no placed belts anywhere) receives the 200 storage belts once on the
  next server start, if the storage has room (`DevWorld.EnsureBeltStock`).
- Each placed belt and item is a durable command with a full-baseline
  broadcast, and every clock step saves; long drags and busy belts are fine
  on the dev site but are not a scalable replication model.
- Belts have one lane. Factorio's two lanes, belt tiers, undergrounds,
  splitters, inserters, and belts feeding or emptying machines are not
  implemented.
- Items are drawn trailing the simulation by up to one second; placement
  ghosts are client previews only.
- Belt prefabs use a trigger collider, so players walk through belts.
