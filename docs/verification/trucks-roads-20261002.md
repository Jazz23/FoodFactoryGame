# Verification: trucks on generated roads and city traffic (decision 0032), 2026-10-02

Branch `main` (uncommitted working tree). All runs in the live Editor (Unity 6000.5.9f1) through Unity CLI `run_tests` /
`test_status`, on isolated temporary saves. Result JSON for each run is in [trucks-roads-20261002/](trucks-roads-20261002/)
under the run name given below.

## Baseline before the change

| Run | Filter | Mode | Matched | Result |
| --- | --- | --- | --- | --- |
| `baseline-Goods` | assembly `FoodFactoryGame.Goods.EditModeTests` | EditMode | 212 | 211 passed; `TruckTests.StepSizeDoesNotChangeTheOutcome` failed (the known flake) |
| (scratch) | assemblies World / Session / Baseline EditMode | EditMode | 21 / 112 / 4 | all passed |
| (scratch) | assembly `FoodFactoryGame.Session.PlayModeTests` | PlayMode | 42 | 41 passed, 1 skipped (restaurant captures, flag-gated) |

## Piece 0: the flaky truck test

Cause: a partly moved lot got a random GUID, and lot IDs break ties in loading order, so one long clock step and many short
ones loaded different items first. Split lots now get an ID hashed from the lot, destination and truck second. After the
fix `TruckTests` ran five times in a row: 20/20 each (`piece0-trucks-1` ... `piece0-trucks-5`; first and last kept).

## Final runs (after every piece)

| Run | Filter | Mode | Matched | Result |
| --- | --- | --- | --- | --- |
| `final2-Goods` | assembly `FoodFactoryGame.Goods.EditModeTests` | EditMode | 222 | 222 passed (10 new `RoadTruckTests`) |
| `final-World` | assembly `FoodFactoryGame.World.EditModeTests` | EditMode | 27 | 27 passed (6 new `RoadNetworkTests`) |
| `final-Session` | assembly `FoodFactoryGame.Session.EditModeTests` | EditMode | 120 | 120 passed (8 new `VehicleTests`) |
| `final-Baseline` | assembly `FoodFactoryGame.Baseline.EditModeTests` | EditMode | 4 | 4 passed |
| `final-sessplay` | assembly `FoodFactoryGame.Session.PlayModeTests` | PlayMode | 44 | 42 passed, 2 skipped (restaurant and truck captures, flag-gated) |
| `final-goodsplay` | assembly `FoodFactoryGame.Goods.PlayModeTests` | PlayMode | 2 | 2 passed |
| `final-custbench` | `CustomerRuntimeBenchmarkTests` | EditMode | 1 | passed (the clock step it times changed) |
| `p3-play-1` | `WorldGenSessionTests.ATruckDrivesTheGeneratedRoadsWhereItIsDrawn` | PlayMode | 1 | passed |
| `p4-cap-1` | `WorldGenSessionTests.TrucksAndCityTrafficCaptures` (flag set) | PlayMode | 1 | passed |
| `bench-trucks-r1..r3` | `TruckRoadBenchmarkTests` | EditMode | 1 each | passed 3/3 |

`final-Goods` (before the last fix) found two `UncommittedTickTests` failures: the per-step refrigeration lookup no longer threw
for a lot in an unknown location, which those tests use to force a tick exception. The lookup now throws again; `final2-Goods`
is the rerun.

`ATruckDrivesTheGeneratedRoadsWhereItIsDrawn` runs the real WorldGen scene as a host with a loopback-UDP teammate in the same
process: it buys the nearest restaurant, places street-reachable docks, buys a truck and routes it over the network, then
samples the drawn truck every 0.25 s while it drives: at least 80% of samples must lie on the leg the server reports (within
8 m along it, inside the road width, facing along it), the teammate's baseline must carry the same leg, and all 10 bread must
arrive with nothing lost. This is not a separate-process or separate-machine check.

## Save dry run

Copies of every `world.db` under the application's saves folder (`dev-world`, `world-1` ... `world-5`, `worldgen`) were loaded
in a scratch folder with the v17 upgrade, and the road network was registered for the generated (format 3) worlds. All seven
loaded as v17; `world-2` ... `world-5` registered their networks (608-643 segments); the three worlds with a truck had it
loading at a dock (no leg to check). The application's saves were not modified.

## Scale

`TruckRoadBenchmarkTests`: seed 20260927, 20 restaurant lots, 100 trucks on 20 routes, lunch rush, 300 clock seconds, Editor
Mono (not server timings):

| Run | Mean | p99 | Max | Legs | Queued truck-seconds |
| --- | --- | --- | --- | --- | --- |
| before lot merging | 181.4 ms | 479.9 ms | 494.3 ms | 186 | 14 |
| lot merging, Dijkstra | 13.45 ms | 42.8 ms | 49.2 ms | 3,364 | 2,274 |
| A* | 3.77 ms | 19.6 ms | 23.1 ms | 3,364 | 2,274 |
| r1 (final) | 3.00 ms | 13.75 ms | 23.4 ms | 3,270 | 2,563 |
| r2 (final) | 2.97 ms | 15.79 ms | 26.4 ms | 3,270 | 2,563 |
| r3 (final) | 2.92 ms | 10.44 ms | 22.8 ms | 3,270 | 2,563 |

The first row ran out of goods early: its trucks left 8,000 one-second split lots, which made every clock step slow. The
"lot merging, Dijkstra" and "A*" rows have identical legs and queues, so A* changed nothing in the outcome. The final rows
keep a queued truck's chosen leg instead of replanning each second. The p99 stays under the 16.7 ms budget but with little
margin; the slowest seconds coincide with garbage collections.

## Captures

[capture-report.txt](trucks-roads-20261002/capture-report.txt): WorldGen seed `piece-two`, hour 12, Editor PlayMode host, 150 cars
drawn (the cap), 300 frames mean 8.60 ms, p95 9.90 ms, max 28.83 ms (decision 0031 measured 9.9 ms mean with 11 sites).

- [truck-on-the-road.png](trucks-roads-20261002/truck-on-the-road.png): the box truck in company green on a local street,
  right-hand lane, city cars further up the street.
- [truck-and-traffic-from-above.png](trucks-roads-20261002/truck-and-traffic-from-above.png): the truck crossing a junction,
  a bus and cars on the surrounding streets.
- [junction-street-level.png](trucks-roads-20261002/junction-street-level.png): cars of several models and paints queued at
  a red light before the crosswalk, a bus and a van passing.

Reviewed by the implementer only; independent review is still required (AGENTS.md).

## Not verified

- A separate-process (or separate-machine) multiplayer check of truck positions.
- Independent review of the captures.
- A truck turning through a junction and docking in a running-game capture (the PlayMode test checks leg positions and
  delivery, not the look of the turn or docking).
- City cars do not turn at junctions; traffic-light lamps do not show the simulated phase.
