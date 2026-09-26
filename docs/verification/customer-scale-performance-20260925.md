# Verification: customer-scale save and tick performance (2026-09-25)

The benchmark used Unity 6000.5.9f1 Editor Mono on an AMD Ryzen 5 5600X, with 10 player restaurants, 10 competitors,
1,005 customers after a 420 s warmup, one accepted transfer, 300 measured one-second ticks and 30 commits. Its SQLite
database was created under a unique OS temp path; no application database or migration was touched. All test XML files
below are copied from Unity's `TestResults.xml` immediately after their run.

| Requested filter | Run identity (UTC) | Matched | Result | Artifact |
| --- | --- | ---: | --- | --- |
| EditMode assembly `FoodFactoryGame.Goods.EditModeTests` | NUnit id 2, 2026-09-26 04:31:10Z | 167 | 167 passed | [Goods XML](artifacts/customer-scale-goods-final-20260925.xml) |
| EditMode assembly `FoodFactoryGame.Goods.EditModeTests`, later rerun | NUnit id 2, 2026-09-26 04:33:54Z | 167 | 166 passed, known nondeterministic truck-ordering failure | [Goods flake XML](artifacts/customer-scale-goods-flake-20260925.xml) |
| PlayMode assembly `FoodFactoryGame.Goods.PlayModeTests` (async) | NUnit id 2, 2026-09-26 04:34:27Z | 1 | 1 passed | [Goods multiplayer XML](artifacts/customer-scale-goods-playmode-final-20260925.xml) |
| PlayMode assembly `FoodFactoryGame.Session.PlayModeTests` (async) | NUnit id 2, 2026-09-26 04:34:57Z | 24 | 24 passed | [Session XML](artifacts/customer-scale-session-final-20260925.xml) |
| EditMode testName `FoodFactoryGame.Benchmarks.Tests.CustomerRuntimeBenchmarkTests` | NUnit id 2, 2026-09-26 04:36:13Z | 1 | 1 passed | [Benchmark XML](artifacts/customer-scale-benchmark-final-20260925.xml) |

Compilation completed without errors before the final tests. The Session fixture emitted one expected `SpawnablePrefabs
is null on session-test-remote` error, explicitly consumed by `LogAssert.Expect` in `SessionBootstrapTests`; no new
compilation error was present. The first Goods run after the save change matched 164 with 163 passing and the already
documented nondeterministic `TruckTests.StepSizeDoesNotChangeTheOutcome` failing. The later 167-test run passed in full;
two subsequent reruns matched 167 and failed only on that same truck test, whose random split-lot IDs can change ordering.
The new tick-failure fixture first failed because its test district omitted required validator fields; after correcting
that fixture and adding the delayed-save case, focused `UncommittedTickTests` passed 7/7.

## Save change before the 0016 amendment

The save stopped copying the world and serialized its validated live state once under the world lock. The first
intermediate benchmark showed save copy 0.00 ms, 30 saves at 16.8 ms mean / 25.5 ms maximum, and tick p99 30.79 ms while
the per-tick rollback copy remained. A second intermediate run measured 19.2 ms mean / 57.6 ms maximum (one decision-0012
warning) and tick p99 31.43 ms; its validation and JSON phases had unusually large single samples. The intermediate XML
was superseded by later Unity runs, but the console measurements are retained here. These observations support removal
of the save copy; they do not establish that save spikes cannot recur.

## Final implementation

`AdvanceUncommitted` now restores the last committed in-memory payload on a simulation exception. A domain test forces
an exception after an unsaved customer purchase and checks exact equality with the last saved world, including goods,
company cash, customer state and revision. A subsequent tick replays the sale once. The initial save is required before
uncommitted ticking. Failed periodic commits still keep unsaved memory, and player commands keep their pre-command copy.
The independent persistence review found two additional risks, which were fixed before the final multiplayer run:
`MarkCommitted` now ignores a delayed older save notification, and the bridge tags site baselines with a rollback epoch.
On a tick exception it broadcasts the restored world; clients accept the lower revision in the new epoch and discard late
old-epoch baselines. The Goods PlayMode fixture exercised this through the real host and remote UDP clients. Bridge startup
also rejects a JSON-equal world instance that lacks its own committed payload.

| Final benchmark measurement | Result |
| --- | ---: |
| 300 ticks, mean / p99 / maximum | 0.18 / 0.72 / 3.33 ms |
| 30 saves, mean / maximum | 15.5 / 34.0 ms |
| Save copy phase, mean / maximum | 0.00 / 0.00 ms |
| Save validation, mean / maximum | 3.86 / 23.61 ms |
| Save JSON, mean / maximum | 3.65 / 8.75 ms |
| SQLite transaction before `COMMIT`, mean / maximum | 5.84 / 12.19 ms |
| `COMMIT` including sync, mean / maximum | 2.13 / 5.83 ms |
| One accepted transfer, total / save | 20.45 / 12.96 ms |
| Final payload | 338 KB |

The final run stayed below the benchmark's provisional 16.7 ms tick p99 and decision-0012's 50 ms commit / 1 MB payload
signals. These are Editor Mono measurements from one local scenario, not a target-hardware player/server acceptance run
or a command-latency distribution. [Decision 0025](../decisions/0025-save-cost-at-customer-scale.md) is deferred; revisit
it when either decision-0012 warning fires again in representative served-world measurements.
