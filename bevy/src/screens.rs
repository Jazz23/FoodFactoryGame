// The session screens (Unity SessionPanel and the pause menu): outside a world, the main menu hosts a new or saved world
// (singleplayer, or hosting for others on a port) or joins a server by address; inside, Escape opens the pause menu (resume,
// leave, quit). The HUD line shows the site, cash and clock. Screens only call Session; the server decides everything.
use bevy::prelude::*;

use food_factory_bevy::persistence;
use food_factory_bevy::server;

use crate::actions::UiFocus;
use crate::map::MapState;
use crate::panels::Panels;
use crate::session::{money, Content, Session, SessionEvent};
use crate::ui::{UiEntry, UiFields, UiIntents, UiModel};

pub struct ScreensPlugin;

impl Plugin for ScreensPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<Screen>().init_resource::<HudLines>().add_systems(Update, (handle_intents, notices, draw_screen).chain());
    }
}

#[derive(Resource, Default, PartialEq, Clone, Copy, Debug)]
pub enum Screen {
    #[default]
    Menu,
    Playing,
    Paused,
}

const DEFAULT_ADDRESS: &str = "127.0.0.1:7777";
const DEFAULT_PORT_ADDRESS: &str = "0.0.0.0:7777";

#[derive(Default)]
struct Notices {
    lines: Vec<(String, f64)>,
}

fn field(fields: &UiFields, id: &str, default: &str) -> String { fields.0.get(id).cloned().filter(|v| !v.trim().is_empty()).unwrap_or_else(|| default.to_string()) }

#[allow(clippy::too_many_arguments)]
fn handle_intents(
    mut intents: ResMut<UiIntents>, mut session: ResMut<Session>, content: Res<Content>, fields: Res<UiFields>, mut screen: ResMut<Screen>,
    mut exit: MessageWriter<AppExit>, mut new_name: Local<String>, settings: Res<crate::session::Settings>, mut prefs: Option<ResMut<crate::settings::Prefs>>,
) {
    if new_name.is_empty() {
        *new_name = server::next_world_name();
    }
    let actions: Vec<String> = intents.0.iter().filter(|a| a.starts_with("menu:")).cloned().collect();
    intents.0.retain(|a| !a.starts_with("menu:"));
    for action in actions {
        let action = &action["menu:".len()..];
        session.display_name = field(&fields, "player-name", &session.display_name.clone());
        let mut remembered = None;
        let result = match action {
            "resume" if !session.in_world() => {
                let last = prefs.as_ref().map(|p| p.last_session.clone()).unwrap_or_default();
                if let Some(world) = last.strip_prefix("world:") {
                    session.host(&content.0, world, "", true, "")
                } else if let Some(address) = last.strip_prefix("server:") {
                    session.join(address);
                    Ok(())
                } else {
                    Ok(())
                }
            }
            "new" | "host-new" => {
                let name = field(&fields, "world-name", &new_name);
                let seed = field(&fields, "seed", "");
                let hosting = action == "host-new";
                let address = field(&fields, "host-address", DEFAULT_PORT_ADDRESS);
                *new_name = String::new();
                remembered = Some(format!("world:{name}"));
                session.host(&content.0, &name, &seed, !hosting, &address)
            }
            "join" => {
                let address = field(&fields, "address", DEFAULT_ADDRESS);
                remembered = Some(format!("server:{address}"));
                session.join(&address);
                Ok(())
            }
            "resume" => {
                *screen = Screen::Playing;
                Ok(())
            }
            "leave" => {
                session.leave();
                session.status = "Saved and left the world.".into();
                *screen = Screen::Menu;
                Ok(())
            }
            "quit" => {
                session.leave();
                exit.write(AppExit::Success);
                Ok(())
            }
            other => {
                if let Some(world) = other.strip_prefix("load:") {
                    remembered = Some(format!("world:{world}"));
                    session.host(&content.0, world, "", true, "")
                } else if let Some(world) = other.strip_prefix("host:") {
                    let address = field(&fields, "host-address", DEFAULT_PORT_ADDRESS);
                    session.host(&content.0, world, "", false, &address)
                } else {
                    Ok(())
                }
            }
        };
        match result {
            Err(problem) => session.status = format!("Could not start: {problem}"),
            Ok(()) => {
                if let (Some(last), Some(prefs)) = (remembered, prefs.as_mut()) {
                    crate::settings::remember_session(&settings, prefs, last);
                }
            }
        }
    }
    let in_world = session.in_world();
    if !in_world && *screen != Screen::Menu {
        *screen = Screen::Menu;
    } else if in_world && *screen == Screen::Menu {
        *screen = Screen::Playing;
    }
}

