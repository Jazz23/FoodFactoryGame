// Integer geometry shared by the generator and the validator. Boxes are in half-metre units (doubled coordinates), so a road
// of even width around an integer centreline and a cell's centre are both exact; nothing uses floating point.
use std::collections::{BTreeSet, HashMap};

use super::layout::{Facing, WorldBuilding, WorldCell, WorldRect};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Box2 {
    pub x0: i32,
    pub z0: i32,
    pub x1: i32,
    pub z1: i32,
}

impl Box2 {
    pub fn new(x0: i32, z0: i32, x1: i32, z1: i32) -> Self {
        Self { x0: x0.min(x1), z0: z0.min(z1), x1: x0.max(x1), z1: z0.max(z1) }
    }

    /// Interiors intersect; touching edges do not count.
    pub fn overlaps(&self, other: &Box2) -> bool {
        self.x0 < other.x1 && other.x0 < self.x1 && self.z0 < other.z1 && other.z0 < self.z1
    }

    pub fn contains_doubled(&self, x: i32, z: i32) -> bool { x >= self.x0 && x <= self.x1 && z >= self.z0 && z <= self.z1 }
}

pub fn of_rect(rect: &WorldRect) -> Box2 {
    Box2::new(2 * rect.x, 2 * rect.z, 2 * (rect.x + rect.width), 2 * (rect.z + rect.depth))
}

pub fn of_building(building: &WorldBuilding) -> Box2 { of_rect(&building.footprint()) }

/// The paved area of an axis-aligned centreline from a to b, including half the width past each end.
pub fn strip(ax: i32, az: i32, bx: i32, bz: i32, width: i32) -> Box2 {
    Box2::new(2 * ax.min(bx) - width, 2 * az.min(bz) - width, 2 * ax.max(bx) + width, 2 * az.max(bz) + width)
}

pub fn step(facing: Facing) -> (i32, i32) {
    match facing {
        Facing::North => (0, 1),
        Facing::East => (1, 0),
        Facing::South => (0, -1),
        Facing::West => (-1, 0),
    }
}

/// Perimeter cells, never corners (decision 0019), on the building's street side.
pub fn is_door_on_facing(building: &WorldBuilding, door: WorldCell) -> bool {
    let (on_side, along, along_min, along_length) = match building.facing {
        Facing::North => (door.z == building.z + building.depth - 1, door.x, building.x, building.width),
        Facing::South => (door.z == building.z, door.x, building.x, building.width),
        Facing::East => (door.x == building.x + building.width - 1, door.z, building.z, building.depth),
        Facing::West => (door.x == building.x, door.z, building.z, building.depth),
    };
    on_side && along > along_min && along < along_min + along_length - 1
}

/// Door cells centred on the street wall: count cells starting at the middle of a wall of the given length.
pub fn doors(rect: WorldRect, facing: Facing, count: i32) -> Vec<WorldCell> {
    let length = if matches!(facing, Facing::North | Facing::South) { rect.width } else { rect.depth };
    let first = (length - count) / 2;
    (0..count)
        .map(|index| {
            let offset = first + index;
            match facing {
                Facing::North => WorldCell::new(rect.x + offset, rect.z + rect.depth - 1),
                Facing::South => WorldCell::new(rect.x + offset, rect.z),
                Facing::East => WorldCell::new(rect.x + rect.width - 1, rect.z + offset),
                Facing::West => WorldCell::new(rect.x, rect.z + offset),
            }
        })
        .collect()
}

/// A footprint extended `apron` cells forward, toward its street: a lot (decision 0028).
pub fn lot_rect_of(footprint: WorldRect, facing: Facing, apron: i32) -> WorldRect {
    match facing {
        Facing::North => WorldRect::new(footprint.x, footprint.z, footprint.width, footprint.depth + apron),
        Facing::South => WorldRect::new(footprint.x, footprint.z - apron, footprint.width, footprint.depth + apron),
        Facing::East => WorldRect::new(footprint.x, footprint.z, footprint.width + apron, footprint.depth),
        Facing::West => WorldRect::new(footprint.x - apron, footprint.z, footprint.width + apron, footprint.depth),
    }
}

/// A property's lot: its footprint extended forward by the apron and, for a restaurant with a service yard, the yard beside it.
pub fn lot_rect(building: &WorldBuilding, apron: i32) -> WorldRect {
    let lot = lot_rect_of(building.footprint(), building.facing, apron);
    let Some(yard) = building.service_yard else { return lot };
    let x0 = lot.x.min(yard.x);
    let z0 = lot.z.min(yard.z);
    WorldRect::new(x0, z0, (lot.x + lot.width).max(yard.x + yard.width) - x0, (lot.z + lot.depth).max(yard.z + yard.depth) - z0)
}

