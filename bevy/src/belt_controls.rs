// The local player's belt controls (Unity EquipmentInteraction.Belts), Factorio-style. With belts on the cursor a ghost belt
// shows the aimed cell in the cursor direction; pressing Place starts a drag that lays belts forward along that direction as
// the aim moves on (never sideways or back), filling every cell it skips; R during a drag turns the line at its head toward
// the aim and carries on from there, and R without a drag turns the cursor direction (or, with an empty cursor, the aimed
// belt). Holding Remove on a belt (belts or lifts on the cursor) takes it up after a short hold, then every belt the aim passes
// over for the rest of the press. With lifts on the cursor a ghost lift shows the aimed cell going up or down one floor; Place
// puts one there, V flips it. With other goods on the cursor Z puts one unit on the aimed belt; F takes the riding item nearest
// the aim off the belt. Each belt is its own request; the server checks and charges every one.
use bevy::prelude::*;

use food_factory_bevy::goods::{self, rules};
use food_factory_bevy::protocol::Command;

use crate::actions::PlayerActions;
use crate::interaction::{inventory_lot, Aim, Cursor};
use crate::models::Bounds;
use crate::panels::Panels;
use crate::screens::Screen;
use crate::session::Session;
use crate::sites::{belt_model, Pick, SiteMaterials};
use crate::space::{grid_rotation, Space};

pub struct BeltControlsPlugin;

impl Plugin for BeltControlsPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<BeltDrag>().add_systems(Update, (belt_input, belt_ghost).chain().after(crate::player::PlayerSet::Move));
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
    time: Res<Time>, actions: Res<PlayerActions>, mut session: ResMut<Session>, aim: Res<Aim>, mut cursor: ResMut<Cursor>, mut drag: ResMut<BeltDrag>, panels: Res<Panels>, screen: Res<Screen>,
) {
    if *screen != Screen::Playing || panels.open.is_some() {
        drag.head = None;
        return;
    }
    let Some(world) = session.joined() else { return };
    let now = time.elapsed_secs_f64();
    let item = cursor.item.clone();
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
    if actions.rotate_pressed && cursor.empty() {
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
    if conveyor && actions.remove_held {
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
            let cell = aimed_cell(&aim).filter(|c| c.0 == world.site);
            if actions.place_pressed {
                if let Some((site, level, (x, z))) = cell.clone() {
                    commands.push(Command::PlaceBelt { site: site.clone(), x, z, direction: cursor.rotation, level });
                    drag.head = Some((site, level, (x, z), cursor.rotation));
                }
            } else if !actions.place_held {
                drag.head = None;
            }
            if actions.rotate_pressed {
                match (&drag.head, &cell) {
                    (Some((site, level, head, direction)), Some((_, _, aimed))) if actions.place_held => {
                        // Turn toward the side the aim is on, laying the new line up to the aim's projection.
                        let (dx, dz) = (aimed.0 - head.0, aimed.1 - head.1);
                        let right = rules::right_of(*direction);
                        let (rx, rz) = rules::belt_step(right);
                        let side = dx * rx + dz * rz;
                        if side != 0 {
                            let turned = if side > 0 { right } else { rules::left_of(*direction) };
                            commands.push(Command::PlaceBelt { site: site.clone(), x: head.0, z: head.1, direction: turned, level: *level });
                            let (sx, sz) = rules::belt_step(turned);
                            let mut at = *head;
                            for _ in 0..side.abs() {
                                at = (at.0 + sx, at.1 + sz);
                                commands.push(Command::PlaceBelt { site: site.clone(), x: at.0, z: at.1, direction: turned, level: *level });
                            }
                            cursor.rotation = turned;
                            drag.head = Some((site.clone(), *level, at, turned));
                        }
                    }
                    _ => cursor.rotation = (cursor.rotation + 1) & 3,
                }
            }
            // Dragging lays belts forward along the line up to the aim's projection.
            if actions.place_held && !actions.place_pressed {
                if let (Some((site, level, head, direction)), Some((_, aimed_level, aimed))) = (drag.head.clone(), cell) {
                    if aimed_level == level {
                        let (sx, sz) = rules::belt_step(direction);
                        let ahead = (aimed.0 - head.0) * sx + (aimed.1 - head.1) * sz;
                        if ahead > 0 {
                            let mut at = head;
                            for _ in 0..ahead.min(32) {
                                at = (at.0 + sx, at.1 + sz);
                                commands.push(Command::PlaceBelt { site: site.clone(), x: at.0, z: at.1, direction, level });
                            }
                            drag.head = Some((site, level, at, direction));
                        }
                    }
                }
            }
        }
        _ => {}
    }
    for command in commands {
        session.command(command, now);
    }
}

#[derive(Component)]
struct BeltGhost {
    key: String,
}

#[allow(clippy::too_many_arguments)]
fn belt_ghost(
    mut commands: Commands, cursor: Res<Cursor>, aim: Res<Aim>, session: Res<Session>, space: Option<Res<Space>>, assets: Res<AssetServer>, bounds: Res<Bounds>, m: Option<Res<SiteMaterials>>,
    mut ghosts: Query<(Entity, &BeltGhost, &mut Transform)>, mut ghost_material: Local<Option<Handle<StandardMaterial>>>, mut materials: ResMut<Assets<StandardMaterial>>,
) {
    let item = cursor.item.as_deref();
    let wanted = matches!(item, Some(goods::BELT_ITEM_ID) | Some(goods::LIFT_ITEM_ID));
    let cell = aimed_cell(&aim);
    let (Some(space), Some(m), Some(_), true, Some((site, level, (x, z)))) = (space, m, session.joined(), wanted, cell) else {
        for (entity, ..) in &ghosts {
            commands.entity(entity).despawn();
        }
        return;
    };
    let lift = if item == Some(goods::LIFT_ITEM_ID) { if cursor.lift_down { -1 } else { 1 } } else { 0 };
    let key = format!("{lift}");
    let shape = rules::BeltShape::Straight;
    let (_, yaw) = belt_model(shape, cursor.rotation);
    let transform = Transform::from_translation(space.site_point(&site, x as f32 + 0.5, z as f32 + 0.5, level)).with_rotation(grid_rotation(yaw));
    if let Some((entity, ghost, mut t)) = ghosts.iter_mut().next() {
        if ghost.key == key {
            *t = transform;
            return;
        }
        commands.entity(entity).despawn();
    }
    let material = ghost_material.get_or_insert_with(|| materials.add(StandardMaterial { base_color: Color::srgba(0.3, 0.8, 1.0, 0.4), alpha_mode: AlphaMode::Blend, unlit: true, ..default() })).clone();
    let root = commands.spawn((BeltGhost { key }, transform, Visibility::default(), Name::new("Belt ghost"))).id();
    crate::sites::spawn_belt_model(&mut commands, &assets, &bounds, &m, root, shape, lift, Some(material.clone()));
    let pad = commands.spawn((Mesh3d(m.quad.clone()), MeshMaterial3d(material), Transform::from_xyz(0.0, 0.03, 0.0).with_rotation(Quat::from_rotation_x(-std::f32::consts::FRAC_PI_2)))).id();
    commands.entity(root).add_child(pad);
}
