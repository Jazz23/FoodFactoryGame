# Prompt: Implement Restaurant Building

Copy the block below into an implementation agent. Replace `<N>` with the slice (1-5). Do one slice per run.

```text
You are implementing slice <N> of restaurant building in the Food Factory Game Unity project (G:\Unity\FoodFactoryGame).

READ FIRST: AGENTS.md; Food_Factory_Restaurant_GDD.md sections 5 (including "Restaurant Building"), 7 ("Registers, Seats &
Ambience"), 9, 29; docs/decisions/0034-restaurant-building.md (authoritative for this work); docs/architecture.md; decisions
0006, 0008, 0019, 0022, 0023, 0024, 0028, 0030. Check docs/development.md for the verified Unity workflow.

GOAL: the owner of a restaurant site can reshape the restaurant, place tables, registers and decor, and receive and send goods
by truck at docks. Slices:
 1. Shell editing: server commands to resize the rectangular shell within the purchased lot and add or remove interior
    walls, doors and windows; validation; payment and full refund; persistence. No UI polish beyond a minimal test hook.
 2. Build-mode UI: top-down grid, placement ghost, validity tint, price/refund preview, confirm. Use the Input System action
    maps; no hard-coded input.
 3. Tables and registers: new equipment kinds, seat counting, register as the sale point (staffed by employee or player),
    door-to-register-to-seat path rule. Decide what happens to the starting restaurant's counter (0030) and migrate.
 4. Decor and ambience: decor items (finishes, props, lighting), one ambience score per restaurant, one extra term in customer
    choice.
 5. Restaurant docks: any number of placed dock equipment pieces anywhere in the lot if street-reachable, one truck each,
    truck arrives from the street, goods transfer slowly in both directions, no animation.

CONFIRMED RULES (owner, 2026-10-01): shell resized within the lot of a bought generated building (restaurants only; land stays
"buildings only"); instant once paid; EVERYTHING refunds in full at any time (structure, tables, registers, decor, docks,
machines); furniture and decor are placed equipment with no contractor; decor is cosmetic with one ambience score per
restaurant and no upkeep; no limit on docks besides space; no restaurant floors.

PROPOSALS: the "PROPOSALS" list in decision 0034 is NOT decided. Implement each as a labelled PROTOTYPE value behind a named
constant or content asset, and list every one in your report. Do not promote them into the GDD.

CONTRACTS (non-negotiable):
- The server owns all state. Clients send requests; the server validates ownership, lot bounds, overlaps, payment and
  affected items.
- An order charges or refunds exactly once, atomically with its change. Rejection or failure changes nothing. Duplicate request
  IDs replay the stored outcome, as in the existing Transfer command.
- Reject changes that would cover equipment, belts, goods, tables or decor. Selling a piece that holds goods or runs a job
  moves the goods to a recorded location first; never delete or duplicate goods.
- Record the charged amount on each structure/item so refunds return exactly that.
- Persist only in the existing SQLite-backed snapshot. Bump the schema version with an in-memory upgrade of older versions
  and keep stable IDs. No save files, PlayerPrefs or other stores.
- Reuse SiteGrid.CellProblem, equipment placement, purchase, and the slot UI. Do not change factory floors or factory rules.
- Follow AGENTS.md C# conventions (var, purpose comment at top of each new file, Unity-aware null checks, serialized
  references validated by authoring checks).
- Do not change gameplay authoring to suit test fixtures; use isolated fixtures.

VERIFICATION (use an isolated database path; never touch the application database):
- Domain tests for the state rules and persistence/recovery of the slice.
- PlayMode tests for Unity integration where the slice touches it.
- An actual multiplayer check of command acceptance and replication (slices 1, 3, 5).
- A running-game capture for visual acceptance (slices 2, 4).
- After C# changes, wait for compilation, check for new console errors, and run the relevant tests. Establish a baseline
  before attributing a regression. On failure, capture diagnostics and state an evidence-backed hypothesis before changing
  anything.
- Report the requested test filter, run identity, matched test count and artifact path for each run. Zero matched tests is a
  failure. Do not mark criteria complete without that evidence.

DOCS: update docs/architecture.md (implemented vs planned, schema version), docs/development.md if a workflow changes, and add
a verification record in docs/verification/.

REPORT: changed files and behaviour, verification evidence, every PROTOTYPE value chosen, remaining issues, decisions needing
the owner. ESCALATE before changing any confirmed rule or if a contract cannot be met.
```
