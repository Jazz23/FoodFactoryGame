// Walking on a site's cell grid, standing in for Unity's NavMesh: open cells are the site's walkable ground (walls, doors and
// placed object-layer pieces as the restaurant rules count them), moves go eight ways without cutting corners, and a found
// path is pulled taut along clear lines of sight. Positions are site metres: cell (x, z) spans [x, x + 1) x [z, z + 1).
use std::cmp::Ordering;
use std::collections::BinaryHeap;

use crate::goods::rules::Walk;

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Area {
    pub x0: f32,
    pub z0: f32,
    pub x1: f32,
    pub z1: f32,
}

impl Area {
    pub fn point(x: f32, z: f32) -> Self { Self { x0: x, z0: z, x1: x, z1: z } }
    pub fn cells(x: i32, z: i32, width: i32, depth: i32) -> Self { Self { x0: x as f32, z0: z as f32, x1: (x + width) as f32, z1: (z + depth) as f32 } }
    pub fn centre(&self) -> (f32, f32) { ((self.x0 + self.x1) * 0.5, (self.z0 + self.z1) * 0.5) }
    pub fn distance(&self, x: f32, z: f32) -> f32 {
        let cx = x.clamp(self.x0, self.x1);
        let cz = z.clamp(self.z0, self.z1);
        ((x - cx).powi(2) + (z - cz).powi(2)).sqrt()
    }
    pub fn contains(&self, x: f32, z: f32) -> bool { x > self.x0 && x < self.x1 && z > self.z0 && z < self.z1 }
}

#[derive(PartialEq)]
struct Entry {
    cost: f32,
    cell: usize,
}

impl Eq for Entry {}

impl Ord for Entry {
    fn cmp(&self, other: &Self) -> Ordering { other.cost.partial_cmp(&self.cost).unwrap_or(Ordering::Equal).then_with(|| other.cell.cmp(&self.cell)) }
}

impl PartialOrd for Entry {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> { Some(self.cmp(other)) }
}

pub fn cell_of(x: f32, z: f32) -> (i32, i32) { (x.floor() as i32, z.floor() as i32) }

/// The cheapest walk from a point to any cell `goal` accepts, as waypoints (cell centres, the start excluded), with its
/// length. The start cell may itself be blocked (an employee standing where a machine was just placed).
pub fn find_path(walk: &Walk, from: (f32, f32), goal: impl Fn(i32, i32) -> bool, limit: usize) -> Option<(Vec<(f32, f32)>, f32)> {
    let (sx, sz) = cell_of(from.0, from.1);
    let width = walk.width;
    let depth = walk.depth;
    if sx < 0 || sz < 0 || sx >= width || sz >= depth {
        return None;
    }
    let index = |x: i32, z: i32| (x * depth + z) as usize;
    let total = (width * depth) as usize;
    let mut cost = vec![f32::INFINITY; total];
    let mut parent = vec![usize::MAX; total];
    let mut heap = BinaryHeap::new();
    let start = index(sx, sz);
    cost[start] = 0.0;
    heap.push(Entry { cost: 0.0, cell: start });
    let mut visited = 0;
    while let Some(Entry { cost: c, cell }) = heap.pop() {
        if c > cost[cell] {
            continue;
        }
        let (x, z) = ((cell / depth as usize) as i32, (cell % depth as usize) as i32);
        if goal(x, z) && (cell != start || walk.get(x, z)) {
            let mut path = vec![];
            let mut at = cell;
            while at != start {
                path.push(((at / depth as usize) as f32 + 0.5, (at % depth as usize) as f32 + 0.5));
                at = parent[at];
            }
            path.reverse();
            return Some((smooth(walk, from, path), c));
        }
        visited += 1;
        if visited > limit {
            return None;
        }
        for (dx, dz) in [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)] {
            let (nx, nz) = (x + dx, z + dz);
            if !walk.get(nx, nz) {
                continue;
            }
            if dx != 0 && dz != 0 && (!walk.get(x + dx, z) || !walk.get(x, z + dz)) {
                continue;
            }
            let step = if dx != 0 && dz != 0 { std::f32::consts::SQRT_2 } else { 1.0 };
            let next = index(nx, nz);
            let next_cost = c + step;
            if next_cost < cost[next] {
                cost[next] = next_cost;
                parent[next] = cell;
                heap.push(Entry { cost: next_cost, cell: next });
            }
        }
    }
    None
}

/// True when the straight line between two points crosses only open cells (sampled finely, so corners count).
pub fn clear_line(walk: &Walk, a: (f32, f32), b: (f32, f32)) -> bool {
    let length = ((b.0 - a.0).powi(2) + (b.1 - a.1).powi(2)).sqrt();
    let steps = (length / 0.2).ceil().max(1.0) as i32;
    for i in 1..steps {
        let t = i as f32 / steps as f32;
        let (x, z) = (a.0 + (b.0 - a.0) * t, a.1 + (b.1 - a.1) * t);
        // Keep a little clearance from blocked cells on each side.
        for (ox, oz) in [(0.0, 0.0), (0.25, 0.0), (-0.25, 0.0), (0.0, 0.25), (0.0, -0.25)] {
            let (cx, cz) = cell_of(x + ox, z + oz);
            if !walk.get(cx, cz) {
                return false;
            }
        }
    }
    true
}

fn smooth(walk: &Walk, from: (f32, f32), path: Vec<(f32, f32)>) -> Vec<(f32, f32)> {
    if path.len() <= 1 {
        return path;
    }
    let mut result = vec![];
    let mut anchor = from;
    let mut index = 0;
    while index < path.len() {
        let mut furthest = index;
        for candidate in (index..path.len()).rev() {
            if clear_line(walk, anchor, path[candidate]) {
                furthest = candidate;
                break;
            }
        }
        result.push(path[furthest]);
        anchor = path[furthest];
        index = furthest + 1;
    }
    result
}
