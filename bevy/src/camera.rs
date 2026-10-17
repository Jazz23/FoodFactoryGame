// Over-the-shoulder orbit camera (Unity OrbitCameraRig): it follows the avatar's position but never its facing; Look orbits
// unless a screen is open; looking up shortens the arm so the camera never drops through the floor. The arm never passes
// through a wall: a ray from the pivot pulls the camera in front of the first collision box at least 1.5 m tall, and indoors
// it stays under the room's ceiling. The top-down view (straight down, yaw snapped to the grid) is build mode's: while a build
// focus is set the camera frames that point from the build distance, Move pans it, Zoom changes the distance and holding the
// right button turns Look into tilt and turn.
use bevy::prelude::*;

use crate::actions::{PlayerActions, UiFocus};
use crate::player::{Player, PlayerSet};
use crate::sites::{Indoors, Solids};

pub struct OrbitCameraPlugin;

impl Plugin for OrbitCameraPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Startup, spawn_camera).add_systems(Update, (orbit_input.before(PlayerSet::Move), follow.after(PlayerSet::Move)));
    }
}

const FOCUS_HEIGHT: f32 = 1.0;
const SHOULDER_OFFSET: f32 = 0.6;
const ORBIT_DEGREES_PER_PIXEL: f32 = 0.2;
const ZOOM_STEP: f32 = 1.0;
const MIN_PITCH: f32 = -89.0;
const MAX_PITCH: f32 = 89.0;
const FLOOR_CLEARANCE: f32 = 0.3;
const MIN_DISTANCE: f32 = 1.0;
const MAX_DISTANCE: f32 = 20.0;
const WALL_HEIGHT: f32 = 1.5;
const MINIMUM_ARM: f32 = 0.4;
const CEILING_GAP: f32 = 0.25;
const CAST_RADIUS: f32 = 0.2;
const MIN_BUILD_DISTANCE: f32 = 4.0;
const MAX_BUILD_DISTANCE: f32 = 90.0;
const MIN_BUILD_PITCH: f32 = 30.0;
const BUILD_PAN_SPEED: f32 = 12.0;
const BUILD_SPRINT_FACTOR: f32 = 2.5;

// Angles in degrees, Unity convention: yaw turns clockwise seen from above, positive pitch looks down.
#[derive(Component)]
pub struct OrbitCamera {
    pub yaw: f32,
    pub pitch: f32,
    pub distance: f32,
    pub top_down: bool,
    pub top_distance: f32,
    pub blend: f32,
    /// Build mode: the framed point (scene), its distance and pitch.
    pub build_focus: Option<Vec3>,
    pub build_distance: f32,
    pub build_pitch: f32,
    /// The arm the walls allowed last frame.
    pub arm: f32,
}

impl OrbitCamera {
    pub fn yaw_rotation(&self) -> Quat { Quat::from_rotation_y(-self.yaw.to_radians()) }
}

fn spawn_camera(mut commands: Commands) {
    commands.spawn((
        Camera3d::default(),
        Projection::Perspective(PerspectiveProjection { far: 2500.0, ..default() }),
        DistanceFog { color: Color::srgba(0.66, 0.76, 0.86, 1.0), falloff: FogFalloff::Linear { start: 350.0, end: 1400.0 }, ..default() },
        OrbitCamera { yaw: 0.0, pitch: 25.0, distance: 6.0, top_down: false, top_distance: 12.0, blend: 0.0, build_focus: None, build_distance: 30.0, build_pitch: 60.0, arm: 6.0 },
    ));
}

fn orbit_input(actions: Res<PlayerActions>, focus: Res<UiFocus>, time: Res<Time>, mut rigs: Query<&mut OrbitCamera>) {
    for mut rig in &mut rigs {
        let target = if rig.top_down { 1.0 } else { 0.0 };
        rig.blend = rig.blend.move_towards(target, time.delta_secs() / 0.4);
        if let Some(mut focus_point) = rig.build_focus {
            let delta = time.delta_secs();
            if actions.remove_held {
                rig.yaw = (rig.yaw + actions.look.x * ORBIT_DEGREES_PER_PIXEL).rem_euclid(360.0);
                rig.build_pitch = (rig.build_pitch + actions.look.y * ORBIT_DEGREES_PER_PIXEL).clamp(MIN_BUILD_PITCH, 90.0);
            }
            let speed = BUILD_PAN_SPEED * if actions.sprint { BUILD_SPRINT_FACTOR } else { 1.0 } * (rig.build_distance / 30.0).max(0.5);
            let pan = rig.yaw_rotation() * Vec3::new(actions.movement.x, 0.0, -actions.movement.y) * speed * delta;
            focus_point += pan;
            rig.build_focus = Some(focus_point);
            if actions.zoom != 0.0 {
                rig.build_distance = (rig.build_distance - actions.zoom.signum() * ZOOM_STEP * 3.0).clamp(MIN_BUILD_DISTANCE, MAX_BUILD_DISTANCE);
            }
            continue;
        }
        if focus.0 {
            continue;
        }
        if rig.top_down {
            if actions.zoom != 0.0 {
                rig.top_distance = (rig.top_distance - actions.zoom.signum() * ZOOM_STEP).clamp(3.0, MAX_DISTANCE);
            }
            continue;
        }
        rig.yaw = (rig.yaw + actions.look.x * ORBIT_DEGREES_PER_PIXEL).rem_euclid(360.0);
        rig.pitch = (rig.pitch + actions.look.y * ORBIT_DEGREES_PER_PIXEL).clamp(MIN_PITCH, MAX_PITCH);
        // Scroll magnitude differs by platform and device, so each nonzero frame is one step; zooming in from behind a wall
        // starts at the arm the wall allows.
        if actions.zoom != 0.0 {
            if actions.zoom > 0.0 && rig.distance > rig.arm + 0.01 {
                rig.distance = rig.arm;
            }
            rig.distance -= actions.zoom.signum() * ZOOM_STEP;
        }
        rig.distance = rig.distance.clamp(MIN_DISTANCE, MAX_DISTANCE);
    }
}

