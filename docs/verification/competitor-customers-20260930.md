# Verification: drawing competitors' customers (decision 0033), 2026-09-30

Branch `main` (uncommitted working tree). All runs in the live Editor (Unity 6000.5.9f1, Ryzen 5 5600X) through the Unity MCP
`run_tests` tool, on isolated temporary saves and worlds. The application's saves were not opened. Unity writes each run's NUnit
XML to `%USERPROFILE%/AppData/LocalLow/DefaultCompany/FoodFactoryGame/TestResults.xml` and overwrites it, so the counts below
are the record. The evidence files are in [competitor-customers-20260930/](competitor-customers-20260930/).

## Baseline (piece 0, before any change)

| Filter | Mode | Matched | Result |
| --- | --- | --- | --- |
| assembly `FoodFactoryGame.Goods.EditModeTests` | EditMode | 196 | 195 passed; `TruckTests.StepSizeDoesNotChangeTheOutcome` failed (the known flake that decision 0032 records) |
| assembly `FoodFactoryGame.Session.EditModeTests` | EditMode | 109 | 109 passed |
| assembly `FoodFactoryGame.Session.PlayModeTests` | PlayMode | 35 | 35 passed |

## After the change

| Filter | Mode | Matched | Result |
| --- | --- | --- | --- |
| `FoodFactoryGame.Goods.Tests.CrowdTests` | EditMode | 3 | 3 passed |
| `CrowdViewsForEightConnectionsStayUnderTheirBudget` | EditMode | 1 | passed |
| `CompetitorFrontageTests` | EditMode | 1 | passed (rerun after the queue-row change: passed) |
| `CompetitorCustomerTests` | PlayMode | 3 | 3 passed (rerun after the queue-row change: 3 passed) |
| assembly `FoodFactoryGame.Goods.EditModeTests` | EditMode | 199 | 199 passed (the truck flake passed this time; it is not fixed) |
| assembly `FoodFactoryGame.Session.EditModeTests` | EditMode | 110 | 110 passed |
| assembly `FoodFactoryGame.World.EditModeTests` | EditMode | 21 | 21 passed |
| assembly `FoodFactoryGame.Baseline.EditModeTests` | EditMode | 4 | 4 passed |
| assembly `FoodFactoryGame.Goods.PlayModeTests` | PlayMode | 2 | 2 passed |
| assembly `FoodFactoryGame.Session.PlayModeTests` | PlayMode | 38 | 38 passed (35 existing, including both `CustomerPresenterTests`, plus 3 new) |
| `PresentationCostAndCapturesWithTheCapFull` (`[Explicit]`, `include_explicit`) | PlayMode | 1 | passed (third run; see below) |

The multiplayer check (`CrowdsReachEachClientOnlyNearItsAvatar`) uses a listen-server host and a second FishNet client over
loopback UDP in the same Editor process, the existing fixture. It is not a separate-process or separate-machine check.

No new console errors. The `SpawnablePrefabs is null` errors are expected by the fixtures (`LogAssert.Expect`). The
`[Goods] Clock step failed` error comes from `GoodsListenServerTests`, which provokes and expects it.

## Crowd cost (piece 1)

`CrowdViewsForEightConnectionsStayUnderTheirBudget`, seed `piece-two` after a 1,800 s warm-up: 8 connections (the starting
lot and the 7 nearest competitors) × 300 s. The final run, with competitors without customers left out:

- One view: mean 0.059 ms, p99 0.195 ms (budget 0.5 ms), max 21.3 ms (one outlier, likely a GC pause).
- All 8 connections per second: mean 0.47 ms.
- Competitors with customers in range: mean 3.3, max 6. Customers sent: mean 4.0, max 7.
- JSON per send: mean 1.1 KB, max 1.9 KB (budget 4 KB/s; a crowd is resent only when it changes).

The first run listed every competitor in range, including those without customers: about 20 competitors and 3.1 KB per send.
After that run, competitors without customers were left out.

## Presentation cost and captures (piece 4)

[presentation-cost.txt](competitor-customers-20260930/presentation-cost.txt), Editor PlayMode host, 300 frames each:

| Figures drawn | Mean | p95 | Max |
| --- | --- | --- | --- |
| none seeded (0) | 10.73 ms | 15.88 ms | 94.25 ms |
| 92: 50 at the starting restaurant, 42 queued at 5 competitors | 19.13 ms | 22.54 ms | 45.60 ms |
| 100, all at the starting restaurant (drawing as it was before this change) | 23.67 ms | 27.13 ms | 62.31 ms |

- Figure for figure, competitors' figures cost no more than the player's own. The mixed run is cheaper than the owned-only one,
  so the escalation condition in decision 0033 (more than 2 ms over the owned-figure baseline) is not met.
- **The 16.7 ms acceptance target is not met in the Editor, with or without this change.** A full 100-figure cap costs about
  13 ms over an empty scene here. That contradicts the 2026-09-25 probe (decision 0024: 100 animated models fit). Editor
  overhead is included and no player build was measured. Before this change the cap rarely filled; now it can, at busy rival
  rows. Whether to lower the cap or measure a player build first is for the owner.
- Two of the seven busy competitors (`competitor-restaurant-0139` and `-0049`) got no figures. Every street point in front
  of them was on screen, and figures only appear out of view. This is the intended rule, not a defect. Their queues appear
  once the camera turns away.
- The evidence test seeds TEST-ONLY queued customers straight into the server world. The first two runs failed for fixture
  reasons that the diagnostics showed. In the first run, the competitors served and seated the seeded customers within the
  minute: 75 figures, because customers inside take no place, by design. In the second run, 92 figures stopped short of a
  95 threshold for the on-screen reason above. The fixture then seeded each competitor's seats and servers plus 8, and waits
  for the count to settle.

Captures (they need review by someone other than the implementer):

- [busy-competitor-queue.png](competitor-customers-20260930/busy-competitor-queue.png): eight figures queueing along a
  competitor's facade, beside its door. In the first capture the eighth figure started a second row beside the door and looked
  like it was cutting in. The queue now bends back at its far end (`CompetitorFrontage.QueueSpot`). This capture is after that
  change.
- [quiet-competitor.png](competitor-customers-20260930/quiet-competitor.png): a competitor with no customers and nobody
  outside.
- [street-from-above.png](competitor-customers-20260930/street-from-above.png): the starting restaurant's queue and several
  competitors' queues on the surrounding blocks.

## Not covered

- A customer walking out of the player's restaurant and visibly crossing to a competitor. The presenter keeps one figure per
  customer ID across both sources, so the figure should continue. No test stages it, because the walk-out's next choice is
  a random draw. No capture shows it either.
- Out-of-view spawning still checks only the local camera, not other players' cameras (decision 0024, open).
- Independent review of the captures.
