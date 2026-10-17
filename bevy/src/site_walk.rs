// Presentation-only customer routes over replicated ground cells (Unity SiteWalk). Four-way paths enter from the street,
// queue back along the way in, and never substitute a straight walk through walls when no route exists.
use std::collections::{HashMap, VecDeque};

use crate::goods::{Equipment, PropertyOffer, Snapshot, grid, rules::{self, Walk}};

type Cell = (i32, i32);
pub type Point = (f32, f32);
const STEPS: [Cell; 4] = [(1, 0), (-1, 0), (0, 1), (0, -1)];

pub struct SiteWalk {
    pub walk: Walk,
    structures: Walk,
    outward: HashMap<Cell, Cell>,
    edges: Vec<Cell>,
    street_direction: Option<Cell>,
}

impl SiteWalk {
    pub fn new(view: &Snapshot, site: &str, offer: Option<&PropertyOffer>) -> Option<Self> {
        let walk = rules::walkable(view, site, None, true)?;
        let shell = Snapshot { site_layouts: view.site_layouts.clone(), buildings: view.buildings.clone(), ..Default::default() };
        let structures = rules::walkable(&shell, site, None, true)?;
        let edges = rules::street_cells(view.layout_of(site), offer).into_iter().filter(|&(x, z)| walk.get(x, z)).collect();
        let street_direction = offer.map(|o| if o.access_z >= o.lot_z + o.depth { (0, 1) }
            else if o.access_z < o.lot_z { (0, -1) } else if o.access_x < o.lot_x { (-1, 0) } else { (1, 0) });
        Some(Self::from_walk(walk, structures, edges, street_direction))
    }

    fn from_walk(walk: Walk, structures: Walk, edges: Vec<Cell>, street_direction: Option<Cell>) -> Self {
        let mut outward = HashMap::new();
        let mut queue = VecDeque::new();
        for &cell in &edges {
            if walk.get(cell.0, cell.1) && outward.insert(cell, cell).is_none() { queue.push_back(cell); }
        }
        while let Some(cell) = queue.pop_front() {
            for (dx, dz) in STEPS {
                let next = (cell.0 + dx, cell.1 + dz);
                if walk.get(next.0, next.1) && !outward.contains_key(&next) {
                    outward.insert(next, cell);
                    queue.push_back(next);
                }
            }
        }
        Self { walk, structures, outward, edges, street_direction }
    }

    pub fn same_ground(&self, other: &Self) -> bool {
        self.walk.width == other.walk.width && self.walk.depth == other.walk.depth && self.walk.open == other.walk.open
            && self.structures.open == other.structures.open && self.edges == other.edges && self.street_direction == other.street_direction
    }

    /// The register-facing reachable cell first, followed by its shortest route back to the street.
    pub fn service_line(&self, register: &Equipment) -> Vec<Point> {
        let reached = self.outward.keys().copied().collect();
        let mut beside = rules::cells_beside(&reached, register);
        let (w, d) = register.footprint();
        let (fx, fz) = grid::facing(register.rotation);
        let centre = (register.cell_x as f32 + (w - 1) as f32 * 0.5, register.cell_z as f32 + (d - 1) as f32 * 0.5);
        let score = |c: Cell| ((c.0 as f32 - centre.0) * fx as f32 + (c.1 as f32 - centre.1) * fz as f32,
            (c.0 as f32 - centre.0).abs() + (c.1 as f32 - centre.1).abs());
        beside.sort_by(|a, b| score(*b).0.total_cmp(&score(*a).0).then_with(|| score(*a).1.total_cmp(&score(*b).1)));
        let Some(mut cell) = beside.first().copied() else { return vec![] };
        let mut line = vec![];
        loop {
            line.push(center(cell));
            let next = self.outward[&cell];
            if next == cell { break; }
            cell = next;
        }
        line
    }

