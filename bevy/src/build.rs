// Restaurant build mode (Unity BuildMode and BuildCatalog), local player only. B opens it on the current site: the camera
// frames the lot from above, the pointer is free and the catalog lists the Sell tool, walls, doors, back doors, windows and
// wall finishes in every style, the supplier's furnishings and machines, and belts. The pointer's cell shows a ghost tinted by
// the same pure rules the server applies (plan_shell, furnish_problem), with the charge or the reason it would be refused. A
// click places one piece, door or window; dragging draws a line of wall, restyles a line or fills an area with a piece, ordered
// when the button is released; Esc during a drag drops it. Holding right click still on something for half a second removes
// it (sells a charged piece, picks up an uncharged one, takes up a belt, removes a wall, door or window), and keeping it held
// sweeps every further thing of the same family; moving the pointer while holding tilts and turns the camera instead. Move
// pans, Zoom zooms, R turns the piece, PgUp/PgDn change floor, middle click picks what is under the pointer, X clears the
// tool, B or Esc leaves. Every order is a request; the next baseline shows what the server accepted.
use bevy::input::mouse::AccumulatedMouseMotion;
use bevy::prelude::*;
use bevy::window::PrimaryWindow;
use bevy::world_serialization::WorldAssetRoot;

use food_factory_bevy::content::OfferKind;
use food_factory_bevy::goods::{self, buildings, buildings::order_kind, buildings::ShellOrder, equipment::FurnishOrder, equipment::GridPlacement, GridCell};
use food_factory_bevy::protocol::Command;

use crate::actions::{PlayerActions, UiFocus};
use crate::camera::OrbitCamera;
use crate::interaction::Cursor;
use crate::models::{fitted, Bounds, Fit};
use crate::panels::{Panel, Panels};
use crate::screens::{reason_text, Screen};
use crate::session::{money, Content, Session, SessionEvent};
use crate::sites::{EditedSite, SiteMaterials};
use crate::space::{grid_rotation, Space, LEVEL_HEIGHT};
use crate::ui::{UiEntry, UiIntents, UiModel};

pub struct BuildPlugin;

impl Plugin for BuildPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<Build>().add_systems(Update, (enter_leave, catalog_intents, pointer, orders, ghost, draw_catalog).chain().after(crate::player::PlayerSet::Move));
    }
}

const HOLD_SECONDS: f32 = 0.5;
const TILT_PIXELS: f32 = 6.0;
const DEGREES_PER_PIXEL: f32 = 0.25;

#[derive(Clone, Debug, PartialEq, Default)]
pub enum Tool {
    #[default]
    None,
    Sell,
    Wall(String),
    Door(String),
    BackDoor(String),
    Window(String),
    Finish(String),
    Item(String),
}

#[derive(Resource, Default)]
pub struct Build {
    pub active: Option<String>,
    pub tool: Tool,
    pub level: i32,
    pub rotation: i32,
    pub filter: String,
    /// The pointer's cell on the build level.
    cell: Option<(i32, i32)>,
    drag_start: Option<(i32, i32)>,
    remove_press: Option<(Vec2, f32, bool)>,
    sweeping: Option<bool>,
    swept: Vec<(i32, i32)>,
    /// What the ghost would order now and the planner's verdict.
    pub preview: String,
    problem: String,
    /// Verification aid (dev_capture): the pointer's cell, instead of the real pointer.
    pub forced_cell: Option<(i32, i32)>,
}

