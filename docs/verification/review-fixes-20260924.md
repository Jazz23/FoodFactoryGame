# Verification: review fixes for sales and purchases (2026-09-24)

A `/code-review` (high) of steps 2 and 3 ([0013](../decisions/0013-sell-counter.md), [0014](../decisions/0014-supplier-purchases.md)) found 8 issues; all were fixed before merging `company-cash` into `main`:

- Supplier Buy buttons are not focusable, so a keyboard Submit cannot repeat a purchase.
- A sale whose credit would overflow the balance waits at zero remaining (goods kept) instead of throwing and stopping the clock.
- Rejected purchases are answered without being recorded or committed; only an accepted purchase is stored for replay.
- A long server step serves successive customers with the leftover time, so revenue no longer depends on tick size.
- The counter hint is decided once when a machine screen opens, not by LINQ every frame.
- Purchases use the client's subscribed site instead of a hard-coded dev site.
- Null pattern matching in `Buy`; the rejection test covers `no-inventory` and compares the whole snapshot.

| Run | Filter | Matched | Result | Artifact |
| --- | --- | --- | --- | --- |
| Live Editor `run_tests` editor, async (NUnit test-run id 2) | none (all EditMode) | 128 (Baseline 4, Goods 88, Session 36) | 128 passed | [editmode.xml](review-fixes-20260924/editmode.xml) |
| Live Editor `run_tests` playmode, async (NUnit test-run id 2) | none (all PlayMode) | 14 | 13 passed, 1 failed | [playmode.xml](review-fixes-20260924/playmode.xml) |

The failure is the intermittent `HotbarKeysOverAStackAndDropsOnTheHotbarAssignSlots` hover test described in [the supplier record](supplier-20260924.md).

New tests: `SaleTests.OneLongStepSellsWhatManyShortStepsWould`, `SaleTests.SaleThatWouldOverflowTheBalanceWaitsWithoutStoppingTheClock`, `PurchaseTests.RejectedRequestRetriedLaterIsChargedAtMostOnce`.
