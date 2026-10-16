// The local avatar (Unity PlayerAvatar and SiteEntry): camera-relative walk and sprint, jump and gravity, turning toward the
// move direction, standing on the drawn land, lot floors and storey slabs, and bumping into walls and machines (the drawn
// sites' collision boxes). It appears at the server's spawn pose once the world is drawn, reports its pose ten times a second,
// asks for the sites it should see, and asks to enter a company site when it walks onto that lot. The server owns everything
// it does with the pose; this only moves the presentation.
use bevy::prelude::*;

use food_factory_bevy::protocol::{ClientMessage, Command, Pose};

use crate::actions::PlayerActions;
use crate::animation::{CharacterModel, GroundHeight};
use crate::camera::OrbitCamera;
use crate::map::MapState;
use crate::session::Session;
use crate::sites::{DoorOpener, Solids};
use crate::space::{yaw_of, yaw_rotation, Space};

pub struct PlayerPlugin;

impl Plugin for PlayerPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Startup, spawn_player)
            .add_systems(Update, (place_on_join, move_player.in_set(PlayerSet::Move), report_pose, enter_sites).chain());
    }
}

#[derive(SystemSet, Debug, Clone, PartialEq, Eq, Hash)]
pub enum PlayerSet {
    Move,
}

const SPEED: f32 = 4.0;
const SPRINT_SPEED: f32 = 7.0;
const JUMP_HEIGHT: f32 = 1.2;
const TURN_DEGREES_PER_SECOND: f32 = 720.0;
const GRAVITY: f32 = -20.0;
// Downward speed held while grounded, as Unity's controller keeps pressing into the ground.
const GROUNDED_PUSH: f32 = -1.0;
const RADIUS: f32 = 0.28;
const HEIGHT: f32 = 1.8;
// Steps up to this height are climbed (kerbs, lot edges, a slab lip).
const STEP: f32 = 0.35;
const POSE_SECONDS: f32 = 0.1;
const WATCH_SECONDS: f32 = 1.0;
/// Company sites within this distance are drawn as well as the current one.
const WATCH_DISTANCE: f32 = 300.0;

#[derive(Component, Default)]
pub struct Player {
    vertical_speed: f32,
    grounded: bool,
    /// Placed at the spawn pose of this join.
    placed: bool,
    /// Movement is frozen while a panel holds focus or the world is not drawn.
    pub frozen: bool,
}

fn spawn_player(mut commands: Commands, assets: Res<AssetServer>) {
    commands
        .spawn((Player::default(), Transform::default(), Visibility::Hidden, GroundHeight::default(), DoorOpener { customer: false }, Name::new("Player")))
        .with_child((
            CharacterModel::new(assets.load("models/player.glb")),
            // The exported model faces +Z; a Bevy transform's forward is -Z (the Unity prefab turns its model child likewise).
            Transform::from_rotation(Quat::from_rotation_y(std::f32::consts::PI)),
        ));
}

/// Shows the avatar at the server's spawn pose once the city is drawn; hides it outside a world.
fn place_on_join(session: Res<Session>, space: Option<Res<Space>>, map: Res<MapState>, mut players: Query<(&mut Player, &mut Transform, &mut Visibility)>, mut rigs: Query<&mut OrbitCamera>) {
    let Ok((mut player, mut transform, mut visibility)) = players.single_mut() else { return };
    let Some(world) = session.joined() else {
        player.placed = false;
        *visibility = Visibility::Hidden;
        return;
    };
    if player.placed {
        return;
    }
    let Some(space) = space else { return };
    if !map.built {
        return;
    }
    let pose = world.spawn;
    let ground = space.land(pose.x, pose.z);
    let y = if pose.y.is_finite() { pose.y.max(ground) } else { ground };
    transform.translation = space.to_scene(pose.x, pose.z, y + 0.05);
    transform.rotation = yaw_rotation(pose.yaw);
    player.placed = true;
    player.vertical_speed = 0.0;
    // The camera starts behind the avatar.
    for mut rig in &mut rigs {
        rig.yaw = pose.yaw;
    }
    *visibility = Visibility::Inherited;
}