fn enter_leave(
    mut build: ResMut<Build>, mut panels: ResMut<Panels>, actions: Res<PlayerActions>, session: Res<Session>, space: Option<Res<Space>>, mut rigs: Query<&mut OrbitCamera>,
    mut edited: ResMut<EditedSite>, mut focus: ResMut<UiFocus>, screen: Res<Screen>,
) {
    let open = panels.open == Some(Panel::Build);
    match (&build.active, open) {
        (None, true) => {
            let (Some(world), Some(space)) = (session.joined(), space) else {
                panels.open = None;
                return;
            };
            let Some(lot) = space.lots.get(&world.site) else {
                panels.open = None;
                return;
            };
            let centre = space.site_point(&world.site, lot.width as f32 * 0.5, lot.depth as f32 * 0.5, 0);
            for mut rig in &mut rigs {
                rig.build_focus = Some(centre);
                rig.build_distance = (lot.width.max(lot.depth) as f32 * 1.1).clamp(12.0, 80.0);
                rig.build_pitch = 62.0;
            }
            build.active = Some(world.site.clone());
            build.level = 0;
            build.drag_start = None;
            edited.0 = Some(world.site.clone());
        }
        (Some(_), true) => {
            let leave = *screen != Screen::Playing || session.joined().is_none() || actions.build_pressed || (actions.close_pressed && build.drag_start.is_none() && build.tool == Tool::None);
            if actions.close_pressed && build.drag_start.is_some() {
                build.drag_start = None;
            } else if actions.close_pressed && build.tool != Tool::None {
                build.tool = Tool::None;
            } else if leave {
                panels.open = None;
            }
        }
        (Some(_), false) => {
            build.active = None;
            edited.0 = None;
            for mut rig in &mut rigs {
                rig.build_focus = None;
            }
        }
        (None, false) => {}
    }
    if build.active.is_some() && !focus.0 {
        focus.0 = true;
    }
}

fn catalog_intents(mut intents: ResMut<UiIntents>, mut build: ResMut<Build>, mut panels: ResMut<Panels>, mut cursor: ResMut<Cursor>, mut session: ResMut<Session>, time: Res<Time>, content: Res<Content>) {
    let actions: Vec<String> = intents.0.iter().filter(|a| a.starts_with("b:")).cloned().collect();
    intents.0.retain(|a| !a.starts_with("b:"));
    for action in actions {
        let parts: Vec<&str> = action[2..].splitn(2, '|').collect();
        build.drag_start = None;
        match parts.as_slice() {
            ["filter", filter] => build.filter = filter.to_string(),
            ["sell"] => build.tool = Tool::Sell,
            ["none"] => build.tool = Tool::None,
            ["wall", style] => build.tool = Tool::Wall(style.to_string()),
            ["door", style] => build.tool = Tool::Door(style.to_string()),
            ["backdoor", style] => build.tool = Tool::BackDoor(style.to_string()),
            ["window", style] => build.tool = Tool::Window(style.to_string()),
            ["finish", style] => build.tool = Tool::Finish(style.to_string()),
            ["item", offer] => build.tool = Tool::Item(offer.to_string()),
            ["belts"] => {
                // Leaves build mode with belts on the cursor, buying a pack when none are carried.
                let has = session.joined().is_some_and(|w| crate::interaction::inventory_lot(w, goods::BELT_ITEM_ID).is_some());
                if !has {
                    if let Some(world) = session.joined() {
                        let site = world.site.clone();
                        if let Some(offer) = content.0.offers.iter().find(|o| matches!(&o.kind, OfferKind::Goods(g) if g.item_id == goods::BELT_ITEM_ID)) {
                            let offer = offer.id.clone();
                            session.command(Command::Buy { site, offer }, time.elapsed_secs_f64());
                        }
                    }
                }
                cursor.machine = None;
                cursor.item = Some(goods::BELT_ITEM_ID.to_string());
                panels.open = None;
            }
            ["floor-up"] => build.level += 1,
            ["floor-down"] => build.level = (build.level - 1).max(0),
            ["add-floor"] => {
                let Some(world) = session.joined() else { continue };
                let Some(building) = world.view().and_then(|v| v.buildings.iter().find(|b| b.site_id == world.site)) else { continue };
                // The elevator stands in the first interior cell of the building.
                let (x, z) = goods::grid::interior_cells(building).into_iter().min().unwrap_or((building.cell_x + 1, building.cell_z + 1));
                let (x, z) = if building.has_elevator() { (building.elevator_x, building.elevator_z) } else { (x, z) };
                let id = building.id.clone();
                session.command(Command::AddFloor { building: id, x, z }, time.elapsed_secs_f64());
            }
            ["leave"] => panels.open = None,
            _ => {}
        }
    }
}

