// The generated city as the player sees it (Unity WorldLayoutPresenter): the land in chunks with its covers (grass, downtown
// paving, industrial yard, river bank), water, lot paving and farm fields, road and rail tiles draped on the land, junction
// patches, bridges, traffic lights, stop signs and crossing signals, every building fitted to its footprint and facing, and
// trees, all merged into one mesh per material per 250 m chunk. Restaurant and factory shells stay separate so a drawn site
// can hide its map model, and restaurant awnings are tinted by owner. Presentation only: it reads the layout, never state.
use std::collections::HashMap;
use std::sync::Arc;

use bevy::gltf::{Gltf, GltfMaterial, GltfMesh, GltfNode};
use bevy::prelude::*;

use food_factory_bevy::world::{geometry, layout::*, random};

use crate::models::{repeat_texture, Bounds, Library, MergeBuffer};
use crate::session::{Session, SessionEvent};
use crate::space::Space;

pub struct MapPlugin;

impl Plugin for MapPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<MapState>().init_resource::<DrawnSites>().init_resource::<MapSolids>().add_systems(Update, (start_map, build_map, tint_awnings, hide_drawn_shells).chain());
    }
}

const CHUNK: f32 = 250.0;
const TERRAIN_CELL: f32 = 5.0;
const TERRAIN_CHUNK_CELLS: usize = 50;
const ROAD_CARVE: f32 = 0.15;
const RIVER_BED_BELOW_SURFACE: f32 = 1.6;
const RIVER_BANK_WIDTH: f32 = 10.0;
const TEXTURE_METRES: f32 = 8.0;
const FIELD_METRES: f32 = 12.0;
const ROAD_TILE: f32 = 10.0;
const RAIL_TILE: f32 = 6.0;
const APARTMENT_STOREY: f32 = 3.0;
const OFFICE_STOREY: f32 = 3.5;
const FOR_SALE_AWNING: Color = Color::srgb(0.95, 0.75, 0.15);
const COMPETITOR_AWNINGS: [Color; 6] = [
    Color::srgb(0.78, 0.22, 0.20), Color::srgb(0.20, 0.42, 0.78), Color::srgb(0.55, 0.30, 0.68), Color::srgb(0.85, 0.45, 0.15),
    Color::srgb(0.15, 0.60, 0.58), Color::srgb(0.65, 0.18, 0.40),
];

/// Sites the site presenter currently draws in full (it fills this; the map hides their shell models).
#[derive(Resource, Default)]
pub struct DrawnSites {
    pub sites: Vec<String>,
}

#[derive(Resource, Default)]
pub struct MapState {
    pub building: bool,
    pub built: bool,
    layout_hash: usize,
    root: Option<Entity>,
    pub shells: HashMap<String, Vec<Entity>>,
    pub awnings: HashMap<String, (Entity, Handle<StandardMaterial>)>,
    pub progress: String,
}

/// Scene-space collision boxes for scenery (houses, apartments, offices, barns, tree trunks, stop signs), bucketed by
/// `MapSolids::BUCKET` metres so the avatar only tests what is near it.
#[derive(Resource, Default)]
pub struct MapSolids {
    buckets: HashMap<(i32, i32), Vec<(Vec3, Vec3)>>,
}

impl MapSolids {
    const BUCKET: f32 = 16.0;

    fn key(x: f32, z: f32) -> (i32, i32) { ((x / Self::BUCKET).floor() as i32, (z / Self::BUCKET).floor() as i32) }

    fn add(&mut self, min: Vec3, max: Vec3) {
        let (a, b) = (Self::key(min.x, min.z), Self::key(max.x, max.z));
        for i in a.0..=b.0 {
            for j in a.1..=b.1 {
                self.buckets.entry((i, j)).or_default().push((min, max));
            }
        }
    }

    /// Boxes whose bucket lies within `reach` of `position` (a box spanning buckets may repeat).
    pub fn near(&self, position: Vec3, reach: f32) -> impl Iterator<Item = &(Vec3, Vec3)> {
        let (a, b) = (Self::key(position.x - reach, position.z - reach), Self::key(position.x + reach, position.z + reach));
        (a.0..=b.0).flat_map(move |i| (a.1..=b.1).map(move |j| (i, j))).filter_map(|k| self.buckets.get(&k)).flatten()
    }
}

/// Every world piece the map merges.
fn pieces() -> Vec<&'static str> {
    vec![
        "Road_Arterial", "Road_Arterial_Crosswalk", "Road_Local", "Road_Local_Crosswalk", "Road_Rural", "Road_Junction", "Rail_Track",
        "Rail_Crossing", "Rail_Signal", "Stop_Sign", "Traffic_Light_Arterial", "Traffic_Light_Local", "Bridge_Railing", "Bridge_Deck",
        "Bridge_Pier", "Foundation", "Tree_Broadleaf_a", "Tree_Broadleaf_b", "Tree_Conifer", "Tree_Poplar", "Tree_Bush", "House_Small_a",
        "House_Small_b", "House_Small_c", "House_Large_a", "House_Large_b", "House_Large_c", "Apartment_a_Ground", "Apartment_a_Middle",
        "Apartment_a_Roof", "Apartment_b_Ground", "Apartment_b_Middle", "Apartment_b_Roof", "Office_a_Ground", "Office_a_Middle",
        "Office_a_Roof", "Office_b_Ground", "Office_b_Middle", "Office_b_Roof", "Restaurant_a", "Restaurant_b", "Factory_a", "Factory_b",
        "Barn_a", "Barn_b", "Station",
    ]
}

fn piece_path(name: &str) -> String { format!("models/catalog/world/{name}.glb") }

#[derive(Resource)]
pub struct Covers {
    pub grass: Handle<StandardMaterial>,
    pub paving: Handle<StandardMaterial>,
    pub yard: Handle<StandardMaterial>,
    pub bank: Handle<StandardMaterial>,
    pub water: Handle<StandardMaterial>,
    pub wheat: Handle<StandardMaterial>,
    pub greens: Handle<StandardMaterial>,
}

