// The settings page (the "Main Menu" design's Settings board): mouse sensitivity, audio, anti-aliasing and the player's name
// on the left, the keybind table on the right. Clicking a key asks the game to listen for the next key press; values are
// saved by the game as they change. Sliders update in place while dragged and send their value on release.

use super::menu_view::{caps, glass, header, menu_field, page};
use super::*;

#[derive(Resource, Clone, PartialEq, Default)]
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

#[derive(Clone, PartialEq, Default)]
pub struct BindGroupDto {
    pub name: String,
    pub rows: Vec<BindDto>,
}

#[derive(Clone, PartialEq, Default)]
pub struct BindDto {
    pub action: String,
    pub label: String,
    pub key: String,
    pub alternate: String,
    pub fixed: bool,
}

pub(super) fn draw_settings(mut commands: Commands, ui: Ui, roots: Res<Roots>, settings: Res<SettingsFrame>, mut last: Local<Option<SettingsFrame>>) {
    if last.as_ref() == Some(&*settings) {
        return;
    }
    *last = Some(settings.clone());
    rebuild(&mut commands, roots.settings, |p| {
        if !settings.open {
            return;
        }
        scene(p, false);
        div(p, page()).with_children(|p| {
            header(p, &ui, "Settings", Click::Intent("s:back".into()), |p| {
                let ghost = Node { height: px(44.0), padding: UiRect::horizontal(px(16.0)), border: UiRect::all(px(1.0)), border_radius: radius(10.0), column_gap: px(8.0), flex_shrink: 0.0, ..row() };
                button(p, ghost, Look::new(GLASS, LINE).hover_bg(RAISED), Click::Intent("s:defaults".into())).with_children(|p| {
                    icon(p, &ui, "reset", 16.0, "ece8e1");
                    text(p, &ui, "Reset to defaults", Face::Medium, 14.0, TEXT);
                });
                let primary = Node { height: px(44.0), padding: UiRect::horizontal(px(22.0)), border_radius: radius(10.0), justify_content: JustifyContent::Center, column_gap: px(10.0), flex_shrink: 0.0, ..row() };
                button(p, primary, Look::new(ACCENT, CLEAR).hover_bg(ACCENT_HOVER), Click::Intent("s:back".into())).with_children(|p| {
                    text(p, &ui, "Done", Face::Medium, 15.0, INK);
                });
            });
            div(p, Node { column_gap: px(24.0), align_items: AlignItems::Stretch, flex_grow: 1.0, flex_shrink: 1.0, min_height: px(0.0), ..row() }).with_children(|p| {
                left(p, &ui, &settings);
                keys(p, &ui, &settings);
            });
        });
    });
}

fn section<'a>(p: &'a mut Kids, ui: &Ui, title: &str) -> EntityCommands<'a> {
    let mut e = p.spawn(glass(Node { padding: UiRect::all(px(22.0)), row_gap: px(18.0), flex_shrink: 0.0, ..col() }));
    e.with_children(|p| caps(p, ui, title, 13.0));
    e
}

#[allow(clippy::too_many_arguments)]
fn slider(p: &mut Kids, ui: &Ui, id: &'static str, label: &str, format: SliderFormat, min: f32, max: f32, step: f32, value: f32, marks: &[&str]) {
    div(p, Node { row_gap: px(10.0), ..col() }).with_children(|p| {
        let mut shown = Entity::PLACEHOLDER;
        div(p, Node { justify_content: JustifyContent::SpaceBetween, ..row() }).with_children(|p| {
            text(p, ui, label, Face::Medium, 15.0, TEXT);
            shown = text(p, ui, format.show(value), Face::MonoBold, 16.0, ACCENT).id();
        });
        let t = ((value - min) / (max - min)).clamp(0.0, 1.0) * 100.0;
        let (mut fill_bar, mut thumb) = (Entity::PLACEHOLDER, Entity::PLACEHOLDER);
        let mut track = p.spawn((Node { height: px(28.0), width: pct(100.0), position_type: PositionType::Relative, ..default() }, RelativeCursorPosition::default()));
        track.with_children(|p| {
            boxed(p, Node { position_type: PositionType::Absolute, left: px(0.0), right: px(0.0), top: px(11.0), height: px(6.0), border_radius: radius(3.0), ..default() }, LINE_2, CLEAR);
            fill_bar = boxed(p, Node { position_type: PositionType::Absolute, left: px(0.0), top: px(11.0), height: px(6.0), width: pct(t), border_radius: radius(3.0), ..default() }, ACCENT, CLEAR).id();
            let knob = Node { position_type: PositionType::Absolute, left: pct(t), top: px(6.0), width: px(16.0), height: px(16.0), margin: UiRect::left(px(-8.0)), border_radius: radius(8.0), ..default() };
            thumb = boxed(p, knob, TEXT, CLEAR).id();
        });
        track.insert(Slider { id, min, max, step, value, format, fill: fill_bar, thumb, label: shown });
        if !marks.is_empty() {
            div(p, Node { justify_content: JustifyContent::SpaceBetween, ..row() }).with_children(|p| {
                for mark in marks {
                    text(p, ui, *mark, Face::Mono, 11.0, DIM);
                }
            });
        }
    });
}

