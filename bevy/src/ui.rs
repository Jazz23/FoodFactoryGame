// Native Bevy UI projects read-only presentation data and queues intents; widgets never mutate the simulation.
use std::collections::BTreeMap;
use bevy::prelude::*;
use bevy::text::EditableText;
use bevy::ui_widgets::{Activate, Button, TextInput};

pub struct GameUiPlugin;
impl Plugin for GameUiPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<UiModel>()
            .init_resource::<UiIntents>()
            .init_resource::<UiFields>()
            .add_systems(Update, (read_fields, draw, scroll, text_focus).chain());
    }
}

#[derive(Resource, Default)]
pub struct UiIntents(pub Vec<String>);
#[derive(Resource, Default)]
pub struct UiFields(pub BTreeMap<String, String>);
#[derive(Clone, PartialEq)]
pub struct UiEntry {
    pub id: String,
    pub label: String,
    pub action: Option<String>,
    pub input: Option<String>,
    pub icon: Option<String>,
}
impl UiEntry {
    pub fn label(id: impl Into<String>, label: impl Into<String>) -> Self {
        Self { id: id.into(), label: label.into(), action: None, input: None, icon: None }
    }
    pub fn button(action: impl Into<String>, label: impl Into<String>) -> Self {
        let action = action.into();
        Self { id: action.clone(), label: label.into(), action: Some(action), input: None, icon: None }
    }
    pub fn field(id: impl Into<String>, value: impl Into<String>) -> Self {
        Self { id: id.into(), label: String::new(), action: None, input: Some(value.into()), icon: None }
    }
    pub fn with_icon(mut self, path: impl Into<String>) -> Self { self.icon = Some(path.into()); self }
}
#[derive(Resource, Default, Clone, PartialEq)]
pub struct UiModel {
    pub heading: String,
    pub status: String,
    pub footer: String,
    pub open: bool,
    pub entries: Vec<UiEntry>,
}
#[derive(Component)]
struct UiRoot;
#[derive(Component)]
struct UiLabel(String);
#[derive(Component)]
struct UiField(String);

fn text_focus(focus: Res<bevy::input_focus::InputFocus>, fields: Query<(), With<UiField>>,
    mut typing: ResMut<crate::actions::UiTyping>) {
    typing.0 = focus.get().is_some_and(|entity| fields.contains(entity));
}

fn scroll(
    mouse: Res<bevy::input::mouse::AccumulatedMouseScroll>,
    mut roots: Query<(&mut ScrollPosition, &ComputedNode), With<UiRoot>>,
) {
    for (mut position, node) in &mut roots {
        let max = ((node.content_size().y - node.size().y) * node.inverse_scale_factor()).max(0.0);
        position.y = (position.y - mouse.delta.y * 32.0).clamp(0.0, max);
    }
}

fn read_fields(fields: Query<(&UiField, &EditableText), Changed<EditableText>>, mut values: ResMut<UiFields>) {
    for (field, text) in &fields { values.0.insert(field.0.clone(), text.value().to_string()); }
}

fn text(commands: &mut Commands, parent: Entity, id: &str, value: &str, size: f32) {
    let child = commands.spawn((
        Text::new(value), UiLabel(id.into()),
        TextFont { font_size: FontSize::Px(size), ..default() },
        TextColor(Color::srgb(0.91, 0.94, 0.96)),
    )).id();
    commands.entity(parent).add_child(child);
}

fn draw(
    mut commands: Commands,
    assets: Res<AssetServer>,
    model: Res<UiModel>,
    fields: Res<UiFields>,
    roots: Query<Entity, With<UiRoot>>,
    mut labels: Query<(&UiLabel, &mut Text)>,
    mut previous: Local<Option<UiModel>>,
) {
    if previous.as_ref() == Some(&*model) { return; }
    let same_layout = previous.as_ref().is_some_and(|old| old.open == model.open
        && old.entries.len() == model.entries.len()
        && old.entries.iter().zip(&model.entries).all(|(a,b)| a.id == b.id && a.action == b.action && a.icon == b.icon && a.input.is_some() == b.input.is_some()));
    if same_layout {
        for (id, mut label) in &mut labels {
            let value = match id.0.as_str() {
                "heading" => Some(model.heading.as_str()),
                "status" => Some(model.status.as_str()),
                "footer" => Some(model.footer.as_str()),
                key => model.entries.iter().find(|e| e.id == key).map(|e| e.label.as_str()),
            };
            if let Some(value) = value { if label.0 != value { label.0 = value.into(); } }
        }
        *previous = Some(model.clone());
        return;
    }
    for root in &roots { commands.entity(root).despawn(); }
    let root = commands.spawn((UiRoot, Node {
        position_type: PositionType::Absolute, left: px(20), top: px(16),
        width: if model.open { px(600) } else { px(500) }, max_height: percent(95),
        flex_direction: FlexDirection::Column, row_gap: px(9),
        padding: UiRect::all(px(18)), border_radius: BorderRadius::all(px(12)),
        overflow: Overflow::scroll_y(), ..default()
    }, BackgroundColor(Color::srgba(0.035, 0.055, 0.075, if model.open { 0.97 } else { 0.83 })))).id();
    text(&mut commands, root, "heading", &model.heading, 25.0);
    text(&mut commands, root, "status", &model.status, 16.0);
    if model.open {
        for entry in &model.entries {
            if let Some(default_value) = &entry.input {
                let value = fields.0.get(&entry.id).unwrap_or(default_value);
                let field = commands.spawn((UiField(entry.id.clone()), TextInput,
                    EditableText::new(value.clone()),
                    TextFont { font_size: FontSize::Px(17.0), ..default() },
                    Node { min_height: px(34), padding: UiRect::all(px(7)), ..default() },
                    BackgroundColor(Color::srgb(0.13, 0.17, 0.21)),
                )).id();
                commands.entity(root).add_child(field);
            } else if let Some(action) = &entry.action {
                let action = action.clone();
                let button = commands.spawn((Button, Node {
                    min_height: px(32), padding: UiRect::all(px(7)),
                    align_items: AlignItems::Center, column_gap: px(8), flex_shrink: 0.0,
                    border_radius: BorderRadius::all(px(5)), ..default()
                }, BackgroundColor(Color::srgb(0.12, 0.26, 0.30))))
                    .observe(move |_: On<Activate>, mut intents: ResMut<UiIntents>| { intents.0.push(action.clone()); }).id();
                commands.entity(root).add_child(button);
                if let Some(path) = &entry.icon {
                    let image = commands.spawn((ImageNode::new(assets.load(path.clone())),
                        Node { width: px(36), height: px(36), ..default() })).id();
                    commands.entity(button).add_child(image);
                }
                text(&mut commands, button, &entry.id, &entry.label, 16.0);
            } else { text(&mut commands, root, &entry.id, &entry.label, 16.0); }
        }
    }
    text(&mut commands, root, "footer", &model.footer, 13.0);
    *previous = Some(model.clone());
}
