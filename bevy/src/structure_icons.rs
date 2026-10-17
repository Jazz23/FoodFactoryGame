// Build catalog icons for structure cards (Unity StructureIcons): walls, doors, back doors, windows and finishes have no
// pre-rendered icon, so each tool and style is rendered once at runtime from the art-kit models the shell is drawn with, in
// the authored icons' three-quarter view on a transparent background, and kept for the rest of the run. A stage lives far
// below the world on its own render layer until its models have loaded and a few frames have drawn; the image keeps the last
// picture after its camera is removed. Presentation only.
//
// Loading the kit models for every style (profiled 2026-10-17) left the world's shadow and opaque passes several times slower
// for the rest of the run, so play uses icons rendered ahead of time: `FF_WRITE_STRUCTURE_ICONS=1` renders each staged icon
// into assets/ui/structures/<tool>-<style>.png (open the build catalog once), and a card whose PNG exists loads it instead
// of staging. A style without a PNG still falls back to staging.
use std::collections::HashMap;
use std::f32::consts::PI;

use bevy::camera::visibility::RenderLayers;
use bevy::camera::{ClearColorConfig, RenderTarget, ScalingMode};
use bevy::prelude::*;
use bevy::render::render_resource::TextureFormat;
use bevy::world_serialization::{WorldAssetRoot, WorldInstanceReady};

use crate::models::Bounds;

pub struct StructureIconsPlugin;

impl Plugin for StructureIconsPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<StructureIcons>().add_systems(Update, (stage_requests, finish_stages).chain());
    }
}

const SIZE: u32 = 128;
const LAYER: usize = 31;
const STAGE_DEPTH: f32 = -5000.0;
/// Stages sit this far apart so one icon camera never sees another stage.
const STAGE_SPACING: f32 = 60.0;
/// Seconds drawn after every model of a stage has loaded (new pipelines compile meanwhile), before the camera is removed.
const SETTLE_SECONDS: f32 = 1.0;
/// A stage whose models never all report ready (a model that fails to load) still goes after this long: every live stage is a
/// camera rendering each frame.
const MAX_STAGE_SECONDS: f32 = 8.0;
/// Stages rendering at once; the rest wait, so opening the catalog does not add dozens of cameras in one frame.
const MAX_STAGES: usize = 4;
// Matches RestaurantShellModel and sites.rs: a window's glazed unit sits on the sill; a single door's leaf hangs from its hinge.
const SILL_HEIGHT: f32 = 0.85;
const SINGLE_HINGE: f32 = 0.475;

/// Rendered icons by key ("tool:style"), the stagings asked for this frame, and the stages still rendering.
#[derive(Resource, Default)]
pub struct StructureIcons {
    images: HashMap<String, Handle<Image>>,
    requests: Vec<(String, Vec<(String, Vec3)>)>,
    stages: Vec<Stage>,
    next_slot: u32,
    /// The icon layer's key and fill lights, present only while stages render.
    lights: Vec<Entity>,
}

struct Stage {
    root: Entity,
    /// The icon's key and image, for writing it out.
    key: String,
    image: Handle<Image>,
    /// Written out (generator mode), waiting a moment for the capture before the stage goes.
    written: bool,
    models: usize,
    ready: usize,
    settle: f32,
    age: f32,
}

const ICONS_FOLDER: &str = "ui/structures";

/// Generator mode: staged icons are saved as PNGs.
fn writing() -> bool { std::env::var_os("FF_WRITE_STRUCTURE_ICONS").is_some() }

fn file_name(key: &str) -> String { format!("{}.png", key.replace([':', '/'], "-")) }

/// The pre-rendered icons on disk, under the asset folder.
fn icons_folder() -> std::path::PathBuf { bevy::asset::io::file::FileAssetReader::get_base_path().join("assets").join(ICONS_FOLDER) }

/// Counts a stage's loaded models.
#[derive(Component)]
struct StageModel(Entity);