fn left(p: &mut Kids, ui: &Ui, s: &SettingsFrame) {
    let column = Node { width: px(340.0), row_gap: px(16.0), overflow: scroll_y(), padding: UiRect::right(px(4.0)), flex_shrink: 0.0, ..col() };
    p.spawn((column, scroller("settings-left", ui))).with_children(|p| {
        section(p, ui, "MOUSE").with_children(|p| {
            slider(p, ui, "sensitivity", "Sensitivity", SliderFormat::Times, 0.1, 3.0, 0.05, s.sensitivity, &["0.10×", "1.00×", "3.00×"]);
        });
        section(p, ui, "AUDIO").with_children(|p| {
            slider(p, ui, "master", "Master volume", SliderFormat::Percent, 0.0, 1.0, 0.05, s.master, &[]);
            slider(p, ui, "effects", "Effects", SliderFormat::Percent, 0.0, 1.0, 0.05, s.effects, &[]);
            slider(p, ui, "interface", "Interface", SliderFormat::Percent, 0.0, 1.0, 0.05, s.interface, &[]);
            slider(p, ui, "ambience", "Music and ambience", SliderFormat::Percent, 0.0, 1.0, 0.05, s.ambience, &[]);
        });
        section(p, ui, "GRAPHICS").with_children(|p| {
            div(p, Node { row_gap: px(10.0), ..col() }).with_children(|p| {
                text(p, ui, "Anti-aliasing", Face::Medium, 15.0, TEXT);
                boxed(p, Node { padding: UiRect::all(px(3.0)), border_radius: radius(10.0), column_gap: px(2.0), align_self: AlignSelf::FlexStart, ..row() }, SUNK, CLEAR).with_children(|p| {
                    for (key, label) in [("off", "Off"), ("fxaa", "FXAA"), ("msaa4", "MSAA 4×")] {
                        let on = s.anti_aliasing == key;
                        let node = Node { height: px(34.0), min_width: px(64.0), padding: UiRect::horizontal(px(12.0)), border_radius: radius(8.0), justify_content: JustifyContent::Center, ..row() };
                        let look = if on { Look::new(LINE, CLEAR) } else { Look::new(CLEAR, CLEAR).hover_bg(RAISED) };
                        button(p, node, look, Click::Intent(format!("s:aa|{key}"))).with_children(|p| {
                            text(p, ui, label, Face::Medium, 13.0, if on { TEXT } else { MUTED });
                        });
                    }
                });
            });
        });
        section(p, ui, "PROFILE").with_children(|p| {
            div(p, Node { row_gap: px(8.0), ..col() }).with_children(|p| {
                text(p, ui, "Your name", Face::Medium, 15.0, TEXT);
                div(p, Node { column_gap: px(8.0), ..row() }).with_children(|p| {
                    div(p, Node { flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..row() }).with_children(|p| menu_field(p, ui, "settings-name", &s.name, ""));
                    let ghost = Node { height: px(44.0), padding: UiRect::horizontal(px(16.0)), border: UiRect::all(px(1.0)), border_radius: radius(10.0), flex_shrink: 0.0, ..row() };
                    button(p, ghost, Look::new(GLASS, LINE).hover_bg(RAISED), Click::Intent("s:name".into())).with_children(|p| {
                        text(p, ui, "Save", Face::Medium, 14.0, TEXT);
                    });
                });
            });
        });
        let soft = Node { padding: UiRect::axes(px(22.0), px(18.0)), row_gap: px(10.0), border: UiRect::all(px(1.0)), border_radius: radius(14.0), flex_shrink: 0.0, ..col() };
        boxed(p, soft, GLASS_SOFT, LINE).with_children(|p| {
            caps(p, ui, "REBINDING", 13.0);
            text(p, ui, "Click a key, then press the new key.", Face::Regular, 13.0, TEXT_2).insert(line_height(1.55));
            div(p, Node { column_gap: px(8.0), ..row() }).with_children(|p| {
                boxed(p, Node { padding: UiRect::axes(px(6.0), px(2.0)), border_radius: radius(4.0), ..default() }, RAISED, CLEAR).with_children(|p| {
                    text(p, ui, "Esc", Face::Mono, 12.0, TEXT);
                });
                text(p, ui, "Cancel", Face::Regular, 12.0, MUTED);
            });
        });
    });
}

