// Conveyor belts and lifts (Unity GoodsWorld.Belts.cs). A placed belt is a one-cell, stable-ID site object bought from the
// build menu for the site company's cash (never an inventory item), with one location holding the single-unit lots riding on
// it; taking it up refunds that price. Belts move their items on the world clock, so a line runs whether or not anyone
// watches. A lift is a belt whose front end is one storey up or down.
use std::collections::{HashMap, HashSet};

use super::grid;
use super::rules::{self, ITEM_SPACING, SUB_STEPS_PER_SECOND, UNITS_PER_SECOND, UNITS_PER_TILE};
use super::state::*;
use super::company::{LEDGER_FURNISH, LEDGER_SELL_BACK};
use super::{inventory_location_id, CashNote, GoodsWorld, BELT_LOCATION_KIND, BELT_PRICE_CENTS, LIFT_PRICE_CENTS};

impl GoodsWorld {
    /// An empty cell takes a new belt charged to the site company; a cell that already holds a belt is turned to the
    /// requested direction at no cost.
    pub fn place_belt(&mut self, player: &str, request: &str, site_id: &str, x: i32, z: i32, direction: i32, level: i32) -> Outcome {
        self.place_conveyor(player, request, site_id, x, z, direction, level, 0, false)
    }

    pub fn place_lift(&mut self, player: &str, request: &str, site_id: &str, x: i32, z: i32, direction: i32, level: i32, lift: i32) -> Outcome {
        self.place_conveyor(player, request, site_id, x, z, direction, level, lift, true)
    }

