// The in-world HUD's data (Unity PlayerHud), drawn by the front end (front/hud_view.rs): the site card, cash with its recent rate and the
// clock, alerts (why the restaurant is not selling, wages about to run out, stock spoiling, notices), the aimed object's
// prompt under the crosshair, the hotbar with each held machine kind's icon and count, and a tooltip beside the free pointer.
// The restaurant readout is recomputed once per baseline revision.
use bevy::prelude::*;
use bevy::window::{CursorGrabMode, CursorOptions, PrimaryWindow};

use food_factory_bevy::goods::readiness::{self, RECENT_SECONDS};
use food_factory_bevy::goods::{self, EquipmentState};
use food_factory_bevy::world::clock::{DAY_SECONDS, HOUR_SECONDS};

use crate::actions::Bindings;
use crate::front::{AlertDto, HotbarSlotDto, HudFrame, KeyHintDto, TooltipFrame};
use crate::interaction::{Aim, Cursor, Hotbar, HotbarEntry, HOTBAR_SIZE};
use crate::map::MapState;
use crate::screens::{format_duration, reason_text, HudLines, Screen};
use crate::session::{money, Content, Session};
use crate::sites::{Indoors, Pick};

pub struct HudPlugin;

impl Plugin for HudPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Update, (emit_hud, emit_tooltip));
    }
}

const WARN: &str = "#E5644E";
const SOON: &str = "#FF9A6B";
const QUIET: &str = "#8F8B83";

/// The restaurant's readout for the site and revision it was computed at.
#[derive(Default)]
struct Readout {
    site: String,
    revision: i64,
    alerts: Vec<AlertDto>,
    rate_cents: Option<i64>,
}

fn readout(world: &crate::session::Joined, content: &Content) -> Readout {
    let mut out = Readout { site: world.site.clone(), revision: world.view().map_or(-1, |v| v.revision), ..default() };
    let Some(view) = world.view() else { return out };
    let menu: Vec<&goods::RecipeDefinition> = content.0.recipes.values().filter(|r| goods::rules::is_menu_item(r)).collect();
    let Some(ready) = readiness::evaluate(view, &world.site, world.offers.get(&world.site), menu.iter().copied()) else { return out };
    if let Some(blocker) = ready.blocker {
        out.alerts.push(AlertDto { kind: "warn".into(), title: "Not selling".into(), detail: crate::screens::blocker_text(blocker, &ready, &content.0, &menu), ..default() });
    } else {
        out.alerts.push(AlertDto { kind: "dot".into(), title: format!("Open: {} queued, {} eating", ready.queued, ready.eating), colour: "#6FCB86".into(), ..default() });
    }
    for warning in &ready.warnings {
        out.alerts.push(match *warning {
            readiness::STOCK_SPOILS_SOON => AlertDto {
                kind: "dot".into(), title: format!("{} spoils soon in a register", content.0.item_name(&ready.spoiling_item_id)),
                time: format_duration(ready.spoiling_seconds, false), colour: WARN.into(), ..default()
            },
            readiness::NO_REACHABLE_SEAT => AlertDto { kind: "dot".into(), title: "Takeaway only: no seat customers can reach".into(), colour: SOON.into(), ..default() },
            readiness::DOCK_NOT_BESIDE_BACK_DOOR => AlertDto { kind: "dot".into(), title: "A dock is not beside a back door".into(), colour: SOON.into(), ..default() },
            other => AlertDto { kind: "dot".into(), title: reason_text(other), colour: SOON.into(), ..default() },
        });
    }
    out.rate_cents = Some((ready.recent_sales_cents - ready.recent_spend_cents) * HOUR_SECONDS / RECENT_SECONDS);
    out
}

