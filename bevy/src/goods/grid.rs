// Pure grid rules shared by the server's placement check and the client's placement preview (Unity SiteGrid); the server
// always re-checks. Placed equipment, belts and building walls share the grid: nothing may overlap anything else on the same
// level. Level 0 is the ground; a higher level exists only over the interior of a building with that many floors, and its
// elevator cell is kept clear on every floor. Equipment has an occupancy layer: the object layer follows these rules; floor
// finishes lie under it, wall decor hangs on wall cells, ceiling decor needs an interior and tabletop props stand on a table.
// A restaurant with free walls has no implied perimeter: its walls are partition records and its interior is what they enclose.
use std::cell::RefCell;
use std::collections::{HashMap, HashSet, VecDeque};
use std::rc::Rc;

use super::state::{Building, Equipment, EquipmentState, Snapshot, Structure};
use super::{DOOR_STRUCTURE, PARTITION_STRUCTURE, SERVICE_DOOR_ROLE, WINDOW_STRUCTURE};

pub const CELL_SIZE: f32 = 1.0;
pub const OBJECT_LAYER: &str = "";
pub const FLOOR_LAYER: &str = "floor";
pub const WALL_LAYER: &str = "wall";
pub const CEILING_LAYER: &str = "ceiling";
pub const TABLETOP_LAYER: &str = "tabletop";

pub fn is_layer(layer: &str) -> bool { matches!(layer, "" | FLOOR_LAYER | WALL_LAYER | CEILING_LAYER | TABLETOP_LAYER) }

/// Odd quarter turns swap the footprint's width and depth.
pub fn footprint(width: i32, depth: i32, rotation: i32) -> (i32, i32) { if rotation % 2 == 0 { (width, depth) } else { (depth, width) } }

#[allow(clippy::too_many_arguments)]
pub fn overlaps(ax: i32, az: i32, aw: i32, ad: i32, bx: i32, bz: i32, bw: i32, bd: i32) -> bool {
    ax < bx + bw && bx < ax + aw && az < bz + bd && bz < az + ad
}

/// None when the equipment can occupy the anchor cell; otherwise the rejection reason. The piece itself is ignored.
pub fn placement_problem(state: &Snapshot, equipment: &Equipment, cell_x: i32, cell_z: i32, rotation: i32, level: i32) -> Option<&'static str> {
    if !(0..=3).contains(&rotation) {
        return Some("invalid-rotation");
    }
    let (width, depth) = footprint(equipment.width, equipment.depth, rotation);
    cell_problem(state, &equipment.site_id, cell_x, cell_z, width, depth, Some(&equipment.id), level, &equipment.layer)
}

/// None when a footprint lies inside the site's layout on an existing floor and overlaps nothing on that level other than
/// ignore_id; otherwise out-of-bounds, no-floor or blocked (or a decor layer's reasons).
#[allow(clippy::too_many_arguments)]
pub fn cell_problem(state: &Snapshot, site_id: &str, cell_x: i32, cell_z: i32, width: i32, depth: i32, ignore_id: Option<&str>, level: i32, layer: &str) -> Option<&'static str> {
    let Some(layout) = state.layout_of(site_id) else { return Some("out-of-bounds") };
    if level < 0 || cell_x < 0 || cell_z < 0 || cell_x + width > layout.width || cell_z + depth > layout.depth {
        return Some("out-of-bounds");
    }
    if !layer.is_empty() {
        return layer_problem(state, site_id, cell_x, cell_z, width, depth, ignore_id, level, layer);
    }
    if level > 0 && !state.buildings.iter().any(|b| b.site_id == site_id && b.floors > level && inside_interior(b, cell_x, cell_z, width, depth)) {
        return Some("no-floor");
    }
    for other in state.equipment.iter().filter(|x| Some(x.id.as_str()) != ignore_id && x.site_id == site_id && x.placed() && x.level == level && x.layer.is_empty()) {
        let (ow, od) = other.footprint();
        if overlaps(cell_x, cell_z, width, depth, other.cell_x, other.cell_z, ow, od) {
            return Some("blocked");
        }
    }
    if state.belts.iter().any(|b| Some(b.id.as_str()) != ignore_id && b.site_id == site_id && (b.level == level || b.exit_level() == level) && overlaps(cell_x, cell_z, width, depth, b.cell_x, b.cell_z, 1, 1)) {
        return Some("blocked");
    }
    if state.buildings.iter().any(|b| b.site_id == site_id && (covers_wall(b, cell_x, cell_z, width, depth) || covers_shaft(b, cell_x, cell_z, width, depth, level))) {
        return Some("blocked");
    }
    None
}

