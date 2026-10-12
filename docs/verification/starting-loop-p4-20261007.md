# Starting loop, P4: demand on the game hour and the start kit (2026-10-07)

Piece P4 of [the starting-loop plan](../starting-loop-plan.md) ([P4 plan](../starting-loop-p4-plan.md)). Decision:
[0039](../decisions/0039-demand-on-the-game-hour-and-start-kit.md). Evidence folder:
[starting-loop-p4-20261007/](starting-loop-p4-20261007/). Unity 6000.5.9f1, Editor driven through Unity CLI `unity command`
(Pipeline). Runs were on 2026-10-08 UTC (evening of 2026-10-07 local). All stateful runs used isolated temporary saves; the
application database was never opened.

## Test runs

| Run | Filter (type, mode) | Identity (start, UTC) | Matched | Result | Artifact |
|---|---|---|---|---|---|
| Baseline goods | `FoodFactoryGame.Goods.EditModeTests` (assembly, editmode) | 01:50Z | 249 | 249 passed | MCP result (not saved) |
| Baseline session EditMode | `FoodFactoryGame.Session.EditModeTests` (assembly, editmode) | 01:51Z | 125 | 124 passed, 1 skipped (flag) | MCP result |
| Baseline world | `FoodFactoryGame.World.EditModeTests` (assembly, editmode) | 01:51:38Z | 28 | 28 passed | MCP result |
| Baseline baseline | `FoodFactoryGame.Baseline.EditModeTests` (assembly, editmode) | 01:52:25Z | 4 | 4 passed | MCP result |
| Baseline session PlayMode | `FoodFactoryGame.Session.PlayModeTests` (assembly, playmode, async) | 01:53Z | 50 | 44 passed, 6 skipped (flags) | `baseline/session-playmode.json` |
| Final goods | `FoodFactoryGame.Goods.EditModeTests` (assembly, editmode) | 02:12:51Z | 253 | 253 passed | MCP result |
| Final world | `FoodFactoryGame.World.EditModeTests` (assembly, editmode) | 02:13:04Z | 33 | 33 passed | MCP result |
| Final session EditMode | `FoodFactoryGame.Session.EditModeTests` (assembly, editmode) | 02:14:00Z | 128 | 126 passed, 2 skipped (flags) | MCP result |
| Final baseline | `FoodFactoryGame.Baseline.EditModeTests` (assembly, editmode) | 02:14:22Z | 4 | 4 passed | MCP result |
| Final session PlayMode | `FoodFactoryGame.Session.PlayModeTests` (assembly, playmode, async) | ~02:15Z | 50 | 44 passed, 6 skipped (flags) | `session-playmode.json` |
| Final goods PlayMode | `FoodFactoryGame.Goods.PlayModeTests` (assembly, playmode, async) | ~02:24Z | 2 | 2 passed | `goods-playmode.json` |
| M-P4 pacing | `...StartingLoopPacingMeasurement.PacingAtAStockedStaffedStartingRestaurant` (testName, editmode), flag | 02:05:11Z | 1 | passed (measures only) | `m-p4-demand.txt` |
| City benchmark, divisor 10 | `FoodFactoryGame.Benchmarks.Tests.CityCustomerBenchmarkTests` (testName, editmode) | 02:07:00Z | 2 | 2 passed | `city-benchmark-d10.json`, `benchmark-console-d10.txt` |
| City benchmark, divisor 6 (TEST-ONLY settings, reverted) | same | 02:11:04Z | 2 | 2 passed | `benchmark-console-d6.txt` |
| Runtime benchmark (P4), 3 runs | `FoodFactoryGame.Benchmarks.Tests.CustomerRuntimeBenchmarkTests` (testName, editmode) | 02:07:05Z, 02:07:22Z, 02:07:26Z | 1 each | failed (58.0 ms commit), passed, failed (96.5 ms) | `runtime-benchmark.json`, `runtime-benchmark-baseline.txt` |
| Runtime benchmark, committed code, 3 runs | same, on 4d24bb6 (git stash) | 02:09:28Z, 02:09:33Z, 02:09:48Z | 1 each | passed, failed (60.2 ms commit), passed | `runtime-benchmark-baseline.txt` |
| v20 → v21 dry run | eval on a copy of a real v20 world | ~02:22Z | n/a | schema 21 in memory, file unchanged | `v20-upgrade-dry-run.txt` |
| Pass R, attempt 1 | `...StartingLoopPlaythroughTests.PassRequestsFirstSeed` (testName, playmode), flag `folder=starting-loop-p4-20261007` | ~02:25Z | 1 | failed: S5 harness (see Playthrough); S1-S4, S10-S12 passed | `pass-r-result.json` (step folder removed before the rerun) |

