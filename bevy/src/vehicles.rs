// Vehicles on the generated roads (Unity TruckPresenter, RoadPose, CityCars and CityTrafficPresenter), presentation only.
// Trucks are drawn from the baselines: on a road leg in the right-hand kerb lane where the simulation has them, carried on
// between baselines and pulling up at the stop line when they will wait at the leg's end; loading or unloading backed onto
// their dock; parked in their restaurant's service yard. The cab paint (VH_Body) is the company colour: this client's company
// green, others a colour from the company ID. City cars are a deterministic function of the layout and the world clock: each
// lane of each direction near the avatar is a stream of cars at the headway the server's background flow implies for the
// hour, with stop-line/light discharge and queues behind trucks and people. No traffic presentation changes the simulation.
use std::collections::{HashMap, HashSet};
use std::f32::consts::PI;

use bevy::prelude::*;

use food_factory_bevy::goods::{self, Truck, TruckState};
use food_factory_bevy::world::layout::RoadKind;
use food_factory_bevy::world::random;
use food_factory_bevy::world::roads::{self, RoadNetwork, LANE_WIDTH};

use crate::models::{Library, Part};
use crate::session::Session;
use crate::space::{yaw_of, yaw_rotation, Space};

pub struct VehiclesPlugin;

impl Plugin for VehiclesPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<Vehicles>().init_resource::<TrafficClock>().init_resource::<TrafficPositions>()
            .add_systems(PreUpdate, traffic_clock)
            .add_systems(Update, (load_models, trucks, city_cars, drive, bodies).chain());
    }
}

const MODELS: &str = "models/Vehicles/Models/Vehicles.glb";
const CAR_NAMES: [&str; 5] = ["Vehicle_Hatchback", "Vehicle_Sedan", "Vehicle_Taxi", "Vehicle_Pickup", "Vehicle_Van"];
const TRUCK_NAME: &str = "Vehicle_BoxTruck";
const BODY_MATERIAL: &str = "VH_Body";
const MAX_EXTRAPOLATION: f32 = 1.25;
const CAR_RADIUS: f32 = 220.0;
const MAX_CARS: usize = 150;
/// A drawn car keeps its place against new ones this many metres closer, so the nearest-first cap does not swap cars in and out.
const KEEP_BIAS: f32 = 40.0;
/// A car the streams no longer list drives on at most this long before it goes, even in sight.
const COAST_SECONDS: f32 = 30.0;
/// While a world is first shown cars appear anywhere; after that only out of sight.
const FILL_SECONDS: f64 = 0.5;
const OWN_COLOUR: Color = Color::srgb(0.18, 0.62, 0.32);
const CAR_COLOURS: [Color; 8] = [
    Color::srgb(0.75, 0.12, 0.12), Color::srgb(0.12, 0.25, 0.6), Color::srgb(0.85, 0.85, 0.85), Color::srgb(0.12, 0.12, 0.13),
    Color::srgb(0.55, 0.57, 0.6), Color::srgb(0.2, 0.45, 0.3), Color::srgb(0.85, 0.6, 0.15), Color::srgb(0.4, 0.25, 0.15),
];

#[derive(Resource, Default)]
pub struct TrafficClock { pub seconds: f64, baseline: i64, received: f64, world: String }
#[derive(Resource, Default)]
pub struct TrafficPositions(pub Vec<Vec2>);

fn traffic_clock(time: Res<Time>, session: Res<Session>, mut clock: ResMut<TrafficClock>) {
    let Some(world) = session.joined() else { *clock = TrafficClock::default(); return };
    let seconds = world.view().map_or(0, |v| v.clock_seconds);
    if clock.baseline != seconds || clock.world != world.world_id {
        clock.baseline = seconds; clock.received = time.elapsed_secs_f64(); clock.world = world.world_id.clone();
    }
    clock.seconds = seconds as f64 + (time.elapsed_secs_f64() - clock.received).clamp(0.0, 1.25);
}

struct Model {
    parts: Vec<Part>,
    min: Vec3,
    max: Vec3,
}

#[derive(Resource, Default)]
struct Vehicles {
    models: HashMap<String, Model>,
    requested: bool,
    trucks: HashMap<String, Entity>,
    cars: HashMap<String, Entity>,
    /// The world the cars were first filled for and the time that fill ends.
    filled: (String, f64),
    /// When each truck's latest baseline arrived (its revision and the local time).
    seen: HashMap<String, (i64, f32)>,
    tints: HashMap<String, Handle<StandardMaterial>>,
    /// The painted-body detail texture under every tint.
    paint: Handle<Image>,
}