/// Distance along a ray to the first tall box, if within `length`.
fn wall_hit(origin: Vec3, direction: Vec3, length: f32, solids: &Solids) -> Option<f32> {
    let mut best: Option<f32> = None;
    for (min, max) in &solids.boxes {
        if max.y - min.y < WALL_HEIGHT {
            continue;
        }
        let (min, max) = (*min - Vec3::splat(CAST_RADIUS), *max + Vec3::splat(CAST_RADIUS));
        // The cast ignores boxes it starts inside.
        if origin.cmpge(min).all() && origin.cmple(max).all() {
            continue;
        }
        let mut near = 0.0f32;
        let mut far = length;
        let mut hit = true;
        for axis in 0..3 {
            let d = direction[axis];
            if d.abs() < 1e-6 {
                if origin[axis] < min[axis] || origin[axis] > max[axis] {
                    hit = false;
                    break;
                }
                continue;
            }
            let (mut a, mut b) = ((min[axis] - origin[axis]) / d, (max[axis] - origin[axis]) / d);
            if a > b {
                std::mem::swap(&mut a, &mut b);
            }
            near = near.max(a);
            far = far.min(b);
            if near > far {
                hit = false;
                break;
            }
        }
        if hit && best.is_none_or(|b| near < b) {
            best = Some(near);
        }
    }
    best
}

fn follow(player: Query<&Transform, (With<Player>, Without<OrbitCamera>)>, mut rigs: Query<(&mut OrbitCamera, &mut Transform)>, solids: Res<Solids>, indoors: Res<Indoors>) {
    let Ok(target) = player.single() else { return };
    for (mut rig, mut transform) in &mut rigs {
        if let Some(focus) = rig.build_focus {
            let rotation = rig.yaw_rotation() * Quat::from_rotation_x(-rig.build_pitch.to_radians());
            *transform = Transform::from_translation(focus + rotation * Vec3::Z * rig.build_distance).with_rotation(rotation);
            continue;
        }
        let focus = target.translation + Vec3::Y * FOCUS_HEIGHT;
        let blend = rig.blend * rig.blend * (3.0 - 2.0 * rig.blend);
        // The shoulder offset stops short of a wall beside the avatar, so the arm never starts inside one (a cast ignores
        // boxes it starts in, and a camera inside a wall sees through it).
        let side = rig.yaw_rotation() * Vec3::X;
        let mut offset = SHOULDER_OFFSET * (1.0 - blend);
        if let Some(hit) = wall_hit(focus, side, offset + CAST_RADIUS, &solids) {
            offset = offset.min((hit - CAST_RADIUS).max(0.0));
        }
        let shoulder = focus + side * offset;
        let pitch = rig.pitch + (90.0 - rig.pitch) * blend;
        let rotation = rig.yaw_rotation() * Quat::from_rotation_x(-pitch.to_radians());
        let mut distance = if rig.pitch < 0.0 { rig.distance.min((FOCUS_HEIGHT - FLOOR_CLEARANCE).max(0.0) / (-rig.pitch).to_radians().sin()) } else { rig.distance };
        distance += (rig.top_distance - distance) * blend;
        // A Bevy camera looks down its local -Z, so it sits on its local +Z.
        let back = rotation * Vec3::Z;
        if blend < 0.5 {
            if let Some(hit) = wall_hit(shoulder, back, distance, &solids) {
                distance = (hit - CAST_RADIUS).max(MINIMUM_ARM).min(distance);
            }
            if let Some(ceiling) = indoors.ceiling {
                let room = ceiling - CEILING_GAP - shoulder.y;
                if back.y > 1e-4 && distance * back.y > room {
                    distance = (room / back.y).max(MINIMUM_ARM);
                }
            }
        }
        rig.arm = distance;
        *transform = Transform::from_translation(shoulder + back * distance).with_rotation(rotation);
    }
}