/// Gives a restaurant its service yard (generator v5): a strip `width` cells wide along its low or high side, from the lot's
/// street edge to the building's rear. The back door is the yard-side wall cell two cells in from the rear corner, and the
/// starter dock the two yard cells beside its doorstep toward the street.
pub fn add_service_yard(building: &mut WorldBuilding, low: bool, width: i32, apron: i32) {
    let shell = building.footprint();
    let lot = lot_rect_of(shell, building.facing, apron);
    let (fx, fz) = step(building.facing);
    if fz != 0 {
        let wall_x = if low { shell.x } else { shell.x + shell.width - 1 };
        let out_x = if low { shell.x - 1 } else { shell.x + shell.width };
        let rear_z = if fz > 0 { shell.z } else { shell.z + shell.depth - 1 };
        building.service_yard = Some(WorldRect::new(if low { shell.x - width } else { shell.x + shell.width }, lot.z, width, lot.depth));
        building.back_door = Some(WorldCell::new(wall_x, rear_z + 2 * fz));
        building.service_dock = Some(WorldRect::new(out_x, (rear_z + 3 * fz).min(rear_z + 4 * fz), 1, 2));
    } else {
        let wall_z = if low { shell.z } else { shell.z + shell.depth - 1 };
        let out_z = if low { shell.z - 1 } else { shell.z + shell.depth };
        let rear_x = if fx > 0 { shell.x } else { shell.x + shell.width - 1 };
        building.service_yard = Some(WorldRect::new(lot.x, if low { shell.z - width } else { shell.z + shell.depth }, lot.width, width));
        building.back_door = Some(WorldCell::new(rear_x + 2 * fx, wall_z));
        building.service_dock = Some(WorldRect::new((rear_x + 3 * fx).min(rear_x + 4 * fx), out_z, 2, 1));
    }
}

/// The cell just outside a door on the perimeter of a rectangle; None for a non-perimeter or corner cell.
pub fn doorstep(footprint: WorldRect, door: WorldCell) -> Option<WorldCell> {
    let west = door.x == footprint.x;
    let east = door.x == footprint.x + footprint.width - 1;
    let south = door.z == footprint.z;
    let north = door.z == footprint.z + footprint.depth - 1;
    if !footprint.contains(door.x, door.z) || (west || east) == (south || north) {
        return None;
    }
    Some(if west {
        WorldCell::new(door.x - 1, door.z)
    } else if east {
        WorldCell::new(door.x + 1, door.z)
    } else if south {
        WorldCell::new(door.x, door.z - 1)
    } else {
        WorldCell::new(door.x, door.z + 1)
    })
}

/// The cell just outside a lot's street edge, level with the door.
pub fn access_cell(lot: WorldRect, facing: Facing, door: WorldCell) -> WorldCell {
    match facing {
        Facing::North => WorldCell::new(door.x, lot.z + lot.depth),
        Facing::South => WorldCell::new(door.x, lot.z - 1),
        Facing::East => WorldCell::new(lot.x + lot.width, door.z),
        Facing::West => WorldCell::new(lot.x - 1, door.z),
    }
}

pub fn contains(outer: &WorldRect, inner: &WorldRect) -> bool {
    inner.x >= outer.x && inner.z >= outer.z && inner.x + inner.width <= outer.x + outer.width && inner.z + inner.depth <= outer.z + outer.depth
}

/// A quarter turn clockwise seen from above about the origin: +Z (north) becomes +X (east).
pub fn turn_point(x: i32, z: i32) -> (i32, i32) { (z, -x) }
pub fn turn_cell(cell: WorldCell) -> WorldCell { WorldCell::new(cell.z, -cell.x - 1) }
pub fn turn_rect(rect: WorldRect) -> WorldRect { WorldRect::new(rect.z, -(rect.x + rect.width), rect.depth, rect.width) }
pub fn turn_facing(facing: Facing) -> Facing { Facing::from_index(facing.index() + 1) }

pub fn floor_div(value: i64, divisor: i64) -> i64 {
    if value >= 0 { value / divisor } else { -((-value + divisor - 1) / divisor) }
}

/// Smallest n with n * n >= value.
pub fn ceil_sqrt(value: i64) -> i64 {
    if value <= 0 {
        return 0;
    }
    let mut root = (value as f64).sqrt() as i64;
    while root * root > value {
        root -= 1;
    }
    while root * root < value {
        root += 1;
    }
    root
}

/// Whether point p lies within `radius` of segment a-b (inclusive), exactly.
pub fn within_distance(px: i64, pz: i64, ax: i64, az: i64, bx: i64, bz: i64, radius: i64) -> bool {
    let dx = bx - ax;
    let dz = bz - az;
    let qx = px - ax;
    let qz = pz - az;
    let along = qx * dx + qz * dz;
    let length_squared = dx * dx + dz * dz;
    if along <= 0 || length_squared == 0 {
        return qx * qx + qz * qz <= radius * radius;
    }
    if along >= length_squared {
        return (px - bx) * (px - bx) + (pz - bz) * (pz - bz) <= radius * radius;
    }
    let cross = (dx * qz - dz * qx) as i128;
    cross * cross <= (radius as i128) * (radius as i128) * (length_squared as i128)
}

