# Plan: Close the Starting Restaurant Loop

Date: 2026-10-06. Revised 2026-10-07 after P0, P1 and the owner's answers to the plan's open questions. Revised 2026-10-12
with the owner's answers after the merge into main (decisions 13-25).

The rest of this plan predates the merge. Where it cites schema v19-v21 or decisions 0037-0039, see `docs/architecture.md`:
on main those migrations are v21-v24, and decision numbers 0037-0039 are each shared by two files.

Status: **accepted in direction (owner, 2026-10-07).** P4 implemented 2026-10-07. The owner's decisions are under
[Owner decisions recorded](#owner-decisions-recorded). Values marked PROTOTYPE are open. Each piece gets a decision record in
`docs/decisions/` when it is implemented, and its status goes in `docs/architecture.md`. Still-open questions are under
[Still open](#still-open).

## Goal

A player starts a **fresh generated world** (the `WorldGen` scene, not `DevSite`) and can play the GDD's early-game loop end
to end with no dev seeds, console commands or editor help:

> buy ingredients → a supplier truck delivers them to the back-door dock → carry them in → cook → stock the register → work
> the register → customers walk in by the front door, buy, sit, leave → cash rises → spend it on layout or equipment

The loop is **one restaurant** (owner, 2026-10-07; GDD section 16, early phase "one site"). It stays correct across a restart,
with a second player in a separate process, and in a player build.

Not part of this loop: a second restaurant that sells while nobody staffs it. A register sells only while staffed (0035).
The loop does use an employee (decision 20). Buying buildings, player trucks and several sites keep working but are not acceptance
criteria here.

## Owner decisions recorded

**2026-10-06 (back doors; GDD sections 5, 7, 9; decision 0037):**

1. Every generated restaurant has a back door on a wall away from the street. It may be moved or removed with free walls,
   but the restaurant keeps at least one.
2. Every dock stands outside the shell next to a back door and is reachable from the street. The building comes with one
   dock. This replaces 0034's "any number, anywhere in the lot".
3. Back doors are for staff and goods only. Customers use customer (front) doors only.
4. Dock space is a side service yard (plan decision 1). Docks placed before the rule keep working and are marked (plan
   decision 2). Automatic back doors for older worlds, belts off doorsteps, a kept last customer door, docks facing the
   wall: see 0037.

**2026-10-07 (this revision):**

5. **Loop scope: one restaurant.** The loop ends at spending on layout or equipment. A second building is not acceptance.
6. **Ingredient delivery.** Ingredient orders come by supplier truck to a back-door dock the player picks. Equipment
   purchases (machines, furniture, decor) stay instant and arrive held. Build order: the truck-card fix first, then P3, P4,
   then P2.
7. **Supplier goods arrive fully fresh.** Goods on a supplier truck do not age; their spoilage clock starts when they are
   unloaded into the dock. (Goods on the player's own trucks age as before, 0018.)
8. **An order whose dock goes away, or cannot take the goods:**
   - before dispatch: refused and refunded in full;
   - in transit: redirected to another eligible dock of the same restaurant, else the truck returns and the order is
     refunded;
   - dock full: the truck waits up to a PROTOTYPE timeout, then returns; goods not unloaded are refunded.
   Goods are never deleted or duplicated.
9. **Start kit and cash.** The starting restaurant has its counter, table, back door and dock, nothing else. The player
   starts holding enough dough for a few bakes, not the dev starter goods (no belts or lifts). Starting cash covers about an
   oven, a fridge, a few tables and an hour of ingredients. All numbers PROTOTYPE. *Starting cash superseded by decision 17.*
10. **Demand follows the traffic hour.** District customer rates are defined per traffic hour (60 clock s,
    `RoadTraffic.HourSeconds`), not per 3600 s. Rates are re-tuned so the city does not jump 60x. **Margins stay as they
    are** for now ($2.50 bread, current offer and building prices).
11. **Acceptance includes a player build** of `WorldGen`, and **separate-process multiplayer** is part of this plan as its
    own piece (P6).
12. **0035 Q5: no, for now.** A register serves while its staff is named, wherever the staff stands.
    **0035 Q1: no.** Restoring a bought building's original walls is charged like any wall (today's behaviour).

**2026-10-12 (after the merge into main):**

13. **Supplier depot:** the world generator places it (generator version and layout format bump). How worlds made before it
    get one is still open.
14. **Who orders:** any player with a grant on the site may order ingredients to its docks, validated by the server.
15. **Full-dock timeout:** 2 minutes (120 clock s, two game hours) for decision 8. PROTOTYPE.
16. **`DevSite` keeps instant ingredient buying.** Generated worlds use supplier delivery only.
17. **Starting cash: $1,000,000** for a new generated world (replaces decision 9's sizing). The 20 dough held at start
    stays. PROTOTYPE.
18. **Prices stay as they are** (decision 10 holds after 0043): bread $2.50; `FoodBalance` and offer prices unchanged.
19. **The starting restaurant is placed where bread sells:** its district's minimum tier is 1 (wealth below the 0043
    tier-2 threshold). This is a generator rule.
20. **The loop uses an employee.** Hiring and wages (0039 hiring) are part of acceptance. Which jobs the employee does in the
    loop is still open.
21. **The loop's dish is bread only** (dough → bread at the oven). Other dishes are not acceptance. Choosing a menu per
    restaurant (0043) stays open and does not block the loop.
22. **First customer:** customers spawn nearer the street, so the first one arrives within about a minute. Replaces the
    other options in 0039 (demand).
23. **Agent recommendation, adopted:** spoil-soon warning when a register item's remaining life is under the shorter of
    600 clock s and a quarter of its shelf life. 600 s alone flags 0043's short-lived goods (chicken, 240 s) as soon as they
    are bought. Ledger retention stays at 50 entries per company. Both PROTOTYPE.
24. **A player's switch-off holds across a restart** (0038 owner feedback): after a restart a player must switch the machine
    back on; an employee cannot.
25. **Agent recommendation, adopted:** the employee screen marks a "Turn machine on" task that is blocked by a player's
    switch-off ("switched off by a player"), using the existing `held-off` refusal.
26. **The starting loop is for new worlds only.** Worlds generated before the depot get no depot and no supplier delivery.
    They keep instant ingredient buying, as `DevSite` does, which is not part of acceptance. They must still load without
    losing goods or cash.
27. **The employee does a mix of jobs in the loop:** working the register, carrying goods, and running the oven. P5 checks
    all three.
28. **Customers spawn very near the street, for testing.** The first customer should arrive well within a minute. This is a
    testing value, not tuned pacing. PROTOTYPE.
29. **Worlds made before the "start where bread sells" rule (decision 19) are left as they are.**
30. **First customer: about one to two minutes is accepted** (after decision 28's street spawn was measured, 0044). The walk is
    5 s; the wait for the first customer to choose the restaurant stays as it is. Checked as a mean first arrival of at most
    120 s over the measured openings on both seeds. PROTOTYPE.

## Where things stand

| Loop step | State in a generated world |
|---|---|
| Start | `company-1` with $700 (PROTOTYPE, 0039; older worlds keep their cash); restaurant with `start-counter`, `start-table`, a back door and `start-dock` in a side service yard (generator v6). The player spawns on the apron holding 20 dough and nothing else (0039). |
| Buy ingredients | Supplier window: instant, goods land in the player's inventory (0014 stand-in). One ingredient: dough. |
| Docks / trucks | Docks beside a back door (0037). Player trucks drive generated roads between owned sites. The truck card's route controls work by pointer (P0-01 fixed 2026-10-07). No supplier trucks. |
| Cook | Dough → bread in the 3x3 oven ($150). 10 bread in 99 s. Bread lasts 3600 s. |
| Sell | Register sells only while staffed; staffing persists while the player cooks and ends on leaving or restart (0035). |
| Customers | Front doors only (0037). Rates per 60 s game hour since 0039. Measured (P4, M-P4, stocked and staffed register): first sale 120-174 clock s, 0.5-1.0 sales per real minute, about 800 customers in the city (P0: first sale 300-600 s, 0.1-0.2 a minute). |
| Feedback | The HUD says why the restaurant cannot sell, with no screen open and in the register screen; a server-written ledger records every cash change, shown in a Ledger tab (0038, fixes P0-05 and P0-06). |
| Expand | Build mode: walls, doors, back doors, tables, decor, docks. Net cost previewed exactly (P0 S10). |

Measured evidence: [P0 baseline](verification/starting-loop-baseline-20261006.md), [P1 back doors](verification/back-door-20261006.md).

## Pieces

Order: P0 → P1 → **truck-card fix** → P3 → P4 → P2 → P5 → P6. Piece IDs are kept stable because other records cite them.

### P0 - Baseline playthrough: **done 2026-10-06**

[Plan](starting-loop-p0-plan.md), [record](verification/starting-loop-baseline-20261006.md). Both seeds, both passes ran
to S13. Pass R passed every step; Pass I passed every step but S12 (P0-01). Goods and cash were conserved at every step and
across the restart. Findings P0-01 to P0-09 are assigned below.

### P1 - Back door and dock rule: **implemented 2026-10-06**

[Decision 0037](decisions/0037-back-doors-and-the-dock-rule.md) (goods snapshot schema v19, generator v5, layout format 4),
[verification](verification/back-door-20261006.md). Remaining:

- The "staff" sign art for back doors (deferred art).
- A running-game capture of a starter dock in a **high-side** yard (dock orientation is checked only by an Editor render).
- Independent visual review of back doors, yards and docks.

### Truck-card fix (P0-01, split out of P2)

Status: **done 2026-10-07** ([0038](decisions/0038-cash-ledger-and-restaurant-readiness.md), [record](verification/starting-loop-stage1-20261007.md)).

The logistics screen's truck card cannot be clicked: `Pick`/`PickAll` stop at `logistics-trucks`, and the `>` route arrow
overflows its card. It is the only UI path to put a truck on a route. It is no longer needed for loop acceptance (decision
5), but it breaks a shipped feature and is small. Confirm with one manual click, find the cause, fix it; P0's Pass I S12 is
the regression check.

### P3 - Restaurant readiness and feedback

Status: **done 2026-10-07** except P0-07 (deferred) and the independent visual review ([0038](decisions/0038-cash-ledger-and-restaurant-readiness.md)).

The loop fails silently: an unstaffed register, an empty register and unreachable seats all just mean "no sales".

- **Readiness readout**, computed on the client from replicated state with the same pure rules the server uses
  (`RestaurantRules`, `Diners`). Shown in the world HUD with no screen open (P0-05) and in the register screen.
  - Blockers (nothing can sell), in priority order: no customer door; no register reachable from the street; no register
    staffed; no edible menu item at any staffed register.
  - Warnings: no reachable seat (takeaway only); a dock not beside a back door; stock that spoils soon (PROTOTYPE window).
  - Status: customers queued and eating; recent sales and spend.
- **Ledger.** A durable, server-written record of every company cash change: sales, supplier, property and truck
  purchases, build-mode orders and refunds (P0-06 found only build-mode orders record cents today). Written in the same
  commit as the cash change. Bounded retention (PROTOTYPE) without breaking the cash check. This is a **schema change**
  (goods snapshot v20). The player can read the last entries.
- Presentation never writes state; readout rules add no server rules.
- Candidate, not required: partial stack moves (P0-07). Deferred unless it is cheap.

### P4 - Start state and pacing

Status: **implemented 2026-10-07** ([plan](starting-loop-p4-plan.md), [0039](decisions/0039-demand-on-the-game-hour-and-start-kit.md),
[record](verification/starting-loop-p4-20261007.md)). Goods snapshot v21, layout format 5, generator v6. The playthrough rerun on the new start is pending. The first-customer
target (about a minute) is not reached by rate alone; the owner chose spawning customers nearer the street (decision 22),
which is not implemented yet. Starting cash becomes $1,000,000 (decision 17). Every value stays PROTOTYPE.

- **Start kit and cash** (decision 9). Generated worlds only; `DevSite` and its tests keep the dev starter goods.
- **Demand on the traffic hour** (decision 10). Re-base `GoodsDistrict.CustomersPerHour` (`GoodsWorld.Customers.cs`) on
  `RoadTraffic.HourSeconds`; make one clock the source for both; re-tune district rates, range and patience.
- **Targets** (PROPOSAL until the owner confirms numbers, see [Still open](#still-open)): first customer within about one
  real minute of a stocked, staffed register; a basic setup (one oven, one register, the starting table) is limited by
  the player's cooking and seats, not by arrivals; spoilage of a normal stock stays small at that rate (bread lasts 3600 s).
- **Budgets.** Rerun 0025 (`CityCustomerBenchmarkTests`: tick p99, decision burst, commit time and payload) after re-tuning.
  Editor commits reached 381 ms in P0 (P0-08); the player-build rerun is in P5.
- The only running cost is employee wages ($10 per game hour, 0039 hiring). Rent does not exist.

### P2 - Ingredients arrive at the back-door dock

GDD section 3 ("an ingredient supplier reachable by road") and sections 5 and 9 (inbound docks receive real goods from real
trucks). Decisions 6-8.

- **Supplier depot:** a world-owned site on the road network near the starting restaurant (0028 world-owned sites, 0032
  roads), placed by the world generator (decision 13; version and layout format bump). Worlds made before it get none
  (decision 26); they must still load with goods and cash intact.
- **Server contract:**
  - An order is a durable record with a stable ID, charged exactly once when accepted. Duplicate request IDs replay.
  - Goods are created on the supplier's truck and do not age there (decision 7). They unload into the dock at
    `LoadUnitsPerSecond` while docked, one truck per dock (0035). Then the truck drives back to the depot.
  - Failure behaviour is decision 8. A refund covers exactly the goods not unloaded.
  - No goods lost or duplicated, and no payment doubled, across a restart in any order state.
  - Supplier trucks are not in the player's fleet. `TruckPresenter` draws them; they drive real legs with traffic.
  - Any player with a grant on the site may order to its docks (as other site actions), validated by the server (decision 14).
  - Full-dock timeout: 120 clock s (decision 15).
- **Removing instant ingredient buying** in generated worlds that have a depot. `DevSite` and worlds generated before the
  depot keep the instant supplier (decisions 16, 26); P0's S3 step changes to an order.
- **UI:** the Supplier window orders to a dock and shows the ETA. Open orders appear in the logistics screen.
- The flaky `WorldGenSessionTests.ATruckDrivesTheGeneratedRoadsWhereItIsDrawn` (7 of 8 drawn samples in P0's baseline)
  gets a diagnosis before P2 changes trucks.

### P5 - Acceptance run (one process, then a player build)

- Rerun P0's script, updated for the new loop (supplier order instead of instant buy; no second site), in a fresh world on
  both seeds, both passes.
- Restart mid-delivery and mid-service: goods, cash, orders, ledger and customers conserved. Queued customers need
  TEST-ONLY seeding (P0 could not queue one).
- Customer-figure checks use a TEST-ONLY busy district, since a real one gives 1-2 figures per run.
- Running-game captures: back door, dock in its yard, a supplier delivery, customers by the front door, the readiness
  readout. Independent visual review.
- **Player build** (decision 11): add `WorldGen` as a build scene, make a real build and play the loop in it. Rerun the 0025
  budgets there (P0-08). Known risk: the script assistant's 3.9 GB LlamaLib folder may be included (`docs/architecture.md`).
  A build dry run is not a build.

### P6 - Separate-process multiplayer

A host and a client in separate processes both play the loop at one restaurant: both order, cook, stock and work the
register, with replication checked on both sides. First check the gaps listed in `docs/architecture.md` (session owner,
authentication, multi-process evidence) and plan them; this piece may need its own detailed plan.

## Verification (every piece, per `AGENTS.md`)

- Domain tests for state rules (Goods/World EditMode), PlayMode for Unity integration, separate-process checks for
  replication, running-game captures for visuals.
- Every run reports its filter, run identity, matched test count (zero is a failure) and artifact path under
  `docs/verification/`.
- Stateful runs use isolated save paths, never the application database. Save migrations are dry-run on a copy first.
- Baseline before attributing a regression. In P0's baseline the two older input tests **passed**; the one failure was
  `ATruckDrivesTheGeneratedRoadsWhereItIsDrawn` (timing margin).

## Ownership (when delegation is authorized)

- Truck-card fix: implementer (UI, bounded).
- P3: senior owner designs the ledger record (schema v20); an implementer can take the readout once the record is fixed.
- P4: senior owner for the demand clock change; one owner for the measurement runs.
- P2: senior owner designs the order record, depot and failure behaviour; implementer once the contract is fixed.
- P5, P6: one owner for live Editor and build runs, serialized. Visual review by someone other than the implementer.

## Out of scope

A second restaurant selling unattended, dishes other than bread and per-restaurant menus (decision 21), competitor AI, bankruptcy and loans
(GDD section 14, undecided), restaurant extra floors (deferred), truck animation at docks (owner: none), several companies
per world, factories' back doors, price and margin changes (decision 10).

## Still open

Decided on 2026-10-12 (decisions 13-25): depot placement, who orders, the full-dock timeout, starting cash, the dish, the
first-customer approach, the spoil-soon window and ledger retention.

Decided later on 2026-10-12 (decisions 26-29): older worlds, the employee's jobs and how near customers spawn.

1. PROTOTYPE values: 20 dough held at start, $1,000,000 starting cash, re-tune divisor 10, the 120 s full-dock timeout, the
   spoil-soon rule and 50-entry ledger retention.
