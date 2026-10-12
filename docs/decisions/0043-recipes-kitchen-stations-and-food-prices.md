# 0043 - Recipes, Kitchen Stations and Food Prices

Date: 2026-10-12

Status: **implemented; verified by domain, authoring and PlayMode tests and running-game captures ([record](../verification/kitchen-20261012.md)). Independent visual review not yet done.** The
owner's answers (2026-10-12) are confirmed requirements. Every number (prices, step times, shelf lives, slot counts, the tier
rule) is a PROTOTYPE default chosen by the agent at the owner's request and stays open.

## Context

The only food chain was dough → bread at the oven, sold at the register (decisions 0007, 0013, 0024). GDD section 17's MVP asks
for several ingredients and several recipes, and section 7's district tastes and tiers had nothing to choose between.

## Owner decisions (confirmed, 2026-10-12)

1. **Ten new dishes** besides bread: Fries, Pancakes, Grilled Cheese, Tomato Soup & Bread, Cheeseburger, Margherita Pizza,
   Spaghetti Bolognese, Fried Chicken & Fries, Fish & Chips and Lasagna, built from intermediate components (below). Pancakes and
   Fish & Chips replace Steak Frites and Apple Pie, which needed butter.
2. **Stations.** New: **Mixer**, **Stove**, **Fryer**, **Grill** and a **Kitchen Counter**. No mill, churn, dairy vat, butcher
   or pasta press: **flour** and **cheese** are raw ingredients, the mixer makes pasta, and the kitchen counter cuts meat and
   vegetables. Butter is not used.
3. **Kitchen counter.** A station where ingredients are assembled (and cut). Assembly is **timed and needs a worker present**:
   it advances only while a player or employee works it. Both players and employees can work it.
