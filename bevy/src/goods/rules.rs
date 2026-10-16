// Pure restaurant rules shared by the server simulation and the client's build-mode preview (Unity RestaurantRules), plus slot
// capacity (GoodsSlots) and conveyor rules (BeltRules).
// Walking: a site's ground cells are walkable unless a wall or a placed object-layer piece covers them. A piece is reached
// when a walkable cell beside its footprint can be walked to. Customers come from the lot's street edge (any edge of a dev
// site) and never pass a back door; a restaurant dock stands beside a back door's doorstep, which nothing else covers. A table
// seats customers only when they can walk to it from a register they order at, directly or through a chair beside it.
use std::collections::{HashMap, HashSet, VecDeque};

use super::grid::{self, footprint, overlaps};
use super::state::{Belt, Building, Equipment, Lot, Location, SiteLayout, Snapshot};
use super::{PropertyOffer, RecipeDefinition, COUNTER_KIND, DOCK_KIND, DOOR_STRUCTURE, PARTITION_STRUCTURE, RESTAURANT_KIND};

pub const AMBIENCE_CAP: i32 = 100;
pub const AMBIENCE_SCALE: f64 = 50.0;
pub const AMBIENCE_WEIGHT: f64 = 0.6;

/// Walkable ground cells of a site.
#[derive(Clone, Debug)]
pub struct Walk {
    pub width: i32,
    pub depth: i32,
    pub open: Vec<bool>,
}

impl Walk {
    pub fn get(&self, x: i32, z: i32) -> bool { x >= 0 && z >= 0 && x < self.width && z < self.depth && self.open[(x * self.depth + z) as usize] }
    fn close(&mut self, x: i32, z: i32) {
        if x >= 0 && z >= 0 && x < self.width && z < self.depth {
            self.open[(x * self.depth + z) as usize] = false;
        }
    }
}

pub type Cells = HashSet<(i32, i32)>;

/// Walkable ground of a site; None without a layout. block marks one more footprint taken; for customers back doors are walls.
pub fn walkable(state: &Snapshot, site_id: &str, block: Option<(i32, i32, i32, i32)>, customers: bool) -> Option<Walk> {
    let layout = state.layout_of(site_id)?;
    let mut walk = Walk { width: layout.width, depth: layout.depth, open: vec![true; (layout.width * layout.depth).max(0) as usize] };
    let buildings: Vec<&Building> = state.buildings.iter().filter(|b| b.site_id == site_id).collect();
    for building in &buildings {
        close_walls(building, &mut walk);
    }
    if customers {
        for building in &buildings {
            for (x, z) in grid::service_doors(building) {
                walk.close(x, z);
            }
        }
    }
    let close_rect = |walk: &mut Walk, cx: i32, cz: i32, w: i32, d: i32| {
        for x in cx.max(0)..(cx + w).min(walk.width) {
            for z in cz.max(0)..(cz + d).min(walk.depth) {
                walk.close(x, z);
            }
        }
    };
    for piece in grid::placed_objects(state, site_id) {
        let (w, d) = piece.footprint();
        close_rect(&mut walk, piece.cell_x, piece.cell_z, w, d);
    }
    if let Some((x, z, w, d)) = block {
        close_rect(&mut walk, x, z, w, d);
    }
    Some(walk)
}

fn close_walls(b: &Building, walk: &mut Walk) {
    let doors: HashSet<(i32, i32)> = b.doors.iter().map(|d| (d.x, d.z)).collect();
    let door_structures: HashSet<(i32, i32)> = b.structures.iter().filter(|s| s.kind == DOOR_STRUCTURE).map(|s| (s.x, s.z)).collect();
    if !b.free_walls && b.width > 0 && b.depth > 0 {
        let right = b.cell_x + b.width - 1;
        let top = b.cell_z + b.depth - 1;
        for x in b.cell_x..=right {
            for z in b.cell_z..=top {
                if (x == b.cell_x || x == right || z == b.cell_z || z == top) && !doors.contains(&(x, z)) {
                    walk.close(x, z);
                }
            }
        }
    }
    for s in b.structures.iter().filter(|s| s.kind == PARTITION_STRUCTURE) {
        if !door_structures.contains(&(s.x, s.z)) {
            walk.close(s.x, s.z);
        }
    }
}

