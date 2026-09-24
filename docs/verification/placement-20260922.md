# Equipment placement verification — 2026-09-22

Branch `placement` (from the unmerged `session-bootstrap`). Editor: Unity 6000.5.9f1, project `E:\Projects\Unity\FoodFactoryGame`, driven through Unity CLI MCP. Decision: [0006](../decisions/0006-equipment-placement-and-inventory.md). As in the session record, the Pipeline returns no native run ID, so EditMode runs are identified by the NUnit `test-run` start time in the XML Unity writes. Each XML was copied right after its run.

## Final runs (code as committed)

| Run identity | Requested filter / mode | Matched | Passed | Failed | Skipped | Artifact |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| NUnit test-run id 2, start 2026-09-23 04:51:51Z | `FoodFactoryGame` (test name) / editor (async) | 67 | 67 | 0 | 0 | [`artifacts/placement-20260922-editmode.xml`](artifacts/placement-20260922-editmode.xml) |
| NUnit test-run id 2, start 2026-09-23 04:52:09Z | `FoodFactoryGame` (test name) / playmode (async) | 5 | 5 | 0 | 0 | [`artifacts/placement-20260922-playmode.xml`](artifacts/placement-20260922-playmode.xml) |

The EditMode run covers Goods 40 (the 32 existing tests, with the station pickup tests migrated, plus 8 new `EquipmentTests`), Session 23 (one new authoring test) and Baseline 4. The PlayMode run covers the Goods listen-server test, both `SessionBootstrapTests` and the two new `EquipmentPlacementTests`.

`recompile`: completed, `compilationFailed: false`; the only compiler warnings are the two existing FishNet `CS0618` warnings. Expected test-time errors are the `SpawnablePrefabs is null on …-remote/host` lines declared with `LogAssert.Expect`. Every `BuildDevSite` run and asset import logs `Assets … GoodsNetworkBridge … and … Player … have the same assetPath hash of 0` from FishNet's `DefaultPrefabObjects` generator. It names the unchanged session prefabs, and I did not establish whether it predates this branch; it does not affect `GamePrefabs`, which `DevSite` uses.

## Coverage

