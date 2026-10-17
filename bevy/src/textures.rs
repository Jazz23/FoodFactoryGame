// Surface detail for flat-coloured models: the tileable textures baked by tools/blender_surface_textures.py
// (assets/Art/Generated) are laid over model materials, chosen from the material's name (wood, metal, fabric, plaster...).
// The greyscale detail multiplies the material's colour, so the palette stays, and its normal map makes the surface catch
// the light. Spawned model parts (machines, furniture, clothing) read both through a second UV set this module adds:
// box-projected from the mesh's own positions (a fixed number of repeats per metre) with matching tangents, so the detail
// follows the surface whatever the model's own UVs hold, including palette-atlas UVs (whose palette is kept and only
// gains relief). Glass, skin, lamps and other emitters stay plain. Merged map meshes get the colour detail on their own UVs.
use std::collections::{HashMap, HashSet};

use bevy::gltf::GltfMaterialName;
use bevy::image::{ImageAddressMode, ImageLoaderSettings, ImageSampler, ImageSamplerDescriptor};
use bevy::mesh::{UvChannel, VertexAttributeValues};
use bevy::prelude::*;

use crate::models::repeat_texture;

pub struct SurfaceTexturesPlugin;

impl Plugin for SurfaceTexturesPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<Dressed>().add_systems(PostUpdate, dress_models);
    }
}

/// Detail repeats per metre of the projected UV set.
const REPEATS_PER_METRE: f32 = 1.5;

/// Materials already looked at (and whether they read the projected UV set) and meshes already given it.
#[derive(Resource, Default)]
struct Dressed {
    materials: HashMap<AssetId<StandardMaterial>, bool>,
    meshes: HashSet<AssetId<Mesh>>,
}

/// The detail for a material name, or None for surfaces that stay plain (glass, skin, lights, screens).
pub fn detail_kind(name: &str) -> Option<&'static str> {
    let name = name.to_ascii_lowercase();
    let has = |words: &[&str]| words.iter().any(|w| name.contains(w));
    if has(&["glass", "skin", "eye", "glow", "lamp", "light", "screen", "flame", "status", "badge", "sign", "chalk", "tread", "water"]) {
        return None;
    }
    Some(if has(&["palette", "shirt", "hoodie", "coat", "cap", "beanie", "blouse", "cuff", "pack", "bag", "collar", "rug", "awning", "teal", "fabric", "upholster", "apron", "pants", "trouser", "jacket", "uniform", "cloth"]) {
        "fabric"
    } else if has(&["brick"]) {
        "bricks"
    } else if has(&["floor", "plank"]) {
        "planks"
    } else if has(&["wood", "oak", "walnut", "board", "barn", "table", "crate", "bench"]) {
        "wood"
    } else if has(&["steel", "metal", "chrome", "brass", "gold", "iron", "stainless", "hardware", "frame", "rail", "cavity", "post", "shell"]) {
        "metal"
    } else if has(&["tyre", "tire", "rubber", "gasket", "feet", "bumper", "boots", "belt"]) {
        "rubber"
    } else if has(&["cardboard", "tape", "paper", "box"]) {
        "paper"
    } else if has(&["concrete", "terracotta", "stone", "grout", "dock", "roof", "ballast"]) {
        "concrete"
    } else if has(&["tile", "ivory", "enamel", "porcelain", "ceramic"]) {
        "tiles"
    } else if has(&["leaf", "sage", "foliage", "food", "bread", "tomato", "oil", "crust", "hair"]) {
        "organic"
    } else if has(&["cream", "plaster", "stucco", "wall", "ceiling", "trim", "cornice"]) {
        "plaster"
    } else {
        "plastic"
    })
}

/// The repeating colour detail of a kind.
pub fn detail_texture(assets: &AssetServer, kind: &str) -> Handle<Image> { repeat_texture(assets, &format!("Art/Generated/{kind}.png")) }

/// The repeating normal map of a kind (linear data, not sRGB).
fn detail_normals(assets: &AssetServer, kind: &str) -> Handle<Image> {
    assets.load_builder().with_settings(|settings: &mut ImageLoaderSettings| {
        settings.is_srgb = false;
        settings.sampler = ImageSampler::Descriptor(ImageSamplerDescriptor {
            address_mode_u: ImageAddressMode::Repeat,
            address_mode_v: ImageAddressMode::Repeat,
            ..ImageSamplerDescriptor::linear()
        });
    }).load(format!("Art/Generated/{kind}_normal.png"))
}

/// Gives a material on its own UVs (merged map meshes) the colour detail its name calls for; true when it changed.
pub fn add_detail(material: &mut StandardMaterial, name: &str, assets: &AssetServer) -> bool {
    if material.base_color_texture.is_some() || material.unlit || material.alpha_mode != AlphaMode::Opaque {
        return false;
    }
    let Some(kind) = detail_kind(name) else { return false };
    material.base_color_texture = Some(detail_texture(assets, kind));
    true
}

