# Restaurant Building: GDD Update Plan and Agent Prompt

Status: **owner decisions applied to the GDD and recorded in `docs/decisions/0034-restaurant-building.md` on 2026-10-01; the implementation prompt is `docs/restaurant-building-prompt.md` (it supersedes the prompt in section 6).** Nothing here is accepted until the owner approves it. Items marked
PROPOSAL are my defaults for questions not yet answered; they are not requirements.

## 1. Owner decisions from the interview (2026-10-01)

- **Floorplan:** the player buys a generated building and may resize the restaurant's rectangular shell within that
  building's lot, then place interior walls, doors and windows. This selects 29.4 option B (rectangular shells) for
  restaurants only, with the rectangle bounded by the purchased lot. Land stays "buildings only" (29.5 D).
- **Floors:** multiple floors for restaurants are **deferred**. The 29.8 record (factories only) stands.
- **Construction timing:** **instant once paid** (29.2 A) for restaurant structural changes. Factories' timing stays open.
- **Decorating:** cosmetic items (floor and wall finishes, props, lighting) **plus an ambience score** that affects customer
  choice and spend.
- **Service flow:** tables provide seats; a **register is the sale point**, staffed by an employee or the player.
- **Docks:** inbound and outbound. A truck arrives from the street and goods transfer slowly between truck and loading bay.
  No truck animation, and no logistics decisions about dock placement, unloading or loading. Real trucks carry real goods.
