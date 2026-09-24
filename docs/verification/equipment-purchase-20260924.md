# Verification: equipment purchases (2026-09-24)

Decision: [0017](../decisions/0017-equipment-purchases.md).

Compilation: live Editor `recompile`, no errors, after each change. Authoring: `run_script AgentScripts/BuildDevSite.cs` →
"DevSite authored", then FishNet "Refresh Default Prefabs". `DefaultPrefabObjects.asset` (reorder) and `EquipmentGhost.mat`
(known drift) were reverted, as documented in development.md. `Dough5.asset` and `Belt10.asset` gain `equipment: {fileID: 0}`.

| Run | Filter | Matched | Result |
| --- | --- | --- | --- |
| Live Editor `run_tests` editor, async | none (all EditMode) | 143 | 143 passed (after one fix to a test, see below) |
| Live Editor `run_tests` playmode, async | none (all PlayMode) | 15 | 14 passed, 1 failed |
| Live Editor `run_tests` playmode, async, **baseline** (`main` at 6533b56, changes stashed and recompiled) | `HotbarKeysOverAStackAndDropsOnTheHotbarAssignSlots` | 1 | 1 failed with the same message |

No NUnit XML was copied for these runs. The Pipeline runner returned results inline and gave no artifact path. The counts
above come from those responses.

- The first EditMode run had 142/143 passing. `BuyingEquipmentPaysAndDeliversAHeldMachineThatCanBePlaced` bought a second
  600-cent oven with 400 cents left, and the server correctly refused it (`insufficient-funds`). The test now tops up cash
  first. This was a test error, not a code change.
- The PlayMode failure is `HotbarKeysOverAStackAndDropsOnTheHotbarAssignSlots` ("The pointer is over the dough stack"). It
  fails the same way on unmodified `main`, so this step did not cause it. It was intermittent in the
  [supplier record](supplier-20260924.md) and now fails on every run in this Editor, with or without this change.

New coverage:

- `PurchaseTests` (Goods, +6): equipment offers must be valid content, and their IDs cannot clash with goods offer IDs in
  either direction. Buying delivers a held machine with the template's footprint, committed together with the debit; a
  second purchase is a separate piece; the machine places and gets a station. A retried request is charged once, including
  after a reload. Rejections (`no-layout`, `forbidden`, `no-company`, `no-inventory`, `insufficient-funds`) change nothing in
  the whole snapshot. A full goods inventory does not block a machine. A failed commit rolls back and the request can be
  retried.
- `SessionAuthoringTests` (+1, 1 changed): `Oven1.asset` sells the authored oven definition for 15000 cents and registers
  as valid server content. `DevSite` lists three offers.
- `EquipmentPlacementTests.HostBuysAnOvenFromTheSupplierAndPlacesIt` (PlayMode, new): the host buys through the Supplier
  window's path. The oven arrives held, shows in an inventory slot, and places as a second oven with a visual. Cash and
  station agree in the committed save.

Not verified: a running-game screenshot of the Supplier window's new row, a player build, a two-process session.
