// Native Bevy UI for menus and panels: a screen describes itself as a UiModel (heading, status, entries: labels, buttons,
// text fields, rows of icon slots) and this draws it, rebuilding only when its layout changes and otherwise updating labels in
// place. Buttons queue their action string in UiIntents for the owning screen to handle; fields keep their text in UiFields.
// Widgets never mutate the simulation.
use std::collections::BTreeMap;

use bevy::prelude::*;
use bevy::text::EditableText;
use bevy::ui_widgets::{Activate, Button, TextInput};

pub struct GameUiPlugin;

impl Plugin for GameUiPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<UiModel>().init_resource::<UiIntents>().init_resource::<UiFields>().add_systems(Update, (read_fields, draw, scroll, text_focus).chain());
    }
}

#[derive(Resource, Default)]
pub struct UiIntents(pub Vec<String>);

#[derive(Resource, Default)]
pub struct UiFields(pub BTreeMap<String, String>);

#[derive(Clone, PartialEq, Default)]
pub struct UiEntry {
    pub id: String,
    pub label: String,
    pub action: Option<String>,
    pub input: Option<String>,
    pub icon: Option<String>,
    pub multiline: bool,
    /// A row of entries side by side (slots, tabs, button groups).
    pub row: Vec<UiEntry>,
    /// Highlighted (the selected tab, the active recipe, a full slot).
    pub selected: bool,
    /// A picture drawn at this size (the phone's map).
    pub picture: Option<(Handle<Image>, f32)>,
}

impl UiEntry {
    pub fn label(id: impl Into<String>, label: impl Into<String>) -> Self { Self { id: id.into(), label: label.into(), ..default() } }
    pub fn button(action: impl Into<String>, label: impl Into<String>) -> Self {
        let action = action.into();
        Self { id: action.clone(), label: label.into(), action: Some(action), ..default() }
    }
    pub fn field(id: impl Into<String>, value: impl Into<String>) -> Self { Self { id: id.into(), input: Some(value.into()), ..default() } }
    pub fn area(id: impl Into<String>, value: impl Into<String>) -> Self { Self { id: id.into(), input: Some(value.into()), multiline: true, ..default() } }
    pub fn row(id: impl Into<String>, entries: Vec<UiEntry>) -> Self { Self { id: id.into(), row: entries, ..default() } }
    pub fn with_icon(mut self, path: impl Into<String>) -> Self {
        let path = path.into();
        self.icon = (!path.is_empty()).then_some(path);
        self
    }
    pub fn picture(id: impl Into<String>, image: Handle<Image>, size: f32) -> Self { Self { id: id.into(), picture: Some((image, size)), ..default() } }
    pub fn selected(mut self, selected: bool) -> Self {
        self.selected = selected;
        self
    }
}

#[derive(Resource, Default, Clone, PartialEq)]
pub struct UiModel {
    pub heading: String,
    pub status: String,
    pub footer: String,
    pub open: bool,
    /// A wider panel (machine and phone screens, the script editor).
    pub wide: bool,
    /// Docked on the right (build mode's catalog), leaving the lot in view.
    pub right: bool,
    pub entries: Vec<UiEntry>,
}

#[derive(Component)]
struct UiRoot;
#[derive(Component)]
struct UiLabel(String);
#[derive(Component)]
struct UiField(String);

fn text_focus(focus: Res<bevy::input_focus::InputFocus>, fields: Query<(), With<UiField>>, mut typing: ResMut<crate::actions::UiTyping>) {
    let now = focus.get().is_some_and(|entity| fields.contains(entity));
    if typing.0 != now {
        typing.0 = now;
    }
}

fn scroll(mouse: Res<bevy::input::mouse::AccumulatedMouseScroll>, mut roots: Query<(&mut ScrollPosition, &ComputedNode), With<UiRoot>>, model: Res<UiModel>) {
    if !model.open {
        return;
    }
    for (mut position, node) in &mut roots {
        let max = ((node.content_size().y - node.size().y) * node.inverse_scale_factor()).max(0.0);
        position.y = (position.y - mouse.delta.y * 32.0).clamp(0.0, max);
    }
}

fn read_fields(fields: Query<(&UiField, &EditableText), Changed<EditableText>>, mut values: ResMut<UiFields>) {
    for (field, text) in &fields {
        values.0.insert(field.0.clone(), text.value().to_string());
    }
}

const TEXT: Color = Color::srgb(0.91, 0.94, 0.96);
const BUTTON: Color = Color::srgb(0.12, 0.26, 0.30);
const SELECTED: Color = Color::srgb(0.22, 0.45, 0.38);

fn text(commands: &mut Commands, parent: Entity, id: &str, value: &str, size: f32) {
    let child = commands.spawn((Text::new(value), UiLabel(id.into()), TextFont { font_size: FontSize::Px(size), ..default() }, TextColor(TEXT))).id();
    commands.entity(parent).add_child(child);
}

fn same_layout(a: &[UiEntry], b: &[UiEntry]) -> bool {
    a.len() == b.len()
        && a.iter().zip(b).all(|(a, b)| a.id == b.id && a.action == b.action && a.icon == b.icon && a.input.is_some() == b.input.is_some() && a.selected == b.selected && a.picture == b.picture && same_layout(&a.row, &b.row))
}