fn grid_row() -> Vec<RepeatedGridTrack> { vec![RepeatedGridTrack::fr(1, 1.0), RepeatedGridTrack::px(1, 104.0), RepeatedGridTrack::px(1, 104.0)] }

fn keys(p: &mut Kids, ui: &Ui, s: &SettingsFrame) {
    p.spawn(glass(Node { flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), overflow: Overflow::clip(), ..col() })).with_children(|p| {
        boxed(p, Node { padding: UiRect::axes(px(24.0), px(18.0)), border: UiRect::bottom(px(1.0)), column_gap: px(16.0), ..row() }, CLEAR, LINE).with_children(|p| {
            text(p, ui, "KEYBINDS", Face::Regular, 13.0, MUTED).insert((spacing(1.0), Node { flex_grow: 1.0, ..default() }));
            if !s.notice.is_empty() {
                div(p, Node { column_gap: px(8.0), ..row() }).with_children(|p| {
                    icon(p, ui, "warning", 16.0, "ffc170");
                    text(p, ui, &s.notice, Face::Regular, 13.0, AMBER);
                });
            }
        });
        let body = Node {
            padding: UiRect::new(px(24.0), px(24.0), px(8.0), px(20.0)),
            column_gap: px(32.0),
            overflow: scroll_y(),
            align_items: AlignItems::FlexStart,
            flex_grow: 1.0,
            flex_shrink: 1.0,
            min_height: px(0.0),
            ..row()
        };
        p.spawn((body, scroller("keybinds", ui))).with_children(|p| {
            for column in &s.columns {
                div(p, Node { flex_grow: 1.0, flex_basis: px(0.0), ..col() }).with_children(|p| {
                    for group in column {
                        div(p, col()).with_children(|p| {
                            let head = Node { display: Display::Grid, grid_template_columns: grid_row(), column_gap: px(8.0), align_items: AlignItems::FlexEnd, padding: UiRect::new(px(8.0), px(8.0), px(14.0), px(6.0)), ..default() };
                            div(p, head).with_children(|p| {
                                text(p, ui, &group.name, Face::Medium, 13.0, TEXT);
                                text(p, ui, "Primary", Face::Regular, 12.0, MUTED).insert(centred());
                                text(p, ui, "Alternate", Face::Regular, 12.0, MUTED).insert(centred());
                            });
                            for r in &group.rows {
                                bind_row(p, ui, s, r);
                            }
                        });
                    }
                });
            }
        });
    });
}

fn bind_row(p: &mut Kids, ui: &Ui, s: &SettingsFrame, r: &BindDto) {
    let node = Node { display: Display::Grid, grid_template_columns: grid_row(), column_gap: px(8.0), align_items: AlignItems::Center, padding: UiRect::axes(px(8.0), px(3.0)), border_radius: radius(8.0), ..default() };
    p.spawn((node, BackgroundColor(CLEAR), BorderColor::all(CLEAR), Look::new(CLEAR, CLEAR).hover_bg(rgba(255, 255, 255, 0.03)), Hovered::default())).with_children(|p| {
        text(p, ui, &r.label, Face::Regular, 14.0, TEXT_2);
        let fixed_cap = |p: &mut Kids, label: &str| {
            boxed(p, Node { height: px(32.0), border_radius: radius(6.0), justify_content: JustifyContent::Center, ..row() }, SLOT_EMPTY, CLEAR).with_children(|p| {
                text(p, ui, label, Face::Mono, 13.0, DIM);
            });
        };
        if r.fixed {
            fixed_cap(p, &r.key);
            div(p, Node { height: px(32.0), justify_content: JustifyContent::Center, ..row() }).with_children(|p| {
                text(p, ui, "Fixed", Face::Regular, 12.0, FAINT);
            });
        } else {
            let listening = s.listening.as_deref() == Some(r.action.as_str());
            let node = Node { height: px(32.0), border: UiRect::all(px(1.0)), border_radius: radius(6.0), justify_content: JustifyContent::Center, ..row() };
            let look = if listening { Look::new(ACCENT_INK, ACCENT) } else { Look::new(RAISED, LINE_2).hover_border(FAINT) };
            let action = if listening { "s:cancel".to_string() } else { format!("s:rebind|{}", r.action) };
            button(p, node, look, Click::Intent(action)).with_children(|p| {
                text(p, ui, if listening { "Press…" } else { r.key.as_str() }, Face::MonoBold, 13.0, if listening { ACCENT } else { TEXT });
            });
            fixed_cap(p, &r.alternate);
        }
    });
}
