# Bevy core integration interface (in progress; not Unity parity)

`food_factory_bevy::{domain,content,persistence,network}` exports headless gameplay; `main.rs`, Bevy systems, input and presentation are owned separately. Game/UI never mutate `Store.world` directly after bootstrapping. No gameplay action trusts client-reported actor ID.

## Public records

- `domain::World::new(id)` owns `sites`, `locations`, `lots`, `reservations`, `companies`, `ledger`, `equipment`, `buildings`, `belts`, `jobs`, `employees`, `customers`, `routes`, `trucks`, `properties`, `grants` and replay outcomes; all IDs are `String` and snapshot state is `serde` serializable.
- `domain::Site`, `Location`, `Lot`, `Equipment`, `Building`, `Belt`, `Company` etc. are public record structs. `World::grant(actor, site)` is server-side bootstrap only; do not offer it as a client action.
- `domain::Catalog` has `stack`, `recipes`, `prices`. `content::load_unity_catalog(repository_root: &Path) -> Result<Catalog,String>` imports item/recipe definitions from existing Unity-authored assets at development time; never stores runtime gameplay there.
- `domain::SiteView` is the authorized projection via `World::site_view(site) -> Result<SiteView,String>`. It has site, second, revision, site lots/locations/equipment/buildings/belts/jobs/employees (scripts redacted)/customers and owning company. Do not present the complete `World` to a remote client.

## Server operations

- `domain::Command` is `serde` tagged `type` in snake case: `Transfer{lot,destination,quantity,reservation}`, `Reserve{lot,quantity}`, `Cancel{reservation}`, `SetPower{equipment,on}`, `SelectRecipe{equipment,recipe}`, `StartJob{equipment,recipe}`, `PlaceEquipment{equipment,x,z,level,rotation}`, `PickupEquipment{equipment}`, `PlaceBelt{site,x,z,level,direction,lift}`, `CreateRoute{pickup,dropoff,items}`, `AssignTruck{truck,route}`, `Purchase{site,item,quantity,price_cents,delivery}`. Commands are incomplete relative to Unity; never render an unsupported action as implemented.
- `persistence::Store::open(explicit_path: &Path, seed: Option<World>, catalog: Catalog) -> Result<Store,String>` opens a SQLite world DB, validates snapshot/checksum and seeds only an empty DB. `Store::command(&mut self, authenticated_actor, request_id, command) -> Outcome` applies and durably commits accepted actions before returning. `Store::tick(&mut self) -> Result<(),String>` advances **one** server second, commits or rolls back. `Store.world.site_view(site)` drives local/remote visuals. The local host must use the same request path as remote clients.
- `domain::Outcome {accepted,reason,revision,entity}`; accepted replays use `(authenticated_actor,request_id)` after recovery; rejected requests change no world state. Do not infer admission/permission from rendered positions.
- `network` will expose a bound loopback-capable TCP session server with connection-resolved credentials, per-site subscription, command result and full revisioned site baseline; details forthcoming. Network is not integrated into `main.rs` yet.

## Boundaries / known gaps

The current core is under construction. UI should treat records as read-only and call `Store::command` for supported mutations. Player movement/avatar remains your sandbox implementation and is not authoritative gameplay movement. Do not claim Unity parity, save migration, worker Lua, customer choice or logistics transport until implemented and verified. Leave original Unity untouched. Separate-process multiplayer and scale remain unverified.
