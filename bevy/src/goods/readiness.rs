// Why a restaurant can or cannot sell right now (Unity RestaurantReadiness, decision 0038), read from a snapshot or a client's
// baseline. Pure: it asks only the questions the customer simulation asks, so the client explains exactly what the server does.
use std::collections::HashSet;

use super::company::LEDGER_SALE;
use super::grid;
use super::production::input_plan;
use super::rules;
use super::state::*;
use super::{PropertyOffer, RecipeDefinition, COUNTER_KIND, DOCK_KIND, NON_PERISHABLE_SECONDS, RESTAURANT_KIND};

pub const NO_CUSTOMER_DOOR: &str = "no-customer-door";
pub const NO_REACHABLE_REGISTER: &str = "no-reachable-register";
pub const NO_STAFFED_REGISTER: &str = "no-staffed-register";
pub const NO_EDIBLE_MENU_ITEM: &str = "no-edible-menu-item";
pub const NO_REACHABLE_SEAT: &str = "no-reachable-seat";
pub const DOCK_NOT_BESIDE_BACK_DOOR: &str = "dock-not-beside-back-door";
pub const STOCK_SPOILS_SOON: &str = "stock-spoils-soon";
pub const SPOIL_SOON_SECONDS: i64 = 600;
pub const RECENT_SECONDS: i64 = 600;

#[derive(Clone, Debug, Default)]
pub struct Readiness {
    pub blocker: Option<&'static str>,
    pub warnings: Vec<&'static str>,
    pub registers: usize,
    pub reachable_registers: usize,
    pub staffed_registers: usize,
    pub reachable_seats: i32,
    pub docks_not_beside_back_door: Vec<String>,
    pub spoiling_item_id: String,
    pub spoiling_seconds: i64,
    pub queued: usize,
    pub eating: usize,
    pub recent_sales: usize,
    pub recent_sales_cents: i64,
    pub recent_spend_cents: i64,
}

impl Readiness {
    pub fn ready(&self) -> bool { self.blocker.is_none() }
}

/// None when the site holds no restaurant or no company owns it.
pub fn evaluate<'a>(state: &Snapshot, site_id: &str, offer: Option<&PropertyOffer>, menu: impl IntoIterator<Item = &'a RecipeDefinition>) -> Option<Readiness> {
    if site_id.is_empty() || !rules::is_restaurant_site(state, site_id) {
        return None;
    }
    let company = state.company_of_site(site_id)?;
    let mut items: Vec<&RecipeDefinition> = menu.into_iter().filter(|r| rules::is_menu_item(r)).collect();
    items.sort_by(|a, b| a.id.cmp(&b.id));
    let mut result = Readiness::default();
    let mut placed: Vec<Equipment> = state.equipment.iter().filter(|x| x.site_id == site_id && x.placed()).cloned().collect();
    placed.sort_by(|a, b| a.id.cmp(&b.id));
    let registers: Vec<Equipment> = placed.iter().filter(|x| x.kind == COUNTER_KIND).cloned().collect();
    let walk = rules::walkable(state, site_id, None, true);
    let reach = rules::reached(walk.as_ref(), rules::street_cells(state.layout_of(site_id), offer));
    let reachable: Vec<Equipment> = registers.iter().filter(|x| rules::serves_customers(&reach, x)).cloned().collect();
    let staffed: Vec<&Equipment> = reachable.iter().filter(|x| !x.staff_id.is_empty()).collect();
    result.registers = registers.len();
    result.reachable_registers = reachable.len();
    result.staffed_registers = staffed.len();
    result.reachable_seats = rules::seating_tables(walk.as_ref(), &reach, &reachable, &placed).iter().map(|t| t.seats).sum();
    let restaurants: Vec<&Building> = state.buildings.iter().filter(|b| b.site_id == site_id && b.kind == RESTAURANT_KIND).collect();
    if restaurants.iter().all(|b| grid::customer_doors(b).is_empty()) {
        result.blocker = Some(NO_CUSTOMER_DOOR);
    } else if reachable.is_empty() {
        result.blocker = Some(NO_REACHABLE_REGISTER);
    } else if staffed.is_empty() {
        result.blocker = Some(NO_STAFFED_REGISTER);
    } else if !staffed.iter().any(|r| state.stations.iter().find(|s| s.id == r.id).is_some_and(|s| items.iter().any(|i| input_plan(state, s, i).is_some()))) {
        result.blocker = Some(NO_EDIBLE_MENU_ITEM);
    }
    if result.reachable_seats == 0 {
        result.warnings.push(NO_REACHABLE_SEAT);
    }
    result.docks_not_beside_back_door = placed.iter().filter(|x| x.kind == DOCK_KIND && !rules::beside_back_door(state, x)).map(|x| x.id.clone()).collect();
    if !result.docks_not_beside_back_door.is_empty() {
        result.warnings.push(DOCK_NOT_BESIDE_BACK_DOOR);
    }
    let inputs: HashSet<String> = registers.iter().map(|r| r.input_location_id()).collect();
    let cold: HashSet<&str> = state.locations.iter().filter(|l| l.refrigerated).map(|l| l.id.as_str()).collect();
    let soonest = state.lots.iter()
        .filter(|l| inputs.contains(&l.location_id) && !cold.contains(l.location_id.as_str()) && !l.spoiled && l.spoil_after_seconds < NON_PERISHABLE_SECONDS)
        .min_by(|a, b| (a.spoil_after_seconds - a.exposure_seconds).cmp(&(b.spoil_after_seconds - b.exposure_seconds)).then_with(|| a.id.cmp(&b.id)));
    if let Some(lot) = soonest.filter(|l| l.spoil_after_seconds - l.exposure_seconds <= SPOIL_SOON_SECONDS) {
        result.spoiling_item_id = lot.item_id.clone();
        result.spoiling_seconds = (lot.spoil_after_seconds - lot.exposure_seconds).max(0);
        result.warnings.push(STOCK_SPOILS_SOON);
    }
    result.queued = state.customers.iter().filter(|c| c.restaurant_id == site_id && c.state == CustomerState::Queued).count();
    result.eating = state.customers.iter().filter(|c| c.restaurant_id == site_id && c.state == CustomerState::Eating).count();
    for entry in state.ledger.iter().filter(|e| e.company_id == company.id && e.clock_seconds > state.clock_seconds - RECENT_SECONDS) {
        if entry.kind == LEDGER_SALE {
            result.recent_sales += 1;
            result.recent_sales_cents += entry.cents;
        } else if entry.cents < 0 {
            result.recent_spend_cents -= entry.cents;
        }
    }
    Some(result)
}
