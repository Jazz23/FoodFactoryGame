// Over-the-shoulder orbit camera ported from the Unity OrbitCameraRig (orbit mode only): it follows the avatar's
// position but never its facing, and looking up shortens the arm so the camera never drops through the floor.
use bevy::prelude::*;

use crate::actions::PlayerActions;
use crate::player::{Player, PlayerSet};

pub struct OrbitCameraPlugin;

impl Plugin for OrbitCameraPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Startup, spawn_camera).add_systems(
            Update,
            (
                orbit_input.before(PlayerSet::Move),
                follow.after(PlayerSet::Move),
            ),
        );
    }
}

const FOCUS_HEIGHT: f32 = 1.0;
const SHOULDER_OFFSET: f32 = 1.0;
const ORBIT_DEGREES_PER_PIXEL: f32 = 0.2;
const ZOOM_STEP: f32 = 1.0;
const MIN_PITCH: f32 = -89.0;
const MAX_PITCH: f32 = 89.0;
const FLOOR_CLEARANCE: f32 = 0.3;
const MIN_DISTANCE: f32 = 1.0;
const MAX_DISTANCE: f32 = 20.0;

// Angles in degrees, Unity convention: yaw turns clockwise seen from above, positive pitch looks down.
#[derive(Component)]
pub struct OrbitCamera {
    pub yaw: f32,
    pub pitch: f32,
    pub distance: f32,
}

impl OrbitCamera {
    pub fn yaw_rotation(&self) -> Quat {
        Quat::from_rotation_y(-self.yaw.to_radians())
    }
}

fn spawn_camera(mut commands: Commands) {
    commands.spawn((
        Camera3d::default(),
        OrbitCamera {
            yaw: 0.0,
            pitch: 40.0,
            distance: 9.0,
        },
    ));
}

fn orbit_input(actions: Res<PlayerActions>, mut rigs: Query<&mut OrbitCamera>) {
    for mut rig in &mut rigs {
        rig.yaw = (rig.yaw + actions.look.x * ORBIT_DEGREES_PER_PIXEL).rem_euclid(360.0);
        rig.pitch =
            (rig.pitch + actions.look.y * ORBIT_DEGREES_PER_PIXEL).clamp(MIN_PITCH, MAX_PITCH);
        // Scroll magnitude differs by platform and device, so each nonzero frame is one step.
        if actions.zoom != 0.0 {
            rig.distance -= actions.zoom.signum() * ZOOM_STEP;
        }
        rig.distance = rig.distance.clamp(MIN_DISTANCE, MAX_DISTANCE);
    }
}

fn follow(
    player: Query<&Transform, (With<Player>, Without<OrbitCamera>)>,
    mut rigs: Query<(&OrbitCamera, &mut Transform)>,
) {
    let Ok(target) = player.single() else { return };
    for (rig, mut transform) in &mut rigs {
        let focus = target.translation + Vec3::Y * FOCUS_HEIGHT;
        let shoulder = focus + rig.yaw_rotation() * Vec3::X * SHOULDER_OFFSET;
        let rotation = rig.yaw_rotation() * Quat::from_rotation_x(-rig.pitch.to_radians());
        let distance = if rig.pitch < 0.0 {
            rig.distance
                .min((FOCUS_HEIGHT - FLOOR_CLEARANCE).max(0.0) / (-rig.pitch).to_radians().sin())
        } else {
            rig.distance
        };
        // A Bevy camera looks down its local -Z, so it sits on its local +Z.
        *transform = Transform::from_translation(shoulder + rotation * Vec3::Z * distance)
            .with_rotation(rotation);
    }
}
