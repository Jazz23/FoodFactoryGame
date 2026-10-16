// Farm orders (Unity GoodsWorld.Farms.cs, decision 0047). Every listed farm lot has a farm type deciding which raw ingredients
// it sells; a player orders packs to a placed dock of a site their company owns and the company pays at once. Delivery: a
// supplier truck belonging to the order starts at the farm's gate with the goods aboard (refrigerated), unloads and drives back.
// Pickup: one of the company's parked trucks fetches the goods. A truck that cannot unload gives up after a timeout; a supplier
// truck then drives the rest back to the farm, where it leaves the world and is refunded. Recurring orders repeat every few
// game hours inside the clock step.
use std::collections::BTreeMap;

use super::company::{LEDGER_FARM_DELIVERY, LEDGER_FARM_GOODS, LEDGER_FARM_REFUND};
use super::employees::GAME_HOUR_SECONDS;
use super::rules;
use super::state::*;
use super::*;

pub const FARM_DELIVERY_FEE_CENTS: i64 = 2000;
pub const FARM_OWNED_PRICE_PERCENT: i64 = 50;
pub const MAX_FARM_ORDER_PACKS: i32 = 20;
pub const MAX_FARM_SCHEDULE_HOURS: i32 = 24;
pub const FARM_DOCK_TIMEOUT_SECONDS: i64 = 120;
pub const SUPPLIER_TRUCK_SPEED: i32 = 15;
pub const SUPPLIER_TRUCK_LOAD_UNITS: i32 = 5;
pub const SUPPLIER_TRUCK_MIN_SLOTS: i32 = 4;
pub const SUPPLIER_TRUCK_NAME: &str = "Farm truck";

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum FarmKind { Dairy, Cattle, Poultry, Vegetable, Grain, Fishery }

pub const FARM_KINDS: [FarmKind; 6] = [FarmKind::Dairy, FarmKind::Cattle, FarmKind::Poultry, FarmKind::Vegetable, FarmKind::Grain, FarmKind::Fishery];

pub fn farm_items(kind: FarmKind) -> &'static [&'static str] {
    match kind {
        FarmKind::Dairy => &["milk", "cheese"],
        FarmKind::Cattle => &["beef"],
        FarmKind::Poultry => &["egg", "chicken"],
        FarmKind::Vegetable => &["potato", "tomato", "lettuce"],
        FarmKind::Grain => &["flour", "dough"],
        FarmKind::Fishery => &["fish"],
    }
}

pub fn farm_name(kind: FarmKind) -> &'static str {
    match kind {
        FarmKind::Dairy => "Dairy farm",
        FarmKind::Cattle => "Cattle farm",
        FarmKind::Poultry => "Poultry farm",
        FarmKind::Vegetable => "Vegetable farm",
        FarmKind::Grain => "Grain farm",
        FarmKind::Fishery => "Fishery",
    }
}

/// Farms take the types in turn in lot ID order.
pub fn farm_kinds<'a>(offers: impl IntoIterator<Item = &'a PropertyOffer>) -> BTreeMap<String, FarmKind> {
    let mut lots: Vec<&str> = offers.into_iter().filter(|o| o.category == FARM_CATEGORY).map(|o| o.lot_id.as_str()).collect();
    lots.sort();
    lots.dedup();
    lots.into_iter().enumerate().map(|(i, lot)| (lot.to_string(), FARM_KINDS[i % FARM_KINDS.len()])).collect()
}

pub fn gate_site_id(lot_id: &str) -> String { format!("farm-gate:{lot_id}") }

pub fn farm_goods_cents(pack_cents: i64, packs: i32, owned: bool) -> i64 { pack_cents * packs as i64 * if owned { FARM_OWNED_PRICE_PERCENT } else { 100 } / 100 }

pub fn owns_lot(state: &Snapshot, company_id: &str, lot_id: &str) -> bool { state.properties.iter().any(|p| p.lot_id == lot_id && p.company_id == company_id) }

