// Presentation traffic (Unity CityCars): deterministic stop-line discharge and nose-to-tail queues, never gameplay state.
use crate::world::{layout::JunctionControl, roads::{self, RoadNetwork}};

pub const GAP: f32 = 7.0;
pub const CAR_HALF_LENGTH: f32 = 2.3;

#[derive(Clone, Debug)]
pub struct Car { pub along: f32, pub key: u32, pub speed: f32, pub stop: f32 }

pub fn lane(network: &RoadNetwork, segment: usize, forward: bool, index: i32, seconds: f64, stop_back: f32) -> Vec<Car> {
    let s = &network.segments[segment];
    if s.length < 15 { return vec![]; }
    let load = roads::background_percent(s, seconds.floor() as i64);
    let flow = load as f64 / 100.0 * s.capacity_per_hour as f64 / 3600.0 / s.lanes_per_direction as f64;
    if flow <= 0.0 || 1.0 / flow > 600.0 { return vec![]; }
    let headway = 1.0 / flow;
    let speed = roads::speed_metres_per_second(s.kind) as f64 * 1000.0 / roads::delay_permille(load) as f64;
    let mut seed = 2166136261u32;
    for c in s.id.encode_utf16() { seed = (seed ^ c as u32).wrapping_mul(16777619); }
    seed = (seed ^ ((if forward { 1 } else { 2 }) * 31 + index as u32 * 977)).wrapping_mul(16777619);
    let phase = (seed % 10000) as f64 / 10000.0 * headway;
    let node = &network.nodes[network.end_node(segment, forward)];
    let stops = network.stops(segment, forward);
    let line = if stops { (s.length as f32 * 0.5).max(s.length as f32 - stop_back - CAR_HALF_LENGTH) as f64 } else { s.length as f64 };
    let first = ((seconds - 90.0 - s.length as f64 / speed - phase) / headway).floor() as i64;
    let last = ((seconds - phase) / headway).floor() as i64;
    let mut previous = f64::NEG_INFINITY;
    let mut waiting = 0;
    let mut cars = vec![];
    for k in first..=last {
        let entered = k as f64 * headway + phase;
        let arrival = entered + line / speed;
        let mut departure = arrival;
        if stops {
            departure = arrival.max(previous + 2.0);
            if node.control == JunctionControl::TrafficLight {
                let start = if s.dx != 0 { 0.0 } else { (roads::LIGHT_CYCLE_SECONDS / 2) as f64 };
                let phase = roads::light_phase(node, departure);
                if phase < start || phase >= start + roads::GREEN_SECONDS as f64 {
                    departure += (start - phase).rem_euclid(roads::LIGHT_CYCLE_SECONDS as f64);
                }
            } else { departure = departure.max(arrival + 2.0); }
        }
        previous = departure;
        let mut moving = speed as f32;
        let mut stop = f32::INFINITY;
        let along = if seconds >= departure { line + speed * (seconds - departure) } else {
            let free = speed * (seconds - entered);
            let hold = line - waiting as f64 * GAP as f64;
            if free >= hold { moving = 0.0; }
            if stops && entered + hold / speed < departure - 0.05 { stop = hold as f32; }
            waiting += 1;
            free.min(hold)
        };
        if along >= 0.0 && along <= s.length as f64 { cars.push(Car { along: along as f32, key: seed ^ (k as u32).wrapping_mul(2654435761), speed: moving, stop }); }
    }
    keep_gaps(&mut cars);
    cars
}

pub fn keep_gaps(cars: &mut Vec<Car>) {
    cars.sort_by(|a, b| b.along.total_cmp(&a.along));
    for i in 1..cars.len() {
        let (ahead, rest) = cars.split_at_mut(i);
        let a = &ahead[i - 1];
        let b = &mut rest[0];
        if b.along > a.along - GAP { b.along = a.along - GAP; b.speed = b.speed.min(a.speed); }
        b.stop = b.stop.min(a.stop - GAP);
    }
    cars.retain(|c| c.along >= 0.0);
}

pub fn hold_behind(cars: &mut Vec<Car>, at: f32, behind: f32, ahead: f32) {
    for car in cars.iter_mut() {
        if car.along > at - behind && car.along <= at + ahead {
            car.along = at - behind;
            car.speed = 0.0;
            car.stop = car.stop.min(car.along);
        }
    }
    keep_gaps(cars);
}

pub fn may_cross(node: &roads::Node, along_x: bool, width: f32, speed: f32, seconds: f64, car_near: bool) -> bool {
    if node.control != JunctionControl::TrafficLight { return !car_near; }
    let into = roads::light_phase(node, seconds) - if along_x { 0.0 } else { (roads::LIGHT_CYCLE_SECONDS / 2) as f64 };
    into >= 0.0 && into < roads::GREEN_SECONDS as f64 && into + width as f64 / speed as f64 + 1.0 <= roads::LIGHT_CYCLE_SECONDS as f64 / 2.0
}
