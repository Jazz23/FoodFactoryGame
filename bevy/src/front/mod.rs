// The game UI in bevy_ui, after the "Main Menu" and "Food Factory UI" designs: menus, world select, settings, the HUD and the
// in-world panels. The game writes one frame resource per surface (menu, settings, hud, tooltip) plus the panel model; each
// surface owns a root node and rebuilds its subtree only when its frame changes. Buttons queue the same action strings the
// screens handle (UiIntents); text fields keep their text in UiFields. Hover colours ease like the designs' 0.12 s
// transitions. Passive nodes ignore the pointer, so only buttons, fields and panel cards block the world below.
use std::collections::HashMap;

use bevy::ecs::system::SystemParam;
use bevy::input_focus::tab_navigation::TabIndex;
use bevy::input_focus::InputFocus;
use bevy::picking::events::{PointerClick, PointerDragDrop, PointerDragEnd, PointerDragStart, PointerPress};
use bevy::picking::hover::Hovered;
use bevy::picking::pointer::PointerButton;
use bevy::picking::Pickable;
use bevy::prelude::*;
use bevy::text::{EditableText, FontSize, LetterSpacing, LineHeight, TextEdit};
use bevy::ui::{RelativeCursorPosition, ScrollPosition, UiSystems};
use bevy::ui_widgets::{ScrollArea, TextInput};

use crate::ui::{UiFields, UiIntents, UiModel};

mod hud_view;
mod menu_view;
mod panel_view;
mod settings_view;

pub use hud_view::{AlertDto, HotbarSlotDto, HudFrame, KeyHintDto, TooltipFrame};
pub use menu_view::{MenuFrame, ResumeDto, WorldDto};
pub use settings_view::{BindDto, BindGroupDto, SettingsFrame};

pub struct FrontPlugin;

impl Plugin for FrontPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<Fonts>()
            .init_resource::<MenuFrame>()
            .init_resource::<SettingsFrame>()
            .init_resource::<HudFrame>()
            .init_resource::<TooltipFrame>()
            .init_resource::<menu_view::MenuLocal>()
            .init_resource::<Scrolls>()
            .init_resource::<Dragging>()
            .init_resource::<SliderDrag>()
            .add_observer(on_click)
            .add_observer(secondary_click)
            .add_observer(drag_drop)
            .add_observer(drag_start)
            .add_observer(drag_end)
            .add_observer(field_press)
            .add_observer(slider_press)
            .add_systems(Startup, mount)
            .add_systems(Update, (fit_scale, ease_looks, field_frames, sync_fields, keep_focus, placeholders, drive_sliders, show_sliders, remember_scrolls))
            .add_systems(
                PostUpdate,
                (hud_view::draw_hud, hud_view::draw_tooltip, panel_view::draw_panel, menu_view::draw_menu, settings_view::draw_settings).before(UiSystems::Layout),
            );
    }
}

/// The root node of each surface, back to front.
#[derive(Resource)]
struct Roots {
    hud: Entity,
    tooltip: Entity,
    panel: Entity,
    menu: Entity,
    settings: Entity,
}

fn mount(mut commands: Commands) {
    let mut root = |name: &'static str, z: i32| commands.spawn((fill(), Pickable::IGNORE, GlobalZIndex(z), Name::new(name))).id();
    let roots = Roots { hud: root("HUD", 10), tooltip: root("Tooltip", 11), panel: root("Panel", 12), menu: root("Menus", 13), settings: root("Settings", 15) };
    commands.insert_resource(roots);
}

/// The designs are drawn at 1600 x 900: the UI scales so that frame fits the window.
fn fit_scale(window: Query<&Window, With<bevy::window::PrimaryWindow>>, mut scale: ResMut<UiScale>) {
    let Ok(window) = window.single() else { return };
    let fit = (window.width() / 1600.0).min(window.height() / 900.0).clamp(0.5, 3.0);
    if (scale.0 - fit).abs() > 0.001 {
        scale.0 = fit;
    }
}

