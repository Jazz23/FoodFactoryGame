// Seed + settings -> WorldLayout, ported from Unity's WorldGenerator (generator v8). Generation is deterministic: every random
// draw comes from WorldRandom streams forked from the seed, every list is built in a fixed order, and all geometry and land
// heights are integers. A layout that fails validation is discarded and generation retries with derive_seed(seed, attempt).
//
// Build steps (canonical frame, industrial to the south; the finished layout is then turned a seeded number of quarter turns):
// arterial grid -> districts -> river -> local streets -> road graph -> rail lines -> land heights -> junction controls ->
// bridges -> level crossings -> stations -> buildings along every block edge -> farms along rural spurs -> trees -> IDs ->
// starting restaurant -> turn -> lots.
use std::collections::{BTreeSet, HashMap};

use super::geometry::{self as geo, Box2, BoxIndex};
use super::layout::*;
use super::random::{derive_seed, mix, WorldRandom};
use super::settings::{DistrictProfile, SizeRange, WorldSettings};
use super::terrain::WorldTerrain;
use super::validator;

pub const VERSION: i32 = 8;
const ONE: i64 = 1024;

#[derive(Clone, Debug)]
pub struct GenerationAttempt {
    pub attempt: i32,
    pub seed: u64,
    pub problems: Vec<String>,
}

#[derive(Clone, Debug)]
pub struct WorldGenerationResult {
    pub layout: WorldLayout,
    pub attempts: Vec<GenerationAttempt>,
}

pub fn generate(requested_seed: &str, seed: u64, settings: &WorldSettings) -> Result<WorldGenerationResult, String> {
    let mut attempts = vec![];
    for attempt in 0..settings.max_attempts {
        let attempt_seed = derive_seed(seed, attempt);
        let mut layout = build(attempt_seed, settings);
        layout.requested_seed = requested_seed.to_string();
        layout.attempt = attempt;
        let problems = validator::validate(&layout, settings);
        let ok = problems.is_empty();
        attempts.push(GenerationAttempt { attempt, seed: attempt_seed, problems });
        if ok {
            return Ok(WorldGenerationResult { layout, attempts });
        }
    }
    let last = attempts.last().map(|a| a.problems.iter().take(5).cloned().collect::<Vec<_>>().join("; ")).unwrap_or_default();
    Err(format!("No valid world after {} attempts; last problems: {last}", attempts.len()))
}

/// One attempt, unvalidated.
pub fn build(seed: u64, settings: &WorldSettings) -> WorldLayout { Builder::new(seed, settings).build() }

#[derive(Clone, Copy, Debug)]
struct Span {
    vertical: bool,
    fixed: i32,
    from: i32,
    to: i32,
    kind: RoadKind,
    width: i32,
}

#[derive(Clone, Copy)]
struct RailCrossing {
    x: i32,
    z: i32,
    rail_vertical: bool,
    road_width: i32,
}

struct Builder<'a> {
    seed: u64,
    s: &'a WorldSettings,
    c: i32,
    m: i32,
    ring: i32,
    n: usize,
    xs: Vec<i32>,
    zs: Vec<i32>,
    kinds: Vec<Vec<DistrictKind>>,
    local_x: Vec<Vec<Vec<i32>>>,
    local_z: Vec<Vec<Vec<i32>>>,
    spans: Vec<Span>,
    road_index: BoxIndex<usize>,
    reserved: BoxIndex<i32>,
    water: BoxIndex<i32>,
    placed: BoxIndex<i32>,
    clear: BoxIndex<i32>,
    road_vertical: Vec<bool>,
    layout: WorldLayout,
    river: Vec<WorldCell>,
    rail_x: i32,
    farm_z: i32,
    terrain_salt: u64,
}

/// Smoothstep on 0..ONE.
fn smooth(t: i64) -> i64 { t * t * (3 * ONE - 2 * t) / (ONE * ONE) }

/// `damped` percent at distance 0 or less, rising smoothly to 100 at `fade`.
fn fade(distance: i32, damped: i32, fade: i32) -> i32 {
    if distance <= 0 {
        damped
    } else if distance >= fade {
        100
    } else {
        damped + ((100 - damped) as i64 * smooth(distance as i64 * ONE / fade as i64) / ONE) as i32
    }
}

/// Value noise in [-amplitude, amplitude] on a lattice of `cell` metres, smoothly interpolated.
fn noise(salt: u64, x: i32, z: i32, cell: i32, amplitude: i32) -> i32 {
    let cell = cell as i64;
    let ix = geo::floor_div(x as i64, cell);
    let iz = geo::floor_div(z as i64, cell);
    let tx = smooth((x as i64 - ix * cell) * ONE / cell);
    let tz = smooth((z as i64 - iz * cell) * ONE / cell);
    let corner = |cx: i64, cz: i64| -> i64 {
        let value = mix(salt.wrapping_mul(0x9E37_79B9_7F4A_7C15) ^ (cx as u64).wrapping_mul(0xC2B2_AE3D_27D4_EB4F) ^ (cz as u64).wrapping_mul(0x1656_67B1_9E37_79F9));
        (value % (2 * amplitude as u64 + 1)) as i64 - amplitude as i64
    };
    let south = corner(ix, iz) * ONE + (corner(ix + 1, iz) - corner(ix, iz)) * tx;
    let north = corner(ix, iz + 1) * ONE + (corner(ix + 1, iz + 1) - corner(ix, iz + 1)) * tx;
    geo::floor_div(south * ONE + (north - south) * tz, ONE * ONE) as i32
}

fn grow(b: Box2, doubled: i32) -> Box2 { Box2::new(b.x0 - doubled, b.z0 - doubled, b.x1 + doubled, b.z1 + doubled) }

fn variant(rng: &mut WorldRandom, count: i32) -> String { ((b'a' + rng.next(count) as u8) as char).to_string() }

fn price(rect: WorldRect, cents_per_cell: i64, percent: i32) -> i64 {
    rect.width as i64 * rect.depth as i64 * cents_per_cell * percent as i64 / 100
}

fn turned_point(point: WorldCell) -> WorldCell {
    let (x, z) = geo::turn_point(point.x, point.z);
    WorldCell::new(x, z)
}

fn category_name(category: BuildingCategory) -> &'static str {
    match category {
        BuildingCategory::Restaurant => "restaurant",
        BuildingCategory::Factory => "factory",
        BuildingCategory::Farm => "farm",
        BuildingCategory::Station => "station",
        BuildingCategory::House => "house",
        BuildingCategory::Apartment => "apartment",
        BuildingCategory::Office => "office",
    }
}

