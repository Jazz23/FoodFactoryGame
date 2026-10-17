// The local player's belt controls (Unity EquipmentInteraction.Belts), Factorio-style. With belts on the cursor a ghost belt
// shows the aimed cell in the cursor direction; pressing Place starts a drag that lays belts forward along that direction as
// the aim moves on (never sideways or back), filling every cell it skips; holding R during a drag turns the line at its head toward
// the aim and carries on from there, and R without a drag turns the cursor direction (or, with an empty cursor, the aimed
// belt). Clicking Remove on a belt (belts or lifts on the cursor) takes it up; holding it takes it up after a short hold, then
// every belt the aim passes over for the rest of the press. With lifts on the cursor a ghost lift shows the aimed cell going up or down one floor; Place
// puts one there, V flips it. With other goods on the cursor Z puts one unit on the aimed belt; F takes the riding item nearest
// the aim off the belt. Build mode uses this same line planner with its free pointer and selected floor, keeping its camera.
// Pending placements reserve inventory and show connected ghosts. Each belt is its own request; the server checks every one.
use bevy::prelude::*;

use food_factory_bevy::goods::{self, rules};
use food_factory_bevy::protocol::Command;

use crate::actions::PlayerActions;
use crate::interaction::{inventory_lot, Aim, Cursor};
use crate::models::Bounds;
use crate::panels::{Panel, Panels};
use crate::build::{Build, Tool};
use crate::screens::Screen;
use crate::session::{Content, Session};
use crate::sites::{belt_model, Pick, SiteMaterials};
use crate::space::{grid_rotation, Space};

pub struct BeltControlsPlugin;

impl Plugin for BeltControlsPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<BeltDrag>().add_systems(Update, (belt_input, belt_ghost).chain().after(crate::build::BuildSet).after(crate::interaction::InteractionSet));
    }
}

/// Seconds Remove is held on a belt before it is taken up.
const HOLD_SECONDS: f32 = 0.5;

#[derive(Resource, Default)]
struct BeltDrag {
    /// The site, level, head cell and direction of the line being laid.
    head: Option<(String, i32, (i32, i32), i32)>,
    remove_held: f32,
    sweeping: bool,
    removed: Vec<String>,
}

fn aimed_cell(aim: &Aim) -> Option<(String, i32, (i32, i32))> {
    let (site, level, point) = aim.floor.as_ref()?;
    Some((site.clone(), *level, (point.x.floor() as i32, point.y.floor() as i32)))
}