#[allow(clippy::too_many_arguments)]
fn layer_problem(state: &Snapshot, site_id: &str, cell_x: i32, cell_z: i32, width: i32, depth: i32, ignore_id: Option<&str>, level: i32, layer: &str) -> Option<&'static str> {
    if !is_layer(layer) {
        return Some("invalid-layer");
    }
    if level != 0 {
        return Some("no-floor");
    }
    let buildings: Vec<&Building> = state.buildings.iter().filter(|b| b.site_id == site_id).collect();
    for x in cell_x..cell_x + width {
        for z in cell_z..cell_z + depth {
            let wall = buildings.iter().any(|b| is_wall(b, x, z, 0));
            match layer {
                FLOOR_LAYER if wall => return Some("blocked"),
                WALL_LAYER if !wall || buildings.iter().any(|b| window_at(b, x, z).is_some()) => return Some("no-wall"),
                CEILING_LAYER if wall || !buildings.iter().any(|b| is_interior(b, x, z)) => return Some("no-ceiling"),
                _ => {}
            }
        }
    }
    if layer == TABLETOP_LAYER
        && !state.equipment.iter().any(|t| t.site_id == site_id && t.placed() && super::is_table(t) && t.layer.is_empty() && t.level == 0 && contains(t, cell_x, cell_z, width, depth))
    {
        return Some("no-table");
    }
    for other in state.equipment.iter().filter(|x| Some(x.id.as_str()) != ignore_id && x.site_id == site_id && x.placed() && x.layer == layer) {
        let (ow, od) = other.footprint();
        if overlaps(cell_x, cell_z, width, depth, other.cell_x, other.cell_z, ow, od) {
            return Some("blocked");
        }
    }
    None
}

/// True when the placed piece's footprint wholly contains the given footprint.
pub fn contains(piece: &Equipment, cell_x: i32, cell_z: i32, width: i32, depth: i32) -> bool {
    let (pw, pd) = piece.footprint();
    cell_x >= piece.cell_x && cell_z >= piece.cell_z && cell_x + width <= piece.cell_x + pw && cell_z + depth <= piece.cell_z + pd
}

/// The grid direction a piece's front faces: rotation 0 faces +Z, each quarter turn a quarter clockwise seen from above.
pub fn facing(rotation: i32) -> (i32, i32) {
    match rotation.rem_euclid(4) {
        0 => (0, 1),
        1 => (1, 0),
        2 => (0, -1),
        _ => (-1, 0),
    }
}

/// The cells just behind a footprint (width and depth as placed), opposite the way it faces.
pub fn behind_cells(cell_x: i32, cell_z: i32, width: i32, depth: i32, rotation: i32) -> Vec<(i32, i32)> {
    let (fx, fz) = facing(rotation);
    if fz != 0 {
        (cell_x..cell_x + width).map(|x| (x, if fz > 0 { cell_z - 1 } else { cell_z + depth })).collect()
    } else {
        (cell_z..cell_z + depth).map(|z| (if fx > 0 { cell_x - 1 } else { cell_x + width }, z)).collect()
    }
}

pub fn wall_at(state: &Snapshot, site_id: &str, cell_x: i32, cell_z: i32) -> bool {
    state.buildings.iter().any(|b| b.site_id == site_id && is_wall(b, cell_x, cell_z, 0))
}

pub fn on_perimeter(b: &Building, x: i32, z: i32) -> bool {
    !b.free_walls
        && overlaps(x, z, 1, 1, b.cell_x, b.cell_z, b.width, b.depth)
        && (x == b.cell_x || x == b.cell_x + b.width - 1 || z == b.cell_z || z == b.cell_z + b.depth - 1)
}

/// A door may be any perimeter cell except a corner.
pub fn is_door_cell(b: &Building, x: i32, z: i32) -> bool {
    on_perimeter(b, x, z) && !((x == b.cell_x || x == b.cell_x + b.width - 1) && (z == b.cell_z || z == b.cell_z + b.depth - 1))
}

fn has_door(b: &Building, x: i32, z: i32) -> bool { b.doors.iter().any(|d| d.x == x && d.z == z) }