impl<'a> Builder<'a> {
    fn new(seed: u64, s: &'a WorldSettings) -> Self {
        let n = s.superblocks as usize;
        Self {
            seed,
            s,
            c: s.city_half_size,
            m: s.map_half_size,
            ring: s.map_half_size - s.outer_ring_inset,
            n,
            xs: vec![0; n + 1],
            zs: vec![0; n + 1],
            kinds: vec![vec![DistrictKind::Residential; n]; n],
            local_x: vec![vec![vec![]; n]; n],
            local_z: vec![vec![vec![]; n]; n],
            spans: vec![],
            road_index: BoxIndex::new(),
            reserved: BoxIndex::new(),
            water: BoxIndex::new(),
            placed: BoxIndex::new(),
            clear: BoxIndex::new(),
            road_vertical: vec![],
            layout: WorldLayout::default(),
            river: vec![],
            rail_x: 0,
            farm_z: 0,
            terrain_salt: 0,
        }
    }

    fn river_half(&self) -> i32 { self.s.river_width / 2 }

    fn build(mut self) -> WorldLayout {
        self.layout.generator_version = VERSION;
        self.layout.seed = self.seed;
        self.layout.city_half_size = self.c;
        self.layout.map_half_size = self.m;
        self.arterials();
        self.districts();
        self.make_river();
        self.local_streets();
        self.road_graph();
        self.rails();
        self.terrain();
        self.controls();
        self.bridges();
        self.level_crossings();
        self.stations();
        self.blocks();
        self.farms();
        self.trees();
        self.assign_ids();
        self.pick_start();
        let turns = WorldRandom::fork(self.seed, "orientation").next(4);
        for _ in 0..turns {
            self.turn();
        }
        self.lots();
        self.layout
    }

    fn arterials(&mut self) {
        let mut rng = WorldRandom::fork(self.seed, "arterials");
        let spacing = 2 * self.c / self.n as i32;
        for index in 0..=self.n {
            let edge = index == 0 || index == self.n;
            let jitter = self.s.arterial_jitter;
            self.xs[index] = -self.c + index as i32 * spacing + if edge { 0 } else { rng.range(-jitter, jitter) };
            self.zs[index] = -self.c + index as i32 * spacing + if edge { 0 } else { rng.range(-jitter, jitter) };
        }
    }

    fn districts(&mut self) {
        let mut rng = WorldRandom::fork(self.seed, "districts");
        let n = self.n as i32;
        let centre = n / 2;
        let neighbours = [(centre - 1, centre), (centre + 1, centre), (centre, centre + 1), (centre, centre - 1)];
        let extra = neighbours[rng.next(neighbours.len() as i32) as usize];
        let anchor = if rng.next(2) == 0 { (0, n - 1) } else { (n - 1, n - 1) };
        for j in 0..n {
            for i in 0..n {
                self.kinds[i as usize][j as usize] = if j == 0 {
                    DistrictKind::Industrial
                } else if (i, j) == (centre, centre) || (i, j) == extra {
                    DistrictKind::Downtown
                } else if (i - anchor.0).abs() + (j - anchor.1).abs() <= 2 {
                    DistrictKind::Wealthy
                } else {
                    DistrictKind::Residential
                };
            }
        }
        for profile in &self.s.districts {
            let mut district = WorldDistrict {
                id: profile.id.to_string(),
                kind: profile.kind,
                areas: vec![],
                cuisines: profile.cuisines.clone(),
                min_recipe_tier: profile.min_recipe_tier,
                customers_per_hour: profile.customers_per_hour,
                traffic_percent: profile.traffic_percent,
                price_percent: profile.price_percent,
            };
            for j in 0..self.n {
                for i in 0..self.n {
                    if self.kinds[i][j] == profile.kind {
                        district.areas.push(WorldRect::new(self.xs[i], self.zs[j], self.xs[i + 1] - self.xs[i], self.zs[j + 1] - self.zs[j]));
                    }
                }
            }
            self.layout.districts.push(district);
        }
    }

    fn make_river(&mut self) {
        let mut rng = WorldRandom::fork(self.seed, "river");
        let row = rng.range(1, self.n as i32 - 1) as usize;
        let clearance = self.s.arterial_width / 2 + self.river_half() + self.s.river_bank + self.s.river_street_clearance + 20;
        let mut low = self.zs[row] + clearance;
        let mut high = self.zs[row + 1] - clearance;
        if high < low {
            low = (self.zs[row] + self.zs[row + 1]) / 2;
            high = low;
        }
        let step = self.s.river_meander_step;
        let mut controls = vec![];
        for _ in 0..=(2 * self.m / step + 1) {
            controls.push(rng.range(low, high));
        }
        let mut x = -self.m;
        while x <= self.m {
            let k = ((x + self.m) / step) as usize;
            let t = (x + self.m - k as i32 * step) as i64 * ONE / step as i64;
            let z = controls[k] + geo::floor_div((controls[k + 1] - controls[k]) as i64 * smooth(t), ONE) as i32;
            self.river.push(WorldCell::new(x, z));
            x += 20;
        }
        self.layout.rivers.push(WorldRiver {
            id: "river-1".into(),
            width: self.s.river_width,
            surface_drop_cm: self.s.river_surface_drop_cm,
            points: self.river.clone(),
        });
        for b in geo::corridor(&self.river, self.river_half() + self.s.river_bank) {
            self.water.add(b, 0);
        }
    }

    /// The river's centreline Z at x.
    fn river_z(&self, x: i32) -> i32 {
        let index = ((x + self.m) / 20).clamp(0, self.river.len() as i32 - 2) as usize;
        let a = self.river[index];
        let b = self.river[index + 1];
        let t = (x - a.x).clamp(0, 20);
        a.z + geo::floor_div((b.z - a.z) as i64 * t as i64, 20) as i32
    }

    fn local_streets(&mut self) {
        let mut rng = WorldRandom::fork(self.seed, "rail-position");
        let column = if rng.next(2) == 0 { 1 } else { self.n - 2 };
        self.rail_x = (self.xs[column] + self.xs[column + 1]) / 2 + rng.range(-10, 10);
        self.farm_z = -self.c - self.s.farm_line_offset;
        let mut rng = WorldRandom::fork(self.seed, "streets");
        let river_gap = self.river_half() + self.s.river_bank + self.s.local_width / 2 + self.s.river_street_clearance;
        for j in 0..self.n {
            for i in 0..self.n {
                let profile = self.s.profile(self.kinds[i][j]).clone();
                let (west, east) = (self.xs[i], self.xs[i + 1]);
                let rail_x = self.rail_x;
                let rail_clearance = self.s.rail_clearance;
                self.local_x[i][j] = self.locals(&mut rng, &profile, self.xs[i], self.xs[i + 1], |_, x| (x - rail_x).abs() >= rail_clearance);
                self.local_z[i][j] = self.locals(&mut rng, &profile, self.zs[j], self.zs[j + 1], |builder, z| {
                    let mut x = west;
                    while x <= east {
                        if (z - builder.river_z(x)).abs() < river_gap {
                            return false;
                        }
                        x += 10;
                    }
                    (z - builder.river_z(east)).abs() >= river_gap
                });
            }
        }
    }

