# 0047 - The Phone, Farm Types and Farm Orders

Date: 2026-10-13

Status: **implemented; verified by EditMode and PlayMode tests and running-game captures (see
[the verification record](../verification/phone-20261013.md)). No independent visual review and no separate-process
multiplayer check yet.** The owner's requests and answers are confirmed requirements; values marked PROTOTYPE stay open.

## Owner requests (confirmed, 2026-10-13)

1. C opens a phone that takes up most of the screen. C no longer switches to the top-down camera.
2. The phone has a map of the world showing every farm and building. Players see lot prices and buy lots, farms and restaurants
   from it.
3. Players tap a farm to buy its goods (eggs, milk, ...). There are different farm types, each producing different base goods.
4. Orders are placed through the phone. The player either picks the goods up with their own truck or pays a flat delivery fee.
5. Goods from a farm cost less when the player owns the farm.
6. Players can schedule recurring orders with a farm.
7. The phone is also where players hire employees and rename their restaurants, buildings and lots, and the employee task screen
   is reachable from it.
8. (Added during the work.) Truck routes are managed and trucks bought from the phone.

Owner answers to the agent's questions (2026-10-13):

- **Delivery** is a visible supplier truck that drives the roads and unloads at the dock (not a timed or instant arrival).
- **Pickup at a farm the company does not own** works through a farm pickup point: the order's goods wait there and one of the
  company's parked trucks fetches them.
- **Farm types:** six. Dairy (milk, cheese), Cattle (beef), Poultry (egg, chicken), Vegetable (potato, tomato, lettuce),
  Grain (flour, dough), Fishery (fish). Together they sell every raw ingredient of the recipe catalog (0043).

## Decisions

- **Farm types are derived, not stored.** Farm lots take the six types in turn in lot ID order (`FarmCatalog.KindsOf`), so a
  world with six or more farms has every type, server and clients agree from the same stored layout, and no generator or
  layout format change is needed. Changing the rule later would retype existing worlds' farms; a stored type would need a
  layout format bump. Implementer choice.
- **Prices come from the supplier's packs** (`FoodBalance`, 0043): a farm sells the same packs at the same list price. Owning
  the farm's lot halves the goods price (`FarmOwnedPricePercent` 50, PROTOTYPE). Delivery costs a flat
  `FarmDeliveryFeeCents` ($20.00, PROTOTYPE). At most 20 packs an order (PROTOTYPE).
- **The farm gate** is a world-owned site per farm (`farm-gate:<lot>`): a map record at the lot's street access, created with
  the first order to that farm, never granted or owned. It is not the lot's reserved site, so the farm stays buyable.
- **Delivery:** the order debits goods and fee in one commit and creates a supplier truck (`GoodsTruck.Supplier`, its
  `OrderId` set) at the gate with the goods aboard in refrigerated cargo (starting-loop decision 7: supplier goods do not age
  until unloaded). It drives the real roads, unloads into the dock's Incoming at its rate, one truck per restaurant dock (0035),
  drives back and is removed at the gate. Supplier trucks are the ordering company's records for the invariants but are not in
  its fleet (the logistics screen and truck names leave them out); they are drawn in white.
- **Pickup:** the order debits the goods only and sends the chosen parked fleet truck to the gate. The goods are created in its
  cargo when it loads there (so nothing waits at the farm before the truck arrives), it unloads at the dock and parks at the
  dock's site. Its cargo ages like any fleet truck's. A truck on an order cannot be reassigned until it is done (`on-order`).
- **Failure (starting-loop decision 8, as implemented):** at the receiving site, a dock that is gone or unreachable is replaced
  by another usable dock of the same site. A truck that cannot unload (no usable dock, full Incoming) waits up to
  `FarmDockTimeoutSeconds` (120 clock s, decision 15) and gives up: a supplier truck drives what is left back to the farm,
  where it leaves the world and is refunded (goods pro rata, the fee too when nothing was delivered); a pickup truck parks with
  the goods aboard, which stay the company's. Goods are never deleted without a refund and never duplicated. Difference from
  decision 8's wording: an order is dispatched at once, so there is no "before dispatch" state, and a dock lost in transit is
  handled on arrival (redirect, else the timeout) rather than by turning back at once.
- **Recurring orders** (`GoodsFarmSchedule`) repeat every 1-24 game hours from the next game hour, placed inside the clock step
  like wages, as the scheduling player's order. One that cannot be placed (no cash, truck busy, dock gone) records why
  (`LastProblem`) and tries again at its next hour; there is no catch-up. Cancelling stops future orders only.
- **Renaming** a site (`RenameSite`) needs a grant on it and a company that owns it; names follow the employee-name rule.
- **Snapshot schema v26:** `FarmOrders`, `FarmSchedules`, `GoodsTruck.OrderId` and `GoodsTruck.Supplier`. A v25 save loads with
  no orders and every truck a fleet truck serving none. A site view carries the company's orders, schedules and the gates its
  orders' trucks use.
- **The phone** (`PhonePanel`, `PhoneMapView`, `Phone.uxml`/`.uss`) is a screen (`InteractionScreen.Phone`) that suspends
  gameplay input like the in-game menu (except Esc, the pointer and C; C typed in a name box is text). Apps: Map (lots coloured
  yours / for sale / farm / not for sale, the player's position, wheel zoom, drag pan; the selected lot's price, Buy, rename
  and, for a farm, its goods and the order form), Orders (open orders with their trucks' progress, repeating orders with
  Cancel), Staff (this site's employees: Hire, Fire, Tasks; Esc on the task screen returns to the phone), Trucks (the logistics
  screen moved into the phone, `LogisticsPanel.Embed`), Places (every site of the company with a rename box).
- **The camera:** the `SwitchCamera` action is renamed `Phone` (same action ID, still C), so saved rebinding overrides carry
  over. The rig has no manual switch; indoors still turns top-down on crossing a threshold (GDD section 3, 0019).
- **Kept as before:** the inventory's Staff window still hires and fires, the supplier window still sells instantly in every
  world (the starting-loop P2 depot is still planned), and L still opens the logistics screen on its own.

## Open

PROTOTYPE values above; whether owning a farm should also produce goods for the owner (GDD section 10 draft); farm goods stock
or capacity limits; cancelling a placed order; ordering to another site's dock while standing elsewhere works, but hiring is
still for the site the player stands in (0039); separate-process multiplayer check; independent visual review.