/// Ground floor: doors are openings. Upper floors have no doors. Partitions stand on the ground floor; one with a door opens.
pub fn is_wall(b: &Building, x: i32, z: i32, level: i32) -> bool {
    (on_perimeter(b, x, z) && (level > 0 || !has_door(b, x, z))) || (level == 0 && is_partition(b, x, z) && !has_structure(b, DOOR_STRUCTURE, x, z))
}

/// A walkable opening: a perimeter door or a door in an interior wall.
pub fn is_door(b: &Building, x: i32, z: i32) -> bool {
    (on_perimeter(b, x, z) && has_door(b, x, z)) || (is_partition(b, x, z) && has_structure(b, DOOR_STRUCTURE, x, z))
}

/// A back door: a door whose record has the service role. Customers treat it as a wall.
pub fn is_service_door(b: &Building, x: i32, z: i32) -> bool {
    is_door(b, x, z) && b.structures.iter().any(|s| s.kind == DOOR_STRUCTURE && s.x == x && s.z == z && s.role == SERVICE_DOOR_ROLE)
}

pub fn service_doors(b: &Building) -> Vec<(i32, i32)> {
    b.structures.iter().filter(|s| s.kind == DOOR_STRUCTURE && s.role == SERVICE_DOOR_ROLE && is_door(b, s.x, s.z)).map(|s| (s.x, s.z)).collect()
}

/// Every customer (front) door of a building: a door on an outer wall (it has a doorstep) that is not a back door.
pub fn customer_doors(b: &Building) -> Vec<(i32, i32)> {
    let mut cells: Vec<(i32, i32)> = vec![];
    for cell in b.doors.iter().map(|d| (d.x, d.z)).chain(b.structures.iter().filter(|s| s.kind == DOOR_STRUCTURE).map(|s| (s.x, s.z))) {
        if !cells.contains(&cell) {
            cells.push(cell);
        }
    }
    cells.into_iter().filter(|c| is_door(b, c.0, c.1) && !is_service_door(b, c.0, c.1) && doorstep(b, c.0, c.1).is_some()).collect()
}

/// The doorstep of a door on an outer wall: the neighbour across the wall that is not interior.
pub fn doorstep(b: &Building, x: i32, z: i32) -> Option<(i32, i32)> {
    let mut outside = vec![];
    for (nx, nz) in [(x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1)] {
        if !on_perimeter(b, nx, nz) && !is_partition(b, nx, nz) && !is_interior(b, nx, nz) {
            outside.push((nx, nz));
        }
    }
    if outside.len() == 1 {
        return Some(outside[0]);
    }
    let beyond: Vec<_> = outside.iter().copied().filter(|c| !overlaps(c.0, c.1, 1, 1, b.cell_x, b.cell_z, b.width, b.depth)).collect();
    if outside.len() == 2 && beyond.len() == 1 { Some(beyond[0]) } else { None }
}

pub fn is_partition(b: &Building, x: i32, z: i32) -> bool { has_structure(b, PARTITION_STRUCTURE, x, z) }

pub fn has_structure(b: &Building, kind: &str, x: i32, z: i32) -> bool { b.structures.iter().any(|s| s.kind == kind && s.x == x && s.z == z) }

/// The window covering a cell (a window spans its cell and the next one along its axis).
pub fn window_at(b: &Building, x: i32, z: i32) -> Option<&Structure> {
    b.structures.iter().find(|s| s.kind == WINDOW_STRUCTURE && window_covers(s, x, z))
}

pub fn window_covers(window: &Structure, x: i32, z: i32) -> bool {
    (window.x == x && window.z == z) || if window.axis == 0 { window.x + 1 == x && window.z == z } else { window.x == x && window.z + 1 == z }
}

/// Strictly inside the walls; door cells are not interior.
pub fn is_interior(b: &Building, x: i32, z: i32) -> bool {
    if b.free_walls {
        enclosed(b).contains(&(x, z))
    } else {
        x > b.cell_x && x < b.cell_x + b.width - 1 && z > b.cell_z && z < b.cell_z + b.depth - 1
    }
}

pub fn inside_interior(b: &Building, x: i32, z: i32, width: i32, depth: i32) -> bool {
    if !b.free_walls {
        return is_interior(b, x, z) && is_interior(b, x + width - 1, z + depth - 1);
    }
    let cells = enclosed(b);
    (x..x + width).all(|cx| (z..z + depth).all(|cz| cells.contains(&(cx, cz))))
}

