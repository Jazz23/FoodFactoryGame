# 0016 - Periodic Commits for Clock Ticks

Date: 2026-09-24

Status: accepted by the project owner (option 2 of three offered, after [0015](0015-wal-and-held-world-connection.md));
implemented. See [architecture status](../architecture.md#implemented-sqlite-storage-2026-09-23) and the
[verification record](../verification/wal-20260924.md).

## Context

After 0015 the served world still committed once a second, on the main thread, for every clock tick. On a hard disk that
was about 50 ms each (`commits=121 avg=50.2ms`), still a visible hitch every second. Player commands are rare by
comparison; the per-second cost came from ticks.

## Decision

- The server advances the clock in memory every second (`GoodsWorld.AdvanceUncommitted`) and broadcasts it as before.
- It commits the world every **10 s** (`GoodsNetworkBridge.CommitIntervalSeconds`) with `TryCommitDurably`, which writes
  only if the world is ahead of its last save (`HasUncommittedChanges`).
- Player commands are unchanged: each still commits before it is acknowledged. Because a commit saves the whole world,
  it also saves every tick before it, so an acknowledged action never depends on unsaved ticks.
- A clean stop (`SessionRoot.Shutdown`, the bridge's `OnStopServer`/`OnDestroy`) commits pending ticks before closing
  the save.
- If a periodic commit fails, memory is kept (not rolled back), commands are refused with `persistence-unavailable` as
  before, and the clock waits, retrying the commit, so memory never runs more than one interval ahead of the save.

## Consequences

- **A crash can lose up to 10 s of simulated time** and everything it produced: bakes, sales, spoilage, belt movement.
  They roll back together as one revision, so no goods or cash are duplicated or lost relative to each other, and no
  acknowledged player action is lost. Clients may have seen those ticks before the crash; after a restart the world
  resumes from the last save.
- Tick commits drop from about 60 to about 6 a minute; on a hard disk the remaining hitch happens every 10 s instead of
  every second. It is not removed; a background writer (option 3) remains the way to remove it, together with the
  decision-0012 move to relational tables.
- `TryAdvanceDurably` stays for callers that need a durable step (tests, tools). `GoodsSnapshotStore.Save` and `Load`
  record the committed revision on the world (`MarkCommitted`).
- Tests that read the save right after a tick now wait for the interval or stop the server first.
