// Food Factory character sandbox: one flat floor and the player character from the Unity project, with the same
// movement, orbit camera and animator behaviour. No buildings, networking or game logic.
// Release builds on Windows run without a console window.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
mod actions;
mod animation;
mod camera;
mod dev_capture;
mod player;

use bevy::asset::RenderAssetUsages;
use bevy::diagnostic::{DiagnosticsStore, FrameTimeDiagnosticsPlugin};
use bevy::image::{ImageAddressMode, ImageSampler, ImageSamplerDescriptor};
use bevy::light::CascadeShadowConfigBuilder;
use bevy::math::Affine2;
use bevy::prelude::*;
use bevy::render::render_resource::{Extent3d, TextureDimension, TextureFormat};
use bevy::window::{PresentMode, PrimaryWindow};

// Floor half-extent in metres; large enough that the edge is never reached in casual play.
const FLOOR_SIZE: f32 = 400.0;
// Floor checker tile size in metres, so ground motion stays readable on an otherwise blank floor.
const TILE_SIZE: f32 = 2.0;

fn main() {
    App::new()
        .add_plugins(
            DefaultPlugins
                .set(WindowPlugin {
                    primary_window: Some(Window {
                        title: "Food Factory - Bevy".into(),
                        // Uncapped frame rate: no vsync, falling back to vsync where the platform cannot present
                        // without it.
                        present_mode: PresentMode::AutoNoVsync,
                        ..default()
                    }),
                    ..default()
                })
                .set(asset_plugin()),
        )
        .insert_resource(ClearColor(Color::srgb(0.62, 0.74, 0.86)))
        .insert_resource(GlobalAmbientLight {
            color: Color::WHITE,
            brightness: 400.0,
            ..default()
        })
        .add_plugins((
            actions::ActionsPlugin,
            camera::OrbitCameraPlugin,
            player::PlayerPlugin,
            animation::CharacterAnimationPlugin,
            dev_capture::DevCapturePlugin,
        ))
        .add_plugins(FrameTimeDiagnosticsPlugin::default())
        .add_systems(Startup, spawn_world)
        .add_systems(Update, show_fps)
        .run();
}

// dx launches the game from target/dx/.../app, whose assets folder is empty, so hot-patch builds read the project's
// assets folder directly (and file_watcher reloads edits there). Other builds keep Bevy's default lookup.
fn asset_plugin() -> AssetPlugin {
    #[cfg(feature = "hotpatch")]
    let file_path = concat!(env!("CARGO_MANIFEST_DIR"), "/assets").to_string();
    #[cfg(not(feature = "hotpatch"))]
    let file_path = AssetPlugin::default().file_path;
    AssetPlugin {
        file_path,
        ..default()
    }
}

fn spawn_world(
    mut commands: Commands,
    mut meshes: ResMut<Assets<Mesh>>,
    mut materials: ResMut<Assets<StandardMaterial>>,
    mut images: ResMut<Assets<Image>>,
) {
    commands.spawn((
        Mesh3d(meshes.add(Plane3d::default().mesh().size(FLOOR_SIZE, FLOOR_SIZE))),
        MeshMaterial3d(materials.add(StandardMaterial {
            base_color_texture: Some(images.add(checker_image())),
            perceptual_roughness: 0.9,
            // The checker texture holds two tiles per repeat.
            uv_transform: Affine2::from_scale(Vec2::splat(FLOOR_SIZE / (TILE_SIZE * 2.0))),
            ..default()
        })),
    ));

    commands.spawn((
        DirectionalLight {
            illuminance: 9_000.0,
            shadow_maps_enabled: true,
            ..default()
        },
        Transform::from_xyz(4.0, 10.0, 6.0).looking_at(Vec3::ZERO, Vec3::Y),
        CascadeShadowConfigBuilder {
            maximum_distance: 60.0,
            first_cascade_far_bound: 12.0,
            ..default()
        }
        .build(),
    ));
}

// Shows the smoothed frame rate in the window title, refreshed twice a second.
fn show_fps(
    time: Res<Time>,
    diagnostics: Res<DiagnosticsStore>,
    mut window: Query<&mut Window, With<PrimaryWindow>>,
    mut since_update: Local<f32>,
) {
    *since_update += time.delta_secs();
    if *since_update < 0.5 {
        return;
    }
    *since_update = 0.0;
    let Some(fps) = diagnostics
        .get(&FrameTimeDiagnosticsPlugin::FPS)
        .and_then(|fps| fps.smoothed())
    else {
        return;
    };
    if let Ok(mut window) = window.single_mut() {
        window.title = format!("Food Factory - Bevy ({fps:.0} FPS)");
    }
}

// A 2x2 checker of two close greys, repeated across the floor.
fn checker_image() -> Image {
    let light = [196, 198, 200, 255];
    let dark = [176, 178, 181, 255];
    let data = [light, dark, dark, light].concat();
    let mut image = Image::new(
        Extent3d {
            width: 2,
            height: 2,
            depth_or_array_layers: 1,
        },
        TextureDimension::D2,
        data,
        TextureFormat::Rgba8UnormSrgb,
        RenderAssetUsages::RENDER_WORLD,
    );
    image.sampler = ImageSampler::Descriptor(ImageSamplerDescriptor {
        address_mode_u: ImageAddressMode::Repeat,
        address_mode_v: ImageAddressMode::Repeat,
        ..ImageSamplerDescriptor::nearest()
    });
    image
}
