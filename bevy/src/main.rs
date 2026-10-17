// Food Factory Bevy application: the client (menus, the drawn city and sites, the avatar, panels) over the authoritative
// server in the library, which a hosting client runs on its own thread. Release builds on Windows run without a console.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
mod actions;
mod animation;
#[cfg(feature = "audio")]
mod audio;
mod belt_controls;
mod build;
mod camera;
mod characters;
mod dev_capture;
mod front;
mod hud;
mod interaction;
mod map;
mod models;
mod panels;
mod phone_map;
mod pedestrian_routes;
mod player;
mod screens;
mod session;
mod settings;
mod sites;
mod space;
mod structure_icons;
mod textures;
mod ui;
mod vehicles;
mod window_icon;

use bevy::diagnostic::{DiagnosticsStore, FrameTimeDiagnosticsPlugin};
use bevy::light::CascadeShadowConfigBuilder;
use bevy::prelude::*;
use bevy::window::{PresentMode, PrimaryWindow};

fn main() {
    App::new()
        .add_plugins(
            DefaultPlugins
                .set(WindowPlugin {
                    primary_window: Some(Window {
                        title: "Food Factory".into(),
                        // Uncapped frame rate: no vsync, falling back to vsync where the platform cannot present without it.
                        // Capture runs (dev_capture) keep vsync so automated checks never drive the GPU flat out.
                        present_mode: if std::env::var_os("FF_CAPTURE_DIR").is_some() && std::env::var_os("FF_CAPTURE_UNCAPPED").is_none() { PresentMode::AutoVsync } else { PresentMode::AutoNoVsync },
                        ..default()
                    }),
                    ..default()
                })
                .set(asset_plugin()),
        )
        .insert_resource(ClearColor(Color::srgb(0.62, 0.74, 0.86)))
        .insert_resource(GlobalAmbientLight { color: Color::srgb(0.85, 0.9, 1.0), brightness: 350.0, ..default() })
        .add_plugins((
            session::SessionPlugin,
            models::ModelsPlugin,
            map::MapPlugin,
            sites::SitesPlugin,
            actions::ActionsPlugin,
            camera::OrbitCameraPlugin,
            player::PlayerPlugin,
            animation::CharacterAnimationPlugin,
            dev_capture::DevCapturePlugin,
            ui::GameUiPlugin,
            panels::PanelsPlugin,
            interaction::InteractionPlugin,
            characters::CharactersPlugin,
            hud::HudPlugin,
            screens::ScreensPlugin,
        ))
        .add_plugins(SoundPlugin)
        .add_plugins(front::FrontPlugin)
        .add_plugins((belt_controls::BeltControlsPlugin, build::BuildPlugin, vehicles::VehiclesPlugin, settings::SettingsPlugin, phone_map::PhoneMapPlugin, structure_icons::StructureIconsPlugin, window_icon::WindowIconPlugin, textures::SurfaceTexturesPlugin, FrameTimeDiagnosticsPlugin::default()))
        .add_systems(Startup, spawn_sun)
        .add_systems(Update, show_fps)
        .run();
}

/// Sound, when built with the `audio` feature.
struct SoundPlugin;

impl Plugin for SoundPlugin {
    fn build(&self, _app: &mut App) {
        #[cfg(feature = "audio")]
        _app.add_plugins(audio::GameAudioPlugin);
    }
}

// dx launches the game from target/dx/.../app, whose assets folder is empty, so hot-patch builds read the project's
// assets folder directly (and file_watcher reloads edits there). Other builds keep Bevy's default lookup.
fn asset_plugin() -> AssetPlugin {
    #[cfg(feature = "hotpatch")]
    let file_path = concat!(env!("CARGO_MANIFEST_DIR"), "/assets").to_string();
    #[cfg(not(feature = "hotpatch"))]
    let file_path = AssetPlugin::default().file_path;
    AssetPlugin { file_path, ..default() }
}

fn spawn_sun(mut commands: Commands) {
    commands.spawn((
        DirectionalLight { illuminance: 7_000.0, shadow_maps_enabled: true, color: Color::srgb(1.0, 0.97, 0.9), ..default() },
        Transform::from_xyz(40.0, 80.0, 30.0).looking_at(Vec3::ZERO, Vec3::Y),
        CascadeShadowConfigBuilder { maximum_distance: 120.0, first_cascade_far_bound: 15.0, ..default() }.build(),
    ));
}

// Shows the smoothed frame rate and the entity count in the window title, refreshed twice a second; a count that keeps
// climbing while the game sits idle points at a leak.
fn show_fps(time: Res<Time>, diagnostics: Res<DiagnosticsStore>, mut window: Query<&mut Window, With<PrimaryWindow>>, entities: Query<Entity>, mut since_update: Local<f32>) {
    *since_update += time.delta_secs();
    if *since_update < 0.5 {
        return;
    }
    *since_update = 0.0;
    let Some(fps) = diagnostics.get(&FrameTimeDiagnosticsPlugin::FPS).and_then(|fps| fps.smoothed()) else { return };
    if let Ok(mut window) = window.single_mut() {
        window.title = format!("Food Factory ({fps:.0} FPS, {} entities)", entities.iter().count());
    }
}
