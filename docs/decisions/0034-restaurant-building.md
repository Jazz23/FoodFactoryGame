# 0034 - Restaurant Building

Date: 2026-10-01

Status: **accepted (design); not implemented.** Requested by the project owner ("expand on building restaurants").
Owner decisions of 2026-10-01 are recorded in the GDD (section 5 "Restaurant Building", section 7 "Registers, Seats &
Ambience", section 9 "Trucks", and 29.2, 29.4, 29.6, 29.8). Items marked PROPOSAL or PROTOTYPE are not decided. Working
notes: [restaurant-building-plan.md](../restaurant-building-plan.md).

## Context

Today a building shell is server data with walls on grid cells (0019) and no player command to change it. Every generated
building has its own site with a lot (0028). Equipment, docks, and trucks exist (0006, 0022, 0023). Customers queue at a
counter and self-seat (0024, 0030). The GDD deferred player-shaped buildings (29.4, 29.5).

## Decision (owner)

- **Shell.** The player resizes the restaurant's rectangular shell within the purchased lot, and places or removes interior
  walls, doors, and windows. Lots and "buildings only" land do not change. Applies to restaurants only.
- **Timing.** Instant once paid.
- **Refunds.** Everything refunds in full at any time: structure, tables, registers, decor, docks, and machines.
- **Furniture.** Tables, registers, and decor are placed equipment with no contractor.
- **Decor.** Cosmetic, one ambience score per restaurant, no upkeep.
- **Service.** Tables supply seats; a register is the sale point, staffed by an employee or the player.
- **Docks.** Placed `dock` equipment, any number, anywhere in the lot if street-reachable, one truck each. Loading is
  automatic and slow, with no animation.
- **Floors.** Restaurant floors deferred.

## Accepted contracts (derived from the GDD requirements)

- The server validates and owns every order. A client only requests.
- An order charges or refunds exactly once, as one atomic operation with the change it pays for. A rejected or failed order
  changes nothing.
- A change that would cover equipment, belts, goods, tables, or decor is rejected. (Selling a piece is a separate order.)
- Selling or removing a piece that holds goods or has a running job moves its goods to a recorded location first. Goods are
  never deleted or duplicated.
- Each structure and item records the amount charged, so a refund returns exactly that amount.

## PROPOSALS (not decided; implement as labelled PROTOTYPE values behind named constants)

- **Price.** Per wall cell and per added floor cell, plus a fixed fee per order, with a district multiplier (GDD section 5).
  Refunds return the fee too.
- **Seat rule.** A seat counts only with a walkable door-to-register path and a path to the seat.
- **Dock rule.** A dock counts as usable if placed in the lot with a walkable path to the lot's `Access` cell.
- **Transfer rate.** Reuse the truck's `LoadUnitsPerSecond`.
- **Ambience.** Sum of per-item values with diminishing returns and a cap; one extra weighted term in customer choice.
- **Counter and register.** The starting restaurant's counter (0030) becomes or is replaced by a register; a migration for
  existing counters is needed.
- **Shell storage.** Extend `GoodsBuilding` (or add a layout record) with a snapshot version bump and an in-memory upgrade.

## Open

Ambience weights; whether truck arrival uses road routes (0032) or abstract trip time; refunds for factories; custom
buildings or empty land; build time for factories.

## Delivery slices

1. Shell editing: server commands, validation, payment and refund, persistence.
2. Build-mode UI on the top-down grid.
3. Tables and registers: seat counting, sale point, path rule.
4. Decor and ambience.
5. Restaurant docks: reachability, one truck per dock, slow automatic transfer.

Each slice needs verification evidence per `AGENTS.md` before it is marked complete.

## Consequence to revisit

Full refunds on everything mean money never constrains layout over the long run. Revisit if this removes tension.