/// The build level's floor cell under the free pointer.
#[allow(clippy::too_many_arguments)]
fn pointer(mut build: ResMut<Build>, space: Option<Res<Space>>, cameras: Query<(&Camera, &GlobalTransform)>, window: Query<&Window, With<PrimaryWindow>>, actions: Res<PlayerActions>, motion: Res<AccumulatedMouseMotion>, mut rigs: Query<&mut OrbitCamera>, hover: Res<bevy::picking::hover::HoverMap>, nodes: Query<(), With<Node>>) {
    let (Some(site), Some(space)) = (build.active.clone(), space) else { return };
    if actions.floor_up_pressed {
        build.level += 1;
    }
    if actions.floor_down_pressed {
        build.level = (build.level - 1).max(0);
    }
    if actions.rotate_pressed {
        build.rotation = (build.rotation + 1) & 3;
    }
    if actions.clear_pressed {
        build.tool = Tool::None;
        build.drag_start = None;
    }
    // Holding right click and moving tilts and turns the camera.
    if let Some((_, _, tilting)) = build.remove_press {
        if tilting {
            for mut rig in &mut rigs {
                rig.yaw = (rig.yaw + motion.delta.x * DEGREES_PER_PIXEL).rem_euclid(360.0);
                rig.build_pitch = (rig.build_pitch + motion.delta.y * DEGREES_PER_PIXEL).clamp(30.0, 90.0);
            }
        }
    }
    if let Some(cell) = build.forced_cell {
        build.cell = Some(cell);
        return;
    }
    let over_ui = hover.0.values().any(|hits| hits.keys().any(|e| nodes.contains(*e)));
    let (Ok(window), Some((camera, transform))) = (window.single(), cameras.iter().next()) else { return };
    let Some(pointer) = window.cursor_position().filter(|_| !over_ui) else {
        build.cell = None;
        return;
    };
    let Ok(ray) = camera.viewport_to_world(transform, pointer) else { return };
    let floor = space.floor_height(&site, build.level);
    let direction = *ray.direction;
    if direction.y > -1e-4 {
        build.cell = None;
        return;
    }
    let t = (floor - ray.origin.y) / direction.y;
    let (x, z) = space.to_site(&site, ray.origin + direction * t);
    build.cell = Some((x.floor() as i32, z.floor() as i32));
}

/// The cells of a straight line from a to b along its longer axis.
fn line(a: (i32, i32), b: (i32, i32)) -> Vec<(i32, i32)> {
    if (b.0 - a.0).abs() >= (b.1 - a.1).abs() {
        let (from, to) = (a.0.min(b.0), a.0.max(b.0));
        (from..=to).map(|x| (x, a.1)).collect()
    } else {
        let (from, to) = (a.1.min(b.1), a.1.max(b.1));
        (from..=to).map(|z| (a.0, z)).collect()
    }
}

