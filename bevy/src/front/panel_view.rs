// The open in-world panel (inventory, a machine, an employee's tasks, the phone, build mode's catalog), drawn from the game's
// UiModel in the "Food Factory UI" design's style: a dark card over a dimmed world (with the carried inventory as a second
// card beside a machine), a header with an optional avatar, status dot and extra widgets, underlined tabs, a scrolling body of
// the design's widgets (slot grids, task cards, place boxes, segmented pickers, switches, stat tiles, person rows) and a footer
// bar with its buttons. The phone is drawn as a phone held sideways: a bezel with its camera, a status bar, an app dock and the
// screen. Every button sends its action; panel buttons also take right clicks and slot drags. A rebuild waits while a slot is
// being dragged, so the drag reaches its drop. A change to the footer alone (build mode's per-cell preview) rewrites that one
// text, so the panel is not rebuilt under a moving pointer.

use super::*;
use crate::ui::{Style, UiEntry};

/// The panel's footer text, rewritten in place when only it changes.
#[derive(Component)]
pub(super) struct PanelFooter;

const CARD_RAISED: Color = hex(0x232528);
const CARD_LINE: Color = hex(0x2E3135);
const RED: Color = hex(0xFF8A78);
const RED_LINE: Color = hex(0x5A3A36);
const SWITCH_ON: Color = hex(0x3E9A57);
const KNOB: Color = hex(0xF4F1EA);
const CODE_BG: Color = hex(0x111214);
const DISABLED: Color = hex(0x4A4C50);

pub(super) fn draw_panel(
    mut commands: Commands, ui: Ui, roots: Res<Roots>, model: Res<UiModel>, dragging: Res<Dragging>, mut last: Local<Option<UiModel>>,
    mut footers: Query<&mut Text, With<PanelFooter>>,
) {
    if dragging.0 || last.as_ref() == Some(&*model) {
        return;
    }
    if let Some(previous) = last.as_mut() {
        let same_body = UiModel { footer: String::new(), ..previous.clone() } == UiModel { footer: String::new(), ..model.clone() };
        if same_body && !previous.footer.is_empty() && !model.footer.is_empty() && !footers.is_empty() {
            for mut footer in &mut footers {
                footer.0 = model.footer.clone();
            }
            previous.footer = model.footer.clone();
            return;
        }
    }
    *last = Some(model.clone());
    rebuild(&mut commands, roots.panel, |p| {
        if !panel_shown(&model) {
            return;
        }
        if model.right {
            let panel = Node { padding: UiRect::new(px(20.0), px(20.0), px(18.0), px(20.0)), row_gap: px(14.0), border: UiRect::all(px(1.0)), border_radius: radius(14.0), ..col() };
            p.spawn((Node { position_type: PositionType::Absolute, right: px(24.0), top: px(24.0), width: px(460.0), max_height: px(760.0), ..panel }, BackgroundColor(CARD), BorderColor::all(LINE)))
                .with_children(|p| {
                    header(p, &ui, &model, false);
                    body(p, &ui, &model.entries, "panel-body", 0.0, 0.0);
                    if !model.footer.is_empty() {
                        text(p, &ui, &model.footer, Face::Regular, 12.0, MUTED).insert(PanelFooter);
                    }
                });
            return;
        }
        boxed(p, fill(), rgba(10, 10, 12, 0.55), CLEAR);
        if model.phone {
            phone(p, &ui, &model);
            return;
        }
        let wrap = Node { position_type: PositionType::Absolute, left: px(0.0), right: px(0.0), top: px(40.0), bottom: px(104.0), justify_content: JustifyContent::Center, align_items: AlignItems::FlexStart, column_gap: px(16.0), ..row() };
        div(p, wrap).with_children(|p| {
            if !model.side.is_empty() {
                let side = UiModel { heading: model.side_heading.clone(), status: model.side_status.clone(), entries: model.side.clone(), ..default() };
                p.spawn(card(Node { width: px(556.0), max_height: pct(100.0), ..col() })).with_children(|p| {
                    header(p, &ui, &side, false);
                    body(p, &ui, &side.entries, "panel-side", 20.0, 22.0);
                });
            }
            let width = if model.width > 0.0 { model.width } else if model.wide { 780.0 } else { 600.0 };
            let mut node = Node { width: px(width), max_height: pct(100.0), ..col() };
            if model.height > 0.0 {
                node.height = px(model.height);
            }
            p.spawn(card(node)).with_children(|p| {
                header(p, &ui, &model, true);
                body(p, &ui, &model.entries, "panel-body", if model.actions.is_empty() { 0.0 } else { 16.0 }, 22.0);
                footer(p, &ui, &model, 22.0);
            });
        });
    });
}

fn card(node: Node) -> (Node, BackgroundColor, BorderColor) {
    (Node { border: UiRect::all(px(1.0)), border_radius: radius(14.0), ..node }, BackgroundColor(CARD), BorderColor::all(LINE))
}

fn close_button(p: &mut Kids, ui: &Ui) {
    let node = Node { width: px(32.0), height: px(32.0), border: UiRect::all(px(1.0)), border_radius: radius(8.0), justify_content: JustifyContent::Center, flex_shrink: 0.0, ..row() };
    button(p, node, Look::new(BUTTON, LINE).hover_bg(RAISED), Click::Intent("p:close".into())).with_children(|p| icon(p, ui, "close", 14.0, "ece8e1"));
}

