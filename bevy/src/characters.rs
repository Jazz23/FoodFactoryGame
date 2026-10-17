// People drawn from replicated records (Unity PlayerAvatar remote copies, EmployeeWorker visuals, CustomerPresenter): other
// players with the player model and a shirt hue from their name, employees with the employee model (carrying a box while they
// hold goods), and customers: walking open site cells to their register's queue or table, and in
// front of nearby competitors queued outside the door. Each figure glides toward its replicated pose and plays idle or walk
// (carry idle or carry walk) from how fast it actually moves. Employees can be aimed at. Presentation only.
use std::collections::{HashMap, HashSet, VecDeque};
use std::f32::consts::PI;

use bevy::gltf::GltfMaterialName;
use bevy::prelude::*;
use bevy::world_serialization::{WorldAssetRoot, WorldInstanceReady};

use food_factory_bevy::goods::{self, CustomerState};
use food_factory_bevy::world::random;
use food_factory_bevy::site_walk::{Frontage, Point, SiteWalk, FRONTAGE_QUEUE_SPOTS};

use crate::animation::{CharacterModel, GroundHeight};
use crate::session::Session;
use crate::sites::{DoorOpener, Pick, PickBox, Solids};
use crate::space::{grid_rotation, yaw_of, yaw_rotation, Space};

pub struct CharactersPlugin;

impl Plugin for CharactersPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<Figures>().init_resource::<crate::pedestrian_routes::Ambient>().add_systems(Startup, load_clips).add_systems(Update, (sync_avatars, sync_employees, sync_customers, sync_pedestrians, walk_customers, hide_inside, finish_departures, glide, animate, employee_picks).chain())
            .add_systems(PostUpdate, pose_seated.after(bevy::app::AnimationSystems).before(bevy::transform::TransformSystems::Propagate));
    }
}

const EMPLOYEE_MODEL: &str = "models/Models/Employee/Employee.glb";
const CLIPS: [&str; 4] = [
    "Employee_Armature|Employee_Armature|Employee_Idle",
    "Employee_Armature|Employee_Armature|Employee_Walk",
    "Employee_Armature|Employee_Armature|Employee_CarryIdle",
    "Employee_Armature|Employee_Armature|Employee_CarryWalk",
];
/// Street characters on the employee armature (Unity CharacterFigures): variant 1.. of a figure; variant 0 is the employee model.
const STREET_MODELS: [&str; 2] = ["models/Characters/Resources/Characters/Pedestrian_A.glb", "models/Characters/Resources/Characters/Pedestrian_B.glb"];
/// Parts of a street character that take the clothing tint.
const TINTED_PARTS: [&str; 5] = ["Torso", "UpperArm", "Thigh", "Shin", "Pelvis"];
const BOX_MATERIALS: [&str; 2] = ["Employee_Cardboard", "Employee_Tape"];
const OUTFIT_MATERIAL: &str = "Employee_Teal";
const WALK_SPEED: f32 = 1.4;
const MAX_CUSTOMERS: usize = 60;
/// Competitor queues further than this from the avatar are not drawn.
const CROWD_DISTANCE: f32 = 120.0;
/// A competitor's travelling customer is drawn arriving this many seconds out (Unity's arrival window).
const COMPETITOR_LEAD_SECONDS: i64 = 12;
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
    /// Customers whose record left, walking back out to the street; despawned on arrival or at the deadline (seconds).
    departing: Vec<(Entity, f32)>,
    pedestrians: HashMap<String, Entity>,
    walks: HashMap<String, CachedWalk>,
    /// A travelling customer's arrival path length from the street, for its walk generation and goal cell, so the route is
    /// searched once rather than every frame while it is still too far out to be drawn.
    arrivals: HashMap<String, (u64, (i32, i32), Option<f32>)>,
    /// Competitor frontages by lot.
    frontages: HashMap<String, Frontage>,
}

/// A competitor's customer figure: its lot, where it is going, and whether it goes inside on arrival (ordering or eating).
#[derive(Component)]
struct CompetitorFigure { lot: String, goal: Vec3, enters: bool }

/// Hides a competitor's figure inside its door once it has walked there to order or eat (Unity HideOnArrival).
fn hide_inside(mut figures: Query<(&CompetitorFigure, &CustomerRoute, &mut Visibility)>) {
    for (figure, route, mut visibility) in &mut figures {
        if figure.enters && route.points.is_empty() && *visibility != Visibility::Hidden { *visibility = Visibility::Hidden; }
    }
}

struct CachedWalk { revision: i64, generation: u64, walk: SiteWalk, lines: HashMap<String, Vec<(f32, f32)>> }

