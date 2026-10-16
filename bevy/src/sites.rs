// Drawn sites (Unity DrawnSites, BuildingPresenter, RestaurantShellModel, EquipmentPresenter, BeltPresenter): every site with
// a baseline is drawn in full at its lot. A restaurant's ground storey is built from the art kit (wall graph: straight cells pair
// into 2 m pieces, corners and ends get a post with half pieces, doorways with frame and swinging leaves, windows over two
// cells); other storeys and shells are boxes; upper storeys get a slab, the elevator cell a pad, and every shell a roof and
// (single storey) a ceiling. Placed equipment is centred on its footprint and mounted (flush, wall, ceiling, tabletop, backed);
// belts are straight or corner models with riding goods gliding toward their replicated positions. The local avatar's cell
// decides "indoors": that building's roof and the storeys above the avatar hide. Collision boxes for the avatar and pick boxes
// for aiming are rebuilt with the visuals. Presentation only: nothing here changes state.
use std::collections::{HashMap, HashSet};
use std::f32::consts::PI;

use bevy::light::NotShadowCaster;
use bevy::prelude::*;
use bevy::world_serialization::WorldAssetRoot;

use food_factory_bevy::content::{EquipmentContent, Mount};
use food_factory_bevy::goods::{self, grid, rules, Belt, Building, Equipment, Snapshot};

use crate::map::DrawnSites;
use crate::models::{fitted, Bounds, Fit};
use crate::session::{Content, Session};
use crate::space::{grid_rotation, yaw_rotation, Space, LEVEL_HEIGHT};

pub struct SitesPlugin;

impl Plugin for SitesPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<SiteVisuals>()
            .init_resource::<Solids>()
            .init_resource::<Indoors>()
            .init_resource::<EditedSite>()
            .add_systems(Startup, setup_materials)
            .add_systems(Update, (choose_drawn, build_buildings, build_equipment, build_belts, rebuild_solids, find_indoors, show_storeys, glide_items, swing_doors).chain());
    }
}

const SLAB: f32 = 0.2;
const WALL_THICKNESS: f32 = 0.3;
const DOOR_HEIGHT: f32 = 2.2;
const SILL_HEIGHT: f32 = 0.85;
const SINGLE_HINGE: f32 = 0.475;
const DOUBLE_HINGE: f32 = 0.975;
const LEAF_WIDTH: f32 = 0.95;
const DOUBLE_LEAF_SCALE: f32 = (DOUBLE_HINGE - 0.003) / LEAF_WIDTH;
const WALL_FACE: f32 = 0.16;
const FLUSH_LIFT: f32 = 0.015;
pub const BELT_SURFACE: f32 = 0.8;
const ITEM_SIZE: f32 = 0.42;
const DOOR_OPEN_DEGREES: f32 = 95.0;
const DOOR_DEGREES_PER_SECOND: f32 = 300.0;
const DOOR_REACH: f32 = 1.8;
const DIRECTIONS: [(i32, i32); 4] = [(1, 0), (-1, 0), (0, 1), (0, -1)];

pub const BELT_STRAIGHT: &str = "models/Models/Belt/Conveyor_Straight_1m.glb";
pub const BELT_LEFT: &str = "models/Models/Belt/Conveyor_Corner_Left_90.glb";
pub const BELT_RIGHT: &str = "models/Models/Belt/Conveyor_Corner_Right_90.glb";

/// The site being edited in build mode: its roofs hide and every storey shows.
#[derive(Resource, Default)]
pub struct EditedSite(pub Option<String>);

/// Where the local avatar stands: the drawn site, the building it is inside (if any), its level and cell.
#[derive(Resource, Default, Clone, Debug)]
pub struct Indoors {
    pub site: Option<String>,
    pub building: Option<String>,
    pub level: i32,
    pub cell: (i32, i32),
    pub ceiling: Option<f32>,
}

/// What the aim ray can name.
#[derive(Clone, Debug, PartialEq)]
pub enum Pick {
    Equipment(String),
    Belt(String),
    Employee(String),
}

#[derive(Clone, Debug)]
pub struct PickBox {
    pub pick: Pick,
    pub site: String,
    pub level: i32,
    pub min: Vec3,
    pub max: Vec3,
}

/// Scene-space boxes the avatar collides with (walls, slabs, machines).
#[derive(Resource, Default)]
pub struct Solids {
    pub boxes: Vec<(Vec3, Vec3)>,
    pub picks: Vec<PickBox>,
    /// Picks that move every frame (employees), written by the characters module.
    pub moving: Vec<PickBox>,
    /// Collision boxes that move every frame (vehicle bodies), written by the vehicles module.
    pub moving_boxes: Vec<(Vec3, Vec3)>,
    revision: u64,
}

struct ShellView {
    root: Entity,
    built: String,
    site: String,
    storeys: Vec<Entity>,
    roof: Vec<Entity>,
    ceiling: Option<Entity>,
    /// Site-local solid boxes (x, height, z in site metres; z not flipped) with the level they stand on.
    solids: Vec<(Vec3, Vec3, i32)>,
}

struct EquipmentView {
    root: Entity,
    kind: String,
    site: String,
    level: i32,
    height: f32,
}

struct BeltView {
    root: Entity,
    shape: rules::BeltShape,
    lift: i32,
    site: String,
}

/// Drawn visuals by record ID.
#[derive(Resource, Default)]
pub struct SiteVisuals {
    sites: HashMap<String, Entity>,
    shells: HashMap<String, ShellView>,
    equipment: HashMap<String, EquipmentView>,
    belts: HashMap<String, BeltView>,
    items: HashMap<String, Entity>,
    revision: u64,
    seen: HashMap<String, i64>,
}

impl SiteVisuals {
    pub fn equipment_entity(&self, id: &str) -> Option<Entity> { self.equipment.get(id).map(|e| e.root) }
}

#[derive(Resource)]
pub struct SiteMaterials {
    pub wall: Handle<StandardMaterial>,
    pub floor: Handle<StandardMaterial>,
    pub roof: Handle<StandardMaterial>,
    pub ceiling: Handle<StandardMaterial>,
    pub elevator: Handle<StandardMaterial>,
    pub post: Handle<StandardMaterial>,
    pub cube: Handle<Mesh>,
    pub quad: Handle<Mesh>,
    icons: HashMap<String, Handle<StandardMaterial>>,
}