4. **Recipe selection.** A machine with several recipes from the same inputs (the mixer's pasta and breading) gets a **recipe
   chooser**; this applies to every multi-recipe machine, and employee tasks can set it.
5. **Power switches on all machines**: mixer, stove, fryer and grill have the oven's switch (decision 0037).
6. **Selling.** Every dish the restaurant has stocked at a register sells at a fixed price, different for each dish. Customers in
   different districts are pickier about what is sold: **tier demand**.
7. **Ingredients come from the supplier only** in this slice; farms come later.
8. **Prices.** The agent chooses every price; they are kept in **one ScriptableObject the owner can edit**.
9. **Art.** Station models are made with the Blender CLI; the agent draws the icons.
10. **Employee tasks.** Two new visual task types: **Work at counter** and **Set machine recipe**.
11. More upscale dishes will be added later (Lasagna is the only tier 3 dish for now).

Open: whether the GDD's per-restaurant menu choice (section 7, "player chooses which recipes each restaurant sells") is still
wanted on top of stock-based selling; until decided, what is stocked is the menu.

## Recipes

Raw (supplier, packs of 5): flour, potato, tomato, lettuce, milk, egg, cheese, beef, chicken, fish, and dough as before.

| Station | Recipe | Inputs → output | Time |
|---|---|---|---|
| Mixer | Dough | flour → dough | 6 s |
| Mixer | Pasta | flour + egg → pasta | 6 s |
| Mixer | Breading | flour + egg → 2 breading | 6 s |
| Mixer | Batter | flour + egg + milk → 2 batter | 6 s |
| Stove | Tomato Sauce | 2 tomato → tomato sauce | 10 s |
| Stove | Tomato Soup | tomato sauce + milk → tomato soup | 8 s |
| Stove | Bolognese Sauce | ground beef + tomato sauce → bolognese | 12 s |
| Stove | Spaghetti Bolognese | pasta + bolognese → spaghetti | 8 s |
| Stove | Pancakes | batter → pancakes | 8 s |
| Fryer | Fries | cut potatoes → fries | 6 s |
| Fryer | Fried Chicken | chicken + breading → fried chicken | 8 s |
| Fryer | Fried Fish | fish + batter → fried fish | 8 s |
| Grill | Cooked Patty | patty → cooked patty | 8 s |
| Grill | Grilled Cheese | bread + cheese → grilled cheese | 6 s |
| Oven | Bread (unchanged) | dough → bread | 10 s |
| Oven | Bun | dough → bun | 10 s |
| Oven | Margherita Pizza | pizza base + tomato sauce + cheese → pizza | 12 s |
| Oven | Lasagna | pasta + bolognese + cheese → lasagna | 20 s |
| Kitchen counter | Cut Potatoes | potato → 2 cut potatoes | 4 s |
| Kitchen counter | Patty | beef → patty | 4 s |
| Kitchen counter | Ground Beef | beef → ground beef | 4 s |
| Kitchen counter | Chopped Lettuce | lettuce → 2 chopped lettuce | 4 s |
| Kitchen counter | Pizza Base | dough → pizza base | 4 s |
| Kitchen counter | Soup & Bread | tomato soup + bread → soup & bread | 6 s |
| Kitchen counter | Cheeseburger | bun + cooked patty + cheese + chopped lettuce → cheeseburger | 6 s |
| Kitchen counter | Chicken & Fries | fried chicken + fries → chicken & fries | 6 s |
| Kitchen counter | Fish & Chips | fried fish + fries → fish & chips | 6 s |

Shared intermediates (dough, tomato sauce, bolognese, batter, fries) are what makes a central factory worthwhile.

## Implementation

- **Recipe setting (goods, server).** `GoodsEquipment.RecipeId`, saved (goods snapshot schema **v20**; a v19 row loads with every
  machine on automatic, the old behaviour). `GoodsWorld.SelectRecipe[Durably]` sets it to one of the kind's goods recipes or ""
  (automatic); refused with `forbidden`, `not-placed` or `invalid-recipe` and nothing changes; replays return the first outcome.
  An automatic station then starts only its chosen recipe; on automatic it starts the first ready recipe by ID as before. A batch
  already running keeps its recipe. `SelectableRecipes(kind)` lists the choices.
- **Worker-bound stations (goods, server).** `EquipmentDefinition.workerBound` (the kitchen counter) is registered each start
  with `GoodsWorld.RegisterWorkerBound`, like manual power. Such a station starts and advances batches only while its
  `StaffId` (the register field of decision 0034, generalised) names a worker who is not unpaid; leaving pauses a batch where it
  is, and a manual `StartJob` is refused with `unstaffed`. `Staff` accepts registers and worker-bound kinds (`not-a-register`
  otherwise); one actor works one station at a time, and leaving the site, disconnecting, being fired or a restart release it as
  for registers.
- **Customers (goods, server).** A customer considers a player restaurant only for menu items in stock at one of its staffed,
  reachable registers (`StockedMenu`, cached per customer second), and only items and competitors of at least
  `GoodsWorld.MinTier(district)`: tier 2 for districts at `PickyWealthPercent` (70) wealth and above, else tier 1 (PROTOTYPE).
  Generated wealthy districts are 85, so they buy tier 2 and 3 only. The existing wealth-weighted tier preference still applies.
- **Food prices.** `FoodBalance` (`Assets/Content/FoodBalance.asset`) holds every food price: supply rows (item, pack size,
  pack price, shelf life) become supplier offers `supplier-<item>-<pack>`, dish rows (item, name, price, tier, cuisine, serve
  time) become menu items `counter-sell-<item>`. `SessionRoot` lists them before its authored offers and after its authored
  recipes. The old `Dough5` offer and `SellBread` recipe assets are replaced by rows with the same IDs, so saves and outcomes are
  unaffected. Machine prices stay on their offer assets, as for the oven.
- **Content.** 35 items, 26 recipe assets, 5 station definitions and offers, the food balance and the station prefabs are
  authored by `AgentScripts/BuildKitchenContent.cs`; models by `ArtSource/Kitchen/build_kitchen_models.py` (Blender 5.2, headless);
  icons by `AgentScripts/DrawFoodIcons.ps1`. The oven gets 3 input and 2 output slots.
- **Screens.** A machine with several recipes shows a **Recipe** chooser (Automatic or one recipe) under its progress line; the
  header shows the chosen recipe. The kitchen counter shows **Work this counter** / **Leave** and who works it; it has no
  Assign buttons. A player who walks out of reach of a counter they work leaves it (client side, `LeaveDistantStation`).
- **Employees.** Lua `work(station, recipe)` walks to a worker-bound station, sets its recipe when given, works it for one batch
  (returns 1), or leaves after 2 s if nothing starts (returns 0 and a reason); it gives up after 300 s. `set_recipe(machine,
  recipe)` walks to a machine and sets it (a recipe ID or the item it makes; "" for automatic). Stopping, going unpaid or a restart
  leaves any worker-bound station the employee works. Visual tasks **Work at counter** (a counter and a recipe, or whatever it is
  set to) and **Set machine recipe** (a machine with several recipes and a recipe or Automatic) generate those calls.

## PROTOTYPE prices (in `FoodBalance`)

| Ingredient (pack of 5) | Price | Shelf life (ambient) |
|---|---|---|
| Dough | $2.50 | 2 h |
| Flour | $2.00 | 7 game days |
| Potato | $1.50 | 3 game days |
| Tomato, Lettuce | $2.00 | 1 game day |
| Milk | $2.50 | 12 game hours |
| Egg | $1.50 | 12 game hours |
| Cheese | $5.00 | 12 game hours |
| Beef | $7.50 | 4 game hours |
| Chicken | $6.00 | 4 game hours |
| Fish | $7.50 | 4 game hours |

| Dish | Tier | Cuisine | Price |
|---|---|---|---|
| Fries | 1 | fast-food | $2.00 |
| Bread | 1 | bakery | $2.50 |
| Pancakes | 1 | bakery | $3.00 |
| Grilled Cheese | 1 | grill | $3.50 |
| Soup & Bread | 1 | bakery | $4.00 |
| Margherita Pizza | 2 | fast-food | $7.00 |
| Chicken & Fries | 2 | fast-food | $7.50 |
| Cheeseburger | 2 | grill | $8.00 |
| Spaghetti Bolognese | 2 | noodles | $8.50 |
| Fish & Chips | 2 | fast-food | $9.00 |
| Lasagna | 3 | fine-dining | $13.00 |

Stations: mixer $120, fryer $140, grill $160, stove $180, kitchen counter $90 (offer assets). Mixer and fryer are 1×1, stove,
grill and counter 2×1. Input slots: mixer 3, stove, fryer and grill 2, counter 4, oven 3; outputs 2.

## Consequences

- A restaurant with only bread in stock is not chosen by wealthy districts at all; it needs a tier 2 dish there.
- Stock now matters to choice: an empty register no longer draws customers who then walk out.
- A kitchen counter needs labour: an employee's task loop or a player standing at it.
- Open: per-restaurant menus, more tier 3 dishes, farm supply, balance of every number above, and whether one clock step should
  start more than one batch per station (unchanged: a step starts at most one).