    fn locals(&self, rng: &mut WorldRandom, profile: &DistrictProfile, from: i32, to: i32, allowed: impl Fn(&Self, i32) -> bool) -> Vec<i32> {
        let count = rng.range(profile.min_local_streets, profile.max_local_streets);
        let mut result = vec![];
        for index in 1..=count {
            let jitter = self.s.local_street_jitter;
            let position = from + (to - from) * index / (count + 1) + rng.range(-jitter, jitter);
            if allowed(self, position) {
                result.push(position);
            }
        }
        result
    }

    fn add_span(&mut self, vertical: bool, position: i32, from: i32, to: i32, kind: RoadKind) {
        let width = match kind {
            RoadKind::Arterial => self.s.arterial_width,
            RoadKind::Local => self.s.local_width,
            RoadKind::Rural => self.s.rural_width,
        };
        self.spans.push(Span { vertical, fixed: position, from, to, kind, width });
    }

    fn capacity(&self, kind: RoadKind) -> i32 {
        match kind {
            RoadKind::Arterial => self.s.arterial_capacity,
            RoadKind::Local => self.s.local_capacity,
            RoadKind::Rural => self.s.rural_capacity,
        }
    }

    fn road_graph(&mut self) {
        let (c, ring) = (self.c, self.ring);
        for index in 0..=self.n {
            self.add_span(true, self.xs[index], -c, c, RoadKind::Arterial);
            self.add_span(false, self.zs[index], -c, c, RoadKind::Arterial);
        }
        for j in 0..self.n {
            for i in 0..self.n {
                for x in self.local_x[i][j].clone() {
                    self.add_span(true, x, self.zs[j], self.zs[j + 1], RoadKind::Local);
                }
                for z in self.local_z[i][j].clone() {
                    self.add_span(false, z, self.xs[i], self.xs[i + 1], RoadKind::Local);
                }
            }
        }
        for index in 0..=self.n {
            self.add_span(true, self.xs[index], c, ring, RoadKind::Rural);
            self.add_span(true, self.xs[index], -ring, -c, RoadKind::Rural);
            self.add_span(false, self.zs[index], c, ring, RoadKind::Rural);
            self.add_span(false, self.zs[index], -ring, -c, RoadKind::Rural);
        }
        self.add_span(false, ring, -ring, ring, RoadKind::Rural);
        self.add_span(false, -ring, -ring, ring, RoadKind::Rural);
        self.add_span(true, ring, -ring, ring, RoadKind::Rural);
        self.add_span(true, -ring, -ring, ring, RoadKind::Rural);

        let mut cuts: Vec<Vec<i32>> = self.spans.iter().map(|s| vec![s.from, s.to]).collect();
        for a in 0..self.spans.len() {
            for b in 0..self.spans.len() {
                let vertical = self.spans[a];
                let horizontal = self.spans[b];
                if !vertical.vertical || horizontal.vertical {
                    continue;
                }
                if horizontal.from <= vertical.fixed && vertical.fixed <= horizontal.to && vertical.from <= horizontal.fixed && horizontal.fixed <= vertical.to {
                    cuts[a].push(horizontal.fixed);
                    cuts[b].push(vertical.fixed);
                }
            }
        }
        let mut points = BTreeSet::new();
        let mut pieces: Vec<((i32, i32), (i32, i32), Span)> = vec![];
        for (index, span) in self.spans.iter().enumerate() {
            let stops: Vec<i32> = cuts[index].iter().copied().collect::<BTreeSet<_>>().into_iter().collect();
            for pair in stops.windows(2) {
                let from = if span.vertical { (span.fixed, pair[0]) } else { (pair[0], span.fixed) };
                let to = if span.vertical { (span.fixed, pair[1]) } else { (pair[1], span.fixed) };
                points.insert(from);
                points.insert(to);
                pieces.push((from, to, *span));
            }
        }
        let mut ids = HashMap::new();
        for point in points {
            let id = format!("node-{:04}", self.layout.nodes.len() + 1);
            ids.insert(point, id.clone());
            self.layout.nodes.push(RoadNode { id, x: point.0, z: point.1, control: JunctionControl::None });
        }
        pieces.sort_by_key(|p| (p.0 .0, p.0 .1, p.1 .0, p.1 .1));
        for (from, to, span) in pieces {
            let segment = RoadSegment {
                id: format!("road-{:04}", self.layout.roads.len() + 1),
                from_id: ids[&from].clone(),
                to_id: ids[&to].clone(),
                kind: span.kind,
                width: span.width,
                capacity_per_hour: self.capacity(span.kind),
            };
            self.road_index.add(geo::strip(from.0, from.1, to.0, to.1, segment.width), self.layout.roads.len());
            self.road_vertical.push(span.vertical);
            self.layout.roads.push(segment);
        }
    }

    fn rails(&mut self) {
        let city = RailLine { id: "rail-city".into(), width: self.s.rail_width, points: vec![WorldCell::new(self.rail_x, self.farm_z), WorldCell::new(self.rail_x, self.m - 20)] };
        let farm = RailLine {
            id: "rail-farm".into(),
            width: self.s.rail_width,
            points: vec![WorldCell::new(-self.ring + 60, self.farm_z), WorldCell::new(self.ring - 60, self.farm_z)],
        };
        self.layout.rails.push(city);
        self.layout.rails.push(farm);
        for line in &self.layout.rails {
            for pair in line.points.windows(2) {
                self.reserved.add(geo::strip(pair[0].x, pair[0].z, pair[1].x, pair[1].z, line.width), -1);
            }
        }
    }

    fn terrain(&mut self) {
        self.terrain_salt = WorldRandom::fork(self.seed, "terrain").next_u64();
        let spacing = self.s.terrain_spacing;
        assert!(spacing > 0 && (2 * self.m) % spacing == 0, "terrain spacing must divide the map size");
        let samples = 2 * self.m / spacing + 1;
        let mut terrain = WorldTerrain { spacing, samples, heights_cm: vec![0; (samples * samples) as usize] };
        let origin = terrain.origin();
        let mut floor: HashMap<i32, i32> = HashMap::new();
        for z in 0..samples {
            for x in 0..samples {
                let px = origin + x * spacing;
                let pz = origin + z * spacing;
                let level = match floor.get(&px) {
                    Some(level) => *level,
                    None => {
                        let mut sum = 0i64;
                        for k in -2..=2 {
                            sum += self.land(px + 40 * k, self.river_z(px + 40 * k)) as i64;
                        }
                        let level = geo::floor_div(sum, 5) as i32 - self.s.river_valley_drop_cm;
                        floor.insert(px, level);
                        level
                    }
                };
                let land = self.land(px, pz);
                let distance = (pz - self.river_z(px)).abs() - self.river_half() - self.s.river_valley_flat;
                let weight = if distance <= 0 {
                    ONE
                } else if distance >= self.s.river_valley_fade {
                    0
                } else {
                    ONE - smooth(distance as i64 * ONE / self.s.river_valley_fade as i64)
                };
                terrain.heights_cm[(z * samples + x) as usize] = land + geo::floor_div((level - land) as i64 * weight, ONE) as i32;
            }
        }
        self.layout.terrain = terrain;
    }