fn notices(mut events: MessageReader<SessionEvent>, time: Res<Time>, mut notices: Local<Notices>, mut model: ResMut<HudLines>) {
    let now = time.elapsed_secs_f64();
    for event in events.read() {
        match event {
            SessionEvent::Notice(text) | SessionEvent::Left(text) => notices.lines.push((text.clone(), now)),
            SessionEvent::Outcome { outcome, .. } if !outcome.accepted && !outcome.reason.is_empty() => notices.lines.push((reason_text(&outcome.reason), now)),
            _ => {}
        }
    }
    notices.lines.retain(|(_, at)| now - at < 6.0);
    let lines: Vec<String> = notices.lines.iter().rev().take(4).map(|(t, _)| t.clone()).collect();
    if model.notices != lines {
        model.notices = lines;
    }
}

/// A rejection reason as a sentence.
pub fn reason_text(reason: &str) -> String {
    let text = reason.replace('-', " ");
    let mut chars = text.chars();
    match chars.next() {
        Some(first) => first.to_uppercase().collect::<String>() + chars.as_str(),
        None => String::new(),
    }
}

/// Lines the HUD shows under the status (notices and hints), filled by screens and panels.
#[derive(Resource, Default)]
pub struct HudLines {
    pub notices: Vec<String>,
    pub hint: String,
}

