// Building shells, factory floors and restaurant shell editing (Unity GoodsWorld.Buildings.cs and .Shell.cs). A shell's
// perimeter cells are walls except its ground-floor doors; a factory can gain floors with one elevator shaft through them. A
// restaurant's owner reshapes its shell with orders that one pure planner turns into the building it would leave, the cents
// it charges and refunds; the build-mode preview and the server share it. Every order but a resize first turns a rectangular
// restaurant into free walls, so outer walls are then drawn and removed like interior ones.
use serde::{Deserialize, Serialize};

use super::grid::{self, overlaps};
use super::rules;
use super::state::*;
use super::company::{LEDGER_FLOOR, LEDGER_SHELL};
use super::*;

pub const WALL_STYLES: [&str; 4] = ["plaster", "brick", "wainscot", "tile"];
pub const DOOR_STYLES: [&str; 3] = ["panel", "glazed", "kitchen"];
pub const WINDOW_STYLES: [&str; 3] = ["picture", "mullioned", "hatch"];
pub const SHELL_FLOOR_CELL_CENTS: i64 = 2_000;
pub const SHELL_WALL_CELL_CENTS: i64 = 3_000;
pub const SHELL_DOOR_CENTS: i64 = 15_000;
pub const SHELL_WINDOW_CENTS: i64 = 25_000;
pub const SHELL_ORDER_FEE_CENTS: i64 = 10_000;
pub const MAX_SHELL_CELLS: usize = 400;

pub mod order_kind {
    pub const RESIZE: &str = "resize";
    pub const PARTITION: &str = "partition";
    pub const DOOR: &str = "door";
    pub const WINDOW: &str = "window";
    pub const REMOVE: &str = "remove";
    pub const WALL_FINISH: &str = "finish";
    pub const BACK_DOOR: &str = "backdoor";
}

/// What an owner asks the shell to become; the kind picks the fields used.
#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq)]
#[serde(default)]
pub struct ShellOrder {
    pub kind: String,
    pub building_id: String,
    pub x: i32,
    pub z: i32,
    pub width: i32,
    pub depth: i32,
    pub axis: i32,
    pub style: String,
    pub cells: Vec<GridCell>,
}

/// The planner's answer: the problem (None when the order is valid), the resulting building and what it costs.
#[derive(Clone, Debug, Default)]
pub struct ShellPlan {
    pub problem: Option<&'static str>,
    pub building: Option<Building>,
    pub charge_cents: i64,
    pub refund_cents: i64,
    pub added: Vec<Structure>,
    pub removed: Vec<Structure>,
}

impl ShellPlan {
    pub fn net_cents(&self) -> i64 { self.charge_cents - self.refund_cents }
}

/// A base price at a district price percent, rounded down, at least 1 cent.
pub fn scaled_cents(base: i64, percent: i32) -> i64 { (base * percent.max(1) as i64 / 100).max(1) }

/// Price of the next floor of a building: one charge per interior cell.
pub fn floor_price_cents(building: &Building, offer: Option<&FloorOffer>) -> i64 {
    offer.map_or(0, |o| (building.width - 2) as i64 * (building.depth - 2) as i64 * o.cents_per_cell)
}

fn fits_walls(b: &Building) -> bool {
    let walls: Vec<&Structure> = b.structures.iter().filter(|x| x.kind == PARTITION_STRUCTURE).collect();
    if walls.is_empty() {
        return false;
    }
    let min_x = walls.iter().map(|w| w.x).min().unwrap();
    let min_z = walls.iter().map(|w| w.z).min().unwrap();
    let max_x = walls.iter().map(|w| w.x).max().unwrap();
    let max_z = walls.iter().map(|w| w.z).max().unwrap();
    b.cell_x == min_x && b.cell_z == min_z && b.width == max_x - min_x + 1 && b.depth == max_z - min_z + 1
}