/// Avatar, heading over its status (with a dot), the header's widgets, the close button, then the tabs.
fn header(p: &mut Kids, ui: &Ui, model: &UiModel, closable: bool) {
    let pad = if model.right { UiRect::ZERO } else { UiRect::new(px(22.0), px(22.0), px(18.0), px(0.0)) };
    div(p, Node { padding: pad, row_gap: px(14.0), flex_shrink: 0.0, ..col() }).with_children(|p| {
        div(p, Node { column_gap: px(14.0), ..row() }).with_children(|p| {
            if model.avatar {
                avatar(p, ui, 44.0);
            }
            div(p, Node { row_gap: px(3.0), flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..col() }).with_children(|p| {
                text(p, ui, &model.heading, Face::Bold, 17.0, TEXT);
                if !model.status.is_empty() {
                    div(p, Node { column_gap: px(6.0), ..row() }).with_children(|p| {
                        if !model.dot.is_empty() {
                            dot(p, css(&model.dot), 7.0);
                        }
                        text(p, ui, &model.status, Face::Regular, 12.0, if model.dot.is_empty() { MUTED } else { TEXT_2 });
                    });
                }
            });
            for e in &model.header {
                entry_view(p, ui, e);
            }
            if closable {
                close_button(p, ui);
            }
        });
        if !model.tabs.is_empty() {
            tabs(p, ui, &model.tabs);
        }
    });
}

fn body(p: &mut Kids, ui: &Ui, entries: &[UiEntry], key: &'static str, bottom: f32, side: f32) {
    let node = Node {
        row_gap: px(10.0),
        overflow: scroll_y(),
        padding: UiRect::new(px(side), px((side - 4.0).max(4.0)), px(if side > 0.0 { 14.0 } else { 0.0 }), px(bottom)),
        flex_grow: 1.0,
        flex_shrink: 1.0,
        min_height: px(0.0),
        ..col()
    };
    p.spawn((node, scroller(key, ui))).with_children(|p| {
        for entry in entries {
            entry_view(p, ui, entry);
        }
    });
}

/// The footer text and, with actions, the design's footer bar (a top rule, the note, then the buttons).
fn footer(p: &mut Kids, ui: &Ui, model: &UiModel, side: f32) {
    if model.actions.is_empty() {
        if !model.footer.is_empty() {
            div(p, Node { padding: UiRect::new(px(side), px(side), px(12.0), px(18.0)), flex_shrink: 0.0, ..default() }).with_children(|p| {
                text(p, ui, &model.footer, Face::Regular, 12.0, MUTED).insert(PanelFooter);
            });
        } else {
            div(p, Node { height: px(18.0), flex_shrink: 0.0, ..default() });
        }
        return;
    }
    boxed(p, Node { padding: UiRect::axes(px(side), px(14.0)), column_gap: px(10.0), border: UiRect::top(px(1.0)), flex_shrink: 0.0, ..row() }, CLEAR, LINE).with_children(|p| {
        text(p, ui, &model.footer, Face::Regular, 12.0, MUTED).insert((PanelFooter, Node { flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..default() }));
        for e in &model.actions {
            entry_view(p, ui, e);
        }
    });
}

fn avatar(p: &mut Kids, ui: &Ui, size: f32) {
    boxed(p, Node { width: px(size), height: px(size), border_radius: radius(size / 2.0), justify_content: JustifyContent::Center, flex_shrink: 0.0, ..row() }, LINE, CLEAR)
        .with_children(|p| icon(p, ui, "person", size / 2.0, "ece8e1"));
}

fn dot(p: &mut Kids, colour: Color, size: f32) {
    boxed(p, Node { width: px(size), height: px(size), border_radius: radius(size / 2.0), flex_shrink: 0.0, ..default() }, colour, CLEAR);
}

/// A design glyph by its file stem ("target-ff8a78").
fn glyph(p: &mut Kids, ui: &Ui, name: &str, size: f32) {
    let image = ui.assets.load(format!("ui/game/icons/{name}.png"));
    p.spawn((ImageNode::new(image), Node { width: px(size), height: px(size), flex_shrink: 0.0, ..default() }, Pickable::IGNORE));
}

fn tabs(p: &mut Kids, ui: &Ui, entries: &[UiEntry]) {
    boxed(p, Node { column_gap: px(4.0), border: UiRect::bottom(px(1.0)), ..row() }, CLEAR, LINE).with_children(|p| {
        for e in entries {
            let node = Node { height: px(40.0), padding: UiRect::horizontal(px(16.0)), border: UiRect::bottom(px(2.0)), column_gap: px(8.0), justify_content: JustifyContent::Center, ..row() };
            let look = Look::new(CLEAR, if e.selected { ACCENT } else { CLEAR }).hover_border(if e.selected { ACCENT } else { LINE_3 });
            button(p, node, look, Click::Panel(e.action.clone().unwrap_or_default())).with_children(|p| {
                if !e.glyph.is_empty() {
                    glyph(p, ui, &e.glyph, 14.0);
                }
                text(p, ui, &e.label, Face::Medium, 14.0, if e.selected { TEXT } else { MUTED });
            });
        }
    });
}

/// Short all-capital labels are section captions.
fn is_caps(text: &str) -> bool { !text.is_empty() && text.len() < 60 && text != text.to_lowercase() && text == text.to_uppercase() }

fn two_lines(label: &str) -> (&str, &str) {
    let mut lines = label.splitn(2, '\n');
    (lines.next().unwrap_or_default(), lines.next().unwrap_or_default())
}

fn grow_node(e: &UiEntry) -> Node {
    if e.grow { Node { flex_grow: 1.0, flex_shrink: 1.0, flex_basis: px(0.0), min_width: px(0.0), ..col() } } else { Node { flex_shrink: 0.0, ..col() } }
}

