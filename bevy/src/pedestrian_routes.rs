// Local-only sidewalk walkers (Unity AmbientPedestrians): follow junction corners and crosswalks, waiting for lights or cars.
use std::collections::VecDeque;
use bevy::prelude::*;
use food_factory_bevy::world::{layout::RoadKind, random, roads::RoadNetwork};

#[derive(Resource, Default)]
pub struct Ambient {
    pub walkers: Vec<Pedestrian>,
    corners: Vec<Corner>,
    serial: u64,
    world: String,
}

struct Corner { span: Vec2, arms: [Option<usize>; 4] }
pub struct Pedestrian {
    pub id: String,
    pub point: Vec2,
    pub heading: Vec2,
    path: VecDeque<Vec2>,
    node: usize,
    side: Vec2,
    back: usize,
    choice: u64,
    pending: Option<(usize, f64)>,
}

fn direction(dx: i32, dz: i32) -> usize { if dx > 0 { 0 } else if dx < 0 { 1 } else if dz > 0 { 2 } else { 3 } }
fn node_point(net: &RoadNetwork, node: usize) -> Vec2 { Vec2::new(net.nodes[node].x as f32, net.nodes[node].z as f32) }

impl Ambient {
    pub fn clear(&mut self) { self.walkers.clear(); self.world.clear(); }

    fn corner(&self, net: &RoadNetwork, node: usize, side: Vec2) -> Vec2 {
        node_point(net, node) + side * (self.corners[node].span - Vec2::splat(0.5)).max(Vec2::ZERO)
    }

    fn leg(&self, net: &RoadNetwork, segment: usize, node: usize, side: Vec2) -> Vec2 {
        let s = &net.segments[segment];
        let lateral = s.width as f32 * 0.5 - 0.5;
        let mut offset = side * (self.corners[node].span - Vec2::splat(0.5)).max(Vec2::ZERO);
        if s.dx != 0 { offset.y = side.y * lateral; } else { offset.x = side.x * lateral; }
        node_point(net, node) + offset
    }

    pub fn tick(&mut self, world: &str, net: &RoadNetwork, clock: f64, dt: f32, here: Vec2, cars: &[Vec2]) {
        if self.world != world {
            self.clear(); self.world = world.into(); self.serial = 0;
            self.corners = net.nodes.iter().map(|_| Corner { span: Vec2::ZERO, arms: [None; 4] }).collect();
            for s in net.segments.iter().filter(|s| s.kind != RoadKind::Rural) {
                for (node, dx, dz) in [(s.from, s.dx, s.dz), (s.to, -s.dx, -s.dz)] {
                    let c = &mut self.corners[node];
                    if s.dx != 0 { c.span.y = c.span.y.max(s.width as f32 * 0.5); } else { c.span.x = c.span.x.max(s.width as f32 * 0.5); }
                    c.arms[direction(dx, dz)] = Some(s.index);
                }
            }
        }
        let mut walkers = std::mem::take(&mut self.walkers);
        walkers.retain(|w| w.point.distance(here) < 150.0);
        for w in &mut walkers {
            if w.path.is_empty() { self.choose(net, w, clock, cars); }
            let mut step = dt.min(0.25) * 1.3;
            while let Some(&next) = w.path.front() {
                let gap = next - w.point;
                let distance = gap.length();
                if distance > 0.001 { w.heading = gap / distance; }
                if distance <= step { w.point = next; step -= distance; w.path.pop_front(); }
                else { w.point += gap / distance * step; break; }
            }
        }
        for s in net.segments.iter().filter(|s| s.kind != RoadKind::Rural && s.length >= 6) {
            if walkers.len() >= 28 { break; }
            let a = node_point(net, s.from); let b = node_point(net, s.to);
            let close = here.clamp(a.min(b), a.max(b));
            if close.distance(here) > 90.0 { continue; }
            for side in [-1.0, 1.0] {
                if walkers.len() >= 28 { break; }
                let seed = random::mix(self.serial.wrapping_add(s.index as u64)); self.serial += 1;
                let forward = seed & 1 == 0;
                let (from, to) = if forward { (s.from, s.to) } else { (s.to, s.from) };
                let heading = Vec2::new(s.dx as f32, s.dz as f32) * if forward { 1.0 } else { -1.0 };
                let end_side = if s.dx != 0 { Vec2::new(-heading.x, side) } else { Vec2::new(side, -heading.y) };
                let start_side = if s.dx != 0 { Vec2::new(heading.x, side) } else { Vec2::new(side, heading.y) };
                let start = self.leg(net, s.index, from, start_side); let end = self.leg(net, s.index, to, end_side);
                let point = start.lerp(end, ((seed >> 8) % 1000) as f32 / 1000.0);
                if point.distance(here) > 90.0 || walkers.iter().any(|w| w.point.distance(point) < 8.0) { continue; }
                walkers.push(Pedestrian { id: format!("ambient-{}", self.serial), point, heading,
                    path: VecDeque::from([end, self.corner(net, to, end_side)]), node: to, side: end_side,
                    back: direction(-heading.x as i32, -heading.y as i32), choice: seed, pending: None });
            }
        }
        self.walkers = walkers;
    }