/// Both cells of a window are walls of one perimeter side, or both are interior walls without a door.
pub fn window_fits(b: &Building, window: &Structure) -> bool {
    let (x2, z2) = if window.axis == 0 { (window.x + 1, window.z) } else { (window.x, window.z + 1) };
    let perimeter_wall = |x: i32, z: i32| grid::is_door_cell(b, x, z) && !b.doors.iter().any(|d| d.x == x && d.z == z);
    let same_side = if window.axis == 0 { window.z == b.cell_z || window.z == b.cell_z + b.depth - 1 } else { window.x == b.cell_x || window.x == b.cell_x + b.width - 1 };
    if same_side && perimeter_wall(window.x, window.z) && perimeter_wall(x2, z2) {
        return true;
    }
    grid::is_wall(b, window.x, window.z, 0) && grid::is_wall(b, x2, z2, 0) && grid::is_partition(b, window.x, window.z) && grid::is_partition(b, x2, z2)
}

fn structure_problem(b: &Building) -> Option<&'static str> {
    if b.structures.is_empty() && b.wall_style.is_empty() {
        return None;
    }
    if b.kind != RESTAURANT_KIND || (!b.wall_style.is_empty() && !WALL_STYLES.contains(&b.wall_style.as_str())) {
        return Some("invalid-structure");
    }
    for piece in &b.structures {
        if piece.charged_cents < 0 {
            return Some("invalid-structure");
        }
        if !piece.role.is_empty() && (piece.role != SERVICE_DOOR_ROLE || piece.kind != DOOR_STRUCTURE || grid::doorstep(b, piece.x, piece.z).is_none()) {
            return Some("invalid-structure");
        }
        let valid = match piece.kind.as_str() {
            FLOOR_STRUCTURE => !b.free_walls && overlaps(piece.x, piece.z, 1, 1, b.cell_x, b.cell_z, b.width, b.depth),
            WALL_STRUCTURE => grid::on_perimeter(b, piece.x, piece.z),
            PARTITION_STRUCTURE => (b.free_walls || grid::is_interior(b, piece.x, piece.z)) && WALL_STYLES.contains(&piece.style.as_str()),
            DOOR_STRUCTURE => DOOR_STYLES.contains(&piece.style.as_str()) && (b.doors.iter().any(|d| d.x == piece.x && d.z == piece.z) || grid::is_partition(b, piece.x, piece.z)),
            WINDOW_STRUCTURE => WINDOW_STYLES.contains(&piece.style.as_str()) && (piece.axis == 0 || piece.axis == 1) && window_fits(b, piece),
            _ => false,
        };
        if !valid {
            return Some("invalid-structure");
        }
    }
    let mut seen = std::collections::HashSet::new();
    for piece in b.structures.iter().filter(|x| x.kind != WINDOW_STRUCTURE) {
        if !seen.insert((piece.kind.clone(), piece.x, piece.z)) {
            return Some("invalid-structure");
        }
    }
    let windows: Vec<&Structure> = b.structures.iter().filter(|x| x.kind == WINDOW_STRUCTURE).collect();
    for (i, window) in windows.iter().enumerate() {
        if windows.iter().enumerate().any(|(j, other)| i != j && (grid::window_covers(other, window.x, window.z) || grid::window_covers(window, other.x, other.z)))
            || b.structures.iter().any(|x| x.kind == DOOR_STRUCTURE && grid::window_covers(window, x.x, x.z))
        {
            return Some("invalid-structure");
        }
    }
    None
}