/// Where a vehicle is heading (scene) and its yaw; it eases there and snaps across long jumps.
/// A vehicle's half extents across and along it (for its solid body).
#[derive(Component)]
struct Body(Vec2);

/// A city car’s last velocity (scene metres per second), and how long it has driven on unlisted.
#[derive(Component, Default)]
struct CarMotion {
    velocity: Vec3,
    unlisted: f32,
}

#[derive(Component)]
struct Drive {
    position: Vec3,
    yaw: f32,
    snap: bool,
}

#[allow(clippy::too_many_arguments)]
fn load_models(
    mut vehicles: ResMut<Vehicles>, mut library: ResMut<Library>, assets: Res<AssetServer>, meshes: Res<Assets<Mesh>>, gltfs: Res<Assets<bevy::gltf::Gltf>>,
    nodes: Res<Assets<bevy::gltf::GltfNode>>, gltf_meshes: Res<Assets<bevy::gltf::GltfMesh>>, gltf_materials: Res<Assets<bevy::gltf::GltfMaterial>>, mut materials: ResMut<Assets<StandardMaterial>>,
) {
    if !vehicles.requested {
        library.request(&assets, MODELS);
        vehicles.paint = crate::textures::detail_texture(&assets, "plastic");
        vehicles.requested = true;
    }
    if !vehicles.models.is_empty() {
        return;
    }
    if !library.parts.contains_key(MODELS) {
        library.resolve(&gltfs, &nodes, &gltf_meshes, &gltf_materials, &mut materials, &assets);
        return;
    }
    for name in CAR_NAMES.iter().chain([&TRUCK_NAME]) {
        let Some(parts) = library.roots.get(&(MODELS.to_string(), name.to_string())) else { return };
        let mut min = Vec3::splat(f32::MAX);
        let mut max = Vec3::splat(f32::MIN);
        for part in parts {
            let Some(mesh) = meshes.get(&part.mesh) else { return };
            let Some(bevy::mesh::VertexAttributeValues::Float32x3(positions)) = mesh.attribute(Mesh::ATTRIBUTE_POSITION) else { continue };
            for p in positions {
                let p = part.transform.transform_point3(Vec3::from_array(*p));
                min = min.min(p);
                max = max.max(p);
            }
        }
        vehicles.models.insert(name.to_string(), Model { parts: parts.clone(), min, max });
    }
}

/// A drawn vehicle's engine loop (Unity VehicleEngineAudio.AddTo): level, base pitch and hearing distance in metres.
#[derive(Component, Clone, Copy)]
pub struct Engine { pub volume: f32, pub pitch: f32, pub hearing: f32 }

fn spawn_vehicle(commands: &mut Commands, model: &Model, tint: Option<Handle<StandardMaterial>>, position: Vec3, yaw: f32, name: String) -> Entity {
    let half = Vec2::new((model.max.x - model.min.x) * 0.5, (model.max.z - model.min.z) * 0.5);
    let height = model.max.y - model.min.y;
    let root = commands.spawn((Name::new(name), Transform::from_translation(position).with_rotation(yaw_rotation(yaw)), Visibility::default(), Drive { position, yaw, snap: true }, Body(half), BodyHeight(height))).id();
    // The model's length runs along its +Z, cab forward; a scene transform faces -Z, so it is turned half round.
    let centre = Vec3::new((model.min.x + model.max.x) * 0.5, model.min.y, (model.min.z + model.max.z) * 0.5);
    let body = commands.spawn((Transform::from_rotation(Quat::from_rotation_y(PI)) * Transform::from_translation(-centre), Visibility::default())).id();
    commands.entity(root).add_child(body);
    for part in &model.parts {
        let material = match (&tint, part.material_name == BODY_MATERIAL) {
            (Some(tint), true) => tint.clone(),
            _ => part.material.clone(),
        };
        let child = commands.spawn((Mesh3d(part.mesh.clone()), MeshMaterial3d(material), Transform::from_matrix(part.transform))).id();
        commands.entity(body).add_child(child);
    }
    root
}

