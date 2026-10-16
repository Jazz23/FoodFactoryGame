// Models (the converted Unity art, glTF): their recorded bounds (tools/model_bounds.py), placing one fitted to a footprint the
// way Unity's EquipmentModel and WorldLayoutPresenter did (centred, standing on the floor, scaled along and across), and
// reading a model's meshes so many copies can be merged into one mesh per material and chunk, as the Unity map was. glTF
// models face +Z; a scene rotation faces -Z, so every placed model is turned half round first.
use std::collections::HashMap;

use bevy::asset::RenderAssetUsages;
use bevy::gltf::{Gltf, GltfMaterial, GltfMesh, GltfNode};
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

/// Loaded models' parts, for merging.
#[derive(Resource, Default)]
pub struct Library {
    pub gltfs: HashMap<String, Handle<Gltf>>,
    pub parts: HashMap<String, Vec<Part>>,
    materials: HashMap<AssetId<GltfMaterial>, Handle<StandardMaterial>>,
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
                let mut stack: Vec<(Handle<GltfNode>, Mat4)> = roots.into_iter().map(|r| (r, Mat4::IDENTITY)).collect();
                let mut complete = true;
                while let Some((node, parent)) = stack.pop() {
                    let Some(node) = nodes.get(&node) else {
                        complete = false;
                        continue;
                    };
                    let matrix = parent * node.transform.to_matrix();
                    if let Some(mesh) = node.mesh.as_ref().and_then(|m| meshes.get(m)) {
                        for primitive in &mesh.primitives {
                            let (material, name) = match &primitive.material {
                                Some(handle) => match gltf_materials.get(handle) {
                                    Some(source) => {
                                        let standard = self.materials.entry(handle.id()).or_insert_with(|| materials.add(bevy::pbr::gltf::standard_material_from_gltf_material(source))).clone();
                                        let name = gltf.named_materials.iter().find(|(_, h)| h.id() == handle.id()).map(|(n, _)| n.to_string()).unwrap_or_default();
                                        (standard, name)
                                    }
                                    None => {
                                        complete = false;
                                        continue;
                                    }
                                },
                                None => (materials.add(StandardMaterial::default()), String::new()),
                            };
                            parts.push(Part { mesh: primitive.mesh.clone(), material, material_name: name, transform: matrix });
                        }
                    }
                    for child in &node.children {
                        stack.push((child.clone(), matrix));
                    }
                }
                if complete {
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
            self.uvs.push(uvs.and_then(|u| u.get(i)).copied().unwrap_or([0.0, 0.0]));
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