fn entry_view(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    div(p, grow_node(e)).with_children(|p| match e.style {
        Style::Plain => plain(p, ui, e),
        Style::Tabs => tabs(p, ui, &e.row),
        Style::Segmented => segmented(p, ui, e),
        Style::Card => {
            boxed(p, Node { padding: UiRect::axes(px(16.0), px(14.0)), row_gap: px(12.0), border: UiRect::all(px(1.0)), border_radius: radius(12.0), ..col() }, CARD_RAISED, CARD_LINE).with_children(|p| {
                for c in &e.row {
                    entry_view(p, ui, c);
                }
            });
        }
        Style::Inline => {
            div(p, Node { column_gap: px(10.0), row_gap: px(10.0), flex_wrap: FlexWrap::Wrap, justify_content: if e.selected { JustifyContent::SpaceBetween } else { JustifyContent::FlexStart }, ..row() }).with_children(|p| {
                for c in &e.row {
                    entry_view(p, ui, c);
                }
            });
        }
        Style::Badge => {
            boxed(p, Node { width: px(24.0), height: px(24.0), border_radius: radius(6.0), justify_content: JustifyContent::Center, ..row() }, LINE, CLEAR).with_children(|p| {
                text(p, ui, &e.label, Face::MonoBold, 12.0, TEXT);
            });
        }
        Style::Place => place(p, ui, e),
        Style::Pick => small_button(p, ui, e),
        Style::Check => {
            let node = Node { column_gap: px(10.0), padding: UiRect::vertical(px(4.0)), ..row() };
            button(p, node, Look::new(CLEAR, CLEAR), Click::Panel(e.action.clone().unwrap_or_default())).with_children(|p| {
                let bx = Node { width: px(18.0), height: px(18.0), border: UiRect::all(px(2.0)), border_radius: radius(4.0), justify_content: JustifyContent::Center, ..row() };
                boxed(p, bx, if e.selected { ACCENT } else { CLEAR }, if e.selected { ACCENT } else { LINE_3 }).with_children(|p| {
                    if e.selected {
                        icon(p, ui, "check", 12.0, "16171a");
                    }
                });
                text(p, ui, &e.label, Face::Regular, 14.0, TEXT);
            });
        }
        Style::Dashed => {
            let node = Node { height: px(48.0), border: UiRect::all(px(1.0)), border_radius: radius(12.0), column_gap: px(8.0), justify_content: JustifyContent::Center, padding: UiRect::horizontal(px(14.0)), ..row() };
            button(p, node, Look::new(CLEAR, LINE_3).hover_bg(CARD_RAISED).hover_border(DIM), Click::Panel(e.action.clone().unwrap_or_default())).with_children(|p| {
                icon(p, ui, "plus", 16.0, "cfcbc3");
                text(p, ui, &e.label, Face::Medium, 14.0, TEXT_2);
            });
        }
        Style::Stats => {
            let grid = Node { display: Display::Grid, grid_template_columns: vec![RepeatedGridTrack::fr(e.row.len().max(1) as u16, 1.0)], column_gap: px(8.0), ..default() };
            div(p, grid).with_children(|p| {
                for c in &e.row {
                    let (caption, value) = two_lines(&c.label);
                    boxed(p, Node { padding: UiRect::axes(px(14.0), px(12.0)), row_gap: px(4.0), border_radius: radius(10.0), ..col() }, SUNK, CLEAR).with_children(|p| {
                        text(p, ui, caption.to_uppercase(), Face::Regular, 11.0, MUTED).insert(spacing(1.0));
                        text(p, ui, value, Face::MonoBold, 20.0, if c.dot.is_empty() { TEXT } else { css(&c.dot) });
                    });
                }
            });
        }
        Style::Stat => {}
        Style::Person => person(p, ui, e),
        Style::Hand => hand(p, ui, e),
        Style::Toggle => toggle(p, ui, e),
        Style::Flow => {
            boxed(p, Node { padding: UiRect::all(px(16.0)), column_gap: px(14.0), border_radius: radius(12.0), align_items: AlignItems::Center, ..row() }, SUNK, CLEAR).with_children(|p| {
                for c in &e.row {
                    entry_view(p, ui, c);
                }
            });
        }
        Style::Bin => {
            div(p, Node { row_gap: px(6.0), ..col() }).with_children(|p| {
                text(p, ui, e.label.to_uppercase(), Face::Regular, 11.0, MUTED).insert(spacing(1.0));
                slot_grid(p, ui, &e.row, if e.cols == 0 { 2 } else { e.cols }, true);
            });
        }
        Style::Progress => {
            div(p, Node { row_gap: px(8.0), align_items: AlignItems::Center, ..col() }).with_children(|p| {
                let image = ui.assets.load("ui/game/icons/arrow-6a675f.png");
                p.spawn((ImageNode::new(image), Node { width: px(40.0), height: px(20.0), ..default() }, Pickable::IGNORE));
                boxed(p, Node { width: pct(100.0), height: px(6.0), border_radius: radius(3.0), ..default() }, RAISED, CLEAR).with_children(|p| {
                    boxed(p, Node { width: pct(e.fill.clamp(0.0, 1.0) * 100.0), height: px(6.0), border_radius: radius(3.0), ..default() }, if e.dot.is_empty() { ACCENT } else { css(&e.dot) }, CLEAR);
                });
                text(p, ui, &e.label, Face::Mono, 12.0, TEXT_2).insert(centred());
            });
        }
        Style::Accent | Style::Ghost | Style::Danger => big_button(p, ui, e),
        Style::Code => {
            boxed(p, Node { padding: UiRect::all(px(16.0)), border: UiRect::all(px(1.0)), border_radius: radius(10.0), ..col() }, CODE_BG, CARD_LINE).with_children(|p| {
                text(p, ui, &e.label, Face::Mono, 13.0, hex(0xCFE3C8)).insert(line_height(1.6));
            });
        }
        Style::Note => {
            div(p, Node { column_gap: px(8.0), align_items: AlignItems::FlexStart, ..row() }).with_children(|p| {
                if !e.glyph.is_empty() {
                    glyph(p, ui, &e.glyph, 14.0);
                }
                text(p, ui, &e.label, Face::Regular, 12.0, if e.dot.is_empty() { MUTED } else { css(&e.dot) }).insert((line_height(1.4), Node { flex_shrink: 1.0, min_width: px(0.0), ..default() }));
            });
        }
        Style::Chip => {
            boxed(p, Node { height: px(32.0), padding: UiRect::new(px(6.0), px(4.0), px(0.0), px(0.0)), column_gap: px(6.0), border_radius: radius(8.0), ..row() }, RAISED, CLEAR).with_children(|p| {
                if let Some(icon) = &e.icon {
                    picture(p, ui.assets.load(icon.clone()), 22.0);
                }
                text(p, ui, &e.label, Face::Regular, 13.0, TEXT);
                if let Some(action) = &e.action {
                    let node = Node { width: px(24.0), height: px(24.0), border_radius: radius(6.0), justify_content: JustifyContent::Center, ..row() };
                    button(p, node, Look::new(CLEAR, CLEAR).hover_bg(LINE), Click::Panel(action.clone())).with_children(|p| icon(p, ui, "close", 10.0, "a8a49b"));
                }
            });
        }
        Style::ChipBox => {
            boxed(p, Node { padding: UiRect::all(px(6.0)), column_gap: px(6.0), row_gap: px(6.0), flex_wrap: FlexWrap::Wrap, border: UiRect::all(px(1.0)), border_radius: radius(10.0), ..row() }, CARD, LINE_2).with_children(|p| {
                for c in &e.row {
                    entry_view(p, ui, c);
                }
            });
        }
        Style::Legend => {
            div(p, Node { column_gap: px(16.0), row_gap: px(6.0), flex_wrap: FlexWrap::Wrap, ..row() }).with_children(|p| {
                for c in &e.row {
                    div(p, Node { column_gap: px(6.0), ..row() }).with_children(|p| {
                        dot(p, css(&c.dot), 8.0);
                        text(p, ui, &c.label, Face::Regular, 12.0, MUTED);
                    });
                }
            });
        }
        Style::Line => {
            let (title, rest) = two_lines(&e.label);
            let look = if e.selected { Look::new(RAISED, ACCENT) } else { Look::new(CARD_RAISED, CLEAR).hover_bg(RAISED).hover_border(LINE_3) };
            let node = Node { padding: UiRect::axes(px(12.0), px(10.0)), column_gap: px(12.0), border: UiRect::all(px(1.0)), border_radius: radius(10.0), ..row() };
            let mut line = match &e.action {
                Some(action) => button(p, node, look, Click::Panel(action.clone())),
                None => boxed(p, node, look.bg, look.border),
            };
            line.with_children(|p| {
                if let Some(icon) = &e.icon {
                    picture(p, ui.assets.load(icon.clone()), 32.0);
                } else if !e.glyph.is_empty() {
                    glyph(p, ui, &e.glyph, 18.0);
                }
                div(p, Node { row_gap: px(3.0), flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..col() }).with_children(|p| {
                    text(p, ui, title, Face::Medium, 14.0, TEXT);
                    if !rest.is_empty() {
                        div(p, Node { column_gap: px(6.0), align_items: AlignItems::FlexStart, ..row() }).with_children(|p| {
                            if !e.dot.is_empty() {
                                div(p, Node { margin: UiRect::top(px(4.0)), ..default() }).with_children(|p| dot(p, css(&e.dot), 7.0));
                            }
                            text(p, ui, rest, Face::Regular, 12.0, TEXT_2).insert(line_height(1.4));
                        });
                    }
                });
                div(p, Node { column_gap: px(8.0), flex_shrink: 0.0, ..row() }).with_children(|p| {
                    for c in &e.row {
                        entry_view(p, ui, c);
                    }
                });
            });
        }
    });
}

