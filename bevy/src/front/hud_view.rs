// The in-world HUD (the "Food Factory UI" design's HUD board): the site card, cash with its rate and the clock, alerts, the
// crosshair with the aimed object's prompt, the hotbar and the key hints, plus the tooltip beside a free pointer. None of it
// takes the pointer, so the world stays clickable under it. A centred panel covers the cards; the hotbar stays.
use bevy::ui::UiTransform;

use super::*;

#[derive(Resource, Clone, PartialEq, Default)]
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

#[derive(Clone, PartialEq, Default)]
pub struct AlertDto {
    /// "warn" (an accent card with a title and detail) or "dot" (a one-line card with a coloured dot).
    pub kind: String,
    pub title: String,
    pub detail: String,
    pub time: String,
    pub colour: String,
}

#[derive(Clone, PartialEq, Default)]
pub struct HotbarSlotDto {
    pub key: String,
    pub icon: String,
    pub count: String,
    pub selected: bool,
}

#[derive(Clone, PartialEq, Default)]
pub struct KeyHintDto {
    pub key: String,
    pub label: String,
}

#[derive(Resource, Clone, PartialEq, Default)]
pub struct TooltipFrame {
    pub text: String,
    pub x: f32,
    pub y: f32,
}

/// Centred horizontally on its `left: 50%` (the design's `translateX(-50%)`).
fn centre_x(y: f32) -> UiTransform { UiTransform::from_translation(Val2::new(pct(-50.0), px(y))) }

fn hud_card(bg_node: Node) -> (Node, BackgroundColor, BorderColor, Pickable) {
    (Node { border: UiRect::all(px(1.0)), border_radius: radius(12.0), ..bg_node }, BackgroundColor(GLASS), BorderColor::all(LINE), Pickable::IGNORE)
}

pub(super) fn draw_hud(mut commands: Commands, ui: Ui, roots: Res<Roots>, hud: Res<HudFrame>, model: Res<UiModel>, mut last: Local<Option<(HudFrame, bool, bool)>>) {
    let key = (hud.clone(), model.open, model.right);
    if last.as_ref() == Some(&key) {
        return;
    }
    *last = Some(key);
    rebuild(&mut commands, roots.hud, |p| {
        if !hud.visible {
            return;
        }
        // A centred panel takes the screen (the hotbar stays); build mode's catalog docks right and leaves the HUD in view.
        let covered = model.open && !model.right;
        if !hud.loading.is_empty() {
            p.spawn((
                hud_card(Node { position_type: PositionType::Absolute, left: pct(50.0), top: pct(50.0), padding: UiRect::axes(px(20.0), px(14.0)), ..default() }),
                UiTransform::from_translation(Val2::new(pct(-50.0), pct(-50.0))),
            ))
            .with_children(|p| {
                text(p, &ui, &hud.loading, Face::Regular, 15.0, TEXT_2);
            });
        }
        if !covered && !hud.site.is_empty() {
            p.spawn(hud_card(Node { position_type: PositionType::Absolute, left: px(24.0), top: px(24.0), padding: UiRect::new(px(12.0), px(16.0), px(10.0), px(10.0)), column_gap: px(12.0), ..row() }))
                .with_children(|p| {
                    boxed(p, Node { width: px(32.0), height: px(32.0), border_radius: radius(8.0), justify_content: JustifyContent::Center, flex_shrink: 0.0, ..row() }, RAISED, CLEAR)
                        .with_children(|p| icon(p, &ui, "home", 18.0, "ece8e1"));
                    div(p, Node { row_gap: px(2.0), ..col() }).with_children(|p| {
                        text(p, &ui, &hud.site, Face::Medium, 15.0, TEXT);
                        text(p, &ui, &hud.sub, Face::Regular, 12.0, MUTED);
                    });
                });
        }
        if !covered && !(model.open && model.right) && !hud.cash.is_empty() {
            div(p, Node { position_type: PositionType::Absolute, right: px(24.0), top: px(24.0), width: px(280.0), row_gap: px(8.0), ..col() }).with_children(|p| {
                p.spawn(hud_card(Node { padding: UiRect::axes(px(16.0), px(14.0)), row_gap: px(6.0), ..col() })).with_children(|p| {
                    div(p, Node { justify_content: JustifyContent::SpaceBetween, ..row() }).with_children(|p| {
                        text(p, &ui, "CASH", Face::Regular, 12.0, MUTED).insert(spacing(1.0));
                        text(p, &ui, &hud.rate, Face::Mono, 12.0, if hud.rate_up { GREEN } else { ORANGE });
                    });
                    text(p, &ui, &hud.cash, Face::MonoBold, 30.0, TEXT).insert(spacing(-0.3));
                    divider(p, 4.0);
                    div(p, Node { column_gap: px(8.0), ..row() }).with_children(|p| {
                        icon(p, &ui, "clock", 14.0, "a8a49b");
                        text(p, &ui, &hud.day, Face::Regular, 13.0, TEXT_2);
                        text(p, &ui, "·", Face::Regular, 13.0, FAINT);
                        text(p, &ui, &hud.clock, Face::Mono, 13.0, TEXT_2);
                    });
                });
                for alert in &hud.alerts {
                    alert_card(p, &ui, alert);
                }
            });
        }
        if hud.crosshair {
            p.spawn((
                Node { position_type: PositionType::Absolute, left: pct(50.0), top: pct(50.0), width: px(6.0), height: px(6.0), margin: UiRect::new(px(-3.0), px(0.0), px(-3.0), px(0.0)), border_radius: radius(3.0), ..default() },
                BackgroundColor(TEXT),
                BoxShadow::new(rgba(0, 0, 0, 0.35), px(0.0), px(0.0), px(2.0), px(0.0)),
                Pickable::IGNORE,
            ));
        }
        if !hud.prompt.is_empty() && !model.open {
            p.spawn((
                Node { position_type: PositionType::Absolute, left: pct(50.0), top: pct(50.0), column_gap: px(10.0), padding: UiRect::new(px(8.0), px(14.0), px(8.0), px(8.0)), border_radius: radius(10.0), ..row() },
                BackgroundColor(GLASS),
                centre_x(28.0),
                Pickable::IGNORE,
            ))
            .with_children(|p| {
                boxed(p, Node { min_width: px(28.0), height: px(28.0), padding: UiRect::horizontal(px(6.0)), border_radius: radius(6.0), justify_content: JustifyContent::Center, ..row() }, TEXT, CLEAR)
                    .with_children(|p| {
                        text(p, &ui, &hud.prompt_key, Face::MonoBold, 14.0, INK);
                    });
                text(p, &ui, &hud.prompt, Face::Regular, 14.0, TEXT);
            });
        }
        if !hud.hint.is_empty() {
            p.spawn((
                Node { position_type: PositionType::Absolute, left: pct(50.0), bottom: px(104.0), padding: UiRect::axes(px(14.0), px(8.0)), border_radius: radius(10.0), max_width: px(900.0), ..default() },
                BackgroundColor(GLASS),
                centre_x(0.0),
                Pickable::IGNORE,
            ))
            .with_children(|p| {
                text(p, &ui, &hud.hint, Face::Regular, 13.0, TEXT_2);
            });
        }
        p.spawn((
            Node { position_type: PositionType::Absolute, left: pct(50.0), bottom: px(24.0), column_gap: px(6.0), padding: UiRect::all(px(8.0)), border: UiRect::all(px(1.0)), border_radius: radius(14.0), ..row() },
            BackgroundColor(GLASS),
            BorderColor::all(LINE),
            centre_x(0.0),
            Pickable::IGNORE,
        ))
        .with_children(|p| {
            for slot in &hud.hotbar {
                let node = Node { width: px(56.0), height: px(56.0), border: UiRect::all(px(2.0)), border_radius: radius(9.0), justify_content: JustifyContent::Center, flex_shrink: 0.0, ..row() };
                boxed(p, node, if slot.icon.is_empty() { SLOT_EMPTY } else { RAISED }, if slot.selected { ACCENT } else { CLEAR }).with_children(|p| {
                    if !slot.icon.is_empty() {
                        picture(p, ui.assets.load(slot.icon.clone()), 40.0);
                    }
                    text(p, &ui, &slot.key, Face::Mono, 10.0, if slot.selected { ACCENT } else { DIM })
                        .insert(Node { position_type: PositionType::Absolute, left: px(5.0), top: px(3.0), ..default() });
                    if !slot.count.is_empty() {
                        text(p, &ui, &slot.count, Face::MonoBold, 12.0, TEXT).insert(Node { position_type: PositionType::Absolute, right: px(5.0), bottom: px(3.0), ..default() });
                    }
                });
            }
        });
        if !covered {
            div(p, Node { position_type: PositionType::Absolute, left: px(24.0), bottom: px(24.0), column_gap: px(14.0), ..row() }).with_children(|p| {
                for key in &hud.keys {
                    hint(p, &ui, &key.key, &key.label);
                }
            });
        }
    });
}

