# Verification: customers, first build (2026-09-25)

Decision: [0024](../decisions/0024-customer-simulation.md).

Compilation: live Editor `recompile`, no errors (only pre-existing obsolete-API warnings). Authoring: icons regenerated with
`AgentScripts/DrawItemIcons.ps1` (only `Table.png` is new; the other icons are byte-identical). `run_script
AgentScripts/BuildDevSite.cs` (`BuildDevSite.Run` → "DevSite authored") was followed by FishNet "Refresh Default Prefabs".
The documented drifts in `DefaultPrefabObjects.asset` (a pure reorder) and `EquipmentGhost.mat` were reverted. The script
regenerated `DevSite.unity` and `Truck.prefab` with new local file IDs (churn seen in earlier authoring commits) and wrote the
new `seats`, `tier`, `cuisine` and `truck` fields into existing content assets; these changes were kept as generated.
`DevSite` was not dirty before the PlayMode runs.

| Run | Filter | Matched | Result | Artifact |
| --- | --- | --- | --- | --- |
| Live Editor `run_tests` editor, async (2026-09-25 ~18:58Z) | none (all EditMode) | 239 (Baseline 4, Benchmarks 6, Goods 162, Session 67) | 238 passed, 1 failed | [editmode-results.json](customers-20260925/editmode-results.json) |
| Live Editor `run_tests` playmode, async (2026-09-25 18:57:47Z, before the HUD fix below) | assembly `FoodFactoryGame.Session.PlayModeTests` | 22 | 22 passed | (overwritten by the rerun) |
| Same, rerun after the HUD fix (NUnit start 2026-09-25 19:05:23Z) | assembly `FoodFactoryGame.Session.PlayModeTests` | 22 | 22 passed | [session-playmode.xml](customers-20260925/session-playmode.xml) |
| Live Editor `run_tests` playmode, async (2026-09-25 18:59:04Z) | assembly `FoodFactoryGame.Goods.PlayModeTests` | 1 | 1 passed | [goods-playmode.xml](customers-20260925/goods-playmode.xml) |

The one EditMode failure is `TruckTests.StepSizeDoesNotChangeTheOutcome`. It is already recorded as flaky in the architecture
status: split truck lots get random GUID IDs, which are used as an ordering tie-break. Across two runs in this session its
expected and actual strings swapped, which fits that cause. The customer code does not touch trucks. An earlier
`FoodFactoryGame.Goods.EditModeTests` run (162 matched) had the same truck failure plus one wrong assertion in the new
`CustomerTests`. That test checked reputation after 400 s, by which time drift had returned it to 0, so the assertion was moved
to just after the purchase. `WalkingOutSendsTheCustomerToAnotherRestaurantNeverBack` passed vacuously on its first run
(`Assert.Pass`, because the single customer went home); it was rewritten with ten customers and real assertions before the
full run.

The console after the runs showed only the fixture errors the PlayMode tests expect (`SpawnablePrefabs is null on
session-test-remote` / `goods-test-host` / `goods-test-remote`).

New or changed coverage:

- `CustomerTests` (Goods, 11): spawns at the district rate only toward restaurants in range; purchase takes one edible item,
  pays once, takes a seat and leaves; spoiled food is never served and walking out costs reputation; dine-in waits for a
  seat while takeaway behind is served; a walk-out re-chooses without that restaurant; an occupied table or serving counter
  is `occupied`; a failed tick commit rolls back purchase, cash and customer; a restored world continues exactly like one
  that never stopped; one long step equals 240 one-second steps; `Validate` rejects inconsistent customers; v12 upgrades.
- `SaleTests` (Goods, 9, rewritten): stations never start menu items (`customers-only`); legacy sale jobs complete once,
  wait instead of overflowing, need a company, refund on pickup, roll back on a failed commit, survive reload.
- `DevWorldCustomersTests` (Session, 2, new); `DevWorldCounterTests.NewWorldHasThePlacedCounterAndCustomersBuyThereForTheDevCompany`
  (the dev district's customers buy seeded bread, committed); `SessionAuthoringTests` (table definition and offer, menu
  attributes, DevSite lists).
- `EquipmentPlacementTests.HostSellsBreadAtTheCounterAndRemoteSeesTheCash` (PlayMode): bread dropped through the slot path
  is bought by customers from a **test-only** district (`test-district`, 10 m away, one takeaway customer a second); the HUD
  shows "Serving a customer: Bread for $2.50"; the committed save, the host HUD and a remote UDP client show both sales.

Running-game check (2026-09-25, live Editor play mode in `DevSite`, host started through `SessionRoot.Configure` with a save
and identity under the session scratch directory, not the application save). A **visual-check-only** district (10 m away,
1,800 dine-in customers an hour) and 20 bread on the counter were bootstrapped into that isolated save, and the host avatar
was moved inside the restaurant.

- [table-world-empty.png](customers-20260925/table-world-empty.png): the table (top, legs, two chairs each side) inside the
  restaurant under the top-down indoor camera; a first sale had already raised the cash to $502.50.
- [table-screen.png](customers-20260925/table-screen.png): the table window reads "Seats taken: 4/4" and "Served 4, walked
  out 0, reputation +20"; the table hint shows in the readout.
- [counter-screen-before.png](customers-20260925/counter-screen-before.png) showed two defects: with 16 edible bread on the
  counter and every seat taken, the line read "12 customers waiting: put edible Bread in the input, or free a seat", which
  sends the player to the wrong fix; and the longer counter hint wrapped to a third line outside the readout box.
  `PlayerHud.WaitingText` now names the first blocker (no edible menu item in this counter, or every seat taken while a
  dine-in customer queues), and the counter hint was shortened back to two lines.
- [counter-screen-after.png](customers-20260925/counter-screen-after.png), after a restart of the same save: "20 customers
  waiting: every table seat is taken; place more tables", with the hint inside the box. The restart also showed the saved
  customers resuming (4 eating, 18 queued before new arrivals).

Not verified: the "Serving a customer" line was not captured in the running game (the PlayMode test asserts its text); no
walking customer characters exist to check; no independent visual review; no player build; no separate-process multiplayer
check; no measurement against the 1,000-customer target with this runtime code (the earlier benchmark measured a separate
test-only model).