impl StructureIcons {
    /// The icon for a card: pieces are (model path, Unity-local offset) under one root. The image fills in once rendered.
    pub fn icon(&mut self, images: &mut Assets<Image>, assets: &AssetServer, key: &str, pieces: Vec<(String, Vec3)>) -> Option<Handle<Image>> {
        let pieces: Vec<(String, Vec3)> = pieces.into_iter().filter(|p| !p.0.is_empty()).collect();
        if pieces.is_empty() { return None; }
        if let Some(image) = self.images.get(key) { return Some(image.clone()); }
        let file = file_name(key);
        if !writing() && icons_folder().join(&file).exists() {
            let handle: Handle<Image> = assets.load(format!("{ICONS_FOLDER}/{file}"));
            self.images.insert(key.to_string(), handle.clone());
            return Some(handle);
        }
        let mut image = Image::new_target_texture(SIZE, SIZE, TextureFormat::Bgra8UnormSrgb, None);
        image.data = Some(vec![0; (SIZE * SIZE * 4) as usize]);
        let handle = images.add(image);
        self.images.insert(key.to_string(), handle.clone());
        self.requests.push((key.to_string(), pieces));
        Some(handle)
    }

    /// The pieces Unity stages for a structure card, or none when the style has no model.
    pub fn pieces(styles: &food_factory_bevy::content::Styles, tool: &str, style: &str) -> Vec<(String, Vec3)> {
        match tool {
            "wall" => styles.walls.iter().find(|w| w.style == style).map(|w| vec![(w.wall1m.clone(), Vec3::ZERO)]).unwrap_or_default(),
            "finish" => styles.walls.iter().find(|w| w.style == style).map(|w| vec![(w.wall2m.clone(), Vec3::ZERO)]).unwrap_or_default(),
            "door" | "backdoor" => match styles.doors.iter().find(|d| d.style == style) {
                Some(d) => vec![(d.leaf.clone(), Vec3::new(SINGLE_HINGE, 0.005, 0.0)), (styles.single_doorway.clone(), Vec3::ZERO), (styles.single_frame.clone(), Vec3::ZERO)],
                None => vec![],
            },
            "window" => styles.windows.iter().find(|w| w.style == style).map(|w| vec![(w.bay.clone(), Vec3::ZERO), (w.window.clone(), Vec3::Y * SILL_HEIGHT)]).unwrap_or_default(),
            _ => vec![],
        }
    }
}

/// A Unity-local point as a scene offset (Unity +Z is scene -Z, as in sites.rs).
fn scene(unity: Vec3) -> Vec3 { Vec3::new(unity.x, unity.y, -unity.z) }