New tests: `GameClockTests` (4, World), `WorldGeneratorTests.GeneratorV6IsV5WithGameHourRates`, `DemandClockTests` (4, Goods),
`GeneratedWorldTests.TheStartingCashCoversABasicKitAndLittleMore` and `AnOlderWorldLoadsWithReTunedDistrictsAndItsCash`
(Session), and a start-kit assertion in `WorldGenSessionTests.ANewWorldStartsThePlayerOnItsOwnRestaurant`. Fixtures that
encoded the old unit were converted exactly (old / 60: 3600 → 60, 720 → 12, 3000 → 50); the runtime benchmark's simulation
numbers are identical before and after (1005 → 994 customers, 1568 served). WorldGen PlayMode tests that buy buildings get
TEST-ONLY funding (`AdjustCashDurably`, a ledger `adjustment`).

The one console error after each PlayMode run is FishNet's expected `SpawnablePrefabs is null on worldgen-test-remote`
(declared by the tests); no new console errors after compiles.

## Pacing (M-P4)

See decision 0039 for the table and `m-p4-demand.txt` for every game hour. Divisor 10: first sale 174 s (`piece-two`) and
120 s (`p0-second`), 0.5 and 1.0 sales a real minute, 0 walk-outs, city peak about 810 customers. Nearest districts reaching
the starting restaurant are a 66 s and 60 s walk: the first arrival cannot come within a minute by rate alone (escalated).

## Budgets (0025)

| Run | Customers | Tick p99 | Burst / catch-up | Commits avg / max | Payload |
|---|---|---|---|---|---|
| City, before P4 (2026-09-30 record) | ~135 | 0.68 ms | 3.32 / 2.30 ms | 10.9 / 31.7 ms | 157 KB |
| City, divisor 10 | 832 → 819 | 0.50 ms | 1.47 / 5.30 ms | 18.3 / 32.4 ms | 335 KB |
| City, divisor 6 | 1,439 → 1,375 | 0.85 ms | 1.47 / 7.18 ms | 24.2 / 42.9 ms | 522 KB |

Signals: 16.7 ms tick p99 and burst, 50 ms commit, 1 MB payload. Divisor 10 holds them. The 1,000-customer runtime benchmark
crosses the 50 ms commit signal intermittently on the committed code as well (1 of 3 runs before P4, 2 of 3 with P4, same
simulation); this is pre-existing and not attributed to P4. Player-build budgets remain P5.

## Upgrade dry run

`v20-upgrade-dry-run.txt`: a real v20 world (committed build, seed `piece-two`, 437 clock s, 122 customers) loads as v21 in
memory: rates 1870 per 3600 s → 186 per game hour, progress scaled, customers, cash and ledger unchanged, file hash
unchanged. Three of 25 per-block rates differ by one from the layout-derived records (rounding per block vs per district);
records are only added when missing, so nothing is added, lost or duplicated.

## Playthrough

Pass R attempt 1 on `piece-two` (real $700 start, 20 dough): S1-S4 and S10-S12 passed and conservation balanced at every step
that ran; S5 failed "dough into the oven refused: capacity", and S6-S9 were blocked by it. Cause: the harness moved every
held dough stack into the oven, which assumed the old 10 dough; the player now holds 25 (20 + 5 bought) and the oven's one
input slot holds 20, so the second stack was rightly refused. Fix (test-only): Pass R loads only what fits and keeps the rest
in hand. S10 (table, decor, wall and door) passed on the real $700; S12 used TEST-ONLY funding.

**Not yet rerun.** The rerun after the fix did not start (the first version of the fix did not compile; corrected and
compiled clean). Pass R and Pass I on the new start are the next verification step; Pass I's device path
(`LoadOvenWithDevices`) has the same whole-stack assumption to check.

## Open

- First-customer target vs walking floor: owner decision (0039).
- The runtime benchmark's intermittent 50 ms commit signal (pre-existing).
- Pass I on the new start, and the second seed, were not run in P4; P5 reruns the updated script on both seeds and passes.