fn setup_materials(mut commands: Commands, mut materials: ResMut<Assets<StandardMaterial>>, mut meshes: ResMut<Assets<Mesh>>) {
    let plain = |materials: &mut Assets<StandardMaterial>, color: Color, roughness: f32| materials.add(StandardMaterial { base_color: color, perceptual_roughness: roughness, ..default() });
    commands.insert_resource(SiteMaterials {
        wall: plain(&mut materials, Color::srgb(0.86, 0.83, 0.77), 0.9),
        floor: plain(&mut materials, Color::srgb(0.62, 0.55, 0.47), 0.8),
        roof: plain(&mut materials, Color::srgb(0.33, 0.33, 0.36), 0.9),
        ceiling: plain(&mut materials, Color::srgb(0.93, 0.92, 0.89), 0.95),
        elevator: plain(&mut materials, Color::srgb(0.95, 0.75, 0.2), 0.6),
        post: plain(&mut materials, Color::srgb(0.35, 0.37, 0.4), 0.5),
        cube: meshes.add(Cuboid::new(1.0, 1.0, 1.0)),
        quad: meshes.add(Rectangle::new(1.0, 1.0)),
        icons: HashMap::new(),
    });
}

/// Site metres (x east, height, z north) to the site frame's local space.
fn local(x: f32, h: f32, z: f32) -> Vec3 { Vec3::new(x, h, -z) }

fn cell_centre(x: i32, z: i32) -> (f32, f32) { (x as f32 + 0.5, z as f32 + 0.5) }

/// Every site with a baseline and a lot is drawn.
fn choose_drawn(session: Res<Session>, mut drawn: ResMut<DrawnSites>) {
    let sites: Vec<String> = match session.joined() {
        Some(world) => world.views.keys().filter(|s| world.layout.lot_of_site(s).is_some()).cloned().collect(),
        None => vec![],
    };
    if drawn.sites != sites {
        drawn.sites = sites;
    }
}

fn site_root(commands: &mut Commands, visuals: &mut SiteVisuals, space: &Space, site: &str) -> Entity {
    if let Some(entity) = visuals.sites.get(site) {
        return *entity;
    }
    let origin = space.site_point(site, 0.0, 0.0, 0);
    let entity = commands.spawn((Name::new(format!("Site {site}")), Transform::from_translation(origin), Visibility::default())).id();
    visuals.sites.insert(site.to_string(), entity);
    entity
}

/// Spawns an art-kit model at a Unity-style pivot position (site metres) and yaw, scaled in its own axes.
fn kit(commands: &mut Commands, assets: &AssetServer, parent: Entity, path: &str, at: Vec3, yaw: f32, scale: Vec3) -> Option<Entity> {
    if path.is_empty() {
        return None;
    }
    let entity = commands.spawn((
        WorldAssetRoot(assets.load(GltfAssetLabel::Scene(0).from_asset(path.to_string()))),
        Transform { translation: local(at.x, at.y, at.z), rotation: yaw_rotation(yaw) * Quat::from_rotation_y(PI), scale },
    )).id();
    commands.entity(parent).add_child(entity);
    Some(entity)
}

fn cube(commands: &mut Commands, m: &SiteMaterials, parent: Entity, material: &Handle<StandardMaterial>, centre: Vec3, size: Vec3, name: &str) -> Entity {
    let entity = commands.spawn((Mesh3d(m.cube.clone()), MeshMaterial3d(material.clone()), Transform::from_translation(local(centre.x, centre.y, centre.z)).with_scale(size), Name::new(name.to_string()))).id();
    commands.entity(parent).add_child(entity);
    entity
}

/// A swinging door leaf: its closed yaw, hinge and doorway (scene space), and whether only staff open it.
#[derive(Component)]
pub struct DoorLeaf {
    yaw: f32,
    doorway: Vec3,
    service: bool,
    width: f32,
    angle: f32,
    target: f32,
}

/// Opens doors when near: the player, other players and employees (customers only through customer doors).
#[derive(Component)]
pub struct DoorOpener {
    pub customer: bool,
}

fn build_buildings(
    mut commands: Commands, session: Res<Session>, content: Res<Content>, space: Option<Res<Space>>, assets: Res<AssetServer>, m: Option<Res<SiteMaterials>>,
    mut visuals: ResMut<SiteVisuals>, drawn: Res<DrawnSites>,
) {
    let (Some(space), Some(m), Some(world)) = (space, m, session.joined()) else {
        clear_all(&mut commands, &mut visuals);
        return;
    };
    let mut changed = false;
    let mut keep: HashSet<String> = HashSet::new();
    for site in &drawn.sites {
        let Some(view) = world.view_of(site) else { continue };
        let revision = view.revision;
        let seen = visuals.seen.get(&format!("b:{site}")).copied();
        for building in view.buildings.iter().filter(|b| &b.site_id == site) {
            keep.insert(building.id.clone());
        }
        if seen == Some(revision) {
            continue;
        }
        visuals.seen.insert(format!("b:{site}"), revision);
        let root = site_root(&mut commands, &mut visuals, &space, site);
        for building in view.buildings.iter().filter(|b| &b.site_id == site) {
            let built = serde_json::to_string(building).unwrap_or_default();
            if visuals.shells.get(&building.id).is_some_and(|s| s.built == built) {
                continue;
            }
            if let Some(old) = visuals.shells.remove(&building.id) {
                commands.entity(old.root).despawn();
            }
            let shell = create_shell(&mut commands, &assets, &m, &content, root, building, built);
            visuals.shells.insert(building.id.clone(), ShellView { site: site.clone(), ..shell });
            changed = true;
        }
    }
    let stale: Vec<String> = visuals.shells.keys().filter(|k| !keep.contains(*k)).cloned().collect();
    for id in stale {
        if let Some(old) = visuals.shells.remove(&id) {
            commands.entity(old.root).despawn();
            changed = true;
        }
    }
    if changed {
        visuals.revision += 1;
    }
}

fn clear_all(commands: &mut Commands, visuals: &mut SiteVisuals) {
    if visuals.sites.is_empty() {
        return;
    }
    for (_, entity) in visuals.sites.drain() {
        commands.entity(entity).despawn();
    }
    *visuals = SiteVisuals { revision: visuals.revision + 1, ..default() };
}