fn tint(vehicles: &mut Vehicles, materials: &mut Assets<StandardMaterial>, key: String, colour: Color) -> Handle<StandardMaterial> {
    let paint = vehicles.paint.clone();
    vehicles.tints.entry(key).or_insert_with(|| materials.add(StandardMaterial { base_color: colour, base_color_texture: Some(paint), perceptual_roughness: 0.35, metallic: 0.2, ..default() })).clone()
}

/// Lane centre distance right of a segment's centreline (right-hand traffic), lane 0 at the kerb: the middle of the lane the
/// road tile paints (its texture at 64 px per metre: a local road's edge line 3.2 m out; an arterial's 0.12 m double centre
/// line, lane divider at 2.5 m and edge line at 4.64 m; a rural road's edge line 2.9 m out). Tiles are drawn at their own
/// width, so a road of another width keeps these lanes.
fn lane_offset(segment: &roads::Segment, lane: i32) -> f32 {
    let lanes: &[f32] = match segment.kind {
        RoadKind::Arterial => &[3.57, 1.31],
        RoadKind::Local => &[1.6],
        RoadKind::Rural => &[1.45],
    };
    let at = (lane.max(0) as usize).min(lanes.len() - 1);
    // More lanes than the paint shows (a wider custom road): further lanes step out by a lane width.
    lanes[at] + (lane.max(0) as usize - at) as f32 * LANE_WIDTH as f32
}

/// Map position and heading `offset` metres along a segment driven forward or back, in a lane.
fn along(network: &RoadNetwork, segment: usize, offset: f32, forward: bool, lane: i32) -> (Vec2, Vec2) {
    let s = &network.segments[segment];
    let heading = Vec2::new(s.dx as f32, s.dz as f32) * if forward { 1.0 } else { -1.0 };
    let (x, z) = network.position(segment, offset as f64);
    let right = Vec2::new(heading.y, -heading.x);
    (Vec2::new(x as f32, z as f32) + right * lane_offset(s, lane), heading)
}

fn cross_half_width(network: &RoadNetwork, segment: usize, node: usize) -> f32 {
    let along_x = network.segments[segment].dx != 0;
    network.nodes[node].segments.iter().filter(|i| (network.segments[**i].dx != 0) != along_x).map(|i| network.segments[*i].width as f32 / 2.0).fold(0.0, f32::max)
}

/// Metres back from a segment's end node to where a stopping vehicle's front halts (behind the painted stop line).
fn stop_back(network: &RoadNetwork, segment: usize, forward: bool) -> f32 {
    let s = &network.segments[segment];
    let near = cross_half_width(network, segment, network.end_node(segment, forward));
    if s.kind == RoadKind::Rural || near <= 0.0 {
        return near + 1.5;
    }
    let far = cross_half_width(network, segment, network.end_node(segment, !forward));
    let paved = s.length as f32 - near - far;
    if paved < 0.5 {
        return near + 0.3;
    }
    let tile = paved / (paved / 10.0).round().max(1.0);
    near + (4.5 + 0.3) * tile / 10.0
}

fn brake_to_stop(time: f32) -> f32 {
    let cruise = 0.8;
    let speed = 2.0 / (1.0 + cruise);
    if time <= cruise {
        return speed * time;
    }
    let braking = time - cruise;
    speed * cruise + speed * braking - speed * braking * braking / (2.0 * 0.2)
}

/// Unity RoadPose.TruckOffset: where a truck on a road leg is drawn, in metres from its segment's From node.
fn truck_offset(network: &RoadNetwork, segment: usize, truck: &Truck, since: f32, half_length: f32) -> f32 {
    let leg = |stop: f32| {
        if truck.leg_drive_seconds <= 0 || truck.remaining_seconds <= 0 {
            return stop;
        }
        let elapsed = (truck.leg_seconds - truck.remaining_seconds) as f32 + since.clamp(0.0, MAX_EXTRAPOLATION);
        let t = (elapsed / truck.leg_drive_seconds as f32).clamp(0.0, 1.0);
        truck.leg_from as f32 + (stop - truck.leg_from as f32) * t
    };
    let length = network.segments[segment].length;
    if truck.leg_from == truck.leg_to || (truck.leg_to != 0 && truck.leg_to != length) {
        return leg(truck.leg_to as f32);
    }
    let waits = truck.leg_seconds > truck.leg_drive_seconds || truck.remaining_seconds <= 0;
    if !waits {
        return leg(truck.leg_to as f32);
    }
    let forward = truck.leg_to >= truck.leg_from;
    let back = if network.stops(segment, forward) { stop_back(network, segment, forward) } else { cross_half_width(network, segment, network.end_node(segment, forward)) } + half_length;
    let stop = if forward { (truck.leg_from as f32).max(length as f32 - back) } else { (truck.leg_from as f32).min(back) };
    if truck.leg_drive_seconds <= 0 || truck.remaining_seconds <= 0 {
        return stop;
    }
    let elapsed = (truck.leg_seconds - truck.remaining_seconds) as f32 + since.clamp(0.0, MAX_EXTRAPOLATION);
    let t = brake_to_stop((elapsed / truck.leg_drive_seconds as f32).clamp(0.0, 1.0));
    truck.leg_from as f32 + (stop - truck.leg_from as f32) * t
}

