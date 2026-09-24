# Verification: sell counter (2026-09-24)

Decision: [0013](../decisions/0013-sell-counter.md).

Compilation: live Editor `recompile`, no errors. Authoring: `run_script AgentScripts/BuildDevSite.cs` (`BuildDevSite.Run` → "DevSite authored"), then FishNet "Refresh Default Prefabs"; the documented `assetPath hash of 0` errors appeared during the run and the prefab hashes were restored. `DefaultPrefabObjects.asset` (pure reorder) and `EquipmentGhost.mat` (script-vs-tuned blend drift, unrelated) were reverted.

| Run | Filter | Matched | Result | Artifact |
| --- | --- | --- | --- | --- |
| Live Editor `run_tests` editor, async (NUnit test-run id 2) | none (all EditMode) | 119 (Baseline 4, Goods 80, Session 35) | 119 passed | [editmode.xml](sell-counter-20260924/editmode.xml) |
| Live Editor `run_tests` playmode, async (NUnit test-run id 2), final code | none (all PlayMode) | 13 | 12 passed, 1 failed | [playmode.xml](sell-counter-20260924/playmode.xml) |
| Live Editor host in `DevSite` via `SessionRoot.Configure` with an isolated save and identity (not the application save) | n/a | n/a | counter placed; 6 test bread dropped in through `PlayerHud.ClickSlot`; HUD `$502.50` after one sale | [world.png](sell-counter-20260924/world.png), [counter-screen.png](sell-counter-20260924/counter-screen.png) |

The single PlayMode failure is the pre-existing `HotbarKeysOverAStackAndDropsOnTheHotbarAssignSlots` hover test (same message as the baseline recorded in [the SQLite record](sqlite-20260923.md) and [company cash](company-cash-20260924.md)).

Two PlayMode tests (`HostMovesSeededOvenAndRemoteSeesEveryStep`, `ServerRestartShowsOvenWhereLastCommitted`) first failed because they counted *all* equipment and the seed now has a counter. They were changed to count ovens / to expect exactly the seeded oven and counter; the seed was not changed to suit them.

New coverage:

- `SaleTests` (Goods, 8): sale recipes need exactly one result; one bread per 4 s paid to the company, sold goods leave the world; spoiled goods never sell; no company, no sale (`no-company`) and `Validate` rejects a sale on an ownerless site; pickup mid-sale refunds and pays nothing; a failed tick commit rolls back goods and cash together; a sale saved mid-way completes exactly once; a v5 row loads as v6.
- `DevWorldCounterTests` (Session, 2): the new world's counter sells for the dev company and commits; an older save gains the counter once and a held counter is never replaced.
- `SessionAuthoringTests` (+2, 1 changed): counter definition/prefab/icon; sell-bread recipe registers with the server; `DevSite` lists both equipment definitions and both recipes.
- `EquipmentPlacementTests.HostSellsBreadAtTheCounterAndRemoteSeesTheCash` (PlayMode, new): bread moved into the seeded counter through the slot path sells on the server clock; the committed save, the host HUD and a remote UDP client's baseline all show the cash (two sales).

Not verified: a player build; a two-process session.
