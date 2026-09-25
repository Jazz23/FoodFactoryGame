# Verification: save phases at 1,000 customers (2026-09-25)

The runtime benchmark uses a unique database under the OS temp directory, holds its WAL connection, warms a synthetic
world to at least 1,000 customers, times one accepted player transfer, then advances 300 one-second ticks and saves every
10 seconds. It reports copy, validation, JSON, SQLite transaction before `COMMIT`, and `COMMIT` including disk sync.
The phase timer excludes waits for the save lock and `BEGIN IMMEDIATE` writer contention. `COMMIT` is not a pure fsync
timer. The command timer includes its rollback copy, action and save. All databases were isolated; no application save
or migration was modified.

Machine: AMD Ryzen 5 5600X 6-Core Processor, Unity 6000.5.9f1, Editor Mono JIT. Save path was under OS temp on `C:`;
drive medium unknown. The scenario has 10 player restaurants and 10 competitors. The GDD's world-wide population
interpretation is still a planning assumption.

| Measurement | Result |
| --- | ---: |
| Warmup / customers at command / customers after 300 s | 420 s / 1,005 / 994 |
| Clock tick mean / p99 / max over 300 ticks | 9.72 / 22.44 / 35.65 ms |
| Separate rollback-copy sample / step sample | 8.87 / 0.20 ms mean over 30 each |
| Tick commits | 30; mean 38.0 ms, max 79.1 ms, final payload 338 KB |
| Save copy mean / max | 9.63 / 26.98 ms |
| Save validation mean / max | 3.45 / 5.88 ms |
| Save JSON mean / max | 4.06 / 5.63 ms |
| Save transaction before `COMMIT` mean / max | 18.44 / 50.08 ms |
| Save `COMMIT` including sync mean / max | 2.45 / 7.32 ms |
| One accepted player transfer at 1,005 customers | 52.47 ms total; 40.54 ms save; 11.92 ms wrapper/action |
| Transfer save phases: copy / validate / JSON / transaction / `COMMIT` | 8.17 / 4.62 / 4.58 / 16.74 / 6.44 ms |

The benchmark's provisional 16.7 ms tick p99 and decision 0012's 50 ms maximum-commit signals both failed. The
failure is a measured performance warning, not a correctness or persistence failure. The previous run at 905 starting
customers recorded 14.95 ms tick p99 and 91.5 ms maximum commit ([prior record](customer-cache-20260925.md)); the
population and run conditions differ, so this is not a controlled regression comparison. The supported hypothesis is
that whole-world copy and previous-snapshot verification dominate the current save on this machine; the phase breakdown
does not establish how fast another storage design would be.

Compilation completed without errors in the connected Editor after the C# edit; the console showed zero errors after
the runs. Tests used unique OS-temp save paths and deleted them at teardown.

| Requested filter | Run identity (UTC) | Matched | Result | Artifact |
| --- | --- | ---: | --- | --- |
| EditMode `testName: FoodFactoryGame.Benchmarks.Tests.CustomerRuntimeBenchmarkTests` | NUnit `id=2`, 2026-09-25 22:20:38 | 1 | 1 failed on the two performance signals | [benchmark XML](artifacts/customer-save-phases-20260925.xml) |
| EditMode `testName: FoodFactoryGame.Goods.Tests.HeldSaveTests` | NUnit `id=2`, 2026-09-25 22:21:23 | 4 | 4 passed | [held-save XML](artifacts/save-phases-held-20260925.xml) |
| EditMode `testName: FoodFactoryGame.Goods.Tests.CompanyCashTests` | NUnit `id=2`, 2026-09-25 22:21:36 | 8 | 8 passed | [cash XML](artifacts/save-phases-cash-20260925.xml) |

The design comparison is [decision 0025](../decisions/0025-save-cost-at-customer-scale.md), proposed rather than implemented.