#[allow(clippy::too_many_arguments)]
fn trucks(mut commands: Commands, time: Res<Time>, session: Res<Session>, space: Option<Res<Space>>, mut vehicles: ResMut<Vehicles>, mut materials: ResMut<Assets<StandardMaterial>>, mut drives: Query<&mut Drive>) {
    let (Some(world), Some(space)) = (session.joined(), space) else {
        for (_, e) in vehicles.trucks.drain() {
            commands.entity(e).despawn();
        }
        return;
    };
    if !vehicles.models.contains_key(TRUCK_NAME) {
        return;
    }
    let now = time.elapsed_secs();
    let own = world.company().map(|c| c.id.clone()).unwrap_or_default();
    let mut all: Vec<&Truck> = vec![];
    let mut ids = HashSet::new();
    for view in world.views.values() {
        for truck in view.trucks.iter().chain(&view.road_trucks) {
            if ids.insert(truck.id.clone()) {
                all.push(truck);
            }
        }
    }
    let revision = world.view().map_or(0, |v| v.revision);
    let (half_length, half_width) = vehicles.models.get(TRUCK_NAME).map_or((3.0, 1.2), |m| ((m.max.z - m.min.z) * 0.5, (m.max.x - m.min.x) * 0.5));
    let mut keep = HashSet::new();
    let mut parked_index: HashMap<String, usize> = HashMap::new();
    for truck in all {
        let seen = vehicles.seen.entry(truck.id.clone()).or_insert((revision, now));
        if seen.0 != revision {
            *seen = (revision, now);
        }
        let since = now - seen.1;
        let pose: Option<(Vec2, f32)> = if truck.on_road() {
            world.roads.segment_named(&truck.leg_segment_id).map(|segment| {
                let offset = truck_offset(&world.roads, segment, truck, since, half_length);
                let (point, heading) = along(&world.roads, segment, offset, truck.leg_to >= truck.leg_from, 0);
                (point, heading.x.atan2(heading.y).to_degrees())
            })
        } else if truck.docked || matches!(truck.state, TruckState::Loading | TruckState::Unloading) {
            // Backed onto the dock: rear at the dock's outer edge, nose out. A farm order's truck uses the order's dock.
            let order_dock = world.views.values().find_map(|v| v.farm_orders.iter().find(|o| !truck.order_id.is_empty() && o.id == truck.order_id)).map(|o| o.dock_id.clone());
            let docks = || world.views.values().flat_map(|v| v.equipment.iter().map(move |e| (v, e))).filter(|(_, e)| e.kind == goods::DOCK_KIND && e.placed() && e.site_id == truck.site_id);
            docks().find(|(_, e)| order_dock.as_ref() == Some(&e.id))
                .or_else(|| docks().find(|(v, e)| v.routes.iter().any(|r| r.id == truck.route_id && (r.pickup_dock_id == e.id || r.dropoff_dock_id == e.id))))
                .or_else(|| docks().next())
                .map(|(_, e)| e)
                .and_then(|dock| {
                    let lot = space.lots.get(&dock.site_id)?;
                    let (w, d) = dock.footprint();
                    let (fx, fz) = goods::grid::facing(dock.rotation);
                    let out = -Vec2::new(fx as f32, fz as f32);
                    let reach = if fx != 0 { w as f32 * 0.5 } else { d as f32 * 0.5 };
                    let centre = Vec2::new(lot.x as f32 + dock.cell_x as f32 + w as f32 * 0.5, lot.z as f32 + dock.cell_z as f32 + d as f32 * 0.5) + out * (reach + 0.3 + half_length);
                    Some((centre, out.x.atan2(out.y).to_degrees()))
                })
        } else if truck.state == TruckState::Parked {
            // In the service yard beside the restaurant, nose to the street, the next one beside it.
            world.layout.lots.iter().find(|l| l.site_id == truck.site_id).and_then(|lot| world.layout.building(&lot.building_id)).and_then(|b| {
                let yard = b.service_yard?;
                let index = parked_index.entry(truck.site_id.clone()).or_insert(0);
                let (sx, sz) = food_factory_bevy::world::geometry::step(b.facing);
                let across = Vec2::new(sz as f32, -sx as f32);
                let centre = Vec2::new(yard.x as f32 + yard.width as f32 * 0.5, yard.z as f32 + yard.depth as f32 * 0.5) + across * ((*index as f32) * (half_width * 2.0 + 0.8) - 1.0);
                *index += 1;
                Some((centre, (sx as f32).atan2(sz as f32).to_degrees()))
            })
        } else {
            None
        };
        let Some((point, yaw)) = pose else { continue };
        keep.insert(truck.id.clone());
        let position = space.to_scene(point.x, point.y, space.land(point.x, point.y));
        if let Some(entity) = vehicles.trucks.get(&truck.id) {
            if let Ok(mut drive) = drives.get_mut(*entity) {
                drive.position = position;
                drive.yaw = yaw;
            }
            continue;
        }
        let colour = if truck.company_id == own { OWN_COLOUR } else { CAR_COLOURS[(random::fnv1a(&truck.company_id) % CAR_COLOURS.len() as u64) as usize] };
        let material = tint(&mut vehicles, &mut materials, format!("company:{}", truck.company_id), colour);
        let Some(model) = vehicles.models.get(TRUCK_NAME) else { continue };
        let entity = spawn_vehicle(&mut commands, model, Some(material), position, yaw, format!("Truck {}", truck.name));
        commands.entity(entity).insert(Engine { volume: 0.4, pitch: 1.0, hearing: 30.0 });
        vehicles.trucks.insert(truck.id.clone(), entity);
    }
    let stale: Vec<String> = vehicles.trucks.keys().filter(|k| !keep.contains(*k)).cloned().collect();
    for id in stale {
        if let Some(e) = vehicles.trucks.remove(&id) {
            commands.entity(e).despawn();
        }
    }
}

