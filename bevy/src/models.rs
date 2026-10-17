// Models (the converted Unity art, glTF): their recorded bounds (tools/model_bounds.py), placing one fitted to a footprint the
// way Unity's EquipmentModel and WorldLayoutPresenter did (centred, standing on the floor, scaled along and across), and
// reading a model's meshes so many copies can be merged into one mesh per material and chunk, as the Unity map was. glTF
// models face +Z; a scene rotation faces -Z, so every placed model is turned half round first.
use std::collections::HashMap;

use bevy::asset::RenderAssetUsages;
use bevy::gltf::{Gltf, GltfMaterial, GltfMesh, GltfNode};
use bevy::image::{ImageAddressMode, ImageLoaderSettings, ImageSampler, ImageSamplerDescriptor};
use bevy::mesh::{Indices, PrimitiveTopology, VertexAttributeValues};
use bevy::prelude::*;
use bevy::world_serialization::WorldAssetRoot;

pub struct ModelsPlugin;

impl Plugin for ModelsPlugin {
    fn build(&self, app: &mut App) {
        app.insert_resource(Bounds::bundled()).init_resource::<Library>();
    }
}

#[derive(Resource)]
pub struct Bounds(pub HashMap<String, (Vec3, Vec3)>);

impl Bounds {
    fn bundled() -> Self {
        #[derive(serde::Deserialize)]
        struct Entry {
            min: [f32; 3],
            max: [f32; 3],
        }
        let text = include_str!("../assets/catalog/bounds.json");
        let entries: HashMap<String, Entry> = serde_json::from_str(text).unwrap_or_default();
        Self(entries.into_iter().map(|(k, e)| (k, (Vec3::from_array(e.min), Vec3::from_array(e.max)))).collect())
    }

    pub fn get(&self, path: &str) -> (Vec3, Vec3) { self.0.get(path).copied().unwrap_or((Vec3::splat(-0.5), Vec3::splat(0.5))) }

    pub fn size(&self, path: &str) -> Vec3 {
        let (min, max) = self.get(path);
        max - min
    }
}

/// How a model meets its target.
#[derive(Clone, Copy, Debug)]
pub enum Fit {
    /// Authored size, centred on the footprint and standing on the floor (machines, furniture).
    Natural,
    /// Scaled along X and Z to the footprint, height kept (generated buildings).
    Footprint(Vec2),
    /// Uniformly scaled.
    Uniform(f32),
    /// Exactly this size on every axis.
    Exact(Vec3),
}

/// The child transform that places a model's bounds at a parent's origin (centred in X and Z, its base at y = 0), turned
/// to face the parent's forward.
pub fn fitted(bounds: &Bounds, path: &str, fit: Fit) -> Transform {
    let (min, max) = bounds.get(path);
    let size = (max - min).max(Vec3::splat(0.001));
    let scale = match fit {
        Fit::Natural => Vec3::ONE,
        Fit::Footprint(footprint) => Vec3::new(footprint.x / size.x, 1.0, footprint.y / size.z),
        Fit::Uniform(s) => Vec3::splat(s),
        Fit::Exact(target) => target / size,
    };
    let centre = Vec3::new((min.x + max.x) * 0.5, min.y, (min.z + max.z) * 0.5);
    let rotation = Quat::from_rotation_y(std::f32::consts::PI);
    Transform { translation: rotation * (-centre * scale), rotation, scale }
}

/// Spawns a model as a child entity of `parent`, fitted.
pub fn spawn_model(commands: &mut Commands, assets: &AssetServer, bounds: &Bounds, parent: Entity, path: &str, fit: Fit) -> Entity {
    let transform = fitted(bounds, path, fit);
    let child = commands.spawn((WorldAssetRoot(assets.load(GltfAssetLabel::Scene(0).from_asset(path.to_string()))), transform)).id();
    commands.entity(parent).add_child(child);
    child
}

/// One mesh of a model in the model's space, with its material.
#[derive(Clone)]
pub struct Part {
    pub mesh: Handle<Mesh>,
    pub material: Handle<StandardMaterial>,
    pub material_name: String,
    pub transform: Mat4,
}

