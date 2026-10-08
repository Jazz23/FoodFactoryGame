# Plan: P4 - Start State and Pacing

Date: 2026-10-07. Parent: [starting-loop plan](starting-loop-plan.md), piece P4 (next after the truck-card fix and P3, both
done 2026-10-07). Owner decisions 9 and 10 of the parent plan are in force. Every number here is PROTOTYPE unless it is
quoted from a measurement; targets are PROPOSALS until the owner confirms them (parent plan, Still open 1 and 3).

Status: **implemented 2026-10-07** as [decision 0039](decisions/0039-demand-on-the-game-hour-and-start-kit.md); evidence in
[the P4 record](verification/starting-loop-p4-20261007.md). Deviations from this plan: the measurement compared divisors by
rescaling saved rates (TEST-ONLY) rather than code variants; patience was not changed (0-1 walk-outs per 10 minutes); the
first-customer floor was escalated (B3).

## Goal

A fresh `WorldGen` world starts like a small restaurant, not a dev sandbox, and a stocked, staffed starting register sees
customers at a pace a player notices within the first minute or two. Demand and road traffic use one game clock.

Three parts, in this order: **A** one clock, **B** demand on the traffic hour (measured, then tuned), **C** start kit and
cash. A is mechanical; B carries the risk; C is small and independent of B except for the "hour of ingredients" in the
starting cash.

## Where things stand (evidence)

- **Two clocks (P0-04).** Districts spawn on 3600 clock s: `GoodsWorld.Customers.cs:277-280` adds `CustomersPerHour` to
  `SpawnProgress` each second and spawns per 3600. Road traffic's hour is `RoadTraffic.HourSeconds = 60`
  (`Assets/Scripts/World/RoadTraffic.cs:13`). Goods already references the World assembly, so one constant can serve both.
- **Where rates live.** `WorldSettings` district profiles (downtown 900, residential 500, wealthy 350, industrial 120 per
  3600 s) are copied into the stored **layout** (`WorldLayout.Districts[].CustomersPerHour`, written by `WorldLayoutText`),
  then split per block into **goods snapshot** district records by `WorldLayoutCustomers.Districts`. `GeneratedWorld`
  adds only *missing* records by ID, so changing `WorldSettings` alone changes nothing in an existing world. `DevWorld`
  defines its own districts.
- **Measured demand (P0 M1, `m1-demand.txt`).** City: 25 districts, 1870 customers per 3600 s, ~1830 created per clock
  hour. The starting restaurant gets 5-11 of them per hour (about 0.5%); 5-12 sales per hour; first arrival 240-480 clock s,
  first sale 300-600 clock s; 0 walk-outs.
- **Travel time is a floor on the first customer.** A customer walks `distance / WalkMetresPerSecond` (2 m/s,
  `GoodsWorld.Customers.cs:459`) from its block centre; ranges are 300-450 m (`WorldLayoutCustomers.Tuning`). A customer
  from a block 200 m away needs 100 s to arrive, whatever the spawn rate. "First customer within about one real minute"
  may not be reachable by spawn rate alone (see B3).
- **Start kit (P0-09).** `SessionRoot.cs:268-270` gives every newly admitted player `DevWorld.StarterGoods` (5 dough,
  50 belts, 10 lifts) in both kinds of world. `GeneratedWorld.StartingCash` is $1,000,000.
- **Prices (content).** Oven $150 (`Offers/Oven1`), fridge $80, counter $50, dock $60, restaurant tables $8-150, dough
  $2.50 per 5 (spoils 7200 s). Bread: 1 dough → 1 bread, 10 per 99 s bake, sells $2.50, lasts 3600 s.
- **Budgets (0025).** At ~1,005 customers: save 15.5 ms mean / 34 ms max, payload 338 KB, tick p99 ~20.5 ms (above the
  provisional 16.7 ms). P0 saw Editor commits up to 381 ms (P0-08). More customers alive is the main cost risk of B.

## A. One game clock

1. Add `GameClock` in the World assembly (`Assets/Scripts/World/GameClock.cs`) holding `HourSeconds = 60` and
   `DaySeconds`. `RoadTraffic.HourSeconds`/`DaySeconds` become aliases of it (or callers move to it); no traffic behaviour
   changes.
2. Districts spawn on `GameClock.HourSeconds`: `SpawnProgress` accumulates customer-seconds below `HourSeconds`, and the
   validation bound (`GoodsWorld.Customers.cs:600`, `SpawnProgress >= 3600`) follows. Field name `CustomersPerHour` stays;
   its comment, `WorldSettings`, `WorldLayout` and the layout text say "per game hour (60 clock s)".
3. **Out of scope for A:** demand following the time-of-day curve (`RoadTraffic.HourPercent`). Decision 10 rebases the
   unit only. Listed as an owner question below.

