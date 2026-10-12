# Verification: start rules for new generated worlds (2026-10-12)

Decision: [0044](../decisions/0044-start-rules-for-new-generated-worlds.md). The baseline before the change, on the same tree, is
[post-merge-20261012](post-merge-20261012.md). Live Editor Unity 6000.5.9f1, one owner, runs in sequence, isolated temporary saves
only. EditMode run IDs are assigned (`sr-…`) because the Pipeline runner returns none; PlayMode runs carry the NUnit run ID.

## Runs (final code)

| Run ID | Mode | Filter (type) | Matched | Result | Artifact |
|---|---|---|---|---|---|
| sr-world-edit (18:36:03-18:36:50Z) | EditMode | `FoodFactoryGame.World.EditModeTests` (assembly) | 37 | 37 passed (4 new) | [JSON](start-rules-20261012/artifacts/world-editmode.json) |
| sr-goods-edit (18:36:50-18:36:59Z) | EditMode | `FoodFactoryGame.Goods.EditModeTests` (assembly) | 275 | 275 passed (1 new) | [JSON](start-rules-20261012/artifacts/goods-editmode.json) |
| sr-session-edit (18:36:59-18:37:12Z) | EditMode | `FoodFactoryGame.Session.EditModeTests` (assembly) | 147 | 143 passed, 1 failed (pre-existing build list, below), 3 skipped (flag-gated measurements) | [JSON](start-rules-20261012/artifacts/session-editmode.json) |
| sr-baseline-edit (18:37:12-18:37:14Z) | EditMode | `FoodFactoryGame.Baseline.EditModeTests` (assembly) | 4 | 4 passed | [JSON](start-rules-20261012/artifacts/baseline-editmode.json) |
| sr-first-customer-1 (18:37:21-18:37:30Z), flag `Temp/starting-loop.flag` | EditMode | `…StartingLoopFirstCustomerMeasurement.TheFirstCustomerComesWithinAMinuteOnBothSeeds` (testName) | 1 | failed against the original target: 6 of 10 openings over 60 s (its JSON was overwritten by the rerun; the table is identical) | — |
| sr-first-customer-2 (19:14:05-19:14:52Z), flag `Temp/starting-loop.flag`, after owner decision 30 | EditMode | `FoodFactoryGame.Session.Tests.StartingLoopFirstCustomerMeasurement.TheFirstCustomerComesInAboutOneToTwoMinutes` (testName) | 1 | passed: mean 89 s ≤ 120 s, every run had a customer | [JSON](start-rules-20261012/artifacts/first-customer-measurement.json), [table](start-rules-20261012/first-customer.txt) |
| sr-city-1/2/3 (18:37:47-18:37:55Z) | EditMode | `FoodFactoryGame.Benchmarks.Tests.CityCustomerBenchmarkTests` (testName) | 2 each | 2 passed each | [JSON 1](start-rules-20261012/artifacts/city-benchmark-1.json), [2](start-rules-20261012/artifacts/city-benchmark-2.json), [3](start-rules-20261012/artifacts/city-benchmark-3.json), [console](start-rules-20261012/artifacts/city-benchmark-console.txt) |
| NUnit run 2 (18:38:32-18:42:08Z) | PlayMode | `FoodFactoryGame.Session.PlayModeTests` (assembly) | 70 | 38 passed, 24 failed (pre-existing build list), 8 skipped: identical to the baseline | [XML](start-rules-20261012/artifacts/session-playmode-nunit.xml) |
| NUnit run 2 (18:42:18-18:42:31Z) | PlayMode | `FoodFactoryGame.Goods.PlayModeTests` (assembly) | 2 | 2 passed | [XML](start-rules-20261012/artifacts/goods-playmode-nunit.xml) |

The 1 EditMode and 24 PlayMode failures are the DevSite build-list failures from the baseline (`88c023b` left DevSite disabled in
the build list). They fail in SetUp, the same tests and messages as before the change. Every WorldGen PlayMode test passes,
including `ANewWorldStartsThePlayerOnItsOwnRestaurant` (company cash = `GeneratedWorld.StartingCash`) and
`AWholeSaleInTheStartingRestaurantReachesBothClients`. Console after compiling: no new errors or warnings (the CS0618 warnings
are in FishNet and older PlayMode tests).

## Acceptance

