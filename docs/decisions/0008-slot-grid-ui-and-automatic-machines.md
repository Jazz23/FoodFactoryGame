# 0008 - Slot-Grid UI and Automatic Machines

Date: 2026-09-23

Status: point 2 (unit capacity, no stack limit) superseded by
[decision 0009](0009-slot-capacity-and-quick-transfer.md). Point 3's "closing the screen
empties the cursor" no longer applies to a stack from the player's inventory, which
stays on the cursor ([decision 0010](0010-conveyor-belts.md), point 9). Originally
requested by the project owner ("make the UIs look more like
Factorio for now"); implemented on the dev site. Supersedes decision 0007
point 4 (one batch per click) and the list-style screens of point 3. See
[architecture status](../architecture.md#implemented-slot-grid-ui-and-automatic-machines-2026-09-23).

## Context

The owner asked for:

- the player inventory as a grid of slots;
- a machine (oven) screen with an input slot and an output slot, where
  dough placed in the input begins processing immediately and the result
  goes to the output slot;
- 2D icons for dough, bread and the oven, shown in occupied slots and on
  the cursor while a stack is being moved;
- a ghost of the oven while it is on the cursor and ready to place.

## Decision

1. **Automatic machines are a server rule.** `GoodsWorld.AutomaticJobs` is
   server configuration, like recipes: never saved, set by `SessionRoot` on
   every server start. When on, every idle station starts the first recipe
   for its kind (by recipe ID) whose inputs are present and whose output
   fits the output buffer *now*. The check runs inside the same locked
   mutation as an accepted transfer and at every clock step, so a transfer
   into the input and the batch it starts commit (or roll back) together.
   A station keeps running while inputs remain; a full output stops new
   starts (a batch that finishes into a full output still blocks, 0004).
   Automatic jobs record `StartedBy = "automatic"`. Explicit
   `RequestStartJob` still works when a station is idle; the HUD no longer
   has a recipe picker or Start button. With the flag off (domain tests),
   stations stay manual.
2. **Slots are presentation.** The server still counts capacity in units
   per location. The HUD groups a container's lots into one stack per
   (item, spoiled) and the local player's held machines into one stack per
   kind, and shows them in a grid (10 × 3 slots at least for inventory and
   storage; one slot or more for machine input and output). Which slot a
   stack sits in is this client's arrangement: a stack keeps its slot, new
   stacks take the first free slot, and rearranging never contacts the
   server. It is not saved or replicated.
3. **The cursor carries a pointer, not goods.** Clicking a slot puts its
   stack "on the cursor" (`EquipmentInteraction.CursorGoods`), which names
   a container and a stack; the lots never leave their container until the
   stack is dropped on another container, which sends ordinary transfers
   (whole lots, up to the previewed free capacity; the server re-checks).
   Dropping onto the same container rearranges; closing the screen or
   dropping on an output empties the cursor. Nothing can be lost or
   duplicated by the cursor. Picking a machine out of the inventory grid
   puts that kind on the cursor exactly like its hotbar key; closing the
   screen with it keeps it there for placement.
4. **Icons are content.** `ItemDefinition` assets (`dough`, `bread`) carry
   a display name and icon; `EquipmentDefinition.icon` carries the oven's.
   The icons are DEVELOPMENT art drawn by `AgentScripts/DrawItemIcons.ps1`.
   An item without a definition shows its ID instead of an icon.
5. **The ghost is the real model.** While a machine is on the cursor and
   no screen is open, a copy of its visual prefab is drawn see-through
   (`EquipmentGhost.mat`, URP Lit transparent) over the existing
   footprint, tinted green or red by the same placement preview, and
   rotated with R. The copy has no behaviours, enabled colliders or
   lights, so it never runs, glows or intercepts the aim ray.

## Consequences

- No snapshot schema change. `StartReadyJobs` is the only new server rule.
- Any recipe whose inputs are present starts; with several recipes per
  station the lowest recipe ID wins. Recipe choice per machine is open.
- The input accepts any item (the transfer rule is unchanged); an item no
  recipe uses just sits there. Restricting inputs to ingredients is open.
- Slot arrangement resets when the client restarts. Stack size limits,
  splitting stacks (right click) and shift-click quick transfer are not
  implemented.
