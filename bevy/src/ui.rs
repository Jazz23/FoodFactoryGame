// The panel model shared by the in-world screens: a panel describes itself as a UiModel (heading, status, entries: labels,
// buttons, text fields, rows of icon slots) and the front end (front/panel_view.rs) draws it. Buttons queue their action string
// in UiIntents for the owning screen to handle; fields keep their text in UiFields. Widgets never mutate the simulation.
use std::collections::BTreeMap;

use bevy::prelude::*;

pub struct GameUiPlugin;

impl Plugin for GameUiPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<UiModel>().init_resource::<UiIntents>().init_resource::<UiFields>();
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
    /// An icon rendered at runtime (structure cards), drawn like `icon`.
    pub icon_image: Option<Handle<Image>>,
    pub multiline: bool,
    /// A row of entries side by side (slots, tabs, button groups).
    pub row: Vec<UiEntry>,
    /// Highlighted (the selected tab, the active recipe, a full slot).
    pub selected: bool,
    /// Drawn as an inventory slot (a fixed square, icon over a count), filled or empty.
    pub slot: bool,
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
    pub fn with_icon_image(mut self, image: Option<Handle<Image>>) -> Self {
        self.icon_image = image;
        self
    }
    pub fn picture(id: impl Into<String>, image: Handle<Image>, size: f32) -> Self { Self { id: id.into(), picture: Some((image, size)), ..default() } }
    pub fn as_slot(mut self) -> Self {
        self.slot = true;
        self
    }
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