fn plain(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    if let Some((image, size)) = &e.picture {
        let mut map = p.spawn((ImageNode::new(image.clone()), Node { width: px(*size), height: px(*size), flex_shrink: 0.0, align_self: AlignSelf::Center, border_radius: radius(10.0), ..default() }));
        if e.id == "phone-map" {
            // The wheel zooms the map, so it must not also bubble up and scroll the panel's column.
            map.insert((crate::phone_map::MapSurface, RelativeCursorPosition::default()))
                .observe(|mut scroll: On<bevy::picking::events::PointerScroll>| scroll.propagate(false));
        }
    } else if !e.row.is_empty() {
        if e.row.iter().all(|c| c.slot) {
            slot_grid(p, ui, &e.row, if e.cols == 0 { 10 } else { e.cols }, false);
        } else {
            div(p, Node { flex_wrap: FlexWrap::Wrap, column_gap: px(6.0), row_gap: px(6.0), ..row() }).with_children(|p| {
                for c in &e.row {
                    entry_view(p, ui, c);
                }
            });
        }
    } else if let Some(value) = &e.input {
        let style = if e.multiline {
            FieldStyle {
                node: Node { min_height: px(300.0), width: pct(100.0), padding: UiRect::all(px(16.0)), border: UiRect::all(px(1.0)), border_radius: radius(10.0), ..default() },
                bg: CODE_BG,
                border: CARD_LINE,
                face: Face::Mono,
                size: 13.0,
                colour: hex(0xCFE3C8),
                multiline: true,
            }
        } else {
            FieldStyle {
                node: Node { min_height: px(36.0), min_width: px(if e.grow { 0.0 } else { 240.0 }), padding: UiRect::axes(px(10.0), px(8.0)), border: UiRect::all(px(1.0)), border_radius: radius(8.0), ..default() },
                bg: SUNK,
                border: LINE_2,
                face: Face::Regular,
                size: 14.0,
                colour: TEXT,
                multiline: false,
            }
        };
        let placeholder = if e.label.is_empty() { "" } else { e.label.as_str() };
        field(p, ui, &e.id, value, placeholder, style);
    } else if e.action.is_some() {
        if e.slot {
            slot(p, ui, e);
        } else {
            button_view(p, ui, e);
        }
    } else if e.slot {
        slot(p, ui, e);
    } else if !e.label.is_empty() {
        if e.id == "sale-value" {
            // "Sale value $12.00": the amount in green, like money earned.
            let (caption, amount) = e.label.rsplit_once(' ').unwrap_or(("", e.label.as_str()));
            div(p, Node { column_gap: px(6.0), ..row() }).with_children(|p| {
                text(p, ui, caption, Face::Regular, 13.0, TEXT_2);
                text(p, ui, amount, Face::MonoBold, 13.0, hex(0x7BD88F));
            });
        } else if is_caps(&e.label) {
            text(p, ui, &e.label, Face::Regular, 11.0, MUTED).insert((spacing(1.0), Node { margin: UiRect::top(px(6.0)), ..default() }));
        } else {
            text(p, ui, &e.label, Face::Regular, 13.0, if e.dot.is_empty() { TEXT_2 } else { css(&e.dot) }).insert(line_height(1.4));
        }
    }
}

