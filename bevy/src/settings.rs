// Client preferences (Unity ClientSettings and the menu's Settings screen), kept in the client's SQLite key/value settings
// database (decision 0011): display name, mouse sensitivity, master and channel volumes, anti-aliasing, key binding
// overrides, and the last session the menu started, which Resume repeats. The screen opens from the main and pause menus;
// a rebind waits for the next key (Esc cancels). Only this client's presentation and input read these.
use bevy::anti_alias::fxaa::Fxaa;
use bevy::prelude::*;

use crate::actions::Bindings;
use crate::session::Settings;
use crate::ui::{UiEntry, UiIntents, UiModel};

pub struct SettingsPlugin;

impl Plugin for SettingsPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<SettingsScreen>().add_systems(Startup, load).add_systems(Update, (escape, intents, rebind, apply_anti_aliasing, draw).chain());
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub enum AntiAliasing {
    #[default]
    Off,
    Fxaa,
    Msaa4,
}

impl AntiAliasing {
    fn key(self) -> &'static str {
        match self {
            AntiAliasing::Off => "off",
            AntiAliasing::Fxaa => "fxaa",
            AntiAliasing::Msaa4 => "msaa4",
        }
    }
    fn from_key(key: &str) -> Self {
        match key {
            "fxaa" => AntiAliasing::Fxaa,
            "msaa4" => AntiAliasing::Msaa4,
            _ => AntiAliasing::Off,
        }
    }
}

/// The preferences in force.
#[derive(Resource, Clone, Debug)]
pub struct Prefs {
    pub name: String,
    pub sensitivity: f32,
    pub master: f32,
    pub effects: f32,
    pub interface: f32,
    pub ambience: f32,
    pub anti_aliasing: AntiAliasing,
    /// "world:<name>", "server:<address>" or empty.
    pub last_session: String,
}

impl Default for Prefs {
    fn default() -> Self {
        Self { name: String::new(), sensitivity: 1.0, master: 0.8, effects: 1.0, interface: 1.0, ambience: 1.0, anti_aliasing: AntiAliasing::Off, last_session: String::new() }
    }
}

/// Whether the settings screen is open, and the binding waiting for a key.
#[derive(Resource, Default)]
pub struct SettingsScreen {
    pub open: bool,
    pub rebinding: Option<&'static str>,
}

/// Rebindable actions: (key, label).
const ACTIONS: [(&str, &str); 22] = [
    ("up", "Move forward"), ("down", "Move back"), ("left", "Move left"), ("right", "Move right"), ("jump", "Jump"), ("sprint", "Sprint"),
    ("inventory", "Inventory / interact"), ("take_item", "Take item"), ("place_item", "Place item"), ("clear", "Clear cursor"), ("rotate", "Rotate"),
    ("build", "Build mode"), ("phone", "Phone"), ("map", "Map"), ("logistics", "Logistics"), ("flip_lift", "Flip lift"), ("floor_up", "Floor up"),
    ("floor_down", "Floor down"), ("release_cursor", "Close screen"), ("confirm", "Confirm build"), ("hotbar", "Hotbar (fixed: 1-9)"), ("zoom", "Zoom (fixed: scroll)"),
];

fn binding<'a>(bindings: &'a mut Bindings, action: &str) -> Option<&'a mut KeyCode> {
    Some(match action {
        "up" => bindings.up.first_mut()?,
        "down" => bindings.down.first_mut()?,
        "left" => bindings.left.first_mut()?,
        "right" => bindings.right.first_mut()?,
        "jump" => bindings.jump.first_mut()?,
        "sprint" => bindings.sprint.first_mut()?,
        "release_cursor" => bindings.release_cursor.first_mut()?,
        "confirm" => bindings.confirm.first_mut()?,
        "inventory" => &mut bindings.inventory,
        "take_item" => &mut bindings.take_item,
        "place_item" => &mut bindings.place_item,
        "clear" => &mut bindings.clear,
        "rotate" => &mut bindings.rotate,
        "build" => &mut bindings.build,
        "phone" => &mut bindings.phone,
        "map" => &mut bindings.map,
        "logistics" => &mut bindings.logistics,
        "flip_lift" => &mut bindings.flip_lift,
        "floor_up" => &mut bindings.floor_up,
        "floor_down" => &mut bindings.floor_down,
        _ => return None,
    })
}