/// Every walkable cell reached (four-way) from the walkable sources.
pub fn reached(walk: Option<&Walk>, sources: impl IntoIterator<Item = (i32, i32)>) -> Cells {
    let mut reached = HashSet::new();
    let Some(walk) = walk else { return reached };
    let mut queue = VecDeque::new();
    for cell in sources {
        if walk.get(cell.0, cell.1) && reached.insert(cell) {
            queue.push_back(cell);
        }
    }
    while let Some((x, z)) = queue.pop_front() {
        for next in [(x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1)] {
            if walk.get(next.0, next.1) && reached.insert(next) {
                queue.push_back(next);
            }
        }
    }
    reached
}

/// Steps from the nearest walkable source to every walkable cell reached.
pub fn steps(walk: Option<&Walk>, sources: impl IntoIterator<Item = (i32, i32)>) -> HashMap<(i32, i32), i32> {
    let mut steps = HashMap::new();
    let Some(walk) = walk else { return steps };
    let mut queue = VecDeque::new();
    for cell in sources {
        if walk.get(cell.0, cell.1) && !steps.contains_key(&cell) {
            steps.insert(cell, 0);
            queue.push_back(cell);
        }
    }
    while let Some((x, z)) = queue.pop_front() {
        let next_step = steps[&(x, z)] + 1;
        for next in [(x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1)] {
            if walk.get(next.0, next.1) && !steps.contains_key(&next) {
                steps.insert(next, next_step);
                queue.push_back(next);
            }
        }
    }
    steps
}

/// The cells along every edge of a site.
pub fn edge_cells(layout: &SiteLayout) -> Vec<(i32, i32)> {
    let mut cells = vec![];
    for x in 0..layout.width {
        cells.push((x, 0));
        cells.push((x, layout.depth - 1));
    }
    for z in 1..layout.depth - 1 {
        cells.push((0, z));
        cells.push((layout.width - 1, z));
    }
    cells
}

/// The lot's edge on its street access side; every edge for a site without a listed lot.
pub fn street_cells(layout: Option<&SiteLayout>, offer: Option<&PropertyOffer>) -> Vec<(i32, i32)> {
    let Some(layout) = layout else { return vec![] };
    let Some(offer) = offer else { return edge_cells(layout) };
    if offer.access_z >= offer.lot_z + offer.depth {
        (0..layout.width).map(|x| (x, layout.depth - 1)).collect()
    } else if offer.access_z < offer.lot_z {
        (0..layout.width).map(|x| (x, 0)).collect()
    } else if offer.access_x < offer.lot_x {
        (0..layout.depth).map(|z| (0, z)).collect()
    } else {
        (0..layout.depth).map(|z| (layout.width - 1, z)).collect()
    }
}

pub fn touches_rect(reached: &Cells, cx: i32, cz: i32, w: i32, d: i32) -> bool {
    (cx..cx + w).any(|x| reached.contains(&(x, cz - 1)) || reached.contains(&(x, cz + d)))
        || (cz..cz + d).any(|z| reached.contains(&(cx - 1, z)) || reached.contains(&(cx + w, z)))
}

pub fn touches(reached: &Cells, piece: &Equipment) -> bool {
    let (w, d) = piece.footprint();
    touches_rect(reached, piece.cell_x, piece.cell_z, w, d)
}

/// The cells customers reach on a site: from the lot's street edge, never through a back door.
pub fn customer_reach(state: &Snapshot, site_id: &str, offer: Option<&PropertyOffer>) -> Cells {
    let walk = walkable(state, site_id, None, true);
    reached(walk.as_ref(), street_cells(state.layout_of(site_id), offer))
}