fn stage_requests(mut commands: Commands, mut icons: ResMut<StructureIcons>, assets: Res<AssetServer>, bounds: Res<Bounds>) {
    if icons.requests.is_empty() || icons.stages.len() >= MAX_STAGES { return; }
    // Key and fill lights like the authored icons', shared by every stage on the icon layer (the renderer allows only a few
    // directional lights); Unity Euler (pitch, yaw) as a light direction, Unity +Z as scene -Z.
    if icons.lights.is_empty() {
        for (pitch, yaw, lux, colour) in [(50.0f32, 130.0f32, 9000.0, Color::srgb(1.0, 0.93, 0.84)), (25.0, -60.0, 4500.0, Color::srgb(0.82, 0.9, 1.0))] {
            let (p, y) = (pitch.to_radians(), yaw.to_radians());
            let forward = scene(Vec3::new(y.sin() * p.cos(), -p.sin(), y.cos() * p.cos()));
            let light = commands.spawn((DirectionalLight { illuminance: lux, color: colour, shadow_maps_enabled: false, ..default() }, Transform::default().looking_to(forward, Vec3::Y), RenderLayers::layer(LAYER), Name::new("Structure icon light"))).id();
            icons.lights.push(light);
        }
    }
    let room = (MAX_STAGES - icons.stages.len()).min(icons.requests.len());
    let requests: Vec<_> = icons.requests.drain(..room).collect();
    for (key, pieces) in requests {
        let Some(image) = icons.images.get(&key).cloned() else { continue };
        let origin = Vec3::new(icons.next_slot as f32 * STAGE_SPACING, STAGE_DEPTH, 0.0);
        icons.next_slot += 1;
        let layer = RenderLayers::layer(LAYER);
        let root = commands.spawn((Name::new(format!("Structure icon {key}")), Transform::from_translation(origin), Visibility::default())).id();
        // Every piece turns half round like the shell's kit pieces; its bounds turn with it.
        let turn = Quat::from_rotation_y(PI);
        let (mut low, mut high) = (Vec3::splat(f32::MAX), Vec3::splat(f32::MIN));
        for (path, offset) in &pieces {
            let (min, max) = bounds.get(path);
            for corner in [min, max, Vec3::new(min.x, min.y, max.z), Vec3::new(max.x, max.y, min.z)] {
                let p = scene(*offset) + turn * corner;
                low = low.min(p);
                high = high.max(p);
            }
            let model = commands.spawn((WorldAssetRoot(assets.load(GltfAssetLabel::Scene(0).from_asset(path.clone()))), Transform { translation: scene(*offset), rotation: turn, ..default() }, layer.clone(), StageModel(root)))
                .observe(|ready: On<WorldInstanceReady>, mut commands: Commands, children: Query<&Children>, models: Query<&StageModel>, mut icons: ResMut<StructureIcons>| {
                    for descendant in children.iter_descendants(ready.event().entity) { commands.entity(descendant).insert(RenderLayers::layer(LAYER)); }
                    if let Ok(model) = models.get(ready.event().entity) {
                        if let Some(stage) = icons.stages.iter_mut().find(|s| s.root == model.0) { stage.ready += 1; }
                    }
                })
                .id();
            commands.entity(root).add_child(model);
        }
        // In the stage root's own space (the camera is its child).
        let centre = (low + high) * 0.5;
        let extent = ((high - low) * 0.5).length().max(0.1) * 1.05;
        let eye = centre + scene(Vec3::new(-1.0, 0.9, 1.2)).normalize() * 10.0;
        let camera = commands.spawn((
            Camera3d::default(),
            Camera { order: -10, clear_color: ClearColorConfig::Custom(Color::NONE), ..default() },
            RenderTarget::Image(image.clone().into()),
            Projection::Orthographic(OrthographicProjection { scaling_mode: ScalingMode::Fixed { width: extent * 2.0, height: extent * 2.0 }, near: 0.1, far: 30.0, ..OrthographicProjection::default_3d() }),
            Msaa::Off,
            Transform::from_translation(eye).looking_at(centre, Vec3::Y),
            layer.clone(),
            Name::new("Structure icon camera"),
        )).id();
        commands.entity(root).add_child(camera);
        icons.stages.push(Stage { root, key: key.clone(), image: image.clone(), written: false, models: pieces.len(), ready: 0, settle: SETTLE_SECONDS, age: 0.0 });
    }
}

/// Once a stage's models have loaded and drawn for a second (or it has run out of time), its camera stops and the stage goes; the
/// image stays. The lights go with the last stage.
fn finish_stages(mut commands: Commands, mut icons: ResMut<StructureIcons>, time: Res<Time>) {
    let dt = time.delta_secs();
    icons.stages.retain_mut(|stage| {
        stage.age += dt;
        if stage.age < MAX_STAGE_SECONDS {
            if stage.ready < stage.models { return true; }
            if stage.settle > 0.0 { stage.settle -= dt; return true; }
        }
        if writing() && !stage.written && stage.ready >= stage.models {
            // Captured from the camera's next frame; the stage stays a moment longer for it.
            let folder = icons_folder();
            let _ = std::fs::create_dir_all(&folder);
            commands.spawn(bevy::render::view::screenshot::Screenshot::image(stage.image.clone()))
                .observe(bevy::render::view::screenshot::save_to_disk(folder.join(file_name(&stage.key))));
            stage.written = true;
            stage.settle = 0.5;
            stage.age = 0.0;
            return true;
        }
        commands.entity(stage.root).despawn();
        false
    });
    if icons.stages.is_empty() && icons.requests.is_empty() {
        for light in icons.lights.drain(..) { commands.entity(light).despawn(); }
    }
}
