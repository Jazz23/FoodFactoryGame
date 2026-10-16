// People drawn from replicated records (Unity PlayerAvatar remote copies, EmployeeWorker visuals, CustomerPresenter): other
// players with the player model and a shirt hue from their name, employees with the employee model (carrying a box while they
// hold goods), and customers: at a drawn site queued back from their register's service spot or seated at their table, and in
// front of nearby competitors queued outside the door. Each figure glides toward its replicated pose and plays idle or walk
// (carry idle or carry walk) from how fast it actually moves. Employees can be aimed at. Presentation only.
use std::collections::{HashMap, HashSet};
use std::f32::consts::PI;

use bevy::gltf::GltfMaterialName;
use bevy::prelude::*;
use bevy::world_serialization::{WorldAssetRoot, WorldInstanceReady};

use food_factory_bevy::goods::{self, CustomerState};
use food_factory_bevy::world::random;

use crate::animation::{CharacterModel, GroundHeight};
use crate::session::Session;
use crate::sites::{DoorOpener, Pick, PickBox, Solids};
use crate::space::{grid_rotation, yaw_of, yaw_rotation, Space};

pub struct CharactersPlugin;

impl Plugin for CharactersPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<Figures>().add_systems(Startup, load_clips).add_systems(Update, (sync_avatars, sync_employees, sync_customers, sync_pedestrians, glide, animate, employee_picks).chain());
    }
}

const EMPLOYEE_MODEL: &str = "models/Models/Employee/Employee.glb";
const CLIPS: [&str; 4] = [
    "Employee_Armature|Employee_Armature|Employee_Idle",
    "Employee_Armature|Employee_Armature|Employee_Walk",
    "Employee_Armature|Employee_Armature|Employee_CarryIdle",
    "Employee_Armature|Employee_Armature|Employee_CarryWalk",
];
const BOX_MATERIALS: [&str; 2] = ["Employee_Cardboard", "Employee_Tape"];
const OUTFIT_MATERIAL: &str = "Employee_Teal";
const WALK_SPEED: f32 = 1.4;
const QUEUE_SPACING: f32 = 0.9;
const MAX_CUSTOMERS: usize = 60;
/// Competitor queues further than this from the avatar are not drawn.
const CROWD_DISTANCE: f32 = 120.0;
const CUSTOMER_TINTS: [Color; 8] = [
    Color::srgb(0.70, 0.25, 0.22), Color::srgb(0.25, 0.42, 0.70), Color::srgb(0.30, 0.55, 0.30), Color::srgb(0.62, 0.48, 0.20),
    Color::srgb(0.48, 0.30, 0.60), Color::srgb(0.20, 0.55, 0.58), Color::srgb(0.75, 0.55, 0.62), Color::srgb(0.35, 0.35, 0.38),
];

#[derive(Resource, Default)]
struct Figures {
    clips: Option<(Handle<AnimationGraph>, [AnimationNodeIndex; 4])>,
    gltf: Handle<Gltf>,
    avatars: HashMap<String, Entity>,
    employees: HashMap<String, Entity>,
    customers: HashMap<String, Entity>,
    pedestrians: HashMap<String, Entity>,
}

/// Where a figure is heading (scene) and its facing there; `snap` places it at once (first sight, a big jump).
#[derive(Component)]
struct Target {
    position: Vec3,
    yaw: f32,
    snap: bool,
    /// Turn to the walking direction while moving (people on foot), else take the record's yaw.
    face_motion: bool,
}

/// An employee-model figure: its animation player once spawned, whether it carries a box, and its measured speed.
#[derive(Component, Default)]
struct Walker {
    player: Option<Entity>,
    carrying: bool,
    speed: f32,
    last: Option<Vec3>,
    boxes: Vec<Entity>,
    seated: bool,
}

fn load_clips(assets: Res<AssetServer>, mut figures: ResMut<Figures>) { figures.gltf = assets.load(EMPLOYEE_MODEL); }

fn shirt_of(name: &str) -> Color {
    let hue = (random::fnv1a(name) % 360) as f32;
    Color::hsl(hue, 0.55, 0.5)
}