/// A texture that repeats across UVs beyond 0..1 (tiled world surfaces).
pub fn repeat_texture(assets: &AssetServer, path: &str) -> Handle<Image> {
    assets.load_builder().with_settings(|settings: &mut ImageLoaderSettings| {
        settings.sampler = ImageSampler::Descriptor(ImageSamplerDescriptor {
            address_mode_u: ImageAddressMode::Repeat,
            address_mode_v: ImageAddressMode::Repeat,
            ..ImageSamplerDescriptor::linear()
        });
    }).load(path.to_string())
}

/// A Unity world material (tools/export_world_materials.py): the world models carry only its name.
#[derive(serde::Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
struct WorldMaterial {
    base_color: [f32; 4],
    base_map: Option<WorldTexture>,
    normal_map: String,
    metallic: f32,
    smoothness: f32,
    smoothness_from_albedo: bool,
    transparent: bool,
    emission: Option<[f32; 4]>,
}

#[derive(serde::Deserialize, Clone)]
struct WorldTexture {
    path: String,
    scale: [f32; 2],
    offset: [f32; 2],
}

fn world_materials() -> HashMap<String, WorldMaterial> { serde_json::from_str(include_str!("../assets/catalog/world_materials.json")).unwrap_or_default() }

fn world_material(material: &WorldMaterial, assets: &AssetServer) -> StandardMaterial {
    let [r, g, b, a] = material.base_color;
    let texture = material.base_map.as_ref().filter(|t| !t.path.is_empty());
    StandardMaterial {
        base_color: Color::srgba(r, g, b, a),
        base_color_texture: texture.map(|t| repeat_texture(assets, &t.path)),
        normal_map_texture: (!material.normal_map.is_empty()).then(|| repeat_texture(assets, &material.normal_map)),
        uv_transform: texture.map_or(bevy::math::Affine2::IDENTITY, |t| bevy::math::Affine2::from_scale_angle_translation(Vec2::from_array(t.scale), 0.0, Vec2::from_array(t.offset))),
        metallic: material.metallic,
        // Unity read smoothness from the albedo alpha for these; the slider alone would make them glossy.
        perceptual_roughness: if material.smoothness_from_albedo { 0.78 } else { (1.0 - material.smoothness).clamp(0.05, 1.0) },
        alpha_mode: if material.transparent { AlphaMode::Blend } else { AlphaMode::Opaque },
        emissive: material.emission.map_or(LinearRgba::BLACK, |[r, g, b, _]| LinearRgba::rgb(r, g, b) * 4.0),
        ..default()
    }
}

/// Loaded models' parts, for merging.
#[derive(Resource, Default)]
pub struct Library {
    pub gltfs: HashMap<String, Handle<Gltf>>,
    pub parts: HashMap<String, Vec<Part>>,
    /// Parts under each named root node, relative to the root's position (a model file holding several models, such as the vehicles).
    pub roots: HashMap<(String, String), Vec<Part>>,
    materials: HashMap<AssetId<GltfMaterial>, Handle<StandardMaterial>>,
    world: Option<HashMap<String, WorldMaterial>>,
}

impl Library {
    pub fn request(&mut self, assets: &AssetServer, path: &str) {
        if !self.gltfs.contains_key(path) {
            self.gltfs.insert(path.to_string(), assets.load(path.to_string()));
        }
    }

