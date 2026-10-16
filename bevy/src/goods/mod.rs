// The authoritative server simulation, ported from Unity's FoodFactoryGame.Goods (GoodsWorld and its partial classes). It owns
// scene-independent goods, cash, machines, buildings, employees, trucks and customers, the integer-second clock, and replayable
// terminal command outcomes. Nothing here depends on Bevy, a camera or a client: presentation reads views and sends commands.
// Durability is the caller's: `persistence::Store` snapshots before a command and restores on a failed commit.
pub mod belts;
pub mod buildings;
pub mod company;
pub mod customers;
pub mod employees;
pub mod equipment;
pub mod farms;
pub mod grid;
pub mod layout_content;
pub mod production;
pub mod property;
pub mod readiness;
pub mod rules;
pub mod state;
pub mod trucks;

use std::collections::{BTreeMap, HashMap, HashSet};
use std::sync::Arc;

use serde::{Deserialize, Serialize};

pub use state::*;

use crate::world::roads::{RoadNetwork, RoadPoint};

pub const CARRIED_PREFIX: &str = "carried:";
pub const EMPLOYEE_PREFIX: &str = "employee-";
pub const AUTOMATIC_STARTER: &str = "automatic";
pub const BELT_ITEM_ID: &str = "belt";
pub const LIFT_ITEM_ID: &str = "lift";
pub const BELT_LOCATION_KIND: &str = "belt";
pub const VEHICLE_LOCATION_KIND: &str = "vehicle";
pub const ROAD_SITE_ID: &str = "road";
pub const NON_PERISHABLE_SECONDS: i64 = 1_000_000_000_000;
pub const COUNTER_KIND: &str = "counter";
pub const TABLE_KIND: &str = "table";
pub const DOCK_KIND: &str = "dock";
pub const RESTAURANT_KIND: &str = "restaurant";
pub const FACTORY_KIND: &str = "factory";
pub const FARM_CATEGORY: &str = "farm";
pub const STATION_CATEGORY: &str = "station";
pub const SERVICE_DOOR_ROLE: &str = "service";
pub const FLOOR_STRUCTURE: &str = "floor";
pub const WALL_STRUCTURE: &str = "wall";
pub const PARTITION_STRUCTURE: &str = "partition";
pub const DOOR_STRUCTURE: &str = "door";
pub const WINDOW_STRUCTURE: &str = "window";
pub const BACK_DOOR_STYLE: &str = "kitchen";
pub const MINIMUM_BUILDING_SIZE: i32 = 3;

pub fn inventory_location_id(player: &str) -> String { format!("{CARRIED_PREFIX}{player}") }

/// A dining table is any piece with seats.
pub fn is_table(equipment: &Equipment) -> bool { equipment.seats > 0 }

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct RecipeInput {
    pub item_id: String,
    pub quantity: i32,
}

/// Content, not state: registered by the server at start and never saved.
#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct RecipeDefinition {
    pub id: String,
    pub station_kind: String,
    pub duration_seconds: i64,
    pub inputs: Vec<RecipeInput>,
    pub output_item_id: String,
    pub output_quantity: i32,
    pub output_spoil_after_seconds: i64,
    /// A menu item when above zero: customers buy it at a register.
    pub sale_cents: i64,
    pub tier: i32,
    pub cuisine: String,
}

impl Default for RecipeDefinition {
    fn default() -> Self {
        Self {
            id: String::new(), station_kind: String::new(), duration_seconds: 0, inputs: vec![], output_item_id: String::new(),
            output_quantity: 0, output_spoil_after_seconds: 0, sale_cents: 0, tier: 1, cuisine: String::new(),
        }
    }
}

impl RecipeDefinition {
    pub fn is_sale(&self) -> bool { self.sale_cents > 0 }
}

/// Content: one purchasable pack of an item.
#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct PurchaseOffer {
    pub id: String,
    pub item_id: String,
    pub quantity: i32,
    pub price_cents: i64,
    pub spoil_after_seconds: i64,
}

/// Content: one machine; the template is copied into each bought piece.
#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct EquipmentOffer {
    pub id: String,
    pub price_cents: i64,
    pub equipment: Equipment,
}