#[allow(clippy::too_many_arguments)]
fn belt_input(
    mut build: ResMut<Build>, space: Option<Res<Space>>, content: Res<Content>,
    time: Res<Time>, actions: Res<PlayerActions>, mut session: ResMut<Session>, aim: Res<Aim>, mut cursor: ResMut<Cursor>, mut drag: ResMut<BeltDrag>, panels: Res<Panels>, screen: Res<Screen>,
) {
    let building = build.active.is_some() && panels.open == Some(Panel::Build);
    build.belt_dragging = false;
    if *screen != Screen::Playing || (panels.open.is_some() && !(building && build.tool == Tool::Belts)) {
        *drag = BeltDrag::default();
        return;
    }
    let Some(world) = session.joined() else { return };
    let now = time.elapsed_secs_f64();
    let item = if building { Some(goods::BELT_ITEM_ID.to_string()) } else { cursor.item.clone() };
    let cell = if building { build.active.clone().zip(build.cell).map(|(site, cell)| (site, build.level, cell)) } else { aimed_cell(&aim) }.filter(|c| c.0 == world.site);
    let place_pressed = if building { actions.pointer_place_pressed } else { actions.place_pressed };
    let mut rotation = if building { build.rotation } else { cursor.rotation };
    if build.cancel_belt_drag || actions.clear_pressed || !actions.place_held || drag.head.as_ref().is_some_and(|h| cell.as_ref().is_some_and(|c| h.0 != c.0 || h.1 != c.1)) { drag.head = None; }
    let conveyor = matches!(item.as_deref(), Some(goods::BELT_ITEM_ID) | Some(goods::LIFT_ITEM_ID));
    let aimed_belt = match &aim.pick {
        Some(Pick::Belt(id)) => world.views.values().find_map(|v| v.belts.iter().find(|b| &b.id == id).cloned()),
        _ => None,
    };
    let mut commands: Vec<Command> = vec![];

    // F takes the riding item nearest the aim off the aimed belt, whatever the cursor holds.
    if actions.take_item_pressed {
        if let (Some(belt), Some((_, _, point))) = (&aimed_belt, aim.floor.as_ref()) {
            let view = world.views.values().find(|v| v.belts.iter().any(|b| b.id == belt.id));
            let location = belt.location_id();
            let centre = Vec2::new(belt.cell_x as f32 + 0.5, belt.cell_z as f32 + 0.5);
            let nearest = view.and_then(|v| v.lots.iter().filter(|l| l.location_id == location).min_by(|a, b| {
                let along = |l: &goods::Lot| (l.belt_position as f32 / rules::UNITS_PER_TILE as f32 - 0.5) * 1.0;
                let step = rules::belt_step(belt.direction);
                let pa = centre + Vec2::new(step.0 as f32, step.1 as f32) * along(a);
                let pb = centre + Vec2::new(step.0 as f32, step.1 as f32) * along(b);
                pa.distance(*point).total_cmp(&pb.distance(*point))
            }));
            if let Some(lot) = nearest {
                commands.push(Command::TakeFromBelt { lot: lot.id.clone() });
            }
        }
    }
    // Z puts one unit of the cursor goods on the aimed belt.
    if actions.place_item_pressed && !conveyor {
        if let (Some(item), Some(belt)) = (&item, &aimed_belt) {
            if let Some(lot) = inventory_lot(world, item) {
                commands.push(Command::PlaceOnBelt { lot: lot.id.clone(), belt: belt.id.clone() });
            }
        }
    }
    // R with an empty cursor turns the aimed belt or lift.
    if actions.rotate_pressed && cursor.empty() && !building {
        if let Some(belt) = &aimed_belt {
            let direction = rules::right_of(belt.direction);
            commands.push(if belt.lift == 0 {
                Command::PlaceBelt { site: belt.site_id.clone(), x: belt.cell_x, z: belt.cell_z, direction, level: belt.level }
            } else {
                Command::PlaceLift { site: belt.site_id.clone(), x: belt.cell_x, z: belt.cell_z, direction, level: belt.level, lift: belt.lift }
            });
        }
    }
    // Holding Remove with conveyors on the cursor takes belts up.
    if conveyor && !building && actions.remove_held {
        if let Some(belt) = &aimed_belt {
            if drag.sweeping {
                if !drag.removed.contains(&belt.id) {
                    drag.removed.push(belt.id.clone());
                    commands.push(Command::RemoveBelt { belt: belt.id.clone() });
                }
            } else {
                drag.remove_held += time.delta_secs();
                if drag.remove_held >= HOLD_SECONDS {
                    drag.sweeping = true;
                    drag.removed.push(belt.id.clone());
                    commands.push(Command::RemoveBelt { belt: belt.id.clone() });
                }
            }
        }
    } else {
        // A short Remove click on a belt takes it up on release.
        if conveyor && !building && !actions.remove_held && drag.remove_held > 0.0 && !drag.sweeping {
            if let Some(belt) = &aimed_belt {
                commands.push(Command::RemoveBelt { belt: belt.id.clone() });
            }
        }
        drag.remove_held = 0.0;
        drag.sweeping = false;
        drag.removed.clear();
    }

    match item.as_deref() {
        Some(goods::LIFT_ITEM_ID) => {
            if actions.rotate_pressed {
                cursor.rotation = (cursor.rotation + 1) & 3;
            }
            if actions.place_pressed {
                if let Some((site, level, (x, z))) = aimed_cell(&aim).filter(|c| c.0 == world.site) {
                    let lift = if cursor.lift_down { -1 } else { 1 };
                    commands.push(Command::PlaceLift { site, x, z, direction: cursor.rotation, level, lift });
                }
            }
        }
        Some(goods::BELT_ITEM_ID) => {
            if let Some((site, level, aimed)) = cell {
                if let Some(lot) = space.as_ref().and_then(|s| s.lots.get(&site)) {
                    let occupied = world.view().is_some_and(|v| v.belts.iter().any(|b| b.site_id == site && (b.level == level || b.exit_level() == level) && (b.cell_x, b.cell_z) == aimed))
                        || session.pending.values().any(|p| matches!(&p.command, Command::PlaceBelt { site: s, x, z, level: l, .. } if s == &site && *l == level && (*x, *z) == aimed));
                    commands.extend(drag_line(&mut drag.head, &site, level, aimed, &mut rotation,
                        place_pressed && !build.cancel_belt_drag, actions.place_held,
                        actions.rotate_pressed, actions.rotate_held, occupied, (lot.width, lot.depth)));
                }
            }
            if building {
                build.rotation = rotation;
                build.belt_dragging = drag.head.is_some();
            } else { cursor.rotation = rotation; }
        }        _ => {}
    }
    // Build mode pays for belts like any other build item: a pack is bought ahead of the belt that needs it.
    let pack = if building { content.0.goods_offer_for(goods::BELT_ITEM_ID).cloned() } else { None };
    for command in commands {
        let mut buy = None;
        if let Command::PlaceBelt { site, x, z, direction, level } = &command {
            let Some(world) = session.joined() else { continue };
            let Some(view) = world.view_of(site) else { continue };
            let pending = session.pending.values().filter_map(|p| match &p.command {
                Command::PlaceBelt { site: s, x: px, z: pz, direction, level: l } if s == site && l == level && px == x && pz == z => Some((p.sent, *direction)),
                _ => None,
            }).max_by(|a, b| a.0.total_cmp(&b.0));
            let existing = view.belts.iter().find(|b| b.site_id == *site && b.level == *level && b.lift == 0 && b.cell_x == *x && b.cell_z == *z);
            if pending.map(|p| p.1).or_else(|| existing.map(|b| b.direction)) == Some(*direction) { continue; }
            if pending.is_none() && existing.is_none() {
                let held: i32 = view.lots.iter().filter(|l| l.location_id == world.inventory() && l.item_id == goods::BELT_ITEM_ID).map(|l| l.quantity).sum();
                let reserved: std::collections::HashSet<_> = session.pending.values().filter_map(|p| match &p.command {
                    Command::PlaceBelt { site: s, x, z, level, .. } if s == site && !view.belts.iter().any(|b| b.site_id == *s && b.level == *level && b.lift == 0 && b.cell_x == *x && b.cell_z == *z) => Some((*level, *x, *z)),
                    _ => None,
                }).collect();
                if goods::grid::cell_problem(view, site, *x, *z, 1, 1, None, *level, "").is_some() || rules::belt_problem(view, site, *x, *z, *level, 0).is_some() { continue; }
                let buying = pack.as_ref().map_or(0, |pack| pack.quantity * session.pending.values().filter(|p| matches!(&p.command, Command::Buy { offer, .. } if offer == &pack.id)).count() as i32);
                if held + buying <= reserved.len() as i32 {
                    let Some(pack) = &pack else { continue };
                    buy = Some(Command::Buy { site: site.clone(), offer: pack.id.clone() });
                }
            }
        }
        if let Some(buy) = buy { session.command(buy, now); }
        session.command(command, now);
    }
}