/// Slots in a grid of `cols` columns; a fixed grid (a flow's bins) takes exactly the slots' width.
fn slot_grid(p: &mut Kids, ui: &Ui, slots: &[UiEntry], cols: u16, fixed: bool) {
    let mut grid = Node { display: Display::Grid, grid_template_columns: vec![RepeatedGridTrack::fr(cols.max(1), 1.0)], column_gap: px(4.0), row_gap: px(4.0), ..default() };
    if fixed {
        grid.width = px(cols as f32 * 48.0 + (cols.max(1) - 1) as f32 * 4.0);
    }
    div(p, grid).with_children(|p| {
        for c in slots {
            slot(p, ui, c);
        }
    });
}

/// "Bread\n8 spoiled" → name, count, spoiled.
fn slot_parts(label: &str) -> (String, String, bool) {
    let mut lines = label.split('\n');
    let name = lines.next().unwrap_or_default().to_string();
    let rest = lines.next().unwrap_or_default().trim();
    (name, rest.split(' ').next().unwrap_or_default().to_string(), rest.contains("spoiled"))
}

fn slot(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    let (name, count, spoiled) = slot_parts(&e.label);
    let empty = e.icon.is_none() && e.label.is_empty();
    let trash = e.action.as_deref() == Some("p:trash");
    let bg = if spoiled || trash { hex(0x3A2523) } else if empty { SLOT_EMPTY } else { SLOT };
    let border = if e.selected { ACCENT } else { CLEAR };
    // A lone slot (the trash) takes the design's slot size; grid slots fill their cells.
    let width = if e.cols == 1 { px(56.0) } else { Val::Auto };
    let node = Node { width, height: px(48.0), min_width: px(0.0), border: UiRect::all(px(2.0)), border_radius: radius(8.0), justify_content: JustifyContent::Center, ..row() };

    let look = Look::new(bg, border).hover_border(if e.selected { ACCENT } else { LINE_3 });
    let mut slot = match &e.action {
        Some(action) => button(p, node, look, Click::Panel(action.clone())),
        None => boxed(p, node, bg, border),
    };
    slot.with_children(|p| {
        if let Some(icon) = &e.icon {
            picture(p, ui.assets.load(icon.clone()), 36.0);
        } else if trash {
            text(p, ui, &name, Face::Medium, 10.0, RED).insert(centred());
        } else if !name.is_empty() {
            text(p, ui, &name, Face::Regular, 9.0, TEXT_2).insert(centred());
        }
        if !count.is_empty() {
            text(p, ui, &count, Face::MonoBold, 11.0, TEXT).insert(Node { position_type: PositionType::Absolute, right: px(4.0), bottom: px(2.0), ..default() });
        }
        if spoiled {
            text(p, ui, "spoiled", Face::Mono, 9.0, RED).insert(Node { position_type: PositionType::Absolute, left: px(4.0), top: px(2.0), ..default() });
        } else if !e.dot.is_empty() && !e.glyph.is_empty() {
            // A timer in its colour (chilled, spoiling soon).
            text(p, ui, &e.glyph, Face::Mono, 9.0, css(&e.dot)).insert(Node { position_type: PositionType::Absolute, left: px(4.0), top: px(2.0), ..default() });
        }
    });
}

fn segmented(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    boxed(p, Node { padding: UiRect::all(px(3.0)), column_gap: px(2.0), border_radius: radius(10.0), align_self: AlignSelf::FlexStart, flex_wrap: FlexWrap::Wrap, ..row() }, if e.selected { CARD } else { SUNK }, CLEAR)
        .with_children(|p| {
            for c in &e.row {
                let node = Node { height: px(34.0), min_width: px(52.0), padding: UiRect::horizontal(px(12.0)), border_radius: radius(8.0), justify_content: JustifyContent::Center, column_gap: px(6.0), ..row() };
                let look = if c.selected { Look::new(LINE, CLEAR) } else { Look::new(CLEAR, CLEAR).hover_bg(RAISED) };
                button(p, node, look, Click::Panel(c.action.clone().unwrap_or_default())).with_children(|p| {
                    if !c.glyph.is_empty() {
                        glyph(p, ui, &c.glyph, 14.0);
                    }
                    text(p, ui, &c.label, Face::Medium, 13.0, if c.selected { TEXT } else { MUTED });
                });
            }
        });
}

/// A place box: icon, caption over name (a button that cycles it when the entry has an action), then its buttons.
fn place(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    let (caption, name) = two_lines(&e.label);
    boxed(p, Node { padding: UiRect::axes(px(10.0), px(8.0)), column_gap: px(10.0), border_radius: radius(10.0), min_width: px(0.0), ..row() }, CARD, CLEAR).with_children(|p| {
        if let Some(icon) = &e.icon {
            picture(p, ui.assets.load(icon.clone()), 36.0);
        } else if let Some(image) = &e.icon_image {
            picture(p, image.clone(), 36.0);
        } else {
            boxed(p, Node { width: px(36.0), height: px(36.0), border_radius: radius(8.0), ..default() }, SLOT_EMPTY, CLEAR);
        }
        let node = Node { row_gap: px(1.0), flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), padding: UiRect::axes(px(4.0), px(2.0)), border_radius: radius(6.0), ..col() };
        match &e.action {
            Some(action) => {
                button(p, node, Look::new(CLEAR, CLEAR).hover_bg(RAISED), Click::Panel(action.clone())).with_children(|p| place_words(p, ui, caption, name));
            }
            None => {
                div(p, node).with_children(|p| place_words(p, ui, caption, name));
            }
        }
        for c in &e.row {
            entry_view(p, ui, c);
        }
    });
}

