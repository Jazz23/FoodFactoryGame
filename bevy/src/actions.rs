// Action map: gameplay reads named actions from `PlayerActions`, never raw devices. Bindings mirror the Unity
// project's Player map (WASD/arrows move, Space jump, Left Shift sprint, mouse delta look, scroll zoom).
use bevy::input::mouse::{AccumulatedMouseMotion, AccumulatedMouseScroll};
use bevy::prelude::*;
use bevy::window::{CursorGrabMode, CursorOptions, PrimaryWindow};

pub struct ActionsPlugin;

impl Plugin for ActionsPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<Bindings>()
            .init_resource::<UiFocus>()
            .init_resource::<UiTyping>()
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

#[derive(Resource, Default)]
pub struct UiFocus(pub bool);
#[derive(Resource, Default)]
pub struct UiTyping(pub bool);

#[derive(Resource)]
pub struct Bindings {
    pub inventory: KeyCode,
    pub camera: KeyCode,
    pub rotate: KeyCode,
    pub clear: KeyCode,
    pub phone: KeyCode,
    pub build: KeyCode,
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
            inventory: KeyCode::KeyE,
            camera: KeyCode::KeyC,
            rotate: KeyCode::KeyR,
            clear: KeyCode::KeyQ,
            phone: KeyCode::KeyP,
            build: KeyCode::KeyB,
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
    pub inventory_pressed: bool,
    pub camera_pressed: bool,
    pub rotate_pressed: bool,
    pub clear_pressed: bool,
    pub phone_pressed: bool,
    pub build_pressed: bool,
    pub close_pressed: bool,
    pub place_pressed: bool,
    pub remove_pressed: bool,
    pub quick_transfer: bool,
    pub hotbar: Option<usize>,
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
    focus: Res<UiFocus>,
    bindings: Res<Bindings>,
    keys: Res<ButtonInput<KeyCode>>,
    mouse: Res<ButtonInput<MouseButton>>,
    cursor: Query<&mut CursorOptions, With<PrimaryWindow>>,
) {
    if focus.0 || keys.any_just_pressed(bindings.release_cursor.iter().copied()) {
        set_cursor_captured(cursor, false);
    } else if mouse.just_pressed(bindings.capture_cursor) {
        set_cursor_captured(cursor, true);
    } else if focus.is_changed() {
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
    focus: Res<UiFocus>,
    typing: Res<UiTyping>,
    bindings: Res<Bindings>,
    keys: Res<ButtonInput<KeyCode>>,
    mouse: Res<ButtonInput<MouseButton>>,
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
        inventory_pressed: keys.just_pressed(bindings.inventory),
        camera_pressed: keys.just_pressed(bindings.camera),
        rotate_pressed: keys.just_pressed(bindings.rotate),
        clear_pressed: keys.just_pressed(bindings.clear),
        phone_pressed: keys.just_pressed(bindings.phone),
        build_pressed: keys.just_pressed(bindings.build),
        close_pressed: keys.any_just_pressed(bindings.release_cursor.iter().copied()),
        place_pressed: mouse.just_pressed(MouseButton::Left),
        remove_pressed: mouse.just_pressed(MouseButton::Right),
        quick_transfer: keys.any_pressed(bindings.sprint.iter().copied()),
        hotbar: [KeyCode::Digit1, KeyCode::Digit2, KeyCode::Digit3, KeyCode::Digit4,
            KeyCode::Digit5, KeyCode::Digit6, KeyCode::Digit7, KeyCode::Digit8, KeyCode::Digit9]
            .iter().position(|key| keys.just_pressed(*key)),
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
    if focus.0 {
        actions.movement = Vec2::ZERO;
        actions.jump_pressed = false;
        actions.look = Vec2::ZERO;
        actions.place_pressed = false;
        actions.remove_pressed = false;
    }
    if typing.0 {
        let close_pressed = actions.close_pressed;
        *actions = PlayerActions { close_pressed, ..default() };
    }
}