fn spawn_walker(commands: &mut Commands, assets: &AssetServer, figures: &mut Figures, gltfs: &Assets<Gltf>, graphs: &mut Assets<AnimationGraph>, tint: Option<Color>, name: String, customer: bool, materials: &mut Assets<StandardMaterial>) -> Entity {
    if figures.clips.is_none() {
        if let Some(gltf) = gltfs.get(&figures.gltf) {
            let handles: Vec<Handle<AnimationClip>> = CLIPS.iter().filter_map(|c| gltf.named_animations.get(*c).cloned()).collect();
            if handles.len() == 4 {
                let (graph, nodes) = AnimationGraph::from_clips(handles);
                figures.clips = Some((graphs.add(graph), [nodes[0], nodes[1], nodes[2], nodes[3]]));
            }
        }
    }
    let clips = figures.clips.clone();
    let outfit = tint.map(|c| materials.add(StandardMaterial { base_color: c, perceptual_roughness: 0.85, ..default() }));
    let root = commands.spawn((Name::new(name), Transform::default(), Visibility::Hidden, Walker::default(), DoorOpener { customer })).id();
    let model = commands
        .spawn((WorldAssetRoot(assets.load(GltfAssetLabel::Scene(0).from_asset(EMPLOYEE_MODEL))), Transform::from_rotation(Quat::from_rotation_y(PI))))
        .observe(move |ready: On<WorldInstanceReady>, mut commands: Commands, children: Query<&Children>, mut players: Query<&mut AnimationPlayer>, names: Query<&GltfMaterialName>, parents: Query<&ChildOf>, mut walkers: Query<&mut Walker>| {
            let mut boxes = vec![];
            let mut animation = None;
            for descendant in children.iter_descendants(ready.event().entity) {
                if let Ok(name) = names.get(descendant) {
                    if BOX_MATERIALS.contains(&name.0.as_str()) {
                        boxes.push(descendant);
                    }
                    if name.0 == OUTFIT_MATERIAL {
                        if let Some(outfit) = &outfit {
                            commands.entity(descendant).insert(MeshMaterial3d(outfit.clone()));
                        }
                    }
                }
                if let (Ok(mut player), Some((graph, nodes))) = (players.get_mut(descendant), &clips) {
                    for (index, node) in nodes.iter().enumerate() {
                        player.play(*node).repeat().set_weight(if index == 0 { 1.0 } else { 0.0 });
                    }
                    commands.entity(descendant).insert(AnimationGraphHandle(graph.clone()));
                    animation = Some(descendant);
                }
            }
            for entity in &boxes {
                commands.entity(*entity).insert(Visibility::Hidden);
            }
            let Ok(model) = parents.get(ready.event().entity) else { return };
            if let Ok(mut walker) = walkers.get_mut(model.parent()) {
                walker.player = animation;
                walker.boxes = boxes;
            }
        })
        .id();
    commands.entity(root).add_child(model);
    root
}

fn retire(commands: &mut Commands, map: &mut HashMap<String, Entity>, keep: &HashSet<String>) {
    let stale: Vec<String> = map.keys().filter(|k| !keep.contains(*k)).cloned().collect();
    for id in stale {
        if let Some(entity) = map.remove(&id) {
            commands.entity(entity).despawn();
        }
    }
}

fn sync_avatars(mut commands: Commands, session: Res<Session>, space: Option<Res<Space>>, assets: Res<AssetServer>, mut figures: ResMut<Figures>, mut targets: Query<&mut Target>) {
    let (Some(world), Some(space)) = (session.joined(), space) else {
        let all = HashSet::new();
        retire(&mut commands, &mut figures.avatars, &all);
        return;
    };
    let mut keep = HashSet::new();
    for avatar in world.avatars.iter().filter(|a| a.player != world.player) {
        keep.insert(avatar.player.clone());
        let ground = space.land(avatar.pose.x, avatar.pose.z);
        let height = if avatar.pose.y.is_finite() { avatar.pose.y } else { ground };
        let position = space.to_scene(avatar.pose.x, avatar.pose.z, height);
        if let Some(entity) = figures.avatars.get(&avatar.player) {
            if let Ok(mut target) = targets.get_mut(*entity) {
                target.position = position;
                target.yaw = avatar.pose.yaw;
            }
            continue;
        }
        let entity = commands
            .spawn((Name::new(format!("Avatar {}", avatar.name)), Transform::from_translation(position), Visibility::default(), GroundHeight(position.y), DoorOpener { customer: false },
                Target { position, yaw: avatar.pose.yaw, snap: true, face_motion: false }))
            .with_child((CharacterModel::new(assets.load("models/player.glb")).with_shirt(shirt_of(&avatar.name)), Transform::from_rotation(Quat::from_rotation_y(PI))))
            .id();
        figures.avatars.insert(avatar.player.clone(), entity);
    }
    retire(&mut commands, &mut figures.avatars, &keep);
}

