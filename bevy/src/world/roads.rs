// The generated road graph as something vehicles can drive (decision 0032), plus the city traffic flow model, ported from
// Unity's RoadNetwork and RoadTraffic. Everything is integer and ordered by layout index, so a route is the same on every run;
// the server's truck simulation and every client's drawn cars build the same network from the same stored layout.
use std::cmp::Ordering;
use std::collections::{BinaryHeap, HashMap};

use super::clock::{DAY_SECONDS, HOUR_SECONDS};
use super::layout::{JunctionControl, RoadKind, WorldLayout};
use super::terrain::junction_stops;

/// A place on the network: a segment and whole metres along it from its From node (0..length).
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, serde::Serialize, serde::Deserialize)]
pub struct RoadPoint {
    pub segment: i32,
    pub offset: i32,
}

/// One straight piece of a route: along a segment from one offset to another. Forward means toward the To node.
#[derive(Clone, Copy, Debug, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
pub struct RoadLeg {
    pub segment: i32,
    pub from: i32,
    pub to: i32,
}

impl RoadLeg {
    pub fn new(segment: i32, from: i32, to: i32) -> Self { Self { segment, from, to } }
    pub fn forward(&self) -> bool { self.to >= self.from }
    pub fn length(&self) -> i32 { (self.to - self.from).abs() }
}

#[derive(Clone, Debug)]
pub struct Node {
    pub id: String,
    pub x: i32,
    pub z: i32,
    pub control: JunctionControl,
    /// Segment indices meeting here, in layout order.
    pub segments: Vec<usize>,
    pub light_offset: i32,
}

#[derive(Clone, Debug)]
pub struct Segment {
    pub id: String,
    pub index: usize,
    pub from: usize,
    pub to: usize,
    pub kind: RoadKind,
    pub width: i32,
    pub capacity_per_hour: i32,
    pub length: i32,
    /// The district's traffic level at the segment's midpoint; RURAL_TRAFFIC_PERCENT outside every district.
    pub traffic_percent: i32,
    pub lanes_per_direction: i32,
    /// Unit direction from From to To (axis-aligned).
    pub dx: i32,
    pub dz: i32,
}

/// PROTOTYPE: the traffic level of roads outside every district.
pub const RURAL_TRAFFIC_PERCENT: i32 = 10;
/// PROTOTYPE: a lot's access cell further than this from every centreline is not on the network.
pub const MAX_ACCESS_DISTANCE: i32 = 20;
/// PROTOTYPE: metres of road width per lane; a metre of each half is kerb.
pub const LANE_WIDTH: i32 = 3;
/// The fastest any vehicle drives, for the route search's estimate.
const FASTEST_METRES_PER_SECOND: i64 = 20;

#[derive(Clone, Debug, Default)]
pub struct RoadNetwork {
    pub nodes: Vec<Node>,
    pub segments: Vec<Segment>,
    segment_index: HashMap<String, usize>,
    stops: Vec<bool>,
}

#[derive(PartialEq, Eq)]
struct HeapItem {
    estimate: i64,
    node: usize,
    cost: i64,
}

impl Ord for HeapItem {
    fn cmp(&self, other: &Self) -> Ordering {
        // Reversed for a min-heap: lowest estimate, then lowest node index, first.
        other.estimate.cmp(&self.estimate).then_with(|| other.node.cmp(&self.node))
    }
}

impl PartialOrd for HeapItem {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> { Some(self.cmp(other)) }
}