- **Docks (follow-up, 2026-10-01):** docks stay **placed dock equipment** (today's model), several per restaurant, each
  serving one truck at a time. The owner chose full control of where docks go; the "no logistics decisions" line means
  only that loading itself is automatic (truck arrives, goods move slowly, no animation).
- **Refunds:** removing or shrinking structure refunds **in full, at any time**. This revises the 29.6 default (A).
- **Refund scope:** **everything** (structure, tables, registers, decor, docks, machines) refunds in full at any time.
  Selling a piece that holds goods or has a running job must move the goods to a recorded location first, never delete them.
- **Decor upkeep:** none.
- **Docks:** no limit besides space; a dock may be anywhere in the lot (inside or outside the shell) if street-reachable.
- **Ambience:** **one score per restaurant.**
- **Placement:** tables, registers and decor are placed equipment (hands-on, no contractor), as in the section 5 proposal.

## 2. What already exists (do not rebuild)

- Building shells as server data: `GoodsBuilding`, walls on cells, doors (decision 0019). No player command to resize or
  edit them yet.
- Sites per generated building and lot, purchase, ownership (0028); `WorldLot.Access` is the street cell.
- Dock equipment (`dock`: Outgoing input buffer, Incoming output buffer), trucks and routes (0022, 0023); trucks on
  generated roads is a proposed plan (0032).
- Equipment placement, grid rules (`SiteGrid.CellProblem`), purchase, slot-grid UI (0006, 0008, 0009).
- Starting restaurant with a counter and four-seat table (0030); customers choosing restaurants.

## 3. Conflicts and gaps to resolve

1. **Dock vs "no logistics decisions" (resolved).** Placed `dock` equipment stays; no new bay concept. Needed: a dock for
   a restaurant must be reachable from the street side (a dock beside an outer-wall door or in the lot's paved area).
   PROPOSAL: a dock counts as usable if it is placed in the lot and has a walkable path to the lot's street access cell.
2. **"Slowly teleported."** PROPOSAL: reuse `LoadUnitsPerSecond` from truck content, so goods move at a fixed rate between
   the truck cargo and the bay buffer, with no visible carrying.
3. **Where the truck "arrives from."** Depends on 0032 (trucks on roads). PROPOSAL: build the restaurant bay against the
   current abstract trip time and adopt road-based arrival when 0032 lands.
4. **Resizing vs placed things.** Shrinking or moving a wall over equipment, belts, goods, tables or decor must be rejected
   or the items moved to a recorded location first (GDD section 5, required). PROPOSAL: reject.
5. **Full refund risk (new).** Full refunds at any time let players test layouts freely, which is intended, but prices vary
   with district and tuning. PROPOSAL: refund exactly what that structure was charged, stored on the structure record, and
   refund the fixed per-order fee too. Refund and removal are one atomic server order; failure changes nothing.
6. **Ambience formula.** Per-restaurant score (decided). PROPOSAL for the formula: sum of per-item values with
   diminishing returns, capped, entering customer choice as one additional weighted factor (section 7). Numbers are PROTOTYPE.
7. **Register and seats chain.** PROPOSAL: a customer needs a reachable path door to register to seat; no path means the seat
   does not count. Free-seat counting for competitors (section 23) is unchanged.
8. **Cost.** PROPOSAL: price per wall cell, per floor cell added when growing, fixed fee per order, district multiplier
   (section 5 proposal). All PROTOTYPE.

## 4. Plan to update the GDD (apply after approval)

- **Section 5 Construction:** record restaurant resize-within-lot as owner direction; move "interior walls, doors, windows,
  loading bay" under restaurant construction; keep furniture, registers, decor as placed equipment.
- **Section 29.4:** select B for restaurants, status line with the date and the lot bound; mark A and C not selected.
- **Section 29.2:** record "instant" for restaurant structural changes. Leave factories open.
- **Section 29.8:** add a note that restaurant floors are deferred, not forbidden.
- **Section 7:** add the register as the sale point, the seat-path rule, and ambience as a customer-choice input; add decor
  to the capacity/space tradeoffs.
- **Section 9:** add the restaurant loading bay, the slow transfer rule and the no-animation scope.
- **Section 17 (MVP):** line for editable restaurant layout.
- **Section 29.6:** record full refund for restaurant structure (owner, 2026-10-01); other cases stay open.
- **Open list:** ambience weights only. Upkeep, dock limit and refund scope are decided (plan section 1). Note for owner: full refund of every item means money is never a long-term layout constraint; revisit if that removes tension.
- Then update `docs/architecture.md` (planned, not implemented) and add `docs/decisions/0034-restaurant-building.md`.

## 5. Delivery slices

1. **Shell editing:** server commands to resize the rectangle within the lot and add or remove interior walls, doors and
   windows; validation; one-time payment; persistence. No UI polish.
2. **Build-mode UI:** top-down grid, ghost, validity tint, price preview, confirm.
3. **Tables and registers:** equipment kinds, seat counting, register sale point, door-to-register-to-seat path rule.
4. **Decor and ambience:** decor items, ambience score, customer-choice term.
5. **Docks:** restaurant docks on placed `dock` equipment, one truck per dock, truck arrival, slow transfer, street-reachability rule.

## 6. Agent prompt

> You are implementing restaurant building in the Food Factory Game Unity project (`G:\Unity\FoodFactoryGame`). Read
> `AGENTS.md`, `Food_Factory_Restaurant_GDD.md` sections 5, 7, 9, 29, `docs/restaurant-building-plan.md`,
> `docs/architecture.md`, and decisions 0006, 0019, 0022, 0028, 0030 and 0032 first. Do **slice N only** (named below).
>
> **Goal.** The owner of a restaurant site can reshape the restaurant, decorate it, place tables and registers, and receive
> and send goods by truck at a loading bay.
>
> **Confirmed rules** (plan section 1): resize the rectangular shell within the purchased lot; interior walls, doors and
> windows; instant once paid; cosmetic decor plus an ambience score; register as the sale point; inbound and outbound placed docks, one truck each, with slow transfer and no animation; full refund of everything when removed, goods moved not deleted; no restaurant floors. Treat plan section 3 items as PROPOSALS: implement them as
> labelled PROTOTYPE values behind named constants and list them in your report. Do not promote them to GDD requirements.
>
> **Contracts.** The server owns all state. Clients send requests; the server validates ownership, lot bounds, overlaps,
> payment and affected equipment, and charges exactly once. A rejected order changes nothing. Structural changes that would
> cover equipment, belts, goods, tables or decor are rejected. Persist in the existing SQLite snapshot with a schema version
> bump and in-memory upgrade, and keep stable IDs. Reuse `SiteGrid.CellProblem`, equipment placement, purchase, and the slot
> UI. Do not add other storage, hard-code input (use Input System action maps), or change factory floors.
>
> **Verification.** Domain tests for state rules with isolated test fixtures; PlayMode for Unity integration; a multiplayer
> replication check for command acceptance; a running-game capture for build-mode visuals. Report the requested filter, run
> identity, matched test count and artifact path; zero matched tests is a failure. Use an isolated database path.
>
> **Report.** Changed files and behaviour, evidence, PROTOTYPE values chosen, decisions needing the owner, deferred scope.
> Update `docs/architecture.md` with implemented versus planned status. Escalate before changing any confirmed rule.
