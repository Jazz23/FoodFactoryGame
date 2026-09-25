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

## Construction

Confirmed requirements (sections 5, 15, 18, 25-27):

- Construction is outsourced and paid for with money. The player and employees do not build; there are no construction materials, construction jobs, or construction research.
- Properties are purchased outright. Construction happens on land the player owns.
- Structural changes to buildings (not only new buildings) are construction.
- Factories can add floors. Goods move between floors by conveyor lifts; workers and bulk/manual loads use freight elevators.
- Buildings are procedurally generated with the map, and some of them are available for purchase. Adding floors to an owned building is a construction option. Whether players can construct fully custom buildings is undecided. (Project owner, 2026-09-24.)
- Rail track and stations are player-built infrastructure. Roads are public and are never constructed by the player.
- Construction is planned in build/planning mode and completed by paying the required cost.
- Indoors uses the top-down camera aligned to the building grid, so building layouts are grid-based.

Required by the development constraints (`AGENTS.md`), not optional design:

- The server validates and owns every construction order, its payment, and its progress. A client only requests an order.
- A construction order charges its cost exactly once. A rejected, failed, or cancelled order never charges without delivering, charges twice, or deletes goods or equipment.
- Construction that would cover placed equipment, belts, or goods is rejected, or those items are moved to a recorded location first. They are never silently destroyed.
- Construction in progress is saved in SQLite and continues while the world simulation runs, whether or not any client is viewing the site.

Proposal - what counts as construction (not yet approved):

- **Construction:** buying and building on land, building shells (outer walls, doors, footprint), interior walls, extra floors, demolition, elevator and conveyor-lift shafts, and rail track, stations, and loading infrastructure.
- **Not construction:** machines, furniture, storage, refrigeration units, and conveyor belts. These are purchased equipment that the player or an employee places, moves, and picks up directly, as equipment purchases and placement work today. Placing equipment costs no construction fee and does not wait for contractors.
- This split keeps restaurant and factory layout editing hands-on and fast, while structural growth is a deliberate, planned investment.

Proposal - construction order flow (not yet approved):

1. In build/planning mode the player draws or selects a structural change on owned land. A preview shows validity problems (not owned, overlaps, blocks a required doorway, covers equipment) and the price.
2. The player confirms. The server validates the order again and charges the company.
3. The order completes immediately or becomes a construction site visible to all clients until it completes, depending on the timing decision (section 29.2 and 29.3).
4. On completion the structure becomes normal server state: walls block movement and placement, interior cells become usable floor area, and new floors become reachable through their elevators and lifts.

Proposal - costs (not yet approved):

- Price scales with the size of the change (for example, per wall cell, per floor cell, per extra story, per track segment), plus a fixed charge per order so many tiny orders are not cheaper than one planned order.
- Land and district set a price multiplier: expensive districts cost more to build in as well as to buy.
- Upper floors cost more per cell than ground floors, so vertical expansion is a response to scarce land rather than the default.

Open construction decisions are listed in section 29.

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

- Player chooses which recipes each restaurant sells. Prices are fixed per recipe; the player does not set them (owner decision 2026-09-25, revising "and sets prices").

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

- Physical goods representation: LOCKED - location-based goods with selective visual representations (section 28).

- Buildings and floors: LOCKED - buildings are procedurally generated and some are purchasable; adding floors is a construction option. Fully custom buildings are undecided (section 29.4). Adding floors: factories only, freight elevator, immediate on payment, conveyor lifts later (section 29.8).

- Construction details: open - construction scope, timing, disruption, custom buildings, empty land, demolition, cancellation, and floor rules (section 29).

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

Owner decisions recorded 2026-09-25 (details and open items: `docs/decisions/0024-customer-simulation.md`):

- Districts create customers at a district-specific density; each district's customers look different; customers appear out of the player's view.
- Wealthy districts demand nicer food, expressed as recipe/item tier only; food condition stays binary (section 24 unchanged). Wait time matters; spoiled food is never served.
- Takeaway exists, but most customers prefer dining in; free seats count when choosing, so competitors with open tables tend to win them.
- Self-seating: customers queue at the counter; dine-in customers buy once a seat is free, then seat themselves. With no free seat they keep waiting until a seat frees or their patience runs out.
- Customers who are travelling or queued are saved and resume after a restart.

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

## Selected Physical Goods Model

Selected by the project owner on 2026-09-22. "Physical" means that goods occupy a definite place in the world and must move through its logistics. It does not require a separate GameObject, rigidbody, or network object for every unit.

- The server records each lot's stable identity, item type, quantity, owner, edible/spoiled condition, spoilage history, and exactly one physical location. Locations include storage, machine buffers, a character's carried inventory, vehicle cargo, transport in progress, and goods placed in the world.
- Storage, buffers, and cargo may hold batches. Splitting a batch preserves the original goods' condition and spoilage history. Merge only goods with compatible gameplay-relevant state; stacking must never reset spoilage or erase a meaningful difference.
- Players, employees, machines, conveyors, and vehicles move goods through validated pickup, processing, loading, transit, and drop-off actions. Location, capacity, travel time, and refrigeration continue to matter. A failed or cancelled move must leave the goods at a recorded location without loss or duplication.
- Nearby carried goods, placed goods, and visible transport may use visual packages representing one or more authoritative units. The presentation must reflect the actual goods and support interaction with the authoritative lot or quantity; destroying or unloading a visual does not destroy the goods.
- Distant goods retain their inventory, processing, transport, and spoilage state while the world simulation runs, even without local visual objects. Client visibility does not determine their progression.