/// Replaces a surface's subtree.
fn rebuild(commands: &mut Commands, root: Entity, build: impl FnOnce(&mut Kids)) {
    commands.entity(root).despawn_children().with_children(build);
}

// ---- palette and type ----

pub const fn hex(rgb: u32) -> Color { Color::srgb((rgb >> 16 & 255) as f32 / 255.0, (rgb >> 8 & 255) as f32 / 255.0, (rgb & 255) as f32 / 255.0) }
pub const fn rgba(r: u8, g: u8, b: u8, a: f32) -> Color { Color::srgba(r as f32 / 255.0, g as f32 / 255.0, b as f32 / 255.0, a) }

pub const SCENE: Color = hex(0x3A3935);
pub const GRID: Color = hex(0x3F3E3A);
pub const GLASS: Color = rgba(22, 23, 26, 0.86);
pub const GLASS_SOFT: Color = rgba(22, 23, 26, 0.6);
pub const CARD: Color = hex(0x1B1C1F);
pub const LINE: Color = hex(0x34373C);
pub const LINE_2: Color = hex(0x3C3F44);
pub const LINE_3: Color = hex(0x4A4D53);
pub const RAISED: Color = hex(0x2A2C30);
pub const BUTTON: Color = hex(0x232528);
pub const SLOT: Color = hex(0x26282C);
pub const SLOT_EMPTY: Color = hex(0x222327);
pub const SUNK: Color = hex(0x141517);
pub const TEXT: Color = hex(0xECE8E1);
pub const TEXT_2: Color = hex(0xCFCBC3);
pub const MUTED: Color = hex(0xA8A49B);
pub const DIM: Color = hex(0x8F8B83);
pub const FAINT: Color = hex(0x6A675F);
pub const ACCENT: Color = hex(0xF2A33A);
pub const ACCENT_HOVER: Color = hex(0xF7B252);
pub const ACCENT_INK: Color = hex(0x3B2A12);
pub const INK: Color = hex(0x16171A);
pub const GREEN: Color = hex(0x6FCB86);
pub const ORANGE: Color = hex(0xFF9A6B);
pub const AMBER: Color = hex(0xFFC170);
pub const CLEAR: Color = Color::NONE;

/// A CSS-style colour string from the game ("#E5644E").
pub fn css(colour: &str) -> Color { Srgba::hex(colour.trim_start_matches('#')).map_or(TEXT, Color::from) }

#[derive(Clone, Copy)]
pub enum Face {
    Regular,
    Medium,
    Bold,
    Mono,
    MonoBold,
}

#[derive(Resource)]
pub struct Fonts {
    regular: Handle<Font>,
    medium: Handle<Font>,
    bold: Handle<Font>,
    mono: Handle<Font>,
    mono_bold: Handle<Font>,
}

impl FromWorld for Fonts {
    fn from_world(world: &mut World) -> Self {
        let assets = world.resource::<AssetServer>();
        Self {
            regular: assets.load("fonts/Rubik-Regular.ttf"),
            medium: assets.load("fonts/Rubik-Medium.ttf"),
            bold: assets.load("fonts/Rubik-Bold.ttf"),
            mono: assets.load("fonts/JetBrainsMono-Medium.ttf"),
            mono_bold: assets.load("fonts/JetBrainsMono-SemiBold.ttf"),
        }
    }
}

impl Fonts {
    fn get(&self, face: Face) -> Handle<Font> {
        match face {
            Face::Regular => self.regular.clone(),
            Face::Medium => self.medium.clone(),
            Face::Bold => self.bold.clone(),
            Face::Mono => self.mono.clone(),
            Face::MonoBold => self.mono_bold.clone(),
        }
    }
}

// ---- building blocks ----

pub type Kids<'a> = ChildSpawnerCommands<'a>;

/// What a surface needs to build: fonts, icons, typed field values and remembered scroll offsets.
#[derive(SystemParam)]
pub struct Ui<'w> {
    fonts: Res<'w, Fonts>,
    assets: Res<'w, AssetServer>,
    fields: Res<'w, UiFields>,
    scrolls: Res<'w, Scrolls>,
}

