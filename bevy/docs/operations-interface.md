# Customer and employee domain integration contract

This document is the integration handoff for the customer and employee modules. It records Unity behavior that the port must retain and the exact additions needed in `domain/mod.rs`. Those additions belong to the primary owner; this subtask does not edit that file.

## Module shape

Add `pub mod customers;` and `pub mod employees;` in `domain/mod.rs`. Both modules implement `impl World` in their own file. The world tick/command owner calls these methods inside its existing staged-clone transaction, then validates/commits once. Module methods must not independently persist, increment revisions, or commit partial state.

## Employee persisted model (Unity `GoodsEmployee`)

The current `Employee` record is missing persisted pose. Add serde-default fields `x: f32`, `y: f32`, `z: f32`, and `yaw: f32` (finite values required). Retain `script`, `tasks`, `running`, and `unpaid`; a running script restarts from the first line after recovery, not from saved interpreter state. Actor inventory is `carried:<employee-id>` and is ordinary inventory, never discarded on stop/replacement. Scope all actor inventory operations to the employee's granted site.

The visual task contract is data, not the execution engine. Add public serde types in `employees.rs` and put the exact JSON string in `Employee.tasks`:

* `EmployeeTaskList { tasks: Vec<EmployeeTask> }`.
* `EmployeeTask { kind: Move|Power|Work|Recipe, source: String, destination: String, any_item: bool, whitelist: bool, items: Vec<String>, machine: String, power: On|Off|Toggle, recipe: String }`.
* Use serde defaults for absent string/list/boolean/enum fields and preserve unknown/invalid task data only if the primary chooses a forward-compatible representation; Unity deserialization defaults missing values and removes null tasks. Unity names are PascalCase (`Type`, `Move`, `Power`, `Work`, `Recipe`, `Source`, `Destination`, `AnyItem`, `Whitelist`, `Items`, `Machine`, `Power`, `Recipe`, `On`, `Off`, `Toggle`), so use explicit serde renames/aliases if the stored JSON remains compatible with Unity.

Unity generates Lua as the executable program from this visual list. `EmployeeWorker` runs arbitrary Lua through MoonSharp; operations include movement, transfers, machine placement/pickup, belt placement, power, recipe selection, work, queries, and messaging. A structured task interpreter is **not equivalent** to arbitrary Lua and must not be represented as full parity. Do not add `mlua`/a new scripting dependency in this subtask; choose/confirm the runtime integration with the primary owner.

Operational invariants to preserve in any implemented command/tick path: player commands require site grant; unpaid actors cannot work/staff; player-held-off machines cannot be switched on by workers; staff assignments and held-off switches are saved maps (`staff: equipment -> actor`, `held_off: equipment IDs`); worker-bound jobs pause when no paid worker is assigned; a worker work task leaves a station after one batch or timeout, while counter assignment persists; fire must refuse to silently delete carried goods, held equipment, or reservations. Wage retry/automatic starts are owned by primary.

## Customer persisted model (Unity `GoodsSnapshot`)

The current `Customer { id, site, state, remaining }` is insufficient for Unity behavior. Required world-owned persisted records and their semantic fields:

* `District`: stable `id`, name/map coordinates, customers-per-game-hour, wealth, appearance and variant count, liked cuisines, dine-in percentage, range, fractional spawn progress, and spawn-walk distance.
* `Competitor`: stable ID/name/lot/map coordinates, cuisine/tier/price, servers/service duration/seats (fixed AI data, unlimited stock, no cash).
* `Customer`: stable ID/district/appearance, dine-in, patience, state (`Travelling`, `Queued`, `Ordering`, `Eating`), selected restaurant and recipe, last restaurant left, remaining and queued-at seconds, monotonically increasing ticket, serving counter, table/seat, and paid cents.
* `Diner`: stable restaurant ID, reputation, published wait/free seats, served and walked-out counters.
* World counters: deterministic customer RNG state and next customer/ticket number. District/competitor/diner/customer records must be world/server data, never derived from a site view or presentation subscription.

Use serde defaults for migration of absent fields; do not map the four-state customer lifecycle onto `remaining` alone. Site views must expose only that site's customers/diner while withholding district, competitor, and global RNG data.

Unity tick contract: districts accrue `CustomersPerHour` each game second against `GameClock.HourSeconds`; spawn uses persisted xorshift64* random state. Customers score eligible nearby restaurants once at choice/rechoice, with staying-home weight 1, tier/cuisine/price/wealth, published wait/free seats, reputation, ambience and dine-in seat preference; player candidates require company, a staffed reachable register and a stocked sale recipe meeting minimum tier. Travel includes district walk plus in-lot entry time. Arrivals queue by stable ticket. Queue service skips customers who cannot be served, purchases atomically debit eligible unspoiled/unreserved counter input and credit company, seats/counter/server are claimed in that same staged tick; dine-in eats then releases seat, takeaway exits after service. A first queue timeout increases walkout/reputation penalty and rechooses excluding the last restaurant; second walkout exits. Published estimates update every 5 seconds; reputation drifts one toward zero every 60 seconds. One-second substeps are needed for long ticks to match repeated short ticks.

The present Rust domain lacks cuisine/tier/sale recipe semantics, restaurant reachability/table-register path mapping, published diner data, and reliable site-local cash-note IDs; customer parity is therefore blocked until the primary adds the exact persisted fields and connects its authoritative catalog/path/ledger contracts. Do not fake this with a simplified `state/remaining` decrement.

## Command additions

No new gameplay command is required to represent customer transitions: they are server simulation mutations during the staged tick. Employee commands for script/task assignment and rename do require a contract before implementation: Unity durably commits validated source/running/tasks and trimmed names (1–32 chars, no control chars), and keeps task text unchanged if script is assigned without tasks. Primary should add command variants with an authenticated actor/site check and expected error taxonomy only after selecting the interpreter and durable request API. Existing Hire/Fire/Staff/Unstaff commands are primary-owned; extend their validation/side effects rather than duplicating command application here.