impl RoadNetwork {
    pub fn new(layout: &WorldLayout) -> Result<Self, String> {
        let mut network = RoadNetwork::default();
        let mut node_index = HashMap::new();
        for node in &layout.nodes {
            node_index.insert(node.id.clone(), network.nodes.len());
            network.nodes.push(Node { id: node.id.clone(), x: node.x, z: node.z, control: node.control, segments: vec![], light_offset: light_offset(&node.id) });
        }
        for road in &layout.roads {
            let (Some(&from), Some(&to)) = (node_index.get(&road.from_id), node_index.get(&road.to_id)) else {
                return Err(format!("Road {} does not join two nodes.", road.id));
            };
            if from == to {
                return Err(format!("Road {} does not join two nodes.", road.id));
            }
            let (a, b) = (&network.nodes[from], &network.nodes[to]);
            if a.x != b.x && a.z != b.z {
                return Err(format!("Road {} is not axis-aligned.", road.id));
            }
            let segment = Segment {
                id: road.id.clone(),
                index: network.segments.len(),
                from,
                to,
                kind: road.kind,
                width: road.width,
                capacity_per_hour: road.capacity_per_hour.max(1),
                length: (b.x - a.x).abs() + (b.z - a.z).abs(),
                lanes_per_direction: ((road.width / 2 - 1) / LANE_WIDTH).max(1),
                dx: (b.x - a.x).signum(),
                dz: (b.z - a.z).signum(),
                traffic_percent: district_traffic(layout, (a.x + b.x) / 2, (a.z + b.z) / 2),
            };
            network.segment_index.insert(road.id.clone(), segment.index);
            network.nodes[from].segments.push(segment.index);
            network.nodes[to].segments.push(segment.index);
            network.segments.push(segment);
        }
        network.stops = (0..network.segments.len()).flat_map(|index| [network.compute_stops(index, true), network.compute_stops(index, false)]).collect();
        Ok(network)
    }

    pub fn segment_named(&self, id: &str) -> Option<usize> { self.segment_index.get(id).copied() }

    pub fn end_node(&self, segment: usize, forward: bool) -> usize {
        if forward { self.segments[segment].to } else { self.segments[segment].from }
    }

    /// The point on the network nearest a cell, or None when every centreline is further than MAX_ACCESS_DISTANCE.
    pub fn locate(&self, x: i32, z: i32) -> Option<RoadPoint> {
        let mut best = None;
        let mut best_distance = i32::MAX;
        for segment in &self.segments {
            let a = &self.nodes[segment.from];
            let along = if segment.dx != 0 { (x - a.x) * segment.dx } else { (z - a.z) * segment.dz };
            let offset = along.clamp(0, segment.length);
            let px = a.x + segment.dx * offset;
            let pz = a.z + segment.dz * offset;
            let distance = (px - x).abs() + (pz - z).abs();
            if distance >= best_distance {
                continue;
            }
            best_distance = distance;
            best = Some(RoadPoint { segment: segment.index as i32, offset });
        }
        if best_distance <= MAX_ACCESS_DISTANCE { best } else { None }
    }

    /// Map position of a point along a segment (centreline).
    pub fn position(&self, segment: usize, offset: f64) -> (f64, f64) {
        let s = &self.segments[segment];
        let a = &self.nodes[s.from];
        (a.x as f64 + s.dx as f64 * offset, a.z as f64 + s.dz as f64 * offset)
    }

    /// Whether traffic arriving at the end node of a segment driven in this direction must stop there.
    pub fn stops(&self, segment: usize, forward: bool) -> bool { self.stops[2 * segment + if forward { 0 } else { 1 }] }

    fn compute_stops(&self, segment: usize, forward: bool) -> bool {
        let node = &self.nodes[self.end_node(segment, forward)];
        match node.control {
            JunctionControl::None => false,
            JunctionControl::TrafficLight => true,
            JunctionControl::StopSign => junction_stops(node.control, self.segments[segment].kind, node.segments.iter().map(|i| self.segments[*i].kind)),
        }
    }

    /// The node a point stands on (offset 0 or length), or None.
    pub fn at_node(&self, point: RoadPoint) -> Option<usize> {
        let segment = &self.segments[point.segment as usize];
        if point.offset <= 0 {
            Some(segment.from)
        } else if point.offset >= segment.length {
            Some(segment.to)
        } else {
            None
        }
    }

    fn same_node(&self, a: RoadPoint, b: RoadPoint) -> bool { self.at_node(a).is_some() && self.at_node(a) == self.at_node(b) }