#[derive(Component)]
struct BeltGhost;

// Shared by the walking cursor and build mode: fill forward first, then corner toward the aim.
#[allow(clippy::too_many_arguments)]
fn drag_line(head: &mut Option<(String, i32, (i32, i32), i32)>, site: &str, level: i32, aim: (i32, i32), rotation: &mut i32,
    pressed: bool, held: bool, rotate_pressed: bool, rotate_held: bool, occupied: bool, size: (i32, i32)) -> Vec<Command> {
    let mut commands = vec![];
    let inside = |p: (i32, i32)| p.0 >= 0 && p.1 >= 0 && p.0 < size.0 && p.1 < size.1;
    let place = |p: (i32, i32), direction| Command::PlaceBelt { site: site.to_string(), x: p.0, z: p.1, direction, level };
    if !held { *head = None; }
    if pressed && inside(aim) {
        commands.push(place(aim, *rotation));
        *head = Some((site.to_string(), level, aim, *rotation));
    }
    if let Some((drag_site, drag_level, at, direction)) = head {
        if drag_site != site || *drag_level != level { *head = None; return commands; }
        let (sx, sz) = rules::belt_step(*direction);
        let ahead = (aim.0 - at.0) * sx + (aim.1 - at.1) * sz;
        for _ in 0..ahead.max(0) {
            let next = (at.0 + sx, at.1 + sz);
            if !inside(next) { break; }
            *at = next;
            commands.push(place(*at, *direction));
        }
        if (rotate_pressed || rotate_held) && !occupied {
            let right = rules::right_of(*direction);
            let (rx, rz) = rules::belt_step(right);
            let side = (aim.0 - at.0) * rx + (aim.1 - at.1) * rz;
            if side != 0 {
                *direction = if side > 0 { right } else { rules::left_of(*direction) };
                *rotation = *direction;
                commands.push(place(*at, *direction));
                let (sx, sz) = rules::belt_step(*direction);
                for _ in 0..side.abs() {
                    let next = (at.0 + sx, at.1 + sz);
                    if !inside(next) { break; }
                    *at = next;
                    commands.push(place(*at, *direction));
                }
            }
        }
    } else if rotate_pressed { *rotation = rules::right_of(*rotation); }
    commands
}