pub fn serves_customers(reach: &Cells, piece: &Equipment) -> bool { piece.level == 0 && touches(reach, piece) }

/// The reached cells edge to edge with a piece's footprint.
pub fn cells_beside(reached: &Cells, piece: &Equipment) -> Vec<(i32, i32)> {
    let (w, d) = piece.footprint();
    let mut cells = vec![];
    for x in piece.cell_x..piece.cell_x + w {
        for c in [(x, piece.cell_z - 1), (x, piece.cell_z + d)] {
            if reached.contains(&c) {
                cells.push(c);
            }
        }
    }
    for z in piece.cell_z..piece.cell_z + d {
        for c in [(piece.cell_x - 1, z), (piece.cell_x + w, z)] {
            if reached.contains(&c) {
                cells.push(c);
            }
        }
    }
    cells
}

/// PROTOTYPE: seat-less furniture customers sit on beside a table.
pub fn is_seat_furniture(piece: &Equipment) -> bool { piece.kind.starts_with("rt-chair") || piece.kind == "rt-bench" || piece.kind == "rt-booth" }

/// True when two footprints touch edge to edge without overlapping.
pub fn beside(a: &Equipment, b: &Equipment) -> bool {
    let (aw, ad) = a.footprint();
    let (bw, bd) = b.footprint();
    let along_x = a.cell_x < b.cell_x + bw && b.cell_x < a.cell_x + aw;
    let along_z = a.cell_z < b.cell_z + bd && b.cell_z < a.cell_z + ad;
    (along_x && (a.cell_z + ad == b.cell_z || b.cell_z + bd == a.cell_z)) || (along_z && (a.cell_x + aw == b.cell_x || b.cell_x + bw == a.cell_x))
}

/// Per register ID, the ground-floor tables (in `placed` order) its customers can walk to.
pub fn tables_by_register(walk: Option<&Walk>, reach: &Cells, registers: &[Equipment], placed: &[Equipment]) -> HashMap<String, Vec<Equipment>> {
    let mut result = HashMap::new();
    let tables: Vec<&Equipment> = placed.iter().filter(|x| super::is_table(x) && x.level == 0).collect();
    let seats: Vec<&Equipment> = placed.iter().filter(|x| x.level == 0 && is_seat_furniture(x)).collect();
    let mut areas: Vec<Cells> = vec![];
    for register in registers {
        let spots = if register.level == 0 { cells_beside(reach, register) } else { vec![] };
        let mut area = areas.iter().position(|a| spots.iter().any(|s| a.contains(s)));
        if area.is_none() && !spots.is_empty() {
            areas.push(reached(walk, spots.clone()));
            area = Some(areas.len() - 1);
        }
        let list = match area {
            None => vec![],
            Some(index) => {
                let area = &areas[index];
                tables.iter().filter(|t| touches(area, t) || seats.iter().any(|s| beside(s, t) && touches(area, s))).map(|t| (*t).clone()).collect()
            }
        };
        result.insert(register.id.clone(), list);
    }
    result
}

/// The tables of a site customers can walk to from one of the given registers, in `placed` order.
pub fn seating_tables(walk: Option<&Walk>, reach: &Cells, registers: &[Equipment], placed: &[Equipment]) -> Vec<Equipment> {
    let valid: HashSet<String> = tables_by_register(walk, reach, registers, placed).into_values().flatten().map(|t| t.id).collect();
    placed.iter().filter(|x| valid.contains(&x.id)).cloned().collect()
}

/// A sale recipe sold at registers: the restaurant's menu.
pub fn is_menu_item(recipe: &RecipeDefinition) -> bool { recipe.is_sale() && recipe.station_kind == COUNTER_KIND }

pub fn is_restaurant_site(state: &Snapshot, site_id: &str) -> bool { state.buildings.iter().any(|b| b.site_id == site_id && b.kind == RESTAURANT_KIND) }

