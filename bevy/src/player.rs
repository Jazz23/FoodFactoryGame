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
            .add_systems(Update, (place_on_join, ride_elevator, move_player.in_set(PlayerSet::Move), report_pose, enter_sites).chain());
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
    /// Local presentation-only vehicle motion: previous body centres and a fading push, never reported as gameplay state.
    previous_vehicle_centres: Vec<Vec3>,
    vehicle_shove: Vec3,
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
        player.previous_vehicle_centres.clear();
        player.vehicle_shove = Vec3::ZERO;
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
    player.previous_vehicle_centres.clear();
    player.vehicle_shove = Vec3::ZERO;
    // The camera starts behind the avatar.
    for mut rig in &mut rigs {
        rig.yaw = pose.yaw;
    }
    *visibility = Visibility::Inherited;
}

/// Unity ElevatorRider (decision 0020): on the elevator cell of the building the avatar is in, Floor up / Floor down move it
/// to the same cell one storey up or down. A local presentation move the pose report replicates; no server rule reads it.
fn ride_elevator(
    actions: Res<PlayerActions>, indoors: Res<crate::sites::Indoors>, build: Res<crate::build::Build>, session: Res<Session>, space: Option<Res<Space>>,
    mut players: Query<(&mut Player, &mut Transform)>,
) {
    let step = i32::from(actions.floor_up_pressed) - i32::from(actions.floor_down_pressed);
    if step == 0 || build.active.is_some() { return; }
    let (Some(space), Some(world), Ok((mut player, mut transform))) = (space, session.joined(), players.single_mut()) else { return };
    if player.frozen || !player.placed { return; }
    let (Some(site), Some(id)) = (indoors.site.as_ref(), indoors.building.as_ref()) else { return };
    let Some(building) = world.view_of(site).and_then(|v| v.buildings.iter().find(|b| &b.id == id)) else { return };
    let Some(level) = elevator_target(building, indoors.cell, indoors.level, step) else { return };
    transform.translation = space.site_point(site, building.elevator_x as f32 + 0.5, building.elevator_z as f32 + 0.5, level) + Vec3::Y * 0.05;
    player.vertical_speed = 0.0;
    player.previous_vehicle_centres.clear();
}

/// The storey a ride from this cell and level reaches, if the cell is the shaft and the storey exists.
fn elevator_target(building: &food_factory_bevy::goods::Building, cell: (i32, i32), level: i32, step: i32) -> Option<i32> {
    let target = level + step;
    (food_factory_bevy::goods::grid::is_shaft(building, cell.0, cell.1) && target >= 0 && target < building.floors).then_some(target)
}

