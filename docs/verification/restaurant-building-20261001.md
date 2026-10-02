# Verification: restaurant building, slices 1-5 (2026-10-01)

Scope: decision [0034](../decisions/0034-restaurant-building.md) slices 1-5 as implemented under
[0035](../decisions/0035-restaurant-building-implementation.md). Live Editor Unity 6000.5.9f1 (Pipeline CLI, AMD Ryzen 7 5800X).
Every stateful test used a unique directory under the OS temp path; no application save or database was opened. Raw status
JSON for every run below (Pipeline `test_status`, which reports no native run ID; the identity is the UTC start time and file)
is in [restaurant-building-20261001/](restaurant-building-20261001/).

## Baseline before any change (same Editor, same machine)

| Requested filter | Started (UTC) | Matched | Result | Artifact |
|---|---|---:|---|---|
| EditMode assembly `FoodFactoryGame.Goods.EditModeTests` | 19:15 | 199 | 198 passed; `TruckTests.StepSizeDoesNotChangeTheOutcome` failed (known nondeterministic GUID tie-break) | `baseline-goods-editmode.json` |
| EditMode assembly `FoodFactoryGame.Session.EditModeTests` | 19:16 | 110 | 110 passed | `baseline-session-editmode.json` |
| PlayMode assembly `FoodFactoryGame.Session.PlayModeTests` | 19:18 | 38 | 38 passed | `baseline-session-playmode.json` |
| Batch EditMode testName `FoodFactoryGame.Benchmarks.Tests.CustomerRuntimeBenchmarkTests` on a clean worktree of `HEAD` 4256efb (`G:/ffb`, FishNet copied in) | 20:19 | 1 | **failed**: max commit 143.6 ms over the 50 ms signal (avg 47.6 ms; validate 7.88 ms mean; transaction 26.4 ms mean; 1,005 customers) | `baseline-head-runtime-benchmark.xml` |

## Final runs on the finished code

Compilation completed with no errors before the final runs (`recompile_status`: up to date, `compilationFailed` false).

| Requested filter | Started (UTC) | Matched | Result | Artifact |
|---|---|---:|---|---|
| EditMode assembly `FoodFactoryGame.Goods.EditModeTests` | 20:21:16 | 212 | 211 passed; the same known flaky truck test failed (it also failed on the baseline and passed in an earlier run today) | `final-Goods-editmode.json` |
| EditMode assembly `FoodFactoryGame.Session.EditModeTests` | 20:21:31 | 112 | 112 passed | `final-Session-editmode.json` |
| EditMode assembly `FoodFactoryGame.World.EditModeTests` | 20:21:58 | 21 | 21 passed | `final-World-editmode.json` |
| EditMode assembly `FoodFactoryGame.Baseline.EditModeTests` | 20:23:31 | 4 | 4 passed | `final-Baseline-editmode.json` |
| PlayMode assembly `FoodFactoryGame.Goods.PlayModeTests` | 20:23:37 | 2 | 2 passed | `final-Goods-playmode.json` |
| PlayMode assembly `FoodFactoryGame.Session.PlayModeTests` | 20:23:55 | 42 | 41 passed, 1 skipped (the on-request capture test, ignored without its flag file) | `final-Session-playmode.json` |
| EditMode assembly `FoodFactoryGame.Benchmarks.EditModeTests` | 20:06 | 9 | 8 passed; `CustomerRuntimeBenchmarkTests` failed its 50 ms commit signal (max 70.4 ms, avg 40.3 ms), as unmodified HEAD does on this machine (above); reruns 63.5 and 60.1 ms | `benchmarks-2.json`, `runtime-benchmark-rerun-1.json`, `-2.json` |
| PlayMode testName `...RestaurantBuildingSessionTests.BuildModeAndDecoratedRestaurantCaptures` (flag file present) | 20:05 | 1 | 1 passed; captures below | `captures-6.json` |

The 13 errors in the Editor console after the PlayMode runs are the remote-client fixtures' expected FishNet
`SpawnablePrefabs is null on ...-remote` errors, each declared with `LogAssert.Expect` (the test framework fails a test on any
undeclared error, and all passed). The runtime benchmark's customer counts are identical on HEAD and on this branch
(1,005 → 994, served 1,568), so the staffing fixture change reproduces the same simulation.

## What the new tests show

