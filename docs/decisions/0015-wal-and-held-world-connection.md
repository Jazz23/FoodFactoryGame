# 0015 - WAL and a Held World Connection

Date: 2026-09-24

Status: accepted by the project owner (option 1 of three offered); implemented. See
[architecture status](../architecture.md#implemented-sqlite-storage-2026-09-23) and the
[verification record](../verification/wal-20260924.md).

## Context

In a two-process playtest the host froze about once a second; the guest did not. The host's `[Goods]` summary showed
`commits=522 avg=250.6ms max=1087.5ms payload=14.2KB`: the served world commits on every clock tick, on the main thread, and
each commit cost far more than its 4–14 KB payload explains. The save was on a spinning hard disk (Seagate ST2000DM001).

Every `Save` opened a new connection, and the database used SQLite's default rollback journal with `synchronous = FULL`:
each commit created, synced and deleted a journal file and synced the database. An Editor benchmark of 100 tick commits
(isolated temp databases) measured 324.6 ms average / 768.6 ms max on the hard disk and 4.7 ms / 12.7 ms on an SSD.

This tripped decision 0012's 50 ms "revisit" signal, but the cost was sync overhead, not payload size, so it is not by
itself a reason to move the world to relational tables.

## Decision

- Goods databases use `journal_mode = WAL`, still with `synchronous = FULL`: an acknowledged mutation still survives a crash
  immediately after the reply. A commit appends to the WAL and syncs it once.
- `GoodsSnapshotStore.Hold(path)` keeps one connection open for `Save` until `Release(path)`. Closing the last connection
  checkpoints and deletes the WAL, which would cost about what the old journal did, so a per-save open/close gets little
  from WAL. The goods bridge holds the served save from `InitializeServer`; `SessionRoot.Shutdown` (and the bridge's
  `OnStopServer`/`OnDestroy`) release it, so the files can be deleted once the server has stopped.
- A held connection is never reused after a failed commit (it may be mid-transaction if `ROLLBACK` failed); the next
  `Save` reopens it. Unheld saves (seeding, tests, legacy import) keep the open/commit/close pattern.
- Commits stay synchronous on the main thread; the world's rollback-on-failed-save contract is unchanged.

Rejected for now: saving clock-only ticks every N seconds (changes what a crash may lose; needs its own decision), and a
background writer thread (changes the failed-save contract; better done together with the decision-0012 move to relational
tables with incremental writes).

## Consequences

- Measured after the change (same benchmark): 37.0 ms average, 84.5 ms p95, 117.3 ms max on the hard disk; 1.3 ms average
  on the SSD. The rebuilt two-process host on the hard disk logged `commits=121 avg=50.2ms max=145.6ms`. A world save on a
  hard disk can still cost a few frames per tick.
- While a server runs, `world.db-wal` and `world.db-shm` sit next to `world.db`. Copy all three, or stop the server, to back
  up a live save. `Load` and older builds read WAL databases normally.
- The `[Goods]` commit average now reflects the held path, so decision 0012's thresholds measure payload growth more than
  disk overhead.