#[allow(clippy::too_many_arguments)]
/// Cars appear and vanish only out of sight (Unity CityTrafficPresenter): a new car is drawn only where no camera sees it
/// (or while the world is first shown), and one the streams stop listing (its lane ended, or the cap dropped it) drives on
/// along its last heading until it is out of sight.
#[allow(clippy::too_many_arguments)]
fn city_cars(
    mut commands: Commands, time: Res<Time>, session: Res<Session>, space: Option<Res<Space>>, mut vehicles: ResMut<Vehicles>, mut materials: ResMut<Assets<StandardMaterial>>,
    mut cars_drawn: Query<(&mut Drive, &mut CarMotion, &Transform)>, player: Query<&Transform, With<crate::player::Player>>, clock: Res<TrafficClock>,
    people: Query<&GlobalTransform, With<crate::sites::DoorOpener>>, mut positions: ResMut<TrafficPositions>, cameras: Query<(&Camera, &GlobalTransform), With<Camera3d>>,
) {
    positions.0.clear();
    let (Some(world), Some(space), Ok(player)) = (session.joined(), space, player.single()) else {
        for (_, e) in vehicles.cars.drain() {
            commands.entity(e).despawn();
        }
        return;
    };
    if vehicles.models.len() < CAR_NAMES.len() {
        return;
    }
    let t = clock.seconds;
    let now = time.elapsed_secs_f64();
    let dt = time.delta_secs().max(1e-4);
    if vehicles.filled.0 != world.world_id {
        vehicles.filled = (world.world_id.clone(), now + FILL_SECONDS);
    }
    let filling = now < vehicles.filled.1;
    let seen = |p: Vec3| cameras.iter().any(|(camera, at)| camera.is_active && camera.world_to_ndc(at, p + Vec3::Y).is_some_and(|n| n.x.abs() < 1.15 && n.y.abs() < 1.15 && n.z > 0.0));
    let (px, pz, _) = space.to_map(player.translation);
    let here = Vec2::new(px, pz);
    let network = &world.roads;
    let mut wanted: Vec<(f32, String, Vec2, f32, usize)> = vec![];
    for segment in &network.segments {
        let a = &network.nodes[segment.from];
        let b = &network.nodes[segment.to];
        let (ax, az, bx, bz) = (a.x.min(b.x) as f32, a.z.min(b.z) as f32, a.x.max(b.x) as f32, a.z.max(b.z) as f32);
        let dx = (ax - here.x).max(here.x - bx).max(0.0);
        let dz = (az - here.y).max(here.y - bz).max(0.0);
        if dx.max(dz) > CAR_RADIUS {
            continue;
        }
        for forward in [true, false] {
            for lane in 0..segment.lanes_per_direction {
                let length = segment.length as f32;
                let mut cars = food_factory_bevy::traffic::lane(network, segment.index, forward, lane, t, stop_back(network, segment.index, forward));
                if lane == 0 {
                    for view in world.views.values() {
                        for truck in view.trucks.iter().chain(&view.road_trucks).filter(|truck| truck.on_road() && truck.leg_segment_id == segment.id && (truck.leg_to >= truck.leg_from) == forward) {
                            let offset = truck_offset(network, segment.index, truck, (t - view.clock_seconds as f64).clamp(0.0, 1.25) as f32, 3.5);
                            food_factory_bevy::traffic::hold_behind(&mut cars, if forward { offset } else { length - offset }, 10.5, 7.0);
                        }
                    }
                }
                let (start, heading) = along(network, segment.index, if forward { 0.0 } else { length }, forward, lane);
                let right = Vec2::new(heading.y, -heading.x);
                for person in &people {
                    let (x, z, _) = space.to_map(person.translation());
                    let relative = Vec2::new(x, z) - start;
                    let at = relative.dot(heading);
                    if at >= 0.0 && at <= length && relative.dot(right).abs() <= LANE_WIDTH as f32 / 2.0 + 0.1 {
                        food_factory_bevy::traffic::hold_behind(&mut cars, at, 4.5, 2.0);
                    }
                }
                for car in cars {
                    let offset = if forward { car.along } else { length - car.along };
                    let (point, heading) = along(network, segment.index, offset, forward, lane);
                    let distance = point.distance(here);
                    if distance < CAR_RADIUS {
                        let id = format!("{}:{forward}:{lane}:{}", segment.id, car.key);
                        let model = car.key as usize % CAR_NAMES.len();
                        wanted.push((distance, id, point, heading.x.atan2(heading.y).to_degrees(), model));
                    }
                }
            }
        }
    }
    for entry in &mut wanted {
        if vehicles.cars.contains_key(&entry.1) { entry.0 -= KEEP_BIAS; }
    }
    wanted.sort_by(|a, b| a.0.total_cmp(&b.0));
    wanted.truncate(MAX_CARS);
    let mut keep = HashSet::new();
    for (_, id, point, yaw, model_index) in wanted {
        positions.0.push(point);
        keep.insert(id.clone());
        // Cars avoid drawn lots (a road never runs through one, but its kerb lane may graze an apron).
        let position = space.to_scene(point.x, point.y, space.land(point.x, point.y));
        if let Some(entity) = vehicles.cars.get(&id) {
            if let Ok((mut drive, mut motion, _)) = cars_drawn.get_mut(*entity) {
                motion.velocity = if drive.position.distance(position) < 25.0 { (position - drive.position) / dt } else { Vec3::ZERO };
                motion.unlisted = 0.0;
                drive.position = position;
                drive.yaw = yaw;
            }
            continue;
        }
        if !filling && seen(position) {
            continue;
        }
        let name = CAR_NAMES[model_index];
        let colour = if name == "Vehicle_Taxi" { None } else { Some(CAR_COLOURS[(random::fnv1a(&id) % CAR_COLOURS.len() as u64) as usize]) };
        let material = colour.map(|c| tint(&mut vehicles, &mut materials, format!("car:{c:?}"), c));
        let Some(model) = vehicles.models.get(name) else { continue };
        let entity = spawn_vehicle(&mut commands, model, material, position, yaw, format!("Car {id}"));
        commands.entity(entity).insert(CarMotion::default()).insert(Engine { volume: 0.18, pitch: 1.2 + (food_factory_bevy::world::random::fnv1a(&id) % 1000) as f32 / 1000.0 * 0.3, hearing: 22.0 });
        vehicles.cars.insert(id, entity);
    }
    // A car whose lane no longer lists it drives on along its heading until out of sight, but it still stops for people and
    // for any car close ahead in its path, so it neither runs anyone down nor drives through the crossing stream.
    let people_at: Vec<Vec2> = people.iter().map(|p| { let (x, z, _) = space.to_map(p.translation()); Vec2::new(x, z) }).collect();
    let listed: Vec<Vec2> = positions.0.clone();
    let unlisted: Vec<(String, Entity)> = vehicles.cars.iter().filter(|(k, _)| !keep.contains(*k)).map(|(k, e)| (k.clone(), *e)).collect();
    let mut coasting: Vec<Vec2> = vec![];
    for (id, e) in unlisted {
        let gone = match cars_drawn.get_mut(e) {
            Ok((mut drive, mut motion, transform)) => {
                motion.unlisted += dt;
                let (cx, cz, _) = space.to_map(drive.position);
                let at = Vec2::new(cx, cz);
                let step = space.to_map(drive.position + motion.velocity);
                let heading = (Vec2::new(step.0, step.1) - at).normalize_or_zero();
                let blocked = |other: &Vec2, reach: f32, side: f32| {
                    let relative = *other - at;
                    let ahead = relative.dot(heading);
                    ahead > 0.5 && ahead < reach && relative.perp_dot(heading).abs() < side
                };
                let stop = heading != Vec2::ZERO
                    && (people_at.iter().any(|p| blocked(p, food_factory_bevy::traffic::CAR_HALF_LENGTH + 3.5, 1.6))
                        || listed.iter().chain(&coasting).any(|c| blocked(c, food_factory_bevy::traffic::GAP, 1.8)));
                if !stop {
                    drive.position += motion.velocity * dt;
                }
                let (x, z, _) = space.to_map(drive.position);
                coasting.push(Vec2::new(x, z));

                drive.position.y = space.to_scene(x, z, space.land(x, z)).y;
                positions.0.push(Vec2::new(x, z));
                filling || motion.unlisted > COAST_SECONDS || !seen(transform.translation) || Vec2::new(x, z).distance(here) > CAR_RADIUS
            }
            Err(_) => true,
        };
        if gone {
            vehicles.cars.remove(&id);
            commands.entity(e).despawn();
        }
    }
}