fn create_shell(commands: &mut Commands, assets: &AssetServer, m: &SiteMaterials, content: &Content, site_root: Entity, b: &Building, built: String) -> ShellView {
    let root = commands.spawn((Name::new(format!("Building {}", b.id)), Transform::default(), Visibility::default())).id();
    commands.entity(site_root).add_child(root);
    let kit_walls = b.kind == goods::RESTAURANT_KIND && !content.0.styles.walls.is_empty();
    let mut solids = vec![];
    let mut storeys = vec![];
    let interior_centre = Vec2::new(b.cell_x as f32 + 1.0 + (b.width - 2) as f32 * 0.5, b.cell_z as f32 + 1.0 + (b.depth - 2) as f32 * 0.5);
    let interior_size = Vec2::new((b.width - 2) as f32, (b.depth - 2) as f32);
    for level in 0..b.floors.max(1) {
        let storey = commands.spawn((Name::new(format!("Storey {level}")), Transform::default(), Visibility::default())).id();
        commands.entity(root).add_child(storey);
        let base = level as f32 * LEVEL_HEIGHT;
        if kit_walls && level == 0 {
            restaurant_walls(commands, assets, content, storey, b);
            kit_wall_solids(b, &mut solids);
        } else {
            box_walls(commands, m, storey, b, level, &mut solids);
        }
        if level == 0 {
            if !kit_walls {
                for door in &b.doors {
                    let (x, z) = cell_centre(door.x, door.z);
                    cube(commands, m, storey, &m.wall, Vec3::new(x, (DOOR_HEIGHT + LEVEL_HEIGHT) * 0.5, z), Vec3::new(1.0, LEVEL_HEIGHT - DOOR_HEIGHT, 1.0), "Lintel");
                }
            }
            let inside: HashSet<(i32, i32)> = grid::interior_cells(b).into_iter().collect();
            if kit_walls || b.free_walls {
                cover(commands, m, storey, &m.floor, inside.iter().copied(), 0.005, 0.01);
                if kit_walls {
                    for &(x, z) in &inside {
                        let half = 0.5;
                        let west = if inside.contains(&(x - 1, z)) { 0.0 } else { half };
                        let east = if inside.contains(&(x + 1, z)) { 0.0 } else { half };
                        let south = if inside.contains(&(x, z - 1)) { 0.0 } else { half };
                        let north = if inside.contains(&(x, z + 1)) { 0.0 } else { half };
                        if west + east + south + north == 0.0 {
                            continue;
                        }
                        let (cx, cz) = cell_centre(x, z);
                        cube(commands, m, storey, &m.floor, Vec3::new(cx + (east - west) * 0.5, 0.005, cz + (north - south) * 0.5), Vec3::new(1.0 + west + east, 0.01, 1.0 + south + north), "Floor edge");
                    }
                }
            } else {
                cube(commands, m, storey, &m.floor, Vec3::new(interior_centre.x, 0.005, interior_centre.y), Vec3::new(interior_size.x, 0.01, interior_size.y), "Floor");
            }
        } else {
            cube(commands, m, storey, &m.floor, Vec3::new(interior_centre.x, base - SLAB * 0.5, interior_centre.y), Vec3::new(interior_size.x, SLAB, interior_size.y), "Slab");
            let min = Vec3::new(interior_centre.x - interior_size.x * 0.5, base - SLAB, interior_centre.y - interior_size.y * 0.5);
            let max = Vec3::new(interior_centre.x + interior_size.x * 0.5, base, interior_centre.y + interior_size.y * 0.5);
            solids.push((min, max, level));
        }
        if b.has_elevator() {
            let (x, z) = cell_centre(b.elevator_x, b.elevator_z);
            cube(commands, m, storey, &m.elevator, Vec3::new(x, base + 0.02, z), Vec3::new(0.9, 0.02, 0.9), "Elevator");
        }
        storeys.push(storey);
    }
    let mut roof = vec![];
    let top = b.floors.max(1) as f32 * LEVEL_HEIGHT;
    if b.free_walls {
        roof.extend(cover(commands, m, root, &m.roof, roof_cells(b), top + 0.1, 0.2));
    } else {
        roof.push(cube(commands, m, root, &m.roof, Vec3::new(b.cell_x as f32 + b.width as f32 * 0.5, top + 0.1, b.cell_z as f32 + b.depth as f32 * 0.5), Vec3::new(b.width as f32, 0.2, b.depth as f32), "Roof"));
    }
    let ceiling = (b.floors <= 1).then(|| {
        let ceiling = commands.spawn((Name::new("Ceiling"), Transform::default(), Visibility::Hidden)).id();
        commands.entity(root).add_child(ceiling);
        let cells: Vec<(i32, i32)> = if b.free_walls { roof_cells(b) } else { (b.cell_x..b.cell_x + b.width).flat_map(|x| (b.cell_z..b.cell_z + b.depth).map(move |z| (x, z))).collect() };
        // The ceiling shows only to someone inside, so it never shades the room the sun lights through the hidden roof.
        for entity in cover(commands, m, ceiling, &m.ceiling, cells, LEVEL_HEIGHT + 0.023, 0.05) {
            commands.entity(entity).insert(NotShadowCaster);
        }
        ceiling
    });
    ShellView { root, built, site: String::new(), storeys, roof, ceiling, solids }
}

fn roof_cells(b: &Building) -> Vec<(i32, i32)> {
    let inside: HashSet<(i32, i32)> = grid::interior_cells(b).into_iter().collect();
    let mut cells = inside.clone();
    for &(x, z) in &inside {
        for dx in -1..=1 {
            for dz in -1..=1 {
                if grid::is_partition(b, x + dx, z + dz) {
                    cells.insert((x + dx, z + dz));
                }
            }
        }
    }
    cells.into_iter().collect()
}

/// Flat boxes over cells, one per run along each row; the top stands at height + thickness / 2.
fn cover(commands: &mut Commands, m: &SiteMaterials, parent: Entity, material: &Handle<StandardMaterial>, cells: impl IntoIterator<Item = (i32, i32)>, height: f32, thickness: f32) -> Vec<Entity> {
    let mut rows: HashMap<i32, Vec<i32>> = HashMap::new();
    for (x, z) in cells {
        rows.entry(z).or_default().push(x);
    }
    let mut boxes = vec![];
    for (z, mut xs) in rows {
        xs.sort();
        xs.dedup();
        let mut start = 0;
        for index in 1..=xs.len() {
            if index < xs.len() && xs[index] == xs[index - 1] + 1 {
                continue;
            }
            let width = (xs[index - 1] - xs[start] + 1) as f32;
            boxes.push(cube(commands, m, parent, material, Vec3::new(xs[start] as f32 + width * 0.5, height, z as f32 + 0.5), Vec3::new(width, thickness, 1.0), "Cover"));
            start = index;
        }
    }
    boxes
}

/// Perimeter walls of a box shell storey: one box per run of wall cells, broken by ground doors.
fn box_walls(commands: &mut Commands, m: &SiteMaterials, parent: Entity, b: &Building, level: i32, solids: &mut Vec<(Vec3, Vec3, i32)>) {
    let right = b.cell_x + b.width - 1;
    let top = b.cell_z + b.depth - 1;
    let lines: [(Vec<(i32, i32)>, bool); 4] = [
        ((b.cell_x..b.cell_x + b.width).map(|x| (x, b.cell_z)).collect(), true),
        ((b.cell_x..b.cell_x + b.width).map(|x| (x, top)).collect(), true),
        ((b.cell_z + 1..b.cell_z + b.depth - 1).map(|z| (b.cell_x, z)).collect(), false),
        ((b.cell_z + 1..b.cell_z + b.depth - 1).map(|z| (right, z)).collect(), false),
    ];
    let mut cells: Vec<((i32, i32), bool)> = lines.into_iter().flat_map(|(cells, along)| cells.into_iter().map(move |c| (c, along)).chain(std::iter::once(((-1, -1), along)))).collect();
    if b.free_walls {
        // Free walls: every partition cell is its own box.
        cells = b.structures.iter().filter(|s| s.kind == goods::PARTITION_STRUCTURE).flat_map(|s| [((s.x, s.z), true), ((-1, -1), true)]).collect();
    }
    let base = level as f32 * LEVEL_HEIGHT;
    let mut run: Vec<(i32, i32)> = vec![];
    for ((x, z), along) in cells {
        if x >= 0 && (grid::is_wall(b, x, z, level) || (b.free_walls && level > 0)) {
            run.push((x, z));
            continue;
        }
        if run.is_empty() {
            continue;
        }
        let (w, d) = if along { (run.len() as f32, 1.0) } else { (1.0, run.len() as f32) };
        let centre = Vec3::new(run[0].0 as f32 + w * 0.5, base + LEVEL_HEIGHT * 0.5, run[0].1 as f32 + d * 0.5);
        let size = Vec3::new(w, LEVEL_HEIGHT, d);
        cube(commands, m, parent, &m.wall, centre, size, "Wall");
        solids.push((centre - size * 0.5, centre + size * 0.5, level));
        run.clear();
    }
}

