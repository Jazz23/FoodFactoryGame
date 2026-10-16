// Trucks, routes and road driving (Unity GoodsWorld.Trucks.cs). Company vehicles repeat a player-made route between two docks
// on different sites of the same company. With the layout's road network registered, a truck drives the real roads one leg at
// a time, choosing the quickest way on at every junction from current traffic, and can find the next segment full and wait.
// Sites off the network keep an abstract Manhattan trip. Cargo is an ordinary location of kind "vehicle" on the reserved
// "road" site that nobody is granted; only the truck's own loading and unloading move goods in or out. A restaurant dock
// serves one truck at a time and is usable only while a walkable path from the street reaches it.
use std::collections::{HashMap, HashSet};
use std::sync::Arc;

use sha2::{Digest, Sha256};

use super::company::LEDGER_SELL_BACK;
use super::rules;
use super::state::*;
use super::*;
use crate::world::roads::{self as traffic, RoadLeg, RoadNetwork, RoadPoint};

pub const MAX_ALLOWED_ITEMS: usize = 16;
pub const TRUCK_RESALE_PERCENT: i64 = 50;

pub fn route_id_for(player: &str, request: &str) -> String { format!("route:{player}:{request}") }

/// Road distance in metres between two mapped sites (Manhattan); None if either has no map record.
pub fn road_metres(state: &Snapshot, from: &str, to: &str) -> Option<i64> {
    let a = state.site(from)?;
    let b = state.site(to)?;
    Some((a.map_x as i64 - b.map_x as i64).abs() + (a.map_z as i64 - b.map_z as i64).abs())
}

/// Whole seconds a truck needs between two sites on an abstract trip.
pub fn road_seconds(state: &Snapshot, from: &str, to: &str, speed: i32) -> i64 {
    if from == to {
        return 0;
    }
    let metres = road_metres(state, from, to).unwrap_or(0);
    ((metres + speed as i64 - 1) / speed.max(1) as i64).max(1)
}

pub fn route_of<'a>(state: &'a Snapshot, truck: &Truck) -> Option<&'a Route> {
    if truck.route_id.is_empty() { None } else { state.routes.iter().find(|r| r.id == truck.route_id) }
}

/// The truck offer a truck was bought from: same stats and name "<offer name> <n>"; the longest name wins.
pub fn truck_offer_of<'a>(truck: &Truck, offers: impl IntoIterator<Item = &'a TruckOffer>) -> Option<&'a TruckOffer> {
    let mut matches: Vec<&TruckOffer> = offers.into_iter().filter(|x| {
        !x.name.is_empty() && x.cargo_slots == truck.cargo_slots && x.speed_metres_per_second == truck.speed_metres_per_second
            && x.load_units_per_second == truck.load_units_per_second && (truck.name == x.name || truck.name.starts_with(&format!("{} ", x.name)))
    }).collect();
    matches.sort_by(|a, b| b.name.len().cmp(&a.name.len()).then_with(|| a.id.cmp(&b.id)));
    matches.into_iter().next()
}

pub fn truck_resale_cents<'a>(truck: &Truck, offers: impl IntoIterator<Item = &'a TruckOffer>) -> i64 {
    truck_offer_of(truck, offers).map_or(0, |o| o.price_cents * TRUCK_RESALE_PERCENT / 100)
}

pub fn truck_sale_problem(state: &Snapshot, truck: &Truck) -> Option<&'static str> {
    if truck.supplier {
        return Some("forbidden");
    }
    if !truck.order_id.is_empty() {
        return Some("on-order");
    }
    if state.lots.iter().any(|l| l.location_id == truck.cargo_location_id()) { Some("has-cargo") } else { None }
}

/// The dock a truck loads or unloads at: its route's pickup while loading, dropoff while unloading, or its order's dock.
pub fn working_dock_of(state: &Snapshot, truck: &Truck) -> Option<String> {
    if !truck.order_id.is_empty() {
        return if truck.state == TruckState::Unloading { state.farm_orders.iter().find(|o| o.id == truck.order_id).map(|o| o.dock_id.clone()) } else { None };
    }
    let route = route_of(state, truck)?;
    match truck.state {
        TruckState::Loading => Some(route.pickup_dock_id.clone()),
        TruckState::Unloading => Some(route.dropoff_dock_id.clone()),
        _ => None,
    }
}

fn cargo_filter_problem(items: &[String]) -> Option<&'static str> {
    let unique: HashSet<&String> = items.iter().collect();
    if items.len() > MAX_ALLOWED_ITEMS || items.iter().any(|x| x.trim().is_empty()) || unique.len() != items.len() { Some("invalid-cargo") } else { None }
}