The exact package sizes, visual style, interaction ranges, and use of rigidbody physics for exceptional loose goods remain implementation and tuning decisions. Food condition remains binary as specified in section 6.

## Development Constraints and Decision Status

The development instructions require server-owned gameplay state, validated client action requests, and simulation independent of client presentation. Decision 0002 records the accepted technical starting design for those constraints; it is not a claim of an implemented networking system.

Open product decisions:

- Maximum concurrent players and multiplayer ownership/cooperation rules.
- Hosting model and whether a world continues after its host disconnects; offline progression is not implied by distant-site operation.
- Confirm world-wide versus per-site population targets and expected simultaneous client visibility.
- Specify target CPU, GPU, RAM, resolution, and quality settings for the mid-range PC benchmark.

Accepted technical design with implementation pending:

- Simulation scheduling, replication/interest rules, and persistence contracts are defined in `docs/decisions/0002-authoritative-multiplayer-foundation.md`; the selected physical goods model is detailed in `docs/decisions/0003-physical-goods-model.md`. Both still require runtime verification.

The accepted development starting point is an authoritative server with a listen-server path and a headless-compatible simulation, as recorded in `docs/decisions/0002-authoritative-multiplayer-foundation.md`. This does not select the shipped hosting model, dedicated hosting, host migration, or post-disconnect/offline progression. Record implementation status in `docs/architecture.md`; resource facts and setup gaps are tracked in `dev_resources.md`.

# 29. Open Decisions: Construction

None of 29.1-29.7 is selected. The owner's decisions on adding floors are recorded in 29.8. Section 5 records the confirmed construction requirements and the current proposals. Existing implementation: building shells exist as server data with walls on grid cells, created only by the server (`docs/decisions/0019-building-shells-and-indoor-camera.md`), and a factory can buy extra floors (`docs/decisions/0020-factory-floors-and-elevator.md`); buildings cannot be bought yet. Implementation choices made for those slices, including prototype prices, do not select any option below.

## 29.1 Construction Scope

- A. Structure only - shells, walls, doors, floors, demolition, shafts, and rail are construction; machines, furniture, storage, and belts are purchased equipment placed directly by characters. (Section 5 proposal.)
- B. Structure plus fixed installations - refrigeration rooms, elevators, and large machines also require outsourced installation; small equipment is placed directly.
- C. Everything through build mode - all layout changes, including equipment, are construction orders placed by contractors.

## 29.2 Construction Timing

- A. Instant - the structure appears as soon as the order is paid.
- B. Build time - each order takes simulated time scaled by its size; a construction site is visible until it completes.
- C. Build time with rush - as B, but the player can pay extra to shorten or skip the wait.

## 29.3 Disruption During Construction

- A. None - the site operates normally; only the new structure's cells are unavailable until it completes.
- B. Local disruption - the affected cells and a working area around them are blocked while work is in progress.
- C. Building closure - structural work closes the affected building (no customers, no station jobs) until it completes.

## 29.4 Layout Freedom

Owner direction (2026-09-24): buildings are procedurally generated and some are available for purchase; adding floors is an option. Whether players can also construct fully custom buildings is undecided. The options below apply only if custom buildings are added.

- A. Catalog shells - the player chooses from predefined building shells and floor plans that fit a lot.
- B. Rectangular shells - the player sizes a rectangular shell on owned land, then places doors and interior walls freely.
- C. Freeform walls - the player draws any wall layout on the grid, including non-rectangular buildings.

Related open detail: walls currently occupy whole cells. Interior walls on cell edges would keep more floor area usable but need a separate occupancy rule (decision 0019).

## 29.5 Land

Owner direction (2026-09-24): purchasable properties include procedurally generated buildings. Whether empty land is sold, and how, is undecided.

- A. Fixed parcels - land is sold as predefined lots, some empty and some with existing buildings.
- B. Grid land - the player buys any unowned cells, subject to district price.
- C. Parcels that can be merged - predefined lots can be bought together and combined into one larger site.

## 29.6 Demolition and Resale

- A. Demolition costs money and returns nothing.
- B. Demolition costs money; selling land with buildings returns part of the construction value.
- C. Demolition is free; buildings add to resale value, supporting the recovery options in section 14.

## 29.7 Cancellation and Payment

Applies only if 29.2 selects B or C.

- A. Pay up front, no refund - cancelling an order in progress forfeits its cost.
- B. Pay up front, partial refund - cancelling returns the unspent share of the cost.
- C. Pay in stages - cost is charged as work progresses; if the company cannot pay, work pauses.

In every option, cancellation must leave goods, equipment, and cash in a consistent recorded state (section 5).

## 29.8 Decision Record: Adding Floors

Selected by the project owner on 2026-09-24 for the first floors slice:

- Which buildings: factories only, as in section 5. [SELECTED] (Alternative: any owned building, including restaurants.)
- Moving between floors: freight elevator, as in section 27. [SELECTED] (Alternatives: stairs and an elevator; a temporary
  development floor switch.)
- Timing: a paid floor exists immediately. [SELECTED] (Alternatives: build time; build time with a rush payment.) This
  applies to floors only; general construction timing (29.2) remains open.
- Conveyor lifts: in a later slice. [SELECTED] Implemented 2026-09-24: owner selected lifts as belt-like items placed by
  players (not construction orders), each spanning one storey (`docs/decisions/0021-conveyor-lifts.md`). Whether lift shafts
  become construction remains part of 29.1.

Status: selected and implemented (`docs/decisions/0020-factory-floors-and-elevator.md`). Where the elevator goes, floor
prices, the height limit, and storey height are implementation prototype values, not design decisions.
