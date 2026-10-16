// Equipment and restaurant furnishing (Unity GoodsWorld.Equipment.cs and .Furnishing.cs). Equipment is a physical, stable-ID
// machine that is either placed on a site grid or held by one actor. A placed piece owns one station and two buffer locations
// (<id>:in, <id>:out); pickup empties and removes them and placement recreates them under the same IDs. Build mode buys and
// places in one order (all or nothing, each piece recording its price), and anything can be sold back for exactly what it was
// charged, after its goods move into the seller's inventory. A register sells only while staffed.
use serde::{Deserialize, Serialize};

use super::grid;
use super::rules;
use super::state::*;
use super::{inventory_location_id, is_table, CashNote, GoodsWorld, PropertyOffer, Result, COUNTER_KIND, EMPLOYEE_PREFIX, TABLE_KIND};
use super::company::{LEDGER_FURNISH, LEDGER_SELL_BACK, LEDGER_SUPPLIER_EQUIPMENT, LEDGER_SUPPLIER_GOODS, LEDGER_TRUCK};

pub const MAX_FURNISH_PLACEMENTS: usize = 200;

#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
#[serde(default)]
pub struct GridPlacement {
    pub x: i32,
    pub z: i32,
    pub rotation: i32,
}

/// Buy one equipment offer per placement, placed at once (build mode).
#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct FurnishOrder {
    pub site_id: String,
    pub offer_id: String,
    pub level: i32,
    pub placements: Vec<GridPlacement>,
}

/// Pure: the first problem of placing one piece of the template at each placement, in order, each seeing the ones before.
pub fn furnish_problem(state: &Snapshot, site_id: &str, template: &Equipment, placements: &[GridPlacement], level: i32, offer: Option<&PropertyOffer>) -> Option<&'static str> {
    if placements.is_empty() || placements.len() > MAX_FURNISH_PLACEMENTS {
        return Some("invalid-placement");
    }
    let mut probe = Snapshot {
        world_id: state.world_id.clone(),
        site_layouts: state.site_layouts.clone(),
        belts: state.belts.clone(),
        buildings: state.buildings.clone(),
        equipment: state.equipment.clone(),
        ..Default::default()
    };
    for (index, placement) in placements.iter().enumerate() {
        let mut piece = template.clone();
        piece.id = format!("\u{0}preview:{index}");
        piece.site_id = site_id.to_string();
        piece.state = EquipmentState::Placed;
        piece.holder_id.clear();
        piece.cell_x = placement.x;
        piece.cell_z = placement.z;
        piece.rotation = placement.rotation;
        piece.level = level;
        let problem = grid::placement_problem(&probe, &piece, placement.x, placement.z, placement.rotation, level)
            .or_else(|| rules::placement_problem(&probe, &piece, placement.x, placement.z, placement.rotation, offer));
        if problem.is_some() {
            return problem;
        }
        probe.equipment.push(piece);
    }
    None
}

impl GoodsWorld {
    /// A short, never reused ID for new equipment, "<kind>-<n>".
    pub(crate) fn new_equipment_id(&mut self, kind: &str) -> String {
        loop {
            self.state.next_piece_number += 1;
            let id = format!("{kind}-{}", self.state.next_piece_number);
            if !self.state.equipment.iter().any(|x| x.id == id) && !self.state.stations.iter().any(|x| x.id == id) && !self.state.trucks.iter().any(|x| x.id == id) {
                return id;
            }
        }
    }

    pub fn bootstrap_layout(&mut self, layout: SiteLayout) -> Result<()> {
        if layout.site_id.trim().is_empty() || layout.width < 1 || layout.depth < 1 || self.state.layout_of(&layout.site_id).is_some() {
            return Err(format!("Invalid or duplicate site layout {}.", layout.site_id));
        }
        self.state.site_layouts.push(layout);
        self.state.revision += 1;
        Ok(())
    }