/// Key codes a binding may take, by their saved name.
fn key_codes() -> Vec<KeyCode> {
    use KeyCode::*;
    vec![
        KeyA, KeyB, KeyC, KeyD, KeyE, KeyF, KeyG, KeyH, KeyI, KeyJ, KeyK, KeyL, KeyM, KeyN, KeyO, KeyP, KeyQ, KeyR, KeyS, KeyT, KeyU, KeyV, KeyW, KeyX, KeyY, KeyZ,
        Digit0, Space, Enter, NumpadEnter, Escape, Tab, Backspace, ShiftLeft, ShiftRight, ControlLeft, ControlRight, AltLeft, AltRight, ArrowUp, ArrowDown,
        ArrowLeft, ArrowRight, PageUp, PageDown, Home, End, Insert, Delete, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, Comma, Period, Slash,
        Semicolon, Quote, BracketLeft, BracketRight, Backslash, Minus, Equal, Backquote, Numpad0, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6,
        Numpad7, Numpad8, Numpad9, CapsLock,
    ]
}

fn key_name(key: KeyCode) -> String { format!("{key:?}") }

fn parse_key(name: &str) -> Option<KeyCode> { key_codes().into_iter().find(|k| key_name(*k) == name) }

fn label_of(key: KeyCode) -> String {
    let name = key_name(key);
    name.strip_prefix("Key").or_else(|| name.strip_prefix("Digit")).unwrap_or(&name).to_string()
}

fn load(settings: Res<Settings>, mut commands: Commands, mut bindings: ResMut<Bindings>, mut session: ResMut<crate::session::Session>) {
    let store = settings.0.lock().unwrap_or_else(|e| e.into_inner());
    let prefs = Prefs {
        name: store.get("display_name").unwrap_or_default(),
        sensitivity: store.float("look_sensitivity", 1.0).clamp(0.1, 3.0),
        master: store.float("volume", 0.8).clamp(0.0, 1.0),
        effects: store.float("effects_volume", 1.0).clamp(0.0, 1.0),
        interface: store.float("interface_volume", 1.0).clamp(0.0, 1.0),
        ambience: store.float("ambience_volume", 1.0).clamp(0.0, 1.0),
        anti_aliasing: AntiAliasing::from_key(&store.get("anti_aliasing").unwrap_or_default()),
        last_session: store.get("last_session").unwrap_or_default(),
    };
    for (action, _) in ACTIONS {
        if let Some(key) = store.get(&format!("binding.{action}")).and_then(|n| parse_key(&n)) {
            if let Some(slot) = binding(&mut bindings, action) {
                *slot = key;
            }
        }
    }
    bindings.sensitivity = prefs.sensitivity;
    if !prefs.name.is_empty() {
        session.display_name = prefs.name.clone();
    }
    commands.insert_resource(prefs);
}

fn save(settings: &Settings, prefs: &Prefs, bindings: &mut Bindings) {
    let store = settings.0.lock().unwrap_or_else(|e| e.into_inner());
    store.set("display_name", &prefs.name);
    store.set("look_sensitivity", &prefs.sensitivity.to_string());
    store.set("volume", &prefs.master.to_string());
    store.set("effects_volume", &prefs.effects.to_string());
    store.set("interface_volume", &prefs.interface.to_string());
    store.set("ambience_volume", &prefs.ambience.to_string());
    store.set("anti_aliasing", prefs.anti_aliasing.key());
    store.set("last_session", &prefs.last_session);
    for (action, _) in ACTIONS {
        if let Some(key) = binding(bindings, action) {
            store.set(&format!("binding.{action}"), &key_name(*key));
        }
    }
}

