**FOOD FACTORY / RESTAURANT GAME**

**Game Design Document - Rough Draft**

*Working design - assumptions are provisional until explicitly decided*

# 1. High Concept

A 3D management/automation game combining food production, logistics,
restaurant operation, and direct character control. The player starts
with a tiny restaurant, does much of the work personally, and grows into
a vertically integrated food network spanning farms, food factories,
transport infrastructure, and restaurants.

## Design Pillars

- Everything exists physically: ingredients, machines, buildings,
  employees, vehicles, and the player.

- Supply chains matter because food can spoil, and distance and timing affect whether ingredients arrive while still edible.

- Restaurants create demand instead of factories producing goods only
  for abstract markets.

- Growth comes mainly from money, space, logistics, labor, and
  complexity rather than research unlocks.

- The player can personally do jobs, then automate or delegate those
  same jobs to employees.

## Player Fantasy

Build a food empire from the ground up: source ingredients, cook or
process food, deliver it efficiently, operate restaurants that fit local
demand, and expand into a multi-site food and logistics network.

# 2. Core Play Loop

## Moment-to-Moment Loop

**1.** Inspect demand, inventory, queues, and bottlenecks.

2\. Move, cook, operate, stock, deliver, or assign a worker to solve the
immediate problem.

**3.** Products flow through machines, storage, transport, kitchens, and
service.

**4.** Customers buy food based on price, cuisine preference, wait time, and convenience. Spoiled food cannot be sold.

**5.** Revenue is reinvested into capacity, labor, logistics, new sites,
and vertical integration.

## Full Progression Loop

**1.** Establish a profitable local food business.

**2.** Hire employees and automate repetitive work.

**3.** Improve restaurant throughput, menu variety, and customer
capacity.

4\. Internalize supply by purchasing farms, food factories, and
vehicles.

**5.** Expand into other districts with different tastes and income
levels.

**6.** Build larger road/rail distribution networks.

**7.** Buy weak competitors or open new restaurants.

8\. Expand logistics, production capacity, and restaurant coverage while
buying equipment and outsourcing construction.

9\. Operate a multi-region, vertically integrated food and logistics
network.

Draft assumption: the game is open-ended. There is no mandatory final
victory state; optional scenario goals can provide structured
objectives.

# 3. World & Map

- Procedurally generated map divided into districts/markets.

- The game is fully 3D. Outdoor play uses an isometric-style camera;
  building interiors use a more top-down camera.

- Districts vary by population, wealth, cuisine preferences, land cost,
  traffic, and competition.

- Farms, competing restaurants, roads, rail corridors, and purchasable
  properties are distributed across the map.

- Distance matters for delivery cost, employee travel, freshness, and
  network design.

## District Demand

Each district has a demand profile rather than a fixed recipe list.
Examples: strong preference for cheap fast food, high demand for fresh
premium meals, preference for a cuisine family, or high lunch traffic
but weak dinner traffic.

# 4. Player & Employees

## Player Character

- A physical character that walks through the world and interiors.

- Can carry items, interact with machines, cook, serve, stock, load
  vehicles, and perform other operational jobs.

- Direct action is useful early and remains available later for
  troubleshooting or optimization.

## Employees

- General-purpose characters rather than rigid classes.

- Can perform the same operational jobs as the player if the required
  workplace/equipment is available.

- Can be assigned permanent roles, task priorities, zones, routes, or
  one-off remote orders.

- Can be transported between sites.

- Need wages; employees use a simple workforce model with no deep personal stats, needs, moods, or skill simulation.

# 5. Buildings, Space & Construction

- Buildings occupy physical land and have editable interiors. New
  construction and structural changes are outsourced and paid for with
  money.

- Interior floor area is a core resource.

- Restaurants trade dining space against kitchen, storage,
  refrigeration, staff circulation, and utilities.

- Factories trade machine footprint against buffers, worker access,
  loading, and expansion space.

- Construction requires money only. Construction work is outsourced
  rather than performed by the player or employees.

## Restaurant Space Tradeoff

- More tables increase potential customers and revenue.

- Larger kitchens increase throughput and support more complex recipes.

- Storage/refrigeration protects supply reliability but consumes
  valuable floor space.

- Poor layouts create walking congestion and reduce effective
  throughput.

# 6. Food, Recipes & Spoilage

Food can be sold at different levels of processing. Central factories favor scale and consistency; restaurant kitchens favor flexibility and shorter final preparation chains.

## Recipe Structure

- Recipes are chains of ingredient transformations performed by machines
  or workers.

- Some steps can occur in factories or kitchens; others are
  location-specific.

- Larger recipes may require multiple stations, more floor area, more
  labor, and tighter timing.

- All machines are available from the start but must be purchased and
  supported economically.

## Item Condition

- Food condition is binary: **edible** or **spoiled**.

- There is no graded freshness or food-quality score.

- Edible items behave normally until their spoilage threshold is reached.

- Spoiled items cannot be served or sold and must be discarded.