    /// The quickest route from start to end, as legs in driving order. drive(segment, forward) is the time to drive a whole
    /// segment (a partial leg costs its share); junction(segment, forward) is the wait at its end node, paid only when a leg
    /// ends there and the route goes on. Start equal to end gives one zero-length leg. None if no route exists.
    pub fn route(&self, start: RoadPoint, end: RoadPoint, drive: impl Fn(usize, bool) -> i64, junction: impl Fn(usize, bool) -> i64) -> Option<Vec<RoadLeg>> {
        if start == end || self.same_node(start, end) {
            return Some(vec![RoadLeg::new(start.segment, start.offset, start.offset)]);
        }
        let part = |segment: usize, forward: bool, metres: i32| -> i64 {
            if metres <= 0 { 0 } else { (drive(segment, forward) * metres as i64 / self.segments[segment].length.max(1) as i64).max(1) }
        };
        let (target_x, target_z) = self.position(end.segment as usize, end.offset as f64);
        let estimate = |node: usize| -> i64 {
            (((self.nodes[node].x as f64 - target_x).abs() + (self.nodes[node].z as f64 - target_z).abs()) * 1000.0 / FASTEST_METRES_PER_SECOND as f64) as i64
        };
        let count = self.nodes.len();
        let mut distance = vec![i64::MAX; count];
        let mut previous_node: Vec<Option<usize>> = vec![None; count];
        let mut previous_segment: Vec<Option<usize>> = vec![None; count];
        let mut settled = vec![false; count];
        let mut queue = BinaryHeap::new();
        let mut reach = |node: usize, cost: i64, from: Option<usize>, via: Option<usize>, distance: &mut Vec<i64>, queue: &mut BinaryHeap<HeapItem>| {
            if cost >= distance[node] {
                return;
            }
            distance[node] = cost;
            previous_node[node] = from;
            previous_segment[node] = via;
            queue.push(HeapItem { estimate: cost + estimate(node), node, cost });
        };
        let start_segment = &self.segments[start.segment as usize];
        let start_node = self.at_node(start);
        match start_node {
            Some(node) => reach(node, 0, None, None, &mut distance, &mut queue),
            None => {
                let s = start.segment as usize;
                reach(start_segment.to, part(s, true, start_segment.length - start.offset) + junction(s, true), None, Some(s), &mut distance, &mut queue);
                reach(start_segment.from, part(s, false, start.offset) + junction(s, false), None, Some(s), &mut distance, &mut queue);
            }
        }
        let end_node = self.at_node(end);
        let end_segment_index = end.segment as usize;
        let mut reached_end = i64::MAX;
        while let Some(HeapItem { estimate: item_estimate, node, cost }) = queue.pop() {
            if settled[node] || cost != distance[node] {
                continue;
            }
            if item_estimate >= reached_end {
                break;
            }
            settled[node] = true;
            if Some(node) == end_node {
                break;
            }
            let end_segment = &self.segments[end_segment_index];
            if end_node.is_none() && (node == end_segment.from || node == end_segment.to) {
                let from_start = node == end_segment.from;
                reached_end = reached_end.min(cost + part(end_segment_index, from_start, if from_start { end.offset } else { end_segment.length - end.offset }));
            }
            for &index in &self.nodes[node].segments {
                let segment = &self.segments[index];
                let forward = segment.from == node;
                let next = if forward { segment.to } else { segment.from };
                reach(next, cost + drive(index, forward) + junction(index, forward), Some(node), Some(index), &mut distance, &mut queue);
            }
        }
        let mut best_cost = i64::MAX;
        let mut best_node: Option<usize> = None;
        let mut direct = false;
        if let Some(node) = end_node {
            best_cost = distance[node];
            best_node = Some(node);
        } else {
            let end_segment = &self.segments[end_segment_index];
            for (node, forward, metres) in [(end_segment.from, true, end.offset), (end_segment.to, false, end_segment.length - end.offset)] {
                if distance[node] == i64::MAX {
                    continue;
                }
                let cost = distance[node] + part(end_segment_index, forward, metres);
                if cost < best_cost || (cost == best_cost && best_node.is_some_and(|b| node < b)) {
                    best_cost = cost;
                    best_node = Some(node);
                }
            }
            if start_node.is_none() && start.segment == end.segment {
                let forward = end.offset > start.offset;
                let cost = part(start.segment as usize, forward, (end.offset - start.offset).abs());
                if cost <= best_cost {
                    direct = true;
                }
            }
        }
        if direct {
            return Some(vec![RoadLeg::new(start.segment, start.offset, end.offset)]);
        }
        let best_node = best_node?;
        if best_cost == i64::MAX {
            return None;
        }
        let mut legs = vec![];
        if end_node.is_none() {
            let end_segment = &self.segments[end_segment_index];
            legs.push(RoadLeg::new(end.segment, if best_node == end_segment.from { 0 } else { end_segment.length }, end.offset));
        }
        let mut at = best_node;
        while let Some(previous) = previous_node[at] {
            let index = previous_segment[at].expect("a reached node has its segment");
            let segment = &self.segments[index];
            legs.push(if segment.to == at { RoadLeg::new(index as i32, 0, segment.length) } else { RoadLeg::new(index as i32, segment.length, 0) });
            at = previous;
        }
        if start_node.is_none() {
            let segment = &self.segments[start.segment as usize];
            legs.push(RoadLeg::new(start.segment, start.offset, if segment.to == at { segment.length } else { 0 }));
        }
        legs.reverse();
        legs.retain(|leg| leg.length() != 0);
        Some(legs)
    }
}

