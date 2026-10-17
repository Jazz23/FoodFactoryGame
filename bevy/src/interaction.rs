// The local player's world controls (Unity EquipmentInteraction and its Hover part): the aim ray from the crosshair (or the
// free pointer in the top-down view) names the machine, belt or employee under it and the floor cell on the avatar's level.
// Interact (E) opens what is aimed at within reach and Inventory (Tab) the inventory; a hotbar key puts a held machine on the cursor, shown as a ghost
// on its footprint (green where the server's own placement rule passes, red otherwise); R turns it, left click places it,
// Q empties the cursor; left click with an empty cursor opens the aimed machine. C opens the phone and L its logistics.
// Every change is a request: the ghost and hints are previews, the server re-checks each command.
use bevy::prelude::*;
use bevy::window::{CursorGrabMode, CursorOptions, PrimaryWindow};
use bevy::world_serialization::WorldAssetRoot;

use food_factory_bevy::goods::{self, rules, EquipmentState};
use food_factory_bevy::protocol::Command;

use crate::actions::PlayerActions;
use crate::models::{fitted, Bounds, Fit};
use crate::panels::{Panel, Panels};
use crate::screens::{HudLines, Screen};
use crate::session::{Content, Session};
use crate::sites::{Indoors, Pick, Solids};
use crate::space::{grid_rotation, Space, LEVEL_HEIGHT};

pub struct InteractionPlugin;

#[derive(SystemSet, Debug, Clone, PartialEq, Eq, Hash)]
pub struct InteractionSet;

impl Plugin for InteractionPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<Aim>().init_resource::<Cursor>().init_resource::<Hotbar>().add_systems(Update, (aim, hover_outline, pick_outlines, hotbar, act, show_ghost, hint).chain().after(crate::player::PlayerSet::Move).after(crate::panels::close_keys).in_set(InteractionSet));
    }
}

pub const HOTBAR_SIZE: usize = 9;
const MAX_RAY: f32 = 100.0;
pub const REACH: f32 = 2.5;
/// Height of the crosshair as a fraction of the screen from its top (Unity EquipmentInteraction.CrosshairViewportY 0.6 from
/// the bottom): above centre, so it aims further ahead of the avatar.
pub const CROSSHAIR_FROM_TOP: f32 = 0.4;

/// What the aim ray is on this frame.
#[derive(Resource, Default, Clone, Debug)]
pub struct Aim {
    pub pick: Option<Pick>,
    /// The drawn site, level and site-metre point the ray meets on the avatar's floor.
    pub floor: Option<(String, i32, Vec2)>,
    /// The avatar is within reach of the aimed pick.
    pub in_reach: bool,
    /// A purchasable map building (Unity PropertyMarker) aimed at within reach across the ground, by lot, when nothing nearer is.
    pub property: Option<String>,
}

/// What the cursor carries in the world: a held machine (by ID) or an item of the inventory (belts, lifts, goods for a belt),
/// the rotation either would be placed at, and whether a lift goes down.
#[derive(Resource, Default, Clone, Debug)]
pub struct Cursor {
    pub machine: Option<String>,
    pub item: Option<String>,
    pub rotation: i32,
    pub lift_down: bool,
}

impl Cursor {
    pub fn empty(&self) -> bool { self.machine.is_none() && self.item.is_none() }
    pub fn clear(&mut self) {
        self.machine = None;
        self.item = None;
    }
}

#[derive(Clone, Debug, PartialEq)]
pub enum HotbarEntry {
    Machine(String),
    Item(String),
}

/// Hotbar slots: held machine kinds, then belts and lifts in the inventory, in the order first seen (this client only).
#[derive(Resource, Default)]
pub struct Hotbar {
    pub entries: Vec<HotbarEntry>,
}

#[derive(Component)]
struct HoverOutlineEdge;