#[allow(clippy::too_many_arguments)]
fn sync_employees(
    mut commands: Commands, session: Res<Session>, space: Option<Res<Space>>, assets: Res<AssetServer>, mut figures: ResMut<Figures>, gltfs: Res<Assets<Gltf>>, mut graphs: ResMut<Assets<AnimationGraph>>,
    mut materials: ResMut<Assets<StandardMaterial>>, mut query: Query<(&mut Target, &mut Walker)>,
) {
    let (Some(world), Some(space)) = (session.joined(), space) else {
        retire(&mut commands, &mut figures.employees, &HashSet::new());
        return;
    };
    let mut keep = HashSet::new();
    for employee in &world.employees {
        if !space.lots.contains_key(&employee.site) {
            continue;
        }
        keep.insert(employee.id.clone());
        let position = space.site_point(&employee.site, employee.pose.x, employee.pose.z, 0) + Vec3::Y * employee.pose.y;
        if let Some(entity) = figures.employees.get(&employee.id) {
            if let Ok((mut target, mut walker)) = query.get_mut(*entity) {
                if target.position.distance(position) > 4.0 {
                    target.snap = true;
                }
                target.position = position;
                target.yaw = employee.pose.yaw;
                walker.carrying = employee.carrying;
            }
            continue;
        }
        if gltfs.get(&figures.gltf).is_none() {
            continue;
        }
        let entity = spawn_walker(&mut commands, &assets, &mut figures, &gltfs, &mut graphs, None, format!("Employee {}", employee.name), false, &mut materials);
        commands.entity(entity).insert(Target { position, yaw: employee.pose.yaw, snap: true, face_motion: false });
        figures.employees.insert(employee.id.clone(), entity);
    }
    retire(&mut commands, &mut figures.employees, &keep);
}

/// A drawn site customer's place: queued back from its register, ordering at it, or seated at its table.
fn site_customer_place(view: &goods::Snapshot, customer: &goods::Customer, queue_index: usize) -> Option<(f32, f32, f32, bool)> {
    match customer.state {
        CustomerState::Queued | CustomerState::Ordering => {
            let counter = view.equipment.iter().find(|e| e.id == customer.counter_id && e.placed())?;
            let (w, d) = counter.footprint();
            let (fx, fz) = goods::grid::facing(counter.rotation);
            let centre = Vec2::new(counter.cell_x as f32 + w as f32 * 0.5, counter.cell_z as f32 + d as f32 * 0.5);
            let out = Vec2::new(fx as f32, fz as f32);
            let reach = if fx != 0 { w as f32 * 0.5 } else { d as f32 * 0.5 };
            let spot = centre + out * (reach + 0.6 + queue_index as f32 * QUEUE_SPACING);
            let yaw = (-out.x).atan2(-out.y).to_degrees();
            Some((spot.x, spot.y, yaw, false))
        }
        CustomerState::Eating => {
            let table = view.equipment.iter().find(|e| e.id == customer.table_id && e.placed())?;
            let (w, d) = table.footprint();
            let seats = table.seats.max(1);
            let seat = customer.seat.max(0) % seats;
            // Seats alternate along the two long sides, facing the table.
            let along_x = w >= d;
            let per_side = ((seats + 1) / 2).max(1);
            let index = seat / 2;
            let side = if seat % 2 == 0 { -1.0 } else { 1.0 };
            let length = if along_x { w } else { d } as f32;
            let t = (index as f32 + 0.5) / per_side as f32 * length;
            let (x, z, yaw) = if along_x {
                (table.cell_x as f32 + t, table.cell_z as f32 + d as f32 * 0.5 + side * (d as f32 * 0.5 + 0.35), if side < 0.0 { 0.0 } else { 180.0 })
            } else {
                (table.cell_x as f32 + w as f32 * 0.5 + side * (w as f32 * 0.5 + 0.35), table.cell_z as f32 + t, if side < 0.0 { 90.0 } else { 270.0 })
            };
            Some((x, z, yaw, true))
        }
        CustomerState::Travelling => None,
    }
}

