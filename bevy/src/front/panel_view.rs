// The open in-world panel (inventory, a machine, an employee's tasks, the phone, build mode's catalog), drawn from the game's
// UiModel in the "Food Factory UI" design's style: a dark card over a dimmed world, item slots in a ten-wide grid, icon cards
// for things to buy or place, and plain or accent buttons. Every button sends its action; panel buttons also take right
// clicks and slot drags. A rebuild waits while a slot is being dragged, so the drag reaches its drop. A change to the footer
// alone (build mode's per-cell preview) rewrites that one text, so the panel is not rebuilt under a moving pointer.

use super::*;
use crate::ui::UiEntry;

/// The panel's footer text, rewritten in place when only it changes.
#[derive(Component)]
pub(super) struct PanelFooter;

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
        let card = |node: Node| (node, BackgroundColor(CARD), BorderColor::all(LINE));
        let panel = Node { padding: UiRect::new(px(20.0), px(20.0), px(18.0), px(20.0)), row_gap: px(14.0), border: UiRect::all(px(1.0)), border_radius: radius(14.0), ..col() };
        if model.right {
            p.spawn(card(Node { position_type: PositionType::Absolute, right: px(24.0), top: px(24.0), width: px(460.0), max_height: px(760.0), ..panel }))
                .with_children(|p| inner(p, &ui, &model, false));
        } else {
            boxed(p, fill(), rgba(10, 10, 12, 0.55), CLEAR);
            let wrap = Node { position_type: PositionType::Absolute, left: px(0.0), right: px(0.0), top: px(64.0), bottom: px(112.0), justify_content: JustifyContent::Center, align_items: AlignItems::FlexStart, ..row() };
            div(p, wrap).with_children(|p| {
                p.spawn(card(Node { width: px(if model.wide { 780.0 } else { 600.0 }), max_height: pct(100.0), ..panel }))
                    .with_children(|p| inner(p, &ui, &model, true));
            });
        }
    });
}

fn inner(p: &mut Kids, ui: &Ui, model: &UiModel, closable: bool) {
    div(p, Node { row_gap: px(14.0), flex_grow: 1.0, flex_shrink: 1.0, min_height: px(0.0), ..col() }).with_children(|p| {
        div(p, Node { column_gap: px(12.0), align_items: AlignItems::FlexStart, ..row() }).with_children(|p| {
            div(p, Node { row_gap: px(2.0), flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..col() }).with_children(|p| {
                text(p, ui, &model.heading, Face::Medium, 17.0, TEXT);
                if !model.status.is_empty() {
                    text(p, ui, &model.status, Face::Regular, 12.0, MUTED);
                }
            });
            if closable {
                let node = Node { width: px(32.0), height: px(32.0), border: UiRect::all(px(1.0)), border_radius: radius(8.0), justify_content: JustifyContent::Center, flex_shrink: 0.0, ..row() };
                button(p, node, Look::new(BUTTON, LINE).hover_bg(RAISED), Click::Intent("p:close".into())).with_children(|p| icon(p, ui, "close", 14.0, "ece8e1"));
            }
        });
        let body = Node { row_gap: px(10.0), overflow: scroll_y(), padding: UiRect::right(px(4.0)), flex_shrink: 1.0, min_height: px(0.0), ..col() };
        p.spawn((body, scroller("panel-body", ui))).with_children(|p| {
            for entry in &model.entries {
                entry_view(p, ui, entry);
            }
        });
        if !model.footer.is_empty() {
            text(p, ui, &model.footer, Face::Regular, 12.0, MUTED).insert(PanelFooter);
        }
    });
}

/// Short all-capital labels are section captions.
fn is_caps(text: &str) -> bool { !text.is_empty() && text.len() < 60 && text != text.to_lowercase() && text == text.to_uppercase() }

