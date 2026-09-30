# Verification: customers in generated worlds (decision 0030)

Date: 2026-09-30. Unity 6000.5.9f1, Windows 11, AMD Ryzen 5 5600X, live Editor through the Pipeline MCP `run_tests` tool.
That tool reports the filter, matched count and per-test results but no run ID, so each run is identified by the
`start-time` in its NUnit XML, copied to `artifacts/`. Captures are in
[customers-worldgen-20260930/](customers-worldgen-20260930/).

## Baseline before any change

Leftover from piece 2: the reworked $1,000,000 purchase test had not run. It ran first: `WorldGenSessionTests`
(PlayMode) 4 matched, 4 passed, including `ABuildingBoughtThroughThePanelReachesBothClients`. Also: Goods EditMode 179/179
and Session EditMode 93/93.

## Final automated runs (final code)

| Filter (mode) | Matched | Result | Artifact (`start-time`) |
| --- | --- | --- | --- |
| assembly `FoodFactoryGame.Goods.EditModeTests` (EditMode) | 183 | 183 passed | `customers-worldgen-20260930-goods-editmode.xml` (18:40:38Z) |
| assembly `FoodFactoryGame.Session.EditModeTests` (EditMode) | 99 | 99 passed | `customers-worldgen-20260930-session-editmode.xml` (18:41:41Z) |
| assembly `FoodFactoryGame.World.EditModeTests` (EditMode) | 21 | 21 passed | `customers-worldgen-20260930-world-editmode.xml` (18:42:03Z) |
| assembly `FoodFactoryGame.Baseline.EditModeTests` (EditMode) | 4 | 4 passed | (not archived) |
| assembly `FoodFactoryGame.Benchmarks.EditModeTests` (EditMode) | 8 | 8 passed | `customers-worldgen-20260930-benchmarks.xml` (18:42:59Z) |
| assembly `FoodFactoryGame.Goods.PlayModeTests` (PlayMode, async) | 2 | 2 passed | `customers-worldgen-20260930-goods-playmode.xml` (18:43:21Z) |
| assembly `FoodFactoryGame.Session.PlayModeTests` (PlayMode, async) | 31 | 31 passed | `customers-worldgen-20260930-session-playmode.xml` (18:43:44Z) |

Console after the runs: only the three expected FishNet fixture lines (`SpawnablePrefabs is null on session-test-remote`,
`equipment-test-remote` and `worldgen-test-remote`, declared with `LogAssert.Expect`).

New or changed tests:

- Goods `CompetitorLotTests` (4): competitors with lots save and reload; a v14 row loads with every competitor unlinked
  and the next commit is v15; lots that are unknown, for sale, already linked or owned are rejected and change nothing,
  and a catalog that disagrees is refused; choice considers only restaurants within each district's own range, including
  a restaurant and a district added later.
- Session `GeneratedWorldCustomersTests` (5): derivation is byte-identical for the same seed generated elsewhere and
  different for another seed, with one district per block (rates add up, centres inside) and one competitor per
  competitor lot (lot, access point, district cuisine and tier); a new world gets them once, with the counter and table
  inside the shell, door cells clear, two rows in front of the counter and room for the oven, committed and unchanged on
  reload; a piece 2 world gains them once, without a second counter when it already has one, and is unchanged on the next
  load; dev worlds keep their own district and unlinked competitors; a customer from a map district buys bread at the
  starting counter.
- `GeneratedWorldTests.ANewWorldCreatesTheStartingSiteOnceAndReloadsUnchanged` no longer expects zero districts and
  competitors. `WorldGenSessionTests.ANewWorldStartsThePlayerOnItsOwnRestaurant` expects the counter and table.
  `SessionAuthoringTests` expects the counter offer and checks it (`SupplierCounterOfferSellsTheCounterDefinition`).
- PlayMode `WorldGenSessionTests.AWholeSaleInTheStartingRestaurantReachesBothClients`: see the multiplayer check below.

Failures on the way, with their causes: (1) the whole WorldGen PlayMode fixture failed after the scene rebuild, because
the art catalog reference was lost when the builder opened the scene. The builder was fixed and the reference restored.
(2) The NavMesh check in the sale test failed: the counter one row from the wall left no walkable space in front of it.
It now stands two rows in. (3) Test setups: a v14 save that wrote no new revision, and a test that deleted the save
holding the layout. Both fixtures were corrected.

## Multiplayer check (listen server)

`AWholeSaleInTheStartingRestaurantReachesBothClients`: WorldGen hosted in play mode on an isolated temp save (seed
`piece-two`), plus a second client-only NetworkManager joined over loopback UDP.

- The host bought an oven and dough from the supplier ($150.00 + $2.50), placed the oven inside the shell as far from
  the doors as possible, loaded 2 dough, baked bread and moved it onto `start-counter`.
- A TEST-ONLY district with a 5 m range on the site's map point sent a customer who bought bread. The host's cash equals
  start − $152.50 + $2.50 × served.
- The remote client's own replicated site shows the new cash and the customers at the restaurant.
- The committed save's cash equals its own sales count.
- The runtime NavMesh is built for the lot. A path from the far end of the street band to the counter's ordering spot is
  complete and passes through a door, while the straight line is blocked by a wall. Customer figures are drawn.

This is the in-Editor listen-server fixture, not a separate-process build. No player build was made for this change.

## Scale gate

`CityCustomerBenchmarkTests`: seed `piece-two`, 25 districts, 314 competitors and one player restaurant, with a 1,800 s
warm-up, then 300 ticks and a commit every 10 s.

| Run | Tick mean / p99 / max | 100-decision burst | 60 s catch-up | Commit avg / max | Payload |
| --- | --- | --- | --- | --- | --- |
| alone (18:33:32Z) | 0.111 / 0.678 / 1.373 ms | 3.32 ms (123 candidates) | 2.30 ms | 10.9 / 31.7 ms | 157 KB |
| in the assembly run (18:42:59Z) | 0.049 / 0.195 / 0.309 ms | 3.29 ms | 2.21 ms | 6.9 / 9.8 ms | 157 KB |

Budgets (0025): 16.7 ms tick p99 and burst, 50 ms commit, 1 MB payload. All were met, and ranges were not tightened. At
steady state about 135 customers are in the city (69 eating, 56 travelling). The player restaurant served 3 of 1,017 in
the run. The existing 20-restaurant `CustomerRuntimeBenchmarkTests` in the same run reported tick p99 0.33 ms, commit
max 21.4 ms and 338 KB.

Demand probe (isolated temp save, `eval`, seed `piece-two`, counter stocked with 20 bread): first sale after 360 s, and 3
sales in the first hour. Seven map districts reach the starting restaurant, each with 47 to 115 candidate restaurants.

## Visual acceptance

Host in play mode on WorldGen, isolated temp save (seed `piece-two`). The counter was stocked, and a capture-only district
with a 5 m range was added so customers came quickly. The avatar was moved inside, which gives the indoor top-down camera
with the roof hidden.

- `customers-1.png`: the shell with its south doorway, the counter and the table; a customer at the counter's ordering
  side; another customer on the sidewalk.
- `customers-2.png` (a few seconds later): three customers queued beside the counter, one seated at the table, another
  walking along the sidewalk. At that moment the server had 11 customers at this restaurant (4 eating, 6 queued, 1
  ordering) and 9 served.

**Not yet reviewed** by anyone other than the implementer; this acceptance item stays open until then. Things for the
reviewer to judge: the queue forms beside and behind the counter (fixed site-axis offsets, not facing the door), and the
street band is flat whatever the road's height.
