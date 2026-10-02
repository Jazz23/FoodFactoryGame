# Verification: restaurant building feedback round 1 (2026-10-02)

Decision: [0036](../decisions/0036-restaurant-building-feedback.md). Unity 6000.5.9f1, interactive Editor on `G:\Unity\FoodFactoryGame`
(one owner; runs serialized). Stateful tests and captures use temporary save directories; the application's saves were not opened.

## Automated tests

| Run | Filter | Matched | Result |
|---|---|---|---|
| EditMode, full | (none) | 363 | 360 passed, 3 failed (all pre-existing, below) |
| EditMode | `FoodFactoryGame.Session.Tests.SiteWalkTests` (new) | 3 | 3 passed |
| PlayMode, full | `FoodFactoryGame` | 44 | 43 passed, 0 failed, 1 skipped (`BuildModeAndDecoratedRestaurantCaptures` runs only on request) |

New or changed coverage: `RestaurantShellTests` (free-wall conversion keeps paid wall prices and refunds floor records once; outer
walls drawn and removed anywhere in the lot with the footprint and interior following; open shells enclose nothing; corner doors
refused; restyling; free walls survive save and restart; resize still moves doors on a rectangular shell), `SiteWalkTests`,
`RestaurantBuildingSessionTests.BuildModeOrdersReachTheServerAndATeammate` (an outer wall removed by right click and drawn back
with the Wall tool, replicated to a loopback-UDP teammate), `BuildingPresenterTests` (door leaves are the only solid parts besides
walls), `RestaurantContentTests` (ceiling panel withdrawn), world generator hash re-pinned for generator v4.

Pre-existing failures, checked against a clean `HEAD` worktree in batch mode:

- `CustomerRuntimeBenchmarkTests.ThousandCustomersFitAFrameAndCommitUnderTheWarningSignals` fails on `HEAD` too (commits avg
  44.0 ms, max 54.6 ms over the 50 ms signal); with these changes in the same batch setup avg 41.9 ms, max 52.0 ms. In the
  interactive Editor both commit benchmarks also saw 1-3.5 s disk stalls on one run.
- `CityCustomerBenchmarkTests.AGeneratedCityFitsAFrameAndCommitsUnderTheWarningSignals`: same commit signal (its competitor-count
  floor was lowered from 250 to 150 because bigger restaurants leave 178 in the seed city).
- `TruckTests.StepSizeDoesNotChangeTheOutcome` is nondeterministic: on clean `HEAD` it failed 3 of 4 runs. Not investigated here.

## Running-game captures (WorldGen, host, seed `piece-two`)

Script: `AgentScripts/CaptureRestaurantFeedback.cs` (run with `run_script` in Play mode). Log: `restaurant-feedback-20261002/capture-log.txt`.

- Wall clocks placed with rotation 0 on the south and north walls turned to face the room (rotation 0 and 2); the sink (rotation 0)
  backs onto the south wall, facing the room.
- Clearing the build selection leaves tool `None`, no item.
- Build camera tilted to 50 degrees and turned 30 degrees: `0036-build-tilted.png`.
- Inside, top-down view: ceiling hidden (`0036-inside-topdown.png`). Third-person: ceiling shown, camera pulled in to a 2.68 m arm,
  2.75 m above the floor, on an interior cell (`0036-inside-third-person.png`; the first capture showed sunlight through the
  uncovered wall strip, fixed by extending the ceiling over wall cells).
- Front door: closed and blocking with nobody near; open (95 degrees) and not blocking with the avatar 1.2 m away
  (`0036-door-open.png`); closed and blocking again at 4 m.
- Customers: none reached the starting restaurant in 60 s of play, so routing is covered by `SiteWalkTests`, not by a capture.

## Not verified

- Real mouse/keyboard input for pan, zoom, tilt, right-click-vs-drag and X (Input System events do not reach actions without OS
  focus; the rig's state was set directly). The action wiring is in the Player prefab and `InputSystem_Actions`.
- A separate-process multiplayer check; customers walking through a door in a live capture; LOD culling distances by eye.
- Independent visual review of the captures.