pub const fn pct(v: f32) -> Val { Val::Percent(v) }

pub fn row() -> Node { Node { flex_direction: FlexDirection::Row, align_items: AlignItems::Center, ..default() } }
pub fn col() -> Node { Node { flex_direction: FlexDirection::Column, ..default() } }
pub fn fill() -> Node { Node { position_type: PositionType::Absolute, left: px(0.0), top: px(0.0), right: px(0.0), bottom: px(0.0), ..default() } }
pub fn radius(r: f32) -> BorderRadius { BorderRadius::all(px(r)) }

/// A node that never takes the pointer.
pub fn div<'a>(p: &'a mut Kids, node: Node) -> EntityCommands<'a> { p.spawn((node, Pickable::IGNORE)) }

/// A passive box with a background and (when the node has a border width) a border colour.
pub fn boxed<'a>(p: &'a mut Kids, node: Node, bg: Color, border: Color) -> EntityCommands<'a> { p.spawn((node, BackgroundColor(bg), BorderColor::all(border), Pickable::IGNORE)) }

pub fn text<'a>(p: &'a mut Kids, ui: &Ui, value: impl Into<String>, face: Face, size: f32, colour: Color) -> EntityCommands<'a> {
    p.spawn((Text::new(value), TextFont { font: ui.fonts.get(face).into(), font_size: FontSize::Px(size), ..default() }, TextColor(colour), Pickable::IGNORE))
}

/// Text centred within its own box (the design's `text-align: center`).
pub fn centred() -> TextLayout { TextLayout { justify: Justify::Center, ..default() } }

pub fn spacing(px: f32) -> LetterSpacing { LetterSpacing::Px(px) }

/// A design icon (a PNG rasterized from its SVG, in the colour the design draws it).
pub fn icon(p: &mut Kids, ui: &Ui, name: &str, size: f32, colour: &str) {
    let image = ui.assets.load(format!("ui/game/icons/{name}-{colour}.png"));
    p.spawn((ImageNode::new(image), Node { width: px(size), height: px(size), flex_shrink: 0.0, ..default() }, Pickable::IGNORE));
}

/// An asset image (item and machine icons) at a square size.
pub fn picture(p: &mut Kids, image: Handle<Image>, size: f32) {
    p.spawn((ImageNode::new(image), Node { width: px(size), height: px(size), flex_shrink: 0.0, ..default() }, Pickable::IGNORE));
}

pub fn divider(p: &mut Kids, margin: f32) { boxed(p, Node { height: px(1.0), margin: UiRect::vertical(px(margin)), flex_shrink: 0.0, ..default() }, LINE, CLEAR); }

/// A small key cap beside a hint ("Tab Navigate").
pub fn hint(p: &mut Kids, ui: &Ui, key: &str, label: &str) {
    div(p, Node { column_gap: px(6.0), ..row() }).with_children(|p| {
        boxed(p, Node { padding: UiRect::axes(px(6.0), px(2.0)), border_radius: radius(4.0), ..default() }, GLASS, CLEAR).with_children(|p| {
            text(p, ui, key, Face::Mono, 12.0, TEXT);
        });
        text(p, ui, label, Face::Regular, 12.0, MUTED);
    });
}

/// A scrolling column that keeps its offset across rebuilds.
pub fn scroller(key: &'static str, ui: &Ui) -> impl Bundle {
    (ScrollArea, Scroller(key), ScrollPosition(ui.scrolls.0.get(key).copied().unwrap_or_default()))
}

pub fn scroll_y() -> Overflow { Overflow { x: OverflowAxis::Clip, y: OverflowAxis::Scroll } }

// ---- buttons ----

/// What a click does: a game intent, an in-world panel action (which also takes right clicks and slot drags), or the menus'
/// own page state.
#[derive(Component, Clone)]
pub enum Click {
    Intent(String),
    Panel(String),
    Menu(menu_view::MenuClick),
}