/// The twelve edges of a box as unit-cube transforms.
fn box_edges(centre: Vec3, size: Vec3, thickness: f32) -> Vec<Transform> {
    let mut edges = Vec::with_capacity(12);
    for x in [-1.0, 1.0] { for y in [-1.0, 1.0] {
        edges.push(Transform::from_translation(centre + Vec3::new(x * size.x * 0.5, y * size.y * 0.5, 0.0)).with_scale(Vec3::new(thickness, thickness, size.z + thickness)));
    }}
    for x in [-1.0, 1.0] { for z in [-1.0, 1.0] {
        edges.push(Transform::from_translation(centre + Vec3::new(x * size.x * 0.5, 0.0, z * size.z * 0.5)).with_scale(Vec3::new(thickness, size.y + thickness, thickness)));
    }}
    for y in [-1.0, 1.0] { for z in [-1.0, 1.0] {
        edges.push(Transform::from_translation(centre + Vec3::new(0.0, y * size.y * 0.5, z * size.z * 0.5)).with_scale(Vec3::new(size.x + thickness, thickness, thickness)));
    }}
    edges
}

/// While a task target is being picked, every valid target gets a thin light-green outline (Unity's pick tint).
fn pick_outlines(
    mut commands: Commands, panels: Res<Panels>, session: Res<Session>, solids: Res<Solids>, mut drawn: Local<(Vec<Entity>, Vec<(Vec3, Vec3)>)>,
    mut meshes: ResMut<Assets<Mesh>>, mut materials: ResMut<Assets<StandardMaterial>>, mut handles: Local<Option<(Handle<Mesh>, Handle<StandardMaterial>)>>,
) {
    let targets: Vec<(Vec3, Vec3)> = match (&panels.picking, session.joined()) {
        (Some(pick), Some(world)) => {
            let valid = crate::panels::pick_targets(world, pick);
            solids.picks.iter().filter(|b| matches!(&b.pick, Pick::Equipment(id) if valid.contains(id))).map(|b| (b.min, b.max)).collect()
        }
        _ => vec![],
    };
    if targets == drawn.1 { return; }
    for entity in drawn.0.drain(..) { commands.entity(entity).despawn(); }
    let (mesh, material) = handles.get_or_insert_with(|| {
        let material = materials.add(StandardMaterial { base_color: Color::srgb(0.55, 0.9, 0.55), unlit: true, ..default() });
        (meshes.add(Cuboid::new(1.0, 1.0, 1.0)), material)
    }).clone();
    for (min, max) in &targets {
        for transform in box_edges((*min + *max) * 0.5, *max - *min + Vec3::splat(0.06), 0.015) {
            drawn.0.push(commands.spawn((Mesh3d(mesh.clone()), MeshMaterial3d(material.clone()), transform, bevy::light::NotShadowCaster, Name::new("Pick outline"))).id());
        }
    }
    drawn.1 = targets;
}

/// A lightweight bounds outline for the currently aimed, usable equipment or employee.
fn hover_outline(
    mut commands: Commands, aim: Res<Aim>, solids: Res<Solids>, mut edges: Local<Vec<Entity>>, mut shown: Local<Option<Pick>>,
    mut meshes: ResMut<Assets<Mesh>>, mut materials: ResMut<Assets<StandardMaterial>>, mut handles: Local<Option<(Handle<Mesh>, Handle<StandardMaterial>)>>,
    mut transforms: Query<&mut Transform, With<HoverOutlineEdge>>, panels: Res<Panels>,
) {
    // Drawn only while no screen is open (Unity HoverOutline).
    let target = if aim.in_reach && panels.open.is_none() { aim.pick.as_ref().filter(|p| matches!(p, Pick::Equipment(_) | Pick::Employee(_))) } else { None };
    let bounds = target.and_then(|pick| solids.picks.iter().chain(&solids.moving).find(|b| &b.pick == pick));
    if shown.as_ref() != target {
        for entity in edges.drain(..) { commands.entity(entity).despawn(); }
        *shown = target.cloned();
    }
    let Some(bounds) = bounds else { return };
    let centre = (bounds.min + bounds.max) * 0.5;
    let size = bounds.max - bounds.min;
    let (mesh, material) = handles.get_or_insert_with(|| {
        let material = materials.add(StandardMaterial { base_color: Color::srgb(0.33, 0.62, 0.38), unlit: true, ..default() });
        (meshes.add(Cuboid::new(1.0, 1.0, 1.0)), material)
    }).clone();
    let edge_transforms = box_edges(centre, size, 0.025);
    if edges.len() != edge_transforms.len() {
        for transform in edge_transforms {
            edges.push(commands.spawn((HoverOutlineEdge, Mesh3d(mesh.clone()), MeshMaterial3d(material.clone()), transform, bevy::light::NotShadowCaster, Name::new("Hover outline"))).id());
        }
    } else {
        for (entity, transform) in edges.iter().copied().zip(edge_transforms) {
            if let Ok(mut current) = transforms.get_mut(entity) { *current = transform; }
        }
    }
}

