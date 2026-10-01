# 0030 - Customers in Generated Worlds

Date: 2026-09-30

Status: **accepted and implemented** 2026-09-30. The owner approved the plan and chose a pre-equipped starting restaurant.
Everything marked PROTOTYPE below is placeholder tuning chosen by the implementer, not design data. Builds on
[0024](0024-customer-simulation.md) (customer simulation), [0025](0025-save-cost-at-customer-scale.md) (save budgets) and
[0028](0028-sites-for-generated-buildings.md) (lots, and competitors linked to lots). Evidence:
[verification record](../verification/customers-worldgen-20260930.md).

## Context

Generated worlds (0028 piece 2) had no districts or competitors, so no customers ever came. Only the dev world had a
counter, and the supplier did not sell one. Customer figures follow a NavMesh, but generated worlds hide DevSite's baked
NavMesh. Customer choice had been benchmarked with 20 restaurants, and a generated city has about 300 competitors.

## Owner decision (2026-09-30)

- **The starting restaurant comes pre-equipped** with a placed counter and a four-seat table. It needs an oven and dough
  (bought from the supplier) before it can sell bread.

## Decision

1. **Districts come from the map.** Each block (`WorldDistrict.Areas` entry) of each layout district becomes one spawning
   `GoodsDistrict` with ID `district-<layout district>-<block index>` (for example `district-downtown-1`). It spawns
   customers at the block's centre. The layout district's customers per hour are shared between its blocks by area, using
   largest remainder so the blocks add up exactly to the district's rate. The records are a pure function of the stored
   layout (`WorldLayoutCustomers.Districts`) and are never stored separately.
2. **Competitors come from competitor lots.** Each competitor-owned building with a lot becomes a `GoodsCompetitor` with ID
   `competitor-<building>` and `LotId` = its lot. Its map position is the lot's access point, the same rule as sites.
   Cuisine is drawn from its district's weights. Tier is the district's `MinRecipeTier`. Price, servers, service time and
   seats come from an FNV-1a hash of the building ID (`WorldLayoutCustomers.Competitors`). Competitor buildings stay
   listed and not for sale. Buying competitors is later (GDD section 11), and awnings keep their hashed colours.
3. **Save format v15.** `GoodsCompetitor.LotId` is new. v14 saves load with every competitor unlinked (`""`), as dev
   competitors stay. Validation: a linked lot is unique among competitors and never a property's lot. With a property
   catalog registered, the lot must be listed and not for sale. This is checked when a competitor is added and before every
   save.
4. **World creation, one-time.** `GeneratedWorld.LoadOrCreate` gives a new world the starting counter and table and every
   derived district and competitor. A world from before this decision gains each missing district and competitor once, by
   ID. It gains the counter or table only if it has never had one of that kind, whatever its state (equipment is never
   destroyed), so a counter the players already bought is never doubled. The changes are committed before serving. Dev
   worlds keep their dev district and two unlinked competitors.
5. **Starting furniture placement.** Pieces are placed at rotation 0 inside the shell's interior and never on a door's
   inward cell. The counter goes two rows in from the south wall, centred, then further north. The ordering spot
   (presentation) is south of the counter; one row there would close on the NavMesh, because the wall and the counter
   each take one agent radius. The table goes in an interior corner, north corners first. On seed `piece-two` a 3x3 oven
   still fits.
6. **Area lookup for choice.** Each decision scores only the restaurants within its district's range. `GoodsWorld` caches,
   per district, the restaurants in range with their distances, in catalog order, so draws match scoring every restaurant.
   The cache is rebuilt whenever the restaurant catalog changes or a district is added. Diner records are looked up by
   dictionary.
7. **Counter for sale.** Supplier offer `supplier-counter` (`Counter1.asset`), PROTOTYPE $50.00. Content only.
8. **Walkable area at runtime.** `SiteNavigation` (Session) builds a NavMesh when the drawn site is a generated lot, meaning
   its shell spans the lot on every side but the street side. The mesh is a flat floor over the lot plus a street band 6 m
   deep reaching 12 m past each side, with the shell's ground-floor walls built in as obstacles. It is rebuilt when the
   site, its size or its buildings (cells, doors, floors) change. Equipment carves it by itself, as before. Dev sites keep
   the baked DevSite NavMesh.
9. **Where figures appear.** For a generated lot, `CustomerPresenter` spawns and removes figures at points along the street
   band, out of the local camera's view as before. Other sites keep the grid-edge points. Customers of competitors are not
   drawn.

## PROTOTYPE tuning

Per district kind (`WorldLayoutCustomers.Tuning`):

| Kind | Wealth % | Dine-in % | Walking range (Manhattan m) |
| --- | --- | --- | --- |
| Downtown | 55 | 50 | 350 |
| Residential | 40 | 70 | 400 |
| Wealthy | 85 | 80 | 450 |
| Industrial | 25 | 40 | 300 |

- Liked cuisines: a district's cuisines weighted at least 25, heaviest first. Appearance: the layout district ID, with 4
  variants.
- Competitors: price $3.00 to $9.00 in $1.00 steps, plus $3.00 per tier above 1. Servers 1 to 3. Service time 10 to 30 s.
  Seats 4 to 20 in steps of 4.
- Customers per hour are the generator's district values (`WorldSettings.Districts`; downtown 900, residential 500,
  wealthy 350, industrial 120), unchanged.

## Scale gate (measured before enabling)

`CityCustomerBenchmarkTests` uses seed `piece-two`: 25 districts, 314 competitors and one player restaurant, with a
1,800 s warm-up, then 300 one-second ticks with a commit every 10 s. Results across two runs on 2026-09-30, Editor Mono,
Ryzen 5 5600X:

- Tick p99 0.68 and 0.20 ms (budget 16.7 ms). One second in which 100 customers decide, each over 123 candidates:
  3.3 ms.
- A 60 s catch-up step: 2.2 to 2.3 ms.
- Commits averaged 10.9 and 6.9 ms, with maxima of 31.7 and 9.8 ms (budget 50 ms). Payload 157 KB (budget 1 MB), with 315
  diner records.

No limit was exceeded, so the ranges were not tightened.

## Consequences and open items

- **Demand per restaurant is low.** About 1,870 customers an hour are shared by about 300 restaurants. The starting
  restaurant made its first sale after 6 minutes and about 3 sales an hour in isolated runs. Customer counts, ranges and
  scoring are PROTOTYPE, and balancing them is an owner decision.
- The street band is flat at floor height and ignores the terrain and road beyond the lot. Queue and ordering positions
  are still fixed in site axes (south of the counter) whatever the building faces.
- Competitors' customers are not drawn; that waits for piece 3 (several sites drawn at once). Resolved by
  [0033](0033-drawing-competitors-customers.md) on 2026-09-30. Employees in generated
  worlds would use the same runtime NavMesh, but none are spawned there yet.
