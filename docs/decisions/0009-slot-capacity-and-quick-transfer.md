# 0009 - Slot Capacity, Max Stacks and Quick Transfer

Date: 2026-09-23

Status: requested by the project owner; implemented on the dev site.
Supersedes decision 0008 point 2's "the server still counts capacity in
units" and its "no stack-size limit". See
[architecture status](../architecture.md#implemented-slot-capacity-max-stacks-and-quick-transfer-2026-09-23).

## Context

The owner asked that the player inventory no longer hold "10 items":
inventory and storage capacity is a number of slots, each item has a
maximum stack count (20 for dough and bread), and shift-clicking a stack
while two containers are open sends the whole stack to the other one, or
as much as fills it. They also reported that the click that opens the oven
screen also picked up the inventory stack under the pointer.

## Decision

1. **Capacity counts slots.** `GoodsLocation.Capacity` is a slot count. A
   location's goods are grouped by (item, spoiled); each group takes
   `ceil(quantity / max stack)` slots (`GoodsSlots`), whatever lots it is
   made of, so lots of different exposure share slots. Spoiled goods are a
   separate stack, as the HUD already showed them.
2. **Max stack is content.** `ItemDefinition.maxStack` (dev dough and
   bread: 20). The server registers every item on start with
   `GoodsWorld.RegisterItem`, like recipes; it is never saved. An item
   without a registration stacks to 1, so its capacity counts units.
3. **Capacity is enforced on entry only.** Transfers, bootstrap lots,
   starter goods, automatic starts, job output and equipment pickup check
   free slots. Snapshot validation no longer checks capacity, because it
   depends on content: spoilage (a new stack) or a smaller max stack may
   leave a location over its slots. That state is legal, blocks further
   entries and never deletes goods.
4. **No schema change.** Existing capacities are reread as
   slots, which only ever loosens them. Admission enlarges an existing,
   smaller inventory to the configured slot count in the same commit;
   it never shrinks one. Machine buffers follow content (point 7). A
   created storage keeps its recorded slot count.
5. **Quick transfer is a client convenience.** `Player/QuickTransfer`
   (Shift) held while clicking a goods slot moves that slot's stack to the
   other open container: inventory ↔ storage on the inventory screen;
   inventory → input and input/output → inventory on a machine screen. The
   client caps the quantity at the destination's free room for that stack
   (per the latest baseline) and sends ordinary transfers, splitting lots as
   needed; the server re-checks each one. Picking a slot up with a plain
   click likewise carries only that slot's quantity.
6. **The opening press is not a slot click.** Opening a screen arms slot
   clicks only after `Player/Place` is seen released on an earlier frame; a
   slot click counts only when its press began while armed. The press that
   opened the screen can therefore never pick up a stack, however late the
   UI receives it.
7. **Machine buffer slots follow content.** On every server start,
   `SessionRoot` calls `GoodsWorld.ApplyEquipmentCapacitiesDurably` for each
   `EquipmentDefinition`, so saved machines of that kind (placed or held)
   take the content's input/output slot counts, and their placed buffers
   are resized. The change is saved before any request is served; a
   failed save aborts the start with the prior state kept. Lower counts may
   leave a buffer over-full (point 3), which never removes goods. The dev
   oven is 1 input and 1 output slot.
8. **Slot clicks are read from pointer events.** `Button.clicked` never
   fires with a modifier held (its default activator requires no
   modifiers), so the HUD reads left press/release on each slot itself and
   `Player/QuickTransfer` decides between quick transfer and pick-up.

## Consequences

- Dev content: player inventories have 30 slots (the old 10-unit inventory
  becomes 30 slots on the next join), new dev storage 30 slots, the oven
  1 input and 1 output slot (20 dough in, 20 bread out).
- Held machines are shown in the inventory grid but take no server slot;
  whether they should is open.
- Stack splitting (right click), gamepad quick transfer and merging stacks
  by dropping on a slot of the same item are not implemented.