/// Every interior cell of a building.
pub fn interior_cells(b: &Building) -> Vec<(i32, i32)> {
    if b.free_walls {
        let mut cells: Vec<_> = enclosed(b).iter().copied().collect();
        cells.sort();
        return cells;
    }
    let mut cells = vec![];
    for x in b.cell_x + 1..b.cell_x + b.width - 1 {
        for z in b.cell_z + 1..b.cell_z + b.depth - 1 {
            cells.push((x, z));
        }
    }
    cells
}

thread_local! {
    static ENCLOSURES: RefCell<HashMap<(String, u64), Rc<HashSet<(i32, i32)>>>> = RefCell::new(HashMap::new());
}

fn enclosure_key(b: &Building) -> u64 {
    let mut hash: u64 = 1469598103934665603;
    let mut mix = |v: i64| {
        hash ^= v as u64;
        hash = hash.wrapping_mul(1099511628211);
    };
    mix(b.cell_x as i64);
    mix(b.cell_z as i64);
    mix(b.width as i64);
    mix(b.depth as i64);
    for s in b.structures.iter().filter(|s| s.kind == PARTITION_STRUCTURE) {
        mix(s.x as i64);
        mix(s.z as i64);
    }
    hash
}

/// Free walls' enclosed cells, cached per building and wall set.
fn enclosed(b: &Building) -> Rc<HashSet<(i32, i32)>> {
    let key = (b.id.clone(), enclosure_key(b));
    if let Some(cells) = ENCLOSURES.with(|c| c.borrow().get(&key).cloned()) {
        return cells;
    }
    let barriers: HashSet<(i32, i32)> = b.structures.iter().filter(|s| s.kind == PARTITION_STRUCTURE).map(|s| (s.x, s.z)).collect();
    let (min_x, min_z, max_x, max_z) = (b.cell_x - 1, b.cell_z - 1, b.cell_x + b.width, b.cell_z + b.depth);
    let mut outside = HashSet::new();
    let mut queue = VecDeque::new();
    for x in min_x..=max_x {
        for c in [(x, min_z), (x, max_z)] {
            if outside.insert(c) {
                queue.push_back(c);
            }
        }
    }
    for z in min_z..=max_z {
        for c in [(min_x, z), (max_x, z)] {
            if outside.insert(c) {
                queue.push_back(c);
            }
        }
    }
    while let Some((x, z)) = queue.pop_front() {
        for next in [(x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1)] {
            if next.0 >= min_x && next.0 <= max_x && next.1 >= min_z && next.1 <= max_z && !barriers.contains(&next) && outside.insert(next) {
                queue.push_back(next);
            }
        }
    }
    let mut cells = HashSet::new();
    for x in b.cell_x..b.cell_x + b.width {
        for z in b.cell_z..b.cell_z + b.depth {
            if !outside.contains(&(x, z)) && !barriers.contains(&(x, z)) {
                cells.insert((x, z));
            }
        }
    }
    let cells = Rc::new(cells);
    ENCLOSURES.with(|c| {
        let mut cache = c.borrow_mut();
        if cache.len() > 512 {
            cache.clear();
        }
        cache.insert(key, cells.clone());
    });
    cells
}

pub fn is_shaft(b: &Building, x: i32, z: i32) -> bool { b.has_elevator() && b.elevator_x == x && b.elevator_z == z }

/// True when the footprint on that level covers the building's elevator cell on one of its floors.
pub fn covers_shaft(b: &Building, x: i32, z: i32, width: i32, depth: i32, level: i32) -> bool {
    b.has_elevator() && level < b.floors && overlaps(x, z, width, depth, b.elevator_x, b.elevator_z, 1, 1)
}

pub fn covers_wall(b: &Building, x: i32, z: i32, width: i32, depth: i32) -> bool {
    if !overlaps(x, z, width, depth, b.cell_x, b.cell_z, b.width, b.depth) {
        return false;
    }
    (x..x + width).any(|cx| (z..z + depth).any(|cz| is_wall(b, cx, cz, 0)))
}

/// Placed equipment in the object layer on the ground of a site.
pub fn placed_objects<'a>(state: &'a Snapshot, site_id: &'a str) -> impl Iterator<Item = &'a Equipment> + 'a {
    state.equipment.iter().filter(move |x| x.site_id == site_id && x.state == EquipmentState::Placed && x.level == 0 && x.layer.is_empty())
}