/// Scene bounds of a map building: its footprint from its floor to about its roof.
fn property_box(space: &Space, building: &food_factory_bevy::world::layout::WorldBuilding) -> (Vec3, Vec3) {
    let floor = building.elevation_cm as f32 / 100.0;
    let a = space.to_scene(building.x as f32, building.z as f32, floor);
    let b = space.to_scene((building.x + building.width) as f32, (building.z + building.depth) as f32, floor + building.floors.max(1) as f32 * 3.5 + 1.5);
    (a.min(b), a.max(b))
}

fn ray_box(origin: Vec3, direction: Vec3, min: Vec3, max: Vec3) -> Option<f32> {
    let mut near = 0.0f32;
    let mut far = MAX_RAY;
    for axis in 0..3 {
        let d = direction[axis];
        if d.abs() < 1e-6 {
            if origin[axis] < min[axis] || origin[axis] > max[axis] {
                return None;
            }
            continue;
        }
        let (mut a, mut b) = ((min[axis] - origin[axis]) / d, (max[axis] - origin[axis]) / d);
        if a > b {
            std::mem::swap(&mut a, &mut b);
        }
        near = near.max(a);
        far = far.min(b);
        if near > far {
            return None;
        }
    }
    Some(near)
}

#[allow(clippy::too_many_arguments)]
fn aim(
    session: Res<Session>, space: Option<Res<Space>>, solids: Res<Solids>, indoors: Res<Indoors>, cameras: Query<(&Camera, &GlobalTransform)>, window: Query<(&Window, &CursorOptions), With<PrimaryWindow>>,
    player: Query<&Transform, With<crate::player::Player>>, mut aim: ResMut<Aim>, drawn: Res<crate::map::DrawnSites>,
) {
    let (Some(space), Some(world), Ok(player)) = (space, session.joined(), player.single()) else {
        *aim = Aim::default();
        return;
    };
    let Some((camera, transform)) = cameras.iter().next() else { return };
    let Ok((window, cursor)) = window.single() else { return };
    // A locked pointer aims through the crosshair, above the middle of the screen; a free one through the pointer.
    let point = if cursor.grab_mode == CursorGrabMode::None { window.cursor_position() } else { Some(Vec2::new(window.width() * 0.5, window.height() * CROSSHAIR_FROM_TOP)) };
    let ray = point.and_then(|p| camera.viewport_to_world(transform, p).ok()).map(|r| (r.origin, *r.direction));
    let Some((origin, direction)) = ray else { return };
    let site = indoors.site.clone().unwrap_or_else(|| world.site.clone());
    let level = indoors.level;
    // Picks on the avatar's level (or reaching it) are named; the nearest wins.
    let mut best: Option<(f32, &crate::sites::PickBox)> = None;
    for pick in solids.picks.iter().chain(&solids.moving) {
        if pick.site == site && (pick.level - level).abs() > 1 {
            continue;
        }
        if let Some(t) = ray_box(origin, direction, pick.min, pick.max) {
            if best.is_none_or(|b| t < b.0) {
                best = Some((t, pick));
            }
        }
    }
    let floor_y = space.floor_height(&site, level);
    let floor = (direction.y < -1e-4).then(|| (floor_y - origin.y) / direction.y).filter(|t| *t > 0.0 && *t < MAX_RAY).map(|t| {
        let point = origin + direction * t;
        let (x, z) = space.to_site(&site, point);
        (site.clone(), level, Vec2::new(x, z))
    });
    let feet = player.translation;
    let in_reach = best.is_some_and(|(_, p)| {
        let nearest = feet.clamp(p.min, p.max);
        Vec2::new(nearest.x - feet.x, nearest.z - feet.z).length() <= REACH && (p.level == level || p.site != site)
    });
    // Map buildings are aimed at from the street: the same reach, measured across the ground, no floor check.
    let mut property: Option<(f32, String)> = None;
    if world.offers_by_lot.len() > 0 {
        // A site drawn as a shell (its own walls and machines) is not aimed at as a map building.
        let wanted: std::collections::HashMap<&str, &str> = world.offers_by_lot.values().filter(|o| !drawn.sites.contains(&o.site_id))
            .map(|o| (o.building_id.as_str(), o.lot_id.as_str())).collect();
        for building in world.layout.buildings.iter() {
            let Some(lot) = wanted.get(building.id.as_str()) else { continue };
            let (min, max) = property_box(&space, building);
            let Some(t) = ray_box(origin, direction, min, max) else { continue };
            if best.is_some_and(|b| b.0 <= t) || property.as_ref().is_some_and(|p| p.0 <= t) { continue; }
            let nearest = feet.clamp(min, max);
            if Vec2::new(nearest.x - feet.x, nearest.z - feet.z).length() <= REACH { property = Some((t, lot.to_string())); }
        }
    }
    let property = property.map(|p| p.1);
    let pick = if property.is_some() { None } else { best.map(|(_, p)| p.pick.clone()) };
    let next = Aim { pick, floor, in_reach, property };
    if aim.pick != next.pick || aim.floor != next.floor || aim.in_reach != next.in_reach || aim.property != next.property {
        *aim = next;
    }
}

