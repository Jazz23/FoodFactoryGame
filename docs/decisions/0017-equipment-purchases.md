# 0017 - Equipment Purchases

Date: 2026-09-24

Status: requested by the project owner ("buying equipment with cash"); implemented. The price is a PROTOTYPE value chosen by
the implementer, not an owner decision. See [architecture status](../architecture.md#implemented-equipment-purchases-2026-09-24)
and the [verification record](../verification/equipment-purchase-20260924.md).

## Context

Decision [0014](0014-supplier-purchases.md) lets company cash buy goods and left buying equipment open. GDD section 8 says
machines and equipment are purchased for money, with no technology tree. Belts are already goods (decision 0010) and are
bought as packs; this record covers machines (`GoodsEquipment`), starting with the oven.

## Decision

- An equipment offer is content, like a goods offer: `EquipmentOffer { Id, PriceCents, Equipment }`, where `Equipment` is a
  template with the kind, footprint and buffer capacities. It is registered on every server start (`RegisterEquipmentOffer`)
  and never saved. Goods and equipment offers share one ID space, so one offer ID names exactly one thing.
- A purchase is the same command as a goods purchase: `BuyDurably(player, request, site, offer)` through `RequestPurchase`.
  There is no new network message or payload schema version.
- An accepted equipment purchase debits the company and adds one new machine in one commit. The machine is `Held` by the buyer
  on the site, with ID `buy:<player>:<request>` and its own copy of the template, the same as a picked-up machine
  (decision 0006). It is placed with the ordinary `Place` command. The outcome's `EquipmentId` names it for replay.
- Checks, in order: identity and replay, `forbidden`, `invalid-offer`, `no-company`, `no-inventory`, `no-layout` (the site
  has no grid to place on), `insufficient-funds`. There is no `capacity` check, because held machines take no goods slot and
  are uncapped (decision 0006). Rejections are answered, not recorded or committed; a retry is charged at most once. These
  are the same rules as decision 0014.
- `OfferAsset` gains an optional `EquipmentDefinition`. When it is set, the asset is a machine offer (quantity 1) and
  registers itself as the matching kind (`RegisterWith`). The Supplier window shows the machine's icon and name.
- PROTOTYPE content: one oven for $150.00 (`Oven1.asset`, `supplier-oven`). That is 75 breads of margin. The $500.00 start
  can buy one oven and still have money left for dough.

## Alternatives rejected

- **Place directly on purchase (buy at a cell)**: this would combine two commands and their failure reasons. Delivering the
  machine held reuses the placement ghost, rotation and rules that already exist.
- **A separate `RequestBuyEquipment` message**: it would duplicate the replay, grant and funds path for no gain.

## Consequences and open questions

- `GoodsOutcome.EquipmentId` is a new optional field. Older saves load it as empty, so no schema bump is needed.
- Only the oven is offered. The sell counter is equipment too but is not for sale yet.
- A bought machine cannot be sold back or scrapped. There is still no way to remove equipment from the world.
- Open: resale or salvage value, equipment prices tied to site upgrades, delivery or installation time (the GDD mentions
  outsourcing construction), and member spending permissions, as in decision 0014.
