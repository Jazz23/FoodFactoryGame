# 0045 - Owner Feedback Round 4: Start Size, Camera Wheel, In-Game Menu, Worlds and Employees

Date: 2026-10-12

Status: **implemented; verified by domain and PlayMode tests (below). No running-game capture or independent visual review
yet.** The owner's requests are confirmed requirements. Everything marked PROTOTYPE is an implementation choice that stays open.

## Owner requests (confirmed, 2026-10-12)

1. The starting restaurant is too small.
2. Scrolling while the inventory is open also moves the camera; it should not.
3. An escape menu to reach settings in game and to quit back to the main menu.
4. "Select counter" (the "Work at counter" task) does not work. While picking anything for an employee task, every valid option
   gets a small light green tint seen through walls, and the option under the pointer gets a bold outline.
5. Scrolling does not move the camera while indoors.
6. A delete world option on the Select World screen.
7. Employees can be renamed.
8. Employees can be fired from their own screen.

## Implementation

- **Start size (1): generator v8.** `PickStart` takes the *largest* residential restaurant (then cheapest, then first) instead
  of the smallest, and `WorldSettings.StartMaxArea` goes from 400 to 480 cells, which admits every restaurant size (24 x 20 at
  most). `WorldSettings.StartLargest = false` with 400 cells reproduces v7, so the v5, v6 and v7 pins still hold. On the pinned
  seed the start grows from 16 x 14 (224 cells) to 24 x 20 (480). Only new worlds change: stored layouts are never regenerated.
  This replaces the GDD's "small, cheap restaurant shell" (section 3) and the locked "tiny restaurant" start (section 18) for
  generated worlds. The 0044 first-customer and benchmark measurements were taken on v7 starts and were not repeated.
- **Wheel on screens (2).** `OrbitCameraRig.ZoomEnabled` (set by `EquipmentInteraction` every frame) is true only with no
  screen open or while picking in the world; on every other screen the wheel scrolls the screen's lists. Build mode keeps its
  own zoom.
- **Wheel indoors (5).** The indoor top-down view already zoomed with the wheel (now covered by a test). In the third-person
  view indoors, walls and the ceiling hold the camera arm (about 1.7 m in a start restaurant) far inside its 3-20 m distance,
  so the wheel changed only the hidden distance. Now zooming in starts from the arm the walls allow, and the third-person
  distance may go down to 1 m (`minOrbitDistance`, PROTOTYPE). Zooming out still stops at the wall.
- **In-game menu (3).** Esc with no screen open and an empty cursor opens `InteractionScreen.Menu` (it used to release the
  pointer until the next click; that release mode is gone). Gameplay input is suspended under it, like the employee screen.
  `SessionPanel` shows its overlay (see-through, sorting order 50, above the HUD) with Resume, Settings (the main menu's
  settings screen; Back and Done return to the menu), Quit to Main Menu (`SessionRoot.Shutdown`) and Quit Game. Esc on the
  settings screen returns to the menu; the Esc that cancels a key rebind does nothing more. The session keeps running: nothing
  pauses (multiplayer server, decision 0002).
- **Pick highlight and Select counter (4).** WorldGen and DevSite had no `hoverOutlineMaterial` on `EquipmentInteraction`, so no
  hover outline or pick pulse was ever drawn in the shipped scene; both now use `EmployeeOutline.mat`. While picking a source,
  destination, machine or counter, every valid target gets `PickTint.mat` (new shader `FoodFactory/XRayTint`: unlit, no depth
  test, light green at 28% alpha, a stencil bit so a model is tinted once) and a thin green outline; the target under the
  pointer gets a bold, lighter outline (`PickHovered`). The red pulse is gone. The pointer picks the nearest valid target along
  its ray, even behind a wall. "Select counter" also accepts the register: the start restaurant has one, while a kitchen
  counter must be bought. `work(register)` walks there and takes the register over, as the register screen's Assign does,
  and returns 1. The employee stays assigned as it moves on to its next task.
- **Delete world (6).** The chosen world's details have Delete world. The first click turns it into "Click again to delete it
  for good". The second deletes the world's save folder (`SessionRoot.DeleteWorld`: never while a session runs, and only a
  folder `SavedWorlds` lists). Resume forgets a deleted world.
- **Rename (7).** Rename on the employee screen opens a name box; Save or Enter sends `EmployeeWorker.RequestRename`. The server
  checks the sender's grant and the name (trimmed, 1-32 characters, no control characters) and commits it
  (`GoodsWorld.RenameEmployeeDurably`) before the replicated name changes. A refused or failed rename changes nothing. No schema
  change: `GoodsEmployee.Name` was already saved.
- **Fire (8).** Fire on the employee screen asks for a second press, then sends the same request as the Staff window
  (`EquipmentInteraction.Fire`, decision 0039). The screen closes once the employee is gone. A refusal (hands not empty, busy)
  shows under the header.

## Verification (2026-10-12)

- EditMode: `FoodFactoryGame.World.EditModeTests` 40/40 (generator v8 re-pinned to `b9f41eb9…4643`; `GeneratorV8IsV7WithTheLargestStart`,
  `TheStartIsTheLargestResidentialRestaurant`), `FoodFactoryGame.Goods.EditModeTests` 276/276 (`RenameIsSavedAndABadOneChangesNothing`),
  `FoodFactoryGame.Session.EditModeTests` 144 passed, 3 on-request measurements skipped.
- PlayMode (WorldGen host, new world, virtual devices): `OwnerFeedbackRound4Tests` 5/5. They cover the wheel indoors (top-down and
  third person) and on the inventory, the Esc menu and Quit to Main Menu, Delete world, rename and fire, and the counter pick
  (the register tinted and picked by pointing).
- Not checked: a running-game capture of the tint and outlines, independent visual review, a multiplayer check of rename
  replication to a second client.

## Open

- PROTOTYPE: the tint colour and alpha, outline widths, the 1 m third-person minimum, and the largest-start rule.
- Whether a register worked from a task list should be left when the employee's script stops (today it stays assigned, like
  Assign).