## B. Demand re-based and re-tuned

### B1. Migration of existing worlds (senior-owned; decided before code)

A unit change silently multiplies every stored rate by 60 unless stored values are converted. Proposal:

- **Goods snapshot v20 → v21.** In-memory upgrade in the `GoodsSnapshotStore` chain: each district's
  `CustomersPerHour = round(old / RetuneDivisor)` and `SpawnProgress = old * HourSeconds / 3600` (a fraction of one
  customer; no goods or cash involved). `RetuneDivisor` is a named PROTOTYPE constant set from B2's measurement.
- **Layout format 4 → 5.** The layout text reader applies the same conversion to format-4 district rates, so
  `WorldLayoutCustomers.Districts` re-derives exactly the converted records (same IDs, same largest-remainder shares).
  New layouts are written in format 5 from the re-tuned `WorldSettings`. A test checks that converting a format-4 layout's
  districts equals converting its v20 snapshot records, for both P0 seeds.
- **DevSite** districts are converted by the same rule (they are content in `DevWorld.cs`, so just new constants).
- Dry-run the upgrade on a **copy** of a real v20 save (the stage-1 evidence has one) before any live run: report schema,
  district rates before/after, file hash unchanged. Never touch the application database.

Alternative considered: treat district records as content and overwrite them from the layout on every load. Rejected:
it hides a stored-state change from the upgrade chain and dry run that `AGENTS.md` requires.

### B2. Measure, then choose numbers

Extend `StartingLoopDemandMeasurement` (flag-gated, isolated save) into a P4 measurement M-P4 that, for both seeds
(`piece-two`, `p0-second`) and a small grid of candidate tunings, records per real minute (60 clock s) at a stocked,
staffed starting register (TEST-ONLY stock and staff, as M1):

- city customers created and alive (peak), customers that chose this restaurant, sales, walk-outs, queue length;
- first decision for this restaurant, first arrival, first sale (1 s sampling, not 60 s);
- arrivals vs. capacity: one oven makes ~6 bread per minute; one register's service time; seats at the start table.

Starting point (estimate, not a decision): `RetuneDivisor = 10`, i.e. profile rates 90 / 50 / 35 / 12 per game hour. That is
6x today's demand per clock second (city ~3.1 customers/s; ~0.5-1 arrivals per minute at the starting restaurant if its
share holds). Candidates around it: divisor 6, 10, 15, with and without a shorter walking range.

### B3. Levers and their limits

In preference order, because they move player pacing more than city cost:

1. **District rate** (the divisor). Raises city customers alive roughly in proportion; must pass B4's budgets.
2. **Patience** (`MinPatienceSeconds`/`MaxPatienceSeconds`, 90-240 s) so a single-register restaurant at the new rate
   does not shed its queue; walk-outs stay rare at a basic setup.
3. **First-customer floor.** If the measured first arrival stays above ~60 s because of walking distance, do **not**
   speed customers up silently (`WalkMetresPerSecond` also drives the drawn figures, 0033). Report the floor and give the
   owner the options: accept "first customer within ~2 minutes", spawn customers at a block point nearer the street, or a
   faster walk with a figure check. This is an escalation, not an implementer's choice.

Not levers here: prices and margins (decision 10), competitor stats, eat times (they change seat turnover, which the
"limited by cooking and seats" target relies on).

### B4. Budgets (0025)

With the chosen tuning, rerun `CityCustomerBenchmarkTests` and `CustomerRuntimeBenchmarkTests` (tick p99, decision burst,
commit time, payload) at the new peak customer count, Editor only; the player-build rerun stays in P5 (P0-08). Gate: no
regression past 0012's warnings (commit 50 ms, payload 1 MB) at the new steady state. If the chosen rate fails it, prefer
a lower divisor plus lever 2/3 and escalate rather than optimising the store inside P4.

## C. Start kit and cash (decision 9)

1. `GeneratedWorld.StarterGoods`: dough only. PROTOTYPE **20 dough** (two full bakes), spoil 7200 s as supplier dough.
   `SessionRoot` passes it when a generated world is running (`StartOffer != null`); `DevSite` keeps
   `DevWorld.StarterGoods`, so its tests are unchanged.
2. `GeneratedWorld.StartingCash`: PROTOTYPE **$700** (70,000 cents), from content prices: oven $150 + fridge $80 + three
   mid-priced tables (~$200) + an hour of dough at the B2 rate (~60 bread, $30) + slack for a counter or decor. A domain test
   computes that sum from the offer assets and checks the constant covers it with less than 2x slack, so a price change
   flags the constant.