fn textured(assets: &AssetServer, materials: &mut Assets<StandardMaterial>, name: &str, roughness: f32) -> Handle<StandardMaterial> {
    materials.add(StandardMaterial {
        base_color_texture: Some(repeat_texture(assets, &format!("Art/World/Textures/{name}.png"))),
        normal_map_texture: Some(repeat_texture(assets, &format!("Art/World/Textures/{name}_Normal.png"))),
        perceptual_roughness: roughness,
        ..default()
    })
}

fn start_map(
    mut commands: Commands, mut events: MessageReader<SessionEvent>, session: Res<Session>, mut state: ResMut<MapState>, assets: Res<AssetServer>,
    mut library: ResMut<Library>, mut materials: ResMut<Assets<StandardMaterial>>, mut map_solids: ResMut<MapSolids>,
) {
    let joined = events.read().any(|e| matches!(e, SessionEvent::Joined));
    let Some(world) = session.joined() else {
        if state.built || state.building {
            if let Some(root) = state.root.take() {
                commands.entity(root).despawn();
            }
            *state = MapState::default();
            *map_solids = MapSolids::default();
            commands.remove_resource::<Space>();
        }
        return;
    };
    let hash = Arc::as_ptr(&world.layout) as usize;
    if !joined && state.layout_hash == hash {
        return;
    }
    if let Some(root) = state.root.take() {
        commands.entity(root).despawn();
    }
    *state = MapState { building: true, layout_hash: hash, progress: "Loading the city...".into(), ..default() };
    commands.insert_resource(Space::new(&world.layout));
    for piece in pieces() {
        library.request(&assets, &piece_path(piece));
    }
    let water = materials.add(StandardMaterial {
        base_color: Color::srgba(0.18, 0.36, 0.42, 0.88),
        alpha_mode: AlphaMode::Blend,
        perceptual_roughness: 0.08,
        reflectance: 0.6,
        ..default()
    });
    commands.insert_resource(Covers {
        grass: textured(&assets, &mut materials, "Ground_Grass", 0.95),
        paving: textured(&assets, &mut materials, "Ground_Paving", 0.85),
        yard: textured(&assets, &mut materials, "Ground_Yard", 0.9),
        bank: textured(&assets, &mut materials, "Ground_Bank", 0.95),
        water,
        wheat: textured(&assets, &mut materials, "Field_Wheat", 0.95),
        greens: textured(&assets, &mut materials, "Field_Greens", 0.95),
    });
}

/// The map's land height rules (Unity WorldLayoutPresenter.Land and Carve).
struct Land<'a> {
    space: &'a Space,
    layout: &'a WorldLayout,
}

impl Land<'_> {
    fn height(&self, x: f32, z: f32) -> f32 { self.space.land(x, z) }

    fn nearest_river(&self, x: f32, z: f32) -> Option<(f32, &WorldRiver)> {
        let mut best: Option<(f32, &WorldRiver)> = None;
        for river in &self.layout.rivers {
            let reach = river.width as f32 / 2.0 + RIVER_BANK_WIDTH + 20.0;
            for pair in river.points.windows(2) {
                let (a, b) = (Vec2::new(pair[0].x as f32, pair[0].z as f32), Vec2::new(pair[1].x as f32, pair[1].z as f32));
                if x < a.x.min(b.x) - reach || x > a.x.max(b.x) + reach || z < a.y.min(b.y) - reach || z > a.y.max(b.y) + reach {
                    continue;
                }
                let p = Vec2::new(x, z);
                let ab = b - a;
                let t = ((p - a).dot(ab) / ab.length_squared().max(1e-6)).clamp(0.0, 1.0);
                let distance = p.distance(a + ab * t);
                if best.is_none_or(|b| distance < b.0) {
                    best = Some((distance, river));
                }
            }
        }
        best
    }

    fn carve(&self, x: f32, z: f32) -> f32 {
        let Some((distance, river)) = self.nearest_river(x, z) else { return 0.0 };
        let half = river.width as f32 / 2.0;
        let bed = river.surface_drop_cm as f32 / 100.0 + RIVER_BED_BELOW_SURFACE;
        if distance <= half {
            return bed;
        }
        if distance >= half + RIVER_BANK_WIDTH {
            return 0.0;
        }
        let t = (distance - half) / RIVER_BANK_WIDTH;
        bed * (1.0 - t * t * (3.0 - 2.0 * t))
    }
}

/// Merge buffers keyed by material and chunk; `collide` marks surfaces the avatar walks on.
#[derive(Default)]
struct Buffers {
    buffers: HashMap<(AssetId<StandardMaterial>, i32, i32), (Handle<StandardMaterial>, MergeBuffer)>,
}

impl Buffers {
    fn get(&mut self, material: &Handle<StandardMaterial>, x: f32, z: f32) -> &mut MergeBuffer {
        let key = (material.id(), (x / CHUNK).floor() as i32, (z / CHUNK).floor() as i32);
        &mut self.buffers.entry(key).or_insert_with(|| (material.clone(), MergeBuffer::default())).1
    }
}

/// Model-to-scene matrix of a piece: its bounds' centre-bottom at `origin`, its front (+Z in the model) facing Unity yaw.
fn standing(bounds: &Bounds, path: &str, origin: Vec3, yaw: f32, scale: Vec3) -> Mat4 {
    let (min, max) = bounds.get(path);
    let centre = Vec3::new((min.x + max.x) * 0.5, min.y, (min.z + max.z) * 0.5);
    let rotation = crate::space::yaw_rotation(yaw) * Quat::from_rotation_y(std::f32::consts::PI);
    Mat4::from_scale_rotation_translation(scale, rotation, origin) * Mat4::from_translation(-centre)
}