    fn crosses(&self, w: &Pedestrian, m: usize) -> bool {
        let arm = if m == 4 { if w.side.y > 0.0 { 2 } else { 3 } } else if w.side.x > 0.0 { 0 } else { 1 };
        self.corners[w.node].arms[arm].is_some()
    }

    fn may_cross(&self, net: &RoadNetwork, w: &Pedestrian, m: usize, clock: f64, cars: &[Vec2]) -> bool {
        if !self.crosses(w, m) { return true; }
        let span = self.corners[w.node].span;
        let middle = node_point(net, w.node) + if m == 4 { Vec2::new(0.0, w.side.y * (span.y + 1.5)) } else { Vec2::new(w.side.x * (span.x + 1.5), 0.0) };
        food_factory_bevy::traffic::may_cross(&net.nodes[w.node], m == 4, if m == 4 { span.x * 2.0 } else { span.y * 2.0 }, 1.3, clock,
            cars.iter().any(|p| p.distance(middle) < 16.0))
    }

    fn choose(&self, net: &RoadNetwork, w: &mut Pedestrian, clock: f64, cars: &[Vec2]) {
        let c = &self.corners[w.node];
        let mut moves = vec![];
        for m in [if w.side.x > 0.0 { 0 } else { 1 }, if w.side.y > 0.0 { 2 } else { 3 }] {
            if c.arms[m].is_some() { moves.extend([m; 3]); }
        }
        if c.span.x > 0.55 && (!self.crosses(w, 4) || c.span.y > 0.0) { moves.push(4); }
        if c.span.y > 0.55 && (!self.crosses(w, 5) || c.span.x > 0.0) { moves.push(5); }
        if moves.iter().any(|m| *m != w.back) { moves.retain(|m| *m != w.back); }
        if moves.is_empty() { return; }
        let m = if let Some((m, since)) = w.pending {
            if clock - since < 35.0 { m } else { w.pending = None; w.back = m; return; }
        } else { w.choice = random::mix(w.choice); moves[w.choice as usize % moves.len()] };
        if m >= 4 && !self.may_cross(net, w, m, clock, cars) { w.pending.get_or_insert((m, clock)); return; }
        w.pending = None;
        if m < 4 {
            let s = &net.segments[c.arms[m].unwrap()];
            let other = if s.from == w.node { s.to } else { s.from };
            w.path.push_back(self.leg(net, s.index, w.node, w.side));
            if m < 2 { w.side.x = -w.side.x; } else { w.side.y = -w.side.y; }
            w.path.push_back(self.leg(net, s.index, other, w.side));
            w.path.push_back(self.corner(net, other, w.side));
            w.node = other; w.back = m ^ 1;
        } else {
            if self.crosses(w, m) {
                let p = node_point(net, w.node); let offset = w.side * (c.span - Vec2::splat(0.5)).max(Vec2::ZERO);
                if m == 4 {
                    let z = p.y + w.side.y * (c.span.y + 1.5);
                    w.path.extend([Vec2::new(p.x + offset.x, z), Vec2::new(p.x - offset.x, z)]);
                } else {
                    let x = p.x + w.side.x * (c.span.x + 1.5);
                    w.path.extend([Vec2::new(x, p.y + offset.y), Vec2::new(x, p.y - offset.y)]);
                }
            }
            if m == 4 { w.side.x = -w.side.x; } else { w.side.y = -w.side.y; }
            w.path.push_back(self.corner(net, w.node, w.side)); w.back = m;
        }
    }
}