fn wall_cells(b: &Building) -> HashSet<(i32, i32)> {
    let mut cells: HashSet<(i32, i32)> = b.structures.iter().filter(|s| s.kind == goods::PARTITION_STRUCTURE).map(|s| (s.x, s.z)).collect();
    for x in b.cell_x..b.cell_x + b.width {
        for z in b.cell_z..b.cell_z + b.depth {
            if grid::on_perimeter(b, x, z) {
                cells.insert((x, z));
            }
        }
    }
    cells
}

/// Thin collision on the kit wall's centreline: a straight cell is one box along its line; a corner, junction or end is a post
/// with a half-cell arm toward each linked cell.
fn kit_wall_solids(b: &Building, solids: &mut Vec<(Vec3, Vec3, i32)>) {
    let cells = wall_cells(b);
    let h = LEVEL_HEIGHT;
    let mut push = |centre: Vec3, size: Vec3| solids.push((centre - size * 0.5, centre + size * 0.5, 0));
    for &(x, z) in cells.iter().filter(|c| grid::is_wall(b, c.0, c.1, 0)) {
        let (cx, cz) = cell_centre(x, z);
        let centre = Vec3::new(cx, h * 0.5, cz);
        let linked: Vec<(i32, i32)> = DIRECTIONS.iter().copied().filter(|d| cells.contains(&(x + d.0, z + d.1))).collect();
        let straight_x = (linked.len() == 2 && linked.contains(&(1, 0)) && linked.contains(&(-1, 0))) || linked.is_empty();
        let straight_z = linked.len() == 2 && linked.contains(&(0, 1)) && linked.contains(&(0, -1));
        if straight_x || straight_z {
            push(centre, if straight_x { Vec3::new(1.0, h, WALL_THICKNESS) } else { Vec3::new(WALL_THICKNESS, h, 1.0) });
        } else {
            push(centre, Vec3::new(WALL_THICKNESS, h, WALL_THICKNESS));
            for (dx, dz) in linked {
                push(centre + Vec3::new(dx as f32, 0.0, dz as f32) * 0.25, if dx != 0 { Vec3::new(0.5, h, WALL_THICKNESS) } else { Vec3::new(WALL_THICKNESS, h, 0.5) });
            }
        }
    }
}

/// The restaurant art kit ground storey (Unity RestaurantShellModel.Build).
fn restaurant_walls(commands: &mut Commands, assets: &AssetServer, content: &Content, parent: Entity, b: &Building) {
    let styles = &content.0.styles;
    let cells = wall_cells(b);
    let is_door = |c: (i32, i32)| grid::is_door(b, c.0, c.1);
    let is_window = |c: (i32, i32)| grid::window_at(b, c.0, c.1).is_some();
    let style = |c: (i32, i32)| -> String {
        b.structures.iter().find(|s| s.kind == goods::PARTITION_STRUCTURE && s.x == c.0 && s.z == c.1).map(|s| s.style.clone()).filter(|s| !s.is_empty()).unwrap_or_else(|| b.wall_style.clone())
    };
    let centre = |c: (i32, i32)| {
        let (x, z) = cell_centre(c.0, c.1);
        Vec3::new(x, 0.0, z)
    };
    let between = |a: (i32, i32), c: (i32, i32)| (centre(a) + centre(c)) * 0.5;
    let mut ordered: Vec<(i32, i32)> = cells.iter().copied().collect();
    ordered.sort();
    let mut paired: HashSet<(i32, i32)> = HashSet::new();
    for &cell in &ordered {
        if is_door(cell) || is_window(cell) || paired.contains(&cell) {
            continue;
        }
        let Some(finish) = styles.wall(&style(cell)) else { continue };
        let linked: Vec<(i32, i32)> = DIRECTIONS.iter().copied().filter(|d| cells.contains(&(cell.0 + d.0, cell.1 + d.1))).collect();
        let halves: Vec<(i32, i32)> = linked.iter().copied().filter(|d| !is_door((cell.0 + d.0, cell.1 + d.1))).collect();
        let straight_x = linked.len() == 2 && linked.contains(&(1, 0)) && linked.contains(&(-1, 0));
        let straight_z = linked.len() == 2 && linked.contains(&(0, 1)) && linked.contains(&(0, -1));
        let yaw = if straight_x { 0.0 } else { 90.0 };
        if (straight_x || straight_z) && halves.len() == 2 {
            let step = if straight_x { (1, 0) } else { (0, 1) };
            let next = (cell.0 + step.0, cell.1 + step.1);
            let next_linked: Vec<(i32, i32)> = DIRECTIONS.iter().copied().filter(|d| cells.contains(&(next.0 + d.0, next.1 + d.1))).collect();
            let next_straight = cells.contains(&next) && !is_door(next) && !is_window(next) && !paired.contains(&next) && style(next) == style(cell)
                && next_linked.len() == 2 && next_linked.iter().all(|d| *d == (-step.0, -step.1) || *d == step)
                && !is_door((next.0 + step.0, next.1 + step.1));
            if next_straight && !finish.wall2m.is_empty() {
                paired.insert(cell);
                paired.insert(next);
                kit(commands, assets, parent, &finish.wall2m, between(cell, next), yaw, Vec3::ONE);
            } else {
                kit(commands, assets, parent, &finish.wall1m, centre(cell), yaw, Vec3::ONE);
            }
            continue;
        }
        if linked.is_empty() {
            kit(commands, assets, parent, &finish.wall1m, centre(cell), 0.0, Vec3::ONE);
            continue;
        }
        kit(commands, assets, parent, &finish.end, centre(cell), 0.0, Vec3::ONE);
        for (dx, dz) in halves {
            kit(commands, assets, parent, &finish.wall1m, centre(cell) + Vec3::new(dx as f32, 0.0, dz as f32) * 0.25, if dx != 0 { 0.0 } else { 90.0 }, Vec3::new(0.5, 1.0, 1.0));
        }
    }

    // Doors: single doorways, or a double one over two door cells in line.
    let mut doors: Vec<(i32, i32)> = ordered.iter().copied().filter(|c| is_door(*c)).collect();
    doors.sort();
    let mut done: HashSet<(i32, i32)> = HashSet::new();
    for cell in doors {
        if !done.insert(cell) {
            continue;
        }
        let along_x = cells.contains(&(cell.0 + 1, cell.1)) || cells.contains(&(cell.0 - 1, cell.1));
        let next = if along_x { (cell.0 + 1, cell.1) } else { (cell.0, cell.1 + 1) };
        let record = b.structures.iter().find(|s| s.kind == goods::DOOR_STRUCTURE && s.x == cell.0 && s.z == cell.1);
        let leaf = styles.door(record.map_or("", |r| r.style.as_str())).map(|d| d.leaf.clone()).unwrap_or_default();
        let service = record.is_some_and(|r| r.role == goods::SERVICE_DOOR_ROLE);
        let yaw = if along_x { 0.0 } else { 90.0 };
        // Unity local +X along the doorway, as a site-metre offset.
        let along = |distance: f32| {
            let r = yaw_rotation(yaw) * Vec3::X * distance;
            Vec3::new(r.x, 0.0, -r.z)
        };
        if cells.contains(&next) && is_door(next) && !done.contains(&next) {
            done.insert(next);
            let c = between(cell, next);
            kit(commands, assets, parent, &styles.double_doorway, c, yaw, Vec3::ONE);
            kit(commands, assets, parent, &styles.double_frame, c, yaw, Vec3::ONE);
            for (offset, turn) in [(DOUBLE_HINGE, 0.0), (-DOUBLE_HINGE, 180.0)] {
                if let Some(entity) = kit(commands, assets, parent, &leaf, c + along(offset) + Vec3::Y * 0.005, yaw + turn, Vec3::new(DOUBLE_LEAF_SCALE, 1.0, 1.0)) {
                    commands.entity(entity).insert(DoorLeaf { yaw: yaw + turn, doorway: c, service, width: LEAF_WIDTH * DOUBLE_LEAF_SCALE, angle: 0.0, target: 0.0 });
                }
            }
        } else {
            let c = centre(cell);
            kit(commands, assets, parent, &styles.single_doorway, c, yaw, Vec3::ONE);
            kit(commands, assets, parent, &styles.single_frame, c, yaw, Vec3::ONE);
            if let Some(entity) = kit(commands, assets, parent, &leaf, c + along(SINGLE_HINGE) + Vec3::Y * 0.005, yaw, Vec3::ONE) {
                commands.entity(entity).insert(DoorLeaf { yaw, doorway: c, service, width: LEAF_WIDTH, angle: 0.0, target: 0.0 });
            }
        }
    }

    for window in b.structures.iter().filter(|s| s.kind == goods::WINDOW_STRUCTURE) {
        let Some(style) = styles.window(&window.style) else { continue };
        let second = if window.axis == 0 { (window.x + 1, window.z) } else { (window.x, window.z + 1) };
        let c = between((window.x, window.z), second);
        let yaw = if window.axis == 0 { 0.0 } else { 90.0 };
        kit(commands, assets, parent, &style.bay, c, yaw, Vec3::ONE);
        kit(commands, assets, parent, &style.window, c + Vec3::Y * SILL_HEIGHT, yaw, Vec3::ONE);
    }
}