fn route_problem(state: &Snapshot, company_id: &str, pickup_id: &str, dropoff_id: &str) -> Option<&'static str> {
    let pickup = state.equipment.iter().find(|x| x.id == pickup_id && x.kind == DOCK_KIND);
    let dropoff = state.equipment.iter().find(|x| x.id == dropoff_id && x.kind == DOCK_KIND);
    let (Some(pickup), Some(dropoff)) = (pickup, dropoff) else { return Some("invalid-dock") };
    let Some(company) = state.companies.iter().find(|c| c.id == company_id) else { return Some("forbidden") };
    if !company.site_ids.contains(&pickup.site_id) || !company.site_ids.contains(&dropoff.site_id) {
        return Some("forbidden");
    }
    if pickup.site_id == dropoff.site_id {
        return Some("same-site");
    }
    if road_metres(state, &pickup.site_id, &dropoff.site_id).is_none() { Some("no-road") } else { None }
}

/// For screens: about how many seconds a truck on the road still needs, at current city traffic without other trucks.
pub fn estimate_road_seconds(view: &Snapshot, network: &RoadNetwork, truck: &Truck) -> Option<i64> {
    if !truck.driving() || !truck.on_road() {
        return None;
    }
    let segment = network.segment_named(&truck.leg_segment_id)?;
    let Some(target) = view.site(&truck.destination_site_id).and_then(|s| network.locate(s.map_x, s.map_z)) else { return Some(truck.remaining_seconds) };
    let clock = view.clock_seconds;
    let drive = |index: usize, _forward: bool| traffic::drive_millis(&network.segments[index], network.segments[index].length, truck.speed_metres_per_second, traffic::load_percent(&network.segments[index], clock, 0));
    let Some(route) = network.route(RoadPoint { segment: segment as i32, offset: truck.leg_to }, target, drive, |i, f| traffic::junction_estimate_millis(network, i, f, clock)) else {
        return Some(truck.remaining_seconds);
    };
    let mut millis: i64 = route.iter().map(|leg| traffic::drive_millis(&network.segments[leg.segment as usize], leg.length(), truck.speed_metres_per_second, traffic::load_percent(&network.segments[leg.segment as usize], clock, 0))).sum();
    for leg in route.iter().take(route.len().saturating_sub(1)) {
        millis += traffic::junction_estimate_millis(network, leg.segment as usize, leg.forward(), clock);
    }
    Some(truck.remaining_seconds + (millis + 999) / 1000)
}

fn clear_next(truck: &mut Truck) {
    truck.next_segment_id.clear();
    truck.next_from = 0;
    truck.next_to = 0;
    truck.next_final = false;
}

fn clear_leg(truck: &mut Truck) {
    clear_next(truck);
    truck.leg_segment_id.clear();
    truck.leg_from = 0;
    truck.leg_to = 0;
    truck.leg_drive_seconds = 0;
    truck.leg_seconds = 0;
    truck.queued_seconds = 0;
}

/// A driving truck reaches its destination: it starts loading or unloading there, or parks.
fn arrive(truck: &mut Truck) {
    truck.site_id = std::mem::take(&mut truck.destination_site_id);
    truck.remaining_seconds = 0;
    truck.state = match truck.state {
        TruckState::ToPickup => TruckState::Loading,
        TruckState::ToDropoff => TruckState::Unloading,
        _ => TruckState::Parked,
    };
    clear_leg(truck);
}

impl GoodsWorld {
    pub fn register_truck_offer(&mut self, offer: TruckOffer) -> Result<()> {
        if offer.id.trim().is_empty() || offer.price_cents < 1 || offer.name.trim().is_empty() || offer.cargo_slots < 1 || offer.speed_metres_per_second < 1
            || offer.load_units_per_second < 1 || self.offer_exists(&offer.id)
        {
            return Err(format!("Invalid or duplicate truck offer {}.", offer.id));
        }
        self.content.truck_offers.insert(offer.id.clone(), offer);
        Ok(())
    }

    pub fn register_offer(&mut self, offer: PurchaseOffer) -> Result<()> {
        if offer.id.trim().is_empty() || offer.item_id.trim().is_empty() || offer.quantity < 1 || offer.price_cents < 1 || offer.spoil_after_seconds < 1 || self.offer_exists(&offer.id) {
            return Err(format!("Invalid or duplicate offer {}.", offer.id));
        }
        self.content.offers.insert(offer.id.clone(), offer);
        Ok(())
    }

    pub fn register_equipment_offer(&mut self, offer: EquipmentOffer) -> Result<()> {
        let e = &offer.equipment;
        if offer.id.trim().is_empty() || offer.price_cents < 1 || self.offer_exists(&offer.id) || e.kind.trim().is_empty() || e.width < 1 || e.depth < 1
            || e.input_capacity < 1 || e.output_capacity < 1 || e.seats < 0 || (e.kind == TABLE_KIND && e.seats < 1) || !grid::is_layer(&e.layer) || e.ambience < 0
        {
            return Err(format!("Invalid or duplicate equipment offer {}.", offer.id));
        }
        self.content.equipment_offers.insert(offer.id.clone(), offer);
        Ok(())
    }

