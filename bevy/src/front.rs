// The superui front end: mounts the TSX game UI (assets/ui/game: menus, world select, settings, HUD and panels) over the
// world and bridges it. The game sends one JSON event per surface (menu, settings, hud, tooltip, panel) only when it changes;
// the UI sends back intents (the same action strings the native widgets queued), typed field values and whether a text field
// has focus. Right clicks and drags on panel buttons are read here from bevy_picking, since superui only dispatches clicks.
use bevy::picking::events::{PointerClick, PointerDragDrop};
use bevy::picking::pointer::PointerButton;
use bevy::prelude::*;
use serde::{Deserialize, Serialize};
use superui::prelude::{PickingPolicy, SuperUiApp, SuperUiPlugin, SuperUiRoot};

use crate::ui::{PanelDto, RuntimeImages, UiFields, UiIntents, UiModel};

pub struct FrontPlugin;

impl Plugin for FrontPlugin {
    fn build(&self, app: &mut App) {
        app.add_plugins(SuperUiPlugin)
            .init_resource::<RuntimeImages>()
            .init_resource::<FrontGeneration>()
            .add_superui_command::<IntentCmd>("intent")
            .add_superui_command::<FieldCmd>("field")
            .add_superui_command::<TypingCmd>("typing")
            .add_superui_command::<ReadyCmd>("ready")
            .add_superui_event::<MenuFrame>("menu")
            .add_superui_event::<SettingsFrame>("settings")
            .add_superui_event::<HudFrame>("hud")
            .add_superui_event::<TooltipFrame>("tooltip")
            .add_superui_event::<PanelFrame>("panel")
            .add_observer(on_intent)
            .add_observer(on_field)
            .add_observer(on_typing)
            .add_observer(on_ready)
            .add_observer(secondary_click)
            .add_observer(drag_drop)
            .add_systems(Startup, mount)
            .add_systems(Update, (fit_scale, emit_panel, attach_runtime_images));
    }
}

fn mount(mut commands: Commands, assets: Res<AssetServer>) {
    // The root fills the window but never takes the pointer itself; elements with listeners block the world below them.
    commands.spawn((SuperUiRoot::from_asset_dir("ui/game", &assets), PickingPolicy::PassThrough, bevy::picking::Pickable::IGNORE, GlobalZIndex(10), Name::new("Game UI")));
}

/// The designs are drawn at 1600 x 900: the UI scales so that frame fits the window.
fn fit_scale(window: Query<&Window, With<bevy::window::PrimaryWindow>>, mut scale: ResMut<UiScale>) {
    let Ok(window) = window.single() else { return };
    let fit = (window.width() / 1600.0).min(window.height() / 900.0).clamp(0.5, 3.0);
    if (scale.0 - fit).abs() > 0.001 {
        scale.0 = fit;
    }
}

/// Bumped when the UI (re)mounts and asks for state, so every surface is sent again.
#[derive(Resource, Default)]
pub struct FrontGeneration(pub u32);

/// Remembers what a surface last sent, resending when it changes or the UI remounts.
pub struct Sent<T>(u32, Option<T>);

impl<T> Default for Sent<T> {
    fn default() -> Self { Self(u32::MAX, None) }
}

impl<T: PartialEq + Clone + Event> Sent<T>
where
    for<'a> T::Trigger<'a>: Default,
{
    pub fn send(&mut self, commands: &mut Commands, generation: &FrontGeneration, next: T) {
        if self.0 == generation.0 && self.1.as_ref() == Some(&next) {
            return;
        }
        self.0 = generation.0;
        self.1 = Some(next.clone());
        commands.trigger(next);
    }
}

#[derive(Event, Deserialize)]
struct IntentCmd {
    action: String,
}

#[derive(Event, Deserialize)]
struct FieldCmd {
    id: String,
    value: String,
}

#[derive(Event, Deserialize)]
struct TypingCmd {
    on: bool,
}

#[derive(Event, Deserialize, Default)]
struct ReadyCmd {}

fn on_intent(cmd: On<IntentCmd>, mut intents: ResMut<UiIntents>) { intents.0.push(cmd.event().action.clone()); }

fn on_field(cmd: On<FieldCmd>, mut fields: ResMut<UiFields>) {
    let cmd = cmd.event();
    fields.0.insert(cmd.id.clone(), cmd.value.clone());
}

fn on_typing(cmd: On<TypingCmd>, mut typing: ResMut<crate::actions::UiTyping>) {
    if typing.0 != cmd.event().on {
        typing.0 = cmd.event().on;
    }
}

fn on_ready(_: On<ReadyCmd>, mut generation: ResMut<FrontGeneration>) { generation.0 = generation.0.wrapping_add(1); }

/// The main menu, world select and pause menu.
#[derive(Event, Serialize, Clone, PartialEq, Default)]
pub struct MenuFrame {
    /// "menu", "paused" or "hidden".
    pub screen: String,
    pub status: String,
    pub version: String,
    pub resume: Option<ResumeDto>,
    pub worlds: Vec<WorldDto>,
    pub new_name: String,
    pub address: String,
    pub host_address: String,
    /// The pause menu's line: what this session plays or hosts.
    pub session: String,
}

#[derive(Serialize, Clone, PartialEq, Default)]
pub struct ResumeDto {
    pub sub: String,
    pub ago: String,
}

#[derive(Serialize, Clone, PartialEq, Default)]
pub struct WorldDto {
    pub name: String,
    pub sub: String,
    pub day: String,
    pub cash: String,
    pub last: String,
}