/// Remembers the session the menu just started, for Resume.
pub fn remember_session(settings: &Settings, prefs: &mut Prefs, last: String) {
    prefs.last_session = last;
    let store = settings.0.lock().unwrap_or_else(|e| e.into_inner());
    store.set("last_session", &prefs.last_session);
}

fn step(value: f32, delta: f32, min: f32, max: f32) -> f32 { (((value + delta) * 20.0).round() / 20.0).clamp(min, max) }

#[allow(clippy::too_many_arguments)]
fn intents(mut intents: ResMut<UiIntents>, mut screen: ResMut<SettingsScreen>, prefs: Option<ResMut<Prefs>>, settings: Res<Settings>, mut bindings: ResMut<Bindings>, fields: Res<crate::ui::UiFields>, mut session: ResMut<crate::session::Session>) {
    let Some(mut prefs) = prefs else { return };
    let actions: Vec<String> = intents.0.iter().filter(|a| a.starts_with("s:")).cloned().collect();
    intents.0.retain(|a| !a.starts_with("s:"));
    if actions.is_empty() {
        return;
    }
    for action in actions {
        let parts: Vec<&str> = action[2..].splitn(2, '|').collect();
        match parts.as_slice() {
            ["open"] => screen.open = true,
            ["back"] => {
                if let Some(name) = fields.0.get("settings-name").filter(|n| !n.trim().is_empty()) {
                    prefs.name = name.trim().to_string();
                    session.display_name = prefs.name.clone();
                }
                screen.open = false;
                screen.rebinding = None;
            }
            ["sensitivity", delta] => prefs.sensitivity = step(prefs.sensitivity, delta.parse().unwrap_or(0.0), 0.1, 3.0),
            ["master", delta] => prefs.master = step(prefs.master, delta.parse().unwrap_or(0.0), 0.0, 1.0),
            ["effects", delta] => prefs.effects = step(prefs.effects, delta.parse().unwrap_or(0.0), 0.0, 1.0),
            ["interface", delta] => prefs.interface = step(prefs.interface, delta.parse().unwrap_or(0.0), 0.0, 1.0),
            ["ambience", delta] => prefs.ambience = step(prefs.ambience, delta.parse().unwrap_or(0.0), 0.0, 1.0),
            ["aa", key] => prefs.anti_aliasing = AntiAliasing::from_key(key),
            ["rebind", action] => screen.rebinding = ACTIONS.iter().find(|(a, _)| a == action).map(|(a, _)| *a),
            ["reset"] => {
                *bindings = Bindings { sensitivity: prefs.sensitivity, ..Bindings::default() };
            }
            _ => {}
        }
    }
    bindings.sensitivity = prefs.sensitivity;
    save(&settings, &prefs, &mut bindings);
}

/// Esc leaves the settings screen (unless it is the key a rebind is waiting for).
fn escape(actions: Res<crate::actions::PlayerActions>, mut screen: ResMut<SettingsScreen>, mut intents: ResMut<UiIntents>) {
    if screen.open && screen.rebinding.is_none() && actions.close_pressed {
        intents.0.push("s:back".into());
    }
    let _ = &mut screen;
}

/// While a rebind waits, the next key press becomes that action's primary key; Esc cancels.
fn rebind(mut screen: ResMut<SettingsScreen>, keys: Res<ButtonInput<KeyCode>>, mut bindings: ResMut<Bindings>, settings: Res<Settings>, prefs: Option<Res<Prefs>>) {
    let Some(action) = screen.rebinding else { return };
    let Some(pressed) = keys.get_just_pressed().next().copied() else { return };
    screen.rebinding = None;
    if pressed == KeyCode::Escape && action != "release_cursor" {
        return;
    }
    if let Some(slot) = binding(&mut bindings, action) {
        *slot = pressed;
    }
    if let Some(prefs) = prefs {
        save(&settings, &prefs, &mut bindings);
    }
}