pub fn within_distance_line(line: &[WorldCell], px: i64, pz: i64, radius: i64) -> bool {
    line.windows(2).any(|pair| {
        within_distance(px, pz, pair[0].x as i64, pair[0].z as i64, pair[1].x as i64, pair[1].z as i64, radius)
    })
}

/// Where a polyline crosses the axis line (vertical: x = fixed_at, else z = fixed_at) strictly between from and to, as the
/// along coordinate rounded down, with the direction of the crossing piece. Pieces lying on the line are skipped.
pub fn crossings(line: &[WorldCell], vertical: bool, fixed_at: i32, from: i32, to: i32) -> Vec<(i32, i32, i32)> {
    let mut result = vec![];
    for pair in line.windows(2) {
        let (a, b) = (pair[0], pair[1]);
        let (a0, b0) = if vertical { (a.x as i64, b.x as i64) } else { (a.z as i64, b.z as i64) };
        let (a1, b1) = if vertical { (a.z as i64, b.z as i64) } else { (a.x as i64, b.x as i64) };
        if a0 == b0 {
            continue;
        }
        let low = a0.min(b0);
        let high = a0.max(b0);
        let fixed = fixed_at as i64;
        if fixed < low || fixed >= high {
            continue;
        }
        let along = a1 + floor_div((b1 - a1) * (fixed - a0), b0 - a0);
        if along <= from as i64 || along >= to as i64 {
            continue;
        }
        result.push((along as i32, b.x - a.x, b.z - a.z));
    }
    result
}

/// Boxes (doubled units) covering a polyline widened by `radius` metres each side, one per piece; conservative.
pub fn corridor(line: &[WorldCell], radius: i32) -> Vec<Box2> {
    line.windows(2)
        .map(|pair| {
            let (a, b) = (pair[0], pair[1]);
            Box2::new(2 * (a.x.min(b.x) - radius), 2 * (a.z.min(b.z) - radius), 2 * (a.x.max(b.x) + radius), 2 * (a.z.max(b.z) + radius))
        })
        .collect()
}

/// Bucketed boxes for overlap and point queries. Results come back in insertion order, never bucket order.
pub struct BoxIndex<T> {
    buckets: HashMap<(i32, i32), Vec<usize>>,
    items: Vec<(Box2, T)>,
}

const BUCKET: i32 = 128;

fn bucket_floor(value: i32) -> i32 {
    if value >= 0 { value / BUCKET } else { -((-value + BUCKET - 1) / BUCKET) }
}

impl<T: Clone> Default for BoxIndex<T> {
    fn default() -> Self { Self { buckets: HashMap::new(), items: vec![] } }
}

impl<T: Clone> BoxIndex<T> {
    pub fn new() -> Self { Self::default() }

    pub fn add(&mut self, b: Box2, item: T) {
        let index = self.items.len();
        self.items.push((b, item));
        for bx in bucket_floor(b.x0)..=bucket_floor(b.x1) {
            for bz in bucket_floor(b.z0)..=bucket_floor(b.z1) {
                self.buckets.entry((bx, bz)).or_default().push(index);
            }
        }
    }

    pub fn overlapping(&self, b: &Box2) -> Vec<T> {
        let mut hits = BTreeSet::new();
        for bx in bucket_floor(b.x0)..=bucket_floor(b.x1) {
            for bz in bucket_floor(b.z0)..=bucket_floor(b.z1) {
                if let Some(list) = self.buckets.get(&(bx, bz)) {
                    for &index in list {
                        if self.items[index].0.overlaps(b) {
                            hits.insert(index);
                        }
                    }
                }
            }
        }
        hits.into_iter().map(|index| self.items[index].1.clone()).collect()
    }

    pub fn any(&self, b: &Box2) -> bool {
        for bx in bucket_floor(b.x0)..=bucket_floor(b.x1) {
            for bz in bucket_floor(b.z0)..=bucket_floor(b.z1) {
                if let Some(list) = self.buckets.get(&(bx, bz)) {
                    if list.iter().any(|&index| self.items[index].0.overlaps(b)) {
                        return true;
                    }
                }
            }
        }
        false
    }

    /// First item (in insertion order) whose box contains the doubled point.
    pub fn find(&self, x: i32, z: i32) -> Option<T> {
        let list = self.buckets.get(&(bucket_floor(x), bucket_floor(z)))?;
        list.iter().copied().filter(|&index| self.items[index].0.contains_doubled(x, z)).min().map(|index| self.items[index].1.clone())
    }
}
