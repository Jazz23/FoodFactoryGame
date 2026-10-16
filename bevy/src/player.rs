// Player avatar ported from the Unity PlayerAvatar: camera-relative walk/sprint, jump and gravity, turning toward
// the move direction. The floor is the plane y = 0, so a kinematic controller stands in for CharacterController.
use bevy::prelude::*;

use crate::actions::PlayerActions;
use crate::animation::CharacterModel;
use crate::camera::OrbitCamera;

pub struct PlayerPlugin;

impl Plugin for PlayerPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Startup, spawn_player)
            .add_systems(Update, move_player.in_set(PlayerSet::Move));
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

#[derive(Component, Default)]
pub struct Player {
    vertical_speed: f32,
    grounded: bool,
}

fn spawn_player(mut commands: Commands, assets: Res<AssetServer>) {
    commands
        .spawn((
            Player {
                grounded: true,
                ..default()
            },
            Transform::default(),
            Visibility::default(),
        ))
        .with_child((
            CharacterModel::new(assets.load("models/player.glb")),
            // The exported model faces +Z; a Bevy transform's forward is -Z (the Unity prefab turns its model child likewise).
            Transform::from_rotation(Quat::from_rotation_y(std::f32::consts::PI)),
        ));
}

fn move_player(
    time: Res<Time>,
    actions: Res<PlayerActions>,
    rig: Query<&OrbitCamera>,
    mut players: Query<(&mut Player, &mut Transform)>,
) {
    let delta = time.delta_secs();
    let yaw = rig
        .single()
        .map(OrbitCamera::yaw_rotation)
        .unwrap_or_default();
    let direction = yaw * Vec3::new(actions.movement.x, 0.0, -actions.movement.y);
    for (mut player, mut transform) in &mut players {
        player.vertical_speed = if player.grounded {
            if actions.jump_pressed {
                (-2.0 * GRAVITY * JUMP_HEIGHT).sqrt()
            } else {
                GROUNDED_PUSH
            }
        } else {
            player.vertical_speed + GRAVITY * delta
        };
        let speed = if actions.sprint { SPRINT_SPEED } else { SPEED };
        transform.translation += (direction * speed + Vec3::Y * player.vertical_speed) * delta;
        player.grounded = transform.translation.y <= 0.0;
        if player.grounded {
            transform.translation.y = 0.0;
        }
        if direction.length_squared() > 0.0001 {
            let target = Transform::default().looking_to(direction, Vec3::Y).rotation;
            transform.rotation = transform
                .rotation
                .rotate_towards(target, TURN_DEGREES_PER_SECOND.to_radians() * delta);
        }
    }
}
