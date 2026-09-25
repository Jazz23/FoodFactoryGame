# 0025 - Save Cost at Customer Scale

Date: 2026-09-25

Status: proposed, **not accepted**. The one-file fast save path described below is implemented; no storage migration or
background writer is approved. Decisions [0012](0012-company-cash.md), [0015](0015-wal-and-held-world-connection.md),
and [0016](0016-periodic-tick-commits.md) remain the accepted contracts.

## Context and measurement

The GDD section 28 targets about 1,000 customers and 20 sites and 60 FPS on a mid-range PC. These are approximate scale
and client frame targets, not a specified server tick budget. The current server saves a checksummed, complete JSON world
in SQLite WAL with `synchronous = FULL`. It commits a clock tick every 10 seconds and commits an accepted player command
before acknowledging it. Recovery can fall back to the previous valid world revision.

The [initial isolated Editor benchmark](../verification/save-phases-20260925.md) used 10 player restaurants, 10
competitors and 1,005 customers at the command. On an AMD Ryzen 5 5600X in Unity 6000.5.9f1 Editor Mono, 30 tick
commits averaged 38.0 ms, reached 79.1 ms, and wrote a 338 KB payload. It led to the first draft of this proposal.

`GoodsSnapshotStore` now remembers the exact row it successfully wrote through a held connection. During the next
`BEGIN IMMEDIATE` transaction, it compares the stored head row (revision, world ID, payload and checksum) with that
known-valid row. An exact match skips deserializing, hashing and validating the previous snapshot; any difference uses
the original `LatestValid` recovery and quarantine path. Loads and unheld saves still validate as before. The cache is
removed on release or failed commit and never persists. No database schema, sync policy or acknowledgment rule changed.
The [rerun](../verification/fast-save-20260925.md) used the same synthetic workload and an isolated temp database.

| Save phase | Initial mean / max | Fast-path mean / max |
| --- | ---: | ---: |
| World copy | 9.63 / 26.98 ms | 9.13 / 26.33 ms |
| Whole-world validation | 3.45 / 5.88 ms | 3.28 / 4.42 ms |
| JSON conversion | 4.06 / 5.63 ms | 3.71 / 5.93 ms |
| SQLite transaction before `COMMIT` | 18.44 / 50.08 ms | 6.33 / 7.49 ms |
| `COMMIT`, including WAL disk sync | 2.45 / 7.32 ms | 2.58 / 9.37 ms |
| Total save | 38.0 / 79.1 ms | 25.0 / 42.1 ms |

The transaction phase still includes checking the head row, hashing and inserting the new payload, and deleting an older
revision. The `COMMIT` phase includes SQLite bookkeeping as well as disk sync; it is not a direct fsync measurement.
One accepted player transfer at 1,005 customers fell from 52.47 ms (40.54 ms save) to 31.81 ms (23.55 ms save).
These are one command per run, not latency distributions. The isolated saves used the OS temp path on `C:`; the drive
medium was not established. Results are Editor Mono measurements, not a player or dedicated-server benchmark.
An earlier post-change run, before a cache-update placement adjustment, measured 26.2 ms mean / 31.0 ms max save and a
34.26 ms command; the final figures above come from the final code.

The fast-path run stayed under decision 0012's 50 ms commit warning (42.1 ms max) and 1 MB payload warning (338 KB).
This is one local run, so it does not establish the target PC or command p99. The clock-tick p99 issue is separate below.

## Proposed decision

Keep the optimized whole-world snapshot store for now. The measured save no longer crosses the warning signals in this
scenario, so the numbers do not justify immediate relational migration or background saving. Reconsider whole-world
relational storage if representative player/server runs or larger worlds show sustained save or command latency. Any
replacement still needs an isolated migration dry run, recovery and replay tests, and a comparable benchmark.

| Option | Effect suggested by these numbers | Consistency and implementation cost | Disposition |
| --- | --- | --- | --- |
| (a) Customers-only SQLite table beside the JSON world | Could shrink part of the 338 KB payload, but the save still copies, validates and converts the whole world. Customer share of the payload was not measured. | Customer state, counter goods, seats, payments and cash cross the table boundary. Fallback to an older JSON revision must restore matching customer rows; an ordinary current-state table cannot do that. Requires coordinated revision history and recovery. | Reject as a separate store under decision 0012. |
| (b) Relational storage for the whole world | Changed-row writes might remove repeated whole-world copy, validation and JSON work, but the measured pre-commit transaction is now 6.33 ms, not 18.44 ms. A speedup is an unmeasured design inference. | Requires one atomic revision for goods, customers, cash, outcomes and other state; stable identities, idempotent replay, database constraints, prior-revision recovery, migration and rollback tests. | Defer until representative measurements justify the migration. |
| (c) Save tick commits in the background | Could move a 25.0 ms periodic save off the main thread if the entire save runs there. Offloading only `COMMIT` addresses 2.58 ms on average. It does not remove the 31.81 ms acknowledged command. | Needs an immutable revision handoff, ordered commands and ticks, backpressure and failure recovery. Commands must still wait for durable commit before acknowledgment. Thread safety of Unity JSON conversion cannot be assumed. | Defer as a possible scheduling improvement if tick-commit hitches remain visible in player builds. |

## Separate item: rollback copy during clock ticks

`AdvanceUncommitted` copies the world before each one-second step for exception rollback. In separate 30-sample loops the
copy averaged 8.87 ms initially and 8.29 ms after the save change; the simulation step itself averaged 0.20 and 0.14 ms.
The 300-tick p99 was 22.44 ms initially and 20.51 ms after the change, above the benchmark's provisional 16.7 ms
one-frame signal both times. These runs do not show a regression caused by the save path: the save change does not alter
`AdvanceUncommitted`, and the observed p99 difference may be run-to-run noise. Investigate its rollback strategy and
measure tick latency separately from this storage decision. No rollback behavior is changed here.

## Required proof before replacing the store

- Use an explicit isolated SQLite path and run migration and reconciliation as a dry run first; leave application databases untouched.
- Preserve an atomic committed revision across goods, customers, cash, equipment and request outcomes. A damaged newest
  revision must recover a consistent earlier one; cancelled or failed writes must not duplicate payments or lose goods.
- Measure the same 1,000-customer scenario on the target hardware and a player/server build: mean, p99 and maximum of
  tick commits and accepted commands, payload or changed-row count, and failure/recovery behavior. The one-command sample
  here cannot establish a command p99.
- Track the `AdvanceUncommitted` rollback-copy cost under a separate performance item and verify any change to its
  failure semantics with domain tests.

No schema, threading, acknowledgement or crash-window change is authorized by this proposal.