    #[allow(clippy::too_many_arguments)]
    fn place_conveyor(&mut self, player: &str, request: &str, site_id: &str, x: i32, z: i32, direction: i32, level: i32, lift: i32, is_lift: bool) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        if !self.state.can_view(player, site_id) {
            return self.record(request, player, false, "forbidden", "");
        }
        if !(0..=3).contains(&direction) {
            return self.record(request, player, false, "invalid-rotation", "");
        }
        if !(-1..=1).contains(&lift) || is_lift != (lift != 0) {
            return self.record(request, player, false, "invalid-lift", "");
        }
        if let Some(existing) = self.state.belts.iter_mut().find(|b| b.site_id == site_id && b.level == level && b.cell_x == x && b.cell_z == z && b.lift == lift) {
            if existing.direction == direction {
                return self.record(request, player, true, "unchanged", "");
            }
            existing.direction = direction;
            return self.record(request, player, true, "rotated", "");
        }
        let problem = grid::cell_problem(&self.state, site_id, x, z, 1, 1, None, level, "")
            .or_else(|| if lift == 0 { None } else { grid::cell_problem(&self.state, site_id, x, z, 1, 1, None, level + lift, "") })
            .or_else(|| rules::belt_problem(&self.state, site_id, x, z, level, lift));
        if let Some(problem) = problem {
            return self.record(request, player, false, problem, "");
        }
        let price = if lift == 0 { BELT_PRICE_CENTS } else { LIFT_PRICE_CENTS };
        let Some(company) = self.company_of_site(site_id) else { return self.record(request, player, false, "no-company", "") };
        if self.cash_of(&company) < price {
            return self.record(request, player, false, "insufficient-funds", "");
        }
        let id = self.new_unique("belt-");
        self.try_debit(&company, price, CashNote::new(LEDGER_FURNISH, site_id).request(request).equipment(&id));
        let belt = Belt { id, site_id: site_id.to_string(), cell_x: x, cell_z: z, direction, level, lift };
        self.state.locations.push(Location {
            id: belt.location_id(), site_id: site_id.to_string(), kind: BELT_LOCATION_KIND.into(), capacity: UNITS_PER_TILE / ITEM_SPACING, refrigerated: false,
        });
        self.state.belts.push(belt);
        self.record(request, player, true, "placed", "")
    }

    /// Refunds the belt's price; every item riding it goes to the actor's inventory, or nothing changes.
    pub fn remove_belt(&mut self, player: &str, request: &str, belt_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(belt) = self.state.belts.iter().find(|b| b.id == belt_id).cloned() else { return self.record(request, player, false, "forbidden", "") };
        if !self.state.can_view(player, &belt.site_id) {
            return self.record(request, player, false, "forbidden", "");
        }
        let inventory = inventory_location_id(player);
        let riding: Vec<Lot> = self.state.lots.iter().filter(|l| l.location_id == belt.location_id()).cloned().collect();
        if !riding.is_empty() && self.state.location(&inventory).map(|l| l.site_id.as_str()) != Some(belt.site_id.as_str()) {
            return self.record(request, player, false, "no-inventory", "");
        }
        if riding.iter().any(|l| self.reserved(&l.id)) {
            return self.record(request, player, false, "reserved", "");
        }
        if !riding.is_empty() && !self.fits_all(&inventory, riding.iter()) {
            return self.record(request, player, false, "capacity", "");
        }
        let location = belt.location_id();
        for lot in &mut self.state.lots {
            if lot.location_id == location {
                lot.location_id = inventory.clone();
                lot.belt_position = 0;
            }
        }
        let price = if belt.lift == 0 { BELT_PRICE_CENTS } else { LIFT_PRICE_CENTS };
        if let Some(company) = self.company_of_site(&belt.site_id) {
            self.try_credit(&company, price, CashNote::new(LEDGER_SELL_BACK, &belt.site_id).request(request).equipment(&belt.id));
        }
        self.state.locations.retain(|l| l.id != location);
        self.state.belts.retain(|b| b.id != belt.id);
        self.record(request, player, true, "removed", "")
    }

    /// Puts exactly one unit of a lot onto a belt at the free position nearest its middle.
    pub fn place_on_belt(&mut self, player: &str, request: &str, lot_id: &str, belt_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let lot = self.state.lot(lot_id).cloned();
        let belt = self.state.belts.iter().find(|b| b.id == belt_id).cloned();
        let (Some(lot), Some(belt)) = (lot, belt) else { return self.record(request, player, false, "forbidden", "") };
        let Some(source) = self.state.location(&lot.location_id).cloned() else { return self.record(request, player, false, "forbidden", "") };
        if !self.state.can_view(player, &source.site_id) || lot.owner_id != source.site_id || source.site_id != belt.site_id || !self.state.can_view(player, &belt.site_id) {
            return self.record(request, player, false, "forbidden", "");
        }
        if source.kind == BELT_LOCATION_KIND {
            return self.record(request, player, false, "invalid-route", "");
        }
        if self.available(&lot) < 1 {
            return self.record(request, player, false, "quantity-or-reservation", "");
        }
        let occupied: Vec<i32> = self.state.lots.iter().filter(|l| l.location_id == belt.location_id()).map(|l| l.belt_position).collect();
        let Some(position) = rules::free_position(&occupied) else { return self.record(request, player, false, "belt-full", "") };
        let moved = self.move_lot_part(lot_id, &belt.location_id(), 1, None);
        if let Some(moved_lot) = self.state.lots.iter_mut().find(|l| l.id == moved) {
            moved_lot.belt_position = position;
        }
        self.record(request, player, true, "placed-on-belt", &moved)
    }

    /// Takes one riding item into the actor's inventory, keeping its ID and exposure.
    pub fn take_from_belt(&mut self, player: &str, request: &str, lot_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let lot = self.state.lot(lot_id).cloned();
        let belt = lot.as_ref().and_then(|l| self.state.belts.iter().find(|b| b.location_id() == l.location_id).cloned());
        let Some(belt) = belt.filter(|b| self.state.can_view(player, &b.site_id)) else {
            let reason = if lot.is_none() || self.state.belts.iter().any(|b| Some(b.location_id()) == lot.as_ref().map(|l| l.location_id.clone())) { "forbidden" } else { "not-on-belt" };
            return self.record(request, player, false, reason, "");
        };
        let lot = lot.expect("lot exists when its belt does");
        let inventory = inventory_location_id(player);
        if self.state.location(&inventory).map(|l| l.site_id.as_str()) != Some(belt.site_id.as_str()) {
            return self.record(request, player, false, "no-inventory", "");
        }
        if self.reserved(&lot.id) {
            return self.record(request, player, false, "reserved", "");
        }
        if !self.fits(&inventory, &lot.item_id, lot.spoiled, lot.quantity) {
            return self.record(request, player, false, "capacity", "");
        }
        if let Some(target) = self.state.lots.iter_mut().find(|l| l.id == lot.id) {
            target.location_id = inventory;
            target.belt_position = 0;
        }
        self.record(request, player, true, "taken-from-belt", &lot.id)
    }

    /// Moves riding items along their belts in sub-steps, downstream belts first and each belt's items front first, so an
    /// item only closes up on items that already moved and a compressed line stays compressed.
    pub(crate) fn move_belt_items(&mut self, seconds: i64) {
        if self.state.belts.is_empty() {
            return;
        }
        let belt_locations: HashMap<String, usize> = self.state.belts.iter().enumerate().map(|(i, b)| (b.location_id(), i)).collect();
        let riding: Vec<usize> = (0..self.state.lots.len()).filter(|&i| belt_locations.contains_key(&self.state.lots[i].location_id)).collect();
        if riding.is_empty() {
            return;
        }
        let belts = self.state.belts.clone();
        let mut sites: HashMap<&str, Vec<&Belt>> = HashMap::new();
        for belt in &belts {
            sites.entry(belt.site_id.as_str()).or_default().push(belt);
        }
        let cells: HashMap<&str, rules::BeltCells> = sites.iter().map(|(site, list)| (*site, rules::by_cell(list.iter().copied()))).collect();
        let index_of: HashMap<&str, usize> = belts.iter().enumerate().map(|(i, b)| (b.id.as_str(), i)).collect();
        let links: Vec<Option<(usize, i32)>> = belts.iter().map(|b| rules::link(&cells[b.site_id.as_str()], b).map(|(next, entry)| (index_of[next.id.as_str()], entry))).collect();
        let mut sorted: Vec<usize> = (0..belts.len()).collect();
        sorted.sort_by(|a, b| belts[*a].id.cmp(&belts[*b].id));
        let mut order = vec![];
        let mut visited = HashSet::new();
        for start in sorted {
            let mut chain = vec![];
            let mut current = Some(start);
            while let Some(index) = current {
                if !visited.insert(index) {
                    break;
                }
                chain.push(index);
                current = links[index].map(|(next, _)| next);
            }
            while let Some(index) = chain.pop() {
                order.push(index);
            }
        }
        let mut on_belt: Vec<Vec<usize>> = vec![vec![]; belts.len()];
        for &lot in &riding {
            on_belt[belt_locations[&self.state.lots[lot].location_id]].push(lot);
        }
        let step = UNITS_PER_SECOND / SUB_STEPS_PER_SECOND;
        let mut moved = HashSet::new();
        for _ in 0..seconds * SUB_STEPS_PER_SECOND as i64 {
            moved.clear();
            for &belt in &order {
                if on_belt[belt].is_empty() {
                    continue;
                }
                let link = links[belt];
                let mut items = on_belt[belt].clone();
                items.sort_by(|a, b| self.state.lots[*b].belt_position.cmp(&self.state.lots[*a].belt_position));
                for lot in items {
                    if !moved.insert(lot) {
                        continue;
                    }
                    let position = self.state.lots[lot].belt_position;
                    let ahead = on_belt[belt].iter().filter(|&&x| x != lot && self.state.lots[x].belt_position > position).map(|&x| self.state.lots[x].belt_position).min();
                    let limit = if let Some(ahead) = ahead {
                        ahead - ITEM_SPACING
                    } else if let Some((next, entry)) = link {
                        if entry == 0 {
                            match on_belt[next].iter().map(|&x| self.state.lots[x].belt_position).min() {
                                None => i32::MAX,
                                Some(first) => UNITS_PER_TILE + first - ITEM_SPACING,
                            }
                        } else if on_belt[next].iter().all(|&x| (self.state.lots[x].belt_position - entry).abs() >= ITEM_SPACING) {
                            i32::MAX
                        } else {
                            UNITS_PER_TILE - 1
                        }
                    } else {
                        rules::END_REST
                    };
                    let next_position = position.max((position + step).min(limit));
                    if next_position < UNITS_PER_TILE {
                        self.state.lots[lot].belt_position = next_position;
                        continue;
                    }
                    let (next, entry) = link.expect("only a linked belt lets items past its end");
                    on_belt[belt].retain(|&x| x != lot);
                    on_belt[next].push(lot);
                    self.state.lots[lot].location_id = belts[next].location_id();
                    self.state.lots[lot].belt_position = if entry == 0 { next_position - UNITS_PER_TILE } else { entry };
                }
            }
        }
    }
}