fn move_player(
    time: Res<Time>, actions: Res<PlayerActions>, rig: Query<&OrbitCamera>, mut players: Query<(&mut Player, &mut Transform, &mut GroundHeight)>, solids: Res<Solids>,
    space: Option<Res<Space>>,
) {
    let delta = time.delta_secs().min(0.1);
    let yaw = rig.single().map(OrbitCamera::yaw_rotation).unwrap_or_default();
    let Ok((mut player, mut transform, mut ground_height)) = players.single_mut() else { return };
    let Some(space) = space else { return };
    if !player.placed {
        return;
    }
    let movement = if player.frozen { Vec2::ZERO } else { actions.movement };
    let direction = yaw * Vec3::new(movement.x, 0.0, -movement.y);
    player.vertical_speed = if player.grounded {
        if actions.jump_pressed && !player.frozen { (-2.0 * GRAVITY * JUMP_HEIGHT).sqrt() } else { GROUNDED_PUSH }
    } else {
        player.vertical_speed + GRAVITY * delta
    };
    let speed = if actions.sprint { SPRINT_SPEED } else { SPEED };
    let displacement = (direction * speed + Vec3::Y * player.vertical_speed) * delta;
    let mut position = transform.translation;
    let near: Vec<&(Vec3, Vec3)> = solids.boxes.iter().chain(&solids.moving_boxes).filter(|(min, max)| {
        position.x + 3.0 > min.x && position.x - 3.0 < max.x && position.z + 3.0 > min.z && position.z - 3.0 < max.z && position.y + 4.0 > min.y && position.y - 4.0 < max.y
    }).collect();
    player.grounded = false;
    // Axis-separated swept bounds avoid tunnelling across thin walls during a slow frame.
    for axis in [0, 2, 1] {
        let old = position[axis];
        position[axis] += displacement[axis];
        for (min, max) in &near {
            let low = position + Vec3::new(-RADIUS, 0.0, -RADIUS);
            let high = position + Vec3::new(RADIUS, HEIGHT, RADIUS);
            let other_overlap = (0..3).filter(|i| *i != axis).all(|i| high[i] > min[i] + 0.001 && low[i] < max[i] - 0.001);
            if !other_overlap {
                continue;
            }
            let extent_low = if axis == 1 { 0.0 } else { RADIUS };
            let extent_high = if axis == 1 { HEIGHT } else { RADIUS };
            if axis != 1 && max.y - position.y <= STEP && max.y > position.y {
                // A low box is stepped onto rather than blocking.
                continue;
            }
            if displacement[axis] > 0.0 && old + extent_high <= min[axis] + 0.001 && position[axis] + extent_high > min[axis] {
                position[axis] = min[axis] - extent_high;
                if axis == 1 {
                    player.vertical_speed = 0.0;
                }
            } else if displacement[axis] < 0.0 && old - extent_low >= max[axis] - 0.001 && position[axis] - extent_low < max[axis] {
                position[axis] = max[axis] + extent_low;
                if axis == 1 {
                    player.grounded = true;
                    player.vertical_speed = 0.0;
                }
            }
        }
    }
    // The land (and each lot's levelled floor) is the lowest ground; a low box under the feet is stood on.
    let mut ground = space.ground(position);
    for (min, max) in &near {
        let inside = position.x + RADIUS * 0.5 > min.x && position.x - RADIUS * 0.5 < max.x && position.z + RADIUS * 0.5 > min.z && position.z - RADIUS * 0.5 < max.z;
        if inside && max.y <= position.y + STEP && max.y > ground {
            ground = max.y;
        }
    }
    if position.y <= ground + 0.001 || (player.vertical_speed <= 0.0 && position.y - ground < STEP && position.y - ground > 0.0 && displacement.y <= 0.0 && ground > position.y - 0.05) {
        player.grounded = true;
        position.y = ground;
        player.vertical_speed = 0.0;
    }
    if position.y <= ground + 0.02 {
        player.grounded = true;
    }
    if position.y < ground {
        position.y = ground;
    }
    transform.translation = position;
    ground_height.0 = ground;
    if direction.length_squared() > 0.0001 {
        let target = Transform::default().looking_to(direction, Vec3::Y).rotation;
        transform.rotation = transform.rotation.rotate_towards(target, TURN_DEGREES_PER_SECOND.to_radians() * delta);
    }
}

/// The avatar's pose to the server at 10 Hz (map metres and absolute height), and the sites it watches once a second.
fn report_pose(time: Res<Time>, mut session: ResMut<Session>, space: Option<Res<Space>>, players: Query<(&Player, &Transform)>, mut pose_timer: Local<f32>, mut watch_timer: Local<f32>) {
    let Ok((player, transform)) = players.single() else { return };
    let Some(space) = space else { return };
    if !player.placed || session.joined().is_none() {
        return;
    }
    *pose_timer += time.delta_secs();
    *watch_timer += time.delta_secs();
    let (x, z, y) = space.to_map(transform.translation);
    if *pose_timer >= POSE_SECONDS {
        *pose_timer = 0.0;
        let yaw = yaw_of(transform.rotation * Vec3::NEG_Z);
        session.send(ClientMessage::Pose { pose: Pose { x, y, z, yaw } });
    }
    if *watch_timer >= WATCH_SECONDS {
        *watch_timer = 0.0;
        let Some(world) = session.joined() else { return };
        let mut sites = vec![world.site.clone()];
        let mut nearby: Vec<(f32, String)> = world.company_sites().into_iter().filter(|s| *s != world.site).filter_map(|s| {
            let lot = space.lots.get(&s)?;
            let dx = (lot.x as f32 + lot.width as f32 * 0.5 - x).abs() - lot.width as f32 * 0.5;
            let dz = (lot.z as f32 + lot.depth as f32 * 0.5 - z).abs() - lot.depth as f32 * 0.5;
            let distance = dx.max(dz).max(0.0);
            (distance < WATCH_DISTANCE).then_some((distance, s))
        }).collect();
        nearby.sort_by(|a, b| a.0.total_cmp(&b.0));
        sites.extend(nearby.into_iter().map(|(_, s)| s).take(8));
        if world.watched != sites {
            session.send(ClientMessage::Watch { sites: sites.clone() });
            if let Some(world) = session.joined_mut() {
                world.views.retain(|site, _| sites.contains(site));
                world.watched = sites;
            }
        }
    }
}

/// Walking onto another lot of the company asks the server to make it the current site (Unity SiteEntry).
fn enter_sites(time: Res<Time>, mut session: ResMut<Session>, space: Option<Res<Space>>, players: Query<(&Player, &Transform)>, mut asked: Local<(String, f64)>) {
    let Ok((player, transform)) = players.single() else { return };
    let Some(space) = space else { return };
    if !player.placed {
        return;
    }
    let Some(world) = session.joined() else { return };
    let (x, z, _) = space.to_map(transform.translation);
    let Some(lot) = space.site_at(x, z) else { return };
    let site = lot.site.clone();
    if site == world.site || !world.company_sites().contains(&site) {
        return;
    }
    let now = time.elapsed_secs_f64();
    if asked.0 == site && now - asked.1 < 3.0 {
        return;
    }
    *asked = (site.clone(), now);
    session.command(Command::EnterSite { site }, now);
}
