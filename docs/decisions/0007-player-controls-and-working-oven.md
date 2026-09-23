# 0007 - Player Controls and a Working Oven

Date: 2026-09-22

Status: accepted by the project owner; implemented on the dev site. See
[architecture status](../architecture.md#implemented-factorio-style-controls-and-a-working-oven-2026-09-22).

## Context

After step 3b the oven could be moved but not used: nothing could go into it,
there were no recipes, and only the server could start a job. The owner asked
for Factorio-style controls (step 3c) and a working oven (step 4) on one
branch, with these answers to the proposal:

- The oven makes bread: one dough to one bread.
- Camera orbit is always on (mouse movement orbits; no button).
- Removal is instant.
- Batches are started by hand, one per click, rather than repeating.
- Dev ingredients: starter stock in the dev storage plus a few per player.

## Decision

1. **Always-on orbit with a locked pointer.** While no screen is open, the
   pointer is locked and hidden and a crosshair marks the screen centre, so
   `Look` always orbits. The camera pivot sits 1 m beside the avatar (over the
   shoulder) so the crosshair aims past the avatar at the floor ahead. Opening
   a screen frees the pointer and suspends orbiting. Esc with no screen open
   releases the pointer until the next click. The `Orbit` action is removed.
2. **Controls.** `E` toggles the inventory screen and no longer picks things
   up. Keys 1–9 (`Hotbar`) select slots; a slot points to a machine *kind*, in
   the order of the session's equipment content. Selecting a slot you hold at
   least one of puts that kind on the cursor and shows the green/red
   footprint; selecting it again or `Q` empties the cursor, and it empties by
   itself when none of that kind is left. Left click places the cursor item,
   or with an empty cursor opens the machine under the crosshair. Right click
   picks up the machine under the crosshair instantly (existing pickup
   command, including emptying its contents into your inventory). Aim rays
   skip player avatars.
3. **Screens use only existing server commands.** The inventory screen shows
   your machines (kind × count) and goods grouped by item and spoiled state,
   beside the dev storage. The machine screen shows your inventory, a recipe
   picker, a Start button, a progress bar, and the input and output buffers.
   Clicking a stack moves it with the existing server-checked transfer: whole
   lots, most exposed first, up to the destination's free capacity per the
   latest baseline (the server re-checks). Inventory ↔ storage on the
   inventory screen; inventory → input, input → inventory and output →
   inventory on the machine screen.
4. **One batch per click.** The new `RequestStartJob` bridge command calls the
   existing `StartJobDurably`; the chosen recipe is local UI state, not saved.
   The oven never starts another batch by itself. A second start while a batch
   runs is refused with `station-busy`. Picking the oven up still refunds the
   batch in progress (0006).
5. **Recipes are content assets** (`RecipeAsset`), registered by the server on
   every start, including a recovered save; they are never saved (0004). The
   dev recipe `oven-bread`: 1 dough → 1 bread, 10 s, bread spoils after 3600 s
   ambient.
6. **Dev ingredients.** A new dev world's storage gets 20 dough; each player
   gets 5 dough (lot `starter:<playerId>:0`) in the same commit that creates
   their inventory, so reconnecting never grants more. Dough spoils after
   7200 s ambient. Existing saves and players whose inventory already exists
   get neither.
7. **Running display.** A visual shows "running" exactly while the replicated
   baseline has a running (not blocked) job on its station. Displays implement
   `IEquipmentRunningDisplay`; the oven's `OvenToggle` turns its heater glow,
   light and fans on and off. The progress bar is interpolated at most one
   clock step past the latest baseline.

## Consequences

- No new server rules and no snapshot schema change; `TryGrantDurably` takes
  optional starter goods, validated before any mutation.
- Anyone with the site grant can still move goods out of anyone's inventory
  location (the transfer rule checks the site grant, not the holder). This
  predates this step and remains open.
- Gamepad has no hotbar binding and the screens are mouse-driven.
- Aiming is by camera direction; the reach is where the crosshair meets the
  floor, not a range rule (position stays presentation-only, 0005).