    /// Start just outside the street edge connected to the destination, rather than teleporting to its queue or seat.
    pub fn entrance(&self, target: Point) -> Option<Point> {
        let cell = cell_of(target);
        let mut cell = if self.outward.contains_key(&cell) { cell } else {
            STEPS.iter().map(|&(x, z)| (cell.0 + x, cell.1 + z)).find(|c| self.outward.contains_key(c))?
        };
        while self.outward[&cell] != cell { cell = self.outward[&cell]; }
        let (x, z) = center(cell);
        let (dx, dz) = self.street_direction.unwrap_or_else(|| if cell.0 == 0 { (-1, 0) } else if cell.0 == self.walk.width - 1 { (1, 0) }
            else if cell.1 == 0 { (0, -1) } else { (0, 1) });
        Some((x + dx as f32, z + dz as f32))
    }

    /// Whether a seat point could be sat at: not in a wall, window or back-door cell, and beside (or on) an open cell.
    pub fn seat_reachable(&self, seat: Point) -> bool {
        let (x, z) = cell_of(seat);
        x >= 0 && z >= 0 && x < self.walk.width && z < self.walk.depth && self.structures.get(x, z)
            && (self.walk.get(x, z) || STEPS.iter().any(|&(dx, dz)| self.walk.get(x + dx, z + dz)))
    }

    /// Seats may occupy one blocked destination cell; only its cardinal open neighbour can approach it.
    pub fn route(&self, from: Point, to: Point, seat: bool) -> Option<Vec<Point>> {
        let start_cell = cell_of(from);
        let goal_cell = cell_of(to);
        let inside = |(x, z): Cell| x >= 0 && z >= 0 && x < self.walk.width && z < self.walk.depth;
        // A furniture seat can occupy an object footprint, but never a wall/window/back-door cell.
        if inside(goal_cell) && !self.structures.get(goal_cell.0, goal_cell.1) { return None; }
        let start = if self.walk.get(start_cell.0, start_cell.1) { start_cell }
            else if !inside(start_cell) {
                let edge = *self.edges.iter().min_by(|a, b| distance(center(**a), from).total_cmp(&distance(center(**b), from)))?;
                let point = center(edge);
                // The caller spawns at this site's apron. Do not turn an old, distant site's pose into a cross-city path.
                if distance(point, from) > 1.01 || ((point.0 - from.0).abs() > 0.001 && (point.1 - from.1).abs() > 0.001) { return None; }
                edge
            } else {
                if !self.structures.get(start_cell.0, start_cell.1) { return None; }
                // A seated figure can stand up into an adjacent open cell; do not jump across arbitrary obstacles.
                STEPS.iter().map(|&(x, z)| (start_cell.0 + x, start_cell.1 + z)).find(|&(x, z)| self.walk.get(x, z))?
            };
        let goal = |c: Cell| if !inside(goal_cell) {
            self.outward.get(&c) == Some(&c)
        } else if self.walk.get(goal_cell.0, goal_cell.1) { c == goal_cell }
            else { seat && (c.0 - goal_cell.0).abs() + (c.1 - goal_cell.1).abs() == 1 };
        let mut parents = HashMap::from([(start, start)]);
        let mut queue = VecDeque::from([start]);
        while let Some(cell) = queue.pop_front() {
            if goal(cell) {
                let mut cells = vec![cell];
                let mut at = cell;
                while at != start { at = parents[&at]; cells.push(at); }
                cells.reverse();
                let mut points = vec![];
                for (i, &c) in cells.iter().enumerate() {
                    if i > 0 && i + 1 < cells.len() {
                        let (a, b) = (cells[i - 1], cells[i + 1]);
                        if (c.0 - a.0, c.1 - a.1) == (b.0 - c.0, b.1 - c.1) { continue; }
                    }
                    points.push(center(c));
                }
                points.push(to);
                return Some(points);
            }
            for (dx, dz) in STEPS {
                let next = (cell.0 + dx, cell.1 + dz);
                if self.walk.get(next.0, next.1) && !parents.contains_key(&next) {
                    parents.insert(next, cell);
                    queue.push_back(next);
                }
            }
        }
        None
    }
}

fn center((x, z): Cell) -> Point { (x as f32 + 0.5, z as f32 + 0.5) }
fn cell_of((x, z): Point) -> Cell { (x.floor() as i32, z.floor() as i32) }
fn distance(a: Point, b: Point) -> f32 { (a.0 - b.0).powi(2) + (a.1 - b.1).powi(2) }

