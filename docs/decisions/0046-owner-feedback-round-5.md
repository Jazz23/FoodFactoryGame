# 0046 - Owner Feedback Round 5: Rename Box, Double Doors, Shelf Boxes, Back Door Paths

Date: 2026-10-12

Status: **implemented; verified by EditMode and PlayMode tests (below). No running-game capture or independent visual review
yet.** The owner's requests are confirmed requirements. Values marked PROTOTYPE stay open.

## Owner requests (confirmed, 2026-10-12)

1. Renaming an employee is broken: clicking the name box after Rename shows no caret, and the name is not saved.
2. There is a gap between the two leaves of a double (swing) door.
3. Storage shelves show more and more boxes as their inventory slots fill (models made with the Blender CLI).
4. An employee does not take the back door to the loading dock, although it is the shortest path.

## Causes and changes

- **Rename (1).** The employee window was centred with `ff-centered` (a percentage `translate`). UI Toolkit keeps drawing such
  a window at its old size while picking uses the new one (the trap `Theme.uss` documents, starting-loop finding P0-01).
  Opening the rename row grows the window by about 41 px, so a click on the drawn name box landed on the header behind it. The
  box never took focus, so it showed no caret and typing went nowhere, and Save sent the unchanged name. The window is now
  centred by its layer (`ff-centering`), like Logistics. The server path was already correct.
- **Double doors (2).** A kit leaf is 0.95 m from hinge to free end, sized for the single frame. The double frame's hinges
  stood at +-0.995 m, inside the jambs (inner faces at +-0.98 m), so the shut leaves left a 9 cm gap. The hinges now sit 5 mm in
  from the jamb (+-0.975 m, as the single frame does) and each double leaf is stretched 2.4% along its width
  (`RestaurantShellModel.DoubleLeafScale`), so the free ends meet 3 mm short of the centre. Single doors are unchanged.
- **Shelf boxes (3).** `ArtSource/Restaurant/build_shelf_boxes.py` builds eight cardboard box stacks (`Boxes_1` .. `Boxes_8`, two
  per deck, bottom deck first, each inside its deck's headroom) and exports `RT_StorageShelf_Boxes.fbx`. Two new kit
  materials, `RT_Cardboard` and `RT_Tape`, are used, and labels reuse `RT_Ivory`. `AgentScripts/InstallShelfBoxes.cs` puts the
  model under the shelf prefab with every stack inactive, plus a `StorageFillDisplay` whose stages are the stacks.
  `EquipmentPresenter` passes each visual the slots its input buffer's stacks take (`GoodsSlots.SlotsUsed`) out of that
  buffer's capacity (`IEquipmentFillDisplay`). The display shows ceil(used x stages / capacity) stacks, so any stored goods
  show at least one stack and a full shelf shows all eight. This is presentation only: the shelf's 8 slots and the goods rules
  are unchanged. The trolley and prep table also store goods but get no boxes (not requested).
- **Back door (4).** Agent type 0 (Humanoid) had a 0.5 m radius. Walls are whole-cell NavMesh sources (generated lots) and
  whole-cell carving obstacles (every scene). Eroding 0.5 m from each wall end closed a 1 m single doorway completely, while
  the 2 m front door kept a 1 m lane. On the pinned start (`piece-two`) the back door cell had no NavMesh, so the walk from the
  restaurant's middle to the back doorstep was 40.2 m around through the front door instead of 14.4 m. The radius is now 0.3 m
  (PROTOTYPE), which leaves a 0.4 m lane in a single doorway. Runtime lot NavMeshes and carving use it at once; the DevSite
  NavMesh (shared with WorldGen) and the SampleScene NavMesh were re-baked. `EmployeeWorker`'s own agent radius (0.35 m) is
  unchanged: it sets avoidance only, and the real walls are thin on the cell centreline, so figures do not clip them.

## Verification (2026-10-12)

- Before the fixes, the new checks failed: the name box was not under the pointer (the header was picked), the double door's shut
  leaves had a 0.090 m gap, and the back door cell was off the NavMesh (scratch repro, 40.2 m path).
- EditMode `RestaurantContentTests` 3/3 (`AStorageShelfShowsAStackOfBoxesPerUsedSlot`; the kit count is now 60).
- PlayMode `OwnerFeedbackRound3Tests` 7/7 (`TheNameBoxTakesAClickAndTheTypedNameIsSaved`, `EmployeesCanWalkThroughTheBackDoor`),
  `BuildingPresenterTests` 2/2 (double-door gap check). That test teleported the avatar onto the door cell, into the plane of a
  shut leaf. The moved hinge changed which way the controller was pushed out, so the test now waits for the leaves to open
  before standing on the threshold. A player never stands inside a shut leaf: doors open within 1.8 m.
- Not checked: a running-game capture of the shelf boxes, the doors and the rename box; independent visual review.

## Open

- PROTOTYPE: the 0.3 m agent radius, the box stack shapes and the ceil fill rule.
- Whether the trolley and prep table should show their contents too.
