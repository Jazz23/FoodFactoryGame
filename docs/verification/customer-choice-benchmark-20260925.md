# Verification: customer choice benchmark (2026-09-25)

Scope: a **prototype, test-only** model of individual customer choice (GDD section 23, selected) measured at the section 28
target of 1,000 customers and 20 sites. It is evidence for the design of a customer system, not an implementation: nothing
in `Assets/Scripts` changed, and no customer contract is accepted by this record.

Code: `Assets/Tests/EditMode/Benchmarks/` (assembly `FoodFactoryGame.Benchmarks.EditModeTests`, category `Benchmark`).
`CustomerChoiceModel` keeps customers and restaurants in arrays. A customer scores the restaurants within 600 m only when
it gets hungry or gives up on a queue (price, cuisine fit, distance, reputation, wait), then travels (as a timer), queues,
orders, pays once, eats and goes home. There is no pathing, no GameObjects, no networking and no persistence. Every value is synthetic.

Compilation: live Editor `recompile`, no errors; console 0 errors / 0 warnings after the run.

| Run | Filter | Matched | Result | Artifact |
| --- | --- | --- | --- | --- |
| Live Editor `run_tests` editor, sync (NUnit test-run id 2, 2026-09-25 17:44:49Z) | assembly `FoodFactoryGame.Benchmarks.EditModeTests` | 6 | 6 passed | [xml](artifacts/customer-choice-benchmark-20260925.xml) |

Machine: AMD Ryzen 5 5600X (12 logical cores), Unity 6000.5.9f1, **Editor Mono JIT** (not a player or dedicated-server
build). Tick 0.1 s (an assumed 10 Hz; no server tick rate is decided).

| Scenario | Ticks (simulated) | Mean | p99 | Max | Outcome |
| --- | --- | --- | --- | --- | --- |
| 1,000 customers / 20 sites, published wait + logit | 12,000 (20 min) | 0.0011 ms | 0.0023 ms | 0.017 ms | 2,186 decisions, 1,386 served, 15 abandoned, peak queue 12 |
| Lunch rush: all 1,000 decide in one tick (15 worlds) | 1 each | median 0.301 ms | | 0.325 ms | 4,374 evaluations in one tick |
| 10,000 customers / 200 sites (6 km world) | 3,000 (5 min) | 0.0133 ms | 0.0257 ms | 0.092 ms | 5,162 decisions, 2,105 served, peak queue 23 |
| 1,000 / 20, live wait + always-best | 12,000 | 0.0012 ms | 0.0023 ms | 0.039 ms | 1,325 served, 36 abandoned, peak queue 21 |

Zero managed allocations per tick in steady state (Test Framework `Is.Not.AllocatingGCMemory` over 600 further ticks). The
negative control `AllocationCheck_DetectsAnAllocation` shows that the check does detect an allocation. `GC.GetAllocatedBytesForCurrentThread`
always reads 0 on Unity's Mono and cannot be used. Invariants hold after every run: queue and service counts match customer
states, and revenue equals exactly one payment per order. The same seed gives the same outcome.

Assertions (PROVISIONAL budgets, set by this benchmark and not agreed): 1,000/20 mean ≤ 0.5 ms and p99 ≤ 1 ms; lunch-rush
median ≤ 2 ms; 10,000/200 mean ≤ 5 ms. The measured values are two to three orders of magnitude inside these budgets.

Findings:

- The choice itself is not a scaling risk at the target. Even when every customer decides at once, a tick costs about 0.3 ms.
- Live queue plus always-best choice gave a higher peak queue (21 vs 12) and more abandonment (36 vs 15) than a published
  estimate with weighted choice. This is **one seed** and indicative only.

Not measured, and the likely real costs: pathing and animation of visible customers, replication to clients, SQLite
persistence of customer state, a dedicated-server/IL2CPP build, and the cost of the rest of the server tick alongside this one.