/// Where a placed piece's root stands (site metres) and the model's offset in the root (mounts), Unity EquipmentModel rules.
fn equipment_pose(content: &EquipmentContent, piece: &Equipment, view: &Snapshot, bounds: &Bounds) -> (Vec3, Quat, Transform) {
    let (w, d) = piece.footprint();
    let level_floor = piece.level as f32 * LEVEL_HEIGHT;
    let mut at = Vec3::new(piece.cell_x as f32 + w as f32 * 0.5, level_floor, piece.cell_z as f32 + d as f32 * 0.5);
    let rotation = grid_rotation(piece.rotation);
    let mut model = fitted(bounds, &content.model, Fit::Natural);
    let size = bounds.size(&content.model);
    // Rotated forward / back of the root, as site metres.
    let forward = {
        let f = rotation * Vec3::NEG_Z;
        Vec3::new(f.x, 0.0, -f.z)
    };
    match content.mount {
        Mount::Flush => model.translation.y -= size.y - FLUSH_LIFT,
        Mount::Wall => {
            model.translation += Vec3::new(0.0, -size.y * 0.5, -size.z * 0.5);
            at += forward * WALL_FACE + Vec3::Y * content.mount_height;
        }
        Mount::Ceiling => {
            model.translation.y -= size.y;
            at.y += LEVEL_HEIGHT - 0.005;
        }
        Mount::Backed => {
            model.translation.z += (content.depth as f32 * 0.5 - size.z * 0.5).max(0.0);
            let behind = grid::behind_cells(piece.cell_x, piece.cell_z, w, d, piece.rotation);
            if !behind.is_empty() && behind.iter().all(|c| grid::wall_at(view, &piece.site_id, c.0, c.1)) {
                at -= forward * (0.5 - WALL_FACE);
            }
        }
        Mount::Tabletop => {
            let table = view.equipment.iter().find(|x| x.site_id == piece.site_id && x.placed() && goods::is_table(x) && x.layer.is_empty() && grid::contains(x, piece.cell_x, piece.cell_z, 1, 1) && x.level == piece.level);
            if let Some(table) = table {
                at.y = level_floor + table_height(bounds, &table.kind);
            }
        }
        Mount::Floor => {}
    }
    (at, rotation, model)
}

fn table_height(bounds: &Bounds, kind: &str) -> f32 {
    let path = if kind == "table" { "models/gameplay/table.glb".to_string() } else { String::new() };
    if path.is_empty() { 0.75 } else { bounds.size(&path).y }
}