#[allow(clippy::too_many_arguments)]
fn belt_ghost(
    build: Res<Build>, panels: Res<Panels>, drag: Res<BeltDrag>, content: Res<Content>,
    mut commands: Commands, cursor: Res<Cursor>, aim: Res<Aim>, session: Res<Session>, space: Option<Res<Space>>, assets: Res<AssetServer>, bounds: Res<Bounds>, m: Option<Res<SiteMaterials>>,
    ghosts: Query<Entity, With<BeltGhost>>, mut last: Local<String>, mut materials: ResMut<Assets<StandardMaterial>>,
    mut tints: Local<Option<(Handle<StandardMaterial>, Handle<StandardMaterial>)>>,
) {
    let building = build.active.is_some() && panels.open == Some(Panel::Build);
    let item = if building { (build.tool == Tool::Belts).then_some(goods::BELT_ITEM_ID) } else { cursor.item.as_deref() };
    let rotation = if building { build.rotation } else { cursor.rotation };
    let wanted = (building || panels.open.is_none()) && matches!(item, Some(goods::BELT_ITEM_ID) | Some(goods::LIFT_ITEM_ID));
    let cell = if building { build.active.clone().zip(build.cell).map(|(site, cell)| (site, build.level, cell)) } else { aimed_cell(&aim) };
    let context = if building { build.active.clone().map(|site| (site, build.level)) } else { cell.as_ref().map(|c| (c.0.clone(), c.1)).or_else(|| drag.head.as_ref().map(|h| (h.0.clone(), h.1))) };
    let mut shown = vec![];
    if let (Some(world), true, Some((site, level))) = (session.joined(), wanted, context) {
        if let Some(view) = world.view_of(&site) {
            let mut planned = view.belts.clone();
            let mut pending: Vec<_> = session.pending.values().collect();
            pending.sort_by(|a, b| a.sent.total_cmp(&b.sent));
            let mut pending_cells = std::collections::BTreeMap::new();
            for p in pending {
                if let Command::PlaceBelt { site: s, x, z, direction, level: l } = &p.command {
                    if s != &site || *l != level { continue; }
                    planned.retain(|b| !(b.site_id == site && b.level == level && b.lift == 0 && b.cell_x == *x && b.cell_z == *z));
                    planned.push(goods::Belt { site_id: site.clone(), cell_x: *x, cell_z: *z, direction: *direction, level, ..default() });
                    pending_cells.insert((*x, *z), *direction);
                }
            }
            let cells = rules::by_cell(planned.iter().filter(|b| b.site_id == site));
            for (&(x, z), &direction) in &pending_cells {
                shown.push((site.clone(), level, x, z, direction, 0, rules::shape(&cells, x, z, level, direction, 0), true));
            }
            if let Some((_, _, cell)) = cell.filter(|c| drag.head.is_none() && !pending_cells.contains_key(&c.2)) {
                let (x, z) = cell;
                let lift = if item == Some(goods::LIFT_ITEM_ID) { if cursor.lift_down { -1 } else { 1 } } else { 0 };
                let existing = view.belts.iter().any(|b| b.site_id == site && b.level == level && b.lift == lift && (b.cell_x, b.cell_z) == cell);
                let held: i32 = view.lots.iter().filter(|l| l.location_id == world.inventory() && Some(l.item_id.as_str()) == item).map(|l| l.quantity).sum();
                let reserved = pending_cells.keys().filter(|&&(px, pz)| !view.belts.iter().any(|b| b.site_id == site && b.level == level && b.lift == 0 && (b.cell_x, b.cell_z) == (px, pz))).count() as i32;
                let bought = building && content.0.goods_offer_for(goods::BELT_ITEM_ID).is_some();
                let valid = existing || ((bought || held > if lift == 0 { reserved } else { 0 })
                    && goods::grid::cell_problem(view, &site, x, z, 1, 1, None, level, "").is_none()
                    && (lift == 0 || goods::grid::cell_problem(view, &site, x, z, 1, 1, None, level + lift, "").is_none())
                    && rules::belt_problem(view, &site, x, z, level, lift).is_none());
                shown.push((site, level, x, z, rotation, lift, rules::shape(&cells, x, z, level, rotation, lift), valid));
            }
        }
    }
    let key = format!("{shown:?}");
    if *last == key { return; }
    for entity in &ghosts { commands.entity(entity).despawn(); }
    let (Some(space), Some(m)) = (space, m) else { last.clear(); return };
    *last = key;
    let (valid, invalid) = tints.get_or_insert_with(|| {
        let tint = |base_color| StandardMaterial { base_color, alpha_mode: AlphaMode::Blend, unlit: true, ..default() };
        (materials.add(tint(Color::srgba(0.2, 0.85, 0.3, 0.45))), materials.add(tint(Color::srgba(0.9, 0.2, 0.15, 0.45))))
    }).clone();
    for (site, level, x, z, direction, lift, shape, allowed) in shown {
        let (_, yaw) = belt_model(shape, direction);
        let transform = Transform::from_translation(space.site_point(&site, x as f32 + 0.5, z as f32 + 0.5, level)).with_rotation(grid_rotation(yaw));
        let material = if allowed { valid.clone() } else { invalid.clone() };
        let root = commands.spawn((BeltGhost, transform, Visibility::default(), Name::new("Belt ghost"))).id();
        crate::sites::spawn_belt_model(&mut commands, &assets, &bounds, &m, root, shape, lift, Some(material.clone()));
        let pad = commands.spawn((Mesh3d(m.quad.clone()), MeshMaterial3d(material), Transform::from_xyz(0.0, 0.03, 0.0).with_rotation(Quat::from_rotation_x(-std::f32::consts::FRAC_PI_2)))).id();
        commands.entity(root).add_child(pad);
    }
}
#[cfg(test)]
mod tests {
    use super::*;