#[allow(clippy::too_many_arguments)]
fn emit_hud(
    session: Res<Session>, screen: Res<Screen>, map: Res<MapState>, hud: Res<HudLines>, hotbar: Res<Hotbar>, cursor: Res<Cursor>,
    content: Res<Content>, indoors: Res<Indoors>, bindings: Res<Bindings>, window: Query<&CursorOptions, With<PrimaryWindow>>, panels: Res<crate::panels::Panels>,
    mut card: Local<Readout>, mut out: ResMut<HudFrame>,
) {
    let playing = *screen == Screen::Playing;
    let mut frame = HudFrame { visible: playing && session.in_world(), ..default() };
    let key = crate::settings::label_of;
    if let (true, Some(world)) = (frame.visible, session.joined()) {
        if !map.built {
            frame.loading = map.progress.clone();
        }
        if let Some(view) = world.view() {
            let site = view.sites.iter().find(|s| s.id == world.site).map_or(world.site.clone(), |s| s.name.clone());
            let floor = match (&indoors.building, indoors.level) {
                (None, _) => "Outside".to_string(),
                (Some(_), 0) => "Ground floor".to_string(),
                (Some(_), level) => format!("Floor {}", level + 1),
            };
            let staff = world.employees.iter().filter(|e| e.site == world.site).count();
            let cap = goods::employees::employee_cap(view, &world.site);
            frame.site = site;
            frame.sub = format!("{floor} · {staff} of {cap} staff");
            frame.cash = money(world.cash());
            frame.day = format!("Day {}", view.clock_seconds / DAY_SECONDS + 1);
            frame.clock = format!("{:02}:{:02}", view.clock_seconds % DAY_SECONDS / HOUR_SECONDS, view.clock_seconds % HOUR_SECONDS);
            // The restaurant's standing with customers (its diner record), beside the cash.
            if let Some(diner) = view.diners.iter().find(|d| d.restaurant_id == world.site) {
                let limit = goods::customers::REPUTATION_LIMIT.max(1) as f32;
                frame.reputation = format!("{:+}", diner.reputation);
                frame.reputation_fill = (diner.reputation as f32 + limit) / (2.0 * limit);
                frame.reputation_up = diner.reputation >= 0;
            }
            if card.site != world.site || card.revision != view.revision {
                *card = readout(world, &content);
            }
            if let Some(rate) = card.rate_cents {
                frame.rate = format!("{}{} / h", if rate < 0 { "-" } else { "+" }, money(rate.abs()));
                frame.rate_up = rate >= 0;
            }
            // Unity PlayerHud's wage warning: cash covers less than two hours of wages.
            let wages = view.company_wage_cents_per_hour;
            if wages > 0 && panels.wage_warning_hours() > 0 && world.cash() < wages * panels.wage_warning_hours() {

                let hours = world.cash().max(0) / wages;
                let employees = wages / goods::employees::WAGE_CENTS_PER_HOUR;
                frame.alerts.push(AlertDto {
                    kind: "warn".into(),
                    title: if hours == 0 { "Wages run out within the hour".into() } else { format!("Wages run out in about {hours} h") },
                    detail: format!("{employees} staff at {} / h. Unpaid staff stop and wait outside.", money(goods::employees::WAGE_CENTS_PER_HOUR)),
                    ..default()
                });
            }
            frame.alerts.extend(card.alerts.iter().cloned());
            frame.alerts.extend(hud.notices.iter().map(|n| AlertDto { kind: "dot".into(), title: n.clone(), colour: QUIET.into(), ..default() }));
            // The aimed object's "E  Name" hint becomes the key-cap prompt under the crosshair; other hints sit above the hotbar.
            match hud.hint.split_once("  ") {
                Some((cap, rest)) if cap.chars().count() <= 3 && panels.open.is_none() => {
                    frame.prompt_key = cap.to_string();
                    frame.prompt = rest.to_string();
                }
                _ => frame.hint = hud.hint.clone(),
            }
            let locked = window.single().is_ok_and(|c| c.grab_mode != CursorGrabMode::None);
            frame.crosshair = locked && panels.open.is_none();
            let inventory = world.inventory();
            let current = cursor.machine.as_ref().and_then(|id| view.equipment.iter().find(|e| &e.id == id)).map(|e| HotbarEntry::Machine(e.kind.clone())).or_else(|| cursor.item.clone().map(HotbarEntry::Item));
            frame.hotbar = (0..HOTBAR_SIZE).map(|index| {
                let entry = hotbar.entries.get(index);
                let (icon, count) = match entry {
                    Some(HotbarEntry::Machine(kind)) => (
                        content.0.equipment.get(kind).map(|k| k.icon.clone()).unwrap_or_default(),
                        view.equipment.iter().filter(|e| e.state == EquipmentState::Held && e.holder_id == world.player && &e.kind == kind).count(),
                    ),
                    Some(HotbarEntry::Item(item)) => (
                        content.0.items.get(item).map(|i| i.icon.clone()).unwrap_or_default(),
                        view.lots.iter().filter(|l| l.location_id == inventory && &l.item_id == item).map(|l| l.quantity.max(0) as usize).sum(),
                    ),
                    None => (String::new(), 0),
                };
                HotbarSlotDto { key: (index + 1).to_string(), icon, count: if count > 0 { count.to_string() } else { String::new() }, selected: entry.is_some_and(|e| Some(e) == current.as_ref()) }
            }).collect();
            frame.keys = vec![
                KeyHintDto { key: key(bindings.inventory), label: "Inventory".into() },
                KeyHintDto { key: key(bindings.build), label: "Build".into() },
                KeyHintDto { key: key(bindings.phone), label: "Phone".into() },
                KeyHintDto { key: key(bindings.logistics), label: "Logistics".into() },
            ];
        }
    }
    out.set_if_neq(frame);
}

/// Names a usable hover target beside the free pointer; the crosshair prompt remains the locked-pointer fallback.
fn emit_tooltip(
    aim: Res<Aim>, session: Res<Session>, content: Res<Content>, screen: Res<Screen>, window: Query<(&Window, &CursorOptions), With<PrimaryWindow>>,
    scale: Res<UiScale>, mut out: ResMut<TooltipFrame>,
) {
    let mut frame = TooltipFrame::default();
    let free = window.single().ok().filter(|(_, c)| c.grab_mode == CursorGrabMode::None);
    if let (Some((window, _)), Some(world), true) = (free, session.joined(), *screen == Screen::Playing) {
        let picked = if aim.in_reach { match aim.pick.as_ref() {
            Some(Pick::Equipment(id)) => world.views.values().find_map(|v| v.equipment.iter().find(|e| &e.id == id)).map(|e| content.0.equipment_name(&e.kind)),
            Some(Pick::Employee(id)) => world.employees.iter().find(|e| &e.id == id).map(|e| e.name.clone()),
            _ => None,
        }} else { None };
        if let (Some(label), Some(pointer)) = (picked, window.cursor_position()) {
            // Window pixels to the scaled UI's.
            let at = Vec2::new((pointer.x + 16.0).min(window.width() - 180.0).max(4.0), (pointer.y + 18.0).min(window.height() - 42.0).max(4.0)) / scale.0;
            frame = TooltipFrame { text: label, x: at.x.round(), y: at.y.round() };
        }
    }
    out.set_if_neq(frame);
}