fn place_words(p: &mut Kids, ui: &Ui, caption: &str, name: &str) {
    text(p, ui, caption, Face::Regular, 11.0, DIM);
    text(p, ui, name, Face::Medium, 14.0, if name == "Not set" { FAINT } else { TEXT });
}

fn small_button(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    let node = Node { height: px(32.0), padding: UiRect::horizontal(px(10.0)), column_gap: px(6.0), border: UiRect::all(px(1.0)), border_radius: radius(8.0), justify_content: JustifyContent::Center, ..row() };
    let look = if e.selected { Look::new(LINE, ACCENT) } else { Look::new(RAISED, LINE_2).hover_bg(LINE) };
    button(p, node, look, Click::Panel(e.action.clone().unwrap_or_default())).with_children(|p| {
        if !e.glyph.is_empty() {
            glyph(p, ui, &e.glyph, 12.0);
        }
        if !e.label.is_empty() {
            text(p, ui, &e.label, Face::Medium, 12.0, TEXT);
        }
    });
}

/// Accent, outlined and red buttons at the design's footer size. One without an action is drawn disabled; a second label
/// line is a note under it ("Empty hand first").
fn big_button(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    let (label, note) = two_lines(&e.label);
    let enabled = e.action.is_some();
    div(p, Node { row_gap: px(3.0), align_items: AlignItems::FlexEnd, ..col() }).with_children(|p| {
        let (node, look, colour, face) = match e.style {
            Style::Accent => (
                Node { height: px(44.0), padding: UiRect::horizontal(px(22.0)), border_radius: radius(10.0), column_gap: px(8.0), justify_content: JustifyContent::Center, ..row() },
                if enabled { Look::new(ACCENT, CLEAR).hover_bg(ACCENT_HOVER) } else { Look::new(DISABLED, CLEAR) },
                INK,
                Face::Bold,
            ),
            Style::Danger => (
                Node { height: px(36.0), padding: UiRect::horizontal(px(14.0)), border: UiRect::all(px(1.0)), border_radius: radius(8.0), justify_content: JustifyContent::Center, ..row() },
                if enabled { Look::new(CLEAR, RED_LINE).hover_bg(hex(0x2A1E1C)) } else { Look::new(CLEAR, LINE) },
                if enabled { RED } else { FAINT },
                Face::Medium,
            ),
            _ => (
                Node { height: px(44.0), padding: UiRect::horizontal(px(18.0)), border: UiRect::all(px(1.0)), border_radius: radius(10.0), column_gap: px(8.0), justify_content: JustifyContent::Center, ..row() },
                if e.selected { Look::new(LINE, ACCENT) } else { Look::new(CLEAR, LINE_2).hover_bg(CARD_RAISED) },
                if enabled { TEXT } else { FAINT },
                Face::Medium,
            ),
        };
        let size = if e.style == Style::Danger { 13.0 } else { 14.0 };
        let mut b = match &e.action {
            Some(action) => button(p, node, look, Click::Panel(action.clone())),
            None => boxed(p, node, look.bg, look.border),
        };
        b.with_children(|p| {
            if !e.glyph.is_empty() {
                glyph(p, ui, &e.glyph, 12.0);
            }
            text(p, ui, label, face, size, colour);
        });
        if !note.is_empty() {
            text(p, ui, note, Face::Regular, 10.0, DIM);
        }
    });
}

/// A person row: avatar, name over a status line with its dot, then its widgets (hands, buttons).
fn person(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    let (name, status) = two_lines(&e.label);
    boxed(p, Node { padding: UiRect::axes(px(12.0), px(10.0)), column_gap: px(14.0), border: UiRect::all(px(1.0)), border_radius: radius(10.0), ..row() }, CARD_RAISED, if e.selected { ACCENT } else { CLEAR })
        .with_children(|p| {
            avatar(p, ui, 36.0);
            div(p, Node { row_gap: px(3.0), flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..col() }).with_children(|p| {
                text(p, ui, name, Face::Medium, 14.0, TEXT);
                div(p, Node { column_gap: px(6.0), ..row() }).with_children(|p| {
                    if !e.dot.is_empty() {
                        dot(p, css(&e.dot), 7.0);
                    }
                    text(p, ui, status, Face::Regular, 12.0, TEXT_2);
                });
            });
            for c in &e.row {
                entry_view(p, ui, c);
            }
        });
}

/// A held-goods square ("6\nHand": count, then the caption under it).
fn hand(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    let (count, caption) = two_lines(&e.label);
    let size = if caption.is_empty() { 40.0 } else { 44.0 };
    div(p, Node { row_gap: px(2.0), align_items: AlignItems::Center, ..col() }).with_children(|p| {
        let node = Node { width: px(size), height: px(size), border: UiRect::all(px(1.0)), border_radius: radius(8.0), justify_content: JustifyContent::Center, ..row() };
        let bg = if caption.is_empty() { SLOT } else { CARD };
        let mut b = match &e.action {
            Some(action) => button(p, node, Look::new(bg, LINE).hover_border(LINE_3), Click::Panel(action.clone())),
            None => boxed(p, node, bg, if caption.is_empty() { LINE } else { CLEAR }),
        };
        b.with_children(|p| {
            if let Some(icon) = &e.icon {
                picture(p, ui.assets.load(icon.clone()), size - 12.0);
            }
            if !count.is_empty() {
                text(p, ui, count, Face::MonoBold, 10.0, TEXT).insert(Node { position_type: PositionType::Absolute, right: px(3.0), bottom: px(1.0), ..default() });
            }
        });
        if !caption.is_empty() {
            text(p, ui, caption, Face::Regular, 10.0, DIM);
        }
    });
}