fn build_equipment(
    mut commands: Commands, session: Res<Session>, content: Res<Content>, space: Option<Res<Space>>, assets: Res<AssetServer>, bounds: Res<Bounds>,
    mut visuals: ResMut<SiteVisuals>, drawn: Res<DrawnSites>, mut transforms: Query<&mut Transform>,
) {
    let (Some(space), Some(world)) = (space, session.joined()) else { return };
    let mut keep: HashSet<String> = HashSet::new();
    let mut changed = false;
    for site in &drawn.sites {
        let Some(view) = world.view_of(site) else { continue };
        let pieces: Vec<&Equipment> = view.equipment.iter().filter(|x| x.placed() && &x.site_id == site).collect();
        keep.extend(pieces.iter().map(|x| x.id.clone()));
        let key = format!("e:{site}");
        if visuals.seen.get(&key) == Some(&view.revision) {
            continue;
        }
        visuals.seen.insert(key, view.revision);
        let root = site_root(&mut commands, &mut visuals, &space, site);
        for piece in pieces {
            let Some(kind) = content.0.equipment.get(&piece.kind) else { continue };
            let (at, rotation, model) = equipment_pose(kind, piece, view, &bounds);
            let transform = Transform::from_translation(local(at.x, at.y, at.z)).with_rotation(rotation);
            if let Some(existing) = visuals.equipment.get_mut(&piece.id).filter(|e| e.kind == piece.kind) {
                if let Ok(mut t) = transforms.get_mut(existing.root) {
                    if *t != transform {
                        *t = transform;
                        changed = true;
                    }
                }
                existing.level = piece.level;
                continue;
            }
            if let Some(old) = visuals.equipment.remove(&piece.id) {
                commands.entity(old.root).despawn();
            }
            let entity = commands.spawn((Name::new(format!("Equipment {}", piece.id)), transform, Visibility::default())).id();
            let child = commands.spawn((WorldAssetRoot(assets.load(GltfAssetLabel::Scene(0).from_asset(kind.model.clone()))), model)).id();
            commands.entity(entity).add_child(child);
            commands.entity(root).add_child(entity);
            let height = bounds.size(&kind.model).y;
            visuals.equipment.insert(piece.id.clone(), EquipmentView { root: entity, kind: piece.kind.clone(), site: site.clone(), level: piece.level, height });
            changed = true;
        }
    }
    let stale: Vec<String> = visuals.equipment.keys().filter(|k| !keep.contains(*k)).cloned().collect();
    for id in stale {
        if let Some(old) = visuals.equipment.remove(&id) {
            commands.entity(old.root).despawn();
            changed = true;
        }
    }
    if changed {
        visuals.revision += 1;
    }
}

/// The model and yaw (quarter turns) a belt is drawn with.
pub fn belt_model(shape: rules::BeltShape, direction: i32) -> (&'static str, i32) {
    match shape {
        rules::BeltShape::CurveFromLeft => (BELT_LEFT, rules::right_of(direction)),
        rules::BeltShape::CurveFromRight => (BELT_RIGHT, rules::left_of(direction)),
        rules::BeltShape::Straight => (BELT_STRAIGHT, direction),
    }
}

/// A belt model as children of `parent`: a flat belt, or a lift built from half belts and four posts.
pub fn spawn_belt_model(commands: &mut Commands, assets: &AssetServer, bounds: &Bounds, m: &SiteMaterials, parent: Entity, shape: rules::BeltShape, lift: i32, material: Option<Handle<StandardMaterial>>) {
    let scene = |path: &str| WorldAssetRoot(assets.load(GltfAssetLabel::Scene(0).from_asset(path.to_string())));
    if lift == 0 {
        let path = belt_model(shape, 0).0;
        let child = commands.spawn((scene(path), fitted(bounds, path, Fit::Natural))).id();
        commands.entity(parent).add_child(child);
        return;
    }
    let height = lift as f32 * LEVEL_HEIGHT;
    for (y, z) in [(0.0, 0.25), (height, -0.25)] {
        let mut t = fitted(bounds, BELT_STRAIGHT, Fit::Natural);
        t.scale.z *= 0.5;
        t.translation = t.translation * Vec3::new(1.0, 1.0, 0.5) + Vec3::new(0.0, y, z);
        let child = commands.spawn((scene(BELT_STRAIGHT), t)).id();
        commands.entity(parent).add_child(child);
    }
    let bottom = height.min(0.0);
    let tall = LEVEL_HEIGHT + BELT_SURFACE;
    let material = material.unwrap_or_else(|| m.post.clone());
    for (x, z) in [(-0.42, -0.42), (0.42, -0.42), (-0.42, 0.42), (0.42, 0.42)] {
        let post = commands.spawn((Mesh3d(m.cube.clone()), MeshMaterial3d(material.clone()), Transform::from_xyz(x, bottom + tall * 0.5, z).with_scale(Vec3::new(0.06, tall, 0.06)))).id();
        commands.entity(parent).add_child(post);
    }
}

/// Unity BeltPath.LocalPoint, as the belt root's local space (travel toward -Z).
pub fn belt_point(shape: rules::BeltShape, t: f32, lift: i32) -> Vec3 {
    let t = t.clamp(0.0, 1.0);
    let unity = if lift != 0 {
        let climb = ((t - 0.25) * 2.0).clamp(0.0, 1.0);
        let along = if t < 0.25 { t * 2.0 - 0.5 } else if t > 0.75 { (t - 0.75) * 2.0 } else { 0.0 };
        Vec3::new(0.0, BELT_SURFACE + lift as f32 * climb * LEVEL_HEIGHT, along)
    } else {
        let angle = t * PI * 0.5;
        match shape {
            rules::BeltShape::CurveFromLeft => Vec3::new(-0.5 + 0.5 * angle.sin(), BELT_SURFACE, 0.5 - 0.5 * angle.cos()),
            rules::BeltShape::CurveFromRight => Vec3::new(0.5 - 0.5 * angle.sin(), BELT_SURFACE, 0.5 - 0.5 * angle.cos()),
            rules::BeltShape::Straight => Vec3::new(0.0, BELT_SURFACE, t - 0.5),
        }
    };
    Vec3::new(unity.x, unity.y, -unity.z)
}

/// A riding item's target, in its site frame.
#[derive(Component)]
pub struct RidingItem {
    target: Vec3,
}