/// Restaurant placement rules on top of the grid's: doorsteps stay clear and docks touch a back door's doorstep.
pub fn placement_problem(state: &Snapshot, piece: &Equipment, cx: i32, cz: i32, rotation: i32, offer: Option<&PropertyOffer>) -> Option<&'static str> {
    doorstep_problem(state, piece, cx, cz, rotation).or_else(|| dock_problem(state, piece, cx, cz, rotation, offer))
}

pub fn doorstep_problem(state: &Snapshot, piece: &Equipment, cx: i32, cz: i32, rotation: i32) -> Option<&'static str> {
    if piece.kind == DOCK_KIND || !piece.layer.is_empty() || !is_restaurant_site(state, &piece.site_id) {
        return None;
    }
    let (w, d) = footprint(piece.width, piece.depth, rotation);
    if doorsteps(state, &piece.site_id).iter().any(|c| overlaps(c.0, c.1, 1, 1, cx, cz, w, d)) { Some("doorstep") } else { None }
}

pub fn belt_problem(state: &Snapshot, site_id: &str, cx: i32, cz: i32, level: i32, lift: i32) -> Option<&'static str> {
    if (level == 0 || level + lift == 0) && is_restaurant_site(state, site_id) && doorsteps(state, site_id).contains(&(cx, cz)) { Some("doorstep") } else { None }
}

/// True when a placed object-layer piece or a belt with an end on the ground covers the cell.
pub fn covered(state: &Snapshot, site_id: &str, x: i32, z: i32) -> bool {
    grid::placed_objects(state, site_id).any(|p| {
        let (w, d) = p.footprint();
        overlaps(x, z, 1, 1, p.cell_x, p.cell_z, w, d)
    }) || state.belts.iter().any(|b| b.site_id == site_id && b.cell_x == x && b.cell_z == z && (b.level == 0 || b.exit_level() == 0))
}

pub fn dock_problem(state: &Snapshot, dock: &Equipment, cx: i32, cz: i32, rotation: i32, offer: Option<&PropertyOffer>) -> Option<&'static str> {
    if dock.kind != DOCK_KIND || !is_restaurant_site(state, &dock.site_id) {
        return None;
    }
    let (w, d) = footprint(dock.width, dock.depth, rotation);
    let layout = state.layout_of(&dock.site_id);
    if !state.buildings.iter().filter(|b| b.site_id == dock.site_id && b.kind == RESTAURANT_KIND).any(|b| beside_back_door_rect(state, b, layout, cx, cz, w, d)) {
        return Some("not-beside-back-door");
    }
    let walk = walkable(state, &dock.site_id, Some((cx, cz, w, d)), false);
    let reached = reached(walk.as_ref(), street_cells(layout, offer));
    if touches_rect(&reached, cx, cz, w, d) { None } else { Some("no-street-access") }
}

/// True when a footprint lies wholly outside the building's interior and touches the open doorstep of one of its back doors.
#[allow(clippy::too_many_arguments)]
pub fn beside_back_door_rect(state: &Snapshot, b: &Building, layout: Option<&SiteLayout>, cx: i32, cz: i32, w: i32, d: i32) -> bool {
    let Some(layout) = layout else { return false };
    for x in cx..cx + w {
        for z in cz..cz + d {
            if grid::is_interior(b, x, z) {
                return false;
            }
        }
    }
    for (dx, dz) in grid::service_doors(b) {
        let Some(step) = grid::doorstep(b, dx, dz) else { continue };
        if !(step.0 >= 0 && step.1 >= 0 && step.0 < layout.width && step.1 < layout.depth && !grid::wall_at(state, &b.site_id, step.0, step.1)) {
            continue;
        }
        if overlaps(step.0, step.1, 1, 1, cx, cz, w, d) {
            continue;
        }
        let along_x = step.0 >= cx && step.0 < cx + w;
        let along_z = step.1 >= cz && step.1 < cz + d;
        if (along_x && (step.1 == cz - 1 || step.1 == cz + d)) || (along_z && (step.0 == cx - 1 || step.0 == cx + w)) {
            return true;
        }
    }
    false
}