/// A button's colours at rest and under the pointer; the drawn colours ease toward the target.
#[derive(Component, Clone, Copy)]
pub struct Look {
    pub bg: Color,
    pub bg_hover: Color,
    pub border: Color,
    pub border_hover: Color,
}

impl Look {
    pub const fn new(bg: Color, border: Color) -> Self { Self { bg, bg_hover: bg, border, border_hover: border } }
    pub const fn hover_bg(mut self, bg: Color) -> Self {
        self.bg_hover = bg;
        self
    }
    pub const fn hover_border(mut self, border: Color) -> Self {
        self.border_hover = border;
        self
    }
}

pub fn button<'a>(p: &'a mut Kids, node: Node, look: Look, click: Click) -> EntityCommands<'a> {
    p.spawn((node, BackgroundColor(look.bg), BorderColor::all(look.border), look, Hovered::default(), click))
}

fn mix(from: Color, to: Color, t: f32) -> Color {
    let (a, b) = (from.to_srgba(), to.to_srgba());
    Color::srgba(a.red + (b.red - a.red) * t, a.green + (b.green - a.green) * t, a.blue + (b.blue - a.blue) * t, a.alpha + (b.alpha - a.alpha) * t)
}

fn near(a: Color, b: Color) -> bool {
    let (a, b) = (a.to_srgba(), b.to_srgba());
    (a.red - b.red).abs() + (a.green - b.green).abs() + (a.blue - b.blue).abs() + (a.alpha - b.alpha).abs() < 0.004
}

fn ease_looks(time: Res<Time>, mut looks: Query<(&Look, &Hovered, &mut BackgroundColor, &mut BorderColor)>) {
    let t = (time.delta_secs() / 0.12 * 3.0).min(1.0);
    for (look, hovered, mut bg, mut border) in &mut looks {
        let (bg_target, border_target) = if hovered.0 { (look.bg_hover, look.border_hover) } else { (look.bg, look.border) };
        if !near(bg.0, bg_target) {
            bg.0 = mix(bg.0, bg_target, t);
        } else if bg.0 != bg_target {
            bg.0 = bg_target;
        }
        if !near(border.top, border_target) {
            *border = BorderColor::all(mix(border.top, border_target, t));
        } else if border.top != border_target {
            *border = BorderColor::all(border_target);
        }
    }
}

fn on_click(click: On<PointerClick>, clicks: Query<&Click>, mut intents: ResMut<UiIntents>, mut local: ResMut<menu_view::MenuLocal>, menu: Res<MenuFrame>) {
    if click.event().button != PointerButton::Primary {
        return;
    }
    match clicks.get(click.event().entity) {
        Ok(Click::Intent(action) | Click::Panel(action)) => intents.0.push(action.clone()),
        Ok(Click::Menu(m)) => menu_view::menu_click(m, &mut local, &menu, &mut intents),
        Err(_) => {}
    }
}

/// The panel action of exactly this element: pointer events bubble, so a global observer sees each ancestor and acts once, at
/// the element that carries the action.
fn panel_action(entity: Entity, clicks: &Query<&Click>) -> Option<String> {
    match clicks.get(entity) {
        Ok(Click::Panel(action)) => Some(action.clone()),
        _ => None,
    }
}

/// A right click sends the button's action marked `|alt` (Unity's right click on a slot takes half its stack).
fn secondary_click(click: On<PointerClick>, clicks: Query<&Click>, mut intents: ResMut<UiIntents>) {
    if click.event().button != PointerButton::Secondary {
        return;
    }
    if let Some(action) = panel_action(click.event().entity, &clicks) {
        intents.0.push(format!("{action}|alt"));
    }
}

