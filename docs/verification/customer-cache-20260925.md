# Verification: customer indexes and validation lookups (2026-09-25)

The customer simulation now keeps a transient, ticket-ordered queue and busy server, counter and seat counts for each restaurant. Counter or table placement and pickup, new sites or competitors, company ownership, menu registration and map moves invalidate the restaurant catalog. Customer bootstrap reindexes occupancy; restore and failed commits rebuild from the saved snapshot. Equal tickets retain snapshot order. `ValidateCustomers` uses district, competitor, equipment and seat lookups built once for the validation pass.

Unity 6000.5.9f1 live Editor recompiled after the final C# changes without compilation errors. The console reported zero errors after recompile. Test fixtures used isolated SQLite paths under the OS temp directory.

| Requested filter | Run identity (UTC) | Matched | Result | Artifact |
| --- | --- | ---: | --- | --- |
| EditMode `testName: FoodFactoryGame.Goods.Tests.CustomerTests` | NUnit `id=2`, 2026-09-25 22:10:17 | 13 | 13 passed | [focused XML](artifacts/customer-cache-focused-20260925.xml) |
| EditMode `assembly: FoodFactoryGame.Goods.EditModeTests` | NUnit `id=2`, 2026-09-25 22:03:13 | 163 | 162 passed, 1 failed | [goods XML](artifacts/customer-cache-goods-20260925.xml) |
| EditMode `testName: FoodFactoryGame.Benchmarks.Tests.CustomerRuntimeBenchmarkTests` | NUnit `id=2`, 2026-09-25 22:01:25 | 1 | 1 failed on maximum commit time | [benchmark XML](artifacts/customer-runtime-benchmark-20260925.xml) |

The goods suite failure was `TruckTests.StepSizeDoesNotChangeTheOutcome`, previously documented as nondeterministic because split truck lots use random GUIDs as an ordering tie-break ([prior verification](customers-20260925.md)). All customer tests passed, including the added table, published occupancy, equal ticket ordering, table capacity and competitor server cases. The broader goods run preceded the final equal ticket fix and narrower equipment invalidation; the final focused run followed both changes.

The runtime benchmark measured 905 to 1,046 customers over 300 ticks: clock tick mean 9.55 ms, p99 14.95 ms, and customer step 0.20 ms. Its 30 isolated SQLite commits averaged 43.3 ms and peaked at 91.5 ms, above the provisional 50 ms warning signal; payload was 356 KB. The benchmark includes snapshot copying, whole-world validation, serialization and SQLite transaction work, so this result does not isolate the customer validation cost. No before/after timing baseline was captured for this change, and the save warning remains open.

A distinct read-only reviewer found no remaining correctness defect after the equal ticket fix. The reviewer identified an unnecessary catalog rebuild on purchase of held equipment; that invalidation was removed before the final compile and focused test run.