/// None when the shell is well formed and its walls and elevator are clear (everything except ID uniqueness).
pub fn building_problem(state: &Snapshot, b: &Building) -> Option<&'static str> {
    let Some(layout) = state.layout_of(&b.site_id) else { return Some("invalid-building") };
    if b.id.trim().is_empty()
        || (b.kind != RESTAURANT_KIND && b.kind != FACTORY_KIND)
        || (!b.free_walls && (b.width < MINIMUM_BUILDING_SIZE || b.depth < MINIMUM_BUILDING_SIZE))
        || (b.free_walls && (b.kind != RESTAURANT_KIND || !b.doors.is_empty() || !fits_walls(b)))
        || b.cell_x < 0 || b.cell_z < 0 || b.cell_x + b.width > layout.width || b.cell_z + b.depth > layout.depth
    {
        return Some("invalid-building");
    }
    if b.floors < 1 || (b.floors > 1 && b.kind != FACTORY_KIND) || (b.has_elevator() && !grid::is_interior(b, b.elevator_x, b.elevator_z)) {
        return Some("invalid-floors");
    }
    let mut doors = std::collections::HashSet::new();
    if b.doors.iter().any(|d| !grid::is_door_cell(b, d.x, d.z) || !doors.insert((d.x, d.z))) {
        return Some("invalid-door");
    }
    if let Some(problem) = structure_problem(b) {
        return Some(problem);
    }
    if state.buildings.iter().any(|x| x.id != b.id && x.site_id == b.site_id && overlaps(b.cell_x, b.cell_z, b.width, b.depth, x.cell_x, x.cell_z, x.width, x.depth)) {
        return Some("blocked");
    }
    for piece in state.equipment.iter().filter(|x| x.site_id == b.site_id && x.placed() && x.layer.is_empty()) {
        let (w, d) = piece.footprint();
        if grid::covers_wall(b, piece.cell_x, piece.cell_z, w, d) || grid::covers_shaft(b, piece.cell_x, piece.cell_z, w, d, piece.level) {
            return Some("blocked");
        }
    }
    if state.belts.iter().any(|x| {
        x.site_id == b.site_id
            && (grid::covers_wall(b, x.cell_x, x.cell_z, 1, 1) || grid::covers_shaft(b, x.cell_x, x.cell_z, 1, 1, x.level) || grid::covers_shaft(b, x.cell_x, x.cell_z, 1, 1, x.exit_level()))
    }) {
        return Some("blocked");
    }
    None
}

/// Turns a rectangular restaurant into free walls: each perimeter cell becomes a wall record, each perimeter door a door record.
fn to_free_walls(b: &mut Building, plan: &mut ShellPlan) {
    let style = if b.wall_style.is_empty() { WALL_STYLES[0].to_string() } else { b.wall_style.clone() };
    let mut perimeter = vec![];
    for x in b.cell_x..b.cell_x + b.width {
        for z in b.cell_z..b.cell_z + b.depth {
            if !grid::on_perimeter(b, x, z) {
                continue;
            }
            let paid = b.structures.iter().find(|s| s.kind == WALL_STRUCTURE && s.x == x && s.z == z).map_or(0, |s| s.charged_cents);
            perimeter.push(Structure { kind: PARTITION_STRUCTURE.into(), x, z, style: style.clone(), charged_cents: paid, ..Default::default() });
        }
    }
    for door in b.doors.clone() {
        if !b.structures.iter().any(|s| s.kind == DOOR_STRUCTURE && s.x == door.x && s.z == door.z) {
            b.structures.push(Structure { kind: DOOR_STRUCTURE.into(), x: door.x, z: door.z, style: DOOR_STYLES[0].into(), ..Default::default() });
        }
    }
    b.structures.retain(|s| s.kind != WALL_STRUCTURE);
    let floors: Vec<Structure> = b.structures.iter().filter(|s| s.kind == FLOOR_STRUCTURE).cloned().collect();
    b.structures.retain(|s| s.kind != FLOOR_STRUCTURE);
    plan.removed.extend(floors);
    b.structures.extend(perimeter);
    b.doors.clear();
    b.wall_style = style;
    b.free_walls = true;
}

/// A wall cell joined to walls along both axes, where a doorway would cut a wall run off.
pub(crate) fn is_corner(b: &Building, x: i32, z: i32) -> bool {
    (grid::is_partition(b, x + 1, z) || grid::is_partition(b, x - 1, z)) && (grid::is_partition(b, x, z + 1) || grid::is_partition(b, x, z - 1))
}