## Spoilage

- Perishable items spoil after enough unrefrigerated/refrigerated time has elapsed.

- Refrigeration extends time before spoilage.

- Long delivery routes, traffic, and poor inventory planning increase spoilage risk.

- Spoilage creates waste and direct cost.

# 7. Restaurant Simulation

## Customer Flow

**1.** Potential customers choose among nearby restaurants.

**2.** Choice is influenced by cuisine fit, price, reputation, waiting time, and convenience.

**3.** Customers enter, queue if needed, order, receive food, occupy
seating if applicable, pay, and leave.

**4.** Poor service, stockouts, or long waits reduce realized demand.

## Menu

- Player chooses which recipes each restaurant sells and sets prices.

- Menus should match local tastes and the supply network behind the
  restaurant.

- A larger menu can attract more demand but increases inventory and
  production complexity.

## Capacity Constraints

- Kitchen throughput.

- Ingredient stock.

- Employee availability.

- Queue/service rate.

- Dining seats or takeaway handoff capacity.

- Delivery/replenishment reliability.

# 8. Factories & Production

- Factories process ingredients into intermediate or finished foods.

- Factories are used for food and ingredient processing. Non-food
  equipment and infrastructure components are purchased rather than
  manufactured by the player.

- Food production chains use physical inputs, outputs, buffers, labor,
  space, and transport connections.

- Machines and equipment are purchased for money; no technology tree is
  required to access them.

# 9. Logistics

## Trucks

- Flexible point-to-point delivery.

- Lower infrastructure cost.

- Best for short routes, mixed cargo, and lower volume.

- Affected by road distance and congestion.

## Trains

- High throughput over long distances.

- Require tracks, stations, trains, loading infrastructure, and
  scheduling.

- Best for repeated bulk flows between major hubs.

## Logistics Rules

- Goods exist as physical inventory at a location.

- Logistics routes are manually configured by the player.

- The player chooses pickup/dropoff points, allowed cargo, and assigns vehicles.

- Once configured, assigned vehicles repeat the route automatically.

- Loading/unloading capacity can become a bottleneck.

- Perishability makes route time and buffering meaningful.

# 10. Farms & Raw Materials

- Farms across the map can be purchased.

- They produce raw ingredients used directly by restaurants or sent
  through processing chains.

- Owning farms reduces dependence on external suppliers but adds
  capital, logistics, and management requirements.

- Draft assumption: farms have location-specific output and production
  rates rather than detailed farming simulation.

# 11. Competition & Acquisitions

- AI restaurants compete for the same local customer pool.

- Competitors gain share when their cuisine, price, reputation, or waiting
  time better matches local demand.

- Poorly performing competitors can become purchasable.

- Purchased restaurants retain their site/building and can be redesigned
  or integrated into the player network.

- Competition should pressure the player without requiring constant
  direct sabotage or combat.

# 12. Economy

## Revenue

- Restaurant food sales.

- Sale of raw ingredients, intermediate foods, or finished foods to
  non-player restaurants and distributors.

- Optional contracts/scenario rewards.

## Costs

- Ingredients and outside supplies.

- Employee wages.

- Land/building purchases or rent.

- Machines and equipment.

- Vehicles, fuel/energy, and maintenance.

- Outsourced construction and infrastructure.

- Spoilage and waste.

- Optional utilities/taxes depending on desired simulation depth.

Primary progression gate: cash flow. Advanced systems are available
immediately but become practical only when the player can afford and
support them.

# 13. Progression

- No traditional research tree required.

- Soft progression comes from capital, scale, logistics knowledge,
  workforce, floor space, and network complexity.

- Early game begins with a tiny restaurant and emphasizes direct player
  work, purchased ingredients/equipment, and optional sales to outside
  restaurants or distributors.

- Mid game emphasizes employees, multiple sites, and dedicated
  production.

- Late game emphasizes rail, specialization, vertical integration,
  acquisitions, and large-scale food distribution.

# 14. Failure, Pressure & Recovery

- Businesses can operate at a loss and eventually become insolvent.

- Common failure causes: overexpansion, stockouts, spoilage, excessive
  wages, poor menu-market fit, congestion, and underused capital
  equipment.

- Player should be able to recover by selling assets, simplifying menus,
  reducing staff, closing locations, or returning to outside suppliers.

- Draft assumption: bankruptcy ends a scenario/run only when debt/cash
  limits are exceeded; exact rule TBD.

# 15. Controls & Information

- Direct 3D character movement and interaction for local tasks.

- Build/planning mode for placing layouts, furniture, machines, roads,
  and rails; construction is completed by paying the required cost.

- Management overlays for inventory, freshness, throughput, profit,
  demand, staffing, and transport.

- Remote task assignment lets the player manage distant sites without
  physically traveling there.

- Alerts identify stockouts, spoilage risk, route failures, queues, and
  unprofitable locations.

# 16. Intended Pacing