fn alert_card(p: &mut Kids, ui: &Ui, alert: &AlertDto) {
    if alert.kind == "warn" {
        p.spawn(hud_card(Node { padding: UiRect::axes(px(14.0), px(12.0)), column_gap: px(10.0), align_items: AlignItems::FlexStart, ..row() }))
            .insert(BorderColor::all(ACCENT))
            .with_children(|p| {
                div(p, Node { margin: UiRect::top(px(1.0)), ..default() }).with_children(|p| icon(p, ui, "warning", 18.0, "f2a33a"));
                div(p, Node { row_gap: px(3.0), flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..col() }).with_children(|p| {
                    text(p, ui, &alert.title, Face::Medium, 14.0, TEXT);
                    if !alert.detail.is_empty() {
                        text(p, ui, &alert.detail, Face::Regular, 12.0, MUTED);
                    }
                });
            });
    } else {
        p.spawn(hud_card(Node { padding: UiRect::axes(px(14.0), px(10.0)), column_gap: px(10.0), ..row() })).with_children(|p| {
            boxed(p, Node { width: px(8.0), height: px(8.0), border_radius: radius(4.0), flex_shrink: 0.0, ..default() }, css(&alert.colour), CLEAR);
            text(p, ui, &alert.title, Face::Regular, 13.0, TEXT).insert(Node { flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..default() });
            if !alert.time.is_empty() {
                text(p, ui, &alert.time, Face::Mono, 12.0, ORANGE);
            }
        });
    }
}

pub(super) fn draw_tooltip(mut commands: Commands, ui: Ui, roots: Res<Roots>, tooltip: Res<TooltipFrame>, hud: Res<HudFrame>, mut last: Local<Option<(TooltipFrame, bool)>>) {
    let key = (tooltip.clone(), hud.visible);
    if last.as_ref() == Some(&key) {
        return;
    }
    *last = Some(key);
    rebuild(&mut commands, roots.tooltip, |p| {
        if !hud.visible || tooltip.text.is_empty() {
            return;
        }
        boxed(p, Node { position_type: PositionType::Absolute, left: px(tooltip.x), top: px(tooltip.y), padding: UiRect::axes(px(9.0), px(5.0)), border_radius: radius(6.0), ..default() }, rgba(10, 15, 20, 0.86), CLEAR)
            .with_children(|p| {
                text(p, &ui, &tooltip.text, Face::Regular, 14.0, hex(0xF0F2E8));
            });
    });
}