fn district_traffic(layout: &WorldLayout, x: i32, z: i32) -> i32 {
    for district in &layout.districts {
        for area in &district.areas {
            if x >= area.x && x <= area.x + area.width && z >= area.z && z <= area.z + area.depth {
                return district.traffic_percent;
            }
        }
    }
    RURAL_TRAFFIC_PERCENT
}

// ---------------------------------------------------------------------------------------------------------- traffic model

/// Background load never passes this percent of capacity, so city cars alone never close a road.
pub const MAX_BACKGROUND_PERCENT: i32 = 90;
/// Above this load a segment admits no more trucks, unless none is on it or a truck has waited PATIENCE_SECONDS.
pub const QUEUE_PERCENT: i32 = 120;
pub const PATIENCE_SECONDS: i64 = 30;
/// A truck counts as this many cars (percent).
pub const TRUCK_PCE_PERCENT: i32 = 250;
/// Spacing of moving cars at capacity flow, in metres per lane.
pub const CAPACITY_SPACING_METRES: i32 = 20;
pub const LIGHT_CYCLE_SECONDS: i32 = 30;
pub const GREEN_SECONDS: i32 = 13;
pub const START_UP_SECONDS: i64 = 1;

const HOUR_PERCENT: [i32; 24] = [8, 5, 5, 5, 8, 20, 45, 90, 100, 70, 55, 65, 85, 75, 55, 55, 65, 85, 100, 80, 55, 40, 25, 15];

pub fn hour(clock_seconds: i64) -> i32 { ((clock_seconds % DAY_SECONDS + DAY_SECONDS) % DAY_SECONDS / HOUR_SECONDS) as i32 }

pub fn hour_load_percent(clock_seconds: i64) -> i32 { HOUR_PERCENT[hour(clock_seconds) as usize] }

/// Free-flow speed by road kind, in metres per second.
pub fn speed_metres_per_second(kind: RoadKind) -> i32 {
    match kind {
        RoadKind::Arterial => 14,
        RoadKind::Local => 10,
        RoadKind::Rural => 20,
    }
}

fn base_percent(kind: RoadKind) -> i32 {
    match kind {
        RoadKind::Arterial => 55,
        RoadKind::Local => 35,
        RoadKind::Rural => 15,
    }
}

/// City cars on one direction of a segment, in percent of its capacity.
pub fn background_percent(segment: &Segment, clock_seconds: i64) -> i32 {
    MAX_BACKGROUND_PERCENT.min(base_percent(segment.kind) * (50 + segment.traffic_percent) / 100 * hour_load_percent(clock_seconds) / 100)
}

/// Moving vehicles one direction of a segment holds at capacity flow.
pub fn vehicles_at_capacity(segment: &Segment) -> i32 { (segment.lanes_per_direction * segment.length / CAPACITY_SPACING_METRES).max(1) }

/// Load of one direction with this many trucks on it, in percent of capacity.
pub fn load_percent(segment: &Segment, clock_seconds: i64, trucks: i32) -> i32 {
    background_percent(segment, clock_seconds) + trucks * TRUCK_PCE_PERCENT / vehicles_at_capacity(segment)
}