fn apply_anti_aliasing(mut commands: Commands, prefs: Option<Res<Prefs>>, cameras: Query<Entity, With<Camera3d>>, mut applied: Local<Option<(AntiAliasing, Entity)>>) {
    let Some(prefs) = prefs else { return };
    for camera in &cameras {
        if *applied == Some((prefs.anti_aliasing, camera)) {
            continue;
        }
        let mut entity = commands.entity(camera);
        match prefs.anti_aliasing {
            AntiAliasing::Off => {
                entity.insert(Msaa::Off).remove::<Fxaa>();
            }
            AntiAliasing::Fxaa => {
                entity.insert((Msaa::Off, Fxaa::default()));
            }
            AntiAliasing::Msaa4 => {
                entity.insert(Msaa::Sample4).remove::<Fxaa>();
            }
        }
        *applied = Some((prefs.anti_aliasing, camera));
    }
}

fn draw(screen: Res<SettingsScreen>, prefs: Option<Res<Prefs>>, mut bindings: ResMut<Bindings>, mut model: ResMut<UiModel>, session: Res<crate::session::Session>) {
    if !screen.open {
        return;
    }
    let Some(prefs) = prefs else { return };
    let mut next = UiModel { open: true, wide: true, heading: "Settings".into(), ..default() };
    next.status = match screen.rebinding {
        Some(action) => format!("Press a key for {} (Esc cancels)", ACTIONS.iter().find(|(a, _)| *a == action).map_or(action, |(_, l)| *l)),
        None => "Changes are saved as you make them.".into(),
    };
    next.entries.push(UiEntry::label("name-label", "Your name"));
    next.entries.push(UiEntry::field("settings-name", if prefs.name.is_empty() { session.display_name.clone() } else { prefs.name.clone() }));
    let row = |id: &str, label: String, value: f32| UiEntry::row(id.to_string(), vec![
        UiEntry::label(format!("{id}-label"), label),
        UiEntry::button(format!("s:{id}|-0.1"), "-"),
        UiEntry::label(format!("{id}-value"), format!("{:.0}%", value * 100.0)),
        UiEntry::button(format!("s:{id}|0.1"), "+"),
    ]);
    next.entries.push(row("sensitivity", "Mouse sensitivity".into(), prefs.sensitivity));
    next.entries.push(row("master", "Master volume".into(), prefs.master));
    next.entries.push(row("effects", "Effects".into(), prefs.effects));
    next.entries.push(row("interface", "Interface".into(), prefs.interface));
    next.entries.push(row("ambience", "Music and ambience".into(), prefs.ambience));
    next.entries.push(UiEntry::row("aa", vec![
        UiEntry::label("aa-label", "Anti-aliasing"),
        UiEntry::button("s:aa|off", "Off").selected(prefs.anti_aliasing == AntiAliasing::Off),
        UiEntry::button("s:aa|fxaa", "FXAA").selected(prefs.anti_aliasing == AntiAliasing::Fxaa),
        UiEntry::button("s:aa|msaa4", "MSAA 4x").selected(prefs.anti_aliasing == AntiAliasing::Msaa4),
    ]));
    next.entries.push(UiEntry::label("keys-label", "Key bindings"));
    for (action, label) in ACTIONS {
        let key = binding(&mut bindings, action).map(|k| label_of(*k));
        let entry = match key {
            Some(key) => UiEntry::button(format!("s:rebind|{action}"), if screen.rebinding == Some(action) { "...".to_string() } else { key }).selected(screen.rebinding == Some(action)),
            None => UiEntry::label(format!("fixed-{action}"), ""),
        };
        next.entries.push(UiEntry::row(format!("bind-{action}"), vec![UiEntry::label(format!("bind-label-{action}"), label), entry]));
    }
    next.entries.push(UiEntry::row("settings-actions", vec![UiEntry::button("s:reset", "Reset keys"), UiEntry::button("s:back", "Back")]));
    if *model != next {
        *model = next;
    }
}
