# 0038 - Owner Feedback Round 2: Build Input, Script Screen, Short IDs, Power Override

Date: 2026-10-07

Status: **implemented; verified by domain and PlayMode tests (below). No running-game capture yet.** The owner's requests are
confirmed requirements. Everything marked PROTOTYPE is an implementation choice that stays open.

## Owner requests (confirmed, 2026-10-07)

1. When deconstructing, a small wheel appears at the cursor while the right mouse button is held for 1.5 s.
2. Placing has no Confirm button: releasing the mouse button places.
3. Holding right click on a wall clock must delete it. Deconstruction targets the object the cursor points at, not the floor
   cell under the cursor.
4. The employee screen's Lua source tab scrolls, and moving the caret down scrolls with it.
5. Machine IDs used in employee scripts are shorter.
6. The machine power switch must work: off pauses production where it is, and on resumes it.

Follow-up (confirmed, 2026-10-07): right-clicking on nothing shows no wheel, and the hold lasts 0.75 s (replacing 1.5 s).

## Implementation

- **Hold to remove (1, 3).** In build mode, Remove (right mouse) held still for `BuildMode.HoldSeconds` (0.75 s) sells or
  removes; a ring at the pointer (`build-hold-wheel`, UI Toolkit `Painter2D`) fills clockwise meanwhile. Releasing early does
  nothing. A press on nothing removable starts no hold and shows no wheel. Moving the pointer more than 8 px cancels it, and the right drag tilts the camera as before (decision 0036). The
  target is the placed piece whose collider is under the pointer (`BuildMode.PointedPiece`, trigger colliders included). A piece
  counts if it is hit before anything solid, or if it lies inside the first solid collider and faces the camera. Wall decor
  hangs inside its wall cell's 1 m collider box. Without a pointed piece, the target cell is the cell of the first solid surface
  under the pointer (`BuildMode.PointedCell`), so a wall, door or window is removed by pointing anywhere on it, not only at its
  base (follow-up 2026-10-07; the floor point behind a wall used to be taken). That cell's door, window, wall, or tabletop,
  object or floor-layer piece is used. Wall and ceiling decor is never chosen by cell. The Sell tool's left click uses the same
  target.
- **No Confirm (2).** A drag (wall line, wall finish, area of pieces) is ordered on release, like a click. Esc during a drag
  drops it. `Player/BuildConfirm` is no longer read by build mode; the action stays in the asset, unused. `BuildMode.PreviewDrag`
  returns a drag's preview without ordering it.
- **Lua tab (4).** The source text box keeps the page height and scrolls vertically (`ScrollerVisibility.Auto`). UI Toolkit
  scrolls it to keep the caret in view.
- **Short IDs (5).** New placed or bought equipment is named `<kind>-<n>` (`rt-wall-clock-3`, `oven-12`).
  `GoodsSnapshot.NextPieceNumber` is a world-wide counter that is never reused. IDs already in use by a piece, station or truck
  are skipped. Pieces that are already saved keep their `buy:<player>:<request>[:<n>]` IDs. Trucks and bought goods lots keep
  request-derived IDs. Replays still return the first outcome, so a retry never makes a second piece. No schema version change:
  an older save reads the counter as 0.
  Because a client can no longer derive the new ID from its request, the result RPC (`GoodsNetworkBridge.TargetResult`) now
  carries `GoodsOutcome.EquipmentId` to the requesting client.
- **Power (6).** The switch, the pause and the resume already worked. The Editor log of the owner's session showed the cause:
  the employee's "Turn oven on" task switched the oven back on seconds after each player switch-off. A player's switch-off now
  holds (`GoodsEquipment.HeldOff`, saved). An employee's switch-on is refused with `held-off`, and `turn_on`/`toggle` fail
  before walking there ("switched off by a player"). A player's switch-on clears the hold, as do a pickup or a sale.
  An employee's switch-off holds nothing. PROTOTYPE: whether a player's switch-on should also be needed after a restart, and
  whether the task should say so in the employee screen.

## Verification (2026-10-07)

- EditMode `FoodFactoryGame.Goods.EditModeTests` 231/231, including `PowerTests.APlayersSwitchOffHoldsAgainstEmployees` and the
  short-ID assertions in `RestaurantFurnishingTests`. `FoodFactoryGame.Session.EditModeTests` 126/126.
- PlayMode: `EquipmentPlacementTests.PowerSwitchClickPausesAndResumesABatch` (the switch clicked through UI Toolkit pointer
  events; a paused batch keeps its remaining time; it resumes). `RestaurantBuildingSessionTests.HoldingRemoveOnAWallClockSellsItOnceTheWheelFills`
  (a virtual mouse on the clock; an early release sells nothing; a full hold sells it for its price; short ID).
  `ScreenOpeningTests.LongLuaScrollsAndFollowsTheCaret`.
- PlayMode `FoodFactoryGame.Goods.PlayModeTests` 2/2. `FoodFactoryGame.Session.PlayModeTests` 45 of 48 passed, 2 were skipped
  (captures run only on request) and 1 failed:
  `WorldGenSessionTests.ATruckDrivesTheGeneratedRoadsWhereItIsDrawn` (7 drawn samples, 8 required). It fails the same way
  with these changes stashed, so the failure predates them.
- Not checked: a running-game capture of the wheel and the scrolled Lua tab.
