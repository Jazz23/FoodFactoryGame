// Action map: gameplay reads named actions from `PlayerActions`, never raw devices. Bindings mirror the Unity
// project's Player map (WASD/arrows move, Space jump, Left Shift sprint, mouse delta look, scroll zoom).
use bevy::input::mouse::{AccumulatedMouseMotion, AccumulatedMouseScroll};
use bevy::prelude::*;
use bevy::window::{CursorGrabMode, CursorOptions, PrimaryWindow};

pub struct ActionsPlugin;

impl Plugin for ActionsPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<Bindings>()
            .init_resource::<PlayerActions>()
            .add_systems(
                Startup,
                |cursor: Query<&mut CursorOptions, With<PrimaryWindow>>| {
                    set_cursor_captured(cursor, true)
                },
            )
            .add_systems(
                PreUpdate,
                (capture_cursor, read_actions.in_set(ReadActions))
                    .chain()
                    .after(bevy::input::InputSystems),
            );
    }
}

// Systems that adjust actions after they are read (such as dev automation) order after this set.
#[derive(SystemSet, Debug, Clone, PartialEq, Eq, Hash)]
pub struct ReadActions;

#[derive(Resource)]
pub struct Bindings {
    pub up: Vec<KeyCode>,
    pub down: Vec<KeyCode>,
    pub left: Vec<KeyCode>,
    pub right: Vec<KeyCode>,
    pub jump: Vec<KeyCode>,
    pub sprint: Vec<KeyCode>,
    pub release_cursor: Vec<KeyCode>,
    pub capture_cursor: MouseButton,
}

impl Default for Bindings {
    fn default() -> Self {
        Self {
            up: vec![KeyCode::KeyW, KeyCode::ArrowUp],
            down: vec![KeyCode::KeyS, KeyCode::ArrowDown],
            left: vec![KeyCode::KeyA, KeyCode::ArrowLeft],
            right: vec![KeyCode::KeyD, KeyCode::ArrowRight],
            jump: vec![KeyCode::Space],
            sprint: vec![KeyCode::ShiftLeft],
            release_cursor: vec![KeyCode::Escape],
            capture_cursor: MouseButton::Left,
        }
    }
}

// This frame's action values. Look and zoom are zero while the cursor is free, so the camera only turns when captured.
#[derive(Resource, Default)]
pub struct PlayerActions {
    // Ground move input, x right and y forward, clamped to length 1.
    pub movement: Vec2,
    pub jump_pressed: bool,
    pub sprint: bool,
    // Pointer delta in pixels (x right, y down).
    pub look: Vec2,
    // Scroll steps this frame, positive away from the user.
    pub zoom: f32,
}

fn capture_cursor(
    bindings: Res<Bindings>,
    keys: Res<ButtonInput<KeyCode>>,
    mouse: Res<ButtonInput<MouseButton>>,
    cursor: Query<&mut CursorOptions, With<PrimaryWindow>>,
) {
    if keys.any_just_pressed(bindings.release_cursor.iter().copied()) {
        set_cursor_captured(cursor, false);
    } else if mouse.just_pressed(bindings.capture_cursor) {
        set_cursor_captured(cursor, true);
    }
}

fn set_cursor_captured(mut cursor: Query<&mut CursorOptions, With<PrimaryWindow>>, captured: bool) {
    let Ok(mut cursor) = cursor.single_mut() else {
        return;
    };
    cursor.grab_mode = if captured {
        CursorGrabMode::Locked
    } else {
        CursorGrabMode::None
    };
    cursor.visible = !captured;
}

fn read_actions(
    bindings: Res<Bindings>,
    keys: Res<ButtonInput<KeyCode>>,
    motion: Res<AccumulatedMouseMotion>,
    scroll: Res<AccumulatedMouseScroll>,
    cursor: Query<&CursorOptions, With<PrimaryWindow>>,
    mut actions: ResMut<PlayerActions>,
) {
    let axis = |positive: &[KeyCode], negative: &[KeyCode]| {
        let held = |codes: &[KeyCode]| keys.any_pressed(codes.iter().copied()) as i32 as f32;
        held(positive) - held(negative)
    };
    let captured = cursor
        .single()
        .is_ok_and(|cursor| cursor.grab_mode != CursorGrabMode::None);
    *actions = PlayerActions {
        movement: Vec2::new(
            axis(&bindings.right, &bindings.left),
            axis(&bindings.up, &bindings.down),
        )
        .clamp_length_max(1.0),
        jump_pressed: keys.any_just_pressed(bindings.jump.iter().copied()),
        sprint: keys.any_pressed(bindings.sprint.iter().copied()),
        look: if captured { motion.delta } else { Vec2::ZERO },
        zoom: if captured { scroll.delta.y } else { 0.0 },
    };
}