    /// True once every requested model has its parts read.
    #[allow(clippy::too_many_arguments)]
    pub fn resolve(&mut self, gltfs: &Assets<Gltf>, nodes: &Assets<GltfNode>, meshes: &Assets<GltfMesh>, gltf_materials: &Assets<GltfMaterial>, materials: &mut Assets<StandardMaterial>, assets: &AssetServer) -> bool {
        let mut ready = true;
        let pending: Vec<(String, Handle<Gltf>)> = self.gltfs.iter().filter(|(k, _)| !self.parts.contains_key(*k)).map(|(k, v)| (k.clone(), v.clone())).collect();
        for (path, handle) in pending {
            if assets.is_loaded_with_dependencies(handle.id()) || gltfs.get(&handle).is_some() {
                let Some(gltf) = gltfs.get(&handle) else {
                    ready = false;
                    continue;
                };
                let mut parts = vec![];
                let roots: Vec<Handle<GltfNode>> = {
                    let children: std::collections::HashSet<AssetId<GltfNode>> = gltf.nodes.iter().filter_map(|n| nodes.get(n)).flat_map(|n| n.children.iter().map(|c| c.id())).collect();
                    gltf.nodes.iter().filter(|n| !children.contains(&n.id())).cloned().collect()
                };
                let root_names: HashMap<AssetId<GltfNode>, String> = gltf.named_nodes.iter().filter(|(_, h)| roots.iter().any(|r| r.id() == h.id())).map(|(n, h)| (h.id(), n.to_string())).collect();
                let mut stack: Vec<(Handle<GltfNode>, Mat4, Option<(String, Option<Mat4>)>)> = roots.into_iter().map(|r| {
                    let name = root_names.get(&r.id()).cloned();
                    (r, Mat4::IDENTITY, name.map(|n| (n, None)))
                }).collect();
                let mut named: HashMap<String, Vec<Part>> = HashMap::new();
                let mut complete = true;
                while let Some((node_handle, parent, root)) = stack.pop() {
                    let node = node_handle;
                    let Some(node) = nodes.get(&node) else {
                        complete = false;
                        continue;
                    };
                    let matrix = parent * node.transform.to_matrix();
                    // Within a named root, parts are kept relative to the root's own transform.
                    let root = root.map(|(name, base)| (name, Some(base.unwrap_or_else(|| Mat4::from_translation(-matrix.w_axis.truncate())))));
                    if let Some(mesh) = node.mesh.as_ref().and_then(|m| meshes.get(m)) {
                        for primitive in &mesh.primitives {
                            let (material, name) = match &primitive.material {
                                Some(handle) => match gltf_materials.get(handle) {
                                    Some(source) => {
                                        let name = gltf.named_materials.iter().find(|(_, h)| h.id() == handle.id()).map(|(n, _)| n.to_string()).unwrap_or_default();
                                        let world = self.world.get_or_insert_with(world_materials).get(&name).cloned();
                                        let standard = self.materials.entry(handle.id()).or_insert_with(|| {
                                            let mut material = match &world {
                                                Some(world) => world_material(world, assets),
                                                None => bevy::pbr::gltf::standard_material_from_gltf_material(source),
                                            };
                                            crate::textures::add_detail(&mut material, &name, assets);
                                            materials.add(material)
                                        }).clone();
                                        (standard, name)
                                    }
                                    None => {
                                        complete = false;
                                        continue;
                                    }
                                },
                                None => (materials.add(StandardMaterial::default()), String::new()),
                            };
                            let part = Part { mesh: primitive.mesh.clone(), material, material_name: name, transform: matrix };
                            if let Some((root_name, base)) = &root {
                                named.entry(root_name.clone()).or_default().push(Part { transform: base.unwrap_or(Mat4::IDENTITY) * matrix, ..part.clone() });
                            }
                            parts.push(part);
                        }
                    }
                    for child in &node.children {
                        stack.push((child.clone(), matrix, root.clone()));
                    }
                }
                if complete {
                    for (name, parts) in named {
                        self.roots.insert((path.clone(), name), parts);
                    }
                    self.parts.insert(path, parts);
                } else {
                    ready = false;
                }
            } else {
                ready = false;
            }
        }
        ready
    }
}

/// Accumulates transformed copies of mesh parts into one mesh per material.
#[derive(Default)]
pub struct MergeBuffer {
    positions: Vec<[f32; 3]>,
    normals: Vec<[f32; 3]>,
    uvs: Vec<[f32; 2]>,
    indices: Vec<u32>,
}

impl MergeBuffer {
    pub fn is_empty(&self) -> bool { self.indices.is_empty() }

    pub fn vertex_count(&self) -> usize { self.positions.len() }

