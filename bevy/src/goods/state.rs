// Saved world records, ported field for field from Unity's GoodsSnapshot and its record classes. Everything the server owns is
// here and nothing else is persisted: content (recipes, offers, lots for sale) is registered at start and never saved. Empty
// strings mean "none", as in the Unity records, so a snapshot reads the same whatever produced it.
use serde::{Deserialize, Serialize};

pub const CURRENT_SCHEMA: i32 = 1;

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Location {
    pub id: String,
    pub site_id: String,
    pub kind: String,
    /// Slots (decision 0009).
    pub capacity: i32,
    pub refrigerated: bool,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Lot {
    pub id: String,
    pub item_id: String,
    pub owner_id: String,
    pub location_id: String,
    pub quantity: i32,
    pub exposure_seconds: i64,
    pub spoil_after_seconds: i64,
    pub spoiled: bool,
    /// Path position (belt units) while the lot rides a belt; 0 anywhere else.
    pub belt_position: i32,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Reservation {
    pub id: String,
    pub lot_id: String,
    pub player_id: String,
    pub quantity: i32,
    pub active: bool,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Outcome {
    pub request_id: String,
    pub player_id: String,
    pub accepted: bool,
    pub reason: String,
    pub moved_lot_id: String,
    pub reservation_id: String,
    pub job_id: String,
    /// Machine, truck or employee created by the request; empty otherwise.
    pub equipment_id: String,
    /// Cash the accepted order moved: positive when the company paid, negative for a refund.
    pub cents: i64,
    pub revision: i64,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Grant {
    pub player_id: String,
    pub site_id: String,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Station {
    pub id: String,
    pub site_id: String,
    pub kind: String,
    pub input_location_id: String,
    pub output_location_id: String,
}

#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum JobState { #[default] Running, Blocked }

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Job {
    pub id: String,
    pub station_id: String,
    pub recipe_id: String,
    pub started_by: String,
    pub started_at_seconds: i64,
    pub duration_seconds: i64,
    pub remaining_seconds: i64,
    pub state: JobState,
    pub output_item_id: String,
    pub output_quantity: i32,
    pub output_spoil_after_seconds: i64,
    pub sale_cents: i64,
    /// Consumed input slices with their exposure frozen at start; location empty until refunded.
    pub inputs: Vec<Lot>,
}

impl Job {
    pub fn output_lot_id(&self) -> String { format!("{}:out", self.id) }
    pub fn is_sale(&self) -> bool { self.sale_cents > 0 }
}

#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum EquipmentState { #[default] Placed, Held }

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Equipment {
    pub id: String,
    pub kind: String,
    pub site_id: String,
    pub state: EquipmentState,
    /// Actor whose inventory holds the piece; empty while placed.
    pub holder_id: String,
    pub cell_x: i32,
    pub cell_z: i32,
    pub rotation: i32,
    pub level: i32,
    pub width: i32,
    pub depth: i32,
    pub input_capacity: i32,
    pub output_capacity: i32,
    pub input_refrigerated: bool,
    pub output_refrigerated: bool,
    pub seats: i32,
    /// Occupancy layer: "" object, or floor / wall / ceiling / tabletop decor.
    pub layer: String,
    pub ambience: i32,
    pub charged_cents: i64,
    /// Who works a register or worker-bound station; empty when unstaffed.
    pub staff_id: String,
    /// The recipe the machine is set to make; empty is automatic.
    pub recipe_id: String,
    pub powered_on: bool,
    pub held_off: bool,
}

impl Equipment {
    pub fn input_location_id(&self) -> String { format!("{}:in", self.id) }
    pub fn output_location_id(&self) -> String { format!("{}:out", self.id) }
    pub fn placed(&self) -> bool { self.state == EquipmentState::Placed }
    /// Width and depth as placed (odd quarter turns swap them).
    pub fn footprint(&self) -> (i32, i32) { super::grid::footprint(self.width, self.depth, self.rotation) }
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct SiteLayout {
    pub site_id: String,
    pub width: i32,
    pub depth: i32,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Belt {
    pub id: String,
    pub site_id: String,
    pub cell_x: i32,
    pub cell_z: i32,
    /// Travel direction 0..3 (0 = +Z, 1 = +X, 2 = -Z, 3 = -X).
    pub direction: i32,
    pub level: i32,
    /// 0 flat; +1 or -1 for a conveyor lift.
    pub lift: i32,
}

impl Belt {
    pub fn location_id(&self) -> String { format!("{}:items", self.id) }
    pub fn exit_level(&self) -> i32 { self.level + self.lift }
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Company {
    pub id: String,
    pub cash: i64,
    pub site_ids: Vec<String>,
    pub opening_cents: i64,
    pub ledger_carried_cents: i64,
    pub ledger_next_number: i64,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct LedgerEntry {
    pub id: String,
    pub company_id: String,
    pub site_id: String,
    pub kind: String,
    pub cents: i64,
    pub clock_seconds: i64,
    pub revision: i64,
    pub request_id: String,
    pub equipment_id: String,
    pub customer_id: String,
    pub offer_id: String,
}

#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq, Hash)]
#[serde(default)]
pub struct GridCell {
    pub x: i32,
    pub z: i32,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Structure {
    /// floor, wall, partition, door or window.
    pub kind: String,
    pub x: i32,
    pub z: i32,
    /// Windows: covers (x, z) and the next cell along X (0) or Z (1).
    pub axis: i32,
    pub style: String,
    pub charged_cents: i64,
    /// Doors: "service" for a back door; empty for a customer door.
    pub role: String,
}

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Building {
    pub id: String,
    pub site_id: String,
    /// restaurant or factory.
    pub kind: String,
    pub cell_x: i32,
    pub cell_z: i32,
    pub width: i32,
    pub depth: i32,
    pub doors: Vec<GridCell>,
    pub floors: i32,
    pub elevator_x: i32,
    pub elevator_z: i32,
    pub structures: Vec<Structure>,
    pub wall_style: String,
    pub free_walls: bool,
}

impl Default for Building {
    fn default() -> Self {
        Self {
            id: String::new(), site_id: String::new(), kind: "restaurant".into(), cell_x: 0, cell_z: 0, width: 0, depth: 0,
            doors: vec![], floors: 1, elevator_x: 0, elevator_z: 0, structures: vec![], wall_style: String::new(), free_walls: false,
        }
    }
}

impl Building {
    pub fn has_elevator(&self) -> bool { self.floors > 1 }
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Employee {
    pub id: String,
    pub site_id: String,
    pub name: String,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub yaw: f32,
    pub script: String,
    pub script_running: bool,
    pub tasks: String,
    pub unpaid: bool,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Site {
    pub id: String,
    pub name: String,
    pub map_x: i32,
    pub map_z: i32,
}

#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum TruckState { #[default] Parked, ToPickup, Loading, ToDropoff, Unloading, ToPark }

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Truck {
    pub id: String,
    pub company_id: String,
    pub name: String,
    pub cargo_slots: i32,
    pub speed_metres_per_second: i32,
    pub load_units_per_second: i32,
    pub route_id: String,
    pub state: TruckState,
    pub site_id: String,
    pub destination_site_id: String,
    pub remaining_seconds: i64,
    pub docked: bool,
    pub leg_segment_id: String,
    pub leg_from: i32,
    pub leg_to: i32,
    pub leg_drive_seconds: i64,
    pub leg_seconds: i64,
    pub queued_seconds: i64,
    pub next_segment_id: String,
    pub next_from: i32,
    pub next_to: i32,
    pub next_final: bool,
    pub order_id: String,
    pub supplier: bool,
}

impl Truck {
    pub fn cargo_location_id(&self) -> String { format!("{}:cargo", self.id) }
    pub fn driving(&self) -> bool { matches!(self.state, TruckState::ToPickup | TruckState::ToDropoff | TruckState::ToPark) }
    pub fn on_road(&self) -> bool { !self.leg_segment_id.is_empty() }
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Route {
    pub id: String,
    pub company_id: String,
    pub pickup_dock_id: String,
    pub dropoff_dock_id: String,
    pub allowed_item_ids: Vec<String>,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct District {
    pub id: String,
    pub name: String,
    pub map_x: i32,
    pub map_z: i32,
    pub customers_per_hour: i32,
    pub wealth_percent: i32,
    pub appearance: String,
    pub appearance_variants: i32,
    pub liked_cuisines: Vec<String>,
    pub dine_in_percent: i32,
    pub range_metres: i32,
    pub spawn_progress: i64,
    pub spawn_walk_metres: i32,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Competitor {
    pub id: String,
    pub name: String,
    pub lot_id: String,
    pub map_x: i32,
    pub map_z: i32,
    pub cuisine: String,
    pub tier: i32,
    pub price_cents: i64,
    pub servers: i32,
    pub service_seconds: i64,
    pub seats: i32,
}

#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub enum CustomerState { #[default] Travelling, Queued, Ordering, Eating }

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Customer {
    pub id: String,
    pub district_id: String,
    pub appearance: i32,
    pub dine_in: bool,
    pub patience_seconds: i64,
    pub state: CustomerState,
    pub restaurant_id: String,
    pub recipe_id: String,
    pub left_restaurant_id: String,
    pub remaining_seconds: i64,
    pub queued_at_seconds: i64,
    pub ticket: i64,
    pub counter_id: String,
    pub table_id: String,
    pub seat: i32,
    pub paid_cents: i64,
}

impl Default for Customer {
    fn default() -> Self {
        Self {
            id: String::new(), district_id: String::new(), appearance: 0, dine_in: false, patience_seconds: 0,
            state: CustomerState::Travelling, restaurant_id: String::new(), recipe_id: String::new(),
            left_restaurant_id: String::new(), remaining_seconds: 0, queued_at_seconds: 0, ticket: 0, counter_id: String::new(),
            table_id: String::new(), seat: -1, paid_cents: 0,
        }
    }
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Diner {
    pub restaurant_id: String,
    pub reputation: i32,
    pub published_wait_seconds: i64,
    pub published_free_seats: i32,
    pub served: i64,
    pub walked_out: i64,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Property {
    pub lot_id: String,
    pub site_id: String,
    pub company_id: String,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct FarmOrder {
    pub id: String,
    pub company_id: String,
    pub player_id: String,
    pub farm_lot_id: String,
    pub offer_id: String,
    pub item_id: String,
    pub units: i32,
    pub spoil_after_seconds: i64,
    pub goods_cents: i64,
    pub fee_cents: i64,
    pub site_id: String,
    pub dock_id: String,
    pub truck_id: String,
    pub pickup: bool,
    pub schedule_id: String,
    pub placed_seconds: i64,
    pub loaded: bool,
    pub unloaded_units: i32,
    pub wait_seconds: i64,
    pub returning: bool,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct FarmSchedule {
    pub id: String,
    pub company_id: String,
    pub player_id: String,
    pub farm_lot_id: String,
    pub offer_id: String,
    pub packs: i32,
    pub dock_id: String,
    pub truck_id: String,
    pub every_hours: i32,
    pub next_hour: i64,
    pub last_problem: String,
}

/// The whole saved world, or (from `GoodsWorld::view`) one site's authorized baseline.
#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct Snapshot {
    pub schema_version: i32,
    pub world_id: String,
    pub clock_seconds: i64,
    pub revision: i64,
    pub locations: Vec<Location>,
    pub lots: Vec<Lot>,
    pub reservations: Vec<Reservation>,
    pub outcomes: Vec<Outcome>,
    pub grants: Vec<Grant>,
    pub stations: Vec<Station>,
    pub jobs: Vec<Job>,
    pub equipment: Vec<Equipment>,
    pub site_layouts: Vec<SiteLayout>,
    pub belts: Vec<Belt>,
    pub companies: Vec<Company>,
    pub buildings: Vec<Building>,
    pub employees: Vec<Employee>,
    pub sites: Vec<Site>,
    pub trucks: Vec<Truck>,
    pub routes: Vec<Route>,
    /// View-only: other companies' trucks driving near a viewer. Always empty in a stored world.
    pub road_trucks: Vec<Truck>,
    pub districts: Vec<District>,
    pub competitors: Vec<Competitor>,
    pub customers: Vec<Customer>,
    pub diners: Vec<Diner>,
    pub next_customer_number: i64,
    pub next_piece_number: i64,
    pub customer_random: i64,
    pub wages_paid_hour: i64,
    pub next_employee_number: i64,
    /// View-only: wages per game hour of the viewed site's company. Always 0 in a stored world.
    pub company_wage_cents_per_hour: i64,
    pub properties: Vec<Property>,
    pub ledger: Vec<LedgerEntry>,
    pub farm_orders: Vec<FarmOrder>,
    pub farm_schedules: Vec<FarmSchedule>,
    /// Counter behind generated IDs that Unity made from GUIDs (jobs, splits, belts), so they stay unique and deterministic.
    pub next_unique: i64,
}

impl Snapshot {
    pub fn new(world_id: &str) -> Self { Self { schema_version: CURRENT_SCHEMA, world_id: world_id.to_string(), ..Default::default() } }

    pub fn location(&self, id: &str) -> Option<&Location> { self.locations.iter().find(|x| x.id == id) }
    pub fn lot(&self, id: &str) -> Option<&Lot> { self.lots.iter().find(|x| x.id == id) }
    pub fn equipment_of(&self, id: &str) -> Option<&Equipment> { self.equipment.iter().find(|x| x.id == id) }
    pub fn layout_of(&self, site: &str) -> Option<&SiteLayout> { self.site_layouts.iter().find(|x| x.site_id == site) }
    pub fn company_of_site(&self, site: &str) -> Option<&Company> { self.companies.iter().find(|x| x.site_ids.iter().any(|s| s == site)) }
    pub fn site(&self, id: &str) -> Option<&Site> { self.sites.iter().find(|x| x.id == id) }
    pub fn can_view(&self, player: &str, site: &str) -> bool { self.grants.iter().any(|g| g.player_id == player && g.site_id == site) }
    /// The site a view describes: its own layout's site, or else its first location's.
    pub fn view_site_id(&self) -> Option<&str> {
        self.site_layouts.first().map(|x| x.site_id.as_str()).or_else(|| self.locations.first().map(|x| x.site_id.as_str()))
    }
}
