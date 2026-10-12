# 0044 - Start Rules for New Generated Worlds

Date: 2026-10-12

Status: **implemented and accepted; domain tests, the 0025 benchmark and the first-customer measurement pass.** These are owner
decisions 17, 19, 28, 29 and 30 of [the starting-loop plan](../starting-loop-plan.md).
Evidence: [record](../verification/start-rules-20261012.md). Every number here is PROTOTYPE.

## Context

After the starting-loop merge the owner decided (2026-10-12) that:

- a new generated world's company starts with $1,000,000 (decision 17, replacing decision 9's $700 of 0039);
- the starting restaurant stands where bread sells: its district's minimum tier is 1 (decision 19);
- customers spawn very near the street, so the first customer at a stocked, staffed register arrives well within a minute.
  This is a testing value (decision 28, replacing 0039's other first-customer options);
- worlds made before these rules are left as they are (decision 29);
- after the street spawn was measured, a first customer in about one to two minutes is accepted (decision 30).

DevSite is unchanged. Older saves load as before.

## Decision

### Starting cash (decision 17)

- `GeneratedWorld.StartingCash` is 100,000,000 cents. Only `GeneratedWorld.LoadOrCreate` creating a new world uses it, so a world
  made earlier keeps its cash (for example a 0039 world's $700). `DevWorld.StartingCash` is unchanged.
- 0039's test that sized the cash to "an oven, a fridge, a few tables and an hour of dough" is replaced by
  `GeneratedWorldTests.OnlyANewWorldStartsWithAMillionDollars`.

### Start where bread sells (decision 19): generator v7

- `WorldGenerator.Version` is 7. `PickStart` places the start only if the residential profile's `MinRecipeTier` is 1, and
  `WorldLayoutValidator` rejects a layout whose start district's `MinRecipeTier` is not 1 (`start: its district's minimum
  recipe tier must be 1`). The goods side agrees: residential blocks have wealth 40, below 0043's tier-2 threshold of 70, so
  `GoodsWorld.MinTier` is 1 there.
- **The default generator already did this.** v6 already chose the smallest residential restaurant, and residential is tier 1. So with the default settings every seed's v7 layout is byte for byte its v6 layout apart from the `generator` line.
  This is proven for the pinned seed and both P0 seeds (`GeneratorV7IsV6WithTheStartTierRule`). The bump marks the rule's
  contract: a layout from v7 on has passed the check. It changes no existing world. Stored layouts are never regenerated, and the
  validator runs only at generation.
- Layout format is unchanged (5).

### Customers spawn by the street (decision 28): goods schema v25

- `GoodsDistrict.SpawnWalkMetres` (new). When it is above 0, a customer of that district walks at most that far to the
  restaurant it chose (`GoodsWorld.WalkMetres`): it enters the world on the street by that restaurant. Range and scoring still
  measure from the district's block point, so who considers which restaurant is unchanged. 0 is the old whole walk.
- `WorldLayoutCustomers.Districts` sets it to `StreetSpawnMetres` = 10 m (a 5 s walk, PROTOTYPE, a testing value) for layouts
  from generator v7 (`StreetSpawnGenerator`), and 0 for earlier ones. Districts are added to a world only once, by ID, so a
  world made before keeps 0 (decision 29). DevSite's district keeps 0.
- Schema v25: the v24 to v25 step only changes the version. Every older district reads 0, so older saves load and behave as
  before. `Validate` rejects a negative value.
- Drawn figures already appear only in a customer's last `CrowdTravelSeconds` (12 s) of travel, entering from the street edge
  (0033), so presentation is unchanged. A 5 s walk shows the figure from the moment the customer chooses.

## Measured

- **First customer** (`StartingLoopFirstCustomerMeasurement`): a stocked, staffed starting register in a new v7 world, opened
  at 0, 60, 120, 180 or 240 s, on each P0 seed. Every first arrival is the first decision for the restaurant + 5 s, where it was
  60-219 s of walking before. The first decision itself came 12-216 s after opening, so only 4 of 10 openings had their
  first customer within 60 s (piece-two: 95, 50, 221, 29, 110 s; p0-second: 17, 50, 172, 85, 61 s). Mean 89 s (piece-two 101 s,
  p0-second 77 s), median 73 s, maximum 221 s. Under decision 30 the measurement checks a mean of at most 120 s; it passes.
- **0025 budgets** (`CityCustomerBenchmarkTests`, seed piece-two): pass in all 3 runs. Tick p99 0.26 ms, commits 8.5-8.9 ms
  average and 11.7-12.2 ms maximum, payload 248 KB, 531 customers alive. With the whole walk the same city holds 840 (0039
  measured 832). The difference is travelling customers (320 → 31), so the budgets get lighter.

## First-customer target (decision 30)

Spawn distance alone does not bring the first customer within a minute, so the owner accepted about one to two minutes instead.
The walk is now 5 s. What remains is how long the first customer takes to choose the restaurant: a draw from about 22
(piece-two) or 59 (p0-second) customers per game hour in range, against 140-160 competitors. The levers not taken are more
demand (0039's divisor 6: about 1,400 customers in the city, commits near the 50 ms signal) and weighting a new restaurant in
customer choice. A single opening can still wait over three minutes (221 s measured).

## Open

- PROTOTYPE values: $1,000,000, the 10 m street spawn and the 120 s mean first-arrival check.
