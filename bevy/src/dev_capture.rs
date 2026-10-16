// Verification aid, inactive unless FF_CAPTURE_DIR is set: hosts a singleplayer world (FF_CAPTURE_WORLD, default "capture";
// set FF_DATA_ROOT to keep it out of the player's saves), waits until the city is drawn and the avatar placed, then drives the
// action map through a short script (FF_CAPTURE_SCRIPT, default "look"), saving a screenshot at each step, and exits.
// Script steps are separated by ';': `shot NAME`, `wait SECONDS`, `walk X Y SECONDS`, `turn DEGREES`, `pitch DEGREES`,
// `zoom STEPS`, `topdown`, `key KEY` (one frame of a bound action: inventory, phone, build, place, remove, rotate, close).
use std::collections::VecDeque;

use bevy::prelude::*;
use bevy::render::view::screenshot::{save_to_disk, Screenshot};

use crate::actions::PlayerActions;
use crate::camera::OrbitCamera;
use crate::map::MapState;
use crate::session::{Content, Session};

pub struct DevCapturePlugin;

impl Plugin for DevCapturePlugin {
    fn build(&self, app: &mut App) {
        let Ok(dir) = std::env::var("FF_CAPTURE_DIR") else { return };
        let script = std::env::var("FF_CAPTURE_SCRIPT").unwrap_or_else(|_| "wait 2;shot look;turn 180;wait 0.5;shot behind;topdown;wait 1;shot topdown".into());
        let steps = script.split(';').map(|s| s.trim().to_string()).filter(|s| !s.is_empty()).collect();
        app.insert_resource(Capture { dir: dir.into(), steps, started: false, ready_at: None, busy_until: 0.0, walk: None })
            .add_systems(PreUpdate, autopilot.after(crate::actions::ReadActions));
    }
}

#[derive(Resource)]
struct Capture {
    dir: std::path::PathBuf,
    steps: VecDeque<String>,
    started: bool,
    ready_at: Option<f64>,
    busy_until: f64,
    walk: Option<(Vec2, f64)>,
}

