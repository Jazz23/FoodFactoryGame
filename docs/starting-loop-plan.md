# Plan: Close the Starting Restaurant Loop

Date: 2026-10-06. Revised 2026-10-07 after P0, P1 and the owner's answers to the plan's open questions.

Status: **accepted in direction (owner, 2026-10-07).** The owner's decisions are under
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

Not part of this loop: a second restaurant that sells while nobody staffs it. A register sells only while staffed (0035),
and employees are a later piece. Buying buildings, player trucks and several sites keep working but are not acceptance
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
   oven, a fridge, a few tables and an hour of ingredients. All numbers PROTOTYPE.
10. **Demand follows the traffic hour.** District customer rates are defined per traffic hour (60 clock s,
    `RoadTraffic.HourSeconds`), not per 3600 s. Rates are re-tuned so the city does not jump 60x. **Margins stay as they
    are** for now ($2.50 bread, current offer and building prices).
11. **Acceptance includes a player build** of `WorldGen`, and **separate-process multiplayer** is part of this plan as its
    own piece (P6).
12. **0035 Q5: no, for now.** A register serves while its staff is named, wherever the staff stands.
    **0035 Q1: no.** Restoring a bought building's original walls is charged like any wall (today's behaviour).

## Where things stand

| Loop step | State in a generated world |
|---|---|
| Start | `company-1` with $1,000,000 (PROTOTYPE); restaurant with `start-counter`, `start-table`, a back door and `start-dock` in a side service yard (generator v5). The player spawns on the apron holding the dev starter goods (5 dough, 50 belts, 10 lifts). |
| Buy ingredients | Supplier window: instant, goods land in the player's inventory (0014 stand-in). One ingredient: dough. |
| Docks / trucks | Docks beside a back door (0037). Player trucks drive generated roads between owned sites. The truck card's route controls work by pointer (P0-01 fixed 2026-10-07). No supplier trucks. |
| Cook | Dough → bread in the 3x3 oven ($150). 10 bread in 99 s. Bread lasts 3600 s. |
| Sell | Register sells only while staffed; staffing persists while the player cooks and ends on leaving or restart (0035). |
| Customers | Front doors only (0037). Measured (P0): 5-12 sales per 3600 clock s, first sale 300-600 clock s after opening; in 10 real minutes, 1-2 sales. |
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

Every value stays PROTOTYPE.

- **Start kit and cash** (decision 9). Generated worlds only; `DevSite` and its tests keep the dev starter goods.
- **Demand on the traffic hour** (decision 10). Re-base `GoodsDistrict.CustomersPerHour` (`GoodsWorld.Customers.cs`) on
  `RoadTraffic.HourSeconds`; make one clock the source for both; re-tune district rates, range and patience.
- **Targets** (PROPOSAL until the owner confirms numbers, see [Still open](#still-open)): first customer within about one
  real minute of a stocked, staffed register; a basic setup (one oven, one register, the starting table) is limited by
  the player's cooking and seats, not by arrivals; spoilage of a normal stock stays small at that rate (bread lasts 3600 s).
- **Budgets.** Rerun 0025 (`CityCustomerBenchmarkTests`: tick p99, decision burst, commit time and payload) after re-tuning.
  Editor commits reached 381 ms in P0 (P0-08); the player-build rerun is in P5.
- No running costs. Wages belong to employees; rent does not exist.

### P2 - Ingredients arrive at the back-door dock

GDD section 3 ("an ingredient supplier reachable by road") and sections 5 and 9 (inbound docks receive real goods from real
trucks). Decisions 6-8.

- **Supplier depot:** PROPOSAL: a world-owned site on the road network near the starting restaurant (0028 world-owned
  sites, 0032 roads). Open in the design: whether it is a generator change (version and layout format bump) or placed at
  world creation, and how worlds made before it get one (no silent loss; dry-run the upgrade on a copy).
- **Server contract:**
  - An order is a durable record with a stable ID, charged exactly once when accepted. Duplicate request IDs replay.
  - Goods are created on the supplier's truck and do not age there (decision 7). They unload into the dock at
    `LoadUnitsPerSecond` while docked, one truck per dock (0035). Then the truck drives back to the depot.
  - Failure behaviour is decision 8. A refund covers exactly the goods not unloaded.
  - No goods lost or duplicated, and no payment doubled, across a restart in any order state.
  - Supplier trucks are not in the player's fleet. `TruckPresenter` draws them; they drive real legs with traffic.
  - PROPOSAL: any player with a grant on the site may order to its docks (as other site actions), validated by the server.
- **Removing instant ingredient buying** in generated worlds. `DevSite` keeps the instant supplier unless its tests are
  moved; P0's S3 step changes to an order.
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

Employees and wages, a second restaurant selling unattended, more recipes and menus, competitor AI, bankruptcy and loans
(GDD section 14, undecided), restaurant extra floors (deferred), truck animation at docks (owner: none), several companies
per world, factories' back doors, price and margin changes (decision 10).

## Still open

1. **Pacing target numbers** (P4): confirm "first customer within about a minute" and the sales rate a basic setup should
   reach, once P4 has measured the re-based demand.
2. **Supplier depot placement and older worlds** (P2): generator change or world-creation placement; the design goes to
   the owner before P2 is implemented.
3. PROTOTYPE values: dough held at start, starting cash, the full-dock timeout, the spoil-soon warning window, ledger
   retention.
