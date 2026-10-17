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
    /// How the entry is drawn (the design's widgets); Plain keeps the generic look.
    pub style: Style,
    /// A design stroke icon ("target-ff8a78") drawn before the label.
    pub glyph: String,
    /// A status dot's or a value's colour ("#6FCB86").
    pub dot: String,
    /// Takes the free width of its row.
    pub grow: bool,
    /// A slot grid's column count (0: ten, the inventory's).
    pub cols: u16,
    /// A progress bar's fill, 0..1.
    pub fill: f32,
}

/// The design's widgets beyond a plain label, button or slot grid.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub enum Style {
    #[default]
    Plain,
    /// A row of buttons drawn as underlined tabs.
    Tabs,
    /// A row of buttons in a sunk pill, the selected one raised.
    Segmented,
    /// A raised card holding its row as a column (a task).
    Card,
    /// Its row side by side on one line.
    Inline,
    /// A small numbered square.
    Badge,
    /// A place box: icon, caption over name (label "From\nFridge 2"), and its row's buttons at the end.
    Place,
    /// A small bordered button with a glyph ("Set source").
    Pick,
    /// A checkbox with its label; `selected` is checked.
    Check,
    /// A wide outlined "add" button.
    Dashed,
    /// A row of stat tiles.
    Stats,
    /// One stat tile: caption over value (label "HIRED\n3 / 4").
    Stat,
    /// A person row: avatar, name over a status dot line (label "Name\nStatus"), then its row's widgets.
    Person,
    /// A held-goods square with a caption under it ("Hand").
    Hand,
    /// A power switch: title over hint (label "Power\nBaking while on"), `selected` is on.
    Toggle,
    /// A machine's input, progress and output side by side (its row: in, progress, out).
    Flow,
    /// A captioned slot column inside a flow (label is the caption, row the slots).
    Bin,
    /// A progress bar over its label.
    Progress,
    /// A large accent button.
    Accent,
    /// A large outlined button.
    Ghost,
    /// An outlined red button.
    Danger,
    /// A monospace code block.
    Code,
    /// A small muted note with its glyph.
    Note,
    /// A removable item chip (icon, name, and its action removes it).
    Chip,
    /// A box of chips with a field at the end (its row).
    ChipBox,
    /// A coloured-dot legend line (label, dot).
    Legend,
    /// A one-line list row: label left, its row's widgets right.
    Line,
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
    pub fn styled(mut self, style: Style) -> Self {
        self.style = style;
        self
    }
    pub fn glyph(mut self, glyph: impl Into<String>) -> Self {
        self.glyph = glyph.into();
        self
    }
    pub fn dot(mut self, colour: impl Into<String>) -> Self {
        self.dot = colour.into();
        self
    }
    pub fn grow(mut self) -> Self {
        self.grow = true;
        self
    }
    pub fn cols(mut self, cols: u16) -> Self {
        self.cols = cols;
        self
    }
    pub fn fill(mut self, fill: f32) -> Self {
        self.fill = fill;
        self
    }
    pub fn with_row(mut self, row: Vec<UiEntry>) -> Self {
        self.row = row;
        self
    }
    /// A container of the given style around entries.
    pub fn group(id: impl Into<String>, style: Style, row: Vec<UiEntry>) -> Self { Self { id: id.into(), row, style, ..default() } }
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
    /// A person's screen: an avatar before the heading.
    pub avatar: bool,
    /// The status line's dot colour, if any.
    pub dot: String,
    /// Widgets in the header beside the close button (the employee's hands).
    pub header: Vec<UiEntry>,
    /// Underlined tabs under the header, outside the scrolling body.
    pub tabs: Vec<UiEntry>,
    /// Buttons in a footer bar after the footer text (Stop, Run).
    pub actions: Vec<UiEntry>,
    /// A second card left of this one (the carried inventory beside a machine).
    pub side_heading: String,
    pub side_status: String,
    pub side: Vec<UiEntry>,
    /// A fixed card width (0: the default) and height (0: as tall as its content).
    pub width: f32,
    pub height: f32,
    /// Drawn on a phone held sideways: its apps (`tabs`) in a dock, a status bar with this time.
    pub phone: bool,
    pub phone_time: String,
}