/// A power switch: title over hint, then the pill with its knob (on: green, knob right).
fn toggle(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    let (title, hint) = two_lines(&e.label);
    div(p, Node { column_gap: px(12.0), justify_content: JustifyContent::SpaceBetween, ..row() }).with_children(|p| {
        div(p, Node { row_gap: px(2.0), flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..col() }).with_children(|p| {
            text(p, ui, title, Face::Medium, 14.0, TEXT);
            if !hint.is_empty() {
                text(p, ui, hint, Face::Regular, 12.0, MUTED);
            }
        });
        let on = e.selected;
        let node = Node { width: px(88.0), height: px(44.0), padding: UiRect::all(px(4.0)), border_radius: radius(22.0), justify_content: if on { JustifyContent::FlexEnd } else { JustifyContent::FlexStart }, flex_shrink: 0.0, ..row() };
        let look = if on { Look::new(SWITCH_ON, CLEAR).hover_bg(hex(0x47A862)) } else { Look::new(LINE_2, CLEAR).hover_bg(LINE_3) };
        button(p, node, look, Click::Panel(e.action.clone().unwrap_or_default())).with_children(|p| {
            boxed(p, Node { width: px(36.0), height: px(36.0), border_radius: radius(18.0), justify_content: JustifyContent::Center, ..row() }, KNOB, CLEAR).with_children(|p| {
                text(p, ui, if on { "ON" } else { "OFF" }, Face::Bold, 11.0, INK);
            });
        });
    });
}

fn accent_action(action: &str) -> bool { ["p:hire", "p:buy-property", "p:task-run", "p:run|", "b:add-floor", "p:teleport", "p:farm-order-now", "p:create-route"].iter().any(|p| action.starts_with(p)) }

fn danger_action(action: &str) -> bool { ["p:fire", "p:sell|", "p:sell-truck", "p:delete-route", "p:cancel-schedule"].iter().any(|p| action.starts_with(p)) }

fn button_view(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    let action = e.action.clone().unwrap_or_default();
    if e.icon.is_some() || e.icon_image.is_some() {
        let mut lines = e.label.split('\n');
        let name = lines.next().unwrap_or_default();
        let rest: Vec<&str> = lines.collect();
        let node = Node {
            width: px(if rest.len() > 1 { 172.0 } else { 104.0 }),
            min_height: px(104.0),
            padding: UiRect::axes(px(8.0), px(10.0)),
            border: UiRect::all(px(1.0)),
            border_radius: radius(12.0),
            row_gap: px(4.0),
            align_items: AlignItems::Center,
            justify_content: JustifyContent::FlexStart,
            ..col()
        };
        let look = if e.selected { Look::new(RAISED, ACCENT) } else { Look::new(CARD_RAISED, LINE).hover_bg(RAISED).hover_border(LINE_3) };
        button(p, node, look, Click::Panel(action)).with_children(|p| {
            boxed(p, Node { width: px(52.0), height: px(52.0), border_radius: radius(10.0), justify_content: JustifyContent::Center, ..row() }, SLOT, CLEAR).with_children(|p| match (&e.icon, &e.icon_image) {
                (Some(icon), _) => picture(p, ui.assets.load(icon.clone()), 40.0),
                (None, Some(image)) => picture(p, image.clone(), 40.0),
                _ => {}
            });
            text(p, ui, name, Face::Medium, 12.0, TEXT).insert((centred(), Node { width: pct(100.0), ..default() }));
            for (i, line) in rest.iter().enumerate() {
                let (face, colour) = if i + 1 == rest.len() && line.starts_with('$') { (Face::MonoBold, AMBER) } else { (Face::Regular, MUTED) };
                text(p, ui, *line, face, 11.0, colour).insert((centred(), Node { width: pct(100.0), ..default() }));
            }
        });
        return;
    }
    let (look, colour) = if accent_action(&action) {
        (Look::new(ACCENT, ACCENT).hover_bg(ACCENT_HOVER), INK)
    } else if danger_action(&action) {
        (Look::new(CLEAR, RED_LINE).hover_bg(hex(0x2A1E1C)), RED)
    } else if e.selected {
        (Look::new(LINE, ACCENT), TEXT)
    } else {
        (Look::new(RAISED, LINE_2).hover_bg(LINE), TEXT)
    };
    let node = Node { min_height: px(36.0), padding: UiRect::axes(px(14.0), px(8.0)), column_gap: px(8.0), border: UiRect::all(px(1.0)), border_radius: radius(8.0), justify_content: JustifyContent::Center, ..row() };
    button(p, node, look, Click::Panel(action)).with_children(|p| {
        if !e.glyph.is_empty() {
            glyph(p, ui, &e.glyph, 14.0);
        }
        text(p, ui, &e.label, Face::Medium, 13.0, colour);
    });
}

// ---- the phone ----

const BEZEL: Color = hex(0x0C0D0F);
const BEZEL_EDGE: Color = hex(0x2C2E33);
const SCREEN: Color = hex(0x141517);