fn inside(b: &Building, x: i32, z: i32) -> bool { overlaps(x, z, 1, 1, b.cell_x, b.cell_z, b.width, b.depth) }

fn project(from: &Building, to: &Building, x: i32, z: i32) -> (i32, i32) {
    if z == from.cell_z {
        (x, to.cell_z)
    } else if z == from.cell_z + from.depth - 1 {
        (x, to.cell_z + to.depth - 1)
    } else if x == from.cell_x {
        (to.cell_x, z)
    } else {
        (to.cell_x + to.width - 1, z)
    }
}

/// Pure: what the order would do to the current state. Everything but cash and rights is checked here.
pub fn plan_shell(state: &Snapshot, order: &ShellOrder, price_percent: i32) -> ShellPlan {
    let mut plan = ShellPlan::default();
    let fail = |mut plan: ShellPlan, problem: &'static str| {
        plan.problem = Some(problem);
        plan.building = None;
        plan
    };
    let Some(original) = state.buildings.iter().find(|x| x.id == order.building_id) else { return fail(plan, "forbidden") };
    if original.kind != RESTAURANT_KIND {
        return fail(plan, "not-a-restaurant");
    }
    let mut current = original.clone();
    let mut next = original.clone();
    let add = |next: &mut Building, plan: &mut ShellPlan, kind: &str, x: i32, z: i32, base: i64, style: &str, axis: i32| -> usize {
        let piece = Structure { kind: kind.into(), x, z, axis, style: style.into(), charged_cents: scaled_cents(base, price_percent), role: String::new() };
        next.structures.push(piece.clone());
        plan.added.push(piece);
        next.structures.len() - 1
    };
    fn drop(next: &mut Building, plan: &mut ShellPlan, matches: impl Fn(&Structure) -> bool) {
        if let Some(index) = next.structures.iter().position(|s| matches(s)) {
            plan.removed.push(next.structures.remove(index));
        }
    }
    let mut cells: Vec<(i32, i32)> = order.cells.iter().map(|c| (c.x, c.z)).collect();
    let lot = state.layout_of(&original.site_id).cloned();
    if order.kind != order_kind::RESIZE && !next.free_walls {
        to_free_walls(&mut next, &mut plan);
        current = next.clone();
    }
    match order.kind.as_str() {
        order_kind::RESIZE => {
            if current.free_walls {
                return fail(plan, "invalid-order");
            }
            if order.width < MINIMUM_BUILDING_SIZE || order.depth < MINIMUM_BUILDING_SIZE {
                return fail(plan, "too-small");
            }
            let Some(layout) = &lot else { return fail(plan, "out-of-bounds") };
            if order.x < 0 || order.z < 0 || order.x + order.width > layout.width || order.z + order.depth > layout.depth {
                return fail(plan, "out-of-bounds");
            }
            if order.x == current.cell_x && order.z == current.cell_z && order.width == current.width && order.depth == current.depth {
                return fail(plan, "unchanged");
            }
            next.cell_x = order.x;
            next.cell_z = order.z;
            next.width = order.width;
            next.depth = order.depth;
            let outside: Vec<(i32, i32)> = next.structures.iter().filter(|s| s.kind == PARTITION_STRUCTURE && !grid::is_interior(&next, s.x, s.z)).map(|s| (s.x, s.z)).collect();
            for (px, pz) in outside {
                loop {
                    let before = next.structures.len();
                    drop(&mut next, &mut plan, |s| (s.kind == DOOR_STRUCTURE && s.x == px && s.z == pz) || (s.kind == WINDOW_STRUCTURE && grid::window_covers(s, px, pz)));
                    if next.structures.len() == before {
                        break;
                    }
                }
                drop(&mut next, &mut plan, |s| s.kind == PARTITION_STRUCTURE && s.x == px && s.z == pz);
            }
            let mut doors = vec![];
            for door in &current.doors {
                let (x, z) = project(&current, &next, door.x, door.z);
                let record = next.structures.iter().position(|s| s.kind == DOOR_STRUCTURE && s.x == door.x && s.z == door.z);
                if grid::is_door_cell(&next, x, z) {
                    doors.push(GridCell { x, z });
                    if let Some(record) = record {
                        next.structures[record].x = x;
                        next.structures[record].z = z;
                    }
                } else if let Some(record) = record {
                    plan.removed.push(next.structures.remove(record));
                }
            }
            next.doors = doors;
            let windows: Vec<usize> = (0..next.structures.len()).filter(|&i| next.structures[i].kind == WINDOW_STRUCTURE && grid::on_perimeter(&current, next.structures[i].x, next.structures[i].z)).collect();
            let mut gone = vec![];
            for index in windows {
                let window = next.structures[index].clone();
                let (x, z) = project(&current, &next, window.x, window.z);
                let moved = Structure { kind: WINDOW_STRUCTURE.into(), x, z, axis: window.axis, ..Default::default() };
                if window_fits(&next, &moved) {
                    next.structures[index].x = x;
                    next.structures[index].z = z;
                } else {
                    gone.push(index);
                }
            }
            for index in gone.into_iter().rev() {
                plan.removed.push(next.structures.remove(index));
            }
            let next_copy = next.clone();
            let mut kept = vec![];
            for piece in std::mem::take(&mut next.structures) {
                let stale = (piece.kind == FLOOR_STRUCTURE && !inside(&next_copy, piece.x, piece.z)) || (piece.kind == WALL_STRUCTURE && !grid::on_perimeter(&next_copy, piece.x, piece.z));
                if stale {
                    plan.removed.push(piece);
                } else {
                    kept.push(piece);
                }
            }
            next.structures = kept;
            for x in next.cell_x..next.cell_x + next.width {
                for z in next.cell_z..next.cell_z + next.depth {
                    if !inside(&current, x, z) {
                        add(&mut next, &mut plan, FLOOR_STRUCTURE, x, z, SHELL_FLOOR_CELL_CENTS, "", 0);
                    }
                    if grid::on_perimeter(&next_copy, x, z) && !grid::on_perimeter(&current, x, z) {
                        add(&mut next, &mut plan, WALL_STRUCTURE, x, z, SHELL_WALL_CELL_CENTS, "", 0);
                    }
                }
            }
        }
        order_kind::PARTITION => {
            if !WALL_STYLES.contains(&order.style.as_str()) {
                return fail(plan, "invalid-style");
            }
            let unique: std::collections::HashSet<_> = cells.iter().collect();
            if cells.is_empty() || cells.len() > MAX_SHELL_CELLS || unique.len() != cells.len() {
                return fail(plan, "invalid-cell");
            }
            for &(x, z) in &cells {
                let in_lot = lot.as_ref().is_some_and(|l| x >= 0 && z >= 0 && x < l.width && z < l.depth);
                if !in_lot || grid::is_partition(&current, x, z) {
                    return fail(plan, "invalid-cell");
                }
                add(&mut next, &mut plan, PARTITION_STRUCTURE, x, z, SHELL_WALL_CELL_CENTS, &order.style, 0);
            }
        }
        order_kind::DOOR | order_kind::BACK_DOOR => {
            if !DOOR_STYLES.contains(&order.style.as_str()) {
                return fail(plan, "invalid-style");
            }
            if grid::window_at(&current, order.x, order.z).is_some() || grid::is_door(&current, order.x, order.z) {
                return fail(plan, "invalid-cell");
            }
            let back = order.kind == order_kind::BACK_DOOR;
            if back {
                let step = grid::doorstep(&current, order.x, order.z);
                let ok = step.is_some_and(|s| lot.as_ref().is_some_and(|l| s.0 >= 0 && s.1 >= 0 && s.0 < l.width && s.1 < l.depth));
                if !ok {
                    return fail(plan, "invalid-cell");
                }
            }
            if grid::is_door_cell(&current, order.x, order.z) {
                next.doors.push(GridCell { x: order.x, z: order.z });
            } else if !grid::is_partition(&current, order.x, order.z) || is_corner(&current, order.x, order.z) {
                return fail(plan, "invalid-cell");
            }
            let index = add(&mut next, &mut plan, DOOR_STRUCTURE, order.x, order.z, SHELL_DOOR_CENTS, &order.style, 0);
            if back {
                next.structures[index].role = SERVICE_DOOR_ROLE.into();
                if let Some(last) = plan.added.last_mut() {
                    last.role = SERVICE_DOOR_ROLE.into();
                }
            }
        }
        order_kind::WINDOW => {
            if !WINDOW_STYLES.contains(&order.style.as_str()) {
                return fail(plan, "invalid-style");
            }
            let window = Structure { kind: WINDOW_STRUCTURE.into(), x: order.x, z: order.z, axis: order.axis, ..Default::default() };
            let (x2, z2) = if order.axis == 0 { (order.x + 1, order.z) } else { (order.x, order.z + 1) };
            if (order.axis != 0 && order.axis != 1) || !window_fits(&current, &window) || grid::window_at(&current, order.x, order.z).is_some() || grid::window_at(&current, x2, z2).is_some() {
                return fail(plan, "invalid-cell");
            }
            add(&mut next, &mut plan, WINDOW_STRUCTURE, order.x, order.z, SHELL_WINDOW_CENTS, &order.style, order.axis);
        }
        order_kind::REMOVE => {
            if cells.is_empty() || cells.len() > MAX_SHELL_CELLS {
                return fail(plan, "invalid-cell");
            }
            let mut seen = std::collections::HashSet::new();
            cells.retain(|c| seen.insert(*c));
            for (x, z) in cells {
                if plan.removed.iter().any(|s| s.kind == WINDOW_STRUCTURE && grid::window_covers(s, x, z)) {
                    continue;
                }
                let window = next.structures.iter().position(|s| s.kind == WINDOW_STRUCTURE && grid::window_covers(s, x, z));
                let door_record = next.structures.iter().position(|s| s.kind == DOOR_STRUCTURE && s.x == x && s.z == z);
                let perimeter_door = next.doors.iter().position(|d| d.x == x && d.z == z);
                let partition = next.structures.iter().position(|s| s.kind == PARTITION_STRUCTURE && s.x == x && s.z == z);
                if let Some(index) = window {
                    plan.removed.push(next.structures.remove(index));
                } else if perimeter_door.is_some() || door_record.is_some() {
                    if let Some(index) = perimeter_door {
                        next.doors.remove(index);
                    }
                    match door_record {
                        Some(index) => plan.removed.push(next.structures.remove(index)),
                        None => plan.removed.push(Structure { kind: DOOR_STRUCTURE.into(), x, z, ..Default::default() }),
                    }
                } else if let Some(index) = partition {
                    plan.removed.push(next.structures.remove(index));
                } else {
                    return fail(plan, "invalid-cell");
                }
            }
        }
        order_kind::WALL_FINISH => {
            if !WALL_STYLES.contains(&order.style.as_str()) {
                return fail(plan, "invalid-style");
            }
            if cells.len() > MAX_SHELL_CELLS {
                return fail(plan, "invalid-cell");
            }
            let walls: Vec<Option<usize>> = if cells.is_empty() {
                (0..next.structures.len()).filter(|&i| next.structures[i].kind == PARTITION_STRUCTURE).map(Some).collect()
            } else {
                let mut seen = std::collections::HashSet::new();
                cells.iter().filter(|c| seen.insert(**c)).map(|c| next.structures.iter().position(|s| s.kind == PARTITION_STRUCTURE && s.x == c.0 && s.z == c.1)).collect()
            };
            if walls.iter().any(|w| w.is_none()) {
                return fail(plan, "invalid-cell");
            }
            let walls: Vec<usize> = walls.into_iter().flatten().collect();
            if walls.iter().all(|&i| next.structures[i].style == order.style) && (!cells.is_empty() || next.wall_style == order.style) {
                return fail(plan, "unchanged");
            }
            for i in walls {
                next.structures[i].style = order.style.clone();
            }
            if cells.is_empty() {
                next.wall_style = order.style.clone();
            }
        }
        _ => return fail(plan, "invalid-order"),
    }
    if next.free_walls {
        let walls: Vec<&Structure> = next.structures.iter().filter(|s| s.kind == PARTITION_STRUCTURE).collect();
        if walls.is_empty() {
            return fail(plan, "no-walls");
        }
        let (min_x, min_z) = (walls.iter().map(|w| w.x).min().unwrap(), walls.iter().map(|w| w.z).min().unwrap());
        let (max_x, max_z) = (walls.iter().map(|w| w.x).max().unwrap(), walls.iter().map(|w| w.z).max().unwrap());
        next.cell_x = min_x;
        next.cell_z = min_z;
        next.width = max_x - min_x + 1;
        next.depth = max_z - min_z + 1;
    }
    if next.doors.is_empty() && !next.structures.iter().any(|s| s.kind == DOOR_STRUCTURE) {
        return fail(plan, "no-door");
    }
    if !grid::service_doors(&current).is_empty() && grid::service_doors(&next).is_empty() {
        return fail(plan, "no-back-door");
    }
    if !grid::customer_doors(&current).is_empty() && grid::customer_doors(&next).is_empty() {
        return fail(plan, "no-customer-door");
    }
    let old_steps: Vec<Option<(i32, i32)>> = grid::service_doors(&current).iter().map(|d| grid::doorstep(&current, d.0, d.1)).collect();
    if grid::service_doors(&next).iter().filter_map(|d| grid::doorstep(&next, d.0, d.1)).any(|step| !old_steps.contains(&Some(step)) && rules::covered(state, &current.site_id, step.0, step.1)) {
        return fail(plan, "doorstep");
    }
    let with = |building: &Building| Snapshot {
        world_id: state.world_id.clone(),
        site_layouts: state.site_layouts.clone(),
        equipment: state.equipment.clone(),
        belts: state.belts.clone(),
        buildings: state.buildings.iter().map(|x| if x.id == building.id { building.clone() } else { x.clone() }).collect(),
        ..Default::default()
    };
    let with_next = with(&next);
    let with_current = with(&current);
    if state.equipment.iter().any(|x| x.site_id == current.site_id && x.kind == DOCK_KIND && x.placed() && rules::beside_back_door_of(&with_current, &current, x) && !rules::beside_back_door_of(&with_next, &next, x)) {
        return fail(plan, "dock-attached");
    }
    if let Some(first) = plan.added.first_mut() {
        let fee = scaled_cents(SHELL_ORDER_FEE_CENTS, price_percent);
        first.charged_cents += fee;
        let (kind, x, z) = (first.kind.clone(), first.x, first.z);
        if let Some(stored) = next.structures.iter_mut().find(|s| s.kind == kind && s.x == x && s.z == z) {
            stored.charged_cents += fee;
        }
    }
    plan.charge_cents = plan.added.iter().map(|s| s.charged_cents).sum();
    plan.refund_cents = plan.removed.iter().map(|s| s.charged_cents).sum();
    if let Some(problem) = building_problem(&with_next, &next) {
        return fail(plan, if problem == "invalid-building" { "out-of-bounds" } else { problem });
    }
    if state.equipment.iter().any(|x| x.site_id == current.site_id && x.placed() && !x.layer.is_empty() && grid::placement_problem(&with_next, x, x.cell_x, x.cell_z, x.rotation, x.level).is_some()) {
        return fail(plan, "blocked");
    }
    plan.building = Some(next);
    plan
}