/// What a press would order with the current tool from `start` to `end`, for preview and sending.
fn order_for(build: &Build, world: &crate::session::Joined, content: &Content, start: (i32, i32), end: (i32, i32)) -> Option<(Command, String, Option<&'static str>)> {
    let view = world.view()?;
    let building = view.buildings.iter().find(|b| b.site_id == world.site);
    let percent = world.offers.get(&world.site).map_or(100, |o| o.price_percent);
    let shell = |kind: &str, x: i32, z: i32, axis: i32, style: &str, cells: Vec<(i32, i32)>| -> Option<(Command, String, Option<&'static str>)> {
        let b = building?;
        let order = ShellOrder { kind: kind.into(), building_id: b.id.clone(), x, z, width: 0, depth: 0, axis, style: style.into(), cells: cells.into_iter().map(|(x, z)| GridCell { x, z }).collect() };
        let plan = buildings::plan_shell(view, &order, percent);
        let total = plan.net_cents() + if plan.problem.is_none() { buildings::SHELL_ORDER_FEE_CENTS * percent as i64 / 100 } else { 0 };
        let problem = plan.problem.or_else(|| (total > world.cash()).then_some("not-enough-cash"));
        let label = if plan.refund_cents > 0 { format!("{} (refund {})", money(total), money(plan.refund_cents)) } else { money(total) };
        Some((Command::OrderShell { order }, label, problem))
    };
    match &build.tool {
        Tool::Wall(style) => shell(order_kind::PARTITION, 0, 0, 0, style, line(start, end)),
        Tool::Finish(style) => shell(order_kind::WALL_FINISH, 0, 0, 0, style, line(start, end)),
        Tool::Door(style) => shell(order_kind::DOOR, end.0, end.1, 0, style, vec![]),
        Tool::BackDoor(style) => shell(order_kind::BACK_DOOR, end.0, end.1, 0, style, vec![]),
        Tool::Window(style) => {
            let b = building?;
            let axis = if goods::grid::is_wall(b, end.0 + 1, end.1, 0) || goods::grid::is_wall(b, end.0 - 1, end.1, 0) { 0 } else { 1 };
            shell(order_kind::WINDOW, end.0, end.1, axis, style, vec![])
        }
        Tool::Item(offer_id) => {
            let offer = content.0.offer(offer_id)?;
            let OfferKind::Equipment(equipment) = &offer.kind else { return None };
            let template = &equipment.equipment;
            let (w, d) = goods::grid::footprint(template.width, template.depth, build.rotation);
            let (x0, x1) = (start.0.min(end.0), start.0.max(end.0));
            let (z0, z1) = (start.1.min(end.1), start.1.max(end.1));
            let mut placements = vec![];
            let mut z = z0;
            while z <= z1 {
                let mut x = x0;
                while x <= x1 {
                    placements.push(GridPlacement { x, z, rotation: build.rotation });
                    x += w.max(1);
                }
                z += d.max(1);
            }
            let cents = offer.price_cents * placements.len() as i64;
            let problem = goods::equipment::furnish_problem(view, &world.site, template, &placements, build.level, world.offers.get(&world.site)).or_else(|| (cents > world.cash()).then_some("not-enough-cash"));
            let order = FurnishOrder { site_id: world.site.clone(), offer_id: offer_id.clone(), level: build.level, placements };
            Some((Command::BuyAndPlace { order }, money(cents), problem))
        }
        Tool::None | Tool::Sell => None,
    }
}

/// What right click would remove at a cell: a placed piece (decor layers first), a belt, or a structure.
fn removal_at(world: &crate::session::Joined, level: i32, cell: (i32, i32)) -> Option<(Command, bool)> {
    let view = world.view()?;
    let mut pieces: Vec<&goods::Equipment> = view.equipment.iter().filter(|e| e.site_id == world.site && e.placed() && e.level == level && goods::grid::contains(e, cell.0, cell.1, 1, 1)).collect();
    pieces.sort_by_key(|e| e.layer.is_empty());
    if let Some(piece) = pieces.first() {
        let command = if piece.charged_cents > 0 { Command::Sell { equipment: piece.id.clone() } } else { Command::PickUp { equipment: piece.id.clone() } };
        return Some((command, true));
    }
    if let Some(belt) = view.belts.iter().find(|b| b.site_id == world.site && (b.level == level || b.exit_level() == level) && b.cell_x == cell.0 && b.cell_z == cell.1) {
        return Some((Command::RemoveBelt { belt: belt.id.clone() }, true));
    }
    let building = view.buildings.iter().find(|b| b.site_id == world.site)?;
    let structure = goods::grid::is_partition(building, cell.0, cell.1) || goods::grid::is_door(building, cell.0, cell.1) || goods::grid::window_at(building, cell.0, cell.1).is_some();
    structure.then(|| (Command::OrderShell { order: ShellOrder { kind: order_kind::REMOVE.into(), building_id: building.id.clone(), cells: vec![GridCell { x: cell.0, z: cell.1 }], ..default() } }, false))
}

