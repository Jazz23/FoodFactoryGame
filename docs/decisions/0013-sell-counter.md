# 0013 - Sell Counter (Sale Recipes)

Date: 2026-09-24

Status: accepted by the project owner as step 2 of the sell loop; implemented. See
[architecture status](../architecture.md#implemented-sell-counter-2026-09-24) and the
[verification record](../verification/sell-counter-20260924.md).

## Context

Step 1 ([0012](0012-company-cash.md)) gave each world a company balance that nothing earned. The GDD core loop needs food to
become revenue (sections 2 and 7), and customer choice is selected as individual customers (section 23), which is a large
system of its own. This step closes the loop with a **stand-in** for customers, labelled prototype, so production can be
played for money now and replaced by real customers later.

## Decision

- A sale is a **station job whose recipe pays cash instead of producing goods**. `RecipeDefinition.SaleCents > 0` marks a
  sale recipe; it has no goods output (`OutputItemId` empty, quantities zero). The job copies the price, like it copies a
  goods recipe's output, so content edits never change a sale in progress.
- The sale reuses the job rules unchanged: inputs are chosen from edible, unreserved goods (spoiled goods never sell),
  consumed at the start, one job per station, automatic start, progress on the server clock. On completion the consumed
  goods leave the world and the site's company is credited in the same commit (the clock tick). A failed commit rolls
  back both.
- A sale needs a company: automatic and explicit starts are refused without one (`no-company`), and `Validate` rejects a
  sale job on a site no company owns. A sale job is never blocked. Picking the station up mid-sale refunds its inputs and
  pays nothing (the existing running-job refund).
- Content: `RecipeAsset.saleCents`; equipment kind `counter` (2x1 cells, 2 input slots; equipment always has an output
  buffer, which a counter leaves unused). PROTOTYPE values: one customer every 5 s buys one bread for $2.50
  (`counter-sell-bread`). Placeholder art: a primitive-built counter prefab and a GDI+ icon.
- Dev seed: a new world has `dev-counter-1` placed at cells (6, 13). An older save without any counter gets it once
  (`EnsureCounter`, committed before serving); a counter the players picked up is never replaced.
- Presentation: the machine screen shows a counter's prices instead of an output grid, the progress line reads
  "Serving a customer: …", and the readout hint says what a counter does.

Payload schema v6. v5 upgrades with no data change (every existing job is a goods job); the version changes so an older
build refuses a v6 save rather than quarantining its sale jobs as invalid.

## Alternatives rejected

- **A dedicated sell rule in the tick** (sell whatever sits in a "counter" location at a rate): needs its own saved timer,
  refund-on-pickup, spoilage and reservation rules that jobs already have.
- **Customers now**: individual customer choice (GDD 23) needs districts, demand and pathing; out of scope for closing the
  loop.

## Open

Prices set by the player and menus (GDD section 7), customer demand, queues and wait times, a price on the item instead of
the recipe, several sale recipes competing for one counter (the lowest recipe ID wins today), and purchases (step 3).