/// Held machine kinds and conveyor items of this player, in hotbar order.
fn hotbar_entries(session: &Session, hotbar: &mut Hotbar) -> Vec<HotbarEntry> {
    let Some(world) = session.joined() else { return vec![] };
    let Some(view) = world.view() else { return vec![] };
    let mut entries: Vec<HotbarEntry> = view.equipment.iter().filter(|e| e.state == EquipmentState::Held && e.holder_id == world.player).map(|e| HotbarEntry::Machine(e.kind.clone())).collect();
    entries.sort_by(|a, b| format!("{a:?}").cmp(&format!("{b:?}")));
    entries.dedup();
    let inventory = world.inventory();
    for item in [goods::BELT_ITEM_ID, goods::LIFT_ITEM_ID] {
        if view.lots.iter().any(|l| l.location_id == inventory && l.item_id == item && l.quantity > 0) {
            entries.push(HotbarEntry::Item(item.to_string()));
        }
    }
    for entry in &entries {
        if !hotbar.entries.contains(entry) {
            hotbar.entries.push(entry.clone());
        }
    }
    hotbar.entries.retain(|e| entries.contains(e));
    hotbar.entries.truncate(HOTBAR_SIZE);
    hotbar.entries.clone()
}

/// The inventory lot of an item this player could use from the cursor.
pub fn inventory_lot<'a>(world: &'a crate::session::Joined, item: &str) -> Option<&'a goods::Lot> {
    let inventory = world.inventory();
    world.view()?.lots.iter().find(|l| l.location_id == inventory && l.item_id == item && l.quantity > 0 && !l.spoiled)
}

fn hotbar(actions: Res<PlayerActions>, session: Res<Session>, mut bar: ResMut<Hotbar>, mut cursor: ResMut<Cursor>, panels: Res<Panels>, screen: Res<Screen>) {
    let entries = hotbar_entries(&session, &mut bar);
    if *screen != Screen::Playing {
        return;
    }
    if actions.clear_pressed || (actions.close_pressed && panels.open.is_none()) {
        cursor.clear();
    }
    let Some(world) = session.joined() else { return };
    let Some(view) = world.view() else { return };
    // A machine on the cursor that is no longer held (placed, sold, given), or an item no longer carried, leaves it.
    if let Some(id) = &cursor.machine {
        if !view.equipment.iter().any(|e| &e.id == id && e.state == EquipmentState::Held) {
            cursor.machine = None;
        }
    }
    if let Some(item) = &cursor.item {
        if inventory_lot(world, item).is_none() {
            cursor.item = None;
        }
    }
    let Some(slot) = actions.hotbar else { return };
    if panels.open.is_some() && !matches!(panels.open, Some(Panel::Inventory)) {
        return;
    }
    let Some(entry) = entries.get(slot) else { return };
    let current_kind = cursor.machine.as_ref().and_then(|id| view.equipment.iter().find(|e| &e.id == id)).map(|e| e.kind.clone());
    match entry {
        HotbarEntry::Machine(kind) => {
            cursor.item = None;
            if current_kind.as_deref() == Some(kind.as_str()) {
                cursor.machine = None;
                return;
            }
            cursor.machine = view.equipment.iter().find(|e| e.state == EquipmentState::Held && e.holder_id == world.player && &e.kind == kind).map(|e| e.id.clone());
        }
        HotbarEntry::Item(item) => {
            cursor.machine = None;
            cursor.item = if cursor.item.as_deref() == Some(item.as_str()) { None } else { Some(item.clone()) };
        }
    }
}