fn entry_view(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    div(p, Node { row_gap: px(0.0), ..col() }).with_children(|p| {
        if let Some((image, size)) = &e.picture {
            let mut map = p.spawn((ImageNode::new(image.clone()), Node { width: px(*size), height: px(*size), flex_shrink: 0.0, ..default() }));
            if e.id == "phone-map" {
                map.insert((crate::phone_map::MapSurface, RelativeCursorPosition::default()));
            }
        } else if !e.row.is_empty() {
            if e.row.iter().all(|c| c.slot) {
                let grid = Node { display: Display::Grid, grid_template_columns: vec![RepeatedGridTrack::fr(10, 1.0)], column_gap: px(4.0), row_gap: px(4.0), ..default() };
                div(p, grid).with_children(|p| {
                    for c in &e.row {
                        slot(p, ui, c);
                    }
                });
            } else {
                div(p, Node { flex_wrap: FlexWrap::Wrap, column_gap: px(6.0), row_gap: px(6.0), ..row() }).with_children(|p| {
                    for c in &e.row {
                        entry_view(p, ui, c);
                    }
                });
            }
        } else if let Some(value) = &e.input {
            if e.multiline {
                let style = FieldStyle {
                    node: Node { min_height: px(260.0), width: pct(100.0), padding: UiRect::all(px(12.0)), border: UiRect::all(px(1.0)), border_radius: radius(10.0), ..default() },
                    bg: hex(0x111214),
                    border: hex(0x2E3135),
                    face: Face::Mono,
                    size: 13.0,
                    colour: hex(0xCFE3C8),
                    multiline: true,
                };
                field(p, ui, &e.id, value, "", style);
            } else {
                let style = FieldStyle {
                    node: Node { min_height: px(36.0), min_width: px(240.0), padding: UiRect::axes(px(10.0), px(8.0)), border: UiRect::all(px(1.0)), border_radius: radius(8.0), ..default() },
                    bg: SUNK,
                    border: LINE_2,
                    face: Face::Regular,
                    size: 14.0,
                    colour: TEXT,
                    multiline: false,
                };
                field(p, ui, &e.id, value, "", style);
            }
        } else if e.action.is_some() {
            if e.slot {
                slot(p, ui, e);
            } else {
                button_view(p, ui, e);
            }
        } else if !e.label.is_empty() {
            if is_caps(&e.label) {
                text(p, ui, &e.label, Face::Regular, 11.0, MUTED).insert((spacing(1.0), Node { margin: UiRect::top(px(6.0)), ..default() }));
            } else {
                text(p, ui, &e.label, Face::Regular, 13.0, TEXT_2).insert(line_height(1.4));
            }
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
    let bg = if spoiled { hex(0x3A2523) } else if empty { SLOT_EMPTY } else { SLOT };
    let border = if e.selected { ACCENT } else { CLEAR };
    let node = Node { height: px(48.0), border: UiRect::all(px(2.0)), border_radius: radius(8.0), justify_content: JustifyContent::Center, ..row() };
    let look = Look::new(bg, border).hover_border(if e.selected { ACCENT } else { LINE_3 });
    button(p, node, look, Click::Panel(e.action.clone().unwrap_or_default())).with_children(|p| {
        if let Some(icon) = &e.icon {
            picture(p, ui.assets.load(icon.clone()), 36.0);
        } else if !name.is_empty() {
            text(p, ui, &name, Face::Regular, 9.0, TEXT_2).insert(centred());
        }
        if !count.is_empty() {
            text(p, ui, &count, Face::MonoBold, 11.0, TEXT).insert(Node { position_type: PositionType::Absolute, right: px(4.0), bottom: px(2.0), ..default() });
        }
        if spoiled {
            text(p, ui, "spoiled", Face::Mono, 9.0, hex(0xFF8A78)).insert(Node { position_type: PositionType::Absolute, left: px(4.0), top: px(2.0), ..default() });
        }
    });
}

fn accent_action(action: &str) -> bool { ["p:hire", "p:buy-property", "p:task-run", "p:run|", "b:add-floor", "p:teleport"].iter().any(|p| action.starts_with(p)) }

fn danger_action(action: &str) -> bool { ["p:fire", "p:sell|"].iter().any(|p| action.starts_with(p)) }

fn button_view(p: &mut Kids, ui: &Ui, e: &UiEntry) {
    let action = e.action.clone().unwrap_or_default();
    if e.icon.is_some() || e.icon_image.is_some() {
        let mut lines = e.label.split('\n');
        let (name, sub) = (lines.next().unwrap_or_default(), lines.next().unwrap_or_default());
        let node = Node {
            width: px(96.0),
            min_height: px(96.0),
            padding: UiRect::axes(px(6.0), px(8.0)),
            border: UiRect::all(px(1.0)),
            border_radius: radius(10.0),
            row_gap: px(4.0),
            align_items: AlignItems::Center,
            justify_content: JustifyContent::FlexStart,
            ..col()
        };
        let look = if e.selected { Look::new(RAISED, ACCENT) } else { Look::new(BUTTON, LINE).hover_bg(RAISED) };
        button(p, node, look, Click::Panel(action)).with_children(|p| {
            match (&e.icon, &e.icon_image) {
                (Some(icon), _) => picture(p, ui.assets.load(icon.clone()), 40.0),
                (None, Some(image)) => picture(p, image.clone(), 40.0),
                _ => {}
            }
            text(p, ui, name, Face::Regular, 11.0, TEXT).insert((centred(), Node { width: pct(100.0), ..default() }));
            if !sub.is_empty() {
                text(p, ui, sub, Face::Mono, 11.0, MUTED).insert((centred(), Node { width: pct(100.0), ..default() }));
            }
        });
        return;
    }
    let (look, colour) = if accent_action(&action) {
        (Look::new(ACCENT, ACCENT).hover_bg(ACCENT_HOVER), INK)
    } else if danger_action(&action) {
        (Look::new(CLEAR, hex(0x5A3A36)), hex(0xFF8A78))
    } else if e.selected {
        (Look::new(LINE, ACCENT), TEXT)
    } else {
        (Look::new(RAISED, LINE_2).hover_bg(LINE), TEXT)
    };
    let node = Node { height: px(36.0), padding: UiRect::horizontal(px(14.0)), border: UiRect::all(px(1.0)), border_radius: radius(8.0), justify_content: JustifyContent::Center, ..row() };
    button(p, node, look, Click::Panel(action)).with_children(|p| {
        text(p, ui, &e.label, Face::Medium, 13.0, colour);
    });
}
