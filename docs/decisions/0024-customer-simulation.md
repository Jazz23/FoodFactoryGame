# 0024 - Customer Simulation

Date: 2026-09-25

Status: **accepted** 2026-09-25. The owner answered the "Owner decisions" and then accepted the "Proposed" items, which
were based on the [customer choice benchmark](../verification/customer-choice-benchmark-20260925.md). The first build is
**implemented** (see "Implemented" below and [the verification record](../verification/customers-20260925.md)). Customers
replace the sell counter's stand-in buyer ([0013](0013-sell-counter.md)). All numbers are PROTOTYPE.

## Context

GDD section 23 selects individual customer choice, and section 2 locks it: each customer evaluates nearby restaurants on
price, cuisine fit, distance, reputation and wait time. Section 7 describes the flow (queue, order, receive food, seat,
pay, leave) and lists seats and takeaway handoff as capacity limits. Section 28 targets 1,000 concurrent customers. Decision
0013 closed the sell loop with a stand-in and deferred real customers because they need districts, demand and seating.

## Owner decisions (2026-09-25)

1. **Spawning.** Districts create customers at a district-specific density. Each district's customers look different.
   Customers appear out of view of the player.
2. **Prices are fixed permanently.** Each recipe has one price (today's `RecipeAsset.saleCents`). The player chooses which
   recipes a restaurant sells, but never sets prices. This revises GDD section 7 ("…and sets prices"). Restaurants differ
   on price only through what they sell.
3. **What customers weigh.** Customers weigh food tier, wait time and whether the food can be served. Spoiled food is never
   served. Wealthy districts demand nicer food. The owner chose to express this as **tier only**: "nicer" and "fresher" are
   properties of the recipe or item type (for example, a premium dish ranks above basic bread). Food condition stays binary,
   and GDD section 24 (no graded freshness or quality score) is unchanged.
4. **Seating.** Takeaway exists, but most customers prefer to dine in. A restaurant's free seats count when customers choose,
   so a competitor with open tables tends to win them. This is meant to discourage takeaway-only restaurants.
5. **Flow.** Seating is self-service. A customer queues at the counter. A dine-in customer buys only once a seat is free,
   then seats themselves, eats and leaves. If no seat is free, they keep waiting in the queue until a seat frees or their
   patience runs out, and then leave. A takeaway customer buys and leaves.
6. **Persistence.** Customers who are travelling or queued are saved (SQLite, [0011](0011-sqlite-for-all-data-storage.md))
   and resume after a restart.

## Proposed (accepted 2026-09-25)

- **Server-owned records, not GameObjects.** A customer is a plain record in the server world: stable ID, district,
  appearance variant, dine-in or takeaway preference, patience, state, target restaurant, and the time of its next event.
  Visual customers are presentation only. A client draws them only near its camera and never owns their state (the
  section 28 and architecture constraints).
- **"Out of view" is presentation only.** The server creates customers on the district's schedule whether or not anyone
  is looking. What stays out of view is where a *visual* first appears: an edge or transit spawn point that no player's
  camera can see. If every spawn point is visible, the visual is delayed, but the simulated customer is not.
- **Decide on events.** Customers score restaurants only when they appear or give up on a queue, never every tick. The
  benchmark measured about 0.001 ms per tick at 1,000 customers and 20 sites, and 0.3 ms when all 1,000 decide at once.
- **Score.** Cuisine fit, distance and reputation (locked in section 23), plus price, food tier (weighted by district wealth),
  published wait, and free seats (for customers who prefer to dine in). Each term's weight comes from the district's demand
  profile (section 3).
- **Published wait and weighted choice.** Each restaurant publishes a wait estimate every few seconds, and customers choose
  randomly weighted by score (logit) instead of always taking the best. In the benchmark, this halved the peak queue compared
  with a live queue and always-best choice (12 vs 21). That result is from one seed and is indicative only.
- **Buying.** In one server commit, the purchase removes one edible item of the ordered recipe from the restaurant's counter
  and credits the price to the company. It happens exactly once, keyed by the customer's order, and a restart never
  repeats it. A dine-in purchase also reserves a seat in the same commit. With no edible item, there is no sale: the customer
  keeps waiting, and a stockout counts against reputation.
- **Reputation.** A per-restaurant value that rises with the food tier served and falls with long waits, walk-outs and
  stockouts, drifting slowly toward neutral. The formula and rates are tuning.
- **Seats** are equipment (a table kind with N seats), placed like other equipment. A seat is reserved when bought and freed
  when the customer leaves.

## Proposed first build

Replace the counter stand-in at the dev site: one dev district, the dev restaurant with its counter and a table, and one or
two fixed AI competitors. The competitors are records with a menu, seats and a service rate, but no production. Scope:
customer records in the goods snapshot (a schema bump, with v-previous upgrading to no customers), the score and flow above,
exactly-once purchase, and save/restore of travelling and queued customers. Verification: domain tests (choice, seat
waiting, patience walk-out, stockout, exactly-once purchase across a failed commit and a restart), the existing benchmark
re-run against the real code, and one PlayMode check that the HUD cash comes from customers.

## Implemented (first build, 2026-09-25)

- **Records** (`GoodsWorld.Customers.cs`, goods snapshot **v13**): `GoodsDistrict` (map position, customers per hour,
  wealth, appearance set, liked cuisines, dine-in share, range, spawn progress), `GoodsCompetitor` (map position, one menu
  item's cuisine, tier and price, servers, service time, seats), `GoodsCustomer` (district, appearance variant, dine-in,
  patience, state Travelling/Queued/Ordering/Eating, restaurant, chosen menu item, counter, table, price paid, queue
  ticket) and `GoodsDiner` (reputation −1000..1000, published wait and free seats, served and walked-out counts), plus
  `NextCustomerNumber` and the xorshift state `CustomerRandom`. A v12 save upgrades with none of them.
- **Restaurants.** A player restaurant is a mapped site with a company, at least one placed counter and a menu. The menu is
  every registered sale recipe for the counter kind (player menu choice is not built). Its servers are its counters, and
  its seats are those of its placed tables. `RecipeDefinition` gained `Tier` and `Cuisine`, and `GoodsEquipment` gained `Seats`
  (tables only: kind `table` ⇔ seats > 0).
- **Clock.** Customers advance in one-second sub-steps after the rest of each clock step. The order within a second is:
  arrivals and finished service or eating; walk-outs; district spawns; serving each restaurant's queue in ticket order;
  then publishing the wait and free seats every 5 s, and reputation drifting one point toward 0 every 60 s. One long step
  gives the same result as many one-second steps (tested).
- **Choice.** Utility is 1 + cuisine fit + 0.8·tier·wealth − 0.15·price($)·(1 − 0.7·wealth) − 0.5·distance/100 m
  − 0.5·published wait/60 s + reputation/1000 ± the seat term for dine-in customers (+0.5 if seats are free, −1 if not).
  Staying home scores 0, and the draw is logit. A customer who stays home never enters the world. A customer who walks out
  chooses once more, excluding that restaurant; a second walk-out sends them home. Travel is Manhattan distance at 2 m/s.
- **Serving.** A queued customer is served when a counter is free and has an edible unit of their item (and, for dine-in, a
  table has a free seat). Customers who can't be served yet don't block those behind them. At that moment the item leaves
  the world, the company is credited, and the seat and counter are taken. A purchase that would overflow the balance waits.
  Reputation changes by +5·tier − (seconds waited, capped at 600)/30 on each sale, and −40 on each walk-out.
- **Stand-in retired.** Stations never start a sale recipe (`StartJob` answers `customers-only`). A sale job saved by an
  older build still completes and pays once.
- **Pickup.** A counter serving a customer, or a table with someone seated, answers `occupied`.
- **View.** A site's baseline carries its own customers and diner record. Districts, competitors and the random state stay
  on the server.
- **Dev seed and content.** See `DevWorld` (district "Old Town", Corner Cafe, Noodle Bar, `dev-table-1`) and
  `Assets/Content/Equipment/Table.asset` (2x1, 4 seats, supplier `supplier-table` at $40.00). The counter screen shows who is
  being served and how many are waiting; a table screen shows its seats and the restaurant's served, walked-out and
  reputation figures.
- **Built 2026-09-25:** local visual customers with edge spawn points outside the local camera, capped at 100 per client
  ([verification](../verification/customer-visuals-20260925.md)).
- **Not built:** out-of-view checks against other players' cameras, district appearance sets, player menu choice, and
  competitor behaviour beyond fixed records.

## Alternatives rejected

- **Aggregate demand** (GDD 23 A) and **hybrid** (23 C): the owner selected individual choice.
- **Graded freshness** (remaining shelf life as a choice factor) and a **per-lot quality score**: rejected 2026-09-25 in
  favour of tier only, which keeps section 24.
- **Player-set prices:** rejected 2026-09-25; prices are fixed per recipe.
- **Switch to takeaway, or leave at once, when no seat is free:** weaker incentives for building tables. The owner chose
  waiting in the queue.

## Open

- Procedural districts do not exist yet (section 3). The first build uses one authored dev district.
- District numbers: customer density and schedule (for example, lunch and dinner peaks), wealth, the tier weighting, cuisine
  preferences, and appearance sets.
- Food tier values, and which recipes carry which tier and cuisine.
- What a customer orders when a menu has several recipes: one item chosen by preference is proposed.
- Patience, eating time and the takeaway/dine-in split.
- Competitor AI beyond fixed records (section 11), and acquisitions.
- Customer visuals and crowd rendering at scale. The benchmark measured the simulation only; animated visible customers are
  the remaining performance risk. An Editor probe held 100 animated models under the 16.67 ms frame budget, but not 400.
- Whether cuisine fit and distance weights differ by district or are global.
- **Customer groups** (raised by the owner 2026-09-25, deferred to a later step): families of 3+ and couples, with table
  size deciding whether a party can be seated. Undecided: whether a party takes a whole table or shares with strangers,
  whether it may split across tables, whether each member pays or the party places one order, each district's party-size
  mix, and whether takeaway parties exist. Adding a party size is a schema upgrade in which existing customers become
  parties of one.