/// The anchor cell, level and site that would centre the cursor machine's footprint on the aimed floor point.
pub fn placement(aim: &Aim, footprint: (i32, i32)) -> Option<(String, i32, i32, i32)> {
    let (site, level, point) = aim.floor.as_ref()?;
    let x = (point.x - footprint.0 as f32 * 0.5).round() as i32;
    let z = (point.y - footprint.1 as f32 * 0.5).round() as i32;
    Some((site.clone(), x, z, *level))
}

#[allow(clippy::too_many_arguments)]
#[allow(clippy::too_many_arguments)]
fn act(
    time: Res<Time>, actions: Res<PlayerActions>, mut session: ResMut<Session>, aim: Res<Aim>, mut cursor: ResMut<Cursor>, mut panels: ResMut<Panels>, screen: Res<Screen>, content: Res<Content>, build: Res<crate::build::Build>,
    mut intents: ResMut<crate::ui::UiIntents>,
) {
    if *screen != Screen::Playing || session.joined().is_none() {
        return;
    }
    let now = time.elapsed_secs_f64();
    if actions.rotate_pressed && cursor.machine.is_some() {
        cursor.rotation = (cursor.rotation + 1) & 3;
    }
    if actions.flip_lift_pressed && cursor.item.as_deref() == Some(goods::LIFT_ITEM_ID) {
        cursor.lift_down = !cursor.lift_down;
    }
    if panels.open.is_some() {
        return;
    }
    // Picking a task target: a click or E on a valid machine in reach hands its ID back to the employee screen.
    if let Some(pick) = panels.picking.clone() {
        if actions.place_pressed || actions.interact_pressed {
            if let (Some(Pick::Equipment(id)), true, Some(world)) = (&aim.pick, aim.in_reach, session.joined()) {
                if crate::panels::pick_targets(world, &pick).contains(id) {
                    let role = match pick.role { crate::panels::PickRole::Source => "src", crate::panels::PickRole::Destination => "dst", crate::panels::PickRole::Machine => "machine" };
                    intents.0.push(format!("p:task-set|{role}|{}|{id}", pick.index));
                }
            }
        }
        return;
    }
    if actions.phone_pressed {
        panels.open = Some(Panel::Phone(crate::panels::PhoneTab::Map));
        return;
    }
    if actions.logistics_pressed {
        panels.open = Some(Panel::Phone(crate::panels::PhoneTab::Trucks));
        return;
    }
    if actions.build_pressed && build.active.is_none() {
        panels.open = Some(Panel::Build);
        return;
    }
    let aimed_machine = match (&aim.pick, aim.in_reach) {
        (Some(Pick::Equipment(id)), true) => Some(id.clone()),
        _ => None,
    };
    let aimed_employee = match (&aim.pick, aim.in_reach) {
        (Some(Pick::Employee(id)), true) => Some(id.clone()),
        _ => None,
    };
    if actions.inventory_pressed {
        panels.open = Some(Panel::Inventory);
        return;
    }
    if actions.interact_pressed {
        let target = match (aimed_machine, aimed_employee, aim.property.clone()) {
            (Some(id), _, _) if openable(&session, &content, &id) => Some(Panel::Machine(id)),
            (_, Some(id), _) => Some(Panel::Employee(id)),
            (_, _, Some(lot)) => Some(Panel::Property(lot)),
            _ => None,
        };
        if target.is_some() {
            panels.open = target;
        }
        return;
    }
    if !actions.place_pressed || cursor.item.is_some() {
        return;
    }
    if let Some(machine) = cursor.machine.clone() {
        let Some(world) = session.joined() else { return };
        let Some(piece) = world.view().and_then(|v| v.equipment.iter().find(|e| e.id == machine)) else { return };
        let footprint = goods::grid::footprint(piece.width, piece.depth, cursor.rotation);
        let Some((site, x, z, level)) = placement(&aim, footprint) else { return };
        if site != world.site {
            return;
        }
        let rotation = cursor.rotation;
        session.command(Command::Place { equipment: machine, x, z, rotation, level }, now);
        return;
    }
    if let Some(id) = aimed_machine.filter(|id| openable(&session, &content, id)) {
        panels.open = Some(Panel::Machine(id));
    } else if let Some(id) = aimed_employee {
        panels.open = Some(Panel::Employee(id));
    } else if let Some(lot) = aim.property.clone() {
        panels.open = Some(Panel::Property(lot));
    }
}

