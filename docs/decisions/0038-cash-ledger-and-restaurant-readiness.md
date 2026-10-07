# 0038 - Cash Ledger and Restaurant Readiness Readout

Date: 2026-10-07

Status: **implemented; verified** (piece P3 of [the starting-loop plan](../starting-loop-plan.md), with the truck-card fix
P0-01). Evidence: [stage-1 record](../verification/starting-loop-stage1-20261007.md). An independent visual review is still
open. Values marked PROTOTYPE are open.

## Context

P0 found that the loop fails silently: an unstaffed register, an empty register and unreachable seats all just mean "no
sales" (P0-05), and only build-mode orders recorded cents (P0-06). The plan asked for a server-written ledger of every cash
change (schema v20) and a client readout computed from replicated state with the server's own rules.

## Decision

### Ledger (goods snapshot schema v20)

- `GoodsSnapshot.Ledger` holds `GoodsLedgerEntry { Id, CompanyId, SiteId, Kind, Cents (signed), ClockSeconds, Revision,
  RequestId, EquipmentId, CustomerId, OfferId }`. IDs are `<company>:<n>` from `GoodsCompany.LedgerNextNumber`; they are
  never reused.
- **One writer.** `TryCredit` and `TryDebit` are the only cash writers, and each now takes a note (kind, site, related IDs)
  and appends the entry itself. An entry is therefore always in the same snapshot, and the same commit, as its cash change.
  Rejections never reach them; a replayed request returns its recorded outcome before them; a failed commit restores the
  whole snapshot (`Durably`), entry included.
- **Cash paths and kinds:**

  | Path | Kind | Site | Related IDs |
  |---|---|---|---|
  | Supplier goods | `supplier-goods` | buying site | request, offer |
  | Supplier machine / furniture | `supplier-equipment` | buying site | request, offer, delivered equipment |
  | Truck | `truck` | buying site | request, offer, truck |
  | Property | `property` | paying site | request, lot (as offer) |
  | Factory floor | `floor` | building site | request |
  | Build-mode buy-and-place | `build-furnish` | site | request, offer, new piece |
  | Build-mode sell-back | `sell-back` | piece's site | request, piece |
  | Shell order (net, debit or credit) | `build-shell` | building site | request |
  | Customer sale | `sale` | restaurant | register, customer, recipe; clock = moment of sale |
  | Sale job (legacy counter) | `sale` | station site | station, recipe; clock = job end |
  | `AdjustCashDurably` (tools) | `adjustment` | | |

- **Retention (PROTOTYPE).** `GoodsWorld.LedgerEntriesKept` = 50 entries per company. Older entries fold into
  `GoodsCompany.LedgerCarriedCents`. Invariant, checked by `Validate` and by every playthrough step:
  `OpeningCents + LedgerCarriedCents + sum(kept entries) == Cash`.
- **Opening.** A new company's opening is its bootstrap cash. **Upgraded v19 saves (PROTOTYPE):** opening 0, carried = cash,
  no entries, so the invariant holds without inventing history. The upgrade is in memory; the file becomes v20 at the next
  commit. Dry-run on a copy of a real v19 save: the file was unchanged.
- **Replication.** `View` keeps only entries of the companies in the view (the viewed site's owner), so each client sees only
  its own company. Entries ride the existing full-site baseline; no RPC was added. Payload: 244-344 bytes per entry, so up to
  about 12-17 KB more per baseline per watched site at 50 entries (P0 baselines were 75-135 KB, +9-23%).

### Readiness readout (presentation only)

- `RestaurantReadiness.Evaluate(snapshot, site, offer, menu)` is pure and runs on the client's baseline. It uses rules
  extracted from the customer simulation without changing its outcomes: `RestaurantRules.CustomerReach`, `ServesCustomers`,
  `IsMenuItem`, and `GoodsWorld.InputPlan` (the edible-input check). A test asserts that `Ready` is exactly "a queued
  customer is served on the next step".
- Blockers in order: `no-customer-door`, `no-reachable-register`, `no-staffed-register`, `no-edible-menu-item`. Warnings:
  `no-reachable-seat`, `dock-not-beside-back-door`, `stock-spoils-soon` (PROTOTYPE `SpoilSoonSeconds` = 600). Status:
  queued, eating, and sales and spend over the last PROTOTYPE `RecentSeconds` = 600 from the ledger.
- Shown under the cash with no screen open and in the register screen. The empty register now says "Nothing to sell: put
  edible Bread in the input" (P0-05).
- Ledger view: a Ledger tab beside the Supplier on the inventory screen, newest first, PROTOTYPE `LedgerRowsShown` = 20, with
  opening, carried and current cash. It is a pointer tab, so no new input action was added.

### Truck-card fix (P0-01)

Cause: a window centred with `translate: -50% -50%` kept a stale transform after it grew; drawing used the stale offset and
picking the new one, so the drawn arrows were not where clicks landed. Fix: `CentredWindow.Overlay`, a full-screen
pick-ignoring flex overlay that centres the window, used by the logistics, property, employee-script and HUD screens; the
truck card's rows shrink instead of overflowing.

## Open (owner)

- Retention size and the payload increase.
- The upgraded-save opening (0, history carried).
- The ledger opened by a tab instead of a key.

## Rejected

- Recording at each call site: easy to miss a path; the single writer makes a missing entry impossible.
- A separate ledger table outside the snapshot: two writes per cash change, not atomic with the snapshot commit.
