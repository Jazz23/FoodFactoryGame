// Customers (Unity GoodsWorld.Customers.cs and .Crowd.cs, decision 0024). Districts create customers at their rate per game hour;
// each customer scores the restaurants in range once (and again after walking out of a queue), travels there, queues, buys a
// menu item once it can be served (and, to dine in, once a seat is free), then eats or leaves with takeaway. Customers line up at
// any register stocked with their order, but only a staffed one sells; with nobody at any register they give up after two
// minutes. A purchase takes the goods from a register's input and pays the site's company inside the clock tick. Competitors
// are fixed AI restaurants with unlimited stock and no cash. Customers advance in one-second sub-steps so a long step matches many short ones.
use std::collections::{HashMap, HashSet};

use serde::{Deserialize, Serialize};

use super::company::LEDGER_SALE;
use super::grid::CELL_SIZE;
use super::production::input_plan;
use super::rules;
use super::state::*;
use super::*;
use crate::world::clock::HOUR_SECONDS;

pub const WALK_METRES_PER_SECOND: i64 = 2;
pub const PUBLISH_INTERVAL_SECONDS: i64 = 5;
pub const REPUTATION_LIMIT: i32 = 1000;
const REPUTATION_DRIFT_SECONDS: i64 = 60;
const WALK_OUT_REPUTATION: i32 = -40;
const MIN_PATIENCE_SECONDS: i64 = 90;
const MAX_PATIENCE_SECONDS: i64 = 240;
/// How long a customer waits in the queue of a restaurant with nobody at any register before walking away.
const UNSTAFFED_PATIENCE_SECONDS: i64 = 120;
const MIN_EAT_SECONDS: i64 = 120;
const MAX_EAT_SECONDS: i64 = 300;
pub const PICKY_WEALTH_PERCENT: i32 = 70;
pub const TAKEAWAY_PERCENT: i32 = 12;
pub const ENTRY_MARGIN_SECONDS: i64 = 4;
pub const CROWD_RADIUS_METRES: i32 = 150;
pub const CROWD_TRAVEL_SECONDS: i64 = 12;

/// The lowest tier a district's customers will buy.
pub fn min_tier(district: &District) -> i32 { if district.wealth_percent >= PICKY_WEALTH_PERCENT { 2 } else { 1 } }

/// How far a customer of the district walks to a restaurant `distance` metres from its spawn point.
pub fn walk_metres(district: &District, distance: i32) -> i32 { if district.spawn_walk_metres > 0 { distance.min(district.spawn_walk_metres) } else { distance } }

fn food_score(district: &District, tier: i32, cuisine: &str, price_cents: i64) -> f64 {
    let wealth = district.wealth_percent as f64 / 100.0;
    (if district.liked_cuisines.iter().any(|c| c == cuisine) { 1.0 } else { 0.0 }) + 0.8 * tier as f64 * wealth - 0.15 * price_cents as f64 / 100.0 * (1.0 - 0.7 * wealth)
}

/// A restaurant as customers see it.
#[derive(Clone, Debug, Default)]
pub(crate) struct DinerEntry {
    pub id: String,
    pub player: bool,
    pub map_x: i32,
    pub map_z: i32,
    pub competitor: Option<Competitor>,
    pub menu: Vec<RecipeDefinition>,
    pub counters: Vec<Equipment>,
    pub tables_by_counter: HashMap<String, Vec<Equipment>>,
    pub entry_seconds: i64,
    /// Registers with a paid staffer; customers queue at unstaffed registers that hold stock but are only served here.
    pub servers: i32,
    pub service_seconds: i64,
    pub seats: i32,
    pub ambience: i32,
    pub queue: Vec<String>,
    pub seats_used: HashMap<String, i32>,
    pub busy_counters: HashSet<String>,
    pub occupied_seats: i32,
    pub busy_servers: i32,
}

/// Derived from the snapshot, never persisted. Structural changes drop it; customer changes update its occupancy directly.
#[derive(Default)]
pub(crate) struct DinerCache {
    pub diners: Option<Vec<DinerEntry>>,
    pub by_id: HashMap<String, usize>,
    pub index_dirty: bool,
    pub candidates: HashMap<String, Vec<(usize, i32)>>,
    pub records: HashMap<String, usize>,
    pub stocked: HashMap<String, Vec<RecipeDefinition>>,
}