#[allow(clippy::too_many_arguments)]
fn orders(mut build: ResMut<Build>, actions: Res<PlayerActions>, mut session: ResMut<Session>, content: Res<Content>, time: Res<Time>, window: Query<&Window, With<PrimaryWindow>>, mut events: MessageReader<SessionEvent>, hover: Res<bevy::picking::hover::HoverMap>, nodes: Query<(), With<Node>>) {
    for event in events.read() {
        if let SessionEvent::Outcome { outcome, .. } = event {
            if build.active.is_some() {
                build.problem = if outcome.accepted { String::new() } else { reason_text(&outcome.reason) };
            }
        }
    }
    if build.active.is_none() {
        return;
    }
    let now = time.elapsed_secs_f64();
    let pointer = window.single().ok().and_then(|w| w.cursor_position()).unwrap_or_default();
    let over_ui = hover.0.values().any(|hits| hits.keys().any(|e| nodes.contains(*e)));
    let Some(world) = session.joined() else { return };
    let mut sends = vec![];
    let cell = build.cell;
    // Left: start, then order on release.
    if actions.place_pressed && !over_ui {
        if let (Some(cell), true) = (cell, build.tool != Tool::None && build.tool != Tool::Sell) {
            build.drag_start = Some(cell);
        }
    }
    if !actions.place_held {
        if let (Some(start), Some(end)) = (build.drag_start.take(), cell) {
            if let Some((command, _, problem)) = order_for(&build, world, &content, start, end) {
                match problem {
                    Some(problem) => build.problem = reason_text(problem),
                    None => {
                        build.problem.clear();
                        sends.push(command);
                    }
                }
            }
        }
    }
    // Middle click picks what is under the pointer.
    if actions.pick_block_pressed {
        if let (Some(cell), Some(view)) = (cell, world.view()) {
            if let Some(piece) = view.equipment.iter().find(|e| e.site_id == world.site && e.placed() && e.level == build.level && goods::grid::contains(e, cell.0, cell.1, 1, 1)) {
                if let Some(offer) = content.0.offers.iter().find(|o| matches!(&o.kind, OfferKind::Equipment(e) if e.equipment.kind == piece.kind)) {
                    build.tool = Tool::Item(offer.id.clone());
                    build.rotation = piece.rotation;
                }
            } else if let Some(b) = view.buildings.iter().find(|b| b.site_id == world.site) {
                if let Some(window) = goods::grid::window_at(b, cell.0, cell.1) {
                    build.tool = Tool::Window(window.style.clone());
                } else if goods::grid::is_door(b, cell.0, cell.1) {
                    let style = b.structures.iter().find(|s| s.kind == goods::DOOR_STRUCTURE && s.x == cell.0 && s.z == cell.1).map(|s| s.style.clone()).unwrap_or_else(|| "panel".into());
                    build.tool = Tool::Door(style);
                } else if let Some(wall) = b.structures.iter().find(|s| s.kind == goods::PARTITION_STRUCTURE && s.x == cell.0 && s.z == cell.1) {
                    build.tool = Tool::Wall(if wall.style.is_empty() { b.wall_style.clone() } else { wall.style.clone() });
                }
            }
        }
    }
    // Right: hold still to remove, move to tilt; once removing, sweep the same family.
    let mut command = None;
    if actions.remove_pressed && !over_ui {
        build.remove_press = Some((pointer, 0.0, false));
        build.sweeping = None;
        build.swept.clear();
    }
    if actions.remove_held {
        if let Some((start, held, tilting)) = build.remove_press {
            let tilting = tilting || (build.sweeping.is_none() && pointer.distance(start) > TILT_PIXELS);
            let held = held + time.delta_secs();
            build.remove_press = Some((start, held, tilting));
            if let (Some(cell), false) = (cell, tilting) {
                match build.sweeping {
                    None if held >= HOLD_SECONDS => {
                        if let Some((c, piece)) = removal_at(world, build.level, cell) {
                            build.sweeping = Some(piece);
                            build.swept.push(cell);
                            command = Some(c);
                        }
                    }
                    Some(family) if !build.swept.contains(&cell) => {
                        if let Some((c, piece)) = removal_at(world, build.level, cell).filter(|(_, p)| *p == family) {
                            build.swept.push(cell);
                            command = Some(c);
                        }
                    }
                    _ => {}
                }
            }
        }
    } else {
        build.remove_press = None;
        build.sweeping = None;
    }
    // Preview line.
    build.preview = match cell {
        Some(end) => match order_for(&build, world, &content, build.drag_start.unwrap_or(end), end) {
            Some((_, cost, None)) => format!("Cell {}, {}: {cost}", end.0, end.1),
            Some((_, cost, Some(problem))) => format!("Cell {}, {}: {cost} - {}", end.0, end.1, reason_text(problem)),
            None => format!("Cell {}, {}", end.0, end.1),
        },
        None => String::new(),
    };
    sends.extend(command);
    for command in sends {
        session.command(command, now);
    }
}