#[allow(clippy::too_many_arguments)]
fn sync_customers(
    mut commands: Commands, session: Res<Session>, space: Option<Res<Space>>, assets: Res<AssetServer>, mut figures: ResMut<Figures>, gltfs: Res<Assets<Gltf>>, mut graphs: ResMut<Assets<AnimationGraph>>,
    mut materials: ResMut<Assets<StandardMaterial>>, mut query: Query<(&mut Target, &mut Walker)>, player: Query<&Transform, With<crate::player::Player>>, drawn: Res<crate::map::DrawnSites>,
) {
    let (Some(world), Some(space)) = (session.joined(), space) else {
        retire(&mut commands, &mut figures.customers, &HashSet::new());
        return;
    };
    if gltfs.get(&figures.gltf).is_none() {
        return;
    }
    let here = player.single().map(|t| t.translation).unwrap_or_default();
    // (id, appearance, site-or-lot frame, x, z (site metres), yaw, seated)
    let mut wanted: Vec<(String, i32, String, f32, f32, f32, bool)> = vec![];
    for site in &drawn.sites {
        let Some(view) = world.view_of(site) else { continue };
        let mut queues: HashMap<String, usize> = HashMap::new();
        let mut customers: Vec<&goods::Customer> = view.customers.iter().filter(|c| c.restaurant_id == *site || view.equipment.iter().any(|e| e.id == c.counter_id && &e.site_id == site)).collect();
        customers.sort_by_key(|c| (c.state != CustomerState::Ordering, c.ticket));
        for customer in customers {
            let index = if matches!(customer.state, CustomerState::Queued | CustomerState::Ordering) {
                let entry = queues.entry(customer.counter_id.clone()).or_insert(0);
                *entry += 1;
                *entry - 1
            } else {
                0
            };
            if let Some((x, z, yaw, seated)) = site_customer_place(view, customer, index) {
                wanted.push((customer.id.clone(), customer.appearance, site.clone(), x, z, yaw, seated));
            }
        }
    }
    // Competitors near the avatar: their queue outside the door.
    for restaurant in &world.crowd.restaurants {
        let Some(offer) = world.offers_by_lot.get(&restaurant.lot_id) else { continue };
        let Some(door) = offer.doors.first() else { continue };
        let door_scene = space.site_point(&offer.site_id, door.x as f32 + 0.5, door.z as f32 + 0.5, 0);
        if door_scene.distance(here) > CROWD_DISTANCE {
            continue;
        }
        let out = if door.z == offer.building_z + offer.building_depth - 1 { Vec2::Y } else if door.z == offer.building_z { -Vec2::Y } else if door.x == offer.building_x { -Vec2::X } else { Vec2::X };
        let along = Vec2::new(out.y, -out.x);
        let mut index = 0;
        for customer in restaurant.customers.iter().filter(|c| c.state == CustomerState::Queued) {
            let spot = Vec2::new(door.x as f32 + 0.5, door.z as f32 + 0.5) + out * 1.5 + along * (index as f32 * QUEUE_SPACING);
            let yaw = (-along.x).atan2(-along.y).to_degrees();
            wanted.push((customer.id.clone(), customer.appearance, offer.site_id.clone(), spot.x, spot.y, yaw, false));
            index += 1;
        }
    }
    wanted.truncate(MAX_CUSTOMERS);
    let mut keep = HashSet::new();
    for (id, appearance, frame, x, z, yaw, seated) in wanted {
        keep.insert(id.clone());
        let position = space.site_point(&frame, x, z, 0) - Vec3::Y * if seated { 0.45 } else { 0.0 };
        if let Some(entity) = figures.customers.get(&id) {
            if let Ok((mut target, mut walker)) = query.get_mut(*entity) {
                target.position = position;
                target.yaw = yaw;
                walker.seated = seated;
            }
            continue;
        }
        let tint = CUSTOMER_TINTS[(random::fnv1a(&id).wrapping_add(appearance as u64) % CUSTOMER_TINTS.len() as u64) as usize];
        let entity = spawn_walker(&mut commands, &assets, &mut figures, &gltfs, &mut graphs, Some(tint), format!("Customer {id}"), true, &mut materials);
        commands.entity(entity).insert(Target { position, yaw, snap: true, face_motion: true });
        figures.customers.insert(id, entity);
    }
    retire(&mut commands, &mut figures.customers, &keep);
}