/// Content: one truck model at the supplier.
#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct TruckOffer {
    pub id: String,
    pub price_cents: i64,
    pub name: String,
    pub cargo_slots: i32,
    pub speed_metres_per_second: i32,
    pub load_units_per_second: i32,
}

/// Content: what a floor costs and how tall a factory may grow.
#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct FloorOffer {
    pub cents_per_cell: i64,
    pub max_floors: i32,
}

/// Content: one listed lot of the world layout.
#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct PropertyOffer {
    pub lot_id: String,
    pub site_id: String,
    pub building_id: String,
    /// restaurant, factory, farm or station.
    pub category: String,
    pub for_sale: bool,
    pub price_cents: i64,
    pub lot_x: i32,
    pub lot_z: i32,
    pub width: i32,
    pub depth: i32,
    pub access_x: i32,
    pub access_z: i32,
    pub building_x: i32,
    pub building_z: i32,
    pub building_width: i32,
    pub building_depth: i32,
    pub doors: Vec<GridCell>,
    pub back_doors: Vec<GridCell>,
    pub has_dock: bool,
    pub dock_x: i32,
    pub dock_z: i32,
    pub dock_rotation: i32,
    pub floors: i32,
    pub price_percent: i32,
}

impl Default for PropertyOffer {
    fn default() -> Self {
        Self {
            lot_id: String::new(), site_id: String::new(), building_id: String::new(), category: String::new(), for_sale: false,
            price_cents: 0, lot_x: 0, lot_z: 0, width: 0, depth: 0, access_x: 0, access_z: 0, building_x: 0, building_z: 0,
            building_width: 0, building_depth: 0, doors: vec![], back_doors: vec![], has_dock: false, dock_x: 0, dock_z: 0,
            dock_rotation: 0, floors: 1, price_percent: 100,
        }
    }
}

impl PropertyOffer {
    pub fn is_shell(&self) -> bool { self.category == RESTAURANT_KIND || self.category == FACTORY_KIND }
}

/// What a cash change is, for its ledger entry.
#[derive(Clone, Debug, Default)]
pub(crate) struct CashNote {
    pub kind: &'static str,
    pub site_id: String,
    pub request_id: String,
    pub equipment_id: String,
    pub customer_id: String,
    pub offer_id: String,
    pub clock_seconds: Option<i64>,
}

impl CashNote {
    pub fn new(kind: &'static str, site_id: &str) -> Self { Self { kind, site_id: site_id.to_string(), ..Default::default() } }
    pub fn request(mut self, id: &str) -> Self {
        self.request_id = id.to_string();
        self
    }
    pub fn equipment(mut self, id: &str) -> Self {
        self.equipment_id = id.to_string();
        self
    }
    pub fn customer(mut self, id: &str) -> Self {
        self.customer_id = id.to_string();
        self
    }
    pub fn offer(mut self, id: &str) -> Self {
        self.offer_id = id.to_string();
        self
    }
    pub fn at(mut self, seconds: i64) -> Self {
        self.clock_seconds = Some(seconds);
        self
    }
}

/// Server content registered at every start and never saved.
#[derive(Clone, Default)]
pub struct Content {
    pub max_stacks: HashMap<String, i32>,
    /// Keyed by ID, so iteration is in recipe ID order.
    pub recipes: BTreeMap<String, RecipeDefinition>,
    pub manual_power_kinds: HashSet<String>,
    pub worker_kinds: HashSet<String>,
    pub automatic_jobs: bool,
    pub offers: BTreeMap<String, PurchaseOffer>,
    pub equipment_offers: BTreeMap<String, EquipmentOffer>,
    pub truck_offers: BTreeMap<String, TruckOffer>,
    pub floor_offer: Option<FloorOffer>,
    pub property_offers: Option<BTreeMap<String, PropertyOffer>>,
    pub roads: Option<Arc<RoadNetwork>>,
}

/// The world simulation: the saved snapshot plus registered content and derived caches.
pub struct GoodsWorld {
    pub state: Snapshot,
    pub content: Content,
    pub(crate) diners: customers::DinerCache,
    pub(crate) site_points: HashMap<String, Option<RoadPoint>>,
    pub(crate) usable_docks: HashMap<String, bool>,
    pub(crate) truck_now: Option<i64>,
    pub(crate) truck_second: i64,
}

impl Clone for GoodsWorld {
    fn clone(&self) -> Self {
        Self {
            state: self.state.clone(),
            content: self.content.clone(),
            diners: customers::DinerCache::default(),
            site_points: HashMap::new(),
            usable_docks: HashMap::new(),
            truck_now: None,
            truck_second: 0,
        }
    }
}

pub type Result<T> = std::result::Result<T, String>;

fn blank(text: &str) -> bool { text.trim().is_empty() }

impl GoodsWorld {
    pub fn new(world_id: &str) -> Self { Self::restore(Snapshot::new(world_id)) }