#[derive(Component)]
struct BuildGhost;

#[allow(clippy::too_many_arguments)]
fn ghost(
    mut commands: Commands, build: Res<Build>, session: Res<Session>, content: Res<Content>, space: Option<Res<Space>>, m: Option<Res<SiteMaterials>>, assets: Res<AssetServer>, bounds: Res<Bounds>,
    ghosts: Query<Entity, With<BuildGhost>>, mut last: Local<String>, mut tints: Local<Option<(Handle<StandardMaterial>, Handle<StandardMaterial>)>>, mut materials: ResMut<Assets<StandardMaterial>>,
) {
    let world = session.joined();
    let key = format!("{:?}{:?}{:?}{}{}{}", build.tool, build.cell, build.drag_start, build.rotation, build.level, world.and_then(|w| w.view()).map_or(0, |v| v.revision));
    if *last == key {
        return;
    }
    *last = key;
    for entity in &ghosts {
        commands.entity(entity).despawn();
    }
    let (Some(world), Some(space), Some(m), Some(site), Some(end)) = (world, space, m, build.active.clone(), build.cell) else { return };
    let (valid, invalid) = tints.get_or_insert_with(|| {
        let tint = |c: Color| StandardMaterial { base_color: c, alpha_mode: AlphaMode::Blend, unlit: true, ..default() };
        (materials.add(tint(Color::srgba(0.2, 0.85, 0.3, 0.45))), materials.add(tint(Color::srgba(0.9, 0.2, 0.15, 0.45))))
    }).clone();
    let start = build.drag_start.unwrap_or(end);
    let Some((_, _, problem)) = order_for(&build, world, &content, start, end) else {
        // No tool: outline the cell under the pointer.
        let centre = space.site_point(&site, end.0 as f32 + 0.5, end.1 as f32 + 0.5, build.level) + Vec3::Y * 0.04;
        commands.spawn((BuildGhost, Mesh3d(m.cube.clone()), MeshMaterial3d(valid), Transform::from_translation(centre).with_scale(Vec3::new(0.96, 0.02, 0.96))));
        return;
    };
    let material = if problem.is_none() { valid } else { invalid };
    let floor = build.level as f32 * LEVEL_HEIGHT;
    match &build.tool {
        Tool::Item(offer_id) => {
            let Some(OfferKind::Equipment(e)) = content.0.offer(offer_id).map(|o| o.kind.clone()) else { return };
            let kind = content.0.equipment.get(&e.equipment.kind);
            let (w, d) = goods::grid::footprint(e.equipment.width, e.equipment.depth, build.rotation);
            let (x0, x1) = (start.0.min(end.0), start.0.max(end.0));
            let (z0, z1) = (start.1.min(end.1), start.1.max(end.1));
            let mut count = 0;
            let mut z = z0;
            while z <= z1 && count < 60 {
                let mut x = x0;
                while x <= x1 && count < 60 {
                    let centre = space.site_point(&site, x as f32 + w as f32 * 0.5, z as f32 + d as f32 * 0.5, build.level);
                    let root = commands.spawn((BuildGhost, Transform::from_translation(centre).with_rotation(grid_rotation(build.rotation)), Visibility::default())).id();
                    let pad = commands.spawn((Mesh3d(m.cube.clone()), MeshMaterial3d(material.clone()), Transform::from_xyz(0.0, 0.03, 0.0).with_scale(Vec3::new(e.equipment.width as f32 - 0.04, 0.02, e.equipment.depth as f32 - 0.04)))).id();
                    commands.entity(root).add_child(pad);
                    if let Some(kind) = kind {
                        let model = commands.spawn((WorldAssetRoot(assets.load(GltfAssetLabel::Scene(0).from_asset(kind.model.clone()))), fitted(&bounds, &kind.model, Fit::Natural))).id();
                        commands.entity(root).add_child(model);
                    }
                    x += w.max(1);
                    count += 1;
                }
                z += d.max(1);
            }
        }
        Tool::Wall(_) | Tool::Finish(_) => {
            for (x, z) in line(start, end) {
                let centre = space.site_point(&site, x as f32 + 0.5, z as f32 + 0.5, build.level) + Vec3::Y * (LEVEL_HEIGHT * 0.5);
                commands.spawn((BuildGhost, Mesh3d(m.cube.clone()), MeshMaterial3d(material.clone()), Transform::from_translation(centre).with_scale(Vec3::new(0.9, LEVEL_HEIGHT, 0.9))));
            }
        }
        _ => {
            let centre = space.site_point(&site, end.0 as f32 + 0.5, end.1 as f32 + 0.5, build.level) + Vec3::Y * (DOOR_GHOST_HEIGHT * 0.5);
            commands.spawn((BuildGhost, Mesh3d(m.cube.clone()), MeshMaterial3d(material), Transform::from_translation(centre).with_scale(Vec3::new(1.0, DOOR_GHOST_HEIGHT, 1.0))));
        }
    }
    let _ = floor;
}