| Criterion | Evidence | Status |
|---|---|---|
| Cash only in new worlds (decision 17) | `GeneratedWorldTests.OnlyANewWorldStartsWithAMillionDollars`: a new world has $1,000,000; the same world saved at v24 with $700 reopens with $700 and is not topped up; a new DevSite world has `DevWorld.StartingCash`. `AnOlderWorldLoadsWithReTunedDistrictsAndItsCash` keeps $700 through the v23 upgrade. | done |
| Start district at minimum tier 1, both seeds (decision 19) | `WorldGeneratorTests.TheStartStandsInADistrictWhereBreadSells("piece-two")` and `("p0-second")` check the layout district's `MinRecipeTier` = 1 and `GoodsWorld.MinTier` = 1 for the customer block the start stands in. `NoStartWhereBreadDoesNotSell` shows the rule is enforced: a residential tier of 2 yields no layout, and the validator flags a start in a tier-2 district. | done |
| Generator bump changes no existing world (decision 29) | `GeneratorV7IsV6WithTheStartTierRule`: the v7 layout written as v6 equals v6's pinned hash (seed 20260927) and differs only in the version line on both P0 seeds. New pin `34c8eeee…feefc`. The validator runs only at generation. | done |
| Spawn distance (decision 28) | `CustomerTests.AStreetSpawnShortensOnlyTheWalk`: a street-spawn district's customers walk 5 s and one arrives within 30 s; a whole-walk district's walk is 150 s for 300 m; range still counts from the district (a district 1 m out of range sends nobody); a negative value is invalid. `GeneratedWorldCustomersTests.ANewWorldsCustomersSpawnByTheStreet`: a new world's districts have 10 m, and the same layout stamped as v6 derives 0. `AnOlderWorldLoadsWithReTunedDistrictsAndItsCash`: a v23 save loads with every district at 0. | done |
| First customer under 60 s on both seeds (original) | [Measurement](start-rules-20261012/first-customer.txt), below | not met: 4 of 10 |
| First customer in about one to two minutes (owner decision 30) | Same table: mean 89 s (piece-two 101 s, p0-second 77 s), median 73 s, max 221 s; the measurement checks a mean of at most 120 s | done |
| 0025 `CityCustomerBenchmarkTests` | 3 runs passed, below | done |
| Older saves load unchanged | [Probe](start-rules-20261012/artifacts/pre-merge-saves-v25.txt): the four pre-merge save copies load at v25 with the same cash and goods as in the baseline probe, every district at `SpawnWalkMetres` 0. | done |

## First customer

A stocked, staffed starting register in a new v7 world, opened 0, 60, 120, 180 or 240 clock s after the world starts. Each
opening is a separate world, so each is a separate draw of the deterministic customer stream.

| Seed | Opened at | First decision | First arrival |
|---|---|---|---|
| piece-two | 0 s | 90 s | 95 s |
| piece-two | 60 s | 45 s | **50 s** |
| piece-two | 120 s | 216 s | 221 s |
| piece-two | 180 s | 24 s | **29 s** |
| piece-two | 240 s | 105 s | 110 s |
| p0-second | 0 s | 12 s | **17 s** |
| p0-second | 60 s | 45 s | **50 s** |
| p0-second | 120 s | 167 s | 172 s |
| p0-second | 180 s | 80 s | 85 s |
| p0-second | 240 s | 56 s | 61 s |

Every arrival is the first decision + 5 s, so the street spawn works as specified: P4 measured 60-219 s of walking. The original
target (within a minute) is still missed because the first decision for the restaurant comes 12-216 s after opening. In range
are 6 districts with 22 customers per game hour (piece-two) or 5 with 59 (p0-second), and the restaurant competes with 157 or
138 competitors for each. Spawn distance cannot change that wait.

Owner decision 30 (2026-10-12) accepts about one to two minutes. Summary: mean 89 s (piece-two 101 s, p0-second 77 s), median
73 s, minimum 17 s, maximum 221 s. The measurement now checks a mean of at most 120 s and that every run gets a customer, and it
passes (sr-first-customer-2). The customer stream is deterministic, so the rerun gave the same table.

## 0025 budgets

Seed piece-two, Editor Mono: tick p99 0.26 ms (signal 16.7), decision burst 1.46-1.50 ms, commits 8.5-8.9 ms average and
11.7-12.2 ms maximum (signal 50), payload 248 KB (signal 1 MB), 511-531 customers alive. Attribution: the same city with every
district's spawn walk set to 0 in memory holds 840 customers (0039: 832), of which 320 are travelling against 31 with the street
spawn, and serves slightly fewer (5,174 against 5,451 in 1,800 s). So the street spawn lowers load. It does not raise it.

## Not covered

- No PlayMode test or capture of the street-spawn figures in a running game. The presenter logic is unchanged (figures appear in
  the last 12 s of travel from the street edge), so a 5 s walk shows the figure at once. That is an expectation, not an observed
  capture.
- DevSite PlayMode coverage is blocked by the build list (baseline).
- The P4 pacing measurement (`StartingLoopPacingMeasurement`) still loads the retired `SellBread.asset` and was not run.