#[derive(Event, Serialize, Clone, PartialEq, Default)]
pub struct SettingsFrame {
    pub open: bool,
    pub name: String,
    pub sensitivity: f32,
    pub master: f32,
    pub effects: f32,
    pub interface: f32,
    pub ambience: f32,
    pub anti_aliasing: String,
    /// The label of the action waiting for a key.
    pub listening: Option<String>,
    pub notice: String,
    pub columns: Vec<Vec<BindGroupDto>>,
}

#[derive(Serialize, Clone, PartialEq, Default)]
pub struct BindGroupDto {
    pub name: String,
    pub rows: Vec<BindDto>,
}

#[derive(Serialize, Clone, PartialEq, Default)]
pub struct BindDto {
    pub action: String,
    pub label: String,
    pub key: String,
    pub alternate: String,
    pub fixed: bool,
}

#[derive(Event, Serialize, Clone, PartialEq, Default)]
pub struct HudFrame {
    pub visible: bool,
    pub loading: String,
    pub site: String,
    pub sub: String,
    pub cash: String,
    pub rate: String,
    pub rate_up: bool,
    pub day: String,
    pub clock: String,
    pub alerts: Vec<AlertDto>,
    pub prompt_key: String,
    pub prompt: String,
    pub hint: String,
    pub crosshair: bool,
    pub hotbar: Vec<HotbarSlotDto>,
    pub keys: Vec<KeyHintDto>,
}

#[derive(Serialize, Clone, PartialEq, Default)]
pub struct AlertDto {
    /// "warn" (an accent card with a title and detail) or "dot" (a one-line card with a coloured dot).
    pub kind: String,
    pub title: String,
    pub detail: String,
    pub time: String,
    pub colour: String,
}

#[derive(Serialize, Clone, PartialEq, Default)]
pub struct HotbarSlotDto {
    pub key: String,
    pub icon: String,
    pub count: String,
    pub selected: bool,
}

#[derive(Serialize, Clone, PartialEq, Default)]
pub struct KeyHintDto {
    pub key: String,
    pub label: String,
}

#[derive(Event, Serialize, Clone, PartialEq, Default)]
pub struct TooltipFrame {
    pub text: String,
    pub x: f32,
    pub y: f32,
}

#[derive(Event, Serialize, Clone, PartialEq, Default)]
pub struct PanelFrame {
    pub panel: Option<PanelDto>,
}

fn emit_panel(mut commands: Commands, model: Res<UiModel>, fields: Res<UiFields>, mut images: ResMut<RuntimeImages>, generation: Res<FrontGeneration>, mut sent: Local<Sent<PanelFrame>>) {
    if !model.is_changed() && !generation.is_changed() {
        return;
    }
    images.0.clear();
    let panel = model.open.then(|| model.to_dto(&fields, &mut images));
    sent.send(&mut commands, &generation, PanelFrame { panel });
}

/// A runtime image's element (`rt-<key>`) gets the image as its own `ImageNode`; the phone map's also reads the pointer.
fn attach_runtime_images(mut commands: Commands, images: Res<RuntimeImages>, nodes: Query<(Entity, &Name, Option<&ImageNode>), With<Node>>) {
    for (entity, name, image) in &nodes {
        let Some(key) = name.as_str().strip_prefix("rt-") else { continue };
        let Some(handle) = images.0.get(key) else { continue };
        if image.is_none_or(|i| &i.image != handle) {
            // superui may despawn the node later this frame (its list re-rendered); that is not an error here.
            commands.entity(entity).try_insert(ImageNode::new(handle.clone()));
        }
        if key == "phone-map" {
            commands.entity(entity).try_insert_if_new((crate::phone_map::MapSurface, bevy::ui::RelativeCursorPosition::default()));
        }
    }
}

/// The action an element carries (`act:<action>` as its id), searching up from a picked entity.
fn action_of(mut entity: Entity, names: &Query<&Name>, parents: &Query<&ChildOf>) -> Option<String> {
    for _ in 0..8 {
        if let Some(action) = names.get(entity).ok().and_then(|n| n.as_str().strip_prefix("act:")) {
            return Some(action.to_string());
        }
        entity = parents.get(entity).ok()?.parent();
    }
    None
}

/// The action of exactly this element: pointer events bubble, so a global observer sees each ancestor and acts once, at the
/// element that carries the action.
fn own_action(entity: Entity, names: &Query<&Name>) -> Option<String> { names.get(entity).ok().and_then(|n| n.as_str().strip_prefix("act:")).map(str::to_string) }

/// A right click sends the button's action marked `|alt` (Unity's right click on a slot takes half its stack).
fn secondary_click(click: On<PointerClick>, names: Query<&Name>, mut intents: ResMut<UiIntents>) {
    if click.event().button != PointerButton::Secondary {
        return;
    }
    if let Some(action) = own_action(click.event().entity, &names) {
        intents.0.push(format!("{action}|alt"));
    }
}

/// A slot dragged onto another sends `drag>SOURCE>TARGET` (Unity's slot drag and drop).
fn drag_drop(drop: On<PointerDragDrop>, names: Query<&Name>, parents: Query<&ChildOf>, mut intents: ResMut<UiIntents>) {
    let (Some(target), Some(source)) = (own_action(drop.event().entity, &names), action_of(drop.event().dropped, &names, &parents)) else { return };
    if source.starts_with("p:slot|") && source != target && (target.starts_with("p:slot|") || target.starts_with("p:drop|")) {
        intents.0.push(format!("drag>{source}>{target}"));
    }
}

/// Whether the pointer is over UI that takes it: a node that blocks the world below (superui's pass-through chrome does not).
pub fn pointer_over_ui(hover: &bevy::picking::hover::HoverMap, nodes: &Query<Option<&bevy::picking::Pickable>, With<Node>>) -> bool {
    hover.0.values().any(|hits| hits.keys().any(|e| nodes.get(*e).is_ok_and(|p| p.is_none_or(|p| p.should_block_lower))))
}