/// The crowd view: customers at lot-linked competitors near a map point, for drawing only.
#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct CrowdView {
    pub revision: i64,
    pub restaurants: Vec<CrowdRestaurant>,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct CrowdRestaurant {
    pub competitor_id: String,
    pub lot_id: String,
    pub servers: i32,
    pub seats: i32,
    pub customers: Vec<CrowdCustomer>,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct CrowdCustomer {
    pub id: String,
    pub district_id: String,
    pub appearance: i32,
    pub dine_in: bool,
    pub state: CustomerState,
    pub ticket: i64,
    pub remaining_seconds: i64,
    pub walked_out: bool,
}

/// What a client would draw differently: the competitors listed and each customer's ID, state and ticket.
pub fn crowd_signature(crowd: &CrowdView) -> String {
    let mut text = String::new();
    for restaurant in &crowd.restaurants {
        text.push_str(&restaurant.competitor_id);
        text.push('[');
        for customer in &restaurant.customers {
            text.push_str(&format!("{}:{}:{};", customer.id, customer.state as i32, customer.ticket));
        }
        text.push(']');
    }
    text
}

struct Second {
    index: HashMap<String, usize>,
    gone: HashSet<String>,
}

impl GoodsWorld {
    pub(crate) fn invalidate_diners(&mut self) {
        self.diners.diners = None;
        self.diners.by_id.clear();
        self.diners.candidates.clear();
        self.diners.index_dirty = true;
    }

    pub fn bootstrap_district(&mut self, district: District) -> Result<()> {
        if district.id.trim().is_empty() || self.state.districts.iter().any(|d| d.id == district.id) {
            return Err(format!("Invalid or duplicate district {}.", district.id));
        }
        self.state.districts.push(district);
        self.invalidate_diners();
        self.state.revision += 1;
        Ok(())
    }

    pub fn bootstrap_competitor(&mut self, competitor: Competitor) -> Result<()> {
        if competitor.id.trim().is_empty() || self.state.competitors.iter().any(|c| c.id == competitor.id) {
            return Err(format!("Invalid or duplicate competitor {}.", competitor.id));
        }
        if !competitor.lot_id.is_empty() && self.state.properties.iter().any(|p| p.lot_id == competitor.lot_id) {
            return Err(format!("Competitor {} stands on an owned lot.", competitor.id));
        }
        self.state.competitors.push(competitor);
        self.invalidate_diners();
        self.state.revision += 1;
        Ok(())
    }

    pub fn retune_district_dining(&mut self, district_id: &str, dine_in: i32) {
        if let Some(district) = self.state.districts.iter_mut().find(|d| d.id == district_id && d.dine_in_percent != dine_in) {
            district.dine_in_percent = dine_in;
            self.state.revision += 1;
        }
    }

    pub(crate) fn advance_customers(&mut self, seconds: i64) {
        if self.state.districts.is_empty() && self.state.customers.is_empty() {
            return;
        }
        let start = self.state.clock_seconds - seconds;
        for second in 1..=seconds {
            self.customer_second(start + second);
        }
    }

    fn ensure_diners(&mut self) {
        if self.diners.diners.is_some() {
            if self.diners.index_dirty {
                self.rebuild_customer_index();
            }
            return;
        }
        let menu: Vec<RecipeDefinition> = self.content.recipes.values().filter(|r| rules::is_menu_item(r)).cloned().collect();
        let mut diners = vec![];
        for site in self.state.sites.clone() {
            let mut placed: Vec<Equipment> = self.state.equipment.iter().filter(|x| x.site_id == site.id && x.placed()).cloned().collect();
            placed.sort_by(|a, b| a.id.cmp(&b.id));
            // Every register counts: customers come for stock in an unstaffed one too, but only a staffed one sells.
            let mut counters: Vec<Equipment> = placed.iter().filter(|x| x.kind == COUNTER_KIND).cloned().collect();
            if menu.is_empty() || counters.is_empty() || self.company_of_site(&site.id).is_none() {
                continue;
            }
            let offer = self.offer_of_site(&site.id);
            let walk = rules::walkable(&self.state, &site.id, None, true);
            let steps = rules::steps(walk.as_ref(), rules::street_cells(self.state.layout_of(&site.id), offer.as_ref()));
            let reached: rules::Cells = steps.keys().copied().collect();
            counters.retain(|x| rules::serves_customers(&reached, x));
            if counters.is_empty() {
                continue;
            }
            let tables_by_counter = rules::tables_by_register(walk.as_ref(), &reached, &counters, &placed);
            let seating: HashSet<String> = tables_by_counter.values().flatten().map(|t| t.id.clone()).collect();
            let seats: i32 = placed.iter().filter(|x| seating.contains(&x.id)).map(|x| x.seats).sum();
            let staffed = counters.iter().filter(|x| self.staffed(x)).count() as i32;
            let entry_steps = counters.iter().map(|c| rules::cells_beside(&reached, c).iter().map(|cell| steps[cell]).min().unwrap_or(0)).min().unwrap_or(0);
            diners.push(DinerEntry {
                id: site.id.clone(), player: true, map_x: site.map_x, map_z: site.map_z, menu: menu.clone(), servers: staffed,
                counters, tables_by_counter,
                entry_seconds: ENTRY_MARGIN_SECONDS + ((entry_steps + 1) as f64 * CELL_SIZE as f64 / WALK_METRES_PER_SECOND as f64).ceil() as i64,
                service_seconds: menu[0].duration_seconds, seats, ambience: rules::ambience(&self.state, &site.id), ..Default::default()
            });
        }
        for competitor in &self.state.competitors {
            diners.push(DinerEntry {
                id: competitor.id.clone(), map_x: competitor.map_x, map_z: competitor.map_z, competitor: Some(competitor.clone()),
                servers: competitor.servers, service_seconds: competitor.service_seconds, seats: competitor.seats, ..Default::default()
            });
        }
        diners.sort_by(|a, b| a.id.cmp(&b.id));
        self.diners.by_id = diners.iter().enumerate().map(|(i, d)| (d.id.clone(), i)).collect();
        self.diners.diners = Some(diners);
        self.diners.candidates.clear();
        self.rebuild_customer_index();
    }

    fn rebuild_customer_index(&mut self) {
        let Some(diners) = self.diners.diners.as_mut() else { return };
        for diner in diners.iter_mut() {
            diner.queue.clear();
            diner.seats_used.clear();
            diner.busy_counters.clear();
            diner.occupied_seats = 0;
            diner.busy_servers = 0;
        }
        self.diners.records = self.state.diners.iter().enumerate().map(|(i, d)| (d.restaurant_id.clone(), i)).collect();
        let positions: HashMap<String, usize> = self.state.customers.iter().enumerate().map(|(i, c)| (c.id.clone(), i)).collect();
        for customer in &self.state.customers {
            let Some(&index) = self.diners.by_id.get(&customer.restaurant_id) else { continue };
            let diner = &mut self.diners.diners.as_mut().expect("built")[index];
            match customer.state {
                CustomerState::Queued => enqueue(diner, customer, &self.state.customers, &positions),
                CustomerState::Ordering => {
                    diner.busy_servers += 1;
                    if !customer.counter_id.is_empty() {
                        diner.busy_counters.insert(customer.counter_id.clone());
                    }
                    occupy_seat(diner, &customer.table_id);
                }
                CustomerState::Eating => occupy_seat(diner, &customer.table_id),
                CustomerState::Travelling => {}
            }
        }
        self.diners.index_dirty = false;
    }

    fn customer_second(&mut self, now: i64) {
        self.ensure_diners();
        self.diners.stocked.clear();
        let mut second = Second { index: self.state.customers.iter().enumerate().map(|(i, c)| (c.id.clone(), i)).collect(), gone: HashSet::new() };
        let ids: Vec<String> = self.state.customers.iter().map(|c| c.id.clone()).collect();
        for id in ids {
            let Some(&i) = second.index.get(&id) else { continue };
            let restaurant = self.state.customers[i].restaurant_id.clone();
            let diner = self.diners.by_id.get(&restaurant).copied();
            match self.state.customers[i].state {
                CustomerState::Travelling => {
                    self.state.customers[i].remaining_seconds -= 1;
                    if self.state.customers[i].remaining_seconds > 0 {
                        continue;
                    }
                    let ticket = self.state.next_customer_number;
                    self.state.next_customer_number += 1;
                    // The register it lines up at: one holding its order, a staffed one first.
                    let register = diner.and_then(|d| self.queue_register(d, &self.state.customers[i].recipe_id));
                    let customer = &mut self.state.customers[i];
                    customer.state = CustomerState::Queued;
                    customer.queued_at_seconds = now;
                    customer.ticket = ticket;
                    customer.counter_id = register.unwrap_or_default();
                    if let Some(d) = diner {
                        let diners = self.diners.diners.as_mut().expect("built");
                        enqueue(&mut diners[d], &self.state.customers[i], &self.state.customers, &second.index);
                    }
                }
                CustomerState::Ordering => {
                    self.state.customers[i].remaining_seconds -= 1;
                    if self.state.customers[i].remaining_seconds > 0 {
                        continue;
                    }
                    let counter = std::mem::take(&mut self.state.customers[i].counter_id);
                    if let Some(d) = diner {
                        let entry = &mut self.diners.diners.as_mut().expect("built")[d];
                        entry.busy_servers -= 1;
                        if !counter.is_empty() {
                            entry.busy_counters.remove(&counter);
                        }
                    }
                    if !self.state.customers[i].dine_in {
                        second.gone.insert(id.clone());
                        continue;
                    }
                    let eat = self.between(MIN_EAT_SECONDS, MAX_EAT_SECONDS);
                    let customer = &mut self.state.customers[i];
                    customer.state = CustomerState::Eating;
                    customer.remaining_seconds = eat;
                }
                CustomerState::Eating => {
                    self.state.customers[i].remaining_seconds -= 1;
                    if self.state.customers[i].remaining_seconds <= 0 {
                        if let Some(d) = diner {
                            let table = self.state.customers[i].table_id.clone();
                            release_seat(&mut self.diners.diners.as_mut().expect("built")[d], &table);
                        }
                        // Gone at the end of this second; its seat is free now.
                        self.state.customers[i].table_id.clear();
                        self.state.customers[i].seat = -1;
                        second.gone.insert(id.clone());
                    }
                }
                CustomerState::Queued => {
                    let customer = &self.state.customers[i];
                    let unstaffed = diner.is_some_and(|d| { let e = &self.diners.diners.as_ref().expect("built")[d]; e.player && e.servers == 0 });
                    let patience = if unstaffed { customer.patience_seconds.min(UNSTAFFED_PATIENCE_SECONDS) } else { customer.patience_seconds };
                    if now - customer.queued_at_seconds < patience {
                        continue;
                    }
                    if let Some(d) = diner {
                        self.diners.diners.as_mut().expect("built")[d].queue.retain(|x| *x != id);
                    }
                    let record = self.diner_record(&restaurant);
                    self.state.diners[record].walked_out += 1;
                    adjust_reputation(&mut self.state.diners[record], WALK_OUT_REPUTATION);
                    let again = self.state.customers[i].left_restaurant_id.is_empty();
                    self.state.customers[i].left_restaurant_id = restaurant.clone();
                    let mut customer = self.state.customers[i].clone();
                    if !again || !self.decide(&mut customer) {
                        second.gone.insert(id.clone());
                    } else {
                        self.state.customers[i] = customer;
                    }
                }
            }
        }

        for d in 0..self.state.districts.len() {
            self.state.districts[d].spawn_progress += self.state.districts[d].customers_per_hour as i64;
            while self.state.districts[d].spawn_progress >= HOUR_SECONDS {
                self.state.districts[d].spawn_progress -= HOUR_SECONDS;
                self.spawn(d, &mut second);
            }
        }

        let count = self.diners.diners.as_ref().map_or(0, |d| d.len());
        for d in 0..count {
            self.serve(d, now, &second);
        }
        if now % PUBLISH_INTERVAL_SECONDS == 0 {
            for d in 0..count {
                self.publish(d);
            }
        }
        if now % REPUTATION_DRIFT_SECONDS == 0 {
            for record in &mut self.state.diners {
                record.reputation -= record.reputation.signum();
            }
        }
        if !second.gone.is_empty() {
            self.state.customers.retain(|c| !second.gone.contains(&c.id));
        }
    }

    fn spawn(&mut self, district: usize, second: &mut Second) {
        let d = &self.state.districts[district];
        let (id, variants, dine_in_percent) = (format!("customer:{}:{}", d.id, self.state.next_customer_number), d.appearance_variants, d.dine_in_percent);
        let district_id = d.id.clone();
        self.state.next_customer_number += 1;
        let appearance = (self.next_customer_random() * variants as f64) as i32;
        let dine_in = self.next_customer_random() * 100.0 < dine_in_percent as f64;
        let patience = self.between(MIN_PATIENCE_SECONDS, MAX_PATIENCE_SECONDS);
        let mut customer = Customer { id: id.clone(), district_id, appearance, dine_in, patience_seconds: patience, ..Default::default() };
        if self.decide(&mut customer) {
            second.index.insert(id, self.state.customers.len());
            self.state.customers.push(customer);
        }
    }

    fn candidates(&mut self, district: &District) -> Vec<(usize, i32)> {
        if let Some(found) = self.diners.candidates.get(&district.id) {
            return found.clone();
        }
        let nearby: Vec<(usize, i32)> = self.diners.diners.as_ref().map(|diners| {
            diners.iter().enumerate().filter_map(|(i, d)| {
                let distance = (d.map_x - district.map_x).abs() + (d.map_z - district.map_z).abs();
                (distance <= district.range_metres).then_some((i, distance))
            }).collect()
        }).unwrap_or_default();
        self.diners.candidates.insert(district.id.clone(), nearby.clone());
        nearby
    }

    /// Whether a register has a paid staffer to sell at it.
    fn staffed(&self, counter: &Equipment) -> bool { !counter.staff_id.is_empty() && !self.unpaid(&counter.staff_id) }

    /// The register of a player restaurant whose input holds a whole `recipe_id` purchase, preferring a staffed one.
    fn queue_register(&self, diner: usize, recipe_id: &str) -> Option<String> {
        let entry = &self.diners.diners.as_ref()?[diner];
        if !entry.player {
            return None;
        }
        let recipe = self.content.recipes.get(recipe_id)?;
        let stocked: Vec<&Equipment> = entry.counters.iter().filter(|c| {
            self.state.stations.iter().find(|s| s.id == c.id).is_some_and(|station| input_plan(&self.state, station, recipe).is_some())
        }).collect();
        stocked.iter().find(|c| self.staffed(c)).or_else(|| stocked.first()).map(|c| c.id.clone())
    }

    /// A player restaurant's menu items in stock for one whole purchase at one of its registers.
    fn stocked_menu(&mut self, diner: usize) -> Vec<RecipeDefinition> {
        let entry = &self.diners.diners.as_ref().expect("built")[diner];
        if let Some(found) = self.diners.stocked.get(&entry.id) {
            return found.clone();
        }
        let inputs: HashSet<String> = entry.counters.iter().map(|c| c.input_location_id()).collect();
        let mut stock: HashMap<(String, String), i32> = HashMap::new();
        for lot in &self.state.lots {
            if !inputs.contains(&lot.location_id) || lot.spoiled || lot.owner_id != entry.id {
                continue;
            }
            let available = self.available(lot);
            if available > 0 {
                *stock.entry((lot.location_id.clone(), lot.item_id.clone())).or_default() += available;
            }
        }
        let stocked: Vec<RecipeDefinition> = entry.menu.iter().filter(|recipe| {
            entry.counters.iter().any(|counter| recipe.inputs.iter().all(|input| stock.get(&(counter.input_location_id(), input.item_id.clone())).is_some_and(|u| *u >= input.quantity)))
        }).cloned().collect();
        let id = entry.id.clone();
        self.diners.stocked.insert(id, stocked.clone());
        stocked
    }

    /// Scores the restaurants in range and picks one weighted by exp(score) against staying home (weight 1).
    fn decide(&mut self, customer: &mut Customer) -> bool {
        let Some(district) = self.state.districts.iter().find(|d| d.id == customer.district_id).cloned() else { return false };
        let minimum = min_tier(&district);
        let mut options: Vec<(usize, String, f64)> = vec![];
        let mut total = 1.0;
        for (diner, distance) in self.candidates(&district) {
            let entry = self.diners.diners.as_ref().expect("built")[diner].clone();
            if entry.id == customer.left_restaurant_id {
                continue;
            }
            let record = self.diners.records.get(&entry.id).map(|&i| self.state.diners[i].clone());
            let (recipe_id, food) = if entry.player {
                let choices: Vec<RecipeDefinition> = self.stocked_menu(diner).into_iter().filter(|r| r.tier >= minimum).collect();
                if choices.is_empty() {
                    continue;
                }
                let mut scored: Vec<(String, f64)> = choices.iter().map(|r| (r.id.clone(), food_score(&district, r.tier, &r.cuisine, r.sale_cents))).collect();
                scored.sort_by(|a, b| b.1.partial_cmp(&a.1).unwrap_or(std::cmp::Ordering::Equal).then_with(|| a.0.cmp(&b.0)));
                scored.swap_remove(0)
            } else {
                let competitor = entry.competitor.as_ref().expect("a competitor diner");
                if competitor.tier < minimum {
                    continue;
                }
                (String::new(), food_score(&district, competitor.tier, &competitor.cuisine, competitor.price_cents))
            };
            let free_seats = record.as_ref().map_or(entry.seats, |r| r.published_free_seats);
            let score = 1.0 + food - 0.5 * distance as f64 / 100.0 - 0.5 * record.as_ref().map_or(0, |r| r.published_wait_seconds) as f64 / 60.0
                + record.as_ref().map_or(0, |r| r.reputation) as f64 / REPUTATION_LIMIT as f64
                + rules::AMBIENCE_WEIGHT * entry.ambience as f64 / rules::AMBIENCE_CAP as f64
                + if customer.dine_in { if free_seats > 0 { 0.5 } else { -1.0 } } else { 0.0 };
            let weight = score.exp();
            options.push((diner, recipe_id, weight));
            total += weight;
        }
        let mut pick = self.next_customer_random() * total - 1.0;
        if pick < 0.0 {
            return false;
        }
        let count = options.len();
        for (index, (diner, recipe_id, weight)) in options.into_iter().enumerate() {
            pick -= weight;
            if pick >= 0.0 && index < count - 1 {
                continue;
            }
            let entry = &self.diners.diners.as_ref().expect("built")[diner];
            let distance = (entry.map_x - district.map_x).abs() + (entry.map_z - district.map_z).abs();
            customer.state = CustomerState::Travelling;
            customer.restaurant_id = entry.id.clone();
            customer.recipe_id = recipe_id;
            customer.counter_id.clear();
            customer.remaining_seconds = (walk_metres(&district, distance) as i64 / WALK_METRES_PER_SECOND).max(1) + entry.entry_seconds;
            customer.queued_at_seconds = 0;
            return true;
        }
        false
    }

    /// Serves the queue in ticket order; a customer who cannot be served yet does not hold up those behind who can.
    fn serve(&mut self, diner: usize, now: i64, second: &Second) {
        let mut position = 0;
        loop {
            let entry = &self.diners.diners.as_ref().expect("built")[diner];
            if position >= entry.queue.len() || entry.busy_servers >= entry.servers {
                return;
            }
            let customer_id = entry.queue[position].clone();
            let Some(&ci) = second.index.get(&customer_id) else {
                position += 1;
                continue;
            };
            let entry = entry.clone();
            let mut table = String::new();
            let mut seat = -1;
            let (price, service, tier, counter_id);
            if entry.player {
                let Some(recipe) = self.content.recipes.get(&self.state.customers[ci].recipe_id).cloned() else {
                    position += 1;
                    continue;
                };
                let Some(company) = self.company_of_site(&entry.id) else {
                    position += 1;
                    continue;
                };
                if self.cash_of(&company) > i64::MAX - recipe.sale_cents {
                    position += 1;
                    continue;
                }
                if self.state.customers[ci].dine_in && entry.seats == 0 {
                    self.state.customers[ci].dine_in = false;
                }
                let dine_in = self.state.customers[ci].dine_in;
                let mut chosen: Option<(Equipment, Option<Equipment>)> = None;
                for candidate in &entry.counters {
                    if entry.busy_counters.contains(&candidate.id) || !self.staffed(candidate) {
                        continue;
                    }
                    let mut free = None;
                    if dine_in {
                        free = entry.tables_by_counter.get(&candidate.id).and_then(|tables| tables.iter().find(|t| entry.seats_used.get(&t.id).is_none_or(|u| *u < t.seats)).cloned());
                        if free.is_none() {
                            continue;
                        }
                    }
                    let Some(station) = self.state.stations.iter().find(|s| s.id == candidate.id) else { continue };
                    if input_plan(&self.state, station, &recipe).is_none() {
                        continue;
                    }
                    chosen = Some((candidate.clone(), free));
                    break;
                }
                let Some((counter, free)) = chosen else {
                    position += 1;
                    continue;
                };
                if let Some(free) = free {
                    seat = self.free_seat(&free);
                    table = free.id;
                }
                let station = self.state.stations.iter().find(|s| s.id == counter.id).cloned().expect("a placed register has a station");
                for (lot_id, take) in input_plan(&self.state, &station, &recipe).unwrap_or_default() {
                    if let Some(index) = self.state.lots.iter().position(|l| l.id == lot_id) {
                        self.state.lots[index].quantity -= take;
                        if self.state.lots[index].quantity == 0 {
                            self.state.lots.remove(index);
                        }
                    }
                }
                let customer = self.state.customers[ci].id.clone();
                self.try_credit(&company, recipe.sale_cents, CashNote::new(LEDGER_SALE, &entry.id).equipment(&counter.id).customer(&customer).offer(&recipe.id).at(now));
                price = recipe.sale_cents;
                service = recipe.duration_seconds;
                tier = recipe.tier;
                counter_id = counter.id;
            } else {
                if self.state.customers[ci].dine_in {
                    if entry.occupied_seats >= entry.seats {
                        position += 1;
                        continue;
                    }
                    table = entry.id.clone();
                }
                let competitor = entry.competitor.as_ref().expect("a competitor diner");
                price = competitor.price_cents;
                service = competitor.service_seconds;
                tier = competitor.tier;
                counter_id = String::new();
            }
            let queued_at = self.state.customers[ci].queued_at_seconds;
            let customer = &mut self.state.customers[ci];
            customer.state = CustomerState::Ordering;
            customer.paid_cents = price;
            customer.remaining_seconds = service;
            customer.counter_id = counter_id.clone();
            customer.table_id = table.clone();
            customer.seat = seat;
            let target = &mut self.diners.diners.as_mut().expect("built")[diner];
            target.queue.remove(position);
            target.busy_servers += 1;
            if !counter_id.is_empty() {
                target.busy_counters.insert(counter_id);
            }
            occupy_seat(target, &table);
            let record = self.diner_record(&entry.id);
            self.state.diners[record].served += 1;
            adjust_reputation(&mut self.state.diners[record], (5 * tier as i64 - (now - queued_at).min(600) / 30) as i32);
        }
    }

    fn publish(&mut self, diner: usize) {
        let entry = &self.diners.diners.as_ref().expect("built")[diner];
        let (id, wait, free) = (entry.id.clone(), entry.queue.len() as i64 * entry.service_seconds / entry.servers.max(1) as i64, (entry.seats - entry.occupied_seats).max(0));
        let record = self.diner_record(&id);
        self.state.diners[record].published_wait_seconds = wait;
        self.state.diners[record].published_free_seats = free;
    }

    fn diner_record(&mut self, restaurant: &str) -> usize {
        if let Some(&index) = self.diners.records.get(restaurant) {
            if self.state.diners.get(index).is_some_and(|d| d.restaurant_id == restaurant) {
                return index;
            }
        }
        if let Some(index) = self.state.diners.iter().position(|d| d.restaurant_id == restaurant) {
            self.diners.records.insert(restaurant.to_string(), index);
            return index;
        }
        self.state.diners.push(Diner { restaurant_id: restaurant.to_string(), ..Default::default() });
        let index = self.state.diners.len() - 1;
        self.diners.records.insert(restaurant.to_string(), index);
        index
    }

    /// The lowest seat of a player table no customer holds, or -1.
    fn free_seat(&self, table: &Equipment) -> i32 {
        let taken: HashSet<i32> = self.state.customers.iter().filter(|c| c.table_id == table.id).map(|c| c.seat).collect();
        (0..table.seats).find(|s| !taken.contains(s)).unwrap_or(-1)
    }

    fn between(&mut self, min: i64, max: i64) -> i64 { min + (self.next_customer_random() * (max - min + 1) as f64) as i64 }

    /// xorshift64* in the snapshot, so a restored world makes the same choices as one that never stopped.
    fn next_customer_random(&mut self) -> f64 {
        let mut x = self.state.customer_random as u64;
        if x == 0 {
            x = 0x9E37_79B9_7F4A_7C15;
        }
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        self.state.customer_random = x as i64;
        ((x.wrapping_mul(2_685_821_657_736_338_717)) >> 11) as f64 * (1.0 / (1u64 << 53) as f64)
    }

    /// Customers at lot-linked competitors near a map point, for drawing only; never read by the simulation.
    pub fn crowd_near(&self, map_x: f32, map_z: f32, radius: i32) -> CrowdView {
        let mut view = CrowdView { revision: self.state.revision, restaurants: vec![] };
        if map_x.is_nan() || map_z.is_nan() {
            return view;
        }
        let mut near: HashMap<&str, usize> = HashMap::new();
        for competitor in &self.state.competitors {
            if competitor.lot_id.is_empty() || (competitor.map_x as f32 - map_x).abs() + (competitor.map_z as f32 - map_z).abs() > radius as f32 {
                continue;
            }
            near.insert(competitor.id.as_str(), view.restaurants.len());
            view.restaurants.push(CrowdRestaurant { competitor_id: competitor.id.clone(), lot_id: competitor.lot_id.clone(), servers: competitor.servers, seats: competitor.seats, customers: vec![] });
        }
        if near.is_empty() {
            return view;
        }
        for customer in &self.state.customers {
            let Some(&index) = near.get(customer.restaurant_id.as_str()) else { continue };
            let travelling = customer.state == CustomerState::Travelling;
            let walked_out = !customer.left_restaurant_id.is_empty();
            if travelling && customer.remaining_seconds > CROWD_TRAVEL_SECONDS && !walked_out {
                continue;
            }
            view.restaurants[index].customers.push(CrowdCustomer {
                id: customer.id.clone(), district_id: customer.district_id.clone(), appearance: customer.appearance, dine_in: customer.dine_in, state: customer.state,
                ticket: customer.ticket, remaining_seconds: if travelling { customer.remaining_seconds } else { 0 }, walked_out,
            });
        }
        view.restaurants.retain(|r| !r.customers.is_empty());
        view
    }
}

fn enqueue(diner: &mut DinerEntry, customer: &Customer, customers: &[Customer], positions: &HashMap<String, usize>) {
    let mine = positions.get(&customer.id).copied().unwrap_or(usize::MAX);
    let mut index = diner.queue.len();
    while index > 0 {
        let previous = &diner.queue[index - 1];
        let previous_position = positions.get(previous).copied().unwrap_or(0);
        let previous_ticket = customers.get(previous_position).filter(|c| &c.id == previous).map_or(i64::MIN, |c| c.ticket);
        if previous_ticket < customer.ticket || (previous_ticket == customer.ticket && previous_position < mine) {
            break;
        }
        index -= 1;
    }
    diner.queue.insert(index, customer.id.clone());
}

fn occupy_seat(diner: &mut DinerEntry, table: &str) {
    if table.is_empty() {
        return;
    }
    *diner.seats_used.entry(table.to_string()).or_default() += 1;
    diner.occupied_seats += 1;
}

fn release_seat(diner: &mut DinerEntry, table: &str) {
    if table.is_empty() {
        return;
    }
    if let Some(used) = diner.seats_used.get_mut(table) {
        *used -= 1;
        if *used <= 0 {
            diner.seats_used.remove(table);
        }
    }
    diner.occupied_seats -= 1;
}

fn adjust_reputation(record: &mut Diner, delta: i32) { record.reputation = (record.reputation + delta).clamp(-REPUTATION_LIMIT, REPUTATION_LIMIT); }