const DOOR_GHOST_HEIGHT: f32 = 2.2;

fn category(content: &Content, kind: &str) -> &'static str {
    match content.0.equipment.get(kind).map(|k| k.category.as_str()).unwrap_or("") {
        "kitchen" => "Kitchen",
        "storage" => "Storage",
        "sales" => "Sales",
        "furniture" => "Furniture",
        "decor" => "Decor",
        "logistics" => "Logistics",
        _ => match kind {
            "oven" | "stove" | "grill" | "fryer" | "mixer" | "kitchen-counter" | "rt-prep-table" | "rt-sink" => "Kitchen",
            "fridge" | "rt-shelf" | "rt-trolley" => "Storage",
            "counter" | "table" => "Sales",
            "dock" => "Logistics",
            k if k.starts_with("rt-chair") || k.starts_with("rt-table") || k == "rt-bench" || k == "rt-booth" => "Furniture",
            _ => "Decor",
        },
    }
}

fn draw_catalog(build: Res<Build>, session: Res<Session>, content: Res<Content>, mut model: ResMut<UiModel>, mut icons: ResMut<crate::structure_icons::StructureIcons>, mut images: ResMut<Assets<Image>>) {
    if build.active.is_none() {
        return;
    }
    let Some(world) = session.joined() else { return };
    let styles = &content.0.styles;
    let filter = if build.filter.is_empty() { "All" } else { build.filter.as_str() };
    let mut next = UiModel { open: true, wide: false, right: true, heading: "Build".into(), ..default() };
    let floors = world.view().and_then(|v| v.buildings.iter().find(|b| b.site_id == world.site)).map_or(1, |b| b.floors);
    next.status = format!("Cash {}   Floor {} of {}", money(world.cash()), build.level + 1, floors);
    next.entries.push(UiEntry::row("filters", ["All", "Structure", "Kitchen", "Storage", "Sales", "Furniture", "Decor", "Logistics"].iter().map(|f| UiEntry::button(format!("b:filter|{f}"), *f).selected(*f == filter)).collect()));
    next.entries.push(UiEntry::row("tools", vec![
        UiEntry::button("b:sell", "Sell (hold right click)").selected(build.tool == Tool::Sell),
        UiEntry::button("b:floor-down", "Floor down"),
        UiEntry::button("b:floor-up", "Floor up"),
        UiEntry::button("b:add-floor", "Add a floor"),
        UiEntry::button("b:leave", "Done"),
    ]));
    let mut icon = |tool: &str, style: &str| {
        let pieces = crate::structure_icons::StructureIcons::pieces(styles, tool, style);
        icons.icon(&mut images, &format!("{tool}:{style}"), pieces)
    };
    if filter == "All" || filter == "Structure" {
        let walls: Vec<UiEntry> = styles.walls.iter().map(|w| UiEntry::button(format!("b:wall|{}", w.style), format!("Wall: {}\n{} / m", w.display_name, money(buildings::SHELL_WALL_CELL_CENTS))).with_icon_image(icon("wall", &w.style)).selected(build.tool == Tool::Wall(w.style.clone()))).collect();
        next.entries.push(UiEntry::row("walls", walls));
        let mut doors: Vec<UiEntry> = styles.doors.iter().map(|d| UiEntry::button(format!("b:door|{}", d.style), format!("Door: {}\n{}", d.display_name, money(buildings::SHELL_DOOR_CENTS))).with_icon_image(icon("door", &d.style)).selected(build.tool == Tool::Door(d.style.clone()))).collect();
        doors.extend(styles.doors.iter().take(1).map(|d| UiEntry::button(format!("b:backdoor|{}", d.style), format!("Back door\n{}", money(buildings::SHELL_DOOR_CENTS))).with_icon_image(icon("backdoor", &d.style)).selected(build.tool == Tool::BackDoor(d.style.clone()))));
        next.entries.push(UiEntry::row("doors", doors));
        next.entries.push(UiEntry::row("windows", styles.windows.iter().map(|w| UiEntry::button(format!("b:window|{}", w.style), format!("Window: {}\n{}", w.display_name, money(buildings::SHELL_WINDOW_CENTS))).with_icon_image(icon("window", &w.style)).selected(build.tool == Tool::Window(w.style.clone()))).collect()));
        next.entries.push(UiEntry::row("finishes", styles.walls.iter().map(|w| UiEntry::button(format!("b:finish|{}", w.style), format!("Finish: {}", w.display_name)).with_icon_image(icon("finish", &w.style)).selected(build.tool == Tool::Finish(w.style.clone()))).collect()));
    }
    let mut items: Vec<UiEntry> = vec![];
    for offer in &content.0.offers {
        let OfferKind::Equipment(e) = &offer.kind else { continue };
        let section = category(&content, &e.equipment.kind);
        if filter != "All" && filter != section {
            continue;
        }
        let kind = content.0.equipment.get(&e.equipment.kind);
        items.push(UiEntry::button(format!("b:item|{}", offer.id), format!("{}\n{}", content.0.equipment_name(&e.equipment.kind), money(offer.price_cents))).with_icon(kind.map(|k| k.icon.clone()).unwrap_or_default()).selected(build.tool == Tool::Item(offer.id.clone())));
    }
    if filter == "All" || filter == "Logistics" {
        items.push(UiEntry::button("b:belts", "Belts\nlay a line"));
    }
    next.entries.push(UiEntry::row("items", items));
    next.footer = if !build.problem.is_empty() { build.problem.clone() } else if !build.preview.is_empty() { build.preview.clone() } else { "Left click places, drag draws or fills, hold right click removes, R turns, B leaves".into() };
    if *model != next {
        *model = next;
    }
}