    pub fn restore(state: Snapshot) -> Self {
        Self {
            state,
            content: Content::default(),
            diners: customers::DinerCache::default(),
            site_points: HashMap::new(),
            usable_docks: HashMap::new(),
            truck_now: None,
            truck_second: 0,
        }
    }

    /// Replaces the state (a rollback): every derived cache is dropped.
    pub fn replace_state(&mut self, state: Snapshot) {
        self.state = state;
        self.invalidate_diners();
        self.site_points.clear();
    }

    pub(crate) fn new_unique(&mut self, prefix: &str) -> String {
        self.state.next_unique += 1;
        format!("{prefix}{:x}", self.state.next_unique)
    }

    // ------------------------------------------------------------------------------------------------------- content

    pub fn register_item(&mut self, item_id: &str, max_stack: i32) -> Result<()> {
        if blank(item_id) || max_stack < 1 || self.content.max_stacks.contains_key(item_id) {
            return Err(format!("Invalid or duplicate item {item_id}."));
        }
        self.content.max_stacks.insert(item_id.to_string(), max_stack);
        Ok(())
    }

    /// The largest quantity of an item one slot holds; an unregistered item stacks to 1.
    pub fn max_stack(&self, item_id: &str) -> i32 { self.content.max_stacks.get(item_id).copied().unwrap_or(1) }

    pub(crate) fn fits(&self, location_id: &str, item_id: &str, spoiled: bool, quantity: i32) -> bool {
        quantity > 0 && rules::free_units(self.state.location(location_id), &self.state.lots, item_id, spoiled, |i| self.max_stack(i)) >= quantity as i64
    }

    pub(crate) fn fits_all<'a>(&'a self, location_id: &str, incoming: impl IntoIterator<Item = &'a Lot>) -> bool {
        let Some(location) = self.state.location(location_id) else { return false };
        let lots: Vec<&Lot> = self.state.lots.iter().filter(|x| x.location_id == location_id).chain(incoming).collect();
        rules::slots_used(lots, |i| self.max_stack(i)) <= location.capacity
    }

    pub(crate) fn free_units_for(&self, location_id: &str, lot: &Lot) -> i64 {
        rules::free_units(self.state.location(location_id), &self.state.lots, &lot.item_id, lot.spoiled, |i| self.max_stack(i))
    }

    // ------------------------------------------------------------------------------------------------------- bootstrap

    pub fn bootstrap_location(&mut self, location: Location) -> Result<()> {
        if blank(&location.id) || blank(&location.site_id) || blank(&location.kind) || location.capacity < 1 || self.state.location(&location.id).is_some() {
            return Err(format!("Invalid or duplicate location {}.", location.id));
        }
        self.state.locations.push(location);
        self.state.revision += 1;
        Ok(())
    }

    pub fn bootstrap_lot(&mut self, lot: Lot) -> Result<()> {
        if blank(&lot.id) || blank(&lot.item_id) || blank(&lot.owner_id) || lot.quantity < 1 || lot.spoil_after_seconds < 1 || lot.exposure_seconds < 0
            || lot.spoiled != (lot.exposure_seconds >= lot.spoil_after_seconds) || self.state.lot(&lot.id).is_some()
            || !self.fits(&lot.location_id, &lot.item_id, lot.spoiled, lot.quantity)
        {
            return Err(format!("Invalid lot {} or location capacity.", lot.id));
        }
        self.state.lots.push(lot);
        self.state.revision += 1;
        Ok(())
    }