/// Unity CompetitorFrontage (decision 0033): the fixed line customer figures walk in front of a competitor's door, in site
/// metres. Competitor lots have no shell or site walk; their building spans the lot except toward the street, door on that side.
#[derive(Clone, Debug)]
pub struct Frontage {
    /// On the apron just outside the door, where figures go in and come out.
    pub door: Point,
    /// On the lot's street edge, in line with the door.
    pub apron: Point,
    /// On the sidewalk, in line with the door.
    pub kerb: Point,
    pub outward: Point,
    /// Along the facade, toward the side of the door with more room.
    pub along: Point,
    /// Points on the street band where figures appear and leave.
    pub street: Vec<Point>,
    per_row: i32,
}

/// Queue places drawn outside a competitor's door; customers further back get no figure.
pub const FRONTAGE_QUEUE_SPOTS: usize = 8;
const FRONTAGE_SPACING: f32 = 0.8;
const SIDEWALK: f32 = 0.6;
const STREET_REACH: f32 = 12.0;

impl Frontage {
    /// None when the lot's building has no door.
    pub fn new(offer: &PropertyOffer) -> Option<Self> {
        let door = offer.doors.first()?;
        let outward = if door.z == offer.building_z + offer.building_depth - 1 { (0, 1) } else if door.z == offer.building_z { (0, -1) }
            else if door.x == offer.building_x { (-1, 0) } else { (1, 0) };
        let cell = (door.x as f32 + 0.5, door.z as f32 + 0.5);
        let (width, depth) = (offer.width as f32, offer.depth as f32);
        let edge = match outward { (1, _) => width - cell.0, (-1, _) => cell.0, (_, 1) => depth - cell.1, _ => cell.1 };
        let (low, high) = if outward.0 == 0 { (cell.0, width - cell.0) } else { (cell.1, depth - cell.1) };
        let sign = if high >= low { 1.0 } else { -1.0 };
        let along = if outward.0 == 0 { (sign, 0.0) } else { (0.0, sign) };
        let out = (outward.0 as f32, outward.1 as f32);
        let at = |d: f32| (cell.0 + out.0 * d, cell.1 + out.1 * d);
        // The street band: from past one side of the lot to past the other, a sidewalk out from its street edge.
        let (half_x, half_z) = (width * 0.5, depth * 0.5);
        let band = if outward.0 == 0 { (1.0, 0.0) } else { (0.0, 1.0) };
        let middle = (half_x + out.0 * (half_x + SIDEWALK), half_z + out.1 * (half_z + SIDEWALK));
        let reach = if outward.0 == 0 { half_x } else { half_z } + STREET_REACH - 1.0;
        let street = (0..9).map(|i| { let t = -reach + 2.0 * reach * i as f32 / 8.0; (middle.0 + band.0 * t, middle.1 + band.1 * t) }).collect();
        Some(Self {
            door: at(0.5 + 0.3), apron: at((0.5 + 0.3f32).max(edge - 0.3)), kerb: at(edge + SIDEWALK), outward: out, along, street,
            per_row: (((low.max(high) - 0.4) / FRONTAGE_SPACING).floor() as i32).max(1),
        })
    }

    /// The rank-th place in the queue (0 next to the door): along the facade, then back along a row further out.
    pub fn queue_spot(&self, rank: usize) -> Point {
        let rank = rank as i32;
        let row = rank / self.per_row;
        let column = if row % 2 == 0 { rank % self.per_row } else { self.per_row - 1 - rank % self.per_row };
        let out = FRONTAGE_SPACING * (1 + row) as f32 - 0.3;
        let along = FRONTAGE_SPACING * (column + 1) as f32;
        (self.door.0 + self.outward.0 * out + self.along.0 * along, self.door.1 + self.outward.1 * out + self.along.1 * along)
    }

    /// The walk from the street to a target on the apron: to the door's line on the sidewalk, then across.
    pub fn arrive(&self, target: Point) -> Vec<Point> { vec![self.kerb, self.apron, target] }

    /// The walk from the door (or the queue) out to a street point.
    pub fn depart(&self, street: Point) -> Vec<Point> { vec![self.apron, self.kerb, street] }

    /// The street point a figure keyed by this ID comes from and goes to.
    pub fn street_point(&self, id: &str) -> Point { self.street[(crate::world::random::fnv1a(id) % self.street.len() as u64) as usize] }
}
