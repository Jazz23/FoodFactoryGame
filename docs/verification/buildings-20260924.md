# Verification: building shells and the indoor camera (2026-09-24)

Decision: [0019](../decisions/0019-building-shells-and-indoor-camera.md).

Compilation: live Editor `recompile`, no errors; console clear of new errors. Authoring: `run_script AgentScripts/BuildDevSite.cs`
(`BuildDevSite.Run` → "DevSite authored"). As in earlier runs, it also zeroed the FishNet `AssetPathHash` of
`GoodsNetworkBridge.prefab` and `Player.prefab` and changed the blend of `EquipmentGhost.mat`. These are unrelated to this
feature, so the three files were restored from git.

Before choosing the dev shell's cells, the owner's application save was read only (`GoodsSnapshotStore.Load(...).Snapshot()`
in the Editor, no save). Its placed equipment sits at z ≥ 13 and it has no belts, so the shell at (9..19, 0..8) fits it.
`EnsureBuilding` will add the shell there on the next server start.

| Run | Filter | Matched | Result | Artifact |
| --- | --- | --- | --- | --- |
| Live Editor `run_tests` editor, final domain code | none (all EditMode) | 164 | 164 passed | (not kept) |
| Live Editor `run_tests` editor, after the authoring-test change | `DevSiteIsTheOnlyBuildSceneAndIsWiredForSessions` | 1 | 1 passed | (not kept) |
| Live Editor `run_tests` playmode, async, first run | none (all PlayMode) | 16 | 14 passed, 2 failed | (not kept) |
| Live Editor `run_tests` playmode, async, baseline with all changes stashed | `HotbarKeysOverAStackAndDropsOnTheHotbarAssignSlots` | 1 | 1 failed (same message) | (not kept) |
| Live Editor `run_tests` playmode, async, final code | none (all PlayMode) | 16 | 16 passed | (not kept) |
| Live Editor host in `DevSite` via `SessionRoot.Configure` with an isolated save and identity under the system temp folder (not the application save; deleted afterwards) | n/a | n/a | spawn outdoors in the orbit view with the shell's roof visible; avatar moved to cell (14, 4): top-down view, roof hidden, walls and north doorway visible | [outdoor.png](buildings-20260924/outdoor.png), [indoor.png](buildings-20260924/indoor.png) |

First PlayMode run failures:

- `BuildingPresenterTests` read collider bounds in the same frame the shell was built, before physics synced its transforms,
  so a point inside the west wall was reported outside. The test now waits a frame and calls `Physics.SyncTransforms()`.
  The presenter was not changed.
- `HotbarKeysOverAStackAndDropsOnTheHotbarAssignSlots` is the known hover test (see [spoilage](spoilage-20260924.md)). It
  failed the same way on the committed code with every change stashed, and passed in the final full run. It depends on
  the environment and is not related to buildings.

Running-game observations: the orbit capture shows the dark roof and plaster walls beside the spawn. The top-down capture
shows the open doorway in the north wall and the interior floor with wall shadows. The brown strip at the right edge of
`indoor.png` is the HUD's cash panel overlay; it is absent from a camera-only render.

New or changed coverage:

- `BuildingTests` (Goods, 6, new):
  - walls block equipment and belts while the interior and doorways stay usable, and only on their own site;
  - the grid helpers distinguish walls, doors and interior;
  - bootstrap rejects 10 malformed or overlapping shells and walls over equipment or belts, and changes nothing when it
    does;
  - recovery rejects equipment on a wall, duplicate shells, a corner door, and a missing list;
  - shells survive save and appear only in their own site's view;
  - a v6 row loads as v7 with no buildings.
- `DevWorldBuildingTests` (Session, 3, new):
  - a new world has the shell clear of the seeded oven and counter and of all four scene spawn points;
  - an older save gains the shell once, committed;
  - an older save with equipment on a wall cell keeps it and gets a warning instead.
- `SessionAuthoringTests` (1 changed): `DevSite` has one `BuildingPresenter` with its session and three materials.
- `BuildingPresenterTests` (PlayMode, 1, new): in a real DevSite host session:
  - the shell has 5 solid wall runs, the doorway is open, and only walls have colliders;
  - the avatar spawns outdoors in the orbit view;
  - inside, the view is top-down and the roof is hidden;
  - an override to orbit holds while the avatar moves within the building;
  - the doorway is not indoors;
  - re-entering switches back to top-down, and walking out restores orbit and the roof.

Not verified: a player build; a two-process session; an avatar actually walking into a wall with the keyboard (the
colliders are asserted, and movement was set directly); the C key press (the override used the rig's private switch).
