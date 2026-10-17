// Verification aid, inactive unless FF_CAPTURE_DIR is set: hosts a singleplayer world (FF_CAPTURE_WORLD, default "capture";
// set FF_DATA_ROOT to keep it out of the player's saves), waits until the city is drawn and the avatar placed, then drives the
// action map through a short script (FF_CAPTURE_SCRIPT, default "look"), saving a screenshot at each step, and exits.
// Script steps are separated by ';': `shot NAME`, `wait SECONDS`, `walk X Y SECONDS`, `turn DEGREES`, `pitch DEGREES`, `competitor [near]`, `ui TEXT` / `uialt` / `uishift` (click a button by label), `uidrag A|B`, `dump`, `property CATEGORY`, `gotoprop [LOT]`, `elevator`,
// `zoom STEPS`, `topdown`, `key KEY` (one frame of a bound action: inventory, phone, map, build, place, remove, rotate, close).
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
        app.insert_resource(Capture { dir: dir.into(), steps, started: false, ready_at: None, busy_until: 0.0, walk: None, lot: None })
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
    /// The lot chosen by `property`, for `gotoprop`.
    lot: Option<String>,
}

#[allow(clippy::too_many_arguments)]
fn autopilot(
    mut commands: Commands, time: Res<Time>, mut capture: ResMut<Capture>, mut actions: ResMut<PlayerActions>, mut session: ResMut<Session>, content: Res<Content>,
    map: Res<MapState>, mut players: Query<(&Visibility, &mut Transform), With<crate::player::Player>>, indoors: Res<crate::sites::Indoors>, space: Option<Res<crate::space::Space>>, mut rigs: Query<&mut OrbitCamera>, mut exit: MessageWriter<AppExit>, mut intents: ResMut<crate::ui::UiIntents>, mut build: ResMut<crate::build::Build>,
    model: Res<crate::ui::UiModel>,
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
        "junction" => {
            if let (Some(world), Some(space), Ok((_, mut transform))) = (session.joined(), space.as_ref(), players.single_mut()) {
                let (x, z, _) = space.to_map(transform.translation);
                let here = Vec2::new(x, z);
                if let Some(node) = world.roads.nodes.iter().filter(|n| n.control == food_factory_bevy::world::layout::JunctionControl::TrafficLight)
                    .min_by(|a, b| Vec2::new(a.x as f32, a.z as f32).distance_squared(here).total_cmp(&Vec2::new(b.x as f32, b.z as f32).distance_squared(here))) {
                    let x = node.x as f32 + 12.0; let z = node.z as f32 + 12.0;
                    transform.translation = space.to_scene(x, z, space.land(x, z));
                    info!("[Capture] junction {} at {}, {}", node.id, node.x, node.z);
                }
            }
        }
        // Clicks the first on-screen button whose label contains the text (`uialt` right-clicks it, `uishift` Shift+clicks).
        "ui" | "uialt" | "uishift" => {
            let text = parts[1..].join(" ").to_lowercase();
            fn find<'a>(entries: &'a [crate::ui::UiEntry], text: &str) -> Option<&'a crate::ui::UiEntry> {
                entries.iter().find_map(|e| if e.action.is_some() && e.label.to_lowercase().contains(text) { Some(e) } else { find(&e.row, text) })
            }
            match find(&model.entries, &text) {
                Some(entry) => {
                    let action = entry.action.clone().unwrap_or_default();
                    let action = match parts[0] { "uialt" => format!("{action}|alt"), "uishift" => format!("{action}|shift"), _ => action };
                    info!("[Capture] clicked '{}' ({action})", entry.label.replace('\n', " "));
                    intents.0.push(action);
                }
                None => warn!("[Capture] no button labelled '{text}'"),
            }
        }
        // Drags the first button labelled A onto the first labelled B (`uidrag A|B`; `#N` picks the Nth match, from 0).
        "uidrag" => {
            let text = parts[1..].join(" ").to_lowercase();
            fn all<'a>(entries: &'a [crate::ui::UiEntry], text: &str, out: &mut Vec<&'a crate::ui::UiEntry>) {
                for e in entries { if e.action.is_some() && e.label.to_lowercase().contains(text) { out.push(e); } all(&e.row, text, out); }
            }
            let pick = |spec: &str| {
                let (label, index) = spec.rsplit_once('#').map_or((spec, 0), |(l, i)| (l, i.parse().unwrap_or(0)));
                let mut found = vec![];
                all(&model.entries, label.trim(), &mut found);
                found.get(index).and_then(|e| e.action.clone())
            };
            if let Some((a, b)) = text.split_once('|') {
                match (pick(a), pick(b)) {
                    (Some(source), Some(target)) => { info!("[Capture] dragged {source} onto {target}"); intents.0.push(format!("drag>{source}>{target}")); }
                    _ => warn!("[Capture] no buttons for drag '{text}'"),
                }
            }
        }
        // Logs the open screen's text and buttons.
        "dump" => {
            fn walk(entries: &[crate::ui::UiEntry], out: &mut Vec<String>) {
                for e in entries {
                    if !e.label.is_empty() { out.push(format!("{}{}", e.label.replace('\n', " "), e.action.as_ref().map_or(String::new(), |a| format!(" [{a}]")))); }
                    walk(&e.row, out);
                }
            }
            let mut out = vec![model.heading.clone(), model.status.replace('\n', " | "), model.footer.replace('\n', " | ")];
            walk(&model.entries, &mut out);
            info!("[Capture] screen: {}", out.join(" ; "));
        }
        // Opens the property screen of the nearest for-sale lot of a category (restaurant, factory, farm, station).
        "property" => {
            if let (Some(world), Some(space), Ok((_, transform))) = (session.joined(), space.as_ref(), players.single()) {
                let here = transform.translation;
                let category = parts.get(1).copied().unwrap_or("factory");
                let nearest = world.offers_by_lot.values().filter(|o| o.for_sale && o.category == category)
                    .min_by(|a, b| space.site_point(&a.site_id, 0.0, 0.0, 0).distance(here).total_cmp(&space.site_point(&b.site_id, 0.0, 0.0, 0).distance(here)));
                if let Some(offer) = nearest {
                    info!("[Capture] property {} ({})", offer.lot_id, offer.site_id);
                    capture.lot = Some(offer.lot_id.clone());
                    intents.0.push(format!("p:property|{}", offer.lot_id));
                }
            }
        }
        // Into the `property` lot's building, one cell inside its first door.
        "gotoprop" => {
            let lot = parts.get(1).map(|l| l.to_string()).or_else(|| capture.lot.clone());
            if let (Some(world), Some(space), Ok((_, mut transform)), Some(lot)) = (session.joined(), space.as_ref(), players.single_mut(), lot.as_ref()) {
                if let Some(offer) = world.offers_by_lot.get(lot) {
                    if let Some(door) = offer.doors.first() {
                        let centre = (offer.building_x as f32 + offer.building_width as f32 * 0.5, offer.building_z as f32 + offer.building_depth as f32 * 0.5);
                        let cell = (door.x as f32 + 0.5, door.z as f32 + 0.5);
                        let inward = Vec2::new(centre.0 - cell.0, centre.1 - cell.1).normalize_or_zero() * 1.5;
                        transform.translation = space.site_point(&offer.site_id, cell.0 + inward.x, cell.1 + inward.y, 0) + Vec3::Y * 0.05;
                    }
                }
            }
        }
        // Onto the elevator cell of the building the avatar is in, on its current storey.
        "elevator" => {
            if let (Some(world), Some(space), Ok((_, mut transform))) = (session.joined(), space.as_ref(), players.single_mut()) {
                let site = indoors.site.clone().unwrap_or_default();
                let building = indoors.building.as_ref().and_then(|id| world.view_of(&site).and_then(|v| v.buildings.iter().find(|b| &b.id == id)));
                match building {
                    Some(b) if b.has_elevator() => {
                        transform.translation = space.site_point(&site, b.elevator_x as f32 + 0.5, b.elevator_z as f32 + 0.5, indoors.level) + Vec3::Y * 0.05;
                        info!("[Capture] elevator of {} at ({}, {}), {} floors", b.id, b.elevator_x, b.elevator_z, b.floors);
                    }
                    _ => warn!("[Capture] no elevator here"),
                }
            }
        }
        "competitor" => {
            // To the kerb in front of the nearest competitor's door, facing it.
            if let (Some(world), Some(space), Ok((_, mut transform))) = (session.joined(), space.as_ref(), players.single_mut()) {
                let here = transform.translation;
                // The competitor with the most customers arriving or queued, then the nearest.
                let nearest = world.crowd.restaurants.iter().filter_map(|r| world.offers_by_lot.get(&r.lot_id).map(|o| (r.customers.iter().filter(|c| matches!(c.state, food_factory_bevy::goods::CustomerState::Travelling | food_factory_bevy::goods::CustomerState::Queued)).count(), o)))
                    .filter_map(|(n, o)| food_factory_bevy::site_walk::Frontage::new(o).map(|f| (n, o.site_id.clone(), f)))
                    .max_by(|a, b| a.0.cmp(&b.0).then(space.site_point(&b.1, b.2.kerb.0, b.2.kerb.1, 0).distance(here).total_cmp(&space.site_point(&a.1, a.2.kerb.0, a.2.kerb.1, 0).distance(here))))
                    .map(|(_, site, f)| (site, f));
                if let Some((site, f)) = nearest {
                    let stand = if parts.get(1) == Some(&"near") { f.apron } else { (f.kerb.0 + f.outward.0 * 3.0, f.kerb.1 + f.outward.1 * 3.0) };
                    transform.translation = space.site_point(&site, stand.0, stand.1, 0) + Vec3::Y * 0.05;
                    let toward = space.site_point(&site, f.door.0, f.door.1, 0) - transform.translation;
                    rigs.iter_mut().for_each(|mut r| r.yaw = crate::space::yaw_of(toward));
                    let customers: Vec<String> = world.crowd.restaurants.iter().map(|r| format!("{}:{:?}", r.lot_id, r.customers.iter().map(|c| (c.state, c.remaining_seconds)).collect::<Vec<_>>())).collect();
                    info!("[Capture] competitor {site}; crowd {customers:?}");
                } else {
                    info!("[Capture] no competitor frontage: {} crowd restaurants, {} offers", world.crowd.restaurants.len(), world.offers_by_lot.len());
                }
            }
        }
        "hire-editor" => {
            session.command(food_factory_bevy::protocol::Command::Hire, now);
        }
        "employee-editor" => {
            if let Some(employee) = session.joined().and_then(|w| w.employees.first()) {
                intents.0.push(format!("p:employee|{}", employee.id));
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
        "zoom" => rigs.iter_mut().for_each(|mut r| {
            if r.build_focus.is_some() { r.build_distance = number(1); }
            else if r.top_down { r.top_distance = number(1); }
            else { r.distance = number(1); }
        }),
        "topdown" => rigs.iter_mut().for_each(|mut r| r.top_down = !r.top_down),
        "key" => match parts.get(1).copied().unwrap_or("") {
            "inventory" => actions.inventory_pressed = true,
            "phone" => actions.phone_pressed = true,
            "map" => actions.map_pressed = true,
            "build" => actions.build_pressed = true,
            "place" => actions.place_pressed = true,
            "remove" => actions.remove_pressed = true,
            "rotate" => actions.rotate_pressed = true,
            "floorup" => actions.floor_up_pressed = true,
            "floordown" => actions.floor_down_pressed = true,
            "close" => actions.close_pressed = true,
            other => warn!("[Capture] Unknown key {other}"),
        },
        other => warn!("[Capture] Unknown step {other}"),
    }
}