/// Model-to-scene matrix of a tile that runs from its local z = max end at `from` toward map direction `direction`.
fn tile(bounds: &Bounds, path: &str, from: Vec3, direction: Vec3, length: f32, width: Option<f32>) -> Mat4 {
    let (min, max) = bounds.get(path);
    let size = max - min;
    let forward = direction.normalize();
    let right = forward.cross(Vec3::Y).normalize();
    let sx = width.map_or(1.0, |w| w / size.x.max(0.001));
    let sz = length / size.z.max(0.001);
    let axis_x = right * sx;
    let axis_z = -forward * sz;
    let centre_x = (min.x + max.x) * 0.5;
    let translation = from - axis_x * centre_x - axis_z * max.z;
    Mat4::from_cols(axis_x.extend(0.0), Vec3::Y.extend(0.0), axis_z.extend(0.0), translation.extend(1.0))
}

#[derive(Clone, Copy, PartialEq)]
enum Drape {
    /// Every vertex follows the land under it.
    Surface,
    /// The whole piece stands on the land under its origin.
    Rigid,
    /// Placed as given.
    None,
}

#[allow(clippy::too_many_arguments)]
fn build_map(
    mut commands: Commands, session: Res<Session>, mut state: ResMut<MapState>, space: Option<Res<Space>>, covers: Option<Res<Covers>>,
    mut library: ResMut<Library>, bounds: Res<Bounds>, assets: Res<AssetServer>, gltfs: Res<Assets<Gltf>>, nodes: Res<Assets<GltfNode>>,
    gltf_meshes: Res<Assets<GltfMesh>>, gltf_materials: Res<Assets<GltfMaterial>>, mut materials: ResMut<Assets<StandardMaterial>>,
    mut meshes: ResMut<Assets<Mesh>>, images: Res<Assets<Image>>, mut map_solids: ResMut<MapSolids>,
) {
    if !state.building {
        return;
    }
    let (Some(space), Some(covers), Some(world)) = (space, covers, session.joined()) else { return };
    if !library.resolve(&gltfs, &nodes, &gltf_meshes, &gltf_materials, &mut materials, &assets) {
        return;
    }
    let _ = &images;
    let layout = world.layout.clone();
    let land = Land { space: &space, layout: &layout };
    let origin = space.origin;
    let scene = |x: f32, z: f32, h: f32| Vec3::new(x - origin.x, h - origin.y, -(z - origin.z));
    let mut buffers = Buffers::default();
    let mut colliders = MapSolids::default();
    // A site-metre box (x, z corners; floor and height in metres) in scene space.
    let mut collide = |x0: f32, z0: f32, x1: f32, z1: f32, floor: f32, height: f32| {
        let (a, b) = (scene(x0, z0, floor), scene(x1, z1, floor + height));
        colliders.add(a.min(b), a.max(b));
    };
    let root = commands.spawn((Name::new("City"), Transform::default(), Visibility::default())).id();

    // ------------------------------------------------------------------ land
    let half = layout.map_half_size as f32;
    let cells = (2.0 * half / TERRAIN_CELL).round() as usize;
    let size = cells + 1;
    let node_pos: HashMap<&str, (i32, i32)> = layout.nodes.iter().map(|n| (n.id.as_str(), (n.x, n.z))).collect();
    let mut carved = vec![false; size * size];
    let mut mark = |x0: f32, z0: f32, x1: f32, z1: f32| {
        let i0 = ((x0 + half) / TERRAIN_CELL).ceil().max(0.0) as usize;
        let i1 = (((x1 + half) / TERRAIN_CELL).floor() as usize).min(cells);
        let j0 = ((z0 + half) / TERRAIN_CELL).ceil().max(0.0) as usize;
        let j1 = (((z1 + half) / TERRAIN_CELL).floor() as usize).min(cells);
        for j in j0..=j1 {
            for i in i0..=i1 {
                carved[j * size + i] = true;
            }
        }
    };
    for road in &layout.roads {
        let (a, b) = (node_pos[road.from_id.as_str()], node_pos[road.to_id.as_str()]);
        let w = road.width as f32 / 2.0 + 0.8;
        mark(a.0.min(b.0) as f32 - w, a.1.min(b.1) as f32 - w, a.0.max(b.0) as f32 + w, a.1.max(b.1) as f32 + w);
    }
    for line in &layout.rails {
        for pair in line.points.windows(2) {
            let w = 2.8;
            mark(pair[0].x.min(pair[1].x) as f32 - w, pair[0].z.min(pair[1].z) as f32 - w, pair[0].x.max(pair[1].x) as f32 + w, pair[0].z.max(pair[1].z) as f32 + w);
        }
    }
    let mut heights = vec![0.0f32; size * size];
    for j in 0..size {
        for i in 0..size {
            let x = -half + i as f32 * TERRAIN_CELL;
            let z = -half + j as f32 * TERRAIN_CELL;
            let mut h = land.height(x, z) - land.carve(x, z);
            if carved[j * size + i] {
                h -= ROAD_CARVE;
            }
            if let Some(lot) = space.site_at(x, z) {
                h = lot.elevation - 0.05;
            }
            heights[j * size + i] = h;
        }
    }
    let district_cover: Vec<(&Vec<WorldRect>, &Handle<StandardMaterial>)> = layout.districts.iter().filter_map(|d| match d.kind {
        DistrictKind::Downtown => Some((&d.areas, &covers.paving)),
        DistrictKind::Industrial => Some((&d.areas, &covers.yard)),
        _ => None,
    }).collect();
    for cj in (0..cells).step_by(TERRAIN_CHUNK_CELLS) {
        for ci in (0..cells).step_by(TERRAIN_CHUNK_CELLS) {
            let ni = TERRAIN_CHUNK_CELLS.min(cells - ci);
            let nj = TERRAIN_CHUNK_CELLS.min(cells - cj);
            let mut by_cover: HashMap<AssetId<StandardMaterial>, (Handle<StandardMaterial>, MergeBuffer, HashMap<usize, u32>)> = HashMap::new();
            for j in 0..nj {
                for i in 0..ni {
                    let x = -half + (ci + i) as f32 * TERRAIN_CELL + TERRAIN_CELL * 0.5;
                    let z = -half + (cj + j) as f32 * TERRAIN_CELL + TERRAIN_CELL * 0.5;
                    let mut cover = &covers.grass;
                    for (areas, material) in &district_cover {
                        if areas.iter().any(|a| a.contains(x.floor() as i32, z.floor() as i32)) {
                            cover = material;
                        }
                    }
                    if let Some((distance, river)) = land.nearest_river(x, z) {
                        if distance < river.width as f32 / 2.0 + RIVER_BANK_WIDTH + 1.0 {
                            cover = &covers.bank;
                        }
                    }
                    let entry = by_cover.entry(cover.id()).or_insert_with(|| (cover.clone(), MergeBuffer::default(), HashMap::new()));
                    let mut vertex = |gi: usize, gj: usize, buffer: &mut MergeBuffer, map: &mut HashMap<usize, u32>| -> u32 {
                        let key = gj * size + gi;
                        if let Some(v) = map.get(&key) {
                            return *v;
                        }
                        let (vx, vz) = (-half + gi as f32 * TERRAIN_CELL, -half + gj as f32 * TERRAIN_CELL);
                        let v = buffer.push_vertex(scene(vx, vz, heights[key]), Vec3::Y, [vx / TEXTURE_METRES, vz / TEXTURE_METRES]);
                        map.insert(key, v);
                        v
                    };
                    let (gi, gj) = (ci + i, cj + j);
                    let a = vertex(gi, gj, &mut entry.1, &mut entry.2);
                    let b = vertex(gi + 1, gj, &mut entry.1, &mut entry.2);
                    let c = vertex(gi, gj + 1, &mut entry.1, &mut entry.2);
                    let d = vertex(gi + 1, gj + 1, &mut entry.1, &mut entry.2);
                    // Map +Z is scene -Z, so map-counter-clockwise is scene-clockwise; these face up in the scene.
                    entry.1.push_triangle(a, b, c);
                    entry.1.push_triangle(b, d, c);
                }
            }
            for (_, (material, mut buffer, _)) in by_cover {
                buffer.smooth_normals();
                let mesh = meshes.add(buffer.build());
                let child = commands.spawn((Mesh3d(mesh), MeshMaterial3d(material), Transform::default(), Name::new("Land"))).id();
                commands.entity(root).add_child(child);
            }
        }
    }

    // ------------------------------------------------------------------ water
    for river in &layout.rivers {
        let points: Vec<Vec2> = river.points.iter().map(|p| Vec2::new(p.x as f32, p.z as f32)).collect();
        let reach = river.width as f32 / 2.0 + RIVER_BANK_WIDTH * 0.7;
        let mut buffer = MergeBuffer::default();
        let mut along = 0.0;
        for index in 0..points.len() {
            let previous = points[index.saturating_sub(1)];
            let next = points[(index + 1).min(points.len() - 1)];
            let tangent = (next - previous).normalize_or_zero();
            let side = Vec2::new(-tangent.y, tangent.x) * reach;
            if index > 0 {
                along += points[index - 1].distance(points[index]);
            }
            let level = land.height(points[index].x, points[index].y) - river.surface_drop_cm as f32 / 100.0;
            let (l, r) = (points[index] + side, points[index] - side);
            let a = buffer.push_vertex(scene(l.x, l.y, level), Vec3::Y, [0.0, along / TEXTURE_METRES]);
            let b = buffer.push_vertex(scene(r.x, r.y, level), Vec3::Y, [2.0 * reach / TEXTURE_METRES, along / TEXTURE_METRES]);
            if index > 0 {
                let (pa, pb) = (a - 2, b - 2);
                let up = (Vec3::from(scene(l.x, l.y, 0.0)) - scene(points[index - 1].x + side.x, points[index - 1].y + side.y, 0.0)).cross(scene(r.x, r.y, 0.0) - scene(l.x, l.y, 0.0)).y;
                if up >= 0.0 {
                    buffer.push_triangle(pa, a, pb);
                    buffer.push_triangle(pb, a, b);
                } else {
                    buffer.push_triangle(pa, pb, a);
                    buffer.push_triangle(pb, b, a);
                }
            }
        }
        let mesh = meshes.add(buffer.build());
        let child = commands.spawn((Mesh3d(mesh), MeshMaterial3d(covers.water.clone()), Transform::default(), Name::new("Water"))).id();
        commands.entity(root).add_child(child);
    }

    // ------------------------------------------------------------------ lot paving and farm fields
    let grid = |from: f32, to: f32| {
        let mut values = vec![from];
        let mut value = (from / TERRAIN_CELL + 1.0).floor() * TERRAIN_CELL;
        while value < to - 0.01 {
            if value > from + 0.01 {
                values.push(value);
            }
            value += TERRAIN_CELL;
        }
        values.push(to);
        values
    };
    let mut surface = |buffers: &mut Buffers, material: &Handle<StandardMaterial>, x0: f32, z0: f32, x1: f32, z1: f32, lift: f32, uv_metres: f32| {
        let xs = grid(x0, x1);
        let zs = grid(z0, z1);
        let buffer = buffers.get(material, (x0 + x1) * 0.5, (z0 + z1) * 0.5);
        for zi in 0..zs.len() - 1 {
            for xi in 0..xs.len() - 1 {
                let corners = [(xs[xi], zs[zi]), (xs[xi + 1], zs[zi]), (xs[xi + 1], zs[zi + 1]), (xs[xi], zs[zi + 1])];
                let points = corners.map(|(x, z)| scene(x, z, land.height(x, z) + lift));
                let uvs = corners.map(|(x, z)| [x / uv_metres, z / uv_metres]);
                buffer.push_quad(points, uvs);
            }
        }
    };
    for lot in &layout.lots {
        surface(&mut buffers, &covers.paving, lot.x as f32, lot.z as f32, (lot.x + lot.width) as f32, (lot.z + lot.depth) as f32, -0.02, TEXTURE_METRES);
    }
    for farm in layout.buildings.iter().filter(|b| b.category == BuildingCategory::Farm) {
        let material = if variant(farm) == "a" { &covers.wheat } else { &covers.greens };
        surface(&mut buffers, material, farm.x as f32, farm.z as f32, (farm.x + farm.width) as f32, (farm.z + farm.depth) as f32, 0.05, FIELD_METRES);
    }

    // ------------------------------------------------------------------ merged pieces
    let merge = |buffers: &mut Buffers, name: &str, matrix: Mat4, drape: Drape, meshes: &Assets<Mesh>| {
        let path = piece_path(name);
        let Some(parts) = library.parts.get(&path) else { return };
        let anchor = matrix.transform_point3(Vec3::ZERO);
        let (amx, _, amz) = space.to_map(anchor);
        let rigid = land.height(amx, amz) - origin.y;
        for part in parts {
            let Some(mesh) = meshes.get(&part.mesh) else { continue };
            let total = matrix * part.transform;
            let buffer = buffers.get(&part.material, amx, amz);
            match drape {
                Drape::Surface => buffer.append(mesh, total, &|p: Vec3| {
                    let (mx, mz, _) = space.to_map(p);
                    land.height(mx, mz) - origin.y
                }),
                Drape::Rigid => buffer.append(mesh, total, &|_| rigid),
                Drape::None => buffer.append(mesh, total, &|_| 0.0),
            }
        }
    };
    let meshes_ref: &Assets<Mesh> = &meshes;
    let flat = |x: f32, z: f32| scene(x, z, origin.y);

    // Roads: tiles trimmed at junctions by the crossing roads' half widths, crosswalk tiles at junction ends.
    let mut along_z_half: HashMap<&str, f32> = HashMap::new();
    let mut along_x_half: HashMap<&str, f32> = HashMap::new();
    for road in &layout.roads {
        let (a, b) = (node_pos[road.from_id.as_str()], node_pos[road.to_id.as_str()]);
        let halves = if a.0 == b.0 { &mut along_z_half } else { &mut along_x_half };
        for id in [road.from_id.as_str(), road.to_id.as_str()] {
            let entry = halves.entry(id).or_insert(0.0);
            *entry = entry.max(road.width as f32 / 2.0);
        }
    }
    for node in &layout.nodes {
        if let (Some(x), Some(z)) = (along_z_half.get(node.id.as_str()), along_x_half.get(node.id.as_str())) {
            let matrix = Mat4::from_scale_rotation_translation(Vec3::new(2.0 * x, 1.0, 2.0 * z), Quat::IDENTITY, flat(node.x as f32, node.z as f32));
            let (min, max) = bounds.get(&piece_path("Road_Junction"));
            let centre = Vec3::new((min.x + max.x) * 0.5, 0.0, (min.z + max.z) * 0.5);
            merge(&mut buffers, "Road_Junction", matrix * Mat4::from_translation(-centre), Drape::Surface, meshes_ref);
        }
    }
    for road in &layout.roads {
        let (a, b) = (node_pos[road.from_id.as_str()], node_pos[road.to_id.as_str()]);
        let along_z = a.0 == b.0;
        let crossing = if along_z { &along_x_half } else { &along_z_half };
        let trim_a = crossing.get(road.from_id.as_str()).copied().unwrap_or(0.0);
        let trim_b = crossing.get(road.to_id.as_str()).copied().unwrap_or(0.0);
        let start = Vec2::new(a.0 as f32, a.1 as f32);
        let end = Vec2::new(b.0 as f32, b.1 as f32);
        let direction = (end - start).normalize();
        let length = start.distance(end) - trim_a - trim_b;
        if length < 0.5 {
            continue;
        }
        let kind = match road.kind {
            RoadKind::Arterial => "Arterial",
            RoadKind::Local => "Local",
            RoadKind::Rural => "Rural",
        };
        let plain = format!("Road_{kind}");
        let city = road.kind != RoadKind::Rural;
        let count = ((length / ROAD_TILE).round() as i32).max(1);
        let tile_length = length / count as f32;
        for index in 0..count {
            let mut from = start + direction * (trim_a + index as f32 * tile_length);
            let mut tile_direction = direction;
            let mut name = plain.clone();
            if city && index == 0 && trim_a > 0.0 {
                name = format!("Road_{kind}_Crosswalk");
            } else if city && index == count - 1 && trim_b > 0.0 {
                name = format!("Road_{kind}_Crosswalk");
                from += direction * tile_length;
                tile_direction = -direction;
            }
            let scene_direction = Vec3::new(tile_direction.x, 0.0, -tile_direction.y);
            let matrix = tile(&bounds, &piece_path(&name), flat(from.x, from.y), scene_direction, tile_length, None);
            merge(&mut buffers, &name, matrix, Drape::Surface, meshes_ref);
        }
    }

    // Bridges: deck tiles and parapets over each crossing.
    let widths: HashMap<&str, f32> = layout.roads.iter().map(|r| (r.id.as_str(), r.width as f32)).chain(layout.rails.iter().map(|r| (r.id.as_str(), r.width as f32 - 3.0))).collect();
    for bridge in &layout.bridges {
        let Some(&width) = widths.get(bridge.carries_id.as_str()) else { continue };
        let start = Vec2::new(bridge.from.x as f32, bridge.from.z as f32);
        let end = Vec2::new(bridge.to.x as f32, bridge.to.z as f32);
        let length = start.distance(end);
        if length < 1.0 {
            continue;
        }
        let direction = (end - start) / length;
        let scene_direction = Vec3::new(direction.x, 0.0, -direction.y);
        let side = scene_direction.cross(Vec3::Y).normalize();
        let count = ((length / ROAD_TILE).round() as i32).max(1);
        let tile_length = length / count as f32;
        for index in 0..count {
            let from = start + direction * (index as f32 * tile_length);
            let origin_point = flat(from.x, from.y);
            merge(&mut buffers, "Bridge_Deck", tile(&bounds, &piece_path("Bridge_Deck"), origin_point, scene_direction, tile_length, Some(width)), Drape::Surface, meshes_ref);
            for sign in [-1.0f32, 1.0] {
                let edge = origin_point + side * sign * (width / 2.0 + 0.15);
                merge(&mut buffers, "Bridge_Railing", tile(&bounds, &piece_path("Bridge_Railing"), edge, scene_direction, tile_length, None), Drape::Surface, meshes_ref);
            }
        }
    }

    // Rail track tiles, with crossing panels where roads cross at grade.
    let roads_by_id: HashMap<&str, &RoadSegment> = layout.roads.iter().map(|r| (r.id.as_str(), r)).collect();
    for line in &layout.rails {
        for pair in line.points.windows(2) {
            let start = Vec2::new(pair[0].x as f32, pair[0].z as f32);
            let end = Vec2::new(pair[1].x as f32, pair[1].z as f32);
            let length = start.distance(end);
            let direction = (end - start) / length.max(0.001);
            let scene_direction = Vec3::new(direction.x, 0.0, -direction.y);
            let mut here: Vec<(f32, f32)> = layout.crossings.iter().filter(|c| c.rail_id == line.id).filter_map(|c| {
                let at = (Vec2::new(c.x as f32, c.z as f32) - start).dot(direction);
                let half = roads_by_id.get(c.road_id.as_str())?.width as f32 / 2.0 + 0.2;
                (at > 0.0 && at < length).then_some((at, half))
            }).collect();
            here.sort_by(|a, b| a.0.partial_cmp(&b.0).unwrap_or(std::cmp::Ordering::Equal));
            let mut gap_start = 0.0;
            for k in 0..=here.len() {
                let gap_end = if k < here.len() { here[k].0 - here[k].1 } else { length };
                let gap = gap_end - gap_start;
                if gap > 0.5 {
                    let count = ((gap / RAIL_TILE).round() as i32).max(1);
                    let tile_length = gap / count as f32;
                    for step in 0..count {
                        let from = start + direction * (gap_start + step as f32 * tile_length);
                        merge(&mut buffers, "Rail_Track", tile(&bounds, &piece_path("Rail_Track"), flat(from.x, from.y), scene_direction, tile_length, None), Drape::Surface, meshes_ref);
                    }
                }
                if k < here.len() {
                    let (at, half) = here[k];
                    let from = start + direction * (at - half);
                    merge(&mut buffers, "Rail_Crossing", tile(&bounds, &piece_path("Rail_Crossing"), flat(from.x, from.y), scene_direction, 2.0 * half, None), Drape::Surface, meshes_ref);
                    gap_start = at + half;
                }
            }
        }
    }

    // Traffic lights and stop signs on the right of each controlled approach.
    let mut by_node: HashMap<&str, Vec<&RoadSegment>> = HashMap::new();
    for road in &layout.roads {
        by_node.entry(road.from_id.as_str()).or_default().push(road);
        by_node.entry(road.to_id.as_str()).or_default().push(road);
    }
    let on_lot = |x: f32, z: f32| space.site_at(x, z).is_some();
    for node in &layout.nodes {
        if node.control == JunctionControl::None {
            continue;
        }
        let Some(segments) = by_node.get(node.id.as_str()) else { continue };
        let kinds: Vec<RoadKind> = segments.iter().map(|s| s.kind).collect();
        let here = Vec2::new(node.x as f32, node.z as f32);
        for road in segments {
            let far = node_pos[if road.from_id == node.id { road.to_id.as_str() } else { road.from_id.as_str() }];
            let d = (here - Vec2::new(far.0 as f32, far.1 as f32)).normalize();
            let right = Vec2::new(d.y, -d.x);
            let cross = segments.iter().filter(|x| x.id != road.id).filter(|x| {
                let (a, b) = (node_pos[x.from_id.as_str()], node_pos[x.to_id.as_str()]);
                Vec2::new((b.0 - a.0) as f32, (b.1 - a.1) as f32).normalize().dot(d).abs() < 0.5
            }).map(|x| x.width as f32 / 2.0).fold(4.0f32, f32::max);
            let kerb = if road.kind == RoadKind::Rural { road.width as f32 / 2.0 + 0.9 } else { road.width as f32 / 2.0 - 0.7 };
            let (name, back) = if node.control == JunctionControl::TrafficLight {
                (if road.kind == RoadKind::Arterial { "Traffic_Light_Arterial" } else { "Traffic_Light_Local" }, cross + 1.2)
            } else if food_factory_bevy::world::terrain::junction_stops(node.control, road.kind, kinds.iter().copied()) {
                ("Stop_Sign", cross + if road.kind == RoadKind::Rural { 2.0 } else { 5.0 })
            } else {
                continue;
            };
            let position = here - d * back + right * kerb;
            if on_lot(position.x, position.y) {
                continue;
            }
            // Facing the traffic: the sign's front looks back along the approach.
            let yaw = (-d.x).atan2(-d.y).to_degrees();
            let ground = land.height(position.x, position.y);
            if name == "Stop_Sign" {
                collide(position.x - 0.15, position.y - 0.15, position.x + 0.15, position.y + 0.15, ground - 0.2, bounds.size(&piece_path(name)).y + 0.2);
            }
            merge(&mut buffers, name, standing(&bounds, &piece_path(name), scene(position.x, position.y, ground), yaw, Vec3::ONE), Drape::None, meshes_ref);
        }
    }

    // ------------------------------------------------------------------ buildings
    let mut shells: HashMap<String, Vec<Entity>> = HashMap::new();
    let mut awnings = HashMap::new();
    let mut pending: Vec<(String, MergeBuffer, Handle<StandardMaterial>, bool)> = vec![];
    let foundation = |buffers: &mut Buffers, x: f32, z: f32, width: f32, depth: f32, floor: f32| {
        let lowest = [land.height(x, z), land.height(x + width, z), land.height(x, z + depth), land.height(x + width, z + depth), land.height(x + width / 2.0, z + depth / 2.0)].into_iter().fold(f32::MAX, f32::min);
        let drop = floor - lowest + 0.4;
        if drop < 0.45 {
            return;
        }
        let path = piece_path("Foundation");
        let (min, max) = bounds.get(&path);
        let size = max - min;
        let matrix = Mat4::from_scale_rotation_translation(Vec3::new(width / size.x, drop / size.y, depth / size.z), Quat::IDENTITY, scene(x + width / 2.0, z + depth / 2.0, floor - drop))
            * Mat4::from_translation(-Vec3::new((min.x + max.x) * 0.5, min.y, (min.z + max.z) * 0.5));
        merge(buffers, "Foundation", matrix, Drape::None, meshes_ref);
    };
    for building in &layout.buildings {
        let floor = building.elevation_cm as f32 / 100.0;
        let centre = scene(building.x as f32 + building.width as f32 / 2.0, building.z as f32 + building.depth as f32 / 2.0, floor);
        let yaw = building.yaw_degrees() as f32;
        let north_south = matches!(building.facing, Facing::North | Facing::South);
        let along = if north_south { building.width } else { building.depth } as f32;
        let deep = if north_south { building.depth } else { building.width } as f32;
        let variant = variant(building);
        if building.category != BuildingCategory::Farm {
            foundation(&mut buffers, building.x as f32, building.z as f32, building.width as f32, building.depth as f32, floor);
        }
        match building.category {
            BuildingCategory::Farm => {
                let door = building.doors[0];
                let (sx, sz) = geometry::step(building.facing);
                let x = door.x as f32 + 0.5 - sx as f32 * 9.5;
                let z = door.z as f32 + 0.5 - sz as f32 * 9.5;
                let barn_floor = [land.height(x - 8.0, z - 8.0), land.height(x + 8.0, z - 8.0), land.height(x - 8.0, z + 8.0), land.height(x + 8.0, z + 8.0)].into_iter().fold(f32::MIN, f32::max) + 0.1;
                foundation(&mut buffers, x - 8.0, z - 8.0, 16.0, 16.0, barn_floor);
                let name = format!("Barn_{variant}");
                collide(x - 8.0, z - 8.0, x + 8.0, z + 8.0, barn_floor - 0.5, bounds.size(&piece_path(&name)).y + 0.5);
                merge(&mut buffers, &name, standing(&bounds, &piece_path(&name), scene(x, z, barn_floor), yaw, Vec3::ONE), Drape::None, meshes_ref);
            }
            BuildingCategory::Apartment | BuildingCategory::Office => {
                let (prefix, storey) = if building.category == BuildingCategory::Apartment { (format!("Apartment_{variant}"), APARTMENT_STOREY) } else { (format!("Office_{variant}"), OFFICE_STOREY) };
                let ground = format!("{prefix}_Ground");
                let size = bounds.size(&piece_path(&ground));
                let scale = Vec3::new(along / size.x, 1.0, deep / size.z);
                let floors = building.floors.max(2);
                merge(&mut buffers, &ground, standing(&bounds, &piece_path(&ground), centre, yaw, scale), Drape::None, meshes_ref);
                let middle = format!("{prefix}_Middle");
                for level in 1..floors {
                    merge(&mut buffers, &middle, standing(&bounds, &piece_path(&middle), centre + Vec3::Y * level as f32 * storey, yaw, scale), Drape::None, meshes_ref);
                }
                let roof = format!("{prefix}_Roof");
                let height = floors as f32 * storey + bounds.size(&piece_path(&roof)).y;
                collide(building.x as f32, building.z as f32, (building.x + building.width) as f32, (building.z + building.depth) as f32, floor - 0.5, height + 0.5);
                merge(&mut buffers, &roof, standing(&bounds, &piece_path(&roof), centre + Vec3::Y * floors as f32 * storey, yaw, scale), Drape::None, meshes_ref);
            }
            _ => {
                let large = building.model_key.starts_with("house/large");
                let name = match building.category {
                    BuildingCategory::Restaurant => format!("Restaurant_{variant}"),
                    BuildingCategory::Factory => format!("Factory_{variant}"),
                    BuildingCategory::Station => "Station".to_string(),
                    _ => if large { format!("House_Large_{variant}") } else { format!("House_Small_{variant}") },
                };
                let path = piece_path(&name);
                let size = bounds.size(&path);
                let scale = Vec3::new(along / size.x, 1.0, deep / size.z);
                let matrix = standing(&bounds, &path, centre, yaw, scale);
                if !building.is_shell() {
                    if building.category != BuildingCategory::Station {
                        collide(building.x as f32, building.z as f32, (building.x + building.width) as f32, (building.z + building.depth) as f32, floor - 0.5, size.y + 0.5);
                    }
                    merge(&mut buffers, &name, matrix, Drape::None, meshes_ref);
                    continue;
                }
                // A shell is its own entities, so a drawn site hides it; a restaurant's awning is tinted separately.
                let Some(parts) = library.parts.get(&path) else { continue };
                let mut body = MergeBuffer::default();
                let mut body_material: Option<Handle<StandardMaterial>> = None;
                for part in parts {
                    let Some(mesh) = meshes_ref.get(&part.mesh) else { continue };
                    if part.material_name == "WG_Awning" {
                        let mut awning = MergeBuffer::default();
                        awning.append(mesh, matrix * part.transform, &|_| 0.0);
                        pending.push((building.id.clone(), awning, part.material.clone(), true));
                        continue;
                    }
                    if body_material.as_ref().is_some_and(|m| m.id() != part.material.id()) {
                        pending.push((building.id.clone(), std::mem::take(&mut body), body_material.clone().unwrap(), false));
                    }
                    body_material = Some(part.material.clone());
                    body.append(mesh, matrix * part.transform, &|_| 0.0);
                }
                if let Some(material) = body_material.filter(|_| !body.is_empty()) {
                    pending.push((building.id.clone(), body, material, false));
                }
            }
        }
    }

    // ------------------------------------------------------------------ trees
    let tree_pieces = |kind: TreeKind| -> &'static [&'static str] {
        match kind {
            TreeKind::Broadleaf => &["Tree_Broadleaf_a", "Tree_Broadleaf_b"],
            TreeKind::Conifer => &["Tree_Conifer"],
            TreeKind::Poplar => &["Tree_Poplar"],
            TreeKind::Bush => &["Tree_Bush"],
        }
    };
    // Older layouts planted street trees on the sidewalk, in the walkers' way: a tree on any road's width (sidewalks included) is left out.
    let on_road = |x: f32, z: f32| layout.roads.iter().any(|road| {
        let (a, b) = (node_pos[road.from_id.as_str()], node_pos[road.to_id.as_str()]);
        let half = road.width as f32 / 2.0 + 0.3;
        let (across, along, from, to) = if a.0 == b.0 { (x - a.0 as f32, z, a.1.min(b.1), a.1.max(b.1)) } else { (z - a.1 as f32, x, a.0.min(b.0), a.0.max(b.0)) };
        across.abs() < half && along >= from as f32 - half && along <= to as f32 + half
    });
    for tree in &layout.trees {
        let (x, z) = (tree.x as f32 + 0.5, tree.z as f32 + 0.5);
        if on_lot(x, z) || on_road(x, z) {
            continue;
        }
        let hash = random::mix(((tree.x as u32 as u64) << 32) ^ tree.z as u32 as u64);
        let options = tree_pieces(tree.kind);
        let name = options[(hash % options.len() as u64) as usize];
        let yaw = ((hash >> 8) % 360) as f32;
        let ground = land.height(x, z);
        let scale = tree.scale as f32 / 100.0;
        // The trunk (or a bush's whole body) blocks; the canopy overhead does not.
        let half = if tree.kind == TreeKind::Bush { 0.5 * scale } else { 0.25 * scale };
        collide(x - half, z - half, x + half, z + half, ground - 0.2, (bounds.size(&piece_path(name)).y * scale).min(4.0) + 0.2);
        merge(&mut buffers, name, standing(&bounds, &piece_path(name), scene(x, z, ground), yaw, Vec3::splat(scale)), Drape::None, meshes_ref);
    }

    // ------------------------------------------------------------------ flush
    // A shell is its own entities, so a drawn site hides it; a restaurant's awning is tinted separately.
    for (building_id, buffer, material, awning) in pending {
        let material = if awning {
            let copy = materials.get(&material).cloned().unwrap_or_default();
            materials.add(copy)
        } else {
            material
        };
        let entity = commands.spawn((Mesh3d(meshes.add(buffer.build())), MeshMaterial3d(material.clone()), Transform::default(), Name::new(format!("Shell {building_id}")))).id();
        commands.entity(root).add_child(entity);
        if awning {
            awnings.insert(building_id, (entity, material));
        } else {
            shells.entry(building_id).or_insert_with(Vec::new).push(entity);
        }
    }
    let total: usize = buffers.buffers.values().map(|(_, b)| b.vertex_count()).sum();
    info!("[Map] {} merged meshes, {total} vertices, map half size {half} m.", buffers.buffers.len());
    for (_, (material, buffer)) in buffers.buffers {
        if buffer.is_empty() {
            continue;
        }
        let mesh = meshes.add(buffer.build());
        let child = commands.spawn((Mesh3d(mesh), MeshMaterial3d(material), Transform::default())).id();
        commands.entity(root).add_child(child);
    }
    *map_solids = colliders;
    state.shells = shells;
    state.awnings = awnings;
    state.root = Some(root);
    state.building = false;
    state.built = true;
    state.progress.clear();
}