fn find<'a>(entries: &'a [UiEntry], id: &str) -> Option<&'a UiEntry> {
    entries.iter().find_map(|e| if e.id == id { Some(e) } else { find(&e.row, id) })
}

fn entry(commands: &mut Commands, assets: &AssetServer, fields: &UiFields, parent: Entity, entry: &UiEntry) {
    if let Some((image, size)) = &entry.picture {
        let picture = commands.spawn((ImageNode::new(image.clone()), Node { width: px(*size), height: px(*size), flex_shrink: 0.0, ..default() })).id();
        commands.entity(parent).add_child(picture);
        return;
    }
    if !entry.row.is_empty() {
        let row = commands.spawn(Node { flex_direction: FlexDirection::Row, flex_wrap: FlexWrap::Wrap, column_gap: px(6), row_gap: px(6), flex_shrink: 0.0, ..default() }).id();
        commands.entity(parent).add_child(row);
        for child in &entry.row {
            self::entry(commands, assets, fields, row, child);
        }
        return;
    }
    if let Some(default_value) = &entry.input {
        let value = fields.0.get(&entry.id).unwrap_or(default_value);
        let mut editable = EditableText::new(value.clone());
        editable.allow_newlines = entry.multiline;
        let field = commands.spawn((
            UiField(entry.id.clone()),
            TextInput,
            editable,
            TextFont { font_size: FontSize::Px(if entry.multiline { 15.0 } else { 17.0 }), ..default() },
            Node { min_height: px(if entry.multiline { 260 } else { 34 }), min_width: px(240), padding: UiRect::all(px(7)), flex_shrink: 0.0, ..default() },
            BackgroundColor(Color::srgb(0.13, 0.17, 0.21)),
        )).id();
        commands.entity(parent).add_child(field);
        return;
    }
    let Some(action) = &entry.action else {
        text(commands, parent, &entry.id, &entry.label, 16.0);
        return;
    };
    let action = action.clone();
    let slot = entry.icon.is_some();
    let button = commands
        .spawn((
            Button,
            Node {
                min_height: px(32),
                min_width: if slot { px(64) } else { auto() },
                padding: UiRect::all(px(6)),
                flex_direction: if slot { FlexDirection::Column } else { FlexDirection::Row },
                align_items: AlignItems::Center,
                column_gap: px(8),
                flex_shrink: 0.0,
                border_radius: BorderRadius::all(px(5)),
                ..default()
            },
            BackgroundColor(if entry.selected { SELECTED } else { BUTTON }),
        ))
        .observe(move |_: On<Activate>, mut intents: ResMut<UiIntents>| intents.0.push(action.clone()))
        .id();
    commands.entity(parent).add_child(button);
    if let Some(path) = &entry.icon {
        let image = commands.spawn((ImageNode::new(assets.load(path.clone())), Node { width: px(40), height: px(40), ..default() })).id();
        commands.entity(button).add_child(image);
    }
    let size = if slot { 13.0 } else { 16.0 };
    let child = commands.spawn((Text::new(entry.label.clone()), UiLabel(entry.id.clone()), TextFont { font_size: FontSize::Px(size), ..default() }, TextColor(TEXT))).id();
    commands.entity(button).add_child(child);
}

fn draw(
    mut commands: Commands, assets: Res<AssetServer>, model: Res<UiModel>, fields: Res<UiFields>, roots: Query<Entity, With<UiRoot>>, mut labels: Query<(&UiLabel, &mut Text)>,
    mut previous: Local<Option<UiModel>>,
) {
    if previous.as_ref() == Some(&*model) {
        return;
    }
    let same = previous.as_ref().is_some_and(|old| old.open == model.open && old.wide == model.wide && old.right == model.right && same_layout(&old.entries, &model.entries));
    if same {
        for (id, mut label) in &mut labels {
            let value = match id.0.as_str() {
                "heading" => Some(model.heading.as_str()),
                "status" => Some(model.status.as_str()),
                "footer" => Some(model.footer.as_str()),
                key => find(&model.entries, key).map(|e| e.label.as_str()),
            };
            if let Some(value) = value {
                if label.0 != value {
                    label.0 = value.into();
                }
            }
        }
        *previous = Some(model.clone());
        return;
    }
    for root in &roots {
        commands.entity(root).despawn();
    }
    let width = if !model.open { px(420) } else if model.wide { px(760) } else { px(600) };
    let root = commands
        .spawn((
            UiRoot,
            Node {
                position_type: PositionType::Absolute,
                left: if model.right { auto() } else { px(20) },
                right: if model.right { px(20) } else { auto() },
                top: px(16),
                width,
                max_height: percent(88),
                flex_direction: FlexDirection::Column,
                row_gap: px(8),
                padding: UiRect::all(px(if model.open { 18 } else { 12 })),
                border_radius: BorderRadius::all(px(12)),
                overflow: Overflow::scroll_y(),
                ..default()
            },
            BackgroundColor(Color::srgba(0.035, 0.055, 0.075, if model.open { 0.96 } else { 0.72 })),
        ))
        .id();
    text(&mut commands, root, "heading", &model.heading, if model.open { 25.0 } else { 19.0 });
    text(&mut commands, root, "status", &model.status, if model.open { 16.0 } else { 14.0 });
    if model.open {
        for item in &model.entries {
            entry(&mut commands, &assets, &fields, root, item);
        }
    }
    text(&mut commands, root, "footer", &model.footer, 13.0);
    *previous = Some(model.clone());
}
