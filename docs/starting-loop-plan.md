# Plan: Close the Starting Restaurant Loop

Date: 2026-10-06

Status: **plan, not accepted.** The owner's 2026-10-06 back-door decisions are confirmed and are already in the GDD
(section 5, Restaurant Building; sections 7 and 9). Every other item marked PROPOSAL is a default for a question the owner
has not answered. Open questions are under [Owner decisions needed](#owner-decisions-needed). When a piece is accepted it
gets a decision record in `docs/decisions/`, and its status goes in `docs/architecture.md`.

## Goal

A player starts a **fresh generated world** (the `WorldGen` scene, not `DevSite`) and can play the GDD's opening loop end to
end with no dev seeds, console commands or editor help:

> buy ingredients → receive them at the back-door dock → carry them in → cook → stock the register → work the register →
> customers walk in by the front door, buy, sit, leave → cash rises → spend it on layout, equipment or a second building

The world keeps running and stays correct across a restart and with a second connected player.

This piece is mainly integration and pacing. Each system above exists, but most of them were verified on `DevSite`, in
isolated tests, or one at a time. Nobody has verified the whole chain in a generated world from a fresh save.

## Owner decisions recorded (2026-10-06)

1. **Back door is generated and movable.** Every generated restaurant comes with a back door on a wall away from the street.
   The player may move or remove it with free walls, but the restaurant keeps at least one back door.
2. **Docks must touch a back door.** Every dock stands outside the shell next to a back door, and stays reachable from the
   street. The building comes with one dock already there. More back doors allow more docks. This replaces 0034's "any
   number, anywhere in the lot".
3. **Back doors are for staff and goods only.** Customers enter and leave by customer (front) doors only. Players and
   employees use either kind. Seat and register reachability counts only customer doors.

## What exists today (from `docs/architecture.md` and decisions 0014-0036)

| Loop step | State in a generated world |
|---|---|
| Start | `GeneratedWorld` creates `company-1` with $1,000,000 (PROTOTYPE), the starting restaurant with `start-counter` and `start-table`, districts and competitors. The player spawns on the apron with the dev starter goods. |
| Buy ingredients | Supplier window: instant, goods land in the player's inventory (0014 stand-in). The only ingredient is dough. |
| Docks / trucks | Docks are placed equipment. Trucks drive generated roads between **owned** sites. No supplier deliveries (0032 out of scope). |
| Cook | One recipe: dough → bread in the 3x3 oven (bought for $150). Fridges and storage exist. |
| Sell | Register = counter. It sells only while staffed (0035). Customers choose by logit and buy an edible menu item from the register's input. |
| Customers | Generated districts. Figures enter "by a door" (any door). **About 3 sales an hour; first sale after about 6 minutes** (0030 record). |
| Expand | Restaurant build mode (free walls, furnishing, decor, ambience). Buying buildings. Several sites drawn at once. Carrying goods between owned sites. |
| Lot shape | Restaurant lot = footprint + 2 m street apron (`WorldGeometry.LotRect`). **No outdoor ground behind or beside the building.** |

## Pieces

Order: P0 → P1 → P2 → P3 → P4 → P5. P0's findings may shrink or reorder later pieces.

### P0 - Baseline playthrough (audit, no gameplay changes)

Detailed plan: [starting-loop-p0-plan.md](starting-loop-p0-plan.md). The owner chose simulated input (Input System virtual
devices) over a human playthrough (2026-10-06).

Play the loop above in a fresh generated world on the current HEAD, with an isolated save path. Record each step that
breaks, needs a workaround or is confusing.

- Do it two ways: driven through the network bridge's public requests (repeatable), and with real mouse and keyboard
  (catches input and UI gaps that driven requests skip).
- Output: `docs/verification/starting-loop-baseline-<date>.md`, a break list with evidence (log lines, captures, save
  revisions). Fix nothing here; findings feed P1-P5.
- Specific checks: the oven offer and its fit inside the starting shell; which starter goods a joining player gets; whether a
  staffed register stays staffed while the player cooks; whether figures enter through the right door; sales rate over 10
  real minutes; cash before and after; a restart mid-loop conserves goods and cash.

**Done 2026-10-06:** [record](verification/starting-loop-baseline-20261006.md). Both seeds, both passes ran to S13. Pass R
passed every step. Pass I passed every step except S12's truck route. Goods and cash were conserved at every step and across
the restart. Scope changes from its findings (P0-nn), all still subject to acceptance:

- P2 also fixes the logistics truck card, whose route controls a pointer cannot reach (P0-01; confirm by hand first).
- P3 needs a durable record of spends and sales for its ledger: supplier, property and truck purchases record no cents
  today (P0-06). It must also tell the player about an empty or unstaffed register outside the register screen (P0-05).
  Partial stack moves are a candidate (P0-07).
- P4 measures demand in the customer clock (3600 s hours), which differs from the 60-second traffic hour (P0-04).
  Baseline: 5-12 sales per clock hour, first sale 5-10 clock minutes after opening (P0-03). It replaces the dev starter
  goods (P0-09) and reruns the 0025 budgets, since Editor commits reached 381 ms (P0-08).
- P5's "see it sell while nobody views it" cannot pass while staffing ends when the player leaves (0035). That needs an
  owner decision on 0035 Q5 or employees first (P0-02).

### P1 - Back door and dock rule (foundational; schema change)

**Domain (Goods):**
- A door has a role, `customer` (default, everything existing) or `service` (back door). PROPOSAL: a back door is a door
  structure record with a role field in goods snapshot **v18**. Layout-side, the generated back door goes in the building's
  layout record (layout format 4) and is copied into the site's shell when the site is created.
- Shell orders: new piece `backdoor` (PROTOTYPE price = door price). Reject removing the last back door (`no-back-door`).
  Reject removing a back door that a dock depends on, or covering the step outside it (`dock-attached`). Free walls already
  allow moving a door as "place new, then remove old"; the rule above makes that ordering necessary for the last one.
- Dock rule (replaces 0035's on restaurant sites): the dock lies wholly outside the interior. The **doorstep** (the cell
  just outside a back door) stays clear, and the dock's footprint touches it edge to edge. The 0035 street-reachability rule
  still applies, now from the doorstep's side. New placement reason: `not-beside-back-door`. Factories, farms and other sites
  keep 0022/0023 rules.
- Customer walk: for customers, the seat/register reachability search (`RestaurantRules.Walkable`/`Reached`) treats service
  doors as walls and starts only from street cells. Players and employees are unaffected (decision 0005: their position is
  not server-checked anyway).

**Generator (World), `WorldGenerator.Version` 5:**
- The lot needs outdoor room for the dock. PROPOSAL (owner decision 1 below): a **side service yard**, a strip along one
  side of the restaurant from the street edge to the rear (PROTOTYPE 4 cells wide). The shell stays clear of it. The back
  door sits on the yard-side wall near the rear, with the starter dock beside it. Trucks and players reach it from the
  street along the yard.
- Validator: each restaurant has a back door on a wall facing the yard, the doorstep and dock cells are inside the lot and
  outside the shell, and the dock is street-reachable.
- Fewer restaurants will fit a city. Seed tests must report the new competitor count (was 178 after 0036).

**World creation (Session):** a new world places the starter dock (`start-dock`) beside the back door, like `start-counter`.

**Migration (no silent loss):** layouts are write-once, so worlds saved before generator v5 get no generated back door or
yard. PROPOSAL (owner decision 2): existing docks keep working and the dock screen marks them "not beside a back door". New
docks follow the rule. Nothing is moved or deleted. The `DevSite` fixture gets a back door beside its seeded dock so dev
tests exercise the rule. Old dev saves follow the same grandfather rule.

**Presentation:** back-door art (a kit door style plus a "staff" sign). `SiteWalk` and `DoorSwing` never route customers
through or open a back door for them. Build mode gets a Back door tool. The dock ghost shows `not-beside-back-door`.

**Tests:** planner (`no-back-door`, `dock-attached`, moving the last back door in two orders, refunds exact); dock placement
(beside, not beside, blocked doorstep, unreachable from street); customer reachability ignores back doors; v17 → v18
upgrade; generator v5 validator over the seed set; save/restore round trip.

### P2 - Ingredients arrive at the back-door dock

GDD sections 5 and 9 say inbound docks receive real goods from real trucks. Today the supplier puts goods straight into the
player's hands. This piece gives the back door its job.

- PROPOSAL (owner decision 3): **ingredient** orders are delivered by a supplier truck to a dock the player picks. Equipment
  purchases (machines, furniture, decor) stay instant and arrive held, as GDD section 5 says furniture is placed directly.
- PROPOSAL: a world-owned supplier depot on the road network near the start (0028's world-owned sites; 0032 roads). Its
  trucks are not part of the player's fleet, are drawn by `TruckPresenter`, and drive real legs with traffic.
- Server contract (any variant):
  - An order is a durable record with a stable ID. It is charged exactly once when it is accepted.
  - Goods are created on the supplier's truck (cargo location). They unload into the dock at `LoadUnitsPerSecond` while
    the truck is docked, one truck per dock (0035). Then the truck drives back.
  - A full dock pauses unloading and the truck waits. If the dock is sold or its back door removed while an order is open,
    the order fails over to another eligible dock or is refused before it starts (owner decision 4 for what happens in
    transit).
  - Cancellation before dispatch refunds in full. No goods are lost or duplicated across a restart in any state.
- Spoilage applies on the truck (0018 rules).
- UI: the Supplier window orders to a dock and shows the ETA. Open orders appear in the logistics screen.

### P3 - Restaurant readiness and feedback

The loop fails silently today: an unstaffed register, an empty register, unreachable seats and a blocked door all just
mean "no sales".

- A restaurant status readout (HUD or register screen) computed from replicated state with the same pure rules the server
  uses (`RestaurantRules`). It lists blockers in priority order: no customer door, register unstaffed, no menu item stocked
  at any register, no reachable seats (takeaway only), dock not beside a back door, ingredients spoiling. It also shows
  customers queued, eating, and today's sales and spend.
- A simple sales ledger the player can read (last N sales and purchases with times). Cash changes already commit
  atomically. This is presentation over existing outcomes unless P0 shows a missing record.
- No server rules change. Presentation must not write state.

### P4 - Start state and pacing

This piece needs owner input (decisions 5 and 6) before any numbers change. Every value stays PROTOTYPE.

- **Start kit:** what the starting restaurant has beyond counter, table, back door and dock (PROPOSAL: nothing else; the
  player's first purchase is the oven), and what the player starts holding (PROPOSAL: enough dough for the first few
  bakes, instead of the dev starter goods).
- **Starting cash:** $1,000,000 makes every choice free, which works against the GDD's "tiny restaurant, hands-on" start.
  PROPOSAL: enough for the oven, a fridge, a few tables and about one hour of ingredients.
- **Demand pacing:** 3 sales an hour is too slow to feel the loop. Measure first (P0), then tune only district customers
  per hour, range and patience against owner targets (PROPOSAL: first customer within about 1 minute of being ready, and
  sales limited by kitchen and seats, not by arrivals). The 0025 budgets must still hold: tick p99, decision burst, commit
  time and payload, all rerun with `CityCustomerBenchmarkTests`.
- No running costs are added here. Wages belong to employees (option B) and rent does not exist (buy only).

### P5 - Acceptance run

- Run P0's script again on the finished pieces in a fresh world. Then:
  - expand: buy a second restaurant, give it a back-door dock, ship goods to it by player truck, and see it sell while
    nobody views it;
  - restart mid-delivery and mid-service: goods, cash, orders and customers are conserved;
  - **separate-process multiplayer:** a host and a client both play the loop at one restaurant, with replication checked
    on both;
  - running-game captures of the back door, the dock in its yard, a delivery, customers entering by the front door, and
    the readiness readout. Independent visual review.
- PROPOSAL (owner decision 7): add `WorldGen` as a build scene and make a real player build of the loop. Today only
  `DevSite` is built, so the generated world has never run outside the Editor.

## Verification (applies to every piece, per `AGENTS.md`)

- Domain tests for state rules (Goods/World EditMode), PlayMode for Unity integration, separate-process checks for
  replication, running-game captures for visuals.
- Every run reports its filter, run identity, matched test count (zero is a failure) and artifact path under
  `docs/verification/`.
- All stateful runs use isolated save paths, never the application database. Save migrations are tried as a dry run on a
  copy first.
- A baseline comes before attributing any regression. Two known pre-existing PlayMode input failures exist (synthetic input
  with the Editor unfocused); P0 confirms whether they still fail.

## Ownership (when delegation is authorized)

- P1: senior owner (schema, generator version, shared walk rule). Tightly coupled, so keep it in one owner.
- P2: senior owner designs the order record and failure behaviour. An implementer can take it once the contract is fixed.
- P3: implementer (presentation over existing rules).
- P0, P4 measurement and P5: one owner for live Editor runs, serialized. Visual review by someone other than the
  implementer.

## Out of scope

More recipes and menus (option C), employees and wages (option B), competitor AI (option D), bankruptcy and loans (GDD
section 14, undecided), restaurant extra floors (deferred), truck animation at docks (owner: none), several companies per
world, factories' back doors.

## Owner decisions needed

1. **Where the dock fits.** Restaurant lots have no outdoor ground except the 2 m street apron.
   **Recommended: a side service yard** from street to rear (about 4 cells), with the back door on that side.
   Alternatives: a rear yard reached by a side path; or the generator makes the shell smaller than the lot and leaves the
   yard to the player.
2. **Existing saves.** Recommended: existing docks keep working and are marked; nothing moves. Alternative: on load,
   refuse trucks at misplaced docks until the player fixes them.
3. **Ingredient delivery.** Recommended: ingredients come by supplier truck to a back-door dock, and equipment stays
   instant. Alternatives: keep instant buying alongside trucks (a convenience premium?), or trucks only for bulk.
4. **Delivery to a dock that disappears in transit.** Recommended: redirect to another eligible dock of the restaurant,
   else return and refund. Goods are never deleted.
5. **Start kit and starting cash** (P4).
6. **Pacing targets:** time to first customer, and the sales rate a basic setup should reach.
7. **Player build of `WorldGen`** as part of acceptance.
8. Still open from 0035/0036 and touched by this loop: should a register serve only while its staff stands near it (0035
   Q5)? Should restoring original walls be free (0035 Q1)?
