# Verification: company cash (2026-09-24)

Decision: [0012](../decisions/0012-company-cash.md).

Compilation: live Editor `recompile`, no errors; the four CS0618 warnings are in pre-existing files (FishNet upgrade menu, `BeltPlacementTests`).

| Run | Filter | Matched | Result | Artifact |
| --- | --- | --- | --- | --- |
| Live Editor `run_tests` editor, async (NUnit test-run id 2) | none (all EditMode) | 107 (Baseline 4, Goods 72, Session 31) | 107 passed | [editmode.xml](company-cash-20260924/editmode.xml) |
| Live Editor `run_tests` playmode, async (NUnit test-run id 2) | none (all PlayMode) | 12 | 11 passed, 1 failed | [playmode.xml](company-cash-20260924/playmode.xml) |
| Live Editor `run_tests` playmode, baseline (changes stashed, HEAD `dd7aa5d`) | testName `HotbarKeysOverAStackAndDropsOnTheHotbarAssignSlots` | 1 | 1 failed, same message | status only |
| Live Editor host in `DevSite` via `SessionRoot.Configure` with an isolated save directory and identity file (not the application save) | n/a | n/a | HUD shows `$500.00`; `[Goods]` summary logged | [hud-cash.png](company-cash-20260924/hud-cash.png) |

The PlayMode failure (`The pointer is over the dough stack. Expected "dough" but was null`) also fails on the unmodified baseline and was already recorded in [the SQLite record](sqlite-20260923.md); it is not caused by this change.

New coverage:

- `CompanyCashTests` (Goods, 8): invalid companies rejected without changing the world; companies and cash round-trip through `world.db`; a v4 payload row in `world.db` (written with `SnapshotDatabase.WritePayload`) loads as v5 with no companies and is rewritten as v5; rows with negative cash fall back to the previous commit and are quarantined; site views show only the owning company; cash never goes negative, and overflow throws and restores the prior state; a failed commit restores cash; commit stats count only committed saves and report the stored payload's byte length.
- `DevWorldCompanyTests` (Session, 2): a new dev world starts with `dev-company` and 50000 cents, and reopening writes nothing; an older (v4, pre-SQLite) save imported through `LoadOrCreate` gains the company exactly once, and a restart after spending never tops it up.
- `GoodsListenServerTests` (PlayMode) extended: the host's site baseline carries only its company's cash, and a remote client over loopback UDP receives only the rival site's company in its baseline.
- Existing v1/v2/v3 upgrade tests updated to expect the current schema (v5).

First `[Goods]` measurement: `commits=268 avg=6.5ms max=32.0ms payload=2.0KB`, about 60 s after the host started. The counters are per process, and this Editor had run the test suites in the same domain, so the count and timings include test saves. The payload size is the dev world's.

Not verified: a player build; a two-process session; a sale or purchase (not implemented).