    /// Appends a mesh transformed by `matrix`; `lift` adjusts each vertex height after transforming (draping).
    pub fn append(&mut self, mesh: &Mesh, matrix: Mat4, lift: &dyn Fn(Vec3) -> f32) {
        let Some(VertexAttributeValues::Float32x3(positions)) = mesh.attribute(Mesh::ATTRIBUTE_POSITION) else { return };
        let normals = match mesh.attribute(Mesh::ATTRIBUTE_NORMAL) {
            Some(VertexAttributeValues::Float32x3(n)) => Some(n),
            _ => None,
        };
        let uvs = match mesh.attribute(Mesh::ATTRIBUTE_UV_0) {
            Some(VertexAttributeValues::Float32x2(u)) => Some(u),
            _ => None,
        };
        let base = self.positions.len() as u32;
        let normal_matrix = Mat3::from_mat4(matrix).inverse().transpose();
        let flip = Mat3::from_mat4(matrix).determinant() < 0.0;
        for (i, p) in positions.iter().enumerate() {
            let mut point = matrix.transform_point3(Vec3::from_array(*p));
            point.y += lift(point);
            self.positions.push(point.to_array());
            let n = normals.and_then(|n| n.get(i)).map(|n| (normal_matrix * Vec3::from_array(*n)).normalize_or_zero()).unwrap_or(Vec3::Y);
            self.normals.push(n.to_array());
            // A model without UVs gets box-projected ones in its own space, so surface detail has something to follow.
            let model_normal = normals.and_then(|n| n.get(i)).copied().unwrap_or([0.0, 1.0, 0.0]);
            let projected = || {
                let (x, y, z) = (model_normal[0].abs(), model_normal[1].abs(), model_normal[2].abs());
                if y >= x && y >= z { [p[0], p[2]] } else if x >= z { [p[2], p[1]] } else { [p[0], p[1]] }
            };
            self.uvs.push(uvs.and_then(|u| u.get(i)).copied().unwrap_or_else(projected));
        }
        let mut push = |a: u32, b: u32, c: u32| {
            if flip {
                self.indices.extend([base + a, base + c, base + b]);
            } else {
                self.indices.extend([base + a, base + b, base + c]);
            }
        };
        match mesh.indices() {
            Some(Indices::U16(list)) => list.chunks_exact(3).for_each(|t| push(t[0] as u32, t[1] as u32, t[2] as u32)),
            Some(Indices::U32(list)) => list.chunks_exact(3).for_each(|t| push(t[0], t[1], t[2])),
            None => (0..positions.len() as u32 / 3).for_each(|t| push(3 * t, 3 * t + 1, 3 * t + 2)),
        }
    }

    /// Adds a raw triangle list (generated surfaces).
    pub fn push_quad(&mut self, corners: [Vec3; 4], uvs: [[f32; 2]; 4]) {
        let base = self.positions.len() as u32;
        let normal = (corners[1] - corners[0]).cross(corners[2] - corners[0]).normalize_or_zero();
        let normal = if normal.y < 0.0 { -normal } else { normal };
        for (corner, uv) in corners.iter().zip(uvs) {
            self.positions.push(corner.to_array());
            self.normals.push(normal.to_array());
            self.uvs.push(uv);
        }
        // Counter-clockwise seen from above faces up.
        if (corners[1] - corners[0]).cross(corners[2] - corners[0]).y >= 0.0 {
            self.indices.extend([base, base + 1, base + 2, base, base + 2, base + 3]);
        } else {
            self.indices.extend([base, base + 2, base + 1, base, base + 3, base + 2]);
        }
    }

    pub fn push_vertex(&mut self, position: Vec3, normal: Vec3, uv: [f32; 2]) -> u32 {
        self.positions.push(position.to_array());
        self.normals.push(normal.to_array());
        self.uvs.push(uv);
        self.positions.len() as u32 - 1
    }

    pub fn push_triangle(&mut self, a: u32, b: u32, c: u32) { self.indices.extend([a, b, c]); }

    pub fn build(self) -> Mesh {
        let mut mesh = Mesh::new(PrimitiveTopology::TriangleList, RenderAssetUsages::RENDER_WORLD | RenderAssetUsages::MAIN_WORLD);
        mesh.insert_attribute(Mesh::ATTRIBUTE_POSITION, self.positions);
        mesh.insert_attribute(Mesh::ATTRIBUTE_NORMAL, self.normals);
        mesh.insert_attribute(Mesh::ATTRIBUTE_UV_0, self.uvs);
        mesh.insert_indices(Indices::U32(self.indices));
        mesh
    }

    /// Recomputes smooth normals from the triangles (generated land).
    pub fn smooth_normals(&mut self) {
        let mut normals = vec![Vec3::ZERO; self.positions.len()];
        for t in self.indices.chunks_exact(3) {
            let (a, b, c) = (Vec3::from_array(self.positions[t[0] as usize]), Vec3::from_array(self.positions[t[1] as usize]), Vec3::from_array(self.positions[t[2] as usize]));
            let n = (b - a).cross(c - a);
            for &i in t {
                normals[i as usize] += n;
            }
        }
        self.normals = normals.into_iter().map(|n| {
            let n = n.normalize_or_zero();
            (if n.y < 0.0 { -n } else { n }).to_array()
        }).collect();
    }
}