    fn cells(commands: Vec<Command>) -> Vec<(i32, i32, i32)> {
        commands.into_iter().map(|c| match c {
            Command::PlaceBelt { x, z, direction, .. } => (x, z, direction),
            _ => panic!("unexpected command"),
        }).collect()
    }
    #[test]
    fn belt_drag_fills_forward_without_sideways_or_backtracking() {
        let mut head = Some(("site".into(), 0, (2, 2), 0));
        let mut rotation = 0;
        assert_eq!(cells(drag_line(&mut head, "site", 0, (5, 5), &mut rotation, false, true, false, false, false, (20, 20))),
            vec![(2, 3, 0), (2, 4, 0), (2, 5, 0)]);
        assert!(drag_line(&mut head, "site", 0, (6, 1), &mut rotation, false, true, false, false, false, (20, 20)).is_empty());
    }
    #[test]
    fn belt_drag_held_rotate_fills_forward_then_corners() {
        let mut head = Some(("site".into(), 0, (2, 2), 0));
        let mut rotation = 0;
        assert_eq!(cells(drag_line(&mut head, "site", 0, (4, 4), &mut rotation, false, true, false, true, false, (20, 20))),
            vec![(2, 3, 0), (2, 4, 0), (2, 4, 1), (3, 4, 1), (4, 4, 1)]);
        assert_eq!(cells(drag_line(&mut head, "site", 0, (4, 6), &mut rotation, false, true, false, true, false, (20, 20))),
            vec![(4, 4, 0), (4, 5, 0), (4, 6, 0)]);
    }
    #[test]
    fn belt_drag_does_not_turn_toward_existing_or_pending_belt() {
        let mut head = Some(("site".into(), 0, (2, 2), 0));
        let mut rotation = 0;
        assert!(drag_line(&mut head, "site", 0, (4, 2), &mut rotation, false, true, true, true, true, (20, 20)).is_empty());
        assert_eq!(rotation, 0);
    }
    #[test]
    fn belt_drag_stops_at_site_boundary() {
        let mut head = Some(("site".into(), 0, (2, 2), 0));
        let mut rotation = 0;
        assert_eq!(cells(drag_line(&mut head, "site", 0, (2, 100), &mut rotation, false, true, false, false, false, (5, 5))),
            vec![(2, 3, 0), (2, 4, 0)]);
    }
    #[test]
    fn belt_drag_release_and_floor_change_stop_the_line() {
        let mut head = Some(("site".into(), 0, (2, 2), 0));
        let mut rotation = 0;
        assert!(drag_line(&mut head, "site", 1, (2, 5), &mut rotation, false, true, false, false, false, (20, 20)).is_empty());
        assert!(head.is_none());
        drag_line(&mut head, "site", 1, (3, 3), &mut rotation, true, true, false, false, false, (20, 20));
        assert!(head.is_some());
        assert!(drag_line(&mut head, "site", 1, (3, 5), &mut rotation, false, false, false, false, false, (20, 20)).is_empty());
        assert!(head.is_none());
    }
}