/// A slot dragged onto another sends `drag>SOURCE>TARGET` (Unity's slot drag and drop).
fn drag_drop(drop: On<PointerDragDrop>, clicks: Query<&Click>, parents: Query<&ChildOf>, mut intents: ResMut<UiIntents>) {
    let Some(target) = panel_action(drop.event().entity, &clicks) else { return };
    let mut source = None;
    let mut entity = drop.event().dropped;
    for _ in 0..8 {
        if let Some(action) = panel_action(entity, &clicks) {
            source = Some(action);
            break;
        }
        let Ok(parent) = parents.get(entity) else { break };
        entity = parent.parent();
    }
    let Some(source) = source else { return };
    if source.starts_with("p:slot|") && source != target && (target.starts_with("p:slot|") || target.starts_with("p:drop|")) {
        intents.0.push(format!("drag>{source}>{target}"));
    }
}

/// Whether a drag is under way: the panel waits to rebuild until it ends, so the dragged slot survives to its drop.
#[derive(Resource, Default)]
pub struct Dragging(pub bool);

fn drag_start(start: On<PointerDragStart>, clicks: Query<&Click>, mut dragging: ResMut<Dragging>) {
    if clicks.contains(start.event().entity) {
        dragging.0 = true;
    }
}

fn drag_end(_: On<PointerDragEnd>, mut dragging: ResMut<Dragging>) { dragging.0 = false; }

/// Whether the pointer is over UI that takes it: a node that blocks the world below (passive chrome does not).
pub fn pointer_over_ui(hover: &bevy::picking::hover::HoverMap, nodes: &Query<Option<&Pickable>, With<Node>>) -> bool {
    hover.0.values().any(|hits| hits.keys().any(|e| nodes.get(*e).is_ok_and(|p| p.is_none_or(|p| p.should_block_lower))))
}

// ---- text fields ----

/// A text field's editable text, reporting its value to UiFields under `id`.
#[derive(Component)]
pub struct Field {
    pub id: String,
}

/// A field's outer box: its border turns accent while the field inside has focus.
#[derive(Component)]
pub struct FieldFrame {
    border: Color,
}

/// Grey text shown in an empty field.
#[derive(Component)]
struct Placeholder(Entity);

pub struct FieldStyle {
    pub node: Node,
    pub bg: Color,
    pub border: Color,
    pub face: Face,
    pub size: f32,
    pub colour: Color,
    pub multiline: bool,
}

/// A text input that reports each edit as a field and tells the game while it has focus. It starts from what was typed
/// before (UiFields) or else the game's value, so a rebuild never loses typing.
pub fn field(p: &mut Kids, ui: &Ui, id: &str, value: &str, placeholder: &str, style: FieldStyle) {
    let start = ui.fields.0.get(id).cloned().unwrap_or_else(|| value.to_string());
    let font = TextFont { font: ui.fonts.get(style.face).into(), font_size: FontSize::Px(style.size), ..default() };
    let border = style.border;
    p.spawn((Node { position_type: PositionType::Relative, ..style.node }, BackgroundColor(style.bg), BorderColor::all(border), FieldFrame { border }))
        .with_children(|p| {
            let mut editable = EditableText::new(&start);
            editable.allow_newlines = style.multiline;
            let input = p
                .spawn((
                    Node { width: pct(100.0), flex_grow: 1.0, ..default() },
                    TextInput,
                    editable,
                    font.clone(),
                    TextColor(style.colour),
                    TabIndex(0),
                    Field { id: id.to_string() },
                ))
                .id();
            if !placeholder.is_empty() {
                p.spawn((Text::new(placeholder), font, TextColor(FAINT), Node { position_type: PositionType::Absolute, ..default() }, Pickable::IGNORE, Placeholder(input)));
            }
        });
}

/// A press on a field's padding focuses the text inside.
fn field_press(press: On<PointerPress>, frames: Query<&Children, With<FieldFrame>>, fields: Query<(), With<Field>>, mut focus: ResMut<InputFocus>) {
    if press.event().entity != press.original_event_target() {
        return;
    }
    let Ok(children) = frames.get(press.event().entity) else { return };
    if let Some(input) = children.iter().find(|c| fields.contains(*c)) {
        *focus = InputFocus::from_entity(input);
    }
}