    /// Server-only: creates an already-placed piece with its station and buffers.
    pub fn bootstrap_equipment(&mut self, mut equipment: Equipment) -> Result<()> {
        if equipment.id.trim().is_empty() || equipment.kind.trim().is_empty() || equipment.state != EquipmentState::Placed || !equipment.holder_id.is_empty()
            || equipment.width < 1 || equipment.depth < 1 || equipment.input_capacity < 1 || equipment.output_capacity < 1 || equipment.seats < 0
            || (equipment.kind == TABLE_KIND && equipment.seats < 1) || !grid::is_layer(&equipment.layer) || equipment.ambience < 0 || equipment.charged_cents < 0
            || self.state.equipment.iter().any(|x| x.id == equipment.id) || self.state.stations.iter().any(|x| x.id == equipment.id)
            || self.state.location(&equipment.input_location_id()).is_some() || self.state.location(&equipment.output_location_id()).is_some()
            || grid::placement_problem(&self.state, &equipment, equipment.cell_x, equipment.cell_z, equipment.rotation, equipment.level).is_some()
        {
            return Err(format!("Invalid, duplicate or unplaceable equipment {}.", equipment.id));
        }
        equipment.staff_id.clear();
        self.add_placed_parts(&equipment);
        self.state.equipment.push(equipment);
        self.invalidate_diners();
        self.state.revision += 1;
        Ok(())
    }

    /// Server start step: pieces of a kind take the content's current buffer slot counts. Returns whether anything changed.
    pub fn apply_equipment_capacities(&mut self, kind: &str, input: i32, output: i32) -> bool {
        let mut changed = false;
        for index in 0..self.state.equipment.len() {
            let piece = &mut self.state.equipment[index];
            if piece.kind != kind || (piece.input_capacity == input && piece.output_capacity == output) {
                continue;
            }
            piece.input_capacity = input;
            piece.output_capacity = output;
            let (input_id, output_id) = (piece.input_location_id(), piece.output_location_id());
            for location in &mut self.state.locations {
                if location.id == input_id {
                    location.capacity = input;
                } else if location.id == output_id {
                    location.capacity = output;
                }
            }
            changed = true;
        }
        if changed {
            self.state.revision += 1;
        }
        changed
    }

    pub(crate) fn add_placed_parts(&mut self, equipment: &Equipment) {
        self.state.locations.push(Location {
            id: equipment.input_location_id(), site_id: equipment.site_id.clone(), kind: "machine-buffer".into(), capacity: equipment.input_capacity,
            refrigerated: equipment.input_refrigerated,
        });
        self.state.locations.push(Location {
            id: equipment.output_location_id(), site_id: equipment.site_id.clone(), kind: "machine-buffer".into(), capacity: equipment.output_capacity,
            refrigerated: equipment.output_refrigerated,
        });
        self.state.stations.push(Station {
            id: equipment.id.clone(), site_id: equipment.site_id.clone(), kind: equipment.kind.clone(),
            input_location_id: equipment.input_location_id(), output_location_id: equipment.output_location_id(),
        });
    }

    fn equipment_index(&self, id: &str) -> Option<usize> { self.state.equipment.iter().position(|x| x.id == id) }

    /// True while tabletop decor stands on this placed table.
    pub(crate) fn holds_tabletop(&self, table: &Equipment) -> bool {
        if !is_table(table) || !table.layer.is_empty() {
            return false;
        }
        let (tw, td) = table.footprint();
        self.state.equipment.iter().any(|x| x.placed() && x.site_id == table.site_id && x.layer == grid::TABLETOP_LAYER && grid::overlaps(x.cell_x, x.cell_z, x.width, x.depth, table.cell_x, table.cell_z, tw, td))
    }

    /// The goods a placed piece hands back when it leaves the grid: its buffered lots and its job's refund or blocked output.
    fn returned_goods(&self, piece: &Equipment, inventory_id: &str) -> (Vec<String>, Vec<Lot>, Option<String>) {
        let buffered: Vec<String> = self.state.lots.iter().filter(|x| x.location_id == piece.input_location_id() || x.location_id == piece.output_location_id()).map(|x| x.id.clone()).collect();
        let job = self.state.jobs.iter().find(|x| x.station_id == piece.id);
        let returned = match job {
            None => vec![],
            Some(job) if job.state == JobState::Blocked => {
                let station = self.state.stations.iter().find(|x| x.id == piece.id).expect("placed piece has a station");
                vec![super::production::output_lot(job, station, inventory_id, 0)]
            }
            Some(job) => job.inputs.clone(),
        };
        (buffered, returned, job.map(|j| j.id.clone()))
    }