fn glide(time: Res<Time>, mut figures: Query<(&mut Target, &mut Transform, Option<&mut Visibility>, Option<&mut GroundHeight>)>) {
    let delta = time.delta_secs();
    for (mut target, mut transform, visibility, ground) in &mut figures {
        if let Some(mut visibility) = visibility {
            if *visibility == Visibility::Hidden {
                *visibility = Visibility::Inherited;
            }
        }
        let gap = target.position - transform.translation;
        let moving = Vec3::new(gap.x, 0.0, gap.z);
        if target.snap || gap.length() > 12.0 {
            transform.translation = target.position;
            target.snap = false;
        } else {
            let step = (WALK_SPEED * 2.5 * delta).max(gap.length() * (1.0 - (-6.0 * delta).exp()));
            transform.translation = if gap.length() <= step { target.position } else { transform.translation + gap.normalize() * step };
        }
        let facing = if target.face_motion && moving.length() > 0.15 { yaw_of(moving) } else { target.yaw };
        let goal = yaw_rotation(facing);
        transform.rotation = transform.rotation.slerp(goal, (1.0 - (-10.0 * delta).exp()).min(1.0));
        if let Some(mut ground) = ground {
            ground.0 = target.position.y;
        }
    }
}

fn animate(time: Res<Time>, mut walkers: Query<(&mut Walker, &Transform)>, mut players: Query<&mut AnimationPlayer>, mut visibility: Query<&mut Visibility>, figures: Res<Figures>) {
    let delta = time.delta_secs().max(1e-4);
    let Some((_, nodes)) = &figures.clips else { return };
    for (mut walker, transform) in &mut walkers {
        let position = transform.translation;
        let measured = walker.last.map_or(0.0, |last| Vec2::new(position.x - last.x, position.z - last.z).length() / delta);
        walker.last = Some(position);
        walker.speed += (measured - walker.speed) * (1.0 - (-8.0 * delta).exp());
        let walk = (walker.speed / WALK_SPEED).clamp(0.0, 1.0);
        let carry = walker.carrying;
        for entity in walker.boxes.clone() {
            if let Ok(mut v) = visibility.get_mut(entity) {
                let target = if carry { Visibility::Inherited } else { Visibility::Hidden };
                if *v != target {
                    *v = target;
                }
            }
        }
        let Some(player) = walker.player else { continue };
        let Ok(mut player) = players.get_mut(player) else { continue };
        let weights = if carry { [0.0, 0.0, 1.0 - walk, walk] } else { [1.0 - walk, walk, 0.0, 0.0] };
        for (node, weight) in nodes.iter().zip(weights) {
            if let Some(active) = player.animation_mut(*node) {
                active.set_weight(weight);
            }
        }
    }
}

/// Employees as aim targets for this frame.
fn employee_picks(figures: Res<Figures>, session: Res<Session>, transforms: Query<&Transform>, mut solids: ResMut<Solids>, space: Option<Res<Space>>) {
    let (Some(world), Some(space)) = (session.joined(), space) else {
        solids.moving.clear();
        return;
    };
    let mut picks = vec![];
    for employee in &world.employees {
        let Some(entity) = figures.employees.get(&employee.id) else { continue };
        let Ok(transform) = transforms.get(*entity) else { continue };
        let p = transform.translation;
        let level = space.level_at(&employee.site, p.y + 0.3);
        picks.push(PickBox { pick: Pick::Employee(employee.id.clone()), site: employee.site.clone(), level, min: p - Vec3::new(0.3, 0.0, 0.3), max: p + Vec3::new(0.3, 1.8, 0.3) });
    }
    solids.moving = picks;
    let _ = grid_rotation;
}

const PEDESTRIAN_RADIUS: f32 = 90.0;
const MAX_PEDESTRIANS: usize = 28;
const PEDESTRIAN_SPEED: f32 = 1.3;