pub fn beside_back_door_of(state: &Snapshot, b: &Building, dock: &Equipment) -> bool {
    let (w, d) = dock.footprint();
    beside_back_door_rect(state, b, state.layout_of(&dock.site_id), dock.cell_x, dock.cell_z, w, d)
}

/// A placed restaurant dock that touches a back door's doorstep.
pub fn beside_back_door(state: &Snapshot, dock: &Equipment) -> bool {
    state.buildings.iter().any(|b| b.site_id == dock.site_id && b.kind == RESTAURANT_KIND && beside_back_door_of(state, b, dock))
}

/// The doorsteps of a site's back doors that lie on the site grid.
pub fn doorsteps(state: &Snapshot, site_id: &str) -> Vec<(i32, i32)> {
    let Some(layout) = state.layout_of(site_id) else { return vec![] };
    let mut steps = vec![];
    for b in state.buildings.iter().filter(|b| b.site_id == site_id) {
        for (x, z) in grid::service_doors(b) {
            if let Some(step) = grid::doorstep(b, x, z) {
                if step.0 >= 0 && step.1 >= 0 && step.0 < layout.width && step.1 < layout.depth {
                    steps.push(step);
                }
            }
        }
    }
    steps
}

/// True when a walkable path reaches a placed dock from its lot's street edge.
pub fn reaches_street(state: &Snapshot, dock: &Equipment, offer: Option<&PropertyOffer>) -> bool {
    let walk = walkable(state, &dock.site_id, None, false);
    touches(&reached(walk.as_ref(), street_cells(state.layout_of(&dock.site_id), offer)), dock)
}

/// One restaurant's ambience score from the ambience points of everything placed on its site.
pub fn ambience(state: &Snapshot, site_id: &str) -> i32 {
    let points: i64 = state.equipment.iter().filter(|x| x.site_id == site_id && x.placed()).map(|x| x.ambience.max(0) as i64).sum();
    (AMBIENCE_CAP as f64 * (1.0 - (-(points as f64) / AMBIENCE_SCALE).exp())).round() as i32
}

// ------------------------------------------------------------------------------------------------------------ slots

pub fn slots(quantity: i64, max_stack: i32) -> i32 {
    let max_stack = max_stack.max(1) as i64;
    ((quantity.max(0) + max_stack - 1) / max_stack).min(i32::MAX as i64) as i32
}

/// Slots taken by lots: each (item, spoiled) stack takes ceil(quantity / max stack).
pub fn slots_used<'a>(lots: impl IntoIterator<Item = &'a Lot>, max_stack: impl Fn(&str) -> i32) -> i32 {
    let mut stacks: HashMap<(&str, bool), i64> = HashMap::new();
    for lot in lots {
        *stacks.entry((lot.item_id.as_str(), lot.spoiled)).or_default() += lot.quantity as i64;
    }
    stacks.iter().map(|((item, _), quantity)| slots(*quantity, max_stack(item))).sum()
}

/// Units of one stack that can still enter the location: its partly filled slot plus every free slot.
pub fn free_units(location: Option<&Location>, lots: &[Lot], item_id: &str, spoiled: bool, max_stack: impl Fn(&str) -> i32) -> i64 {
    let Some(location) = location else { return 0 };
    let here: Vec<&Lot> = lots.iter().filter(|x| x.location_id == location.id).collect();
    let own: i64 = here.iter().filter(|x| x.item_id == item_id && x.spoiled == spoiled).map(|x| x.quantity as i64).sum();
    let others = slots_used(here.iter().copied().filter(|x| x.item_id != item_id || x.spoiled != spoiled), &max_stack);
    ((location.capacity - others) as i64 * max_stack(item_id).max(1) as i64 - own).max(0)
}

// ------------------------------------------------------------------------------------------------------------ belts

pub const UNITS_PER_TILE: i32 = 240;
pub const ITEM_SPACING: i32 = 60;
pub const UNITS_PER_SECOND: i32 = 240;
pub const SUB_STEPS_PER_SECOND: i32 = 8;
pub const END_REST: i32 = UNITS_PER_TILE - ITEM_SPACING / 2;
pub const MIDDLE: i32 = UNITS_PER_TILE / 2;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum BeltShape { Straight, CurveFromLeft, CurveFromRight }