#[derive(Component)]
struct CustomerRoute {
    site: String,
    generation: u64,
    goal: Vec3,
    points: VecDeque<Vec3>,
    sit: bool,
    speed: f32,
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

/// Unity CustomerPresenter.Sit, after the animation: thighs forward and shins down for a figure at its seat (the root is lowered
/// by Walker). Bones are turned in world space from this frame's animated pose, so it holds whatever axes the armature uses.
fn pose_seated(
    walkers: Query<(Entity, &Walker)>, children: Query<&Children>, names: Query<&Name>, parents: Query<&ChildOf>, mut transforms: Query<&mut Transform>,
    mut legs: Local<HashMap<Entity, [Entity; 6]>>,
) {
    legs.retain(|e, _| walkers.contains(*e));
    for (entity, walker) in &walkers {
        if !walker.seated { continue; }
        let bones = match legs.get(&entity) {
            Some(bones) => *bones,
            None => {
                let find = |name: &str| children.iter_descendants(entity).find(|e| names.get(*e).is_ok_and(|n| n.as_str() == name));
                let found: Vec<Entity> = ["thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R"].iter().filter_map(|n| find(n)).collect();
                let Ok(bones) = <[Entity; 6]>::try_from(found) else { continue };
                legs.insert(entity, bones);
                bones
            }
        };
        // This frame's world transform of an entity, from the local transforms up its hierarchy.
        let world = |e: Entity, transforms: &Query<&mut Transform>| {
            let mut chain = vec![e];
            while let Ok(parent) = parents.get(*chain.last().unwrap()) { chain.push(parent.parent()); }
            chain.iter().rev().fold(Transform::IDENTITY, |m, e| transforms.get(*e).map_or(m, |t| m.mul_transform(*t)))
        };
        let Ok(root) = transforms.get(entity) else { continue };
        let forward = (root.rotation * Vec3::NEG_Z).with_y(0.0).normalize_or_zero();
        if forward == Vec3::ZERO { continue; }
        for leg in [0, 3] {
            for (bone, next, target) in [(bones[leg], bones[leg + 1], forward + Vec3::NEG_Y * 0.1), (bones[leg + 1], bones[leg + 2], Vec3::NEG_Y + forward * 0.1)] {
                let here = world(bone, &transforms);
                let along = world(next, &transforms).translation - here.translation;
                if along.length_squared() < 1e-6 { continue; }
                let turned = Quat::from_rotation_arc(along.normalize(), target.normalize()) * here.rotation;
                let parent = parents.get(bone).map_or(Transform::IDENTITY, |p| world(p.parent(), &transforms));
                if let Ok(mut local) = transforms.get_mut(bone) { local.rotation = (parent.rotation.inverse() * turned).normalize(); }
            }
        }
    }
}

fn load_clips(assets: Res<AssetServer>, mut figures: ResMut<Figures>) { figures.gltf = assets.load(EMPLOYEE_MODEL); }

fn shirt_of(name: &str) -> Color {
    let hue = (random::fnv1a(name) % 360) as f32;
    Color::hsl(hue, 0.55, 0.5)
}

/// A well-mixed figure variant for a stable key (Unity CharacterFigures.Pick): 0 the employee-derived model, 1.. street characters.
fn figure_variant(key: &str) -> usize {
    let mut key = random::fnv1a(key) as u32;
    key ^= key >> 16;
    key = key.wrapping_mul(0x7feb352d);
    key ^= key >> 15;
    key = key.wrapping_mul(0x846ca68b);
    key ^= key >> 16;
    (key % (1 + STREET_MODELS.len() as u32)) as usize
}

#[allow(clippy::too_many_arguments)]
fn spawn_walker(commands: &mut Commands, assets: &AssetServer, figures: &mut Figures, gltfs: &Assets<Gltf>, graphs: &mut Assets<AnimationGraph>, tint: Option<Color>, name: String, customer: bool, materials: &mut Assets<StandardMaterial>, variant: usize) -> Entity {
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
    let path = if variant == 0 { EMPLOYEE_MODEL } else { STREET_MODELS[(variant - 1) % STREET_MODELS.len()] };
    let model = commands
        .spawn((WorldAssetRoot(assets.load(GltfAssetLabel::Scene(0).from_asset(path))), Transform::from_rotation(Quat::from_rotation_y(PI))))
        .observe(move |ready: On<WorldInstanceReady>, mut commands: Commands, children: Query<&Children>, mut players: Query<&mut AnimationPlayer>, names: Query<&GltfMaterialName>, parents: Query<&ChildOf>, mut walkers: Query<&mut Walker>,
            node_names: Query<&Name>, meshes: Query<(), With<Mesh3d>>| {
            let mut boxes = vec![];
            let mut animation = None;
            if variant > 0 {
                // The street models carry no clips, so glTF gave them no animation targets. They share the employee
                // armature's node names, so the employee clips drive them once each node has its name-path target.
                let armature = children.iter_descendants(ready.event().entity).find(|e| node_names.get(*e).is_ok_and(|n| n.as_str() == "Employee_Armature"));
                if let (Some(armature), Some((graph, nodes))) = (armature, &clips) {
                    let mut stack = vec![(armature, vec![node_names.get(armature).unwrap().clone()])];
                    while let Some((entity, path)) = stack.pop() {
                        commands.entity(entity).insert((bevy::animation::AnimationTargetId::from_names(path.iter()), bevy::animation::AnimatedBy(armature)));
                        for child in children.get(entity).into_iter().flatten() {
                            if let Ok(name) = node_names.get(*child) { let mut next = path.clone(); next.push(name.clone()); stack.push((*child, next)); }
                        }
                    }
                    let mut player = AnimationPlayer::default();
                    for (index, node) in nodes.iter().enumerate() { player.play(*node).repeat().set_weight(if index == 0 { 1.0 } else { 0.0 }); }
                    commands.entity(armature).insert((player, AnimationGraphHandle(graph.clone())));
                    animation = Some(armature);
                }
                if let Some(outfit) = &outfit {
                    for descendant in children.iter_descendants(ready.event().entity).filter(|e| meshes.contains(*e)) {
                        let named = |e: Entity| node_names.get(e).is_ok_and(|n| TINTED_PARTS.iter().any(|p| n.as_str().contains(p)));
                        if named(descendant) || parents.get(descendant).is_ok_and(|p| named(p.parent())) {
                            commands.entity(descendant).insert(MeshMaterial3d(outfit.clone()));
                        }
                    }
                }
            }
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
        let entity = spawn_walker(&mut commands, &assets, &mut figures, &gltfs, &mut graphs, None, format!("Employee {}", employee.name), false, &mut materials, 0);
        commands.entity(entity).insert(Target { position, yaw: employee.pose.yaw, snap: true, face_motion: false });
        figures.employees.insert(employee.id.clone(), entity);
    }
    retire(&mut commands, &mut figures.employees, &keep);
}

/// A drawn site customer's place: queued back from its register, ordering at it, or seated at its table.
/// Register queue lines are cached per walk (`lines`, by register): each one walks every reachable cell of the lot.
fn site_customer_place(view: &goods::Snapshot, site: &str, walk: &SiteWalk, lines: &mut HashMap<String, Vec<(f32, f32)>>, customer: &goods::Customer, queue_index: usize, seat_parts: impl Fn(&str) -> Vec<(f32, f32)>) -> Option<(f32, f32, f32, bool)> {
    match customer.state {
        CustomerState::Queued | CustomerState::Ordering | CustomerState::Travelling => {
            let counter = view.equipment.iter().find(|e| e.id == customer.counter_id && e.site_id == site && e.placed())
                .or_else(|| view.equipment.iter().find(|e| e.site_id == site && e.kind == goods::COUNTER_KIND && e.level == 0 && e.placed()))?;
            let (fx, fz) = goods::grid::facing(counter.rotation);
            let out = Vec2::new(fx as f32, fz as f32);
            let line = lines.entry(counter.id.clone()).or_insert_with(|| walk.service_line(counter));
            let index = if customer.state == CustomerState::Ordering { 0 } else { queue_index + 1 };
            let at = index.min(line.len().checked_sub(1)?);
            let mut spot = line[at];
            let (w, d) = counter.footprint();
            let centre = Vec2::new(counter.cell_x as f32 + w as f32 * 0.5, counter.cell_z as f32 + d as f32 * 0.5);
            if at == 0 {
                // Square in front of the register's middle (a front cell's centre sits half a cell aside on a two-wide one),
                // when that place is open; a side cell keeps its own spot.
                let reach = if fx != 0 { w } else { d } as f32 * 0.5 + 0.5;
                let front = centre + out * reach;
                let in_front = (Vec2::new(spot.0, spot.1) - centre).dot(out) > 0.5;
                if in_front && walk.walk.get(front.x.floor() as i32, front.y.floor() as i32) { spot = (front.x, front.y); }
            }
            // The one ordering faces the register; the queue faces the one ahead of it.
            let ahead = if at == 0 { centre } else { Vec2::new(line[at - 1].0, line[at - 1].1) };
            let look = ahead - Vec2::new(spot.0, spot.1);
            let yaw = if look.length_squared() > 1e-4 { look.x.atan2(look.y).to_degrees() } else { (-out.x).atan2(-out.y).to_degrees() };
            Some((spot.0, spot.1, yaw, false))
        }
        CustomerState::Eating => {
            let table = view.equipment.iter().find(|e| e.id == customer.table_id && e.placed())?;
            let (w, d) = table.footprint();
            let seats = table.seats.max(1);
            let wanted = customer.seat.max(0) % seats;
            // A seat part of the table's own model first (its built-in chairs), facing the table's centre.
            let parts = seat_parts(&table.id);
            let count = parts.len();
            if let Some(&(x, z)) = (0..count).map(|k| &parts[(wanted as usize + k) % count]).find(|p| walk.seat_reachable(**p)) {
                let centre = (table.cell_x as f32 + w as f32 * 0.5, table.cell_z as f32 + d as f32 * 0.5);
                let yaw = (centre.0 - x).atan2(centre.1 - z).to_degrees();
                return Some((x, z, yaw, true));
            }
            // Seats alternate along the two long sides, facing the table.
            let along_x = w >= d;
            let per_side = ((seats + 1) / 2).max(1);
            let place = |seat: i32| {
                let index = seat / 2;
                let side = if seat % 2 == 0 { -1.0 } else { 1.0 };
                let length = if along_x { w } else { d } as f32;
                let t = (index as f32 + 0.5) / per_side as f32 * length;
                if along_x {
                    (table.cell_x as f32 + t, table.cell_z as f32 + d as f32 * 0.5 + side * (d as f32 * 0.5 + 0.35), if side < 0.0 { 0.0 } else { 180.0 })
                } else {
                    (table.cell_x as f32 + w as f32 * 0.5 + side * (w as f32 * 0.5 + 0.35), table.cell_z as f32 + t, if side < 0.0 { 90.0 } else { 270.0 })
                }
            };
            // A generated seat against a wall would be unreachable (Unity seats on the model and samples the NavMesh): take the
            // next seat position that is open, keeping the server's seat when it is.
            let (x, z, yaw) = std::iter::once(wanted).chain((0..seats).filter(|s| *s != wanted)).map(place)
                .find(|&(x, z, _)| walk.seat_reachable((x, z))).unwrap_or_else(|| place(wanted));
            Some((x, z, yaw, true))
        }
    }
}

#[allow(clippy::too_many_arguments)]
fn sync_customers(
    mut commands: Commands, time: Res<Time>, session: Res<Session>, space: Option<Res<Space>>, assets: Res<AssetServer>, mut figures: ResMut<Figures>, gltfs: Res<Assets<Gltf>>, mut graphs: ResMut<Assets<AnimationGraph>>,
    mut materials: ResMut<Assets<StandardMaterial>>, mut query: Query<(&mut Target, &mut Walker, &Transform, Option<&mut CustomerRoute>)>, player: Query<&Transform, With<crate::player::Player>>, drawn: Res<crate::map::DrawnSites>,
    mut competitor_figures: Query<&mut CompetitorFigure>, seats: Res<crate::sites::TableSeats>,
) {
    let (Some(world), Some(space)) = (session.joined(), space) else {
        retire(&mut commands, &mut figures.customers, &HashSet::new());
        for (entity, _) in figures.departing.drain(..) { commands.entity(entity).despawn(); }
        figures.walks.clear();
        return;
    };
    if gltfs.get(&figures.gltf).is_none() {
        return;
    }
    let here = player.single().map(|t| t.translation).unwrap_or_default();
    // Last field is arrival seconds for a routed site figure; competitors keep their frontage presentation.
    let mut wanted: Vec<(String, i32, String, f32, f32, f32, bool, Option<i64>)> = vec![];
    figures.walks.retain(|site, _| drawn.sites.contains(site));
    for site in &drawn.sites {
        let Some(view) = world.view_of(site) else { continue };
        if figures.walks.get(site).is_none_or(|cached| cached.revision != view.revision) {
            let offer = world.offers_by_lot.values().find(|o| o.site_id == *site);
            let Some(walk) = SiteWalk::new(view, site, offer) else { continue };
            let generation = figures.walks.get(site).map_or(0, |old| old.generation + u64::from(!old.walk.same_ground(&walk)));
            figures.walks.insert(site.clone(), CachedWalk { revision: view.revision, generation, walk, lines: HashMap::new() });
        }
        let figures = &mut *figures;
        let cached = figures.walks.get_mut(site).expect("inserted above");
        let (walk, lines, generation) = (&cached.walk, &mut cached.lines, cached.generation);
        let mut queues: HashMap<String, usize> = HashMap::new();
        let mut customers: Vec<&goods::Customer> = view.customers.iter().filter(|c| c.restaurant_id == *site || view.equipment.iter().any(|e| e.id == c.counter_id && &e.site_id == site)).collect();
        customers.sort_by_key(|c| (c.state == CustomerState::Travelling, c.state != CustomerState::Ordering, if c.state == CustomerState::Travelling { c.remaining_seconds } else { c.ticket }, c.id.clone()));
        for customer in customers {
            let index = if matches!(customer.state, CustomerState::Queued | CustomerState::Travelling) {
                let entry = queues.entry(customer.counter_id.clone()).or_insert(0);
                *entry += 1;
                *entry - 1
            } else {
                0
            };
            let parts = |table: &str| seats.0.get(table).map(|list| list.iter().map(|p| space.to_site(site, *p)).collect()).unwrap_or_default();
            if let Some((x, z, yaw, seated)) = site_customer_place(view, site, walk, lines, customer, index, parts) {
                let remaining = if customer.state == CustomerState::Travelling { customer.remaining_seconds } else { 0 };
                if remaining > 0 && !figures.customers.contains_key(&customer.id) {
                    let goal = (x.floor() as i32, z.floor() as i32);
                    let known = figures.arrivals.get(&customer.id).filter(|a| a.0 == generation && a.1 == goal).map(|a| a.2);
                    let length = known.unwrap_or_else(|| {
                        let length = walk.entrance((x, z)).and_then(|entry| {
                            let path = walk.route(entry, (x, z), seated)?;
                            let mut from = entry;
                            Some(path.iter().map(|&p| { let d = Vec2::new(p.0 - from.0, p.1 - from.1).length(); from = p; d }).sum::<f32>())
                        });
                        figures.arrivals.insert(customer.id.clone(), (generation, goal, length));
                        length
                    });
                    let Some(length) = length else { continue };
                    if remaining as f32 > (length / 2.0 + 2.0).max(12.0) { continue; }
                }
                wanted.push((customer.id.clone(), customer.appearance, site.clone(), x, z, yaw, seated, Some(remaining)));
            }
        }
    }
    let travelling: HashSet<&str> = drawn.sites.iter().filter_map(|site| world.view_of(site)).flat_map(|v| v.customers.iter().filter(|c| c.state == CustomerState::Travelling).map(|c| c.id.as_str())).collect();
    figures.arrivals.retain(|id, _| travelling.contains(id.as_str()));
    // Competitors near the avatar (Unity CompetitorFrontage): figures come from the street, queue along the facade, go in to
    // order and eat, and walk back out to the street.
    let mut competitors: Vec<(String, i32, String, String, Point, f32, bool)> = vec![];
    for restaurant in &world.crowd.restaurants {
        let Some(offer) = world.offers_by_lot.get(&restaurant.lot_id) else { continue };
        if !figures.frontages.contains_key(&restaurant.lot_id) {
            let Some(frontage) = Frontage::new(offer) else { continue };
            figures.frontages.insert(restaurant.lot_id.clone(), frontage);
        }
        let frontage = &figures.frontages[&restaurant.lot_id];
        if space.site_point(&offer.site_id, frontage.door.0, frontage.door.1, 0).distance(here) > CROWD_DISTANCE { continue; }
        let mut queued: Vec<_> = restaurant.customers.iter().filter(|c| c.state == CustomerState::Queued).collect();
        queued.sort_by_key(|c| (c.ticket, c.id.clone()));
        let yaw = (-frontage.along.0).atan2(-frontage.along.1).to_degrees();
        for customer in &restaurant.customers {
            let enters = matches!(customer.state, CustomerState::Ordering | CustomerState::Eating);
            let target = match customer.state {
                CustomerState::Queued => {
                    let rank = queued.iter().position(|c| c.id == customer.id).unwrap_or(0);
                    if rank >= FRONTAGE_QUEUE_SPOTS { continue; }
                    frontage.queue_spot(rank)
                }
                _ if enters => frontage.door,
                _ => frontage.apron,
            };
            let has = figures.customers.contains_key(&customer.id);
            // Figures already inside or still far off are not drawn arriving.
            if !has && (enters || (customer.state == CustomerState::Travelling && customer.remaining_seconds > COMPETITOR_LEAD_SECONDS)) { continue; }
            competitors.push((customer.id.clone(), customer.appearance, offer.site_id.clone(), restaurant.lot_id.clone(), target, yaw, enters));
        }
    }
    wanted.truncate(MAX_CUSTOMERS);
    let mut keep = HashSet::new();
    for (id, appearance, frame, x, z, yaw, seated, arrival) in wanted {
        keep.insert(id.clone());
        let position = space.site_point(&frame, x, z, 0);
        // The same record can choose another restaurant after walking out. Each site's presentation starts at its own
        // validated entrance; an old figure must never draw an unchecked inter-site segment to the new lot.
        let restart = figures.customers.get(&id).is_some_and(|entity| query.get(*entity).is_ok_and(|(_, _, _, route)| {
            match (arrival, route) {
                (Some(_), Some(route)) => route.site != frame,
                (Some(_), None) | (None, Some(_)) => true,
                (None, None) => false,
            }
        }));
        if restart {
            if let Some(entity) = figures.customers.remove(&id) { commands.entity(entity).despawn(); }
        }
        if let Some(entity) = figures.customers.get(&id) {
            if let Ok((mut target, mut walker, transform, route)) = query.get_mut(*entity) {
                if let Some(remaining) = arrival {
                    let cached = &figures.walks[&frame];
                    let needs_route = route.as_ref().is_none_or(|r| r.site != frame || r.generation != cached.generation || r.goal != position || r.sit != seated);
                    if needs_route {
                        let points = cached.walk.route(space.to_site(&frame, transform.translation), (x, z), seated).unwrap_or_default();
                        commands.entity(*entity).insert(CustomerRoute { site: frame.clone(), generation: cached.generation, goal: position,
                            points: points.into_iter().map(|(x, z)| space.site_point(&frame, x, z, 0)).collect(), sit: seated, speed: 2.0 });
                    } else if let Some(mut route) = route {
                        let mut from = transform.translation;
                        let left: f32 = route.points.iter().map(|&p| { let d = p.distance(from); from = p; d }).sum();
                        route.speed = if remaining > 0 { (left / (remaining as f32 - 0.5).max(0.5)).clamp(1.6, 3.2) } else if !seated && left > 3.0 { 2.4 } else { 1.8 };
                    }
                    target.yaw = yaw;
                    continue;
                }
                commands.entity(*entity).remove::<CustomerRoute>();
                target.position = position;
                target.yaw = yaw;
                walker.seated = seated;
            }
            continue;
        }
        let routed = if arrival.is_some() {
            let cached = &figures.walks[&frame];
            let Some(entry) = cached.walk.entrance((x, z)) else { continue };
            let Some(points) = cached.walk.route(entry, (x, z), seated) else { continue };
            let start = space.site_point(&frame, entry.0, entry.1, 0);
            Some((start, CustomerRoute { site: frame.clone(), generation: cached.generation, goal: position,
                points: points.into_iter().map(|(x, z)| space.site_point(&frame, x, z, 0)).collect(), sit: seated, speed: 2.0 }))
        } else { None };
        let tint = CUSTOMER_TINTS[(random::fnv1a(&id).wrapping_add(appearance as u64) % CUSTOMER_TINTS.len() as u64) as usize];
        let entity = spawn_walker(&mut commands, &assets, &mut figures, &gltfs, &mut graphs, Some(tint), format!("Customer {id}"), true, &mut materials, figure_variant(&id));
        if let Some((start, route)) = routed {
            commands.entity(entity).insert((Transform::from_translation(start), Target { position: start, yaw, snap: false, face_motion: true }, route));
        } else {
            commands.entity(entity).insert(Target { position, yaw, snap: true, face_motion: true });
        }
        figures.customers.insert(id, entity);
    }
    for (id, appearance, site, lot, target, yaw, enters) in competitors {
        if keep.len() >= MAX_CUSTOMERS && !figures.customers.contains_key(&id) { continue; }
        keep.insert(id.clone());
        let frontage = figures.frontages[&lot].clone();
        let goal = space.site_point(&site, target.0, target.1, 0);
        if let Some(&entity) = figures.customers.get(&id) {
            let Ok(mut figure) = competitor_figures.get_mut(entity) else { continue };
            figure.enters = enters;
            if figure.goal != goal {
                figure.goal = goal;
                commands.entity(entity).insert(CustomerRoute { site: site.clone(), generation: u64::MAX - 1, goal, points: VecDeque::from([goal]), sit: false, speed: WALK_SPEED });
            }
            if let Ok((mut t, ..)) = query.get_mut(entity) { t.yaw = yaw; }
            continue;
        }
        let start = frontage.street_point(&id);
        let start = space.site_point(&site, start.0, start.1, 0);
        let points: VecDeque<Vec3> = frontage.arrive(target).into_iter().map(|(x, z)| space.site_point(&site, x, z, 0)).collect();
        let tint = CUSTOMER_TINTS[(random::fnv1a(&id).wrapping_add(appearance as u64) % CUSTOMER_TINTS.len() as u64) as usize];
        let entity = spawn_walker(&mut commands, &assets, &mut figures, &gltfs, &mut graphs, Some(tint), format!("Customer {id}"), true, &mut materials, figure_variant(&id));
        commands.entity(entity).insert((Transform::from_translation(start), Target { position: start, yaw, snap: false, face_motion: true },
            CustomerRoute { site: site.clone(), generation: u64::MAX - 1, goal, points, sit: false, speed: WALK_SPEED * 1.5 },
            CompetitorFigure { lot, goal, enters }));
        figures.customers.insert(id, entity);
    }
    // Unity CustomerPresenter.Leave: a site figure whose record left walks its open cells back out to the street.
    let now = time.elapsed_secs();
    let stale: Vec<String> = figures.customers.keys().filter(|k| !keep.contains(*k)).cloned().collect();
    for id in stale {
        let Some(entity) = figures.customers.remove(&id) else { continue };
        if let Ok(mut figure) = competitor_figures.get_mut(entity) {
            // A competitor's figure comes back out of the door (if it was inside) and walks along the street out of view.
            figure.enters = false;
            let site = world.offers_by_lot.get(&figure.lot).map(|o| o.site_id.clone()).unwrap_or_default();
            if let (Some(frontage), Ok((_, _, transform, _))) = (figures.frontages.get(&figure.lot), query.get(entity)) {
                let points: VecDeque<Vec3> = frontage.depart(frontage.street_point(&id)).into_iter().map(|(x, z)| space.site_point(&site, x, z, 0)).collect();
                let mut at = transform.translation;
                let length: f32 = points.iter().map(|&p| { let d = p.distance(at); at = p; d }).sum();
                let goal = points.back().copied().unwrap_or_default();
                commands.entity(entity).insert(CustomerRoute { site, generation: u64::MAX, goal, points, sit: false, speed: WALK_SPEED });
                figures.departing.push((entity, now + 2.0 + length / WALK_SPEED));
                continue;
            }
        }
        let exit = query.get(entity).ok().and_then(|(_, _, transform, route)| {
            let site = &route?.site;
            let walk = &figures.walks.get(site)?.walk;
            let from = space.to_site(site, transform.translation);
            let points = walk.route(from, walk.entrance(from)?, false)?;
            let points: VecDeque<Vec3> = points.into_iter().map(|(x, z)| space.site_point(site, x, z, 0)).collect();
            let mut at = transform.translation;
            let length: f32 = points.iter().map(|&p| { let d = p.distance(at); at = p; d }).sum();
            Some((site.clone(), points, length))
        });
        match exit {
            Some((site, points, length)) => {
                let goal = points.back().copied().unwrap_or_default();
                commands.entity(entity).insert(CustomerRoute { site, generation: u64::MAX, goal, points, sit: false, speed: 2.0 });
                figures.departing.push((entity, now + 2.0 + length / 2.0));
            }
            None => commands.entity(entity).despawn(),
        }
    }
}

/// Departing customers vanish once they reach the street or run out of time (a blocked or changed route).
fn finish_departures(mut commands: Commands, time: Res<Time>, mut figures: ResMut<Figures>, routes: Query<&CustomerRoute>) {
    let now = time.elapsed_secs();
    figures.departing.retain(|&(entity, deadline)| {
        let arrived = routes.get(entity).map_or(true, |route| route.points.is_empty());
        if arrived || now >= deadline { commands.entity(entity).despawn(); false } else { true }
    });
}

/// Consume a bounded distance along corners, including on slow frames; never use replication's long-gap snap here.
fn walk_customers(time: Res<Time>, mut figures: Query<(&mut CustomerRoute, &mut Target, &mut Transform, &mut Walker, &mut Visibility)>) {
    for (mut route, mut target, mut transform, mut walker, mut visibility) in &mut figures {
        *visibility = Visibility::Inherited;
        if walker.seated { transform.translation.y += 0.45; }
        walker.seated = false;
        let mut budget = route.speed * time.delta_secs();
        let mut direction = Vec3::ZERO;
        while let Some(&point) = route.points.front() {
            let gap = point - transform.translation;
            let distance = gap.length();
            if distance > 0.001 { direction = gap; }
            if distance <= budget {
                transform.translation = point;
                budget -= distance;
                route.points.pop_front();
            } else {
                transform.translation += gap / distance * budget;
                break;
            }
        }
        walker.seated = route.sit && route.points.is_empty() && transform.translation.distance(route.goal) < 0.05;
        if walker.seated { transform.translation.y -= 0.45; }
        let yaw = if direction.length_squared() > 0.001 { yaw_of(direction) } else { target.yaw };
        transform.rotation = transform.rotation.slerp(yaw_rotation(yaw), (1.0 - (-10.0 * time.delta_secs()).exp()).min(1.0));
        target.position = transform.translation;
    }
}

fn glide(time: Res<Time>, mut figures: Query<(&mut Target, &mut Transform, Option<&mut Visibility>, Option<&mut GroundHeight>), Without<CustomerRoute>>) {
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
        // Any real motion plays the walk in full, at the pace the figure covers ground, so its feet keep to the floor.
        let walk = (walker.speed / 0.5).clamp(0.0, 1.0);
        let pace = (walker.speed / WALK_SPEED).clamp(0.6, 2.4);
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
        for (index, (node, weight)) in nodes.iter().zip(weights).enumerate() {
            if let Some(active) = player.animation_mut(*node) {
                active.set_weight(weight);
                // The walk and carry-walk clips (odd nodes) are authored at WALK_SPEED.
                if index % 2 == 1 && (active.speed() - pace).abs() > 0.02 {
                    active.set_speed(pace);
                }
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

/// Draw local sidewalk walkers from the crosswalk-aware route planner; no replicated customer record is created.
#[allow(clippy::too_many_arguments)]
fn sync_pedestrians(
    mut commands: Commands, time: Res<Time>, session: Res<Session>, space: Option<Res<Space>>, assets: Res<AssetServer>, mut figures: ResMut<Figures>, gltfs: Res<Assets<Gltf>>,
    mut graphs: ResMut<Assets<AnimationGraph>>, mut materials: ResMut<Assets<StandardMaterial>>, mut targets: Query<&mut Target>, player: Query<&Transform, With<crate::player::Player>>,
    mut ambient: ResMut<crate::pedestrian_routes::Ambient>, clock: Res<crate::vehicles::TrafficClock>, cars: Res<crate::vehicles::TrafficPositions>,
) {
    let (Some(world), Some(space), Ok(player)) = (session.joined(), space, player.single()) else {
        retire(&mut commands, &mut figures.pedestrians, &HashSet::new());
        ambient.clear();
        return;
    };
    if gltfs.get(&figures.gltf).is_none() {
        return;
    }
    let (px, pz, _) = space.to_map(player.translation);
    let here = Vec2::new(px, pz);
    ambient.tick(&world.world_id, &world.roads, clock.seconds, time.delta_secs(), here, &cars.0);
    let wanted = ambient.walkers.iter().map(|w| (w.id.clone(), w.point, w.heading.x.atan2(w.heading.y).to_degrees())).collect::<Vec<_>>();
    let mut keep = HashSet::new();
    for (id, point, yaw) in wanted {
        keep.insert(id.clone());
        let position = space.to_scene(point.x, point.y, space.land(point.x, point.y) + ambient.surface_lift(&world.roads, point));
        if let Some(entity) = figures.pedestrians.get(&id) {
            if let Ok(mut target) = targets.get_mut(*entity) {
                target.position = position;
                target.yaw = yaw;
            }
            continue;
        }
        let tint = CUSTOMER_TINTS[(random::fnv1a(&id) % CUSTOMER_TINTS.len() as u64) as usize];
        let entity = spawn_walker(&mut commands, &assets, &mut figures, &gltfs, &mut graphs, Some(tint), format!("Pedestrian {id}"), true, &mut materials, figure_variant(&id));
        commands.entity(entity).insert(Target { position, yaw, snap: true, face_motion: true });
        figures.pedestrians.insert(id, entity);
    }
    retire(&mut commands, &mut figures.pedestrians, &keep);
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::Duration;

    fn app_with_route(points: Vec<Vec3>, speed: f32, sit: bool) -> (App, Entity) {
        let mut app = App::new();
        app.insert_resource(Time::<()>::default()).add_systems(Update, walk_customers);
        let goal = *points.last().unwrap();
        let entity = app.world_mut().spawn((CustomerRoute { site: "test".into(), generation: 0, goal, points: points.into(), sit, speed },
            Target { position: Vec3::ZERO, yaw: 0.0, snap: false, face_motion: true }, Transform::default(), Walker::default(), Visibility::Hidden)).id();
        (app, entity)
    }

    #[test]
    fn customer_slow_frame_consumes_corners_without_cutting_or_snapping() {
        let (mut app, entity) = app_with_route(vec![Vec3::new(10.0, 0.0, 0.0), Vec3::new(10.0, 0.0, -10.0)], 3.0, false);
        app.world_mut().resource_mut::<Time>().advance_by(Duration::from_secs(5));
        app.update();
        assert_eq!(app.world().get::<Transform>(entity).unwrap().translation, Vec3::new(10.0, 0.0, -5.0));
        assert_eq!(app.world().get::<CustomerRoute>(entity).unwrap().points.len(), 1);
        assert_eq!(*app.world().get::<Visibility>(entity).unwrap(), Visibility::Inherited);
    }

    #[test]
    fn customer_lowers_only_after_arrival_and_does_not_sink_each_frame() {
        let (mut app, entity) = app_with_route(vec![Vec3::new(4.0, 0.0, 0.0)], 2.0, true);
        app.world_mut().resource_mut::<Time>().advance_by(Duration::from_secs(1));
        app.update();
        assert!(!app.world().get::<Walker>(entity).unwrap().seated);
        assert_eq!(app.world().get::<Transform>(entity).unwrap().translation, Vec3::new(2.0, 0.0, 0.0));
        app.update();
        assert!(app.world().get::<Walker>(entity).unwrap().seated);
        assert_eq!(app.world().get::<Transform>(entity).unwrap().translation, Vec3::new(4.0, -0.45, 0.0));
        app.update();
        assert_eq!(app.world().get::<Transform>(entity).unwrap().translation, Vec3::new(4.0, -0.45, 0.0));
    }
}