fn field_frames(focus: Res<InputFocus>, parents: Query<&ChildOf, With<Field>>, mut frames: Query<(Entity, &FieldFrame, &mut BorderColor)>) {
    let focused = focus.get().and_then(|e| parents.get(e).ok()).map(|c| c.parent());
    for (entity, frame, mut border) in &mut frames {
        let want = if focused == Some(entity) { ACCENT } else { frame.border };
        if border.top != want {
            *border = BorderColor::all(want);
        }
    }
}

fn placeholders(texts: Query<&EditableText>, mut placeholders: Query<(&Placeholder, &mut Visibility)>) {
    for (placeholder, mut visibility) in &mut placeholders {
        let empty = texts.get(placeholder.0).is_ok_and(|t| t.value().to_string().is_empty());
        let want = if empty { Visibility::Inherited } else { Visibility::Hidden };
        if *visibility != want {
            *visibility = want;
        }
    }
}

/// Typed text goes to UiFields; a value the game sets while the field is not focused goes back into the field.
fn sync_fields(
    mut inputs: Query<(Entity, &Field, &mut EditableText)>, mut fields: ResMut<UiFields>, focus: Res<InputFocus>, mut typing: ResMut<crate::actions::UiTyping>,
) {
    for (entity, field, mut editable) in &mut inputs {
        let value = editable.value().to_string();
        if focus.get() == Some(entity) {
            if fields.0.get(&field.id) != Some(&value) {
                fields.0.insert(field.id.clone(), value);
            }
        } else if let Some(set) = fields.0.get(&field.id).filter(|v| **v != value) {
            let set = set.clone();
            editable.editor_mut().set_text(&set);
        }
    }
    let on = focus.get().is_some_and(|e| inputs.contains(e));
    if typing.0 != on {
        typing.0 = on;
    }
}

/// A rebuilt surface replaces its fields: focus moves to the new field with the same id, caret at the end.
fn keep_focus(mut focus: ResMut<InputFocus>, mut inputs: Query<(Entity, &Field, &mut EditableText)>, alive: Query<()>, mut last: Local<Option<String>>) {
    let Some(focused) = focus.get() else {
        *last = None;
        return;
    };
    if let Ok((_, field, _)) = inputs.get(focused) {
        *last = Some(field.id.clone());
        return;
    }
    if alive.contains(focused) {
        *last = None;
        return;
    }
    let found = last.as_ref().and_then(|id| inputs.iter().find(|(_, f, _)| &f.id == id).map(|(e, _, _)| e));
    match found {
        Some(entity) => {
            *focus = InputFocus::from_entity(entity);
            if let Ok((_, _, mut editable)) = inputs.get_mut(entity) {
                editable.queue_edit(TextEdit::TextEnd(false));
            }
        }
        None => {
            focus.clear();
            *last = None;
        }
    }
}

// ---- sliders ----

#[derive(Clone, Copy, PartialEq)]
pub enum SliderFormat {
    Times,
    Percent,
}

impl SliderFormat {
    fn show(self, v: f32) -> String {
        match self {
            SliderFormat::Times => format!("{v:.2}×"),
            SliderFormat::Percent => format!("{}%", (v * 100.0).round() as i32),
        }
    }
}

/// A settings slider's track: the value follows the thumb while dragging and is sent (`s:<id>-set|<value>`) on release.
#[derive(Component)]
pub struct Slider {
    pub id: &'static str,
    pub min: f32,
    pub max: f32,
    pub step: f32,
    pub value: f32,
    pub format: SliderFormat,
    pub fill: Entity,
    pub thumb: Entity,
    pub label: Entity,
}

#[derive(Resource, Default)]
struct SliderDrag(Option<Entity>);

fn slider_press(press: On<PointerPress>, sliders: Query<(), With<Slider>>, mut drag: ResMut<SliderDrag>) {
    if press.event().button == PointerButton::Primary && sliders.contains(press.event().entity) {
        drag.0 = Some(press.event().entity);
    }
}