pub fn belt_step(direction: i32) -> (i32, i32) {
    match direction & 3 {
        0 => (0, 1),
        1 => (1, 0),
        2 => (0, -1),
        _ => (-1, 0),
    }
}

pub fn opposite(direction: i32) -> i32 { (direction + 2) & 3 }
pub fn right_of(direction: i32) -> i32 { (direction + 1) & 3 }
pub fn left_of(direction: i32) -> i32 { (direction + 3) & 3 }

pub type BeltCells<'a> = HashMap<(i32, i32, i32), &'a Belt>;

/// Every belt of one site by cell and level; a lift is found at both of its levels.
pub fn by_cell<'a>(belts: impl IntoIterator<Item = &'a Belt>) -> BeltCells<'a> {
    let mut cells = HashMap::new();
    for belt in belts {
        cells.insert((belt.cell_x, belt.cell_z, belt.level), belt);
        if belt.lift != 0 {
            cells.insert((belt.cell_x, belt.cell_z, belt.exit_level()), belt);
        }
    }
    cells
}

fn belt_at<'a>(cells: &BeltCells<'a>, x: i32, z: i32, level: i32, direction: i32) -> Option<&'a Belt> {
    let (dx, dz) = belt_step(direction);
    cells.get(&(x + dx, z + dz, level)).copied()
}

fn feeds(neighbour: Option<&Belt>, level: i32, direction: i32) -> bool { neighbour.is_some_and(|n| n.exit_level() == level && n.direction == direction) }

/// Straight when fed from behind, from both sides or from nowhere; curved when fed from exactly one side.
pub fn shape(cells: &BeltCells, x: i32, z: i32, level: i32, direction: i32, lift: i32) -> BeltShape {
    if lift != 0 || feeds(belt_at(cells, x, z, level, opposite(direction)), level, direction) {
        return BeltShape::Straight;
    }
    let from_left = feeds(belt_at(cells, x, z, level, left_of(direction)), level, right_of(direction));
    let from_right = feeds(belt_at(cells, x, z, level, right_of(direction)), level, left_of(direction));
    if from_left == from_right {
        BeltShape::Straight
    } else if from_left {
        BeltShape::CurveFromLeft
    } else {
        BeltShape::CurveFromRight
    }
}

pub fn shape_of(cells: &BeltCells, belt: &Belt) -> BeltShape { shape(cells, belt.cell_x, belt.cell_z, belt.level, belt.direction, belt.lift) }

/// The belt in front on this belt's exit level and the entry position there, unless it faces back or is a lift's exit end.
pub fn link<'a>(cells: &BeltCells<'a>, belt: &Belt) -> Option<(&'a Belt, i32)> {
    let next = belt_at(cells, belt.cell_x, belt.cell_z, belt.exit_level(), belt.direction)?;
    if next.level != belt.exit_level() || next.direction == opposite(belt.direction) {
        return None;
    }
    if next.direction == belt.direction {
        return Some((next, 0));
    }
    let next_shape = shape_of(cells, next);
    let curves_from_here = if next.direction == left_of(belt.direction) { next_shape == BeltShape::CurveFromLeft } else { next_shape == BeltShape::CurveFromRight };
    Some((next, if curves_from_here { 0 } else { MIDDLE }))
}

/// Nearest free position to the middle of a belt for a new item, or None when none keeps the spacing.
pub fn free_position(occupied: &[i32]) -> Option<i32> {
    let mut offset = 0;
    while offset <= MIDDLE - ITEM_SPACING / 2 {
        for position in [MIDDLE + offset, MIDDLE - offset] {
            if occupied.iter().all(|x| (x - position).abs() >= ITEM_SPACING) {
                return Some(position);
            }
        }
        offset += 10;
    }
    None
}