/// The phone held sideways: a rounded bezel, thicker at the ends, with the camera at the left end and the buttons along the
/// top edge; inside, a status bar (time, place, signal, battery), the app dock down the left and the open app on the right.
fn phone(p: &mut Kids, ui: &Ui, model: &UiModel) {
    let wrap = Node { position_type: PositionType::Absolute, left: px(0.0), right: px(0.0), top: px(36.0), bottom: px(100.0), justify_content: JustifyContent::Center, align_items: AlignItems::Center, ..row() };
    div(p, wrap).with_children(|p| {
        let body = Node {
            width: px(1240.0),
            height: px(700.0),
            max_height: pct(100.0),
            padding: UiRect::axes(px(46.0), px(18.0)),
            border: UiRect::all(px(2.0)),
            border_radius: radius(56.0),
            ..row()
        };
        p.spawn((body, BackgroundColor(BEZEL), BorderColor::all(BEZEL_EDGE), BoxShadow::new(rgba(0, 0, 0, 0.55), px(0.0), px(18.0), px(0.0), px(48.0)))).with_children(|p| {
            // Volume and power buttons along the top edge, the camera in the left end.
            for (left, width) in [(180.0, 70.0), (264.0, 70.0), (980.0, 96.0)] {
                boxed(p, Node { position_type: PositionType::Absolute, left: px(left), top: px(-6.0), width: px(width), height: px(5.0), border_radius: radius(2.5), ..default() }, BEZEL_EDGE, CLEAR);
            }
            div(p, Node { position_type: PositionType::Absolute, left: px(17.0), top: pct(50.0), margin: UiRect::top(px(-7.0)), ..default() }).with_children(|p| {
                boxed(p, Node { width: px(14.0), height: px(14.0), border_radius: radius(7.0), border: UiRect::all(px(2.0)), ..default() }, hex(0x1A2230), hex(0x24262B));
            });
            boxed(p, Node { position_type: PositionType::Absolute, right: px(20.0), top: pct(50.0), margin: UiRect::top(px(-40.0)), width: px(4.0), height: px(80.0), border_radius: radius(2.0), ..default() }, hex(0x1C1D21), CLEAR);
            // The screen.
            p.spawn((Node { flex_grow: 1.0, height: pct(100.0), border_radius: radius(30.0), overflow: Overflow::clip(), ..col() }, BackgroundColor(SCREEN))).with_children(|p| {
                status_bar(p, ui, model);
                div(p, Node { flex_grow: 1.0, min_height: px(0.0), ..row() }).with_children(|p| {
                    dock(p, ui, &model.tabs);
                    div(p, Node { flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), height: pct(100.0), ..col() }).with_children(|p| {
                        div(p, Node { padding: UiRect::new(px(22.0), px(18.0), px(14.0), px(4.0)), column_gap: px(12.0), flex_shrink: 0.0, ..row() }).with_children(|p| {
                            div(p, Node { row_gap: px(2.0), flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..col() }).with_children(|p| {
                                text(p, ui, &model.heading, Face::Bold, 20.0, TEXT);
                                if !model.status.is_empty() {
                                    text(p, ui, &model.status, Face::Regular, 12.0, MUTED);
                                }
                            });
                            for e in &model.header {
                                entry_view(p, ui, e);
                            }
                            close_button(p, ui);
                        });
                        body_phone(p, ui, &model.entries);
                        footer(p, ui, model, 22.0);
                    });
                });
                // The home bar.
                div(p, Node { position_type: PositionType::Absolute, left: pct(50.0), bottom: px(6.0), margin: UiRect::left(px(-60.0)), ..default() }).with_children(|p| {
                    boxed(p, Node { width: px(120.0), height: px(4.0), border_radius: radius(2.0), ..default() }, rgba(236, 232, 225, 0.28), CLEAR);
                });
            });
        });
    });
}

fn body_phone(p: &mut Kids, ui: &Ui, entries: &[UiEntry]) {
    let node = Node { row_gap: px(10.0), overflow: scroll_y(), padding: UiRect::new(px(22.0), px(18.0), px(10.0), px(8.0)), flex_grow: 1.0, flex_shrink: 1.0, min_height: px(0.0), ..col() };
    p.spawn((node, scroller("phone-body", ui))).with_children(|p| {
        for entry in entries {
            entry_view(p, ui, entry);
        }
    });
}

fn status_bar(p: &mut Kids, ui: &Ui, model: &UiModel) {
    div(p, Node { height: px(30.0), padding: UiRect::horizontal(px(22.0)), column_gap: px(10.0), flex_shrink: 0.0, ..row() }).with_children(|p| {
        text(p, ui, &model.phone_time, Face::MonoBold, 12.0, TEXT);
        div(p, Node { flex_grow: 1.0, ..default() });
        glyph(p, ui, "signal-ece8e1", 14.0);
        glyph(p, ui, "battery-ece8e1", 18.0);
    });
}

/// The phone's apps down the left edge: an icon tile over its name, the open one lit.
fn dock(p: &mut Kids, ui: &Ui, apps: &[UiEntry]) {
    boxed(p, Node { width: px(96.0), padding: UiRect::vertical(px(10.0)), row_gap: px(6.0), border: UiRect::right(px(1.0)), align_items: AlignItems::Center, overflow: scroll_y(), ..col() }, CARD, LINE)
        .with_children(|p| {
            for app in apps {
                let node = Node { width: px(76.0), padding: UiRect::vertical(px(6.0)), row_gap: px(4.0), border_radius: radius(12.0), align_items: AlignItems::Center, ..col() };
                let look = if app.selected { Look::new(RAISED, CLEAR) } else { Look::new(CLEAR, CLEAR).hover_bg(CARD_RAISED) };
                button(p, node, look, Click::Panel(app.action.clone().unwrap_or_default())).with_children(|p| {
                    let tile = Node { width: px(40.0), height: px(40.0), border_radius: radius(11.0), border: UiRect::all(px(1.0)), justify_content: JustifyContent::Center, ..row() };
                    boxed(p, tile, if app.selected { ACCENT_INK } else { SLOT }, if app.selected { ACCENT } else { LINE }).with_children(|p| {
                        if !app.glyph.is_empty() {
                            glyph(p, ui, &app.glyph, 20.0);
                        }
                    });
                    text(p, ui, &app.label, Face::Medium, 11.0, if app.selected { TEXT } else { MUTED });
                });
            }
        });
}