fn drive_sliders(mouse: Res<ButtonInput<MouseButton>>, mut drag: ResMut<SliderDrag>, mut sliders: Query<(&mut Slider, &RelativeCursorPosition)>, mut intents: ResMut<UiIntents>) {
    let Some(entity) = drag.0 else { return };
    let Ok((mut slider, cursor)) = sliders.get_mut(entity) else {
        drag.0 = None;
        return;
    };
    if let Some(at) = cursor.normalized {
        let t = (at.x + 0.5).clamp(0.0, 1.0);
        let raw = slider.min + (slider.max - slider.min) * t;
        let value = ((raw - slider.min) / slider.step).round() * slider.step + slider.min;
        let value = (value * 1000.0).round() / 1000.0;
        if slider.value != value {
            slider.value = value;
        }
    }
    if !mouse.pressed(MouseButton::Left) {
        intents.0.push(format!("s:{}-set|{}", slider.id, slider.value));
        drag.0 = None;
    }
}

fn show_sliders(sliders: Query<&Slider, Changed<Slider>>, mut nodes: Query<&mut Node>, mut texts: Query<&mut Text>) {
    for slider in &sliders {
        let t = ((slider.value - slider.min) / (slider.max - slider.min)).clamp(0.0, 1.0) * 100.0;
        if let Ok(mut node) = nodes.get_mut(slider.fill) {
            node.width = pct(t);
        }
        if let Ok(mut node) = nodes.get_mut(slider.thumb) {
            node.left = pct(t);
        }
        if let Ok(mut text) = texts.get_mut(slider.label) {
            text.0 = slider.format.show(slider.value);
        }
    }
}

// ---- scrolling ----

/// A scrolling area's key, so its offset survives the surface rebuilding.
#[derive(Component)]
pub struct Scroller(&'static str);

#[derive(Resource, Default)]
pub struct Scrolls(HashMap<&'static str, Vec2>);

fn remember_scrolls(areas: Query<(&Scroller, &ScrollPosition), Changed<ScrollPosition>>, mut scrolls: ResMut<Scrolls>) {
    for (key, position) in &areas {
        scrolls.0.insert(key.0, position.0);
    }
}

/// The menu backdrop: the design's scene colour, its 64 px grid and (on the main menu) the vignette behind the column.
pub fn scene(p: &mut Kids, vignette: bool) {
    p.spawn((Node { overflow: Overflow::clip(), ..fill() }, BackgroundColor(SCENE))).with_children(|p| {
        for x in (0..3200).step_by(64) {
            boxed(p, Node { position_type: PositionType::Absolute, left: px(x as f32), top: px(0.0), bottom: px(0.0), width: px(1.0), ..default() }, GRID, CLEAR);
        }
        for y in (0..1800).step_by(64) {
            boxed(p, Node { position_type: PositionType::Absolute, top: px(y as f32), left: px(0.0), right: px(0.0), height: px(1.0), ..default() }, GRID, CLEAR);
        }
        if vignette {
            vignette_fill(p);
        } else {
            boxed(p, fill(), GLASS.with_alpha(0.55), CLEAR);
        }
    });
}

pub fn vignette_fill(p: &mut Kids) {
    use bevy::ui::{ColorStop, Gradient, RadialGradient, RadialGradientShape, UiPosition};
    let stops = vec![
        ColorStop::new(rgba(22, 23, 26, 0.9), pct(0.0)),
        ColorStop::new(rgba(22, 23, 26, 0.55), pct(60.0)),
        ColorStop::new(rgba(22, 23, 26, 0.0), pct(100.0)),
    ];
    let gradient = RadialGradient::new(UiPosition::CENTER, RadialGradientShape::Ellipse(px(620.0), px(520.0)), stops);
    p.spawn((fill(), BackgroundGradient(vec![Gradient::from(gradient)]), Pickable::IGNORE));
}

/// Line height as a multiple of the font size (the designs' `line-height`).
pub fn line_height(multiple: f32) -> LineHeight { LineHeight::RelativeToFont(multiple) }

/// Whether a panel is drawn (any panel hides parts of the HUD).
pub fn panel_shown(model: &UiModel) -> bool { model.open }