Domain (EditMode, `RestaurantShellTests` 7, `RestaurantFurnishingTests` 6):
- Slice 1: growing charges the new floor and wall cells plus the fee; shrinking back refunds exactly what the growth charged;
  interior walls, doors, windows and the finish are charged and refunded in full; doors and windows move with their side and lost
  ones are refunded; walls over a table, wall decor left without its wall, off-lot, too small, last door, corner door, unknown
  style, factory, stranger, unchanged and insufficient funds are refused with nothing changed or recorded; a retried request
  replays and pays once; shells and charges survive a save, a restart and a failed commit; a v15 save upgrades with its counter
  as the same, unstaffed register. Company cash plus every recorded charge is constant throughout.
- Slices 3-5: buy-and-place charges once for all pieces or changes nothing; decor layers overlap only their own kind (rug under
  a table, vase on the table, art on the wall, pendant under the ceiling; refusals on walls, doors, floors, outdoors); selling
  refunds exactly and moves a register's bread to the seller's inventory, and a sale whose goods do not fit is refused; an
  unstaffed register sells nothing, a staffed one sells, walling a table off stops its seats from counting until a door reopens
  the path; ambience is capped with diminishing returns and wins more customers; a dock sealed off from the street is refused,
  one in the yard is accepted, and two trucks share it one at a time with no crate lost.
- Authoring (`RestaurantContentTests` 2): all 59 kit models are buildable in DevSite and WorldGen (23 as styles, 36 as offered
  equipment), every server style has models, layers and mounts agree, and BuildMode uses the Player actions.

Unity integration and multiplayer (PlayMode, WorldGen, host plus a loopback-UDP remote client in the same Editor process):
- `BuildModeOrdersReachTheServerAndATeammate` (slices 1-4): build mode opens top-down; a drawn shell resize, a brick interior
  wall (drawn with the kit's brick models), a bistro table placed by one click and wall art reach the server and the teammate's
  baseline; a teammate's interior wall over the table is refused (`blocked`) with the shell unchanged; selling the table with a
  right click refunds $60.00; the committed save matches; leaving build mode returns the camera.
- `RegisterStaffingReplicatesAndEndsWhenThePlayerLeaves` (slice 3): the host works the register from the register screen and the
  teammate sees it; the teammate takes over; its disconnect clears the register on the host and in the save.
- `RestaurantDocksInTheYardServeOneTruckAtATime` (slice 5): a second restaurant is bought, a dock is bought-and-placed in each
  yard, two trucks run one route, never more than one loads at the pickup dock, and the teammate sees all 8 crates arrive.
- Existing tests updated only for the new contract: sale fixtures now staff their register (confirmed rule), the dev
  restaurant's walls are one collider per wall cell (kit drawing), and authoring lists allow the appended restaurant content.

## Visual captures (running game, WorldGen host, isolated save)

- [build-mode-screen.png](restaurant-building-20261001/build-mode-screen.png): build mode with the panel (tools, catalog with
  icons, charge/refund/net preview, Confirm/Cancel/Leave), the lot grid, the roof hidden, and a valid green banquette ghost.
- [build-mode-world.png](restaurant-building-20261001/build-mode-world.png): the same view without UI.
- [decorated-top-down.png](restaurant-building-20261001/decorated-top-down.png): a restaurant furnished through the same orders
  (wainscot finish, oak floor, bistro tables with chairs, vases and pendants, floor plants, sconces and wall art, a mullioned
  window, a brick corner room with a glazed door and a serving hatch); ambience 82/100
  ([capture-log.txt](restaurant-building-20261001/capture-log.txt)).

Earlier captures in the same run found real defects, fixed before the final captures: the roof hid the interior in build mode,
the panel's tool rows shrank under the catalog, the session readout drew over the panel (the panel moved to the right), and the
grid lines faced away from the camera (winding fixed).

## Not verified

- Independent review of the captures by someone other than the implementer (AGENTS.md asks for it on visual changes).
- A separate-process (two player builds) multiplayer check; only the in-Editor loopback fixtures above ran.
- Real mouse and keyboard input in build mode (tests call the same BuildMode methods the input handlers call).
- `SampleScene` (not a build scene) has not been given the restaurant content or build mode.
- The 50 ms commit signal of `CustomerRuntimeBenchmarkTests` fails on this machine with or without this work.
