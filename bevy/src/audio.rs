// Presentation-only sound (Unity GameAudio, AmbienceDirector, AudioZones, EquipmentRunningAudio and FootstepAudio): menu
// music while no world runs; inside one, the city, farmland and factory beds crossfading by where the avatar stands (a factory
// nearby takes over first, then farmland or open country, the city has the rest) with occasional horns, birds and clanks; a
// click for every button, the till when the company makes a sale, a thud when a piece lands on a drawn site, footsteps while
// the avatar walks, and each running machine's positional loop (sizzle, hum, mixer). Nothing here reads or changes gameplay
// state beyond what the baselines show, and a missing clip only makes that sound silent.
use std::collections::{HashMap, HashSet};

use bevy::audio::{PlaybackMode, SpatialScale, Volume};
use bevy::prelude::*;
use bevy::ui_widgets::Button;

use food_factory_bevy::goods::{company, JobState};
use food_factory_bevy::world::layout::BuildingCategory;
use food_factory_bevy::world::random;

use crate::session::Session;
use crate::settings::Prefs;
use crate::space::Space;

pub struct GameAudioPlugin;

impl Plugin for GameAudioPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<AudioState>().add_systems(Startup, setup).add_systems(Update, (listener, master, music_and_beds, incidentals, clicks, till, placements, footsteps, machines));
    }
}

const MUSIC_LEVEL: f32 = 0.4;
const BED_LEVEL: f32 = 0.35;
const FOOTSTEP_SECONDS: f32 = 0.42;
const MACHINE_HEARING: f32 = 14.0;

fn clip_path(name: &str) -> String { format!("Audio/Resources/Audio/{name}.wav") }

#[derive(Resource, Default)]
struct AudioState {
    clips: HashMap<String, Handle<AudioSource>>,
    music: Option<Entity>,
    beds: [Option<Entity>; 3],
    mix: [f32; 4],
    sales: usize,
    placed: HashSet<String>,
    placed_sites: HashSet<String>,
    step_timer: f32,
    step_index: u64,
    incidental_timer: f32,
    machines: HashMap<String, Entity>,
}

impl AudioState {
    fn clip(&mut self, assets: &AssetServer, name: &str) -> Handle<AudioSource> { self.clips.entry(name.to_string()).or_insert_with(|| assets.load(clip_path(name))).clone() }
}

fn looped(commands: &mut Commands, clip: Handle<AudioSource>, volume: f32) -> Entity {
    commands.spawn((AudioPlayer::new(clip), PlaybackSettings { mode: PlaybackMode::Loop, volume: Volume::Linear(volume), ..default() })).id()
}

fn one_shot(commands: &mut Commands, clip: Handle<AudioSource>, volume: f32, at: Option<Vec3>) {
    let settings = PlaybackSettings { mode: PlaybackMode::Despawn, volume: Volume::Linear(volume), spatial: at.is_some(), spatial_scale: Some(SpatialScale::new(0.25)), ..default() };
    match at {
        Some(position) => commands.spawn((AudioPlayer::new(clip), settings, Transform::from_translation(position))),
        None => commands.spawn((AudioPlayer::new(clip), settings)),
    };
}

fn setup(mut commands: Commands, assets: Res<AssetServer>, mut state: ResMut<AudioState>) {
    let music = state.clip(&assets, "menu_music");
    state.music = Some(looped(&mut commands, music, MUSIC_LEVEL));
    for (index, name) in ["city", "farm", "factory"].iter().enumerate() {
        let clip = state.clip(&assets, name);
        state.beds[index] = Some(looped(&mut commands, clip, 0.0));
    }
}

/// The master volume is the global one.
fn master(prefs: Option<Res<Prefs>>, mut global: ResMut<GlobalVolume>) {
    let Some(prefs) = prefs else { return };
    if prefs.is_changed() {
        global.volume = Volume::Linear(prefs.master);
    }
}

fn channel(prefs: &Option<Res<Prefs>>, pick: fn(&Prefs) -> f32) -> f32 { prefs.as_ref().map_or(1.0, |p| pick(p)) }

/// The camera hears positional sounds.
fn listener(mut commands: Commands, cameras: Query<Entity, (With<Camera3d>, Without<SpatialListener>)>) {
    for camera in &cameras {
        commands.entity(camera).insert(SpatialListener::new(0.3));
    }
}