/// Gives a spawned part's material colour detail (unless it has its own texture, such as a palette) and relief, both read
/// through the projected UV set; true when the material now needs that set.
fn add_projected_detail(material: &mut StandardMaterial, name: &str, assets: &AssetServer) -> bool {
    if material.unlit || material.alpha_mode != AlphaMode::Opaque || material.normal_map_texture.is_some() {
        return false;
    }
    let Some(kind) = detail_kind(name) else { return false };
    let palette = name.to_ascii_lowercase().contains("palette");
    if material.base_color_texture.is_some() && !palette {
        return false;
    }
    if material.base_color_texture.is_none() {
        material.base_color_texture = Some(detail_texture(assets, kind));
        material.base_color_channel = UvChannel::Uv1;
    }
    material.normal_map_texture = Some(detail_normals(assets, kind));
    material.normal_map_channel = UvChannel::Uv1;
    true
}

/// Box projection of a vertex: its UV (metres, scaled) and the model-space directions of +U and +V.
fn project(p: [f32; 3], n: Vec3) -> ([f32; 2], Vec3, Vec3) {
    let (x, y, z) = (n.x.abs(), n.y.abs(), n.z.abs());
    let s = REPEATS_PER_METRE;
    if y >= x && y >= z {
        ([p[0] * s, p[2] * s], Vec3::X, Vec3::Z)
    } else if x >= z {
        ([p[2] * s, p[1] * s], Vec3::Z, Vec3::Y)
    } else {
        ([p[0] * s, p[1] * s], Vec3::X, Vec3::Y)
    }
}

/// Adds the projected UV set and its tangents (and a primary UV set copied from it when the mesh has none).
fn add_projected_uvs(mesh: &mut Mesh) {
    let Some(VertexAttributeValues::Float32x3(positions)) = mesh.attribute(Mesh::ATTRIBUTE_POSITION) else { return };
    let Some(VertexAttributeValues::Float32x3(normals)) = mesh.attribute(Mesh::ATTRIBUTE_NORMAL) else { return };
    if normals.len() != positions.len() {
        return;
    }
    let mut uvs = Vec::with_capacity(positions.len());
    let mut tangents = Vec::with_capacity(positions.len());
    for (p, n) in positions.iter().zip(normals) {
        let n = Vec3::from_array(*n).normalize_or(Vec3::Y);
        let (uv, u_axis, v_axis) = project(*p, n);
        let tangent = (u_axis - n * n.dot(u_axis)).normalize_or(n.any_orthonormal_vector());
        let sign = if n.cross(tangent).dot(v_axis) < 0.0 { -1.0 } else { 1.0 };
        uvs.push(uv);
        tangents.push([tangent.x, tangent.y, tangent.z, sign]);
    }
    if !mesh.contains_attribute(Mesh::ATTRIBUTE_UV_0) {
        mesh.insert_attribute(Mesh::ATTRIBUTE_UV_0, uvs.clone());
    }
    mesh.insert_attribute(Mesh::ATTRIBUTE_UV_1, uvs);
    // A model's own tangents serve its own normal map; the detail only goes where there were none.
    if !mesh.contains_attribute(Mesh::ATTRIBUTE_TANGENT) {
        mesh.insert_attribute(Mesh::ATTRIBUTE_TANGENT, tangents);
    }
}

/// glTF model parts whose material was set or replaced (a tinted outfit, a player's shirt): the material gets its detail
/// once, and every mesh drawn with a detailed material gets the projected UV set once.
fn dress_models(
    parts: Query<(&MeshMaterial3d<StandardMaterial>, &Mesh3d, &GltfMaterialName), Changed<MeshMaterial3d<StandardMaterial>>>, mut dressed: ResMut<Dressed>,
    mut materials: ResMut<Assets<StandardMaterial>>, mut meshes: ResMut<Assets<Mesh>>, assets: Res<AssetServer>,
) {
    for (material, mesh, name) in &parts {
        let id = material.0.id();
        let needs_uvs = match dressed.materials.get(&id) {
            Some(needs) => *needs,
            None => {
                let Some(current) = materials.get(&material.0) else { continue };
                let mut copy = current.clone();
                let needs = add_projected_detail(&mut copy, &name.0, &assets);
                if needs {
                    if let Some(mut m) = materials.get_mut(&material.0) {
                        *m = copy;
                    }
                }
                dressed.materials.insert(id, needs);
                needs
            }
        };
        if needs_uvs && !dressed.meshes.contains(&mesh.0.id()) {
            if let Some(mut m) = meshes.get_mut(&mesh.0) {
                add_projected_uvs(&mut m);
                dressed.meshes.insert(mesh.0.id());
            }
        }
    }
}