#[allow(clippy::too_many_arguments)]
fn autopilot(
    mut commands: Commands, time: Res<Time>, mut capture: ResMut<Capture>, mut actions: ResMut<PlayerActions>, mut session: ResMut<Session>, content: Res<Content>,
    map: Res<MapState>, mut players: Query<(&Visibility, &mut Transform), With<crate::player::Player>>, indoors: Res<crate::sites::Indoors>, space: Option<Res<crate::space::Space>>, mut rigs: Query<&mut OrbitCamera>, mut exit: MessageWriter<AppExit>, mut intents: ResMut<crate::ui::UiIntents>, mut build: ResMut<crate::build::Build>,
) {
    let now = time.elapsed_secs_f64();
    // The real mouse never steers a capture.
    actions.look = Vec2::ZERO;
    actions.zoom = 0.0;
    actions.movement = Vec2::ZERO;
    if !capture.started {
        capture.started = true;
        let world = std::env::var("FF_CAPTURE_WORLD").unwrap_or_else(|_| "capture".into());
        let seed = std::env::var("FF_CAPTURE_SEED").unwrap_or_default();
        if let Err(problem) = session.host(&content.0, &world, &seed, true, "") {
            error!("[Capture] Could not host: {problem}");
            exit.write(AppExit::error());
        }
        return;
    }
    if now > 600.0 {
        error!("[Capture] Timed out.");
        exit.write(AppExit::error());
        return;
    }
    let placed = map.built && players.single().is_ok_and(|(v, _)| *v != Visibility::Hidden);
    if !placed {
        return;
    }
    let ready = *capture.ready_at.get_or_insert(now);
    if now - ready < 1.5 {
        return;
    }
    if let Some((direction, until)) = capture.walk {
        if now < until {
            actions.movement = direction;
            return;
        }
        capture.walk = None;
    }
    if now < capture.busy_until {
        return;
    }
    let Some(step) = capture.steps.pop_front() else {
        if capture.walk.is_none() && capture.busy_until > 0.0 && now > capture.busy_until + 1.0 {
            exit.write(AppExit::Success);
        } else if capture.busy_until <= 0.0 || now <= capture.busy_until {
            capture.busy_until = now;
        }
        return;
    };
    let parts: Vec<&str> = step.split_whitespace().collect();
    let number = |i: usize| parts.get(i).and_then(|p| p.parse::<f32>().ok()).unwrap_or(0.0);
    match parts.first().copied().unwrap_or("") {
        "shot" => {
            let name = parts.get(1).copied().unwrap_or("shot");
            commands.spawn(Screenshot::primary_window()).observe(save_to_disk(capture.dir.join(format!("{name}.png"))));
            capture.busy_until = now + 0.3;
        }
        "where" => {
            if let (Ok((_, transform)), Some(space)) = (players.single(), space.as_ref()) {
                let (x, z, y) = space.to_map(transform.translation);
                let site = indoors.site.clone().unwrap_or_default();
                let local = space.to_site(&site, transform.translation);
                let yaw = crate::space::yaw_of(transform.rotation * Vec3::NEG_Z);
                if let Some(view) = session.joined().and_then(|w| w.view_of(&site)) {
                    for b in view.buildings.iter().filter(|b| b.site_id == site) {
                        info!("[Capture] building {} at ({}, {}) {}x{} doors {:?} floors {}", b.id, b.cell_x, b.cell_z, b.width, b.depth, b.doors, b.floors);
                    }
                    for e in view.equipment.iter().filter(|e| e.site_id == site && e.placed()) {
                        info!("[Capture] {} {} at ({}, {}) r{}", e.id, e.kind, e.cell_x, e.cell_z, e.rotation);
                    }
                }
                info!("[Capture] map ({x:.1}, {z:.1}) h {y:.2} yaw {yaw:.0}; site {site} at ({:.1}, {:.1}); indoors {:?}", local.0, local.1, *indoors);
            }
        }
        "wait" => capture.busy_until = now + number(1) as f64,
        "walk" => capture.walk = Some((Vec2::new(number(1), number(2)), now + number(3) as f64)),
        "turn" => rigs.iter_mut().for_each(|mut r| r.yaw = (r.yaw + number(1)).rem_euclid(360.0)),
        "tp" => {
            if let (Ok((_, mut transform)), Some(space)) = (players.single_mut(), space.as_ref()) {
                let site = indoors.site.clone().unwrap_or_default();
                transform.translation = space.site_point(&site, number(1), number(2), 0) + Vec3::Y * 0.05;
            }
        }
        "bcell" => build.forced_cell = Some((number(1) as i32, number(2) as i32)),
        "intent" => intents.0.push(parts[1..].join(" ")),
        "hotbar" => actions.hotbar = Some((number(1) as usize).saturating_sub(1)),
        "yaw" => rigs.iter_mut().for_each(|mut r| r.yaw = number(1).rem_euclid(360.0)),
        "pitch" => rigs.iter_mut().for_each(|mut r| r.pitch = number(1)),
        "zoom" => rigs.iter_mut().for_each(|mut r| r.distance = number(1)),
        "topdown" => rigs.iter_mut().for_each(|mut r| r.top_down = !r.top_down),
        "key" => match parts.get(1).copied().unwrap_or("") {
            "inventory" => actions.inventory_pressed = true,
            "phone" => actions.phone_pressed = true,
            "build" => actions.build_pressed = true,
            "place" => actions.place_pressed = true,
            "remove" => actions.remove_pressed = true,
            "rotate" => actions.rotate_pressed = true,
            "close" => actions.close_pressed = true,
            other => warn!("[Capture] Unknown key {other}"),
        },
        other => warn!("[Capture] Unknown step {other}"),
    }
}
