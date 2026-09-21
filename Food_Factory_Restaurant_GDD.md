**FOOD FACTORY / RESTAURANT GAME**

**Game Design Document - Rough Draft**

*Working design - assumptions are provisional until explicitly decided*

This is the authoritative gameplay design. `gdd.md` contains superseded historical notes. Confirmed requirements, proposals, and open decisions are labeled separately; a proposal is not an approved gameplay requirement.

# 1. High Concept

A multiplayer 3D management/automation game combining food production, logistics,
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

- The game is fully 3D. Outdoors use a third-person camera that orbits the player. Indoors use a top-down camera aligned so the building floor grid reads vertically/horizontally on screen.

- Districts vary by population, wealth, cuisine preferences, land cost,
  traffic, and competition.

- Farms, competing restaurants, roads, rail corridors, and purchasable
  properties are distributed across the map.

- Distance matters for delivery cost, employee travel, freshness, and
  network design.

- Operations at distant sites continue while the world simulation is running, regardless of whether a client is viewing the site or has its presentation loaded. Whether the world continues after its host disconnects is undecided.

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

- Factories trade machine footprint against buffers, worker access, loading, and expansion space.

- Factories can add additional floors, allowing vertical expansion when land is limited.

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

## Roads

- Roads are public infrastructure and are not built or owned by the player.

- Trucks use the existing road network.

## Trains

- High throughput over long distances.

- Rail infrastructure is player-built and player-owned.

- Requires tracks, stations, trains, loading infrastructure, and scheduling.

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

- Land/building purchases.

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

- Build/planning mode for placing layouts, furniture, machines, and player-built rail; construction is completed by paying the required cost. Roads are pre-existing public infrastructure.

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

- Property ownership: LOCKED - properties are purchased outright; no renting or leasing. Construction is LOCKED as
  outsourced for money.

- Roads/rail ownership: LOCKED - roads are public/pre-existing; rail is player-built and player-owned.

- How bankruptcy, loans, and recovery work.

- Whether there are scenarios/campaign goals in addition to sandbox
  play.

- Camera model: LOCKED - outdoors use an orbiting third-person camera; indoors use a top-down camera aligned to the building grid.

- Multiplayer: LOCKED - required. Maximum concurrent players and hosting/disconnect behavior remain undecided (section 28).

- Distant operations: LOCKED - sites continue operating independently of client visibility (section 28).

- Scale/performance targets: recorded in section 28; exact benchmark hardware and population scope remain to be confirmed.

- Physical goods representation: proposed in section 28; not yet selected.

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

- B. Individual choice - each customer evaluates nearby restaurants using price, cuisine fit, distance, reputation, and wait time. [SELECTED]

- C. Hybrid - districts generate demand in aggregate, then spawned customers choose among nearby restaurants using a simpler score.

Status: selected - Individual choice.

# 24. Decision Record: Food Condition

- Food condition is binary: edible or spoiled. [SELECTED]

- No graded freshness or food-quality score is used.

- Refrigeration only affects how long an item remains edible.

Status: selected - Binary edible/spoiled condition.

# 25. Decision Record: Property Ownership

- A. Buy only - all player-owned restaurants, factories, farms, and other properties are purchased outright. [SELECTED]

- B. Rent or buy - renting lowers upfront cost while ownership reduces long-term cost.

- C. Lease only - properties are leased rather than purchased.

Status: selected - Buy only.

# 26. Decision Record: Roads & Rail

- A. Public roads, player-built rail - roads already exist; player builds and owns rail infrastructure. [SELECTED]

- B. Player-built roads and rail - player constructs the full transport network.

- C. Mostly public infrastructure - player buys access/stations/depots rather than constructing networks.

Status: selected - Public roads, player-built rail.


# 27. Decision Record: Vertical Factory Transport

- A. Freight elevators only - vertical movement is handled through elevators.

- B. Conveyor lifts only - automated conveyor systems move goods between floors.

- C. Both - conveyor lifts handle automated item flow; elevators handle workers and bulk/manual transport. [SELECTED]

Status: selected - Both conveyors and elevators.

# 28. Multiplayer, Scale & Technical Decision Status

## Confirmed Requirements

Recorded from the project owner's decisions on 2026-09-21:

- Multiplayer is required.
- Distant sites continue operating while the world simulation is running. Client visibility and presentation loading must not control whether a site operates.
- Approximate scale targets:

| Population | Target |
|------------|--------|
| Active workers | 20 |
| Customers | 1,000 |
| Goods | Thousands; exact benchmark quantity TBD |
| Vehicles | 100 |
| Sites | 20 |

- Performance target: 60 FPS on a mid-range PC (approximately 16.7 ms per rendered frame).

Planning assumption, not a confirmed decision: these populations are world-wide concurrent totals, not per-site counts. The number simultaneously visible to a client is unspecified. Server simulation capacity and client rendering performance require separate measurements; 60 FPS does not prescribe a server tick rate. No benchmark has established achievement of these targets.

## Proposed Physical Goods Model - Awaiting Approval

Goods always have an authoritative quantity, condition, owner, and physical location. They do not always require individual GameObjects, rigidbodies, or network objects.

- Storage and vehicle cargo may use inventory batches.
- Carried goods and visible transport may use visual representations of authoritative inventory or transport state.
- Distant goods retain their inventory, processing, transport, and spoilage state without requiring local visual objects.
- Batch only goods whose gameplay-relevant state is compatible. Preserve different spoilage histories; stacking must not reset spoilage or erase relevant differences.

This proposal preserves location-based logistics and the physical-world pillar. The precise interaction and representation rules remain undecided; do not implement it as a locked requirement without approval.

## Development Constraints and Pending Technical Decisions

The development instructions require server-owned gameplay state, validated client action requests, and simulation independent of client presentation. These are implementation constraints, not claims of an implemented networking system.

Open decisions:

- Maximum concurrent players and multiplayer ownership/cooperation rules.
- Hosting model and whether a world continues after its host disconnects; offline progression is not implied by distant-site operation.
- Physical goods representation: accept, revise, or replace the proposal above.
- Confirm world-wide versus per-site population targets and expected simultaneous client visibility.
- Specify target CPU, GPU, RAM, resolution, and quality settings for the mid-range PC benchmark.
- Simulation scheduling, replication/interest rules, and persistence contracts need technical design and verification.

A listen-server development path with a headless-compatible simulation is a proposal, not a selected hosting requirement. Record accepted technical contracts and implementation status in `docs/architecture.md` when the foundation work creates it. Resource facts and setup gaps are tracked in `dev_resources.md`.