    fn land(&self, x: i32, z: i32) -> i32 {
        let mut relief = 0i64;
        for octave in 0..self.s.terrain_lattice_metres.len() {
            relief += noise(self.terrain_salt.wrapping_add(octave as u64), x, z, self.s.terrain_lattice_metres[octave], self.s.terrain_amplitude_cm[octave]) as i64;
        }
        let outside = x.abs().max(z.abs()) - self.c;
        let mut percent = fade(outside, self.s.city_relief_percent, self.s.terrain_city_fade);
        let rail_distance = (if z >= self.farm_z && z <= self.m { (x - self.rail_x).abs() } else { i32::MAX / 2 }).min((z - self.farm_z).abs());
        percent = percent.min(fade(rail_distance - self.s.rail_width, self.s.rail_relief_percent, self.s.rail_relief_fade));
        geo::floor_div(relief * percent as i64, 100) as i32
    }

    fn elevation(&self, cell: WorldCell) -> i32 { self.layout.terrain.cell_height_cm(cell.x, cell.z) }

    /// The entrance height, or None when the footprint rises or falls too far from it.
    fn level_for(&self, rect: WorldRect, entrance: WorldCell) -> Option<i32> {
        let level = self.elevation(entrance);
        let t = &self.layout.terrain;
        let (x0, z0, x1, z1) = (2 * rect.x, 2 * rect.z, 2 * (rect.x + rect.width), 2 * (rect.z + rect.depth));
        let heights = [t.height_cm(x0, z0), t.height_cm(x1, z0), t.height_cm(x0, z1), t.height_cm(x1, z1), t.height_cm((x0 + x1) / 2, (z0 + z1) / 2)];
        if heights.iter().any(|h| (h - level).abs() > self.s.max_footprint_rise_cm) { None } else { Some(level) }
    }

    fn controls(&mut self) {
        let mut kinds: HashMap<String, Vec<RoadKind>> = HashMap::new();
        for road in &self.layout.roads {
            for id in [&road.from_id, &road.to_id] {
                kinds.entry(id.clone()).or_default().push(road.kind);
            }
        }
        for index in 0..self.layout.nodes.len() {
            let node = &self.layout.nodes[index];
            let list = kinds.get(&node.id).cloned().unwrap_or_default();
            let arterials = list.iter().filter(|k| **k == RoadKind::Arterial).count();
            let downtown = self.district_at(2 * node.x, 2 * node.z) == Some(DistrictKind::Downtown);
            let control = if list.len() <= 2 {
                JunctionControl::None
            } else if arterials >= 3 || (downtown && list.len() == 4 && arterials >= 2) {
                JunctionControl::TrafficLight
            } else {
                JunctionControl::StopSign
            };
            self.layout.nodes[index].control = control;
        }
    }

    fn node_map(&self) -> HashMap<String, (i32, i32)> { self.layout.nodes.iter().map(|n| (n.id.clone(), (n.x, n.z))).collect() }