/// A machine opens a screen when its kind says so or when it holds goods (storage, a station).
fn openable(session: &Session, content: &Content, id: &str) -> bool {
    let Some(piece) = session.joined().and_then(|w| w.views.values().find_map(|v| v.equipment.iter().find(|e| e.id == id))) else { return false };
    content.0.equipment.get(&piece.kind).is_some_and(|k| k.opens_screen) || piece.input_capacity > 0 || piece.output_capacity > 0 || goods::is_table(piece)
}

#[derive(Component)]
struct Ghost {
    kind: String,
}

#[allow(clippy::too_many_arguments)]
fn show_ghost(
    mut commands: Commands, session: Res<Session>, cursor: Res<Cursor>, aim: Res<Aim>, space: Option<Res<Space>>, content: Res<Content>, assets: Res<AssetServer>, bounds: Res<Bounds>,
    mut ghosts: Query<(Entity, &Ghost, &mut Transform, &Children)>, mut materials: ResMut<Assets<StandardMaterial>>, mut meshes: ResMut<Assets<Mesh>>,
    mut pads: Query<&mut MeshMaterial3d<StandardMaterial>>, mut colours: Local<Option<(Handle<StandardMaterial>, Handle<StandardMaterial>, Handle<Mesh>)>>,
) {
    let piece = session.joined().zip(cursor.machine.as_ref()).and_then(|(w, id)| w.view().and_then(|v| v.equipment.iter().find(|e| &e.id == id)).map(|e| (w, e)));
    let (Some((world, piece)), Some(space)) = (piece, space) else {
        for (entity, ..) in &ghosts {
            commands.entity(entity).despawn();
        }
        return;
    };
    let (valid, invalid, quad) = colours.get_or_insert_with(|| {
        let tint = |c: Color| StandardMaterial { base_color: c, alpha_mode: AlphaMode::Blend, unlit: true, ..default() };
        (materials.add(tint(Color::srgba(0.2, 0.85, 0.3, 0.45))), materials.add(tint(Color::srgba(0.9, 0.2, 0.15, 0.45))), meshes.add(Plane3d::default().mesh().size(1.0, 1.0)))
    }).clone();
    let footprint = goods::grid::footprint(piece.width, piece.depth, cursor.rotation);
    let target = placement(&aim, footprint);
    let Some((site, x, z, level)) = target else {
        for (entity, ..) in &ghosts {
            commands.entity(entity).despawn();
        }
        return;
    };
    let offer = world.offers.get(&site);
    let problem = world.view_of(&site).map_or(Some("no-site"), |view| {
        if site != world.site {
            return Some("not-current-site");
        }
        let mut at = piece.clone();
        at.site_id = site.clone();
        goods::grid::placement_problem(view, &at, x, z, cursor.rotation, level).or_else(|| rules::placement_problem(view, &at, x, z, cursor.rotation, offer))
    });
    let centre = space.site_point(&site, x as f32 + footprint.0 as f32 * 0.5, z as f32 + footprint.1 as f32 * 0.5, level) + Vec3::Y * 0.0;
    let transform = Transform::from_translation(centre).with_rotation(grid_rotation(cursor.rotation));
    let material = if problem.is_none() { valid } else { invalid };
    let existing = ghosts.iter_mut().next();
    match existing {
        Some((entity, ghost, mut t, children)) if ghost.kind == piece.kind => {
            *t = transform;
            if let Some(pad) = children.first() {
                if let Ok(mut current) = pads.get_mut(*pad) {
                    if current.0 != material {
                        current.0 = material;
                    }
                }
            }
            let _ = entity;
        }
        other => {
            if let Some((entity, ..)) = other {
                commands.entity(entity).despawn();
            }
            let Some(kind) = content.0.equipment.get(&piece.kind) else { return };
            let root = commands.spawn((Ghost { kind: piece.kind.clone() }, transform, Visibility::default(), Name::new("Ghost"))).id();
            let pad = commands.spawn((Mesh3d(quad), MeshMaterial3d(material), Transform::from_xyz(0.0, 0.03, 0.0).with_scale(Vec3::new(piece.width as f32, 1.0, piece.depth as f32)))).id();
            let model = commands.spawn((WorldAssetRoot(assets.load(GltfAssetLabel::Scene(0).from_asset(kind.model.clone()))), fitted(&bounds, &kind.model, Fit::Natural))).id();
            commands.entity(root).add_children(&[pad, model]);
        }
    }
    let _ = LEVEL_HEIGHT;
}

