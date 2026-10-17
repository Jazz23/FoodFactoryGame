// The panel model shared by the in-world screens: a panel describes itself as a UiModel (heading, status, entries: labels,
// buttons, text fields, rows of icon slots) and the superui front end (front.rs) draws it. Buttons queue their action string
// in UiIntents for the owning screen to handle; fields keep their text in UiFields. Widgets never mutate the simulation.
use std::collections::BTreeMap;

use bevy::prelude::*;
use serde::Serialize;

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

/// An entry as the front end draws it: icons as asset paths, runtime images (structure cards, the phone map) by key.
#[derive(Serialize, PartialEq, Clone, Default)]
pub struct EntryDto {
    pub id: String,
    pub label: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub action: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub input: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub icon: Option<String>,
    /// A runtime image the front end attaches by element id (`rt-<key>`), with its size in pixels.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub image: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub size: Option<f32>,
    #[serde(skip_serializing_if = "std::ops::Not::not")]
    pub multiline: bool,
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub row: Vec<EntryDto>,
    #[serde(skip_serializing_if = "std::ops::Not::not")]
    pub selected: bool,
    #[serde(skip_serializing_if = "std::ops::Not::not")]
    pub slot: bool,
}

#[derive(Serialize, PartialEq, Clone, Default)]
pub struct PanelDto {
    pub heading: String,
    pub status: String,
    pub footer: String,
    pub wide: bool,
    pub right: bool,
    pub entries: Vec<EntryDto>,
}

/// Runtime images a panel shows, keyed as its entries name them.
#[derive(Resource, Default)]
pub struct RuntimeImages(pub BTreeMap<String, Handle<Image>>);

impl UiModel {
    /// The open panel as the front end draws it, collecting its runtime images. Field values start from what was typed.
    pub fn to_dto(&self, fields: &UiFields, images: &mut RuntimeImages) -> PanelDto {
        fn entry(e: &UiEntry, fields: &UiFields, images: &mut RuntimeImages) -> EntryDto {
            // A picture is keyed by its entry (the phone map), an icon by its image.
            let mut image = |key: String, handle: &Handle<Image>| {
                let key: String = key.chars().filter(|c| c.is_ascii_alphanumeric() || *c == '-').collect();
                images.0.insert(key.clone(), handle.clone());
                key
            };
            let (picture, size) = match &e.picture {
                Some((handle, size)) => (Some(image(e.id.clone(), handle)), Some(*size)),
                None => (e.icon_image.as_ref().map(|h| image(format!("img-{}", h.id()), h)), None),
            };
            EntryDto {
                id: e.id.clone(),
                label: e.label.clone(),
                action: e.action.clone(),
                input: e.input.as_ref().map(|default| fields.0.get(&e.id).cloned().unwrap_or_else(|| default.clone())),
                icon: e.icon.clone(),
                image: picture,
                size,
                multiline: e.multiline,
                row: e.row.iter().map(|c| entry(c, fields, images)).collect(),
                selected: e.selected,
                slot: e.slot,
            }
        }
        PanelDto {
            heading: self.heading.clone(),
            status: self.status.clone(),
            footer: self.footer.clone(),
            wide: self.wide,
            right: self.right,
            entries: self.entries.iter().map(|e| entry(e, fields, images)).collect(),
        }
    }
}