#[derive(Component)]
struct BodyHeight(f32);

/// Each drawn vehicle is a solid box the avatar bumps into (axis-aligned around its turned footprint).
fn bodies(vehicles: Query<(&Transform, &Body, &BodyHeight)>, mut solids: ResMut<crate::sites::Solids>) {
    solids.moving_boxes.clear();
    for (transform, body, height) in &vehicles {
        let across = transform.rotation * Vec3::X * body.0.x;
        let along = transform.rotation * Vec3::Z * body.0.y;
        let extent = Vec3::new(across.x.abs() + along.x.abs(), 0.0, across.z.abs() + along.z.abs());
        let p = transform.translation;
        solids.moving_boxes.push((Vec3::new(p.x - extent.x, p.y, p.z - extent.z), Vec3::new(p.x + extent.x, p.y + height.0.max(1.4), p.z + extent.z)));
    }
}

/// Vehicles ease toward their pose; a long jump (a new leg, a teleport) snaps.
fn drive(time: Res<Time>, mut query: Query<(&mut Drive, &mut Transform)>) {
    let delta = time.delta_secs();
    for (mut drive, mut transform) in &mut query {
        let gap = drive.position - transform.translation;
        if drive.snap || gap.length() > 25.0 {
            transform.translation = drive.position;
            transform.rotation = yaw_rotation(drive.yaw);
            drive.snap = false;
            continue;
        }
        let k = 1.0 - (-8.0 * delta).exp();
        transform.translation += gap * k;
        let moving = Vec3::new(gap.x, 0.0, gap.z);
        let yaw = if moving.length() > 0.05 { yaw_of(moving) } else { drive.yaw };
        transform.rotation = transform.rotation.slerp(yaw_rotation(yaw), (1.0 - (-6.0 * delta).exp()).min(1.0));
    }
}