    /// A site exists once it has a map record or any location.
    pub fn site_exists(state: &Snapshot, site_id: &str) -> bool {
        !blank(site_id) && (state.sites.iter().any(|x| x.id == site_id) || state.locations.iter().any(|x| x.site_id == site_id))
    }

    pub fn grant(&mut self, player: &str, site: &str) -> Result<()> {
        if blank(player) || site == ROAD_SITE_ID || !Self::site_exists(&self.state, site) {
            return Err("Invalid grant.".into());
        }
        if !self.state.can_view(player, site) {
            self.state.grants.push(Grant { player_id: player.to_string(), site_id: site.to_string() });
            self.state.revision += 1;
        }
        Ok(())
    }

    /// Server-only admission: ensures the grant and (with inventory_capacity > 0) an inventory of at least that many slots.
    /// Starter goods are added only when the inventory is created. Returns whether anything changed.
    pub fn ensure_grant(&mut self, player: &str, site: &str, inventory_capacity: i32, starter: &[(String, i32, i64)]) -> Result<bool> {
        if blank(player) || site == ROAD_SITE_ID || !Self::site_exists(&self.state, site) {
            return Err("Invalid grant.".into());
        }
        let inventory_id = inventory_location_id(player);
        let existing = self.state.locations.iter().position(|x| x.id == inventory_id);
        let needs_inventory = inventory_capacity > 0 && existing.is_none();
        let needs_slots = existing.is_some_and(|i| self.state.locations[i].capacity < inventory_capacity);
        if self.state.can_view(player, site) && !needs_inventory && !needs_slots {
            return Ok(false);
        }
        if let Some(index) = existing.filter(|_| needs_slots) {
            self.state.locations[index].capacity = inventory_capacity;
            self.state.revision += 1;
        }
        if needs_inventory {
            self.bootstrap_location(Location { id: inventory_id.clone(), site_id: site.to_string(), kind: "carried".into(), capacity: inventory_capacity, refrigerated: false })?;
            for (index, (item, quantity, spoil)) in starter.iter().enumerate() {
                self.bootstrap_lot(Lot {
                    id: format!("starter:{player}:{index}"), item_id: item.clone(), owner_id: site.to_string(), location_id: inventory_id.clone(),
                    quantity: *quantity, spoil_after_seconds: *spoil, ..Default::default()
                })?;
            }
        }
        self.grant(player, site)?;
        Ok(true)
    }

    // ------------------------------------------------------------------------------------------------------- views

    /// The authorized baseline of one site for a player: that site's records, its company and logistics, never other sites'
    /// goods, reservations, outcomes or grants. None without a grant.
    pub fn view(&self, player: &str, site: &str) -> Option<Snapshot> {
        if !self.state.can_view(player, site) {
            return None;
        }
        let s = &self.state;
        let locations: Vec<Location> = s.locations.iter().filter(|x| x.site_id == site).cloned().collect();
        let ids: HashSet<&str> = locations.iter().map(|x| x.id.as_str()).collect();
        let stations: Vec<Station> = s.stations.iter().filter(|x| x.site_id == site).cloned().collect();
        let station_ids: HashSet<&str> = stations.iter().map(|x| x.id.as_str()).collect();
        let companies: Vec<Company> = s.companies.iter().filter(|x| x.site_ids.iter().any(|y| y == site)).cloned().collect();
        let mut view = Snapshot {
            schema_version: s.schema_version,
            world_id: s.world_id.clone(),
            clock_seconds: s.clock_seconds,
            revision: s.revision,
            lots: s.lots.iter().filter(|x| ids.contains(x.location_id.as_str())).cloned().collect(),
            jobs: s.jobs.iter().filter(|x| station_ids.contains(x.station_id.as_str())).cloned().collect(),
            locations,
            stations,
            equipment: s.equipment.iter().filter(|x| x.site_id == site).cloned().collect(),
            site_layouts: s.site_layouts.iter().filter(|x| x.site_id == site).cloned().collect(),
            belts: s.belts.iter().filter(|x| x.site_id == site).cloned().collect(),
            buildings: s.buildings.iter().filter(|x| x.site_id == site).cloned().collect(),
            ledger: s.ledger.iter().filter(|x| companies.iter().any(|c| c.id == x.company_id)).cloned().collect(),
            companies,
            employees: s.employees.iter().filter(|x| x.site_id == site).cloned().map(|mut e| {
                e.script.clear();
                e.tasks.clear();
                e
            }).collect(),
            customers: s.customers.iter().filter(|x| x.restaurant_id == site).cloned().collect(),
            diners: s.diners.iter().filter(|x| x.restaurant_id == site).cloned().collect(),
            properties: s.properties.clone(),
            next_customer_number: s.next_customer_number,
            next_piece_number: s.next_piece_number,
            wages_paid_hour: s.wages_paid_hour,
            next_employee_number: s.next_employee_number,
            ..Default::default()
        };
        view.company_wage_cents_per_hour = employees::company_wages(&self.state, site);
        self.view_logistics(&mut view, site);
        self.view_farms(&mut view, site);
        Some(view)
    }