/// City, farmland and factory weights of a map point (they sum to 1).
fn zones(world: &crate::session::Joined, x: f32, z: f32) -> [f32; 3] {
    let near = |category: BuildingCategory, reach: f32| {
        world.layout.buildings.iter().filter(|b| b.category == category).map(|b| {
            let dx = (b.x as f32 - x).max(x - (b.x + b.width) as f32).max(0.0);
            let dz = (b.z as f32 - z).max(z - (b.z + b.depth) as f32).max(0.0);
            dx.max(dz)
        }).fold(f32::MAX, f32::min).min(reach) / reach
    };
    let factory = 1.0 - near(BuildingCategory::Factory, 60.0);
    let city_half = world.layout.city_half_size as f32;
    let outside = ((x.abs().max(z.abs()) - city_half) / 60.0).clamp(0.0, 1.0);
    let farm = ((1.0 - near(BuildingCategory::Farm, 90.0)).max(outside)) * (1.0 - factory);
    [1.0 - factory - farm, farm, factory]
}

#[allow(clippy::too_many_arguments)]
fn music_and_beds(time: Res<Time>, prefs: Option<Res<Prefs>>, session: Res<Session>, mut state: ResMut<AudioState>, mut sinks: Query<&mut AudioSink>, space: Option<Res<Space>>, player: Query<&Transform, With<crate::player::Player>>, mut lookup: Local<f32>) {
    let delta = time.delta_secs();
    let in_world = session.joined().is_some();
    let music_target = if in_world { 0.0 } else { MUSIC_LEVEL };
    state.mix[3] = state.mix[3].move_towards(music_target, delta * MUSIC_LEVEL / if in_world { 1.5 } else { 2.5 });
    let mut target = [0.0f32; 3];
    *lookup += delta;
    if let (Some(world), Some(space), Ok(player)) = (session.joined(), space, player.single()) {
        let (x, z, _) = space.to_map(player.translation);
        target = zones(world, x, z);
    }
    for i in 0..3 {
        state.mix[i] = state.mix[i].move_towards(target[i] * BED_LEVEL, delta * 0.4 * BED_LEVEL);
    }
    let ambience = channel(&prefs, |p| p.ambience);
    let levels = [state.mix[0] * ambience, state.mix[1] * ambience, state.mix[2] * ambience];
    if let Some(music) = state.music {
        if let Ok(mut sink) = sinks.get_mut(music) {
            sink.set_volume(Volume::Linear(state.mix[3] * ambience));
        }
    }
    for (bed, level) in state.beds.iter().zip(levels) {
        if let Some(Ok(mut sink)) = bed.map(|b| sinks.get_mut(b)) {
            sink.set_volume(Volume::Linear(level));
        }
    }
}

/// Now and then a horn in town, birds over farmland, a clank by a factory, somewhere around the avatar.
fn incidentals(mut commands: Commands, prefs: Option<Res<Prefs>>, time: Res<Time>, assets: Res<AssetServer>, mut state: ResMut<AudioState>, player: Query<&Transform, With<crate::player::Player>>, session: Res<Session>) {
    let Ok(player) = player.single() else { return };
    if session.joined().is_none() {
        return;
    }
    state.incidental_timer -= time.delta_secs();
    if state.incidental_timer > 0.0 {
        return;
    }
    let roll = random::mix((time.elapsed_secs() * 1000.0) as u64);
    state.incidental_timer = 6.0 + (roll % 900) as f32 / 100.0;
    let weights = [state.mix[0], state.mix[1], state.mix[2]];
    let pick = weights.iter().enumerate().max_by(|a, b| a.1.total_cmp(b.1)).map_or(0, |(i, _)| i);
    if weights[pick] < 0.05 {
        return;
    }
    let name = match pick {
        0 => format!("honk_{}", 1 + roll % 3),
        1 => format!("bird_{}", 1 + roll % 3),
        _ => format!("clank_{}", 1 + roll % 3),
    };
    let angle = (roll % 360) as f32 * std::f32::consts::PI / 180.0;
    let at = player.translation + Vec3::new(angle.cos() * 18.0, 2.0, angle.sin() * 18.0);
    let clip = state.clip(&assets, &name);
    one_shot(&mut commands, clip, 0.5 * channel(&prefs, |p| p.ambience), Some(at));
}

fn clicks(mut commands: Commands, prefs: Option<Res<Prefs>>, assets: Res<AssetServer>, mut state: ResMut<AudioState>, buttons: Query<(), (Added<bevy::ui::Pressed>, With<Button>)>) {
    if !buttons.is_empty() {
        let clip = state.clip(&assets, "ui_click");
        one_shot(&mut commands, clip, 0.6 * channel(&prefs, |p| p.interface), None);
    }
}

/// The till rings when the company's ledger records a new sale.
fn till(mut commands: Commands, prefs: Option<Res<Prefs>>, assets: Res<AssetServer>, mut state: ResMut<AudioState>, session: Res<Session>) {
    let Some(world) = session.joined() else { return };
    let (Some(view), Some(company)) = (world.view(), world.company()) else { return };
    let sales = company::ledger_of(view, &company.id).iter().filter(|e| e.kind == company::LEDGER_SALE).count();
    if sales > state.sales && state.sales > 0 {
        let clip = state.clip(&assets, "cash");
        one_shot(&mut commands, clip, 0.7 * channel(&prefs, |p| p.interface), None);
    }
    state.sales = sales;
}