    /// Removes a placed piece's station, job and buffers after its goods moved to `inventory_id`.
    fn sweep_piece(&mut self, piece: &Equipment, inventory_id: &str, buffered: &[String], returned: Vec<Lot>) {
        for lot in &mut self.state.lots {
            if buffered.contains(&lot.id) {
                lot.location_id = inventory_id.to_string();
            }
        }
        for mut lot in returned {
            lot.location_id = inventory_id.to_string();
            self.state.lots.push(lot);
        }
        self.state.jobs.retain(|x| x.station_id != piece.id);
        self.state.stations.retain(|x| x.id != piece.id);
        let (input, output) = (piece.input_location_id(), piece.output_location_id());
        self.state.locations.retain(|x| x.id != input && x.id != output);
    }

    /// Picks a placed piece into the actor's holding; its buffers and job refund go to the actor's inventory, all or nothing.
    pub fn pick_up(&mut self, player: &str, request: &str, equipment_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(index) = self.equipment_index(equipment_id) else { return self.record(request, player, false, "forbidden", "") };
        let piece = self.state.equipment[index].clone();
        if !self.state.can_view(player, &piece.site_id) {
            return self.record(request, player, false, "forbidden", "");
        }
        if !piece.placed() {
            return self.record(request, player, false, "not-placed", "");
        }
        if self.state.customers.iter().any(|x| x.counter_id == piece.id || x.table_id == piece.id) {
            return self.record(request, player, false, "occupied", "");
        }
        if self.holds_tabletop(&piece) {
            return self.record(request, player, false, "blocked", "");
        }
        let inventory_id = inventory_location_id(player);
        if self.state.location(&inventory_id).map(|l| l.site_id.as_str()) != Some(piece.site_id.as_str()) {
            return self.record(request, player, false, "no-inventory", "");
        }
        let (buffered, returned, job) = self.returned_goods(&piece, &inventory_id);
        if buffered.iter().any(|id| self.reserved(id)) {
            return self.record(request, player, false, "reserved", "");
        }
        let incoming: Vec<Lot> = self.state.lots.iter().filter(|x| buffered.contains(&x.id)).cloned().chain(returned.iter().cloned()).collect();
        if !self.fits_all(&inventory_id, incoming.iter()) {
            return self.record(request, player, false, "capacity", "");
        }
        self.sweep_piece(&piece, &inventory_id, &buffered, returned);
        let held = &mut self.state.equipment[index];
        held.state = EquipmentState::Held;
        held.holder_id = player.to_string();
        held.staff_id.clear();
        held.powered_on = false;
        held.held_off = false;
        held.cell_x = 0;
        held.cell_z = 0;
        held.rotation = 0;
        held.level = 0;
        self.invalidate_diners();
        let job = job.unwrap_or_default();
        let mut result = self.record(request, player, true, "picked-up", "");
        result.job_id = job.clone();
        if let Some(last) = self.state.outcomes.last_mut() {
            last.job_id = job;
        }
        result
    }