- Early: hands-on restaurant work, basic purchasing, simple deliveries,
  first hires.

- Mid: multiple restaurants, food factories, owned farms, manually configured
  truck routes, external wholesale sales.

- Late: regional specialization, rail, acquisitions, large distribution
  networks, and mostly delegated operations.

| Phase | Player Focus                    | Typical Systems                                                                    |
|-------|---------------------------------|------------------------------------------------------------------------------------|
| Early | Hands-on operation and survival | One site, purchased ingredients, manual work, first hires                          |
| Mid   | Automation and network building | Multiple restaurants, trucks, factories, farms, specialization                     |
| Late  | Optimization and expansion      | Rail, acquisitions, regional chains, large-scale distribution, vertical integration |

# 17. Rough MVP Scope

- One procedural 3D region with several districts.

- Player character + general-purpose employees.

- One farm type, several ingredients, several recipes.

- One restaurant type with editable kitchen/dining layout.

- Basic food factory processing plus the ability to sell output to
  outside restaurants/distributors.

- Truck logistics.

- Binary edible/spoiled food condition + refrigeration.

- AI competitors and local demand.

- Cash economy and purchasing of properties/machines.

- Rail and larger-scale distribution systems can follow after the core
  loop is proven.

# 18. Major Decisions Still To Lock

- Starting position: LOCKED - tiny restaurant with hands-on early play.

- Hands-on cooking/production: LOCKED - automation-first station interactions; tasks run automatically once started.

- Logistics routes: LOCKED - player manually creates pickup/dropoff routes and assigns vehicles.

- Employee simulation depth: LOCKED - simple workforce; employees mainly differ by wage and assignment.

- External sales: LOCKED - non-player restaurants and distributors can
  buy player-produced food; contract/pricing details TBD.

- Customer choice: LOCKED - each customer individually evaluates nearby restaurants using price, cuisine fit, distance, reputation, and wait time.

- Food condition: LOCKED - binary edible or spoiled; no graded quality score.

- Property ownership and rent rules. Construction is LOCKED as
  outsourced for money.

- How roads/rails are built and who owns them.

- How bankruptcy, loans, and recovery work.

- Whether there are scenarios/campaign goals in addition to sandbox
  play.

- Exact camera transition between outdoor isometric and indoor top-down
  views.

Decision process: handle these one at a time. For each decision, present
three distinct options, select one, and update this document.

# 19. Decision Record: Starting Position

- A. Restaurant Start - begin with a tiny restaurant and do much of the
  work personally. \[SELECTED\]

- B. Factory Start - begin with a small food factory supplying AI
  restaurants.

- C. Open Sandbox Start - begin with cash/land and choose restaurant,
  farm, factory, or logistics as the first business.

Status: selected - Restaurant Start.

# 20. Next Decision: Hands-On Food Preparation

- A. Simple interactions - short click/hold tasks; layout and timing
  matter more than execution skill.

- B. Light minigames - simple chopping, cooking, assembly, or timing
  interactions can improve speed/quality.

- C. Automation-first - the player uses the same stations as employees,
  but tasks run automatically once started. [SELECTED]

Status: selected - Automation-first.


# 21. Next Decision: Logistics Route Creation

- A. Manual routes - player explicitly creates each pickup/dropoff route and assigns vehicles.

- B. Demand-driven automation - storage locations request goods automatically and available vehicles fulfill demand.

- C. Hybrid - player defines routes, allowed goods, or supply/demand links; vehicles then automate the detailed trips.

Status: selected - Manual routes.

# 22. Decision Record: Employee Simulation Depth

- A. Simple workforce - employees mainly differ by wage and current assignment; minimal individual stats or needs. [SELECTED]

- B. Skills only - employees gain job-specific skills that affect speed/quality, but there are no personal needs or moods.

- C. Light people simulation - skills plus simple needs such as energy, breaks, and satisfaction affect performance.

Status: selected - Simple workforce.

# 23. Next Decision: Customer Choice Simulation

- A. Aggregate demand - each restaurant gets a calculated demand score; customers are mostly visual representation.

- B. Individual choice - each customer evaluates nearby restaurants using price, quality, cuisine fit, distance, and wait time. [SELECTED]

- C. Hybrid - districts generate demand in aggregate, then spawned customers choose among nearby restaurants using a simpler score.

Status: selected - Individual choice.

# 24. Next Decision: Food Quality Calculation

- A. Weighted score - ingredient quality, freshness, recipe complexity, and execution combine into one simple quality score.

- B. Weakest-link model - each production step can cap final quality, so poor ingredients or bad processing meaningfully limit the result.

- C. Multiple attributes - food tracks separate qualities such as taste, freshness, and presentation; different customers value them differently.

Status: undecided.

# 23. Decision Record: Food Condition

- Food has a binary condition: edible or spoiled. [SELECTED]

- No graded freshness or quality score is used.

- Refrigeration only affects how long an item remains edible.

Status: selected - Binary edible/spoiled condition.