impl GoodsWorld {
    /// Orders packs of a farm's offer to a dock; an empty truck means delivery (pays the fee), otherwise that truck picks up.
    #[allow(clippy::too_many_arguments)]
    pub fn order_from_farm(&mut self, player: &str, request: &str, farm_lot: &str, offer_id: &str, packs: i32, dock_id: &str, truck_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let dock = self.state.equipment.iter().find(|x| x.id == dock_id && x.kind == DOCK_KIND).cloned();
        let problem = if dock.as_ref().is_some_and(|d| !self.state.can_view(player, &d.site_id)) {
            Err("forbidden")
        } else {
            self.place_farm_order(&format!("order:{player}:{request}"), player, request, farm_lot, offer_id, packs, dock_id, truck_id, "")
        };
        match problem {
            Err(reason) => self.reject(request, player, reason),
            Ok(cents) => {
                let mut outcome = self.record(request, player, true, "ordered", "");
                outcome.cents = cents;
                if let Some(last) = self.state.outcomes.last_mut() {
                    last.cents = cents;
                }
                outcome
            }
        }
    }

    #[allow(clippy::too_many_arguments)]
    pub fn schedule_farm_order(&mut self, player: &str, request: &str, farm_lot: &str, offer_id: &str, packs: i32, dock_id: &str, truck_id: &str, every_hours: i32) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let dock = self.state.equipment.iter().find(|x| x.id == dock_id && x.kind == DOCK_KIND).cloned();
        let mut problem = self.farm_order_problem(farm_lot, offer_id, packs, dock.as_ref(), truck_id, false).err();
        if problem.is_none() && !dock.as_ref().is_some_and(|d| self.state.can_view(player, &d.site_id)) {
            problem = Some("forbidden");
        }
        if problem.is_none() && !(1..=MAX_FARM_SCHEDULE_HOURS).contains(&every_hours) {
            problem = Some("invalid-interval");
        }
        if let Some(problem) = problem {
            return self.reject(request, player, problem);
        }
        let company = self.company_of_site(&dock.expect("checked").site_id).unwrap_or_default();
        self.state.farm_schedules.push(FarmSchedule {
            id: format!("schedule:{player}:{request}"), company_id: company, player_id: player.into(), farm_lot_id: farm_lot.into(), offer_id: offer_id.into(),
            packs, dock_id: dock_id.into(), truck_id: truck_id.into(), every_hours, next_hour: self.state.clock_seconds / GAME_HOUR_SECONDS + 1, last_problem: String::new(),
        });
        self.record(request, player, true, "scheduled", "")
    }

    pub fn cancel_farm_schedule(&mut self, player: &str, request: &str, schedule_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let company = self.state.farm_schedules.iter().find(|s| s.id == schedule_id).and_then(|s| self.state.companies.iter().find(|c| c.id == s.company_id));
        if !company.is_some_and(|c| c.site_ids.iter().any(|s| self.state.can_view(player, s))) {
            return self.reject(request, player, "forbidden");
        }
        self.state.farm_schedules.retain(|s| s.id != schedule_id);
        self.record(request, player, true, "unscheduled", "")
    }

    /// Ok(cents) when the order can be placed now (or, when !now, is well formed); otherwise the first problem.
    fn farm_order_problem(&mut self, farm_lot: &str, offer_id: &str, packs: i32, dock: Option<&Equipment>, truck_id: &str, now: bool) -> std::result::Result<i64, &'static str> {
        let Some(offers) = self.content.property_offers.as_ref() else { return Err("unknown-farm") };
        if !offers.get(farm_lot).is_some_and(|f| f.category == FARM_CATEGORY) {
            return Err("unknown-farm");
        }
        let kind = farm_kinds(offers.values())[farm_lot];
        let Some(offer) = self.content.offers.get(offer_id).filter(|o| farm_items(kind).contains(&o.item_id.as_str())).cloned() else { return Err("not-sold-here") };
        if !(1..=MAX_FARM_ORDER_PACKS).contains(&packs) {
            return Err("invalid-quantity");
        }
        let Some(dock) = dock else { return Err("invalid-dock") };
        let Some(company) = self.company_of_site(&dock.site_id) else { return Err("no-company") };
        if self.state.site(&dock.site_id).is_none() {
            return Err("no-road");
        }
        let units = offer.quantity * packs;
        if !truck_id.is_empty() {
            let Some(truck) = self.state.trucks.iter().find(|t| t.id == truck_id && t.company_id == company && !t.supplier) else { return Err("invalid-truck") };
            if now && (truck.state != TruckState::Parked || !truck.order_id.is_empty()) {
                return Err("truck-busy");
            }
            if now && !self.fits(&truck.cargo_location_id(), &offer.item_id, false, units) {
                return Err("capacity");
            }
            if !now && rules::slots(units as i64, self.max_stack(&offer.item_id)) > truck.cargo_slots {
                return Err("capacity");
            }
        }
        if !now {
            return Ok(0);
        }
        if !dock.placed() || self.placed_dock(&dock.id).is_none() {
            return Err("dock-unreachable");
        }
        let cents = farm_goods_cents(offer.price_cents, packs, owns_lot(&self.state, &company, farm_lot)) + if truck_id.is_empty() { FARM_DELIVERY_FEE_CENTS } else { 0 };
        if self.cash_of(&company) < cents {
            return Err("insufficient-funds");
        }
        Ok(cents)
    }

    #[allow(clippy::too_many_arguments)]
    fn place_farm_order(&mut self, order_id: &str, player: &str, request: &str, farm_lot: &str, offer_id: &str, packs: i32, dock_id: &str, truck_id: &str, schedule_id: &str) -> std::result::Result<i64, &'static str> {
        let dock = self.state.equipment.iter().find(|x| x.id == dock_id && x.kind == DOCK_KIND).cloned();
        self.usable_docks.clear();
        let checked = self.farm_order_problem(farm_lot, offer_id, packs, dock.as_ref(), truck_id, true);
        self.usable_docks.clear();
        let cents = checked?;
        if self.state.farm_orders.iter().any(|o| o.id == order_id) {
            return Err("duplicate-order");
        }
        let dock = dock.expect("checked");
        let offer = self.content.offers[offer_id].clone();
        let company = self.company_of_site(&dock.site_id).expect("checked");
        let pickup = !truck_id.is_empty();
        let units = offer.quantity * packs;
        let fee = if pickup { 0 } else { FARM_DELIVERY_FEE_CENTS };
        let goods = cents - fee;
        let gate = self.farm_gate(farm_lot);
        self.try_debit(&company, goods, CashNote::new(LEDGER_FARM_GOODS, &dock.site_id).request(request).offer(offer_id));
        if fee > 0 {
            self.try_debit(&company, fee, CashNote::new(LEDGER_FARM_DELIVERY, &dock.site_id).request(request).offer(offer_id));
        }
        let mut order = FarmOrder {
            id: order_id.into(), company_id: company.clone(), player_id: player.into(), farm_lot_id: farm_lot.into(), offer_id: offer_id.into(),
            item_id: offer.item_id.clone(), units, spoil_after_seconds: offer.spoil_after_seconds, goods_cents: goods, fee_cents: fee,
            site_id: dock.site_id.clone(), dock_id: dock.id.clone(), pickup, schedule_id: schedule_id.into(), placed_seconds: self.state.clock_seconds,
            loaded: !pickup, ..Default::default()
        };
        if pickup {
            order.truck_id = truck_id.into();
            self.state.farm_orders.push(order);
            let index = self.state.trucks.iter().position(|t| t.id == truck_id).expect("checked");
            self.state.trucks[index].order_id = order_id.into();
            self.drive(index, &gate, TruckState::ToPickup, TruckState::Loading);
        } else {
            let truck = Truck {
                id: format!("supply:{order_id}"), company_id: company, name: SUPPLIER_TRUCK_NAME.into(), supplier: true, order_id: order_id.into(),
                cargo_slots: SUPPLIER_TRUCK_MIN_SLOTS.max(rules::slots(units as i64, self.max_stack(&offer.item_id))), speed_metres_per_second: SUPPLIER_TRUCK_SPEED,
                load_units_per_second: SUPPLIER_TRUCK_LOAD_UNITS, state: TruckState::Parked, site_id: gate, ..Default::default()
            };
            order.truck_id = truck.id.clone();
            let cargo = truck.cargo_location_id();
            self.state.farm_orders.push(order);
            self.add_truck(truck);
            self.state.lots.push(Lot {
                id: format!("farm:{order_id}"), item_id: offer.item_id.clone(), owner_id: dock.site_id.clone(), location_id: cargo, quantity: units,
                spoil_after_seconds: offer.spoil_after_seconds, ..Default::default()
            });
            let index = self.state.trucks.len() - 1;
            self.drive(index, &dock.site_id, TruckState::ToDropoff, TruckState::Unloading);
        }
        self.state.revision += 1;
        Ok(cents)
    }

    /// The farm's gate site, created at its lot's street access on first use.
    fn farm_gate(&mut self, farm_lot: &str) -> String {
        let id = gate_site_id(farm_lot);
        if self.state.site(&id).is_some() {
            return id;
        }
        let farm = self.content.property_offers.as_ref().expect("a farm lot is listed")[farm_lot].clone();
        self.state.sites.push(Site { id: id.clone(), name: farm.building_id, map_x: farm.access_x, map_z: farm.access_z });
        self.site_points.remove(&id);
        id
    }

    pub(crate) fn run_farm_schedules(&mut self) {
        if self.state.farm_schedules.is_empty() {
            return;
        }
        let hour = self.state.clock_seconds / GAME_HOUR_SECONDS;
        let mut due: Vec<FarmSchedule> = self.state.farm_schedules.iter().filter(|s| s.next_hour <= hour).cloned().collect();
        due.sort_by(|a, b| a.id.cmp(&b.id));
        for schedule in due {
            let result = self.place_farm_order(&format!("order:{}:{}", schedule.id, schedule.next_hour), &schedule.player_id, "", &schedule.farm_lot_id, &schedule.offer_id, schedule.packs, &schedule.dock_id, &schedule.truck_id, &schedule.id);
            if let Some(stored) = self.state.farm_schedules.iter_mut().find(|s| s.id == schedule.id) {
                stored.last_problem = result.err().unwrap_or("").to_string();
                stored.next_hour = (stored.next_hour + stored.every_hours as i64).max(hour + 1);
            }
        }
    }

    pub(crate) fn load_order_second(&mut self, index: usize) -> bool {
        let order_id = self.state.trucks[index].order_id.clone();
        let Some(order_index) = self.state.farm_orders.iter().position(|o| o.id == order_id) else { return false };
        if !self.state.farm_orders[order_index].loaded {
            let order = &self.state.farm_orders[order_index];
            let lot = Lot {
                id: format!("farm:{}", order.id), item_id: order.item_id.clone(), owner_id: order.site_id.clone(), location_id: self.state.trucks[index].cargo_location_id(),
                quantity: order.units, spoil_after_seconds: order.spoil_after_seconds, ..Default::default()
            };
            self.state.lots.push(lot);
            self.state.farm_orders[order_index].loaded = true;
        }
        let site = self.state.farm_orders[order_index].site_id.clone();
        self.drive(index, &site, TruckState::ToDropoff, TruckState::Unloading);
        true
    }

    pub(crate) fn unload_order_second(&mut self, index: usize) -> bool {
        let order_id = self.state.trucks[index].order_id.clone();
        let Some(order) = self.state.farm_orders.iter().find(|o| o.id == order_id).cloned() else { return false };
        let dock = self.placed_dock(&order.dock_id).filter(|d| d.site_id == order.site_id);
        if dock.is_none() {
            let mut others: Vec<String> = self.state.equipment.iter().filter(|x| x.site_id == order.site_id && x.kind == DOCK_KIND && x.placed()).map(|x| x.id.clone()).collect();
            others.sort();
            let other = others.into_iter().find(|id| self.placed_dock(id).is_some());
            if let Some(other) = other {
                if let Some(stored) = self.state.farm_orders.iter_mut().find(|o| o.id == order_id) {
                    stored.dock_id = other;
                }
                self.state.trucks[index].docked = false;
                return true;
            }
        }
        let mut moved = 0;
        if let Some(dock) = &dock {
            let truck = self.state.trucks[index].clone();
            let mut budget = truck.load_units_per_second;
            let mut cargo: Vec<Lot> = self.state.lots.iter().filter(|l| l.location_id == truck.cargo_location_id() && l.item_id == order.item_id).cloned().collect();
            cargo.sort_by(|a, b| b.exposure_seconds.cmp(&a.exposure_seconds).then_with(|| a.id.cmp(&b.id)));
            for lot in cargo {
                let take = (budget.min(lot.quantity) as i64).min(self.free_units_for(&dock.output_location_id(), &lot)) as i32;
                if take == 0 {
                    continue;
                }
                self.move_units(&lot.id, &dock.output_location_id(), take, &dock.site_id);
                budget -= take;
                moved += take;
                if budget == 0 {
                    break;
                }
            }
        }
        let stored = self.state.farm_orders.iter_mut().find(|o| o.id == order_id).expect("order exists");
        stored.unloaded_units += moved;
        if moved > 0 {
            stored.wait_seconds = 0;
        } else {
            stored.wait_seconds += 1;
        }
        if stored.unloaded_units < stored.units && stored.wait_seconds < FARM_DOCK_TIMEOUT_SECONDS {
            return true;
        }
        self.finish_unloading(index, &order_id);
        true
    }

    fn finish_unloading(&mut self, index: usize, order_id: &str) {
        self.state.trucks[index].docked = false;
        if !self.state.trucks[index].supplier {
            let truck = &mut self.state.trucks[index];
            truck.order_id.clear();
            truck.state = TruckState::Parked;
            self.state.farm_orders.retain(|o| o.id != order_id);
            return;
        }
        let lot = match self.state.farm_orders.iter_mut().find(|o| o.id == order_id) {
            Some(order) => {
                order.returning = true;
                order.farm_lot_id.clone()
            }
            None => return,
        };
        self.drive(index, &gate_site_id(&lot), TruckState::ToPark, TruckState::Parked);
        if self.state.trucks[index].state == TruckState::Parked {
            self.return_supplier_truck(index);
        }
    }

    /// A supplier truck back at its farm: its remaining goods leave the world and are refunded, and the truck and order go.
    pub(crate) fn return_supplier_truck(&mut self, index: usize) {
        let truck = self.state.trucks[index].clone();
        let cargo = truck.cargo_location_id();
        let left: Vec<Lot> = self.state.lots.iter().filter(|l| l.location_id == cargo).cloned().collect();
        if let Some(order) = self.state.farm_orders.iter().find(|o| o.id == truck.order_id).cloned() {
            let units: i64 = left.iter().map(|l| l.quantity as i64).sum();
            let mut refund = if order.units == 0 { 0 } else { order.goods_cents * units.min(order.units as i64) / order.units as i64 };
            if order.unloaded_units == 0 {
                refund += order.fee_cents;
            }
            if refund > 0 {
                let now = self.truck_now.unwrap_or(self.state.clock_seconds);
                self.try_credit(&order.company_id, refund, CashNote::new(LEDGER_FARM_REFUND, &order.site_id).offer(&order.offer_id).at(now));
            }
            self.state.farm_orders.retain(|o| o.id != order.id);
        }
        let gone: std::collections::HashSet<String> = left.iter().map(|l| l.id.clone()).collect();
        self.state.lots.retain(|l| !gone.contains(&l.id));
        self.state.reservations.retain(|r| !gone.contains(&r.lot_id));
        self.state.trucks.retain(|t| t.id != truck.id);
        self.state.locations.retain(|l| l.id != cargo);
    }

    /// A view carries its company's open orders and schedules, and the gates its orders' trucks use.
    pub(crate) fn view_farms(&self, view: &mut Snapshot, site_id: &str) {
        let company = self.state.company_of_site(site_id);
        view.farm_orders = company.map(|c| self.state.farm_orders.iter().filter(|o| o.company_id == c.id).cloned().collect()).unwrap_or_default();
        view.farm_schedules = company.map(|c| self.state.farm_schedules.iter().filter(|o| o.company_id == c.id).cloned().collect()).unwrap_or_default();
        let gates: std::collections::HashSet<String> = view.farm_orders.iter().map(|o| gate_site_id(&o.farm_lot_id)).collect();
        let extra: Vec<Site> = self.state.sites.iter().filter(|s| gates.contains(&s.id) && !view.sites.iter().any(|v| v.id == s.id)).cloned().collect();
        view.sites.extend(extra);
    }
}