    pub fn has_lots_at(&self, location_id: &str) -> bool { self.state.lots.iter().any(|x| x.location_id == location_id) }

    // ------------------------------------------------------------------------------------------------------- outcomes

    pub(crate) fn replay(&self, player: &str, id: &str) -> Option<Outcome> {
        if blank(id) {
            return None;
        }
        self.state.outcomes.iter().find(|x| x.player_id == player && x.request_id == id).cloned()
    }

    /// Records a terminal outcome (accepted or not) so the request replays.
    pub(crate) fn record(&mut self, id: &str, player: &str, accepted: bool, reason: &str, moved: &str) -> Outcome {
        self.state.revision += 1;
        let outcome = Outcome {
            request_id: id.to_string(), player_id: player.to_string(), accepted, reason: reason.to_string(), moved_lot_id: moved.to_string(),
            revision: self.state.revision, ..Default::default()
        };
        self.state.outcomes.push(outcome.clone());
        outcome
    }

    /// An accepted outcome that also records the cents it moved and the entity it created.
    pub(crate) fn record_with(&mut self, id: &str, player: &str, reason: &str, cents: i64, entity: &str, job: &str) -> Outcome {
        let mut result = self.record(id, player, true, reason, "");
        result.cents = cents;
        result.equipment_id = entity.to_string();
        result.job_id = job.to_string();
        if let Some(stored) = self.state.outcomes.last_mut() {
            stored.cents = cents;
            stored.equipment_id = entity.to_string();
            stored.job_id = job.to_string();
        }
        result
    }

    /// A rejection that is answered but not recorded: it changes nothing and is evaluated afresh on retry.
    pub(crate) fn reject(&self, id: &str, player: &str, reason: &str) -> Outcome {
        Outcome { request_id: id.to_string(), player_id: player.to_string(), accepted: false, reason: reason.to_string(), revision: self.state.revision, ..Default::default() }
    }

    /// Identity check and replay shared by every command: Some(outcome) ends the command.
    pub(crate) fn begin(&self, player: &str, request: &str) -> Option<Outcome> {
        if blank(player) || blank(request) {
            return Some(Outcome { reason: "invalid-identity".into(), ..Default::default() });
        }
        self.replay(player, request)
    }

    pub(crate) fn available(&self, lot: &Lot) -> i32 {
        lot.quantity - self.state.reservations.iter().filter(|x| x.active && x.lot_id == lot.id).map(|x| x.quantity).sum::<i32>()
    }

    pub(crate) fn reserved(&self, lot_id: &str) -> bool { self.state.reservations.iter().any(|x| x.active && x.lot_id == lot_id) }

    fn allowed(&self, player: &str, location_id: &str) -> bool {
        self.state.location(location_id).is_some_and(|l| self.state.can_view(player, &l.site_id))
    }

    pub(crate) fn is_belt_location(&self, location_id: &str) -> bool { self.state.location(location_id).is_some_and(|x| x.kind == BELT_LOCATION_KIND) }

    // ------------------------------------------------------------------------------------------------------- clock

