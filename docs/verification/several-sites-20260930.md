# Verification: several sites drawn at once (decision 0031), 2026-09-30

Branch `several-sites-drawn`. All runs in the live Editor (Unity 6000.5.9f1) through the Unity MCP `run_tests` tool, on
isolated temporary saves; the application's saves were only read, as copies, in the dry run below.

## Dry run of the new invariant

Before the carried-site invariant was enabled for real use, copies of every `world.db` under the application's saves folder
(`dev-world`, `world-1`, `world-2`, `world-3`, `worldgen`) were copied to a scratch folder and loaded with it (load validates).
All five loaded. Held machines found: 1 (dev-world); carried locations: 2, 1, 1, 1, 1.

## Test runs

| Filter | Mode | Matched | Result |
| --- | --- | --- | --- |
| assembly `FoodFactoryGame.Goods.EditModeTests` | EditMode | 196 | 195 passed; `TruckTests.StepSizeDoesNotChangeTheOutcome` (known GUID tie-break flake) failed, then passed alone (1/1) |
| `EnterSiteTests` | EditMode | 13 | 13 passed |
| assembly `FoodFactoryGame.Session.EditModeTests` | EditMode | 106 | 106 passed (includes 5 `SitePlacementTests`, 2 new registry tests) |
| assembly `FoodFactoryGame.World.EditModeTests` | EditMode | 21 | 21 passed |
| assembly `FoodFactoryGame.Baseline.EditModeTests` | EditMode | 4 | 4 passed |
| all PlayMode | PlayMode | 36 | 36 passed (before the 3c tests were added) |
| `ATeammateSeesTheHostEnterAndBuildInTheSecondSite` | PlayMode | 1 | passed |
| `PresentationCostAndCapturesWithTenEquippedSites` (`[Explicit]`, `include_explicit`) | PlayMode | 1 | passed |

The multiplayer check uses a listen-server host and a second FishNet client over loopback UDP in the same Editor process
(the existing fixture); it is not a separate-process or separate-machine check.

## Presentation cost

[presentation-cost.txt](several-sites-20260930/presentation-cost.txt), Editor PlayMode host, seed `piece-two`, 300 frames
each, Ryzen 5 5600X:

- 1 site drawn: mean 5.53 ms, p95 6.81 ms, max 22.57 ms.
- 11 sites drawn (10 bought shells, 53 TEST-ONLY ovens, 55 machine visuals): mean 9.87 ms, p95 12.30 ms, max 63.35 ms.

Editor frame times include Editor overhead and the max includes one-off rebuilds; compare the two runs only. Both stay under
the 16.7 ms (60 FPS) mean; no player build was measured.

## Captures (need review by someone other than the implementer)

- [sites-side-by-side.png](several-sites-20260930/sites-side-by-side.png): the starting restaurant and bought shells from
  above; each drawn shell (dark roof) stands in its own lot on levelled paving.
- [inside-bought-restaurant.png](several-sites-20260930/inside-bought-restaurant.png): the indoor top-down view inside a
  bought restaurant after entering it, with its street-side doorway and four test ovens.
