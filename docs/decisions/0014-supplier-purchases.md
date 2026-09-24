# 0014 - Supplier Purchases

Date: 2026-09-24

Status: accepted by the project owner as step 3 of the sell loop; implemented. See
[architecture status](../architecture.md#implemented-supplier-purchases-2026-09-24) and the
[verification record](../verification/supplier-20260924.md).

## Context

Steps 1 and 2 ([0012](0012-company-cash.md), [0013](0013-sell-counter.md)) gave the company cash and a way to earn it. This
step lets it be spent on inputs, closing the loop buy → bake → sell → reinvest (GDD section 2). Real supply comes later
from farms, factories and truck logistics (GDD sections 8–10); this is a **prototype stand-in**.

## Decision

- A purchase is an ordinary player command, `BuyDurably(player, request, site, offer)`, sent through the goods bridge
  (`RequestPurchase`) like a transfer. It checks, in order: identity and replay; the player's grant for the site
  (`forbidden`); the offer (`invalid-offer`); the site's company (`no-company`); the player's inventory on the site
  (`no-inventory`); room for the pack (`capacity`); the company's cash (`insufficient-funds`). Only then does it debit the
  company and add one fresh lot (exposure 0, owned by the site, ID `buy:<player>:<request>`) to the player's inventory, in
  one commit.
- The terminal outcome is stored per player and request ID, so a retried request (lost reply, reconnect, restart)
  replays the first result and is never charged twice. A failed commit records nothing and changes nothing, so the same
  request can be retried.
- Offers are content, like recipes: `PurchaseOffer { Id, ItemId, Quantity, PriceCents, SpoilAfterSeconds }`, authored as
  `OfferAsset`, registered on every server start and never saved. No payload schema change.
- Any player granted the site can spend the shared company balance; there are no per-member limits yet.
- PROTOTYPE content and prices: 5 dough for $2.50 (50 cents a unit, leaving $2.00 margin on a $2.50 bread) and 10 belts
  for $5.00. Delivery is immediate, into the buyer's inventory.
- The inventory screen shows a Supplier window with a Buy button per offer; affordability is not previewed, and the
  server's rejection reason is shown in the readout.

## Alternatives rejected

- **Deliver to the site storage instead of the inventory**: closer to a delivery, but the storage is a dev convenience
  location and the inventory gives the player immediate feedback. Revisit with trucks.
- **Client-side price or affordability checks**: the server is authoritative for payments (AGENTS.md); the client only
  shows the reason it refused.

## Consequences and open questions

- The dev starter goods (5 dough and 50 belts per new player) and the dev storage stock are **kept**. The earlier plan said
  purchases would replace them; removing them now would strand new players with no way to bake before their first
  purchase and would change the existing seed tests. Whether to remove them is open for the owner.
- Open: member spending permissions, bulk quantities, dynamic or player-negotiated prices, supplier stock limits, delivery
  times and logistics, and buying equipment (GDD section 12).