    /// Advances the world clock by whole seconds: exposure, jobs, belts, trucks, customers, wages and farm schedules.
    pub fn advance(&mut self, seconds: i64) -> Result<()> {
        if seconds < 0 {
            return Err("negative step".into());
        }
        if seconds == 0 {
            return Ok(());
        }
        self.state.clock_seconds = self.state.clock_seconds.checked_add(seconds).ok_or("clock overflow")?;
        self.emit_blocked_outputs();
        let cold: HashMap<&str, bool> = self.state.locations.iter().map(|l| (l.id.as_str(), l.refrigerated)).collect();
        let mut exposures = Vec::with_capacity(self.state.lots.len());
        for lot in &self.state.lots {
            let Some(&refrigerated) = cold.get(lot.location_id.as_str()) else {
                return Err(format!("Lot {} is in unknown location {}.", lot.id, lot.location_id));
            };
            exposures.push(!(lot.spoiled || refrigerated));
        }
        for (lot, ages) in self.state.lots.iter_mut().zip(exposures) {
            if ages {
                lot.exposure_seconds += seconds.min(lot.spoil_after_seconds - lot.exposure_seconds);
                lot.spoiled = lot.exposure_seconds >= lot.spoil_after_seconds;
            }
        }
        self.progress_jobs(seconds);
        self.start_ready_jobs();
        self.move_belt_items(seconds);
        self.move_trucks(seconds);
        self.advance_customers(seconds);
        self.pay_wages();
        self.run_farm_schedules();
        self.state.revision += 1;
        Ok(())
    }

    // ------------------------------------------------------------------------------------------------------- goods