fn build_belts(
    mut commands: Commands, session: Res<Session>, content: Res<Content>, space: Option<Res<Space>>, assets: Res<AssetServer>, bounds: Res<Bounds>,
    m: Option<ResMut<SiteMaterials>>, mut materials: ResMut<Assets<StandardMaterial>>, mut visuals: ResMut<SiteVisuals>, drawn: Res<DrawnSites>, mut transforms: Query<&mut Transform>,
    mut riding: Query<&mut RidingItem>,
) {
    let (Some(space), Some(world), Some(mut m)) = (space, session.joined(), m) else { return };
    let mut keep: HashSet<String> = HashSet::new();
    let mut keep_items: HashSet<String> = HashSet::new();
    let mut changed = false;
    for site in &drawn.sites {
        let Some(view) = world.view_of(site) else { continue };
        let belts: Vec<&Belt> = view.belts.iter().filter(|b| &b.site_id == site).collect();
        keep.extend(belts.iter().map(|b| b.id.clone()));
        let belt_locations: HashMap<String, &Belt> = belts.iter().map(|b| (b.location_id(), *b)).collect();
        for lot in view.lots.iter().filter(|l| belt_locations.contains_key(&l.location_id)) {
            keep_items.insert(lot.id.clone());
        }
        let key = format!("l:{site}");
        if visuals.seen.get(&key) == Some(&view.revision) {
            continue;
        }
        visuals.seen.insert(key, view.revision);
        let root = site_root(&mut commands, &mut visuals, &space, site);
        let cells = rules::by_cell(belts.iter().copied());
        let mut shapes = HashMap::new();
        for belt in &belts {
            let shape = rules::shape_of(&cells, belt);
            shapes.insert(belt.id.clone(), shape);
            let (_, yaw) = belt_model(shape, belt.direction);
            let (cx, cz) = cell_centre(belt.cell_x, belt.cell_z);
            let transform = Transform::from_translation(local(cx, belt.level as f32 * LEVEL_HEIGHT, cz)).with_rotation(grid_rotation(yaw));
            if let Some(existing) = visuals.belts.get(&belt.id).filter(|v| v.shape == shape && v.lift == belt.lift) {
                if let Ok(mut t) = transforms.get_mut(existing.root) {
                    *t = transform;
                }
                continue;
            }
            if let Some(old) = visuals.belts.remove(&belt.id) {
                commands.entity(old.root).despawn();
            }
            let entity = commands.spawn((Name::new(format!("Belt {}", belt.id)), transform, Visibility::default())).id();
            spawn_belt_model(&mut commands, &assets, &bounds, &m, entity, shape, belt.lift, None);
            commands.entity(root).add_child(entity);
            visuals.belts.insert(belt.id.clone(), BeltView { root: entity, shape, lift: belt.lift, site: site.clone() });
            changed = true;
        }
        // Riding goods: camera-facing icons gliding toward the replicated position.
        for lot in view.lots.iter().filter(|l| belt_locations.contains_key(&l.location_id)) {
            let belt = belt_locations[&lot.location_id];
            let shape = shapes[&belt.id];
            let (_, yaw) = belt_model(shape, belt.direction);
            let t = lot.belt_position as f32 / rules::UNITS_PER_TILE as f32;
            let path_shape = shape;
            // The model is turned for corners; the path is in the travel direction's frame.
            let point = grid_rotation(belt.direction) * belt_point(path_shape, t, belt.lift);
            let _ = yaw;
            let (cx, cz) = cell_centre(belt.cell_x, belt.cell_z);
            let target = local(cx, belt.level as f32 * LEVEL_HEIGHT, cz) + point + Vec3::Y * ITEM_SIZE * 0.5;
            if let Some(entity) = visuals.items.get(&lot.id) {
                if let Ok(mut item) = riding.get_mut(*entity) {
                    item.target = target;
                }
                continue;
            }
            let icon = content.0.items.get(&lot.item_id).map(|i| i.icon.clone()).unwrap_or_default();
            let material = m.icons.entry(lot.item_id.clone()).or_insert_with(|| materials.add(StandardMaterial {
                base_color_texture: (!icon.is_empty()).then(|| assets.load(icon.clone())),
                alpha_mode: AlphaMode::Mask(0.5),
                unlit: true,
                double_sided: true,
                cull_mode: None,
                ..default()
            })).clone();
            let entity = commands.spawn((Mesh3d(m.quad.clone()), MeshMaterial3d(material), Transform::from_translation(target).with_scale(Vec3::splat(ITEM_SIZE)), RidingItem { target }, Name::new(format!("Item {}", lot.id)))).id();
            commands.entity(root).add_child(entity);
            visuals.items.insert(lot.id.clone(), entity);
        }
    }
    let stale: Vec<String> = visuals.belts.keys().filter(|k| !keep.contains(*k)).cloned().collect();
    for id in stale {
        if let Some(old) = visuals.belts.remove(&id) {
            commands.entity(old.root).despawn();
            changed = true;
        }
    }
    let stale: Vec<String> = visuals.items.keys().filter(|k| !keep_items.contains(*k)).cloned().collect();
    for id in stale {
        if let Some(entity) = visuals.items.remove(&id) {
            commands.entity(entity).despawn();
        }
    }
    if changed {
        visuals.revision += 1;
    }
}

/// Riding goods glide at belt speed toward their latest replicated position and face the camera.
fn glide_items(time: Res<Time>, camera: Query<&GlobalTransform, With<Camera3d>>, mut items: Query<(&RidingItem, &mut Transform, &ChildOf)>, parents: Query<&GlobalTransform, Without<Camera3d>>) {
    let speed = rules::UNITS_PER_SECOND as f32 / rules::UNITS_PER_TILE as f32 * 1.2;
    let camera = camera.iter().next().map(|c| c.translation());
    for (item, mut transform, child_of) in &mut items {
        let gap = item.target - transform.translation;
        let step = speed * time.delta_secs();
        transform.translation = if gap.length() <= step || gap.length() > 2.5 { item.target } else { transform.translation + gap.normalize() * step };
        if let (Some(camera), Ok(parent)) = (camera, parents.get(child_of.parent())) {
            let world = parent.translation() + transform.translation;
            let towards = camera - world;
            let flat = Vec3::new(towards.x, 0.0, towards.z);
            if flat.length_squared() > 1e-4 {
                transform.rotation = Quat::from_rotation_arc(Vec3::Z, flat.normalize());
            }
        }
    }
}

/// Collision and pick boxes in scene space, rebuilt when any visual changes.
fn rebuild_solids(session: Res<Session>, space: Option<Res<Space>>, visuals: Res<SiteVisuals>, mut solids: ResMut<Solids>, content: Res<Content>) {
    let (Some(space), Some(world)) = (space, session.joined()) else {
        if !solids.boxes.is_empty() || !solids.picks.is_empty() {
            *solids = Solids::default();
        }
        return;
    };
    if solids.revision == visuals.revision {
        return;
    }
    solids.revision = visuals.revision;
    let to_scene = |site: &str, min: Vec3, max: Vec3| {
        let a = space.site_point(site, min.x, min.z, 0) + Vec3::Y * min.y;
        let b = space.site_point(site, max.x, max.z, 0) + Vec3::Y * max.y;
        (a.min(b), a.max(b))
    };
    let mut boxes = vec![];
    for shell in visuals.shells.values() {
        for (min, max, _) in &shell.solids {
            boxes.push(to_scene(&shell.site, *min, *max));
        }
    }
    let mut picks = vec![];
    for (id, view) in &visuals.equipment {
        let Some(snapshot) = world.view_of(&view.site) else { continue };
        let Some(piece) = snapshot.equipment.iter().find(|x| &x.id == id) else { continue };
        let (w, d) = piece.footprint();
        let floor = piece.level as f32 * LEVEL_HEIGHT;
        let kind = content.0.equipment.get(&piece.kind);
        let mount = kind.map_or(Mount::Floor, |k| k.mount);
        let (y0, y1) = match mount {
            Mount::Flush => (floor, floor + 0.05),
            Mount::Ceiling => (floor + LEVEL_HEIGHT - view.height.min(1.0), floor + LEVEL_HEIGHT),
            Mount::Wall => {
                let h = kind.map_or(1.5, |k| k.mount_height);
                (floor + h - view.height * 0.5, floor + h + view.height * 0.5)
            }
            Mount::Tabletop => (floor + 0.7, floor + 0.7 + view.height.max(0.1)),
            _ => (floor, floor + view.height.clamp(0.3, 2.6)),
        };
        let min = Vec3::new(piece.cell_x as f32 + 0.04, y0, piece.cell_z as f32 + 0.04);
        let max = Vec3::new((piece.cell_x + w) as f32 - 0.04, y1, (piece.cell_z + d) as f32 - 0.04);
        let (smin, smax) = to_scene(&view.site, min, max);
        if piece.layer.is_empty() && mount != Mount::Flush {
            boxes.push((smin, smax));
        }
        picks.push(PickBox { pick: Pick::Equipment(id.clone()), site: view.site.clone(), level: piece.level, min: smin, max: smax });
    }
    for (id, view) in &visuals.belts {
        let Some(snapshot) = world.view_of(&view.site) else { continue };
        let Some(belt) = snapshot.belts.iter().find(|b| &b.id == id) else { continue };
        let floor = belt.level as f32 * LEVEL_HEIGHT;
        let top = if belt.lift > 0 { floor + LEVEL_HEIGHT + BELT_SURFACE } else { floor + BELT_SURFACE + 0.1 };
        let bottom = if belt.lift < 0 { floor - LEVEL_HEIGHT } else { floor };
        let (smin, smax) = to_scene(&view.site, Vec3::new(belt.cell_x as f32, bottom, belt.cell_z as f32), Vec3::new(belt.cell_x as f32 + 1.0, top, belt.cell_z as f32 + 1.0));
        picks.push(PickBox { pick: Pick::Belt(id.clone()), site: view.site.clone(), level: belt.level, min: smin, max: smax });
    }
    solids.boxes = boxes;
    solids.picks = picks;
}

