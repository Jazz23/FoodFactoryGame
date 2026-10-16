// Verification aid, inactive unless FF_CAPTURE_DIR is set: drives the action map through idle, sprint and a jump,
// saves a screenshot at each phase into that directory, then exits.
use bevy::prelude::*;
use bevy::render::view::screenshot::{Screenshot, save_to_disk};

use crate::actions::PlayerActions;

pub struct DevCapturePlugin;

impl Plugin for DevCapturePlugin {
    fn build(&self, app: &mut App) {
        let Ok(dir) = std::env::var("FF_CAPTURE_DIR") else {
            return;
        };
        app.insert_resource(CaptureDir(dir.into()))
            .add_systems(PreUpdate, autopilot.after(crate::actions::ReadActions));
    }
}

#[derive(Resource)]
struct CaptureDir(std::path::PathBuf);

// (seconds since startup, screenshot name)
const SHOTS: [(f32, &str); 5] = [
    (3.0, "idle"),
    (4.2, "walk"),
    (5.6, "run"),
    (5.95, "jump"),
    (6.45, "fall"),
];

fn autopilot(
    mut commands: Commands,
    time: Res<Time>,
    dir: Res<CaptureDir>,
    mut actions: ResMut<PlayerActions>,
    mut next_shot: Local<usize>,
    mut exit: MessageWriter<AppExit>,
) {
    let t = time.elapsed_secs();
    let previous = t - time.delta_secs();
    if t > 3.6 {
        actions.movement = Vec2::new(0.4, 1.0).normalize() * if t < 4.4 { 0.45 } else { 1.0 };
        actions.sprint = t >= 4.4;
    }
    actions.jump_pressed = previous < 5.7 && t >= 5.7;
    if let Some((at, name)) = SHOTS.get(*next_shot)
        && t >= *at
    {
        commands
            .spawn(Screenshot::primary_window())
            .observe(save_to_disk(dir.0.join(format!("{name}.png"))));
        *next_shot += 1;
    }
    if t > 7.5 {
        exit.write(AppExit::Success);
    }
}