impl GoodsWorld {
    pub fn register_floor_offer(&mut self, offer: FloorOffer) -> Result<()> {
        if offer.cents_per_cell < 1 || offer.max_floors < 2 || self.content.floor_offer.is_some() {
            return Err("Invalid or duplicate floor offer.".into());
        }
        self.content.floor_offer = Some(offer);
        Ok(())
    }

    pub fn bootstrap_building(&mut self, building: Building) -> Result<()> {
        if self.state.buildings.iter().any(|x| x.id == building.id) {
            return Err(format!("Duplicate building {}.", building.id));
        }
        if let Some(problem) = building_problem(&self.state, &building) {
            return Err(format!("Invalid building {}: {problem}.", building.id));
        }
        self.state.buildings.push(building);
        self.state.revision += 1;
        Ok(())
    }

    /// The price percent of a site: its lot's district, or 100 for a site without a listed lot.
    pub fn price_percent_of(&self, site_id: &str) -> i32 { self.offer_of_site(site_id).map_or(100, |o| o.price_percent) }

    /// Outsourced construction, instant: the company pays and the factory gains a floor; the first floor fixes the elevator.
    pub fn add_floor(&mut self, player: &str, request: &str, building_id: &str, elevator_x: i32, elevator_z: i32) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(index) = self.state.buildings.iter().position(|x| x.id == building_id) else { return self.reject(request, player, "forbidden") };
        let building = self.state.buildings[index].clone();
        if !self.state.can_view(player, &building.site_id) {
            return self.reject(request, player, "forbidden");
        }
        if building.kind != FACTORY_KIND {
            return self.reject(request, player, "not-a-factory");
        }
        let Some(offer) = self.content.floor_offer.clone() else { return self.reject(request, player, "no-floor-offer") };
        if building.floors >= offer.max_floors {
            return self.reject(request, player, "max-floors");
        }
        let Some(company) = self.company_of_site(&building.site_id) else { return self.reject(request, player, "no-company") };
        if !building.has_elevator() {
            if !grid::is_interior(&building, elevator_x, elevator_z) {
                return self.reject(request, player, "invalid-elevator");
            }
            if let Some(problem) = grid::cell_problem(&self.state, &building.site_id, elevator_x, elevator_z, 1, 1, None, 0, "") {
                return self.reject(request, player, problem);
            }
        }
        let price = floor_price_cents(&building, Some(&offer));
        if self.cash_of(&company) < price {
            return self.reject(request, player, "insufficient-funds");
        }
        self.try_debit(&company, price, CashNote::new(LEDGER_FLOOR, &building.site_id).request(request));
        let target = &mut self.state.buildings[index];
        if !target.has_elevator() {
            target.elevator_x = elevator_x;
            target.elevator_z = elevator_z;
        }
        target.floors += 1;
        self.record(request, player, true, "floor-added", "")
    }

    pub fn order_shell(&mut self, player: &str, request: &str, order: &ShellOrder) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(index) = self.state.buildings.iter().position(|x| x.id == order.building_id) else { return self.reject(request, player, "forbidden") };
        let site = self.state.buildings[index].site_id.clone();
        if !self.state.can_view(player, &site) {
            return self.reject(request, player, "forbidden");
        }
        let Some(company) = self.company_of_site(&site) else { return self.reject(request, player, "no-company") };
        let plan = plan_shell(&self.state, order, self.price_percent_of(&site));
        if let Some(problem) = plan.problem {
            return self.reject(request, player, problem);
        }
        let net = plan.net_cents();
        if net > 0 && self.cash_of(&company) < net {
            return self.reject(request, player, "insufficient-funds");
        }
        self.state.buildings[index] = plan.building.expect("a valid plan has a building");
        if net > 0 {
            self.try_debit(&company, net, CashNote::new(LEDGER_SHELL, &site).request(request));
        } else if net < 0 {
            self.try_credit(&company, -net, CashNote::new(LEDGER_SHELL, &site).request(request));
        }
        self.invalidate_diners();
        self.record_with(request, player, "shell-changed", net, "", "")
    }
}