    /// Places a piece the actor holds. Player position is not checked (decision 0005).
    pub fn place(&mut self, player: &str, request: &str, equipment_id: &str, x: i32, z: i32, rotation: i32, level: i32) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(index) = self.equipment_index(equipment_id) else { return self.record(request, player, false, "forbidden", "") };
        let piece = self.state.equipment[index].clone();
        if !self.state.can_view(player, &piece.site_id) {
            return self.record(request, player, false, "forbidden", "");
        }
        if piece.state != EquipmentState::Held || piece.holder_id != player {
            return self.record(request, player, false, "not-held", "");
        }
        let offer = self.offer_of_site(&piece.site_id);
        if let Some(problem) = grid::placement_problem(&self.state, &piece, x, z, rotation, level).or_else(|| rules::placement_problem(&self.state, &piece, x, z, rotation, offer.as_ref())) {
            return self.record(request, player, false, problem, "");
        }
        let placed = &mut self.state.equipment[index];
        placed.state = EquipmentState::Placed;
        placed.holder_id.clear();
        placed.cell_x = x;
        placed.cell_z = z;
        placed.rotation = rotation;
        placed.level = level;
        let placed = placed.clone();
        self.add_placed_parts(&placed);
        self.invalidate_diners();
        self.record(request, player, true, "placed", "")
    }

    /// Hands a held piece to an employee on its site, keeping its ID.
    pub fn give(&mut self, player: &str, request: &str, equipment_id: &str, employee_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(index) = self.equipment_index(equipment_id) else { return self.record(request, player, false, "forbidden", "") };
        let piece = self.state.equipment[index].clone();
        if !self.state.can_view(player, &piece.site_id) {
            return self.record(request, player, false, "forbidden", "");
        }
        if piece.state != EquipmentState::Held || piece.holder_id != player {
            return self.record(request, player, false, "not-held", "");
        }
        if employee_id == player || !self.state.employees.iter().any(|x| x.id == employee_id && x.site_id == piece.site_id) {
            return self.record(request, player, false, "unknown-employee", "");
        }
        self.state.equipment[index].holder_id = employee_id.to_string();
        self.record(request, player, true, "given", "")
    }

    /// The listed lot of a site, or None for sites without one.
    pub fn offer_of_site(&self, site_id: &str) -> Option<PropertyOffer> {
        self.content.property_offers.as_ref()?.values().find(|x| x.site_id == site_id).cloned()
    }

    // ------------------------------------------------------------------------------------------------------- supplier

    /// Spends the site company's cash on a goods, machine or truck offer; delivery is immediate.
    pub fn buy(&mut self, player: &str, request: &str, site_id: &str, offer_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        if !self.state.can_view(player, site_id) {
            return self.reject(request, player, "forbidden");
        }
        let goods = self.content.offers.get(offer_id).cloned();
        let machine = self.content.equipment_offers.get(offer_id).cloned();
        let vehicle = self.content.truck_offers.get(offer_id).cloned();
        if goods.is_none() && machine.is_none() && vehicle.is_none() {
            return self.reject(request, player, "invalid-offer");
        }
        let Some(company) = self.state.company_of_site(site_id).map(|c| c.id.clone()) else { return self.reject(request, player, "no-company") };
        let inventory_id = inventory_location_id(player);
        let has_inventory = self.state.location(&inventory_id).is_some_and(|l| l.site_id == site_id);
        if vehicle.is_none() && !has_inventory {
            return self.reject(request, player, "no-inventory");
        }
        if vehicle.is_some() && self.state.site(site_id).is_none() {
            return self.reject(request, player, "no-road");
        }
        if machine.is_some() && self.state.layout_of(site_id).is_none() {
            return self.reject(request, player, "no-layout");
        }
        if let Some(offer) = &goods {
            if !self.fits(&inventory_id, &offer.item_id, false, offer.quantity) {
                return self.reject(request, player, "capacity");
            }
        }
        let price = goods.as_ref().map(|x| x.price_cents).or(machine.as_ref().map(|x| x.price_cents)).or(vehicle.as_ref().map(|x| x.price_cents)).unwrap_or(0);
        if self.cash_of(&company) < price {
            return self.reject(request, player, "insufficient-funds");
        }
        let delivered = format!("buy:{player}:{request}");
        if let Some(vehicle) = vehicle {
            self.try_debit(&company, vehicle.price_cents, CashNote::new(LEDGER_TRUCK, site_id).request(request).equipment(&delivered).offer(offer_id));
            let number = self.state.trucks.iter().filter(|x| x.company_id == company && !x.supplier).count() + 1;
            self.add_truck(Truck {
                id: delivered.clone(), company_id: company, name: format!("{} {number}", vehicle.name), cargo_slots: vehicle.cargo_slots,
                speed_metres_per_second: vehicle.speed_metres_per_second, load_units_per_second: vehicle.load_units_per_second,
                state: TruckState::Parked, site_id: site_id.to_string(), ..Default::default()
            });
            return self.record_with(request, player, "bought", 0, &delivered, "");
        }
        if let Some(machine) = machine {
            let mut piece = machine.equipment.clone();
            piece.id = self.new_equipment_id(&piece.kind);
            self.try_debit(&company, machine.price_cents, CashNote::new(LEDGER_SUPPLIER_EQUIPMENT, site_id).request(request).equipment(&piece.id).offer(offer_id));
            piece.site_id = site_id.to_string();
            piece.state = EquipmentState::Held;
            piece.holder_id = player.to_string();
            piece.cell_x = 0;
            piece.cell_z = 0;
            piece.rotation = 0;
            piece.level = 0;
            piece.charged_cents = machine.price_cents;
            piece.staff_id.clear();
            let id = piece.id.clone();
            self.state.equipment.push(piece);
            return self.record_with(request, player, "bought", 0, &id, "");
        }
        let offer = goods.expect("one offer kind matched");
        self.try_debit(&company, offer.price_cents, CashNote::new(LEDGER_SUPPLIER_GOODS, site_id).request(request).offer(offer_id));
        self.state.lots.push(Lot {
            id: delivered.clone(), item_id: offer.item_id.clone(), owner_id: site_id.to_string(), location_id: inventory_id, quantity: offer.quantity,
            spoil_after_seconds: offer.spoil_after_seconds, ..Default::default()
        });
        self.record(request, player, true, "bought", &delivered)
    }

    // ------------------------------------------------------------------------------------------------------- build mode

    pub fn buy_and_place(&mut self, player: &str, request: &str, order: &FurnishOrder) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let site_id = order.site_id.clone();
        if site_id.is_empty() || !self.state.can_view(player, &site_id) {
            return self.reject(request, player, "forbidden");
        }
        let Some(offer) = self.content.equipment_offers.get(&order.offer_id).cloned() else { return self.reject(request, player, "invalid-offer") };
        let Some(company) = self.state.company_of_site(&site_id).map(|c| c.id.clone()) else { return self.reject(request, player, "no-company") };
        if self.state.layout_of(&site_id).is_none() {
            return self.reject(request, player, "no-layout");
        }
        let lot = self.offer_of_site(&site_id);
        if let Some(problem) = furnish_problem(&self.state, &site_id, &offer.equipment, &order.placements, order.level, lot.as_ref()) {
            return self.reject(request, player, problem);
        }
        let Some(price) = offer.price_cents.checked_mul(order.placements.len() as i64) else { return self.reject(request, player, "insufficient-funds") };
        if self.cash_of(&company) < price {
            return self.reject(request, player, "insufficient-funds");
        }
        let mut first = String::new();
        for placement in &order.placements {
            let mut piece = offer.equipment.clone();
            piece.id = self.new_equipment_id(&piece.kind);
            piece.site_id = site_id.clone();
            piece.state = EquipmentState::Placed;
            piece.holder_id.clear();
            piece.staff_id.clear();
            piece.cell_x = placement.x;
            piece.cell_z = placement.z;
            piece.rotation = placement.rotation;
            piece.level = order.level;
            piece.charged_cents = offer.price_cents;
            self.add_placed_parts(&piece);
            if first.is_empty() {
                first = piece.id.clone();
            }
            self.state.equipment.push(piece);
        }
        self.try_debit(&company, price, CashNote::new(LEDGER_FURNISH, &site_id).request(request).equipment(&first).offer(&order.offer_id));
        self.invalidate_diners();
        self.record_with(request, player, "bought", price, &first, "")
    }

    /// Sells a piece back for exactly what it was charged, after its goods move to the seller's inventory.
    pub fn sell(&mut self, player: &str, request: &str, equipment_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(index) = self.equipment_index(equipment_id) else { return self.reject(request, player, "forbidden") };
        let piece = self.state.equipment[index].clone();
        if !self.state.can_view(player, &piece.site_id) {
            return self.reject(request, player, "forbidden");
        }
        let company = self.state.company_of_site(&piece.site_id).map(|c| c.id.clone());
        if company.is_none() && piece.charged_cents > 0 {
            return self.reject(request, player, "no-company");
        }
        if piece.state == EquipmentState::Held && piece.holder_id != player {
            return self.reject(request, player, "not-held");
        }
        if self.state.routes.iter().any(|x| x.pickup_dock_id == piece.id || x.dropoff_dock_id == piece.id) {
            return self.reject(request, player, "in-route");
        }
        let inventory_id = inventory_location_id(player);
        let mut job_id = String::new();
        if piece.placed() {
            if self.state.customers.iter().any(|x| x.counter_id == piece.id || x.table_id == piece.id) {
                return self.reject(request, player, "occupied");
            }
            if self.holds_tabletop(&piece) {
                return self.reject(request, player, "blocked");
            }
            let (buffered, returned, job) = self.returned_goods(&piece, &inventory_id);
            if buffered.iter().any(|id| self.reserved(id)) {
                return self.reject(request, player, "reserved");
            }
            if !buffered.is_empty() || job.is_some() {
                if self.state.location(&inventory_id).map(|l| l.site_id.as_str()) != Some(piece.site_id.as_str()) {
                    return self.reject(request, player, "no-inventory");
                }
                let incoming: Vec<Lot> = self.state.lots.iter().filter(|x| buffered.contains(&x.id)).cloned().chain(returned.iter().cloned()).collect();
                if !self.fits_all(&inventory_id, incoming.iter()) {
                    return self.reject(request, player, "capacity");
                }
            }
            job_id = job.unwrap_or_default();
            self.sweep_piece(&piece, &inventory_id, &buffered, returned);
        }
        self.state.equipment.retain(|x| x.id != piece.id);
        if piece.charged_cents > 0 {
            if let Some(company) = &company {
                self.try_credit(company, piece.charged_cents, CashNote::new(LEDGER_SELL_BACK, &piece.site_id).request(request).equipment(&piece.id));
            }
        }
        self.invalidate_diners();
        self.record_with(request, player, "sold", -piece.charged_cents, &piece.id, &job_id)
    }

    /// Sets who works a register or worker-bound station: the requester (on its site), an employee of the site, or nobody.
    pub fn staff(&mut self, player: &str, request: &str, register_id: &str, staff_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(index) = self.equipment_index(register_id) else { return self.reject(request, player, "forbidden") };
        let register = self.state.equipment[index].clone();
        if !self.state.can_view(player, &register.site_id) {
            return self.reject(request, player, "forbidden");
        }
        if register.kind != COUNTER_KIND && !self.content.worker_kinds.contains(&register.kind) {
            return self.reject(request, player, "not-a-register");
        }
        if !register.placed() {
            return self.reject(request, player, "not-placed");
        }
        if staff_id == player {
            if self.state.location(&inventory_location_id(player)).map(|l| l.site_id.as_str()) != Some(register.site_id.as_str()) {
                return self.reject(request, player, "not-on-site");
            }
        } else if staff_id.starts_with(EMPLOYEE_PREFIX) {
            if !self.state.employees.iter().any(|x| x.id == staff_id && x.site_id == register.site_id) {
                return self.reject(request, player, "unknown-employee");
            }
        } else if !staff_id.is_empty() {
            return self.reject(request, player, "forbidden");
        }
        if register.staff_id == staff_id {
            return self.reject(request, player, "unchanged");
        }
        if !staff_id.is_empty() {
            self.release_staff(staff_id);
        }
        self.state.equipment[index].staff_id = staff_id.to_string();
        self.invalidate_diners();
        if !staff_id.is_empty() && self.content.automatic_jobs && self.content.worker_kinds.contains(&register.kind) {
            self.start_ready_job(&register.id);
        }
        self.record(request, player, true, if staff_id.is_empty() { "unstaffed" } else { "staffed" }, "")
    }

    /// Clears the actor's register, if any; true when one was cleared. The caller counts the revision.
    pub(crate) fn release_staff(&mut self, actor: &str) -> bool {
        let mut changed = false;
        for piece in &mut self.state.equipment {
            if !actor.is_empty() && piece.staff_id == actor {
                piece.staff_id.clear();
                changed = true;
            }
        }
        changed
    }

    /// Server-only: a player who leaves stops working their register; None clears every register a player (not an employee)
    /// works, as on a server start. True when something changed.
    pub fn release_staff_of(&mut self, actor: Option<&str>) -> bool {
        let mut changed = false;
        for piece in &mut self.state.equipment {
            if piece.staff_id.is_empty() {
                continue;
            }
            let matches = match actor {
                None => !piece.staff_id.starts_with(EMPLOYEE_PREFIX),
                Some(actor) => piece.staff_id == actor,
            };
            if matches {
                piece.staff_id.clear();
                changed = true;
            }
        }
        if changed {
            self.invalidate_diners();
            self.state.revision += 1;
        }
        changed
    }

    /// The placed register the actor staffs and its site's grid size, so an idle employee can keep standing at its post.
    pub fn staffed_register(&self, actor: &str) -> Option<(Equipment, SiteLayout)> {
        if actor.is_empty() {
            return None;
        }
        let register = self.state.equipment.iter().find(|x| x.staff_id == actor && x.kind == COUNTER_KIND && x.placed())?;
        let layout = self.state.layout_of(&register.site_id)?;
        Some((register.clone(), layout.clone()))
    }
}