    /// Reserves units of a lot for a player under a server-made ID.
    pub fn reserve(&mut self, player: &str, request: &str, lot_id: &str, quantity: i32) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(lot) = self.state.lot(lot_id).cloned() else { return self.record(request, player, false, "forbidden", "") };
        if !self.allowed(player, &lot.location_id) {
            return self.record(request, player, false, "forbidden", "");
        }
        if quantity <= 0 || quantity > self.available(&lot) {
            return self.record(request, player, false, "quantity-or-reservation", "");
        }
        let id = self.new_unique("reservation-");
        self.state.reservations.push(Reservation { id: id.clone(), lot_id: lot_id.to_string(), player_id: player.to_string(), quantity, active: true });
        let mut result = self.record(request, player, true, "reserved", "");
        result.reservation_id = id.clone();
        if let Some(last) = self.state.outcomes.last_mut() {
            last.reservation_id = id;
        }
        result
    }

    pub fn cancel(&mut self, player: &str, request: &str, reservation_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(reservation) = self.state.reservations.iter_mut().find(|x| x.id == reservation_id && x.player_id == player && x.active) else {
            return self.record(request, player, false, "reservation-unavailable", "");
        };
        reservation.active = false;
        self.record(request, player, true, "cancelled", "")
    }

    /// Moves units of a lot to another location on the same site: grant, route, unreserved quantity (or the player's
    /// reservation) and destination slots are checked before the single mutation; a partial move splits under a new ID.
    pub fn transfer(&mut self, player: &str, request: &str, lot_id: &str, destination_id: &str, quantity: i32, reservation_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(lot) = self.state.lot(lot_id).cloned() else { return self.record(request, player, false, "forbidden", "") };
        let Some(source) = self.state.location(&lot.location_id).cloned() else { return self.record(request, player, false, "forbidden", "") };
        if !self.allowed(player, &source.id) || lot.owner_id != source.site_id {
            return self.record(request, player, false, "forbidden", "");
        }
        let destination = self.state.location(destination_id).cloned();
        let Some(destination) = destination.filter(|d| {
            lot.location_id != d.id && source.kind != BELT_LOCATION_KIND && d.kind != BELT_LOCATION_KIND && source.kind != VEHICLE_LOCATION_KIND && d.kind != VEHICLE_LOCATION_KIND
        }) else {
            return self.record(request, player, false, "invalid-route", "");
        };
        if source.site_id != destination.site_id || !self.allowed(player, &destination.id) {
            return self.record(request, player, false, "forbidden", "");
        }
        let reservation = self.state.reservations.iter().position(|x| x.id == reservation_id && x.active);
        let available = self.available(&lot);
        let bad = quantity <= 0
            || (!reservation_id.is_empty() && reservation.is_none())
            || match reservation {
                None => quantity > available,
                Some(index) => {
                    let r = &self.state.reservations[index];
                    r.lot_id != lot.id || r.player_id != player || r.quantity != quantity || quantity > available + r.quantity
                }
            };
        if bad {
            return self.record(request, player, false, "quantity-or-reservation", "");
        }
        if !self.fits(&destination.id, &lot.item_id, lot.spoiled, quantity) {
            return self.record(request, player, false, "capacity", "");
        }
        let moved = self.move_lot_part(lot_id, &destination.id, quantity, None);
        if let Some(index) = reservation {
            self.state.reservations[index].active = false;
        }
        self.start_ready_jobs();
        self.record(request, player, true, "transferred", &moved)
    }

    /// Moves `quantity` of a lot to a location (re-owned when owner is given), keeping its ID when all of it moves and
    /// splitting under a new ID otherwise. Returns the moved lot's ID.
    pub(crate) fn move_lot_part(&mut self, lot_id: &str, location_id: &str, quantity: i32, owner: Option<&str>) -> String {
        let index = self.state.lots.iter().position(|x| x.id == lot_id).expect("moved lot exists");
        if quantity == self.state.lots[index].quantity {
            let lot = &mut self.state.lots[index];
            lot.location_id = location_id.to_string();
            if let Some(owner) = owner {
                lot.owner_id = owner.to_string();
            }
            return lot.id.clone();
        }
        let id = self.new_unique("lot-");
        let lot = &mut self.state.lots[index];
        lot.quantity -= quantity;
        let mut part = lot.clone();
        part.id = id.clone();
        part.location_id = location_id.to_string();
        part.quantity = quantity;
        part.belt_position = 0;
        if let Some(owner) = owner {
            part.owner_id = owner.to_string();
        }
        self.state.lots.push(part);
        id
    }

    /// Merges two equivalent, unreserved lots in one location.
    pub fn merge(&mut self, first: &str, second: &str) -> bool {
        let (Some(a), Some(b)) = (self.state.lots.iter().position(|x| x.id == first), self.state.lots.iter().position(|x| x.id == second)) else { return false };
        let (la, lb) = (&self.state.lots[a], &self.state.lots[b]);
        if a == b || la.item_id != lb.item_id || la.owner_id != lb.owner_id || la.location_id != lb.location_id || la.exposure_seconds != lb.exposure_seconds
            || la.spoil_after_seconds != lb.spoil_after_seconds || la.spoiled != lb.spoiled || self.is_belt_location(&la.location_id)
            || self.reserved(first) || self.reserved(second)
        {
            return false;
        }
        let add = lb.quantity;
        self.state.lots[a].quantity += add;
        self.state.lots.remove(b);
        self.state.revision += 1;
        true
    }

    /// Light consistency checks run before every save: identity, references and capacity-independent invariants.
    pub fn validate(&self) -> Result<()> {
        let s = &self.state;
        let mut ids = HashSet::new();
        for location in &s.locations {
            if blank(&location.id) || location.capacity < 1 || !ids.insert(location.id.as_str()) {
                return Err(format!("invalid or duplicate location {}", location.id));
            }
        }
        let mut lots = HashSet::new();
        for lot in &s.lots {
            if blank(&lot.id) || lot.quantity < 1 || lot.spoiled != (lot.exposure_seconds >= lot.spoil_after_seconds) || !ids.contains(lot.location_id.as_str()) || !lots.insert(lot.id.as_str()) {
                return Err(format!("invalid lot {}", lot.id));
            }
        }
        let mut companies = HashSet::new();
        for company in &s.companies {
            if company.cash < 0 || !companies.insert(company.id.as_str()) {
                return Err(format!("invalid company {}", company.id));
            }
            if !company::ledger_balances(s, company) {
                return Err(format!("ledger of {} does not balance", company.id));
            }
        }
        let mut equipment = HashSet::new();
        for piece in &s.equipment {
            if !equipment.insert(piece.id.as_str()) {
                return Err(format!("duplicate equipment {}", piece.id));
            }
            if piece.placed() != s.stations.iter().any(|x| x.id == piece.id) {
                return Err(format!("equipment {} station mismatch", piece.id));
            }
        }
        for truck in &s.trucks {
            if !ids.contains(truck.cargo_location_id().as_str()) {
                return Err(format!("truck {} has no cargo location", truck.id));
            }
        }
        Ok(())
    }
}