EditMode, `EquipmentTests` (test-only 6×5 site, 3×3 oven, 2×1 counter, temp saves):
- Pickup then placement elsewhere keeps the equipment ID. The station comes back with the same `<id>:in`/`<id>:out` IDs, capacities and refrigeration. Held equipment is visible in another player's view. Replaying the place request returns the original outcome.
- Rejections change nothing: `forbidden` (no grant, unknown ID), `not-held` (placed piece, someone else's piece), `not-placed`, `blocked`, `out-of-bounds` (including negative), `invalid-rotation`.
- Footprints swap width and depth for odd rotations; a rotated 2×1 fits only when turned and blocks overlapping placements.
- A failed save rolls back both pickup and placement; held state survives reload and replay after recovery.
- Admission creates the inventory once (no rewrite when present), backfills a pre-existing grant, and rolls back on a failed save.
- A v2 save loads as v3 with no equipment and is written as v3.
- `Validate` rejects overlapping and out-of-bounds footprints, duplicate equipment, a station without equipment, held equipment that still has a station, a placed piece with a holder, a missing layout, and a held piece without a holder.

EditMode, `StationJobTests` (migrated to equipment-backed stations): pickup of a running job refunds inputs with frozen exposure and sweeps both buffers (lot IDs kept). Pickup of a blocked job hands over the output and sweeps the filler. `capacity`, `forbidden`, `no-inventory` and `reserved` leave goods untouched; durable pickup survives reload and a failed save rolls back. v1 loads as v3.

EditMode, `SessionAuthoringTests`: `DevSite` has one `EquipmentPresenter` and one `EquipmentInteraction` with every reference set. The ghost is inactive and has no collider, and the scene holds no oven instance or `EquipmentVisual`. `SessionRoot` lists `Oven.asset`, and `SessionPanel.equipment` is set. `Oven.asset` is kind `oven`, 3×3, with non-zero capacities and `Oven.prefab`. The prefab has no `NetworkObject` or `OvenClickInput`, has a collider, and its rendered footprint (2.58 × 2.22 m) fits 3×3 cells. `Player` has `Interact` (E, **no Hold interaction**), `Place` (left mouse), `Rotate` (R) and `Point`.

PlayMode, `EquipmentPlacementTests` (real `DevSite`, host plus loopback-UDP remote, temp paths):
- The seed places `dev-oven-1` at (12, 13), each admitted player has an inventory, and the host's presenter shows one visual, with a collider, at the footprint centre.
- The host picks the oven up. The remote sees it held by the host's player ID, and the host's visual disappears. The remote's pickup gets `not-placed` and its place gets `not-held`; the host's edge placement gets `out-of-bounds`.
- The host places at (4, 5) with rotation 1. The remote sees it there at the same site revision as the host, the host shows exactly one visual at the new centre, and the server has one equipment record.
- The remote picks it up and disconnects; the committed save has it held by the remote's ID. After reconnecting (same ID) the remote still holds it and places it.
- After a shutdown, a restart is refused until the transport has stopped. The server-only restart then shows the oven where it was last committed, and the seed is not re-applied.

`blocked` is covered only in EditMode; the dev site has one machine.

## Multi-process (Windows development player)

Final build `build_904069c99141`: `StandaloneWindows64`, Development, scene `Assets/Scenes/DevSite.unity`, **Succeeded**, 0 errors, 1 warning (the existing "No RuntimePipelineConfig asset" notice), 192,422,124 bytes, 17.3 s, output `build/Placement/FoodFactoryGame.exe`. The report is at `TestResults/placement-20260922/build-report-final.json` (git-ignored).

Host (`-host -name Host`, isolated `-save`/`-identity`) and guest (`-connect 127.0.0.1 -name Guest`) on one machine over loopback UDP port 7770. The host was driven with OS-level keyboard and mouse input:

| Step | Evidence |
| --- | --- |
| Both joined | `player-6bf48496…` (host), `player-c787d92d…` (guest) |
| Seeded oven | host capture: oven on the floor at its cell |
| Host presses E on the oven | host log `Requesting pickup of dev-oven-1` → `Accepted: picked-up (revision 19)`; host shows the green 3×3 ghost and "Holding oven"; the guest capture at the same site revision (33) has no oven |
| R, then cursor off the grid | red ghost, readout `[out-of-bounds]`; clicking logs `Rejected: out-of-bounds` |
| Click on a valid cell | `Requesting placement of dev-oven-1 at (2, 6) rotation 1` → `Accepted: placed (revision 57)` |
| After placement | host and guest captures both show the rotated oven at the same spot, both readouts at site revision 60 |
| Committed save | `world.snapshot` schema 3: `dev-oven-1` Placed at (2, 6), rotation 1; inventories `carried:<host>` and `carried:<guest>` |

Evidence: [`artifacts/placement-20260922-multiprocess/`](artifacts/placement-20260922-multiprocess/): six captures plus the `[Session]`/`[Equipment]` lines from each log. Full logs are in `TestResults/placement-20260922` (git-ignored). All processes launched for the check were closed.

## Issues found and fixed during verification

1. **Same-frame restart lost the world.** `Shutdown` then `Begin(Server)` in one frame: the old server's `Stopped` event arrived during the new `StartConnection` and ran `ReleaseServer()`. The new `Started` event then found no world, and the bridge never initialized (console seq 994–998 of the failing run). `Begin` now refuses until both transport sides report `Stopped` (`SessionRoot.CanBegin`), and the test asserts the refusal before waiting.
2. **Pickup needed a hold.** The starter `Player/Interact` action carries a `Hold` interaction, so a tap never performed. This was confirmed in Editor Play mode: queued input performed only after 0.7 s, and pickup then succeeded. `Interact` is now a plain press, guarded by an authoring assertion.
3. **Interaction gave no feedback on a miss.** `EquipmentInteraction` now reports `nothing-under-cursor` in the readout and logs `[Equipment]` request/result lines. The logs are what isolated the next two harness problems.
4. Harness only, not product: synthetic keys without scan codes were ignored by the player, and moving windows with `MoveWindow` made the player report the pointer at a fixed offset (e.g. `(-311, 459)` for a cursor at client `(990, 510)`). Both are documented in `docs/development.md`.

## Not verified / remaining

- Independent review is still required for this visual, multiplayer change; the implementer produced this record.
- The guest's own pickup and placement were not driven in the player build (they are covered in PlayMode). No LAN run and no gamepad input.
- The `assetPath hash of 0` generator error was not attributed to a baseline.
- `BuildDevSite` re-serialized `Player.prefab` and `GoodsNetworkBridge.prefab` (`PrefabId`, transform properties). This is regenerated output; all tests pass with it.
