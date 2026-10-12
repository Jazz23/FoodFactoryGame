# 0039 - Demand on the Game Hour, and the Generated World's Start Kit

Date: 2026-10-07

Status: **implemented; Editor tests, measurement, benchmarks and upgrade dry run done; the playthrough rerun is pending** (piece P4 of [the starting-loop plan](../starting-loop-plan.md);
[P4 plan](../starting-loop-p4-plan.md)). Evidence: [P4 record](../verification/starting-loop-p4-20261007.md). Every number here
is PROTOTYPE. The first-customer target is not met by rate alone and is open for the owner (below).

## Context

Owner decisions 9 and 10 of the starting-loop plan (2026-10-07): a generated world starts with a small restaurant's money and
a few bakes of dough, not the dev kit (P0-09); and district customer rates count the traffic hour (60 clock s) instead of
3600 clock s (P0-04), re-tuned so the city does not jump 60x, with margins unchanged. The owner confirmed on 2026-10-07: the
target is the first customer within about one real minute; demand does not follow the time-of-day curve in P4; 20 dough,
$700 and a re-tune divisor of 10 as the starting point.

## Decision

### One game clock

- `GameClock` (World assembly) holds `HourSeconds = 60` and `DaySeconds`. `RoadTraffic.HourSeconds`/`DaySeconds` are aliases
  of it; traffic behaviour is unchanged.
- `GoodsDistrict.CustomersPerHour` counts customers per game hour. `SpawnProgress` accumulates customer-seconds and spawns a
  customer per `GameClock.HourSeconds`; `Validate` requires `0 <= SpawnProgress < HourSeconds`. A long clock step still equals
  many one-second steps.
- Demand does not follow `RoadTraffic.HourPercent` (owner, 2026-10-07).

### Re-tune and migration

- `GameClock.RetuneDivisor = 10` (PROTOTYPE, chosen by measurement below). An old per-3600 s rate becomes
  `GameClock.FromClockHourRate(old) = round_half_up(old / 10)` customers per game hour: 6x the old demand per clock second.
- **Goods snapshot v20 → v21** (in memory, in the `GoodsSnapshotStore` chain): every district's rate goes through
  `FromClockHourRate`, and `SpawnProgress` is scaled by 60/3600 (it is a fraction of one customer; no goods, cash or customers
  change). Nothing is written until the next commit.
- **Layout format 5, generator v6.** The layout text is unchanged in shape; a format 5 district line stores the game-hour rate,
  older formats the per-3600 s rate. `WorldLayout.RatePerGameHour(district)` reads either; `WorldLayoutCustomers.Districts`
  uses it, so an older world derives records with the same IDs as its upgraded snapshot and adds none twice. Their rates can
  differ by one per block: the snapshot rounds each block, the layout rounds the district total and then shares it (dry run
  on a real v20 world: 3 of 25 blocks, city 186 vs 187 per game hour). Stored records are authoritative; derived ones are
  only added when missing. A stored
  layout is never rewritten, so its hash stays. Generator v6 is generator v5 with the profile rates divided by 10 (downtown
  90, residential 50, wealthy 35, industrial 12 per game hour); a test rewrites v6's layout as format 4 with v5's rates and
  gets v5's pinned hash byte for byte.
- **DevSite** Old Town: 240 per 3600 s becomes 24 per game hour (the same rule), about one customer every 2.5 s.
- Rejected: overwriting district records from the layout on every load. It would hide a stored-state change from the upgrade
  chain and its dry run.

### Start kit and cash (generated worlds only)

- `GeneratedWorld.StartingCash = 70,000` cents ($700). A test prices a basic kit from the real offers (oven $150, fridge $80,
  three $40 tables, an hour of dough at one sale a minute: 12 packs, $30 = $380) and requires the cash to cover it with less
  than 2x slack.
- `GeneratedWorld.StarterGoods`: 20 dough (spoils in 7200 s like supplier dough), nothing else. `SessionRoot` gives it to
  players admitted to a generated world; DevSite keeps `DevWorld.StarterGoods` (5 dough, 50 belts, 10 lifts).
- Only a **new** world gets the new cash; an older world keeps its cash (no clawback, which would be a cash change outside
  the ledger's kinds). Starter goods are granted at a player's first admission, so a new player in an older world gets the
  new kit. Each new player brings 20 dough (noted for P6). The starting cash is the ledger's opening balance (0038).
- Unchanged: prices and margins (decision 10), patience (90-240 s; measured walk-outs were 0-1 per 10 minutes), walking speed,
  eat times, competitors.

### Tests that need more money

PlayMode tests that buy buildings (`WorldGenSessionTests`, `RestaurantBuildingSessionTests`) and the playthrough's S12 (a
second restaurant, no longer loop acceptance) get TEST-ONLY funding through `AdjustCashDurably`, which the ledger records as
an `adjustment`. Gameplay values are not changed to suit them.

## Measurement (M-P4)

`StartingLoopPacingMeasurement` (flag-gated, isolated saves): a stocked, staffed starting register, 600 clock s in 1 s steps,
both P0 seeds, divisors 6, 10 and 15 (TEST-ONLY rescaling of the saved rates). Full table:
`verification/starting-loop-p4-20261007/m-p4-demand.txt`.

| Divisor | First sale (piece-two / p0-second) | Sales per real minute | City customers alive (peak) |
|---|---|---|---|
| P0 (per 3600 s) | 300 / 600 s (60 s sampling) | 0.1-0.2 | ~135 |
| 6 | 81 / 120 s | 0.9 / 2.2 | ~1,400 |
| **10 (chosen)** | 174 / 120 s | 0.5 / 1.0 | ~810 |
| 15 | 146 / 219 s | 0.3 / 0.7 | ~500 |

Budgets (0025), city benchmark on seed `piece-two`: divisor 10: 832 customers, tick p99 0.50 ms, commits 18.3 avg / 32.4 max
ms, payload 335 KB (157 KB before). Divisor 6: 1,439 customers, tick p99 0.85 ms, commits 24.2 / 42.9 ms, payload 522 KB. Both
are inside the signals; divisor 6 is close to the 50 ms commit signal, which the 1,000-customer runtime benchmark already
crosses intermittently on the committed code (`runtime-benchmark-baseline.txt`). Divisor 10 is chosen.

## Open for the owner

- **The first customer cannot arrive within a minute by spawn rate alone.** Customers walk at 2 m/s from their block's
  centre, and the nearest blocks that reach the starting restaurant are 60-66 s away on both seeds; the first arrival was
  81-219 s in every run. Options: (a) accept about two minutes; (b) spawn customers at the block point nearest the street or
  the restaurant; (c) a faster walk (it also drives the drawn figures, 0033, so it needs a visual check); (d) divisor 6 for
  more decisions sooner, at about 1,400 customers in the city.
- A basic setup on `piece-two` is limited by arrivals (0.5 sales a minute, seats at most 1 of 4 busy), on `p0-second` by
  seats (4 of 4 busy at times). The oven makes about 6 bread a minute, so neither is limited by cooking.
- PROTOTYPE values: $700, 20 dough, divisor 10.