/// The HUD's restaurant card (Unity PlayerHud.WorldText): what stops sales or that it is open, the warnings, recent money.
pub fn readiness_card(ready: &food_factory_bevy::goods::readiness::Readiness, content: &food_factory_bevy::content::GameContent, menu: &[&food_factory_bevy::goods::RecipeDefinition]) -> String {
    use food_factory_bevy::goods::readiness::*;
    let mut lines = vec![match ready.blocker {
        None => format!("Open: {} queued, {} eating", ready.queued, ready.eating),
        Some(blocker) => format!("Not selling: {}", blocker_text(blocker, ready, content, menu)),
    }];
    let warnings: Vec<String> = ready.warnings.iter().map(|w| match *w {
        NO_REACHABLE_SEAT => "takeaway only: no seat customers can reach".to_string(),
        DOCK_NOT_BESIDE_BACK_DOOR => "a dock is not beside a back door".to_string(),
        STOCK_SPOILS_SOON => format!("{} in a register spoils in {}", content.item_name(&ready.spoiling_item_id), format_duration(ready.spoiling_seconds, false)),
        other => reason_text(other),
    }).collect();
    if !warnings.is_empty() { lines.push(warnings.join(", ")); }
    lines.push(format!("Last {}: {} sale{} {}, spent {}", format_duration(RECENT_SECONDS, true), ready.recent_sales, if ready.recent_sales == 1 { "" } else { "s" },
        money(ready.recent_sales_cents), money(ready.recent_spend_cents)));
    lines.join("
")
}

pub fn blocker_text(blocker: &str, ready: &food_factory_bevy::goods::readiness::Readiness, content: &food_factory_bevy::content::GameContent, menu: &[&food_factory_bevy::goods::RecipeDefinition]) -> String {
    use food_factory_bevy::goods::readiness::*;
    match blocker {
        NO_CUSTOMER_DOOR => "the restaurant has no customer door (add a door in build mode, B)".into(),
        NO_REACHABLE_REGISTER if ready.registers == 0 => "no register (buy one in build mode, B)".into(),
        NO_REACHABLE_REGISTER => "customers cannot reach a register from the front door".into(),
        NO_STAFFED_REGISTER => "nobody works a register (open it and choose Work this register)".into(),
        NO_EDIBLE_MENU_ITEM => {
            let mut foods: Vec<String> = vec![];
            for item in menu.iter().flat_map(|r| &r.inputs).map(|i| content.item_name(&i.item_id)) { if !foods.contains(&item) { foods.push(item); } }
            let food = if foods.is_empty() { "food".to_string() } else if foods.len() <= 3 { foods.join(" or ") } else { "menu food".to_string() };
            format!("no {food} in a worked register (put some in its input)")
        }
        other => reason_text(other),
    }
}

/// Game seconds as Unity's PlayerHud.FormatDuration writes them: compact "10m", or "10m 05s", "2h 03m", "1d 4h".
pub fn format_duration(seconds: i64, compact: bool) -> String {
    let s = seconds.max(0);
    if compact {
        return if s < 60 { format!("{s}s") } else if s < 3600 { format!("{}m", s / 60) } else if s < 86400 { format!("{}h", s / 3600) } else { format!("{}d", s / 86400) };
    }
    if s < 60 { format!("{s}s") } else if s < 3600 { format!("{}m {:02}s", s / 60, s % 60) } else if s < 86400 { format!("{}h {:02}m", s / 3600, s / 60 % 60) } else { format!("{}d {}h", s / 86400, s / 3600 % 24) }
}

/// A ledger entry's kind as shown to players (Unity PlayerHud.LedgerKind).
pub fn ledger_kind(kind: &str) -> String {
    use food_factory_bevy::goods::company::*;
    match kind {
        LEDGER_SALE => "Sale", LEDGER_SUPPLIER_GOODS => "Supplier goods", LEDGER_SUPPLIER_EQUIPMENT => "Supplier equipment", LEDGER_TRUCK => "Truck",
        LEDGER_PROPERTY => "Property", LEDGER_FLOOR => "Factory floor", LEDGER_FURNISH => "Build: furnishing", LEDGER_SELL_BACK => "Sold back",
        LEDGER_SHELL => "Build: walls and doors", LEDGER_ADJUSTMENT => "Adjustment", LEDGER_WAGE => "Wages", LEDGER_FARM_GOODS => "Farm goods",
        LEDGER_FARM_DELIVERY => "Farm delivery", LEDGER_FARM_REFUND => "Farm refund", other => other,
    }.to_string()
}

fn clock_text(seconds: i64) -> String {
    let day = seconds / food_factory_bevy::world::clock::DAY_SECONDS + 1;
    let hour = seconds % food_factory_bevy::world::clock::DAY_SECONDS / food_factory_bevy::world::clock::HOUR_SECONDS;
    format!("Day {day}, {hour:02}:00")
}

#[allow(clippy::too_many_arguments)]
fn draw_screen(
    session: Res<Session>, screen: Res<Screen>, mut model: ResMut<UiModel>, map: Res<MapState>, hud: Res<HudLines>, panels: Res<Panels>, mut focus: ResMut<UiFocus>,
    mut worlds: Local<Option<(f64, Vec<String>)>>, time: Res<Time>, settings_screen: Res<crate::settings::SettingsScreen>, prefs: Option<Res<crate::settings::Prefs>>,
    content: Res<Content>, mut card: Local<(String, i64, String)>,
) {
    let now = time.elapsed_secs_f64();
    if worlds.as_ref().is_none_or(|(at, _)| now - at > 3.0) {
        *worlds = Some((now, persistence::saved_worlds().into_iter().map(|w| w.0).collect()));
    }
    if panels.open.is_some() {
        return;
    }
    if settings_screen.open {
        if !focus.0 {
            focus.0 = true;
        }
        return;
    }
    let next = match *screen {
        Screen::Menu => {
            let last = prefs.as_ref().map(|p| p.last_session.clone()).unwrap_or_default();
            let mut entries = vec![];
            if let Some(world) = last.strip_prefix("world:").filter(|w| worlds.as_ref().is_some_and(|(_, list)| list.iter().any(|x| x == w))) {
                entries.push(UiEntry::button("menu:resume", format!("Resume {world}")));
            } else if let Some(address) = last.strip_prefix("server:") {
                entries.push(UiEntry::button("menu:resume", format!("Resume: join {address}")));
            }
            entries.extend([
                UiEntry::label("name-label", "Your name"),
                UiEntry::field("player-name", session.display_name.clone()),
                UiEntry::label("new-label", "New world (name, seed)"),
                UiEntry::field("world-name", server::next_world_name()),
                UiEntry::field("seed", ""),
                UiEntry::button("menu:new", "Create singleplayer world"),
                UiEntry::button("menu:host-new", "Create and host for others"),
            ]);
            let saved = worlds.as_ref().map(|w| w.1.clone()).unwrap_or_default();
            if !saved.is_empty() {
                entries.push(UiEntry::label("saved-label", "Saved worlds"));
                for world in saved.iter().take(8) {
                    entries.push(UiEntry::button(format!("menu:load:{world}"), format!("Play {world}")));
                    entries.push(UiEntry::button(format!("menu:host:{world}"), format!("Host {world}")));
                }
            }
            entries.push(UiEntry::label("host-label", "Host address"));
            entries.push(UiEntry::field("host-address", DEFAULT_PORT_ADDRESS));
            entries.push(UiEntry::label("join-label", "Join a server"));
            entries.push(UiEntry::field("address", DEFAULT_ADDRESS));
            entries.push(UiEntry::button("menu:join", "Join"));
            entries.push(UiEntry::button("s:open", "Settings"));
            entries.push(UiEntry::button("menu:quit", "Quit"));
            let status = if session.status.is_empty() { "Build a food business in a generated city.".to_string() } else { session.status.clone() };
            UiModel { heading: "Food Factory".into(), status, footer: "Bevy port".into(), open: true, wide: false, right: false, entries }
        }
        Screen::Paused => UiModel {
            heading: "Paused".into(),
            status: session.hosted().map_or("Connected to a server.".to_string(), |h| match &h.listener {
                Some(l) => format!("Hosting {} on {}", h.world_name, l.address),
                None => format!("Playing {}", h.world_name),
            }),
            footer: String::new(),
            open: true,
            wide: false, right: false,
            entries: vec![UiEntry::button("menu:resume", "Resume"), UiEntry::button("s:open", "Settings"), UiEntry::button("menu:leave", "Save and leave"), UiEntry::button("menu:quit", "Quit")],
        },
        Screen::Playing => {
            let (heading, status) = match session.joined() {
                Some(world) if map.built => {
                    let site = world.view().and_then(|v| v.sites.iter().find(|s| s.id == world.site)).map(|s| s.name.clone()).unwrap_or_else(|| world.site.clone());
                    let clock = world.view().map(|v| clock_text(v.clock_seconds)).unwrap_or_default();
                    // Unity PlayerHud.Readiness: the client site's readout, recomputed once per baseline revision.
                    let revision = world.view().map_or(-1, |v| v.revision);
                    if card.0 != world.site || card.1 != revision {
                        let menu: Vec<&food_factory_bevy::goods::RecipeDefinition> = content.0.recipes.values().filter(|r| food_factory_bevy::goods::rules::is_menu_item(r)).collect();
                        let text = world.view().and_then(|v| food_factory_bevy::goods::readiness::evaluate(v, &world.site, world.offers.get(&world.site), menu.iter().copied()))
                            .map(|ready| readiness_card(&ready, &content.0, &menu)).unwrap_or_default();
                        *card = (world.site.clone(), revision, text);
                    }
                    let status = if card.2.is_empty() { clock } else { format!("{clock}
{}", card.2) };
                    (format!("{site}   {}", money(world.cash())), status)
                }
                Some(_) => ("Food Factory".into(), map.progress.clone()),
                None => ("Food Factory".into(), session.status.clone()),
            };
            let mut footer = hud.notices.join("\n");
            if !hud.hint.is_empty() {
                if !footer.is_empty() {
                    footer.push('\n');
                }
                footer.push_str(&hud.hint);
            }
            UiModel { heading, status, footer, open: false, wide: false, right: false, entries: vec![] }
        }
    };
    if *model != next {
        *model = next;
    }
    let wants_focus = *screen != Screen::Playing;
    if focus.0 != wants_focus && panels.open.is_none() {
        focus.0 = wants_focus;
    }
}