    fn offer_exists(&self, id: &str) -> bool {
        self.content.offers.contains_key(id) || self.content.equipment_offers.contains_key(id) || self.content.truck_offers.contains_key(id)
    }

    pub fn bootstrap_site(&mut self, site: Site) -> Result<()> {
        if site.id.trim().is_empty() || site.id == ROAD_SITE_ID || self.state.site(&site.id).is_some() {
            return Err(format!("Invalid or duplicate site {}.", site.id));
        }
        self.state.sites.push(site);
        self.invalidate_diners();
        self.state.revision += 1;
        Ok(())
    }

    pub fn bootstrap_truck(&mut self, truck: Truck) -> Result<()> {
        if truck.id.trim().is_empty() || self.state.trucks.iter().any(|t| t.id == truck.id) || self.state.location(&truck.cargo_location_id()).is_some() || truck.cargo_slots < 1 {
            return Err(format!("Invalid or duplicate truck {}.", truck.id));
        }
        self.add_truck(truck);
        self.state.revision += 1;
        Ok(())
    }

    pub fn bootstrap_route(&mut self, route: Route) -> Result<()> {
        if route.id.trim().is_empty() || self.state.routes.iter().any(|r| r.id == route.id) {
            return Err(format!("Invalid or duplicate route {}.", route.id));
        }
        if let Some(problem) = route_problem(&self.state, &route.company_id, &route.pickup_dock_id, &route.dropoff_dock_id) {
            return Err(format!("Invalid route {}: {problem}.", route.id));
        }
        self.state.routes.push(route);
        self.state.revision += 1;
        Ok(())
    }

    pub(crate) fn add_truck(&mut self, truck: Truck) {
        self.state.locations.push(Location {
            id: truck.cargo_location_id(), site_id: ROAD_SITE_ID.into(), kind: VEHICLE_LOCATION_KIND.into(), capacity: truck.cargo_slots, refrigerated: truck.supplier,
        });
        self.state.trucks.push(truck);
    }

    pub fn register_roads(&mut self, network: Arc<RoadNetwork>) -> Result<()> {
        if self.content.roads.is_some() {
            return Err("Duplicate road network.".into());
        }
        for truck in self.state.trucks.iter().filter(|t| t.on_road()) {
            let ok = network.segment_named(&truck.leg_segment_id).is_some_and(|i| truck.leg_from.max(truck.leg_to) <= network.segments[i].length);
            if !ok {
                return Err(format!("Truck {} drives a leg that is not on the road network.", truck.id));
            }
        }
        self.content.roads = Some(network);
        self.site_points.clear();
        Ok(())
    }

    pub fn has_roads(&self) -> bool { self.content.roads.is_some() }

    fn truck_index(&self, id: &str) -> Option<usize> { self.state.trucks.iter().position(|t| t.id == id) }

    fn dock_site(&self, dock_id: &str) -> String { self.state.equipment.iter().find(|x| x.id == dock_id).map(|x| x.site_id.clone()).unwrap_or_default() }

    fn has_cargo(&self, index: usize) -> bool {
        let cargo = self.state.trucks[index].cargo_location_id();
        self.state.lots.iter().any(|l| l.location_id == cargo)
    }