/// The HUD hint for what the aim names.
fn hint(aim: Res<Aim>, session: Res<Session>, content: Res<Content>, cursor: Res<Cursor>, mut hud: ResMut<HudLines>, panels: Res<Panels>, bindings: Res<crate::actions::Bindings>) {
    let Some(world) = session.joined() else { return };
    let e = crate::settings::label_of(bindings.interact);
    let text = if let Some(pick) = &panels.picking {
        let aimed = match (&aim.pick, aim.in_reach) {
            (Some(Pick::Equipment(id)), true) if crate::panels::pick_targets(world, pick).contains(id) => format!(" - click or {e} to choose this one"),
            (Some(Pick::Equipment(_)), true) => " - this one cannot be chosen".to_string(),
            _ => String::new(),
        };
        format!("Choose the {} for task {}: aim at an outlined machine{aimed} (Esc cancels)", pick.role.noun(), pick.index + 1)
    } else if panels.open.is_some() {
        String::new()
    } else if let Some(item) = &cursor.item {
        match item.as_str() {
            goods::BELT_ITEM_ID => "Belts: hold left click and move to lay a line, R turns, hold right click on a belt to take it up".to_string(),
            goods::LIFT_ITEM_ID => format!("Lift ({}): left click to place, R turns, V flips", if cursor.lift_down { "down" } else { "up" }),
            other => format!("{}: Z puts one on the aimed belt", content.0.item_name(other)),
        }
    } else if let Some(id) = &cursor.machine {
        let kind = world.view().and_then(|v| v.equipment.iter().find(|e| &e.id == id)).map(|e| content.0.equipment_name(&e.kind)).unwrap_or_default();
        format!("{kind}: left click to place, R to turn, Q to put away")
    } else {
        match &aim.pick {
            Some(Pick::Equipment(id)) => {
                let kind = world.views.values().find_map(|v| v.equipment.iter().find(|e| &e.id == id)).map(|e| content.0.equipment_name(&e.kind)).unwrap_or_default();
                if aim.in_reach { format!("{e}  {kind}") } else { format!("{kind} (too far)") }
            }
            Some(Pick::Employee(id)) => {
                let name = world.employees.iter().find(|e| &e.id == id).map(|e| e.name.clone()).unwrap_or_default();
                if aim.in_reach { format!("{e}  {name}") } else { name }
            }
            Some(Pick::Belt(_)) => "Conveyor belt (F takes an item off it)".into(),
            None => match aim.property.as_ref().and_then(|lot| world.offers_by_lot.get(lot)) {
                Some(offer) => format!("{e}  {}: price and owner", offer.building_id),
                None => String::new(),
            },
        }
    };
    if hud.hint != text {
        hud.hint = text;
    }
}