/// The local avatar's site, building, level and cell (Unity BuildingPresenter.Update).
fn find_indoors(session: Res<Session>, space: Option<Res<Space>>, player: Query<&Transform, With<crate::player::Player>>, drawn: Res<DrawnSites>, mut indoors: ResMut<Indoors>) {
    let (Some(space), Some(world), Ok(player)) = (space, session.joined(), player.single()) else {
        if indoors.site.is_some() {
            *indoors = Indoors::default();
        }
        return;
    };
    let position = player.translation;
    let mut found = Indoors { site: Some(world.site.clone()), ..default() };
    for site in &drawn.sites {
        let Some(view) = world.view_of(site) else { continue };
        let (x, z) = space.to_site(site, position);
        let cell = (x.floor() as i32, z.floor() as i32);
        let level = space.level_at(site, position.y + 0.3);
        let inside = view.buildings.iter().find(|b| &b.site_id == site && grid::is_interior(b, cell.0, cell.1) && level < b.floors);
        let current = *site == world.site;
        if inside.is_none() && !(current && found.building.is_none()) {
            continue;
        }
        found.site = Some(site.clone());
        found.cell = cell;
        found.level = level;
        found.building = inside.map(|b| b.id.clone());
        found.ceiling = inside.map(|_| space.floor_height(site, level) + LEVEL_HEIGHT);
        if inside.is_some() {
            break;
        }
    }
    if indoors.site != found.site || indoors.building != found.building || indoors.level != found.level || indoors.cell != found.cell {
        *indoors = found;
    }
}

/// Roofs hide over the local building and the edited site; storeys above the local level hide; the ceiling shows inside in
/// the third-person view only.
fn show_storeys(
    visuals: Res<SiteVisuals>, indoors: Res<Indoors>, edited: Res<EditedSite>, camera: Query<&crate::camera::OrbitCamera>, mut visibility: Query<&mut Visibility>,
    session: Res<Session>,
) {
    let top_down = camera.iter().next().is_some_and(|c| c.top_down);
    let mut set = |entity: Entity, shown: bool| {
        if let Ok(mut v) = visibility.get_mut(entity) {
            let target = if shown { Visibility::Inherited } else { Visibility::Hidden };
            if *v != target {
                *v = target;
            }
        }
    };
    for (id, shell) in &visuals.shells {
        let local = indoors.building.as_deref() == Some(id.as_str());
        let editing = edited.0.as_deref() == Some(shell.site.as_str());
        for roof in &shell.roof {
            set(*roof, !local && !editing);
        }
        if let Some(ceiling) = shell.ceiling {
            set(ceiling, local && !editing && !top_down);
        }
        for (level, storey) in shell.storeys.iter().enumerate() {
            set(*storey, editing || !local || level as i32 <= indoors.level);
        }
    }
    let Some(world) = session.joined() else { return };
    let local_building = indoors.building.as_ref().and_then(|id| indoors.site.as_ref().and_then(|s| world.view_of(s)).and_then(|v| v.buildings.iter().find(|b| &b.id == id)));
    for (_, view) in &visuals.equipment {
        let hidden = local_building.is_some_and(|b| b.site_id == view.site && view.level > indoors.level);
        set(view.root, !hidden);
    }
    for (_, view) in &visuals.belts {
        let hidden = local_building.is_some_and(|b| b.site_id == view.site && indoors.level == 0 && view.lift == 0 && false);
        set(view.root, !hidden);
    }
}

/// Door leaves swing open (away from whoever came first) while an opener is near the doorway.
fn swing_doors(time: Res<Time>, mut leaves: Query<(&mut DoorLeaf, &mut Transform, &ChildOf)>, parents: Query<&GlobalTransform>, openers: Query<(&GlobalTransform, &DoorOpener)>) {
    for (mut leaf, mut transform, child_of) in &mut leaves {
        let Ok(parent) = parents.get(child_of.parent()) else { continue };
        let doorway = parent.transform_point(local(leaf.doorway.x, 0.0, leaf.doorway.z));
        let nearest = openers.iter().filter(|(_, o)| !(leaf.service && o.customer)).map(|(t, _)| t.translation()).filter(|p| Vec2::new(p.x - doorway.x, p.z - doorway.z).length() < DOOR_REACH && (p.y - doorway.y).abs() < 2.0)
            .min_by(|a, b| a.distance_squared(doorway).total_cmp(&b.distance_squared(doorway)));
        match nearest {
            None => leaf.target = 0.0,
            Some(opener) if leaf.target == 0.0 => {
                let hinge = parent.transform_point(transform.translation);
                let free = |sign: f32| hinge + yaw_rotation(leaf.yaw + sign * DOOR_OPEN_DEGREES) * Vec3::new(-leaf.width, 0.0, 0.0);
                leaf.target = if free(1.0).distance_squared(opener) >= free(-1.0).distance_squared(opener) { DOOR_OPEN_DEGREES } else { -DOOR_OPEN_DEGREES };
            }
            _ => {}
        }
        let step = DOOR_DEGREES_PER_SECOND * time.delta_secs();
        leaf.angle = if (leaf.target - leaf.angle).abs() <= step { leaf.target } else { leaf.angle + step * (leaf.target - leaf.angle).signum() };
        transform.rotation = yaw_rotation(leaf.yaw + leaf.angle) * Quat::from_rotation_y(PI);
    }
}