    fn bridges(&mut self) {
        let nodes = self.node_map();
        let river = self.layout.rivers[0].clone();
        let mut ways = vec![];
        for road in &self.layout.roads {
            let a = nodes[&road.from_id];
            let b = nodes[&road.to_id];
            ways.push((road.id.clone(), a, b, road.width));
        }
        for line in &self.layout.rails {
            for pair in line.points.windows(2) {
                ways.push((line.id.clone(), (pair[0].x, pair[0].z), (pair[1].x, pair[1].z), line.width));
            }
        }
        for (id, a, b, width) in ways {
            let vertical = a.0 == b.0;
            let fixed = if vertical { a.0 } else { a.1 };
            let from = if vertical { a.1.min(b.1) } else { a.0.min(b.0) };
            let to = if vertical { a.1.max(b.1) } else { a.0.max(b.0) };
            self.bridge(&id, &river, vertical, fixed, from, to, width);
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn bridge(&mut self, carries_id: &str, river: &WorldRiver, vertical: bool, fixed_at: i32, from: i32, to: i32, width: i32) {
        for (along, dx, dz) in geo::crossings(&river.points, vertical, fixed_at, from, to) {
            let across = if vertical { dx.abs() } else { dz.abs() } as i64;
            let slant = if vertical { dz.abs() } else { dx.abs() } as i64;
            let length = geo::ceil_sqrt(dx as i64 * dx as i64 + dz as i64 * dz as i64);
            let reach = (((self.river_half() + self.s.river_bank) as i64 * length + (width / 2) as i64 * slant + across - 1) / across) as i32;
            let start = from.max(along - reach);
            let end = to.min(along + reach);
            let bridge = WorldBridge {
                id: format!("bridge-{:04}", self.layout.bridges.len() + 1),
                carries_id: carries_id.to_string(),
                river_id: river.id.clone(),
                from: if vertical { WorldCell::new(fixed_at, start) } else { WorldCell::new(start, fixed_at) },
                to: if vertical { WorldCell::new(fixed_at, end) } else { WorldCell::new(end, fixed_at) },
            };
            self.clear.add(geo::strip(bridge.from.x, bridge.from.z, bridge.to.x, bridge.to.z, width + 4), 0);
            self.layout.bridges.push(bridge);
        }
    }

    fn level_crossings(&mut self) {
        let nodes = self.node_map();
        let rails = self.layout.rails.clone();
        for line in &rails {
            for pair in line.points.windows(2) {
                let (p, q) = (pair[0], pair[1]);
                let rail_vertical = p.x == q.x;
                let mut found: Vec<(i32, String)> = vec![];
                for road in &self.layout.roads {
                    let a = nodes[&road.from_id];
                    let b = nodes[&road.to_id];
                    if (a.0 == b.0) == rail_vertical {
                        continue;
                    }
                    if rail_vertical {
                        if a.1 > p.z.min(q.z) && a.1 < p.z.max(q.z) && p.x > a.0.min(b.0) && p.x < a.0.max(b.0) {
                            found.push((a.1, road.id.clone()));
                        }
                    } else if a.0 > p.x.min(q.x) && a.0 < p.x.max(q.x) && p.z > a.1.min(b.1) && p.z < a.1.max(b.1) {
                        found.push((a.0, road.id.clone()));
                    }
                }
                found.sort_by_key(|f| f.0);
                for (along, road) in found {
                    let crossing = LevelCrossing {
                        id: format!("crossing-{:04}", self.layout.crossings.len() + 1),
                        road_id: road,
                        rail_id: line.id.clone(),
                        x: if rail_vertical { p.x } else { along },
                        z: if rail_vertical { along } else { p.z },
                    };
                    self.clear.add(Box2::new(2 * crossing.x - 24, 2 * crossing.z - 24, 2 * crossing.x + 24, 2 * crossing.z + 24), 0);
                    self.layout.crossings.push(crossing);
                }
            }
        }
    }

    fn stations(&mut self) {
        let mut rng = WorldRandom::fork(self.seed, "stations");
        let rails = self.layout.rails.clone();
        for line in &rails {
            let crossings = self.rail_crossings(line);
            if line.id == "rail-farm" {
                let groups = self.s.farm_line_stations as usize;
                for group in 0..groups {
                    let members: Vec<RailCrossing> = crossings.iter().enumerate().filter(|(index, _)| index * groups / crossings.len() == group).map(|(_, c)| *c).collect();
                    self.place_in_group(&mut rng, line, &members);
                }
            } else {
                let mut zones: Vec<String> = vec![];
                for crossing in &crossings {
                    let zone = self.zone(crossing.x, crossing.z);
                    if !zones.contains(&zone) {
                        zones.push(zone);
                    }
                }
                for zone in zones {
                    let members: Vec<RailCrossing> = crossings.iter().filter(|c| self.zone(c.x, c.z) == zone).copied().collect();
                    self.place_in_group(&mut rng, line, &members);
                }
            }
        }
    }

    fn place_in_group(&mut self, rng: &mut WorldRandom, line: &RailLine, group: &[RailCrossing]) {
        if group.is_empty() {
            return;
        }
        let start = rng.next(group.len() as i32) as usize;
        for offset in 0..group.len() {
            if self.try_station(rng, line, group[(start + offset) % group.len()]) {
                return;
            }
        }
    }

    fn rail_crossings(&self, line: &RailLine) -> Vec<RailCrossing> {
        let mut result = vec![];
        for pair in line.points.windows(2) {
            let (a, b) = (pair[0], pair[1]);
            let vertical = a.x == b.x;
            let low = if vertical { a.z.min(b.z) } else { a.x.min(b.x) };
            let high = if vertical { a.z.max(b.z) } else { a.x.max(b.x) };
            let fixed_at = if vertical { a.x } else { a.z };
            let mut spans: Vec<&Span> = self.spans.iter().filter(|s| s.vertical != vertical && s.fixed > low && s.fixed < high && s.from <= fixed_at && fixed_at <= s.to).collect();
            spans.sort_by_key(|s| s.fixed);
            for span in spans {
                result.push(if vertical {
                    RailCrossing { x: fixed_at, z: span.fixed, rail_vertical: true, road_width: span.width }
                } else {
                    RailCrossing { x: span.fixed, z: fixed_at, rail_vertical: false, road_width: span.width }
                });
            }
        }
        result
    }

    fn try_station(&mut self, rng: &mut WorldRandom, line: &RailLine, crossing: RailCrossing) -> bool {
        let length = self.s.station_length;
        let depth = self.s.station_depth;
        let half = crossing.road_width / 2;
        let rail_half = line.width / 2;
        let sides = [(1, 1), (1, -1), (-1, 1), (-1, -1)];
        let first = rng.next(sides.len() as i32) as usize;
        for offset in 0..sides.len() {
            let (along_side, across_side) = sides[(first + offset) % sides.len()];
            let (rect, facing) = if crossing.rail_vertical {
                let z = if along_side > 0 { crossing.z + half + self.s.setback } else { crossing.z - half - self.s.setback - length };
                let x = if across_side > 0 { crossing.x + rail_half + 1 } else { crossing.x - rail_half - 1 - depth };
                (WorldRect::new(x, z, depth, length), if along_side > 0 { Facing::South } else { Facing::North })
            } else {
                let x = if along_side > 0 { crossing.x + half + self.s.setback } else { crossing.x - half - self.s.setback - length };
                let z = if across_side > 0 { crossing.z + rail_half + 1 } else { crossing.z - rail_half - 1 - depth };
                (WorldRect::new(x, z, length, depth), if along_side > 0 { Facing::West } else { Facing::East })
            };
            let b = geo::of_rect(&geo::lot_rect_of(rect, facing, self.s.setback_for(BuildingCategory::Station)));
            if !self.inside_ring(rect) || self.road_index.any(&b) || self.reserved.any(&b) || self.water.any(&b) {
                continue;
            }
            let doors = geo::doors(rect, facing, 1);
            let Some(level) = self.level_for(rect, doors[0]) else { continue };
            let district = self.district_at(2 * rect.x + rect.width, 2 * rect.z + rect.depth);
            let percent = district.map_or(100, |d| self.s.profile(d).price_percent);
            let mut building = WorldBuilding::new(String::new(), BuildingCategory::Station);
            building.district_id = district.map_or(String::new(), |d| self.s.profile(d).id.to_string());
            building.x = rect.x;
            building.z = rect.z;
            building.width = rect.width;
            building.depth = rect.depth;
            building.facing = facing;
            building.doors = doors;
            building.ownership = Ownership::ForSale;
            building.line_id = line.id.clone();
            building.price_cents = price(rect, self.s.station_cents_per_cell, percent);
            building.elevation_cm = level;
            self.add(building);
            return true;
        }
        false
    }

    fn blocks(&mut self) {
        let mut rng = WorldRandom::fork(self.seed, "buildings");
        for j in 0..self.n {
            for i in 0..self.n {
                let mut verticals = vec![(self.xs[i], self.s.arterial_width)];
                verticals.extend(self.local_x[i][j].iter().map(|x| (*x, self.s.local_width)));
                verticals.push((self.xs[i + 1], self.s.arterial_width));
                let mut horizontals = vec![(self.zs[j], self.s.arterial_width)];
                horizontals.extend(self.local_z[i][j].iter().map(|z| (*z, self.s.local_width)));
                horizontals.push((self.zs[j + 1], self.s.arterial_width));
                for row in 0..horizontals.len() - 1 {
                    for column in 0..verticals.len() - 1 {
                        self.block(&mut rng, self.kinds[i][j], verticals[column], verticals[column + 1], horizontals[row], horizontals[row + 1]);
                    }
                }
            }
        }
    }

    fn block(&mut self, rng: &mut WorldRandom, kind: DistrictKind, west: (i32, i32), east: (i32, i32), south: (i32, i32), north: (i32, i32)) {
        let max_gap = self.s.profile(kind).max_gap;
        let x0 = west.0 + west.1 / 2 + self.s.setback;
        let x1 = east.0 - east.1 / 2 - self.s.setback;
        let z0 = south.0 + south.1 / 2 + self.s.setback;
        let z1 = north.0 - north.1 / 2 - self.s.setback;
        if x1 - x0 < 12 || z1 - z0 < 12 {
            return;
        }
        let split = 2.max(max_gap);
        let deep_north_south = (z1 - z0 - split) / 2;
        let deep_east_west = (x1 - x0 - split) / 2;
        self.edge(rng, kind, Facing::North, x0, x1, z1, deep_north_south);
        self.edge(rng, kind, Facing::South, x0, x1, z0, deep_north_south);
        self.edge(rng, kind, Facing::East, z0, z1, x1, deep_east_west);
        self.edge(rng, kind, Facing::West, z0, z1, x0, deep_east_west);
    }

    #[allow(clippy::too_many_arguments)]
    fn edge(&mut self, rng: &mut WorldRandom, kind: DistrictKind, facing: Facing, from: i32, to: i32, edge: i32, deep_limit: i32) {
        let profile = self.s.profile(kind).clone();
        let weights = [profile.restaurant_weight, profile.factory_weight, profile.house_weight, profile.apartment_weight, profile.office_weight];
        let categories = [BuildingCategory::Restaurant, BuildingCategory::Factory, BuildingCategory::House, BuildingCategory::Apartment, BuildingCategory::Office];
        let mut cursor = from + rng.range(0, 2);
        while cursor < to {
            let category = categories[rng.weighted(&weights).unwrap_or(0)];
            let size = self.size_of(category, kind);
            let back = self.s.setback_for(category) - self.s.setback;
            let deep_max = size.max_deep.min(deep_limit - back);
            if deep_max < size.min_deep {
                cursor += 3;
                continue;
            }
            let yard = if category == BuildingCategory::Restaurant { self.s.service_yard_width } else { 0 };
            let along = rng.range(size.min_along, size.max_along).min(to - cursor - yard);
            if along < size.min_along {
                break;
            }
            let deep = rng.range(size.min_deep, deep_max);
            let yard_low = yard > 0 && rng.chance(50);
            let start = cursor + if yard_low { yard } else { 0 };
            let line = if matches!(facing, Facing::North | Facing::East) { edge - back } else { edge + back };
            let rect = match facing {
                Facing::North => WorldRect::new(start, line - deep, along, deep),
                Facing::South => WorldRect::new(start, line, along, deep),
                Facing::East => WorldRect::new(line - deep, start, deep, along),
                Facing::West => WorldRect::new(line, start, deep, along),
            };
            let mut building = self.lot_building(rng, category, kind, rect, facing);
            if yard > 0 {
                geo::add_service_yard(&mut building, yard_low, yard, self.s.setback_for(category));
            }
            let b = geo::of_rect(&if building.has_lot() { self.lot_rect_of_building(&building) } else { rect });
            if self.placed.any(&grow(b, 2 * profile.min_gap)) {
                cursor += 2;
                continue;
            }
            let level = if self.reserved.any(&b) || self.water.any(&b) || self.road_index.any(&b) { None } else { self.level_for(rect, building.doors[0]) };
            if let Some(level) = level {
                building.elevation_cm = level;
                self.add(building);
            }
            cursor += along + yard + rng.range(profile.min_gap, profile.max_gap);
        }
    }

    fn size_of(&self, category: BuildingCategory, kind: DistrictKind) -> SizeRange {
        match category {
            BuildingCategory::Restaurant => self.s.restaurant,
            BuildingCategory::Factory => self.s.factory,
            BuildingCategory::House => if kind == DistrictKind::Wealthy { self.s.large_house } else { self.s.house },
            BuildingCategory::Apartment => self.s.apartment,
            _ => self.s.office,
        }
    }

    fn lot_building(&self, rng: &mut WorldRandom, category: BuildingCategory, kind: DistrictKind, rect: WorldRect, facing: Facing) -> WorldBuilding {
        let profile = self.s.profile(kind);
        let mut building = WorldBuilding::new(String::new(), category);
        building.district_id = profile.id.to_string();
        building.x = rect.x;
        building.z = rect.z;
        building.width = rect.width;
        building.depth = rect.depth;
        building.facing = facing;
        match category {
            BuildingCategory::Restaurant => {
                building.doors = geo::doors(rect, facing, 2);
                building.ownership = if rng.chance(self.s.restaurant_for_sale_percent) { Ownership::ForSale } else { Ownership::Competitor };
                building.price_cents = price(rect, self.s.restaurant_cents_per_cell, profile.price_percent);
            }
            BuildingCategory::Factory => {
                building.doors = geo::doors(rect, facing, 2);
                building.ownership = Ownership::ForSale;
                building.price_cents = price(rect, self.s.factory_cents_per_cell, profile.price_percent);
            }
            BuildingCategory::House => {
                building.doors = geo::doors(rect, facing, 1);
                building.model_key = format!("{}{}", if kind == DistrictKind::Wealthy { "house/large-" } else { "house/small-" }, variant(rng, 3));
                building.floors = rng.range(1, 2);
            }
            BuildingCategory::Apartment => {
                building.doors = geo::doors(rect, facing, 1);
                building.model_key = format!("apartment/block-{}", variant(rng, 2));
                building.floors = if kind == DistrictKind::Downtown { rng.range(4, 8) } else { rng.range(3, 5) };
            }
            _ => {
                building.doors = geo::doors(rect, facing, 1);
                building.model_key = format!("office/tower-{}", variant(rng, 2));
                building.floors = rng.range(6, 16);
            }
        }
        building
    }

    fn farms(&mut self) {
        let mut rng = WorldRandom::fork(self.seed, "farms");
        let mut farms: BoxIndex<i32> = BoxIndex::new();
        let city = Box2::new(-2 * self.c, -2 * self.c, 2 * self.c, 2 * self.c);
        let spurs: Vec<Span> = self.spans.iter().filter(|s| s.kind == RoadKind::Rural && s.fixed.abs() != self.ring).copied().collect();
        for spur in spurs {
            let industrial_side = spur.vertical && spur.to <= -self.c;
            let chance = self.s.farm_chance_percent + if industrial_side { self.s.farm_industrial_side_bonus_percent } else { 0 };
            let low = spur.from + 12;
            let high = spur.to - 12;
            let half = spur.width / 2;
            for side in [1, -1] {
                let mut cursor = low + rng.range(0, 20);
                loop {
                    let along = rng.range(self.s.farm.min_along, self.s.farm.max_along);
                    let deep = rng.range(self.s.farm.min_deep, self.s.farm.max_deep);
                    if cursor + along > high {
                        break;
                    }
                    let place = rng.chance(chance);
                    let near = spur.fixed + side * (half + self.s.farm_setback);
                    let rect = if spur.vertical {
                        WorldRect::new(if side > 0 { near } else { near - deep }, cursor, deep, along)
                    } else {
                        WorldRect::new(cursor, if side > 0 { near } else { near - deep }, along, deep)
                    };
                    let facing = if spur.vertical {
                        if side > 0 { Facing::West } else { Facing::East }
                    } else if side > 0 {
                        Facing::South
                    } else {
                        Facing::North
                    };
                    let b = geo::of_rect(&geo::lot_rect_of(rect, facing, self.s.setback_for(BuildingCategory::Farm)));
                    let model_variant = variant(&mut rng, 2);
                    if place && self.inside_ring(rect) && !b.overlaps(&city) && !self.road_index.any(&b) && !self.reserved.any(&b) && !self.water.any(&b) && !farms.any(&b) {
                        farms.add(b, 0);
                        let doors = geo::doors(rect, facing, 1);
                        let mut building = WorldBuilding::new(String::new(), BuildingCategory::Farm);
                        building.x = rect.x;
                        building.z = rect.z;
                        building.width = rect.width;
                        building.depth = rect.depth;
                        building.facing = facing;
                        building.elevation_cm = self.elevation(doors[0]);
                        building.doors = doors;
                        building.model_key = format!("farm/barn-{model_variant}");
                        building.ownership = Ownership::ForSale;
                        building.price_cents = price(rect, self.s.farm_cents_per_cell, 100);
                        self.add(building);
                    }
                    cursor += along + rng.range(self.s.min_farm_gap, self.s.max_farm_gap);
                }
            }
        }
    }

    fn inside_ring(&self, rect: WorldRect) -> bool {
        let limit = self.ring - self.s.rural_width / 2;
        rect.x >= -limit && rect.z >= -limit && rect.x + rect.width <= limit && rect.z + rect.depth <= limit
    }

    fn lot_rect_of_building(&self, building: &WorldBuilding) -> WorldRect { geo::lot_rect(building, self.s.setback_for(building.category)) }

    fn add(&mut self, building: WorldBuilding) {
        let b = geo::of_rect(&if building.has_lot() { self.lot_rect_of_building(&building) } else { building.footprint() });
        self.placed.add(b, 0);
        if building.category == BuildingCategory::Station {
            self.reserved.add(b, -1);
        }
        let (dx, dz) = geo::step(building.facing);
        for door in &building.doors {
            let reach = self.s.setback + 4;
            let ex = door.x + dx * reach;
            let ez = door.z + dz * reach;
            self.clear.add(Box2::new(2 * door.x.min(ex) - 4, 2 * door.z.min(ez) - 4, 2 * door.x.max(ex) + 6, 2 * door.z.max(ez) + 6), 0);
        }
        self.layout.buildings.push(building);
    }

    fn trees(&mut self) {
        let mut rng = WorldRandom::fork(self.seed, "trees");
        let woodland_salt = WorldRandom::fork(self.seed, "woodland").next_u64();
        let nodes = self.node_map();
        let mut trees = vec![];
        for index in 0..self.layout.roads.len() {
            let road = &self.layout.roads[index];
            if road.kind == RoadKind::Rural {
                continue;
            }
            let a = nodes[&road.from_id];
            let b = nodes[&road.to_id];
            let vertical = self.road_vertical[index];
            let from = (if vertical { a.1.min(b.1) } else { a.0.min(b.0) }) + self.s.street_tree_junction_clear;
            let to = (if vertical { a.1.max(b.1) } else { a.0.max(b.0) }) - self.s.street_tree_junction_clear;
            let fixed_at = if vertical { a.0 } else { a.1 };
            let half = road.width / 2;
            for side in [1, -1] {
                let mut along = from + rng.range(0, 4);
                while along <= to {
                    'body: {
                        let offset = if side > 0 { fixed_at + half - 1 } else { fixed_at - half };
                        let x = if vertical { offset } else { along };
                        let z = if vertical { along } else { offset };
                        let roll = rng.next(100);
                        let kind_roll = rng.next(100);
                        let scale = rng.range(85, 115);
                        let Some(district) = self.district_at(2 * x + 1, 2 * z + 1) else { break 'body };
                        if roll >= self.s.profile(district).street_tree_percent {
                            break 'body;
                        }
                        let point = Box2::new(2 * x + 1, 2 * z + 1, 2 * x + 1, 2 * z + 1);
                        if self.clear.any(&grow(point, 1)) || self.placed.any(&grow(point, 2)) || self.reserved.any(&grow(point, 2)) || self.near_water(x, z) {
                            break 'body;
                        }
                        if self.road_index.overlapping(&grow(point, 1)).iter().any(|r| self.road_vertical[*r] != vertical) {
                            break 'body;
                        }
                        let kind = match district {
                            DistrictKind::Wealthy => if kind_roll < 70 { TreeKind::Broadleaf } else { TreeKind::Poplar },
                            DistrictKind::Industrial => if kind_roll < 50 { TreeKind::Broadleaf } else { TreeKind::Poplar },
                            DistrictKind::Residential => if kind_roll < 85 { TreeKind::Broadleaf } else { TreeKind::Poplar },
                            DistrictKind::Downtown => TreeKind::Broadleaf,
                        };
                        trees.push(WorldTree { x, z, kind, scale });
                    }
                    along += self.s.street_tree_spacing + rng.range(-2, 2);
                }
            }
        }
        let yard = self.s.yard_tree_lattice;
        let mut gz = -self.c;
        while gz < self.c {
            let mut gx = -self.c;
            while gx < self.c {
                let x = gx + rng.next(yard);
                let z = gz + rng.next(yard);
                let roll = rng.next(100);
                let kind_roll = rng.next(100);
                let scale = rng.range(75, 120);
                gx += yard;
                let Some(district) = self.district_at(2 * x + 1, 2 * z + 1) else { continue };
                let bank = self.riverbank(x, z);
                let yard_percent = self.s.profile(district).yard_tree_percent;
                if roll >= if bank { self.s.riverbank_tree_percent.max(yard_percent) } else { yard_percent } {
                    continue;
                }
                if !self.free_ground(x, z, 2) {
                    continue;
                }
                let kind = if bank {
                    if kind_roll < 50 { TreeKind::Broadleaf } else if kind_roll < 85 { TreeKind::Poplar } else { TreeKind::Bush }
                } else if kind_roll < 55 {
                    TreeKind::Broadleaf
                } else if kind_roll < 75 {
                    TreeKind::Conifer
                } else {
                    TreeKind::Bush
                };
                trees.push(WorldTree { x, z, kind, scale });
            }
            gz += yard;
        }
        let country = self.s.country_tree_lattice;
        let edge = self.m - 3;
        let mut gz = -edge;
        while gz < edge - country {
            let mut gx = -edge;
            while gx < edge - country {
                let x = gx + rng.next(country);
                let z = gz + rng.next(country);
                let roll = rng.next(100);
                let kind_roll = rng.next(100);
                let scale = rng.range(80, 130);
                gx += country;
                if (2 * x + 1).abs().max((2 * z + 1).abs()) < 2 * self.c {
                    continue;
                }
                let woodland = noise(woodland_salt, x, z, self.s.woodland_lattice_metres, 50) + 50 >= self.s.woodland_threshold_percent;
                let bank = self.riverbank(x, z);
                let chance = (if woodland { self.s.woodland_tree_percent } else { self.s.lone_tree_percent }).max(if bank { self.s.riverbank_tree_percent } else { 0 });
                if roll >= chance || !self.free_ground(x, z, 3) {
                    continue;
                }
                let kind = if bank && !woodland {
                    if kind_roll < 50 { TreeKind::Broadleaf } else if kind_roll < 85 { TreeKind::Poplar } else { TreeKind::Bush }
                } else if woodland {
                    if kind_roll < 45 { TreeKind::Conifer } else if kind_roll < 90 { TreeKind::Broadleaf } else { TreeKind::Bush }
                } else if kind_roll < 80 {
                    TreeKind::Broadleaf
                } else {
                    TreeKind::Conifer
                };
                trees.push(WorldTree { x, z, kind, scale });
            }
            gz += country;
        }
        self.layout.trees = trees;
    }

    fn free_ground(&self, x: i32, z: i32, metres: i32) -> bool {
        let point = Box2::new(2 * x + 1, 2 * z + 1, 2 * x + 1, 2 * z + 1);
        let grown = grow(point, 2 * metres);
        !self.placed.any(&grow(point, 2)) && !self.road_index.any(&grown) && !self.reserved.any(&grown) && !self.clear.any(&point) && !self.near_water(x, z)
    }

    fn near_water(&self, x: i32, z: i32) -> bool { self.within_river(x, z, self.river_half() + 8) }

    fn riverbank(&self, x: i32, z: i32) -> bool { self.within_river(x, z, self.river_half() + self.s.riverbank_tree_band) }

    fn within_river(&self, x: i32, z: i32, metres: i32) -> bool {
        for pair in self.river.windows(2) {
            let (a, b) = (pair[0], pair[1]);
            if 2 * x + 1 < 2 * a.x.min(b.x) - 2 * metres || 2 * x + 1 > 2 * a.x.max(b.x) + 2 * metres {
                continue;
            }
            if geo::within_distance((2 * x + 1) as i64, (2 * z + 1) as i64, 2 * a.x as i64, 2 * a.z as i64, 2 * b.x as i64, 2 * b.z as i64, 2 * metres as i64) {
                return true;
            }
        }
        false
    }

    fn assign_ids(&mut self) {
        let mut counts: HashMap<BuildingCategory, i32> = HashMap::new();
        for building in &mut self.layout.buildings {
            let count = counts.entry(building.category).or_insert(0);
            *count += 1;
            building.id = format!("{}-{:04}", category_name(building.category), *count);
        }
    }

    fn pick_start(&mut self) {
        let residential = self.s.profile(DistrictKind::Residential);
        if residential.min_recipe_tier != 1 {
            return;
        }
        let largest = self.s.start_largest;
        let max_area = self.s.start_max_area;
        let id = residential.id;
        let start = self
            .layout
            .buildings
            .iter_mut()
            .filter(|b| b.category == BuildingCategory::Restaurant && b.district_id == id && b.width * b.depth <= max_area)
            .min_by_key(|b| (if largest { -(b.width * b.depth) } else { b.width * b.depth }, b.price_cents));
        if let Some(start) = start {
            start.ownership = Ownership::Player;
            self.layout.start_restaurant_id = start.id.clone();
        }
    }

    fn lots(&mut self) {
        let mut lots = vec![];
        for building in self.layout.buildings.iter_mut().filter(|b| b.has_lot()) {
            let rect = geo::lot_rect(building, self.s.setback_for(building.category));
            building.site_id = WorldLot::site_id_for(&building.id);
            lots.push(WorldLot {
                id: WorldLot::id_for(&building.id),
                building_id: building.id.clone(),
                site_id: building.site_id.clone(),
                x: rect.x,
                z: rect.z,
                width: rect.width,
                depth: rect.depth,
                access: geo::access_cell(rect, building.facing, building.doors[0]),
            });
        }
        self.layout.lots = lots;
    }

    fn district_at(&self, doubled_x: i32, doubled_z: i32) -> Option<DistrictKind> {
        for j in 0..self.n {
            for i in 0..self.n {
                if doubled_x >= 2 * self.xs[i] && doubled_x < 2 * self.xs[i + 1] && doubled_z >= 2 * self.zs[j] && doubled_z < 2 * self.zs[j + 1] {
                    return Some(self.kinds[i][j]);
                }
            }
        }
        None
    }

    fn zone(&self, x: i32, z: i32) -> String {
        match self.district_at(2 * x, 2 * z) {
            Some(kind) => format!("{kind:?}"),
            None => if z > 0 { "farmland-north".into() } else { "farmland-south".into() },
        }
    }

    fn turn(&mut self) {
        let layout = &mut self.layout;
        for district in &mut layout.districts {
            district.areas = district.areas.iter().map(|r| geo::turn_rect(*r)).collect();
        }
        for node in &mut layout.nodes {
            (node.x, node.z) = geo::turn_point(node.x, node.z);
        }
        for line in &mut layout.rails {
            line.points = line.points.iter().map(|p| turned_point(*p)).collect();
        }
        for building in &mut layout.buildings {
            let rect = geo::turn_rect(building.footprint());
            building.x = rect.x;
            building.z = rect.z;
            building.width = rect.width;
            building.depth = rect.depth;
            building.facing = geo::turn_facing(building.facing);
            building.doors = building.doors.iter().map(|d| geo::turn_cell(*d)).collect();
            building.service_yard = building.service_yard.map(geo::turn_rect);
            building.back_door = building.back_door.map(geo::turn_cell);
            building.service_dock = building.service_dock.map(geo::turn_rect);
        }
        layout.terrain = layout.terrain.turned();
        for river in &mut layout.rivers {
            river.points = river.points.iter().map(|p| turned_point(*p)).collect();
        }
        for bridge in &mut layout.bridges {
            bridge.from = turned_point(bridge.from);
            bridge.to = turned_point(bridge.to);
        }
        for crossing in &mut layout.crossings {
            (crossing.x, crossing.z) = geo::turn_point(crossing.x, crossing.z);
        }
        for tree in &mut layout.trees {
            let cell = geo::turn_cell(WorldCell::new(tree.x, tree.z));
            tree.x = cell.x;
            tree.z = cell.z;
        }
    }
}