fn move_player(
    time: Res<Time>, actions: Res<PlayerActions>, rig: Query<&OrbitCamera>, mut players: Query<(&mut Player, &mut Transform, &mut GroundHeight)>, solids: Res<Solids>,
    space: Option<Res<Space>>, map_solids: Res<crate::map::MapSolids>,
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
    let close = |(min, max): &&(Vec3, Vec3)| {
        position.x + 3.0 > min.x && position.x - 3.0 < max.x && position.z + 3.0 > min.z && position.z - 3.0 < max.z && position.y + 4.0 > min.y && position.y - 4.0 < max.y
    };
    // Static geometry: site walls and machines plus the city's scenery (houses, trees, signs).
    let statics: Vec<(Vec3, Vec3)> = solids.boxes.iter().chain(map_solids.near(position, 4.0)).filter(close).copied().collect();
    let near: Vec<&(Vec3, Vec3)> = statics.iter().chain(solids.moving_boxes.iter().filter(close)).collect();
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
    // Unity VehicleBump: resolve overlap against this frame's vehicle boxes, then sweep the
    // correction and shove against static geometry so even a large frame cannot push through a wall.
    let (correction, shove, centres) = vehicle_bump(position, delta, &solids.moving_boxes, &player.previous_vehicle_centres, player.vehicle_shove);
    player.previous_vehicle_centres = centres;
    player.vehicle_shove = shove;
    position = sweep_horizontal(position, correction + shove * delta, &statics);
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

const BUMP_SKIN: f32 = 0.05;
const BUMP_STILL: f32 = 0.3;
const BUMP_MOVING: f32 = 1.5;
const BUMP_REACH: f32 = 20.0;

/// Find the shortest horizontal ejection from nearby AABBs and estimate velocity from matched previous centres.
fn vehicle_bump(position: Vec3, delta: f32, boxes: &[(Vec3, Vec3)], previous: &[Vec3], mut shove: Vec3) -> (Vec3, Vec3, Vec<Vec3>) {
    let centres: Vec<Vec3> = boxes.iter().map(|(min, max)| (*min + *max) * 0.5).collect();
    let mut used = vec![false; previous.len()];
    let mut correction = Vec3::ZERO;
    for (index, (min, max)) in boxes.iter().enumerate() {
        let centre = centres[index];
        if centre.distance_squared(position + Vec3::Y * (HEIGHT * 0.5)) > BUMP_REACH * BUMP_REACH { continue; }
        let mut nearest = None;
        for (old_index, old) in previous.iter().enumerate() {
            if used[old_index] { continue; }
            let distance = old.distance_squared(centre);
            if distance < nearest.map_or(25.0, |(_, best)| best) { nearest = Some((old_index, distance)); }
        }
        let velocity = nearest.map_or(Vec3::ZERO, |(old_index, distance)| {
            used[old_index] = true;
            if delta > 0.0 && distance < 25.0 { (centre - previous[old_index]) / delta } else { Vec3::ZERO }
        });
        let low = position;
        let high = position + Vec3::Y * HEIGHT;
        if high.y < min.y || low.y > max.y { continue; }
        let x_left = position.x + RADIUS - min.x;
        let x_right = max.x + RADIUS - position.x;
        let z_back = position.z + RADIUS - min.z;
        let z_front = max.z + RADIUS - position.z;
        let (penetration_x, sign_x) = if x_left <= x_right { (x_left, -1.0) } else { (x_right, 1.0) };
        let (penetration_z, sign_z) = if z_back <= z_front { (z_back, -1.0) } else { (z_front, 1.0) };
        // Require overlap on both horizontal axes. Prefer the minimum translation axis.
        if penetration_x <= 0.02 || penetration_z <= 0.02 { continue; }
        let out_along_x = penetration_x <= penetration_z;
        let out = if out_along_x { Vec3::X * sign_x * (penetration_x + BUMP_SKIN) } else { Vec3::Z * sign_z * (penetration_z + BUMP_SKIN) };
        correction += out;
        // Vehicles drive along their longer side (roads are axis-aligned), so leaving along it means a bumper hit.
        let travels_along_x = max.x - min.x >= max.z - min.z;
        let bumper = out_along_x == travels_along_x;
        let flat_speed = Vec3::new(velocity.x, 0.0, velocity.z).length();
        if flat_speed < BUMP_STILL { continue; }
        let direction = if bumper && flat_speed >= BUMP_MOVING {
            // Knocked off the bumper toward the vehicle's nearer side, not carried along ahead of it.
            let side = if travels_along_x { Vec3::Z * (position.z - centre.z).signum() } else { Vec3::X * (position.x - centre.x).signum() };
            (side + out.normalize() * 0.5).normalize()
        } else { out.normalize() };
        let speed = (flat_speed + 1.0).clamp(3.0, 9.0);
        if shove.dot(direction) < speed { shove = direction * speed; }
    }
    shove *= (-5.0 * delta).exp();
    if shove.length_squared() < 0.01 { shove = Vec3::ZERO; }
    (correction, shove, centres)
}

/// Sweeps the avatar's horizontal footprint axis-by-axis through static AABBs; used by vehicle pushes.
fn sweep_horizontal(mut position: Vec3, displacement: Vec3, obstacles: &[(Vec3, Vec3)]) -> Vec3 {
    for axis in [0, 2] {
        let old = position[axis];
        position[axis] += displacement[axis];
        for (min, max) in obstacles {
            let low = position + Vec3::new(-RADIUS, 0.0, -RADIUS);
            let high = position + Vec3::new(RADIUS, HEIGHT, RADIUS);
            let other = (0..3).filter(|i| *i != axis).all(|i| high[i] > min[i] + 0.001 && low[i] < max[i] - 0.001);
            if !other { continue; }
            if displacement[axis] > 0.0 && old + RADIUS <= min[axis] + 0.001 && position[axis] + RADIUS > min[axis] {
                position[axis] = min[axis] - RADIUS;
            } else if displacement[axis] < 0.0 && old - RADIUS >= max[axis] - 0.001 && position[axis] - RADIUS < max[axis] {
                position[axis] = max[axis] + RADIUS;
            }
        }
    }
    position
}

#[cfg(test)]
mod vehicle_bump_tests {
    use super::*;

    #[test]
    fn bump_correction_is_swept_and_stops_at_a_wall() {
        let wall = (Vec3::new(0.8, 0.0, -2.0), Vec3::new(1.0, 3.0, 2.0));
        let end = sweep_horizontal(Vec3::ZERO, Vec3::new(2.0, 0.0, 0.0), &[wall]);
        assert!(end.x <= wall.0.x - RADIUS + 0.001, "{end:?}");
    }

    #[test]
    fn large_push_does_not_tunnel_through_thin_static_geometry() {
        let wall = (Vec3::new(1.0, 0.0, -1.0), Vec3::new(1.05, 3.0, 1.0));
        let end = sweep_horizontal(Vec3::ZERO, Vec3::new(10.0, 0.0, 0.0), &[wall]);
        assert!(end.x <= wall.0.x - RADIUS + 0.001, "{end:?}");
    }

    #[test]
    fn elevator_rides_only_from_the_shaft_and_within_the_storeys() {
        let building = food_factory_bevy::goods::Building { floors: 3, elevator_x: 4, elevator_z: 5, ..Default::default() };
        assert_eq!(elevator_target(&building, (4, 5), 0, 1), Some(1));
        assert_eq!(elevator_target(&building, (4, 5), 2, 1), None);
        assert_eq!(elevator_target(&building, (4, 5), 0, -1), None);
        assert_eq!(elevator_target(&building, (4, 6), 0, 1), None);
        let single = food_factory_bevy::goods::Building { floors: 1, ..building };
        assert_eq!(elevator_target(&single, (4, 5), 0, 1), None);
    }

    #[test]
    fn vehicle_driving_along_x_knocks_a_player_at_its_bumper_sideways() {
        // A car 4.4 m long along X, moving +X at 10 m/s, its front bumper over a player slightly north of its centreline.
        let body = (Vec3::new(-2.2, 0.0, -0.9), Vec3::new(2.2, 1.6, 0.9));
        let old_centre = Vec3::new(-1.0, 0.8, 0.0);
        let player = Vec3::new(2.3, 0.0, 0.3);
        let (_, shove, _) = vehicle_bump(player, 0.1, &[body], &[old_centre], Vec3::ZERO);
        assert!(shove.z > 0.0 && shove.z.abs() > shove.x.abs(), "{shove:?}");
    }

    #[test]
    fn overlapping_vehicle_ejects_and_a_moving_side_collision_builds_shove() {
        let body = (Vec3::new(-1.0, 0.0, -2.0), Vec3::new(1.0, 2.0, 2.0));
        let old_centre = Vec3::new(0.0, 1.0, -1.0);
        let (correction, shove, _) = vehicle_bump(Vec3::ZERO, 0.1, &[body], &[old_centre], Vec3::ZERO);
        assert!(correction.length() > 0.0);
        assert!(shove.length() > 0.0);
        let moved = sweep_horizontal(Vec3::ZERO, correction, &[]);
        assert!(moved.x.abs() >= 1.0 + RADIUS || moved.z.abs() >= 2.0 + RADIUS);
    }

    #[test]
    fn vehicle_bump_ignores_boxes_without_real_horizontal_overlap() {
        let body = (Vec3::new(1.0, 0.0, -1.0), Vec3::new(2.0, 2.0, 1.0));
        let (correction, _, _) = vehicle_bump(Vec3::ZERO, 0.1, &[body], &[], Vec3::ZERO);
        assert_eq!(correction, Vec3::ZERO);
    }
}

/// The avatar's pose to the server at 10 Hz (map metres and absolute height), and the sites it watches once a second.
fn report_pose(time: Res<Time>, mut session: ResMut<Session>, space: Option<Res<Space>>, players: Query<(&Player, &Transform)>, mut pose_timer: Local<f32>, mut watch_timer: Local<f32>, panels: Res<crate::panels::Panels>) {
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
        // The logistics screen shows every company site's stock (Unity LogisticsPanel subscribes to them while open).
        if panels.open == Some(crate::panels::Panel::Phone(crate::panels::PhoneTab::Trucks)) {
            for site in world.company_sites() { if !sites.contains(&site) { sites.push(site); } }
        }
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