/// Ambient walkers on the city sidewalks near the avatar (Unity AmbientPedestrians): a deterministic stream along each side
/// of every non-rural road in both directions, representing no record. Simplification: they walk straight through junctions.
#[allow(clippy::too_many_arguments)]
fn sync_pedestrians(
    mut commands: Commands, time: Res<Time>, session: Res<Session>, space: Option<Res<Space>>, assets: Res<AssetServer>, mut figures: ResMut<Figures>, gltfs: Res<Assets<Gltf>>,
    mut graphs: ResMut<Assets<AnimationGraph>>, mut materials: ResMut<Assets<StandardMaterial>>, mut targets: Query<&mut Target>, player: Query<&Transform, With<crate::player::Player>>,
) {
    let (Some(world), Some(space), Ok(player)) = (session.joined(), space, player.single()) else {
        retire(&mut commands, &mut figures.pedestrians, &HashSet::new());
        return;
    };
    if gltfs.get(&figures.gltf).is_none() {
        return;
    }
    let t = time.elapsed_secs();
    let (px, pz, _) = space.to_map(player.translation);
    let here = Vec2::new(px, pz);
    let network = &world.roads;
    let mut wanted: Vec<(f32, String, Vec2, f32)> = vec![];
    for segment in network.segments.iter().filter(|s| s.kind != food_factory_bevy::world::layout::RoadKind::Rural) {
        let a = &network.nodes[segment.from];
        let b = &network.nodes[segment.to];
        let dx = (a.x.min(b.x) as f32 - here.x).max(here.x - a.x.max(b.x) as f32).max(0.0);
        let dz = (a.z.min(b.z) as f32 - here.y).max(here.y - a.z.max(b.z) as f32).max(0.0);
        if dx.max(dz) > PEDESTRIAN_RADIUS {
            continue;
        }
        let spacing = if segment.kind == food_factory_bevy::world::layout::RoadKind::Arterial { 22.0 } else { 34.0 };
        let direction = Vec2::new(segment.dx as f32, segment.dz as f32);
        let right = Vec2::new(direction.y, -direction.x);
        let start = Vec2::new(a.x as f32, a.z as f32);
        let length = segment.length as f32;
        for side in [-1.0f32, 1.0] {
            for forward in [true, false] {
                let seed = random::fnv1a(&format!("walk:{}:{side}:{forward}", segment.id));
                let phase = (seed % 1000) as f32 / 1000.0 * spacing;
                let head = t * PEDESTRIAN_SPEED + phase;
                let mut n = (head / spacing).floor() as i64;
                loop {
                    let along = head - n as f32 * spacing;
                    if along < 0.0 {
                        break;
                    }
                    if along <= length {
                        let offset = if forward { along } else { length - along };
                        let lateral = segment.width as f32 / 2.0 - 0.45 + if forward { 0.0 } else { -0.5 };
                        let point = start + direction * offset + right * side * lateral;
                        let distance = point.distance(here);
                        if distance < PEDESTRIAN_RADIUS && space.site_at(point.x, point.y).is_none() {
                            let heading = if forward { direction } else { -direction };
                            wanted.push((distance, format!("{}:{side}:{forward}:{n}", segment.id), point, heading.x.atan2(heading.y).to_degrees()));
                        }
                    }
                    n -= 1;
                    if n < (head / spacing).floor() as i64 - 64 {
                        break;
                    }
                }
            }
        }
    }
    wanted.sort_by(|a, b| a.0.total_cmp(&b.0));
    wanted.truncate(MAX_PEDESTRIANS);
    let mut keep = HashSet::new();
    for (_, id, point, yaw) in wanted {
        keep.insert(id.clone());
        let position = space.to_scene(point.x, point.y, space.land(point.x, point.y));
        if let Some(entity) = figures.pedestrians.get(&id) {
            if let Ok(mut target) = targets.get_mut(*entity) {
                target.position = position;
                target.yaw = yaw;
            }
            continue;
        }
        let tint = CUSTOMER_TINTS[(random::fnv1a(&id) % CUSTOMER_TINTS.len() as u64) as usize];
        let entity = spawn_walker(&mut commands, &assets, &mut figures, &gltfs, &mut graphs, Some(tint), format!("Pedestrian {id}"), true, &mut materials);
        commands.entity(entity).insert(Target { position, yaw, snap: true, face_motion: true });
        figures.pedestrians.insert(id, entity);
    }
    retire(&mut commands, &mut figures.pedestrians, &keep);
}