    fn route_request_problem(&self, player: &str, company_id: &str, pickup_id: &str, dropoff_id: &str, items: &[String]) -> Option<&'static str> {
        let pickup = self.state.equipment.iter().find(|x| x.id == pickup_id && x.kind == DOCK_KIND);
        let dropoff = self.state.equipment.iter().find(|x| x.id == dropoff_id && x.kind == DOCK_KIND);
        let (Some(pickup), Some(dropoff)) = (pickup, dropoff) else { return Some("invalid-dock") };
        if !self.state.can_view(player, &pickup.site_id) || !self.state.can_view(player, &dropoff.site_id) {
            return Some("forbidden");
        }
        route_problem(&self.state, company_id, pickup_id, dropoff_id).or_else(|| cargo_filter_problem(items))
    }

    pub fn create_route(&mut self, player: &str, request: &str, pickup: &str, dropoff: &str, items: Vec<String>) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let company = self.state.equipment.iter().find(|x| x.id == pickup && x.kind == DOCK_KIND).and_then(|d| self.company_of_site(&d.site_id)).unwrap_or_default();
        if let Some(problem) = self.route_request_problem(player, &company, pickup, dropoff, &items) {
            return self.reject(request, player, problem);
        }
        self.state.routes.push(Route { id: route_id_for(player, request), company_id: company, pickup_dock_id: pickup.into(), dropoff_dock_id: dropoff.into(), allowed_item_ids: items });
        self.record(request, player, true, "route-created", "")
    }

    pub fn set_route(&mut self, player: &str, request: &str, route_id: &str, pickup: &str, dropoff: &str, items: Vec<String>) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(index) = self.state.routes.iter().position(|r| r.id == route_id) else { return self.reject(request, player, "forbidden") };
        let company = self.state.routes[index].company_id.clone();
        if let Some(problem) = self.route_request_problem(player, &company, pickup, dropoff, &items) {
            return self.reject(request, player, problem);
        }
        let route = &mut self.state.routes[index];
        route.pickup_dock_id = pickup.into();
        route.dropoff_dock_id = dropoff.into();
        route.allowed_item_ids = items;
        let trucks: Vec<usize> = (0..self.state.trucks.len()).filter(|&i| self.state.trucks[i].route_id == route_id).collect();
        for truck in trucks {
            self.dispatch(truck);
        }
        self.record(request, player, true, "route-set", "")
    }

    pub fn delete_route(&mut self, player: &str, request: &str, route_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(route) = self.state.routes.iter().find(|r| r.id == route_id).cloned() else { return self.reject(request, player, "forbidden") };
        if !self.state.can_view(player, &self.dock_site(&route.pickup_dock_id)) || !self.state.can_view(player, &self.dock_site(&route.dropoff_dock_id)) {
            return self.reject(request, player, "forbidden");
        }
        let trucks: Vec<usize> = (0..self.state.trucks.len()).filter(|&i| self.state.trucks[i].route_id == route_id).collect();
        for truck in trucks {
            self.park(truck);
        }
        self.state.routes.retain(|r| r.id != route_id);
        self.record(request, player, true, "route-deleted", "")
    }

    /// Puts a truck on a route of its company and sends it on; an empty route parks it.
    pub fn assign_truck(&mut self, player: &str, request: &str, truck_id: &str, route_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(index) = self.truck_index(truck_id).filter(|&i| !self.state.trucks[i].supplier) else { return self.reject(request, player, "forbidden") };
        if !self.state.trucks[index].order_id.is_empty() {
            return self.reject(request, player, "on-order");
        }
        if route_id.is_empty() {
            let company = self.state.companies.iter().find(|c| c.id == self.state.trucks[index].company_id);
            if !company.is_some_and(|c| c.site_ids.iter().any(|s| self.state.can_view(player, s))) {
                return self.reject(request, player, "forbidden");
            }
            self.park(index);
            return self.record(request, player, true, "parked", "");
        }
        let Some(route) = self.state.routes.iter().find(|r| r.id == route_id).cloned() else { return self.reject(request, player, "forbidden") };
        if route.company_id != self.state.trucks[index].company_id || !self.state.can_view(player, &self.dock_site(&route.pickup_dock_id))
            || !self.state.can_view(player, &self.dock_site(&route.dropoff_dock_id))
        {
            return self.reject(request, player, "forbidden");
        }
        if self.state.trucks[index].route_id != route.id {
            self.state.trucks[index].route_id = route.id;
            self.dispatch(index);
        }
        self.record(request, player, true, "assigned", "")
    }

    /// Sells an empty fleet truck back for half its model's list price.
    pub fn sell_truck(&mut self, player: &str, request: &str, truck_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(truck) = self.state.trucks.iter().find(|t| t.id == truck_id && !t.supplier).cloned() else { return self.reject(request, player, "forbidden") };
        let Some(company) = self.state.companies.iter().find(|c| c.id == truck.company_id).cloned() else { return self.reject(request, player, "forbidden") };
        if !company.site_ids.iter().any(|s| self.state.can_view(player, s)) {
            return self.reject(request, player, "forbidden");
        }
        if let Some(problem) = truck_sale_problem(&self.state, &truck) {
            return self.reject(request, player, problem);
        }
        let offer = truck_offer_of(&truck, self.content.truck_offers.values()).map(|o| o.id.clone()).unwrap_or_default();
        let cents = truck_resale_cents(&truck, self.content.truck_offers.values());
        self.state.farm_schedules.retain(|s| s.truck_id != truck.id);
        self.state.trucks.retain(|t| t.id != truck.id);
        let cargo = truck.cargo_location_id();
        self.state.locations.retain(|l| l.id != cargo);
        let site = if company.site_ids.contains(&truck.site_id) { truck.site_id.clone() } else { company.site_ids.first().cloned().unwrap_or_default() };
        if cents > 0 {
            self.try_credit(&company.id, cents, CashNote::new(LEDGER_SELL_BACK, &site).request(request).equipment(&truck.id).offer(&offer));
        }
        self.record_with(request, player, "truck-sold", -cents, &truck.id, "")
    }

    /// A parked truck stands where it is; one driving the roads goes on to the quicker of its route's sites and parks there.
    pub(crate) fn park(&mut self, index: usize) {
        let route = route_of(&self.state, &self.state.trucks[index]).cloned();
        {
            let truck = &mut self.state.trucks[index];
            truck.route_id.clear();
            truck.docked = false;
        }
        if self.state.trucks[index].driving() && self.state.trucks[index].on_road() {
            if let Some(route) = route {
                let pickup = self.dock_site(&route.pickup_dock_id);
                let dropoff = self.dock_site(&route.dropoff_dock_id);
                let nearer = if self.estimate_millis(index, &dropoff) < self.estimate_millis(index, &pickup) { dropoff } else { pickup };
                let truck = &mut self.state.trucks[index];
                if nearer != truck.destination_site_id {
                    clear_next(truck);
                }
                truck.destination_site_id = nearer;
            }
            self.state.trucks[index].state = TruckState::ToPark;
            return;
        }
        let truck = &mut self.state.trucks[index];
        truck.state = TruckState::Parked;
        truck.destination_site_id.clear();
        truck.remaining_seconds = 0;
        clear_leg(truck);
    }

    /// Sends a routed truck on: loaded to the dropoff, empty to the pickup.
    fn dispatch(&mut self, index: usize) {
        let Some(route) = route_of(&self.state, &self.state.trucks[index]).cloned() else { return };
        if self.has_cargo(index) {
            let site = self.dock_site(&route.dropoff_dock_id);
            self.drive(index, &site, TruckState::ToDropoff, TruckState::Unloading);
        } else {
            let site = self.dock_site(&route.pickup_dock_id);
            self.drive(index, &site, TruckState::ToPickup, TruckState::Loading);
        }
    }

    /// Sends a truck toward a site: by road when both sites are on the network, otherwise on an abstract trip.
    pub(crate) fn drive(&mut self, index: usize, destination: &str, driving: TruckState, arrived: TruckState) {
        self.state.trucks[index].docked = false;
        if self.state.trucks[index].driving() && self.state.trucks[index].on_road() {
            let truck = &mut self.state.trucks[index];
            truck.state = driving;
            if truck.destination_site_id != destination {
                clear_next(truck);
            }
            truck.destination_site_id = destination.to_string();
            return;
        }
        let from = self.site_point(&self.state.trucks[index].site_id.clone());
        let to = self.site_point(destination);
        if self.state.trucks[index].site_id != destination {
            if let (Some(start), Some(_)) = (from, to) {
                let segment_id = self.content.roads.as_ref().expect("a site point needs roads").segments[start.segment as usize].id.clone();
                let truck = &mut self.state.trucks[index];
                truck.state = driving;
                truck.destination_site_id = destination.to_string();
                truck.remaining_seconds = 0;
                truck.leg_segment_id = segment_id;
                truck.leg_from = start.offset;
                truck.leg_to = start.offset;
                truck.leg_drive_seconds = 0;
                truck.leg_seconds = 0;
                self.continue_on_road(index, true);
                return;
            }
        }
        let seconds = road_seconds(&self.state, &self.state.trucks[index].site_id, destination, self.state.trucks[index].speed_metres_per_second);
        let truck = &mut self.state.trucks[index];
        clear_leg(truck);
        truck.state = if seconds == 0 { arrived } else { driving };
        if seconds == 0 {
            truck.site_id = destination.to_string();
            truck.destination_site_id.clear();
        } else {
            truck.destination_site_id = destination.to_string();
        }
        truck.remaining_seconds = seconds;
    }

    /// Where a site meets the road network, or None without a network or when the site is off it.
    fn site_point(&mut self, site_id: &str) -> Option<RoadPoint> {
        let roads = self.content.roads.clone()?;
        if site_id.is_empty() {
            return None;
        }
        if let Some(point) = self.site_points.get(site_id) {
            return *point;
        }
        let point = self.state.site(site_id).and_then(|s| roads.locate(s.map_x, s.map_z));
        self.site_points.insert(site_id.to_string(), point);
        point
    }

    fn now(&self) -> i64 { self.truck_now.unwrap_or(self.state.clock_seconds) }

    /// Trucks on each direction of each segment, other than the one deciding.
    fn occupancy(&self, except: usize, roads: &RoadNetwork) -> HashMap<(usize, bool), i32> {
        let mut counts = HashMap::new();
        for (index, truck) in self.state.trucks.iter().enumerate() {
            if index == except || !truck.on_road() {
                continue;
            }
            let Some(segment) = roads.segment_named(&truck.leg_segment_id) else { continue };
            *counts.entry((segment, truck.leg_to >= truck.leg_from)).or_default() += 1;
        }
        counts
    }

    fn plan_route(&self, index: usize, roads: &RoadNetwork, from: RoadPoint, to: RoadPoint) -> Option<Vec<RoadLeg>> {
        let occupancy = self.occupancy(index, roads);
        let now = self.now();
        let speed = self.state.trucks[index].speed_metres_per_second;
        roads.route(
            from,
            to,
            |i, forward| traffic::drive_millis(&roads.segments[i], roads.segments[i].length, speed, traffic::load_percent(&roads.segments[i], now, occupancy.get(&(i, forward)).copied().unwrap_or(0))),
            |i, forward| traffic::junction_estimate_millis(roads, i, forward, now),
        )
    }

    /// At the end of its leg a truck arrives, or plans the quickest way on and enters its first leg if the segment admits it.
    fn continue_on_road(&mut self, index: usize, departing: bool) {
        let destination = self.state.trucks[index].destination_site_id.clone();
        let target = self.site_point(&destination);
        let roads = self.content.roads.clone();
        let (Some(roads), Some(target)) = (roads, target) else {
            self.finish_abstractly(index);
            return;
        };
        let Some(segment) = roads.segment_named(&self.state.trucks[index].leg_segment_id) else {
            self.finish_abstractly(index);
            return;
        };
        let here = RoadPoint { segment: segment as i32, offset: self.state.trucks[index].leg_to };
        let arrived = here == target || (roads.at_node(here).is_some() && roads.at_node(here) == roads.at_node(target));
        if arrived && !departing {
            arrive(&mut self.state.trucks[index]);
            return;
        }
        let now = self.now();
        let (leg, last) = match roads.segment_named(&self.state.trucks[index].next_segment_id) {
            Some(waiting) => {
                let truck = &self.state.trucks[index];
                (RoadLeg::new(waiting as i32, truck.next_from, truck.next_to), truck.next_final)
            }
            None => match self.plan_route(index, &roads, here, target) {
                Some(route) if !route.is_empty() => (route[0], route.len() == 1),
                _ => {
                    self.finish_abstractly(index);
                    return;
                }
            },
        };
        let occupancy = self.occupancy(index, &roads);
        let road = &roads.segments[leg.segment as usize];
        let others = occupancy.get(&(leg.segment as usize, leg.forward())).copied().unwrap_or(0);
        let load = traffic::load_percent(road, now, others + 1);
        let truck = &mut self.state.trucks[index];
        if !traffic::admits(load, others, truck.queued_seconds) {
            truck.remaining_seconds = 0;
            truck.next_segment_id = road.id.clone();
            truck.next_from = leg.from;
            truck.next_to = leg.to;
            truck.next_final = last;
            return;
        }
        clear_next(truck);
        let drive = ((traffic::drive_millis(road, leg.length(), truck.speed_metres_per_second, load) + 999) / 1000).max(1);
        let wait = if last { 0 } else { traffic::junction_wait_seconds(&roads, leg.segment as usize, leg.forward(), now + drive) };
        truck.leg_segment_id = road.id.clone();
        truck.leg_from = leg.from;
        truck.leg_to = leg.to;
        truck.leg_drive_seconds = drive;
        truck.leg_seconds = drive + wait;
        truck.remaining_seconds = drive + wait;
        truck.queued_seconds = 0;
    }

    fn finish_abstractly(&mut self, index: usize) {
        clear_leg(&mut self.state.trucks[index]);
        let truck = &self.state.trucks[index];
        let seconds = road_seconds(&self.state, &truck.site_id, &truck.destination_site_id, truck.speed_metres_per_second);
        if seconds == 0 {
            arrive(&mut self.state.trucks[index]);
        } else {
            self.state.trucks[index].remaining_seconds = seconds;
        }
    }

    fn estimate_millis(&mut self, index: usize, site_id: &str) -> i64 {
        let target = self.site_point(site_id);
        let Some(roads) = self.content.roads.clone() else { return i64::MAX };
        let (Some(target), Some(segment)) = (target, roads.segment_named(&self.state.trucks[index].leg_segment_id)) else { return i64::MAX };
        let here = RoadPoint { segment: segment as i32, offset: self.state.trucks[index].leg_to };
        let Some(route) = self.plan_route(index, &roads, here, target) else { return i64::MAX };
        let occupancy = self.occupancy(index, &roads);
        let now = self.now();
        let speed = self.state.trucks[index].speed_metres_per_second;
        route.iter().map(|leg| {
            let s = &roads.segments[leg.segment as usize];
            traffic::drive_millis(s, leg.length(), speed, traffic::load_percent(s, now, occupancy.get(&(leg.segment as usize, leg.forward())).copied().unwrap_or(0)))
        }).sum()
    }

    /// Trucks act one simulated second at a time in ID order; while every active truck drives a leg, time skips to the next
    /// leg end. A truck that cannot work waits until the next step.
    pub(crate) fn move_trucks(&mut self, seconds: i64) {
        if self.state.trucks.is_empty() {
            return;
        }
        let mut waiting: HashSet<String> = HashSet::new();
        self.usable_docks.clear();
        let mut left = seconds;
        while left > 0 {
            self.truck_second = self.state.clock_seconds - left;
            let mut ids: Vec<String> = self.state.trucks.iter().map(|t| t.id.clone()).collect();
            ids.sort();
            let working: HashSet<String> = self.state.trucks.iter().filter(|t| matches!(t.state, TruckState::Loading | TruckState::Unloading) && !waiting.contains(&t.id)).map(|t| t.id.clone()).collect();
            let driving: Vec<&Truck> = self.state.trucks.iter().filter(|t| t.driving()).collect();
            if working.is_empty() && driving.is_empty() {
                break;
            }
            let queued = driving.iter().any(|t| t.remaining_seconds == 0);
            let step = if !working.is_empty() || queued { 1 } else { left.min(driving.iter().map(|t| t.remaining_seconds).min().unwrap_or(left)) };
            self.truck_now = Some(self.truck_second + step);
            for id in &ids {
                let Some(index) = self.truck_index(id) else { continue };
                if self.state.trucks[index].driving() {
                    let truck = &mut self.state.trucks[index];
                    if truck.remaining_seconds > 0 {
                        truck.remaining_seconds -= step;
                        if truck.remaining_seconds > 0 {
                            continue;
                        }
                    } else {
                        truck.queued_seconds += step;
                    }
                    if self.state.trucks[index].on_road() {
                        self.continue_on_road(index, false);
                    } else {
                        arrive(&mut self.state.trucks[index]);
                    }
                } else if working.contains(id) {
                    if !self.take_dock(index) {
                        continue;
                    }
                    let worked = if self.state.trucks[index].state == TruckState::Loading { self.load_second(index) } else { self.unload_second(index) };
                    if !worked {
                        if let Some(index) = self.truck_index(id) {
                            self.state.trucks[index].docked = false;
                        }
                        waiting.insert(id.clone());
                    }
                }
            }
            let returned: Vec<String> = self.state.trucks.iter().filter(|t| t.supplier && t.state == TruckState::Parked).map(|t| t.id.clone()).collect();
            for id in returned {
                if let Some(index) = self.truck_index(&id) {
                    self.return_supplier_truck(index);
                }
            }
            left -= step;
        }
        self.truck_now = None;
    }

    /// One second at the pickup dock: loads allowed, unreserved goods, most exposed first; with nothing loaded and cargo
    /// aboard it leaves for the dropoff. False when it waits.
    fn load_second(&mut self, index: usize) -> bool {
        if !self.state.trucks[index].order_id.is_empty() {
            return self.load_order_second(index);
        }
        let Some(route) = route_of(&self.state, &self.state.trucks[index]).cloned() else { return false };
        let Some(dock) = self.placed_dock(&route.pickup_dock_id) else { return false };
        let truck = self.state.trucks[index].clone();
        let mut budget = truck.load_units_per_second;
        let mut candidates: Vec<Lot> = self.state.lots.iter()
            .filter(|l| l.location_id == dock.input_location_id() && l.owner_id == dock.site_id && (route.allowed_item_ids.is_empty() || route.allowed_item_ids.contains(&l.item_id)) && self.available(l) > 0)
            .cloned().collect();
        candidates.sort_by(|a, b| b.exposure_seconds.cmp(&a.exposure_seconds).then_with(|| a.id.cmp(&b.id)));
        for lot in candidates {
            let Some(current) = self.state.lot(&lot.id).cloned() else { continue };
            let take = (budget.min(self.available(&current)) as i64).min(self.free_units_for(&truck.cargo_location_id(), &current)) as i32;
            if take == 0 {
                continue;
            }
            let owner = current.owner_id.clone();
            self.move_units(&current.id, &truck.cargo_location_id(), take, &owner);
            budget -= take;
            if budget == 0 {
                break;
            }
        }
        if budget < truck.load_units_per_second {
            return true;
        }
        if !self.has_cargo(index) {
            return false;
        }
        let site = self.dock_site(&route.dropoff_dock_id);
        self.drive(index, &site, TruckState::ToDropoff, TruckState::Unloading);
        true
    }

    /// One second at the dropoff dock: unloads into the dock's incoming buffer, re-owned by that site.
    fn unload_second(&mut self, index: usize) -> bool {
        if !self.state.trucks[index].order_id.is_empty() {
            return self.unload_order_second(index);
        }
        let Some(route) = route_of(&self.state, &self.state.trucks[index]).cloned() else { return false };
        if !self.has_cargo(index) {
            let site = self.dock_site(&route.pickup_dock_id);
            self.drive(index, &site, TruckState::ToPickup, TruckState::Loading);
            return true;
        }
        let Some(dock) = self.placed_dock(&route.dropoff_dock_id) else { return false };
        let truck = self.state.trucks[index].clone();
        let mut budget = truck.load_units_per_second;
        let mut cargo: Vec<Lot> = self.state.lots.iter().filter(|l| l.location_id == truck.cargo_location_id()).cloned().collect();
        cargo.sort_by(|a, b| b.exposure_seconds.cmp(&a.exposure_seconds).then_with(|| a.id.cmp(&b.id)));
        for lot in cargo {
            let take = (budget.min(lot.quantity) as i64).min(self.free_units_for(&dock.output_location_id(), &lot)) as i32;
            if take == 0 {
                continue;
            }
            self.move_units(&lot.id, &dock.output_location_id(), take, &dock.site_id);
            budget -= take;
            if budget == 0 {
                break;
            }
        }
        if budget == truck.load_units_per_second {
            return false;
        }
        if !self.has_cargo(index) {
            let site = self.dock_site(&route.pickup_dock_id);
            self.drive(index, &site, TruckState::ToPickup, TruckState::Loading);
        }
        true
    }

    /// A placed dock a truck can use: on a restaurant's site it must also be reachable from the street.
    pub(crate) fn placed_dock(&mut self, dock_id: &str) -> Option<Equipment> {
        let dock = self.state.equipment.iter().find(|x| x.id == dock_id && x.kind == DOCK_KIND && x.placed())?.clone();
        if !rules::is_restaurant_site(&self.state, &dock.site_id) {
            return Some(dock);
        }
        let usable = match self.usable_docks.get(&dock.id) {
            Some(usable) => *usable,
            None => {
                let offer = self.offer_of_site(&dock.site_id);
                let usable = rules::reaches_street(&self.state, &dock, offer.as_ref());
                self.usable_docks.insert(dock.id.clone(), usable);
                usable
            }
        };
        if usable { Some(dock) } else { None }
    }

    /// True when the truck may work its dock this second: any dock off a restaurant site, or a free restaurant dock.
    fn take_dock(&mut self, index: usize) -> bool {
        if self.state.trucks[index].docked {
            return true;
        }
        let Some(dock_id) = working_dock_of(&self.state, &self.state.trucks[index]) else { return true };
        let Some(dock) = self.state.equipment.iter().find(|x| x.id == dock_id) else { return true };
        if !rules::is_restaurant_site(&self.state, &dock.site_id) {
            return true;
        }
        let taken = self.state.trucks.iter().enumerate().any(|(i, t)| {
            i != index && t.docked && matches!(t.state, TruckState::Loading | TruckState::Unloading) && working_dock_of(&self.state, t).as_deref() == Some(dock_id.as_str())
        });
        if taken {
            return false;
        }
        self.state.trucks[index].docked = true;
        true
    }

    /// Moves units of a lot, keeping its ID when all of it moves; a part joins an equivalent lot there or splits under an ID
    /// hashed from the lot, the destination and the truck second.
    pub(crate) fn move_units(&mut self, lot_id: &str, location_id: &str, quantity: i32, owner: &str) {
        let Some(index) = self.state.lots.iter().position(|l| l.id == lot_id) else { return };
        if quantity == self.state.lots[index].quantity {
            let lot = &mut self.state.lots[index];
            lot.location_id = location_id.to_string();
            lot.owner_id = owner.to_string();
            return;
        }
        self.state.lots[index].quantity -= quantity;
        let lot = self.state.lots[index].clone();
        let same = self.state.lots.iter().position(|x| {
            x.location_id == location_id && x.item_id == lot.item_id && x.owner_id == owner && x.exposure_seconds == lot.exposure_seconds
                && x.spoil_after_seconds == lot.spoil_after_seconds && x.spoiled == lot.spoiled && !self.reserved(&x.id)
        });
        if let Some(same) = same {
            self.state.lots[same].quantity += quantity;
            return;
        }
        let id = self.split_lot_id(&lot.id, location_id);
        self.state.lots.push(Lot { id, owner_id: owner.to_string(), location_id: location_id.to_string(), quantity, belt_position: 0, ..lot });
    }

    fn split_lot_id(&self, lot_id: &str, location_id: &str) -> String {
        let digest = Sha256::digest(format!("{lot_id}\n{location_id}\n{}", self.truck_second).as_bytes());
        let id: String = digest.iter().take(16).map(|b| format!("{b:02x}")).collect();
        let mut candidate = id.clone();
        let mut suffix = 1;
        while self.state.lots.iter().any(|l| l.id == candidate) {
            candidate = format!("{id}-{suffix}");
            suffix += 1;
        }
        candidate
    }

    /// A view carries its company's routes and fleet with their cargo, its sites' map records and other companies' trucks
    /// on the road (without route or cargo).
    pub(crate) fn view_logistics(&self, view: &mut Snapshot, site_id: &str) {
        let company = self.state.company_of_site(site_id);
        view.sites = self.state.sites.iter().filter(|s| s.id == site_id || company.is_some_and(|c| c.site_ids.contains(&s.id))).cloned().collect();
        view.routes = company.map(|c| self.state.routes.iter().filter(|r| r.company_id == c.id).cloned().collect()).unwrap_or_default();
        view.trucks = company.map(|c| self.state.trucks.iter().filter(|t| t.company_id == c.id).cloned().collect()).unwrap_or_default();
        view.road_trucks = self.state.trucks.iter().filter(|t| Some(t.company_id.as_str()) != company.map(|c| c.id.as_str()) && t.driving() && t.on_road()).cloned().map(|mut t| {
            t.route_id.clear();
            t
        }).collect();
        let cargo: HashSet<String> = view.trucks.iter().map(|t| t.cargo_location_id()).collect();
        view.locations.extend(self.state.locations.iter().filter(|l| cargo.contains(&l.id)).cloned());
        view.lots.extend(self.state.lots.iter().filter(|l| cargo.contains(&l.location_id)).cloned());
    }
}