3. Applies to **new** generated worlds only. Existing worlds keep their cash (no clawback; it would be a cash change outside
   the ledger's sale/purchase kinds). Starter goods are granted at a player's first admission (`SessionAdmission.Admit`), so
   in a world where both rules apply a later player gets the new kit. Each new player bringing 20 dough is accepted as
   PROTOTYPE; noted for P6.
4. Ledger: the starting cash is the company's opening balance (0038); no entry needed. A test checks the ledger invariant
   on a new world with the new cash.
5. Readiness: no change; with the new kit a fresh world shows "no edible menu item" until the player bakes, which is the
   intended first prompt (check it in the PlayMode run).

## Order of work

1. Baseline: compile, console baseline, Goods EditMode, Session EditMode, Session PlayMode (filters as stage 1). Record
   counts; the flaky `ATruckDrivesTheGeneratedRoadsWhereItIsDrawn` is baseline, not a P4 regression.
2. A (clock constant) with tests; behaviour identical, so all baseline tests stay green except those that assert 3600.
3. B1 design written into a new decision record (`docs/decisions/0039-demand-on-the-game-hour.md`) **before** code; owner
   or senior sign-off on the divisor being chosen by measurement.
4. B2 measurement grid (flag-gated); pick the tuning; record numbers and the reason.
5. B1 implementation: schema v21, layout format 5, conversions, dry run on a copy, round-trip tests.
6. B3 patience tuning if walk-outs appear; escalate the travel floor if needed.
7. B4 benchmarks.
8. C start kit and cash, with tests.
9. Rerun the starting-loop playthrough Pass R and Pass I on `piece-two` (S1 now shows 20 dough and $700; S3's instant
   dough buy still exists until P2; S7 should see a sale within a few minutes) and update conservation expectations.

## Verification (per `AGENTS.md`)

- **Domain (Goods EditMode):** spawn uses `GameClock.HourSeconds` (a district at N per hour spawns N in 60 s, and one long
  step equals many one-second steps); v20 → v21 upgrade converts rates and progress and validates; format 4 → 5 layout
  conversion matches the snapshot conversion; save/restore round trip; start cash covers the content sum; ledger balances
  on a new world.
- **Session EditMode:** generated-world creation uses the new start kit and cash; an older v20 world loads converted, and
  its districts are not duplicated; `DevSite` keeps the dev kit.
- **PlayMode:** a fresh `WorldGen` session: player holds 20 dough and no belts or lifts, company cash $700, readiness says
  "no edible menu item"; existing customer/WorldGen session tests pass at the new rates (adjust only tests that assert the
  old unit, never gameplay authoring to fit a fixture).
- **Measurement:** M-P4 output in `docs/verification/starting-loop-p4-<date>/m-p4-demand.txt`, both seeds, every candidate.
- **Benchmarks:** 0025 numbers before and after.
- **Playthrough:** Pass R and Pass I, conservation at every step and across the restart.
- Every run reports filter, run identity, matched count (zero fails), result and artifact path in
  `docs/verification/starting-loop-p4-<date>.md`. Isolated saves only; dry run on a copy before any upgrade.

## Docs

Decision 0039 (clock, migration, chosen PROTOTYPE values and the measurement behind them); `docs/architecture.md`
(schema v21, layout format 5, start kit); parent plan P4 status and the measured pacing for Still open 1;
`docs/development.md` only if the measurement workflow changes.

## Ownership (when delegation is authorized)

Senior owner: B1 (migration design) and the choice in B2/B3. One owner runs all live Editor work (measurement grid,
benchmarks, playthroughs), serialized. A and C are bounded and can go to an implementer once B1's decision record exists
(C does not touch the schema). No visual change, so no visual review is required beyond the playthrough captures.

## For the owner (needed before or during P4)

**Answered 2026-10-07 (owner):** 1 yes (first customer within about one real minute is the target); 2 leave out (no
time-of-day demand in P4); 3 yes (20 dough, $700, divisor 10 as the starting point, patience as measured; all stay
PROTOTYPE).

1. **Pacing targets** (parent Still open 1): confirm "first customer within ~1 real minute" once M-P4 shows whether walking
   distance allows it, and the sales rate a basic setup should reach (proposal: limited by one oven's ~6 bread/min and the
   start table's seats, not by arrivals).
2. **Time-of-day demand:** should customer rates also follow the traffic day curve (rush hours), now both share an hour?
   Proposal: not in P4; it makes a 24-minute day the player can feel, and needs its own tuning.
3. **PROTOTYPE values** to confirm or replace: 20 starting dough, $700 starting cash, the retune divisor, patience.

## Out of scope

Supplier trucks and removing instant buying (P2), player-build budgets (P5), multiplayer kit effects (P6), price and margin
changes, running costs, competitor tuning.