/// A piece appearing on a site already drawn is heard landing; a site coming into view appears silently.
fn placements(mut commands: Commands, prefs: Option<Res<Prefs>>, assets: Res<AssetServer>, mut state: ResMut<AudioState>, session: Res<Session>, space: Option<Res<Space>>) {
    let (Some(world), Some(space)) = (session.joined(), space) else { return };
    let mut now = HashSet::new();
    let mut landed = vec![];
    for (site, view) in &world.views {
        for piece in view.equipment.iter().filter(|e| e.placed() && &e.site_id == site) {
            now.insert(piece.id.clone());
            if !state.placed.contains(&piece.id) && state.placed_sites.contains(site) {
                let (w, d) = piece.footprint();
                landed.push(space.site_point(site, piece.cell_x as f32 + w as f32 * 0.5, piece.cell_z as f32 + d as f32 * 0.5, piece.level));
            }
        }
    }
    for at in landed {
        let clip = state.clip(&assets, "place");
        one_shot(&mut commands, clip, 0.8 * channel(&prefs, |p| p.effects), Some(at));
    }
    state.placed = now;
    state.placed_sites = world.views.keys().cloned().collect();
}

fn footsteps(mut commands: Commands, prefs: Option<Res<Prefs>>, time: Res<Time>, assets: Res<AssetServer>, mut state: ResMut<AudioState>, player: Query<&Transform, With<crate::player::Player>>, mut last: Local<Option<Vec3>>, actions: Res<crate::actions::PlayerActions>) {
    let Ok(player) = player.single() else { return };
    let position = player.translation;
    let moved = last.map_or(0.0, |l| Vec2::new(position.x - l.x, position.z - l.z).length() / time.delta_secs().max(1e-4));
    let grounded = last.is_none_or(|l| (position.y - l.y).abs() < 0.05);
    *last = Some(position);
    if moved < 0.8 || !grounded {
        state.step_timer = 0.0;
        return;
    }
    state.step_timer -= time.delta_secs() * if actions.sprint { 1.6 } else { 1.0 };
    if state.step_timer > 0.0 {
        return;
    }
    state.step_timer = FOOTSTEP_SECONDS;
    state.step_index += 1;
    let name = format!("footstep_{}", 1 + random::mix(state.step_index) % 6);
    let clip = state.clip(&assets, &name);
    one_shot(&mut commands, clip, 0.35 * channel(&prefs, |p| p.effects), Some(position));
}

fn machine_clip(kind: &str) -> Option<&'static str> {
    match kind {
        "fryer" | "grill" | "stove" => Some("sizzle"),
        "oven" | "fridge" => Some("hum"),
        "mixer" => Some("mixer"),
        _ => None,
    }
}

/// Each running machine near the avatar plays its loop where it stands.
fn machines(mut commands: Commands, prefs: Option<Res<Prefs>>, assets: Res<AssetServer>, mut state: ResMut<AudioState>, session: Res<Session>, space: Option<Res<Space>>, player: Query<&Transform, With<crate::player::Player>>) {
    let (Some(world), Some(space), Ok(player)) = (session.joined(), space, player.single()) else {
        for (_, e) in state.machines.drain() {
            commands.entity(e).despawn();
        }
        return;
    };
    let mut running = HashSet::new();
    for (site, view) in &world.views {
        for piece in view.equipment.iter().filter(|e| e.placed() && &e.site_id == site) {
            let Some(name) = machine_clip(&piece.kind) else { continue };
            let on = piece.kind == "fridge" || view.jobs.iter().any(|j| j.station_id == piece.id && j.state == JobState::Running);
            if !on {
                continue;
            }
            let (w, d) = piece.footprint();
            let at = space.site_point(site, piece.cell_x as f32 + w as f32 * 0.5, piece.cell_z as f32 + d as f32 * 0.5, piece.level) + Vec3::Y;
            if at.distance(player.translation) > MACHINE_HEARING {
                continue;
            }
            running.insert(piece.id.clone());
            if !state.machines.contains_key(&piece.id) {
                let clip = state.clip(&assets, name);
                let entity = commands.spawn((AudioPlayer::new(clip), PlaybackSettings { mode: PlaybackMode::Loop, volume: Volume::Linear(0.4 * channel(&prefs, |p| p.effects)), spatial: true, spatial_scale: Some(SpatialScale::new(0.25)), ..default() }, Transform::from_translation(at))).id();
                state.machines.insert(piece.id.clone(), entity);
            }
        }
    }
    let stale: Vec<String> = state.machines.keys().filter(|k| !running.contains(*k)).cloned().collect();
    for id in stale {
        if let Some(e) = state.machines.remove(&id) {
            commands.entity(e).despawn();
        }
    }
}