/// The model variant letter: from the premade model key, or chosen per ID for generated shells and stations.
fn variant(building: &WorldBuilding) -> String {
    if let Some(dash) = building.model_key.rfind('-') {
        if dash + 1 < building.model_key.len() {
            return building.model_key[dash + 1..].to_string();
        }
    }
    if random::fnv1a(&building.id) % 2 == 0 { "a".into() } else { "b".into() }
}

/// Restaurant awnings: hidden when a company owns the lot (or it is the player's start), yellow for sale, else a competitor colour.
fn tint_awnings(session: Res<Session>, state: Res<MapState>, mut materials: ResMut<Assets<StandardMaterial>>, mut visibility: Query<&mut Visibility>, mut last: Local<String>) {
    let Some(world) = session.joined() else { return };
    let Some(view) = world.view() else { return };
    let key = format!("{}", view.properties.len());
    if *last == key || !state.built {
        return;
    }
    *last = key;
    for (building_id, (entity, material)) in &state.awnings {
        let Some(building) = world.layout.building(building_id) else { continue };
        let owned = view.properties.iter().any(|p| p.lot_id == WorldLot::id_for(building_id)) || building.ownership == Ownership::Player;
        if let Ok(mut shown) = visibility.get_mut(*entity) {
            *shown = if owned { Visibility::Hidden } else { Visibility::Inherited };
        }
        let colour = if building.ownership == Ownership::ForSale { FOR_SALE_AWNING } else { COMPETITOR_AWNINGS[(random::fnv1a(building_id) % COMPETITOR_AWNINGS.len() as u64) as usize] };
        if let Some(mut m) = materials.get_mut(material) {
            m.base_color = colour;
        }
    }
}

/// A shell model is hidden while its site is drawn (the site presenter draws the real building).
fn hide_drawn_shells(state: Res<MapState>, drawn: Res<DrawnSites>, session: Res<Session>, mut visibility: Query<&mut Visibility>) {
    if !drawn.is_changed() && !state.is_changed() {
        return;
    }
    let Some(world) = session.joined() else { return };
    for (building_id, entities) in &state.shells {
        let Some(site) = world.layout.lots.iter().find(|l| &l.building_id == building_id).map(|l| l.site_id.clone()) else { continue };
        let hidden = drawn.sites.iter().any(|s| *s == site) && world.views.contains_key(&site);
        for entity in entities {
            if let Ok(mut shown) = visibility.get_mut(*entity) {
                *shown = if hidden { Visibility::Hidden } else { Visibility::Inherited };
            }
        }
    }
}