/// The volume-delay curve (BPR: 1 + 0.15 (v/c)^4), in thousandths of the free-flow time.
pub fn delay_permille(load_percent: i32) -> i64 {
    let x = load_percent.clamp(0, 1000) as i64;
    1000 + 150 * x * x * x * x / 100_000_000
}

/// Milliseconds to drive this many metres of a segment at a load, no faster than the vehicle's own top speed.
pub fn drive_millis(segment: &Segment, metres: i32, vehicle_speed: i32, load_percent: i32) -> i64 {
    let speed = speed_metres_per_second(segment.kind).min(vehicle_speed).max(1) as i64;
    metres as i64 * 1000 * delay_permille(load_percent) / 1000 / speed
}

/// Whether a truck may enter a direction whose load, counting it, would be load_with_truck.
pub fn admits(load_with_truck: i32, trucks_already: i32, queued_seconds: i64) -> bool {
    trucks_already == 0 || load_with_truck <= QUEUE_PERCENT || queued_seconds >= PATIENCE_SECONDS
}

/// The busiest cross street's background load at a segment's end node, in percent.
pub fn cross_percent(network: &RoadNetwork, segment: usize, forward: bool, clock_seconds: i64) -> i32 {
    let node = &network.nodes[network.end_node(segment, forward)];
    node.segments.iter().filter(|i| **i != segment).map(|i| background_percent(&network.segments[*i], clock_seconds)).max().unwrap_or(0)
}

/// Seconds a vehicle arriving at a segment's end node at this time waits there.
pub fn junction_wait_seconds(network: &RoadNetwork, segment: usize, forward: bool, arrival_seconds: i64) -> i64 {
    if !network.stops(segment, forward) {
        return 0;
    }
    let node = &network.nodes[network.end_node(segment, forward)];
    if node.control == JunctionControl::TrafficLight {
        let wait = seconds_until_green(node, network.segments[segment].dx != 0, arrival_seconds);
        return if wait == 0 { 0 } else { wait + START_UP_SECONDS };
    }
    2 + cross_percent(network, segment, forward, arrival_seconds) as i64 / 30
}

/// What route planning expects a junction to cost, in milliseconds.
pub fn junction_estimate_millis(network: &RoadNetwork, segment: usize, forward: bool, clock_seconds: i64) -> i64 {
    if !network.stops(segment, forward) {
        return 0;
    }
    if network.nodes[network.end_node(segment, forward)].control == JunctionControl::TrafficLight {
        return 6000;
    }
    2000 + cross_percent(network, segment, forward, clock_seconds) as i64 * 1000 / 30
}

/// Light phase: a stable offset per node so neighbouring lights do not switch together.
pub fn light_offset(node_id: &str) -> i32 {
    let mut hash: u32 = 2_166_136_261;
    for c in node_id.encode_utf16() {
        hash ^= u32::from(c);
        hash = hash.wrapping_mul(16_777_619);
    }
    (hash % LIGHT_CYCLE_SECONDS as u32) as i32
}

/// Seconds into the node's cycle at a time: traffic along X has green from 0, along Z from half the cycle.
pub fn light_phase(node: &Node, seconds: f64) -> f64 {
    let phase = (seconds + node.light_offset as f64) % LIGHT_CYCLE_SECONDS as f64;
    if phase < 0.0 { phase + LIGHT_CYCLE_SECONDS as f64 } else { phase }
}

pub fn is_green(node: &Node, along_x: bool, seconds: f64) -> bool {
    let start = if along_x { 0.0 } else { (LIGHT_CYCLE_SECONDS / 2) as f64 };
    let phase = light_phase(node, seconds);
    phase >= start && phase < start + GREEN_SECONDS as f64
}

/// Whole seconds from an arrival until the approach's light is green (0 if it already is).
pub fn seconds_until_green(node: &Node, along_x: bool, arrival_seconds: i64) -> i64 {
    let start = if along_x { 0 } else { (LIGHT_CYCLE_SECONDS / 2) as i64 };
    let phase = light_phase(node, arrival_seconds as f64) as i64;
    if phase >= start && phase < start + GREEN_SECONDS as i64 {
        return 0;
    }
    let cycle = LIGHT_CYCLE_SECONDS as i64;
    ((start - phase) % cycle + cycle) % cycle
}
