// The session screens (Unity SessionPanel and the pause menu): outside a world, the main menu hosts a new or saved world
// (singleplayer, or hosting for others on a port) or joins a server by address; inside, Escape opens the pause menu (resume,
// leave, quit). The HUD line shows the site, cash and clock. Screens only call Session; the server decides everything.
use bevy::prelude::*;

use food_factory_bevy::persistence;
use food_factory_bevy::server;

use crate::actions::UiFocus;
use crate::panels::Panels;
use crate::session::{money, Content, Session, SessionEvent};
use crate::front::{FrontGeneration, MenuFrame, ResumeDto, Sent, WorldDto};
use crate::ui::{UiFields, UiIntents, UiModel};

pub struct ScreensPlugin;

impl Plugin for ScreensPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<Screen>().init_resource::<HudLines>().add_systems(Update, (handle_intents, notices, emit_menu).chain());
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


/// What the world list shows for a save, read once per modification time on a worker thread.
#[derive(Clone, Default)]
struct WorldSummary {
    sub: String,
    day: String,
    cash: String,
}

#[derive(Default)]
struct WorldList {
    refreshed: f64,
    saved: Vec<(String, std::time::SystemTime)>,
    summaries: std::sync::Arc<std::sync::Mutex<std::collections::HashMap<(String, std::time::SystemTime), WorldSummary>>>,
    pending: std::collections::HashSet<(String, std::time::SystemTime)>,
}

fn summarize(world: &str) -> WorldSummary {
    let Some(snapshot) = persistence::peek_snapshot(world) else { return WorldSummary { sub: "Unreadable save".into(), day: "-".into(), cash: "-".into() } };
    let company = snapshot.companies.first();
    let sites = company.map_or(0, |c| c.site_ids.len());
    let staff = snapshot.employees.len();
    WorldSummary {
        sub: format!("{sites} site{} · {staff} staff", if sites == 1 { "" } else { "s" }),
        day: (snapshot.clock_seconds / food_factory_bevy::world::clock::DAY_SECONDS + 1).to_string(),
        cash: company.map_or("-".into(), |c| money(c.cash)),
    }
}

/// "2 h ago", "Yesterday", "3 days ago".
fn ago(time: std::time::SystemTime) -> String {
    let seconds = std::time::SystemTime::now().duration_since(time).map_or(0, |d| d.as_secs());
    match seconds {
        0..60 => "Just now".into(),
        60..3600 => format!("{} min ago", seconds / 60),
        3600..86400 => format!("{} h ago", seconds / 3600),
        86400..172800 => "Yesterday".into(),
        _ => format!("{} days ago", seconds / 86400),
    }
}

#[allow(clippy::too_many_arguments)]
fn emit_menu(
    mut commands: Commands, session: Res<Session>, screen: Res<Screen>, mut model: ResMut<UiModel>, panels: Res<Panels>, mut focus: ResMut<UiFocus>,
    mut worlds: Local<WorldList>, time: Res<Time>, settings_screen: Res<crate::settings::SettingsScreen>, prefs: Option<Res<crate::settings::Prefs>>,
    generation: Res<FrontGeneration>, mut sent: Local<Sent<MenuFrame>>,
) {
    let now = time.elapsed_secs_f64();
    if worlds.saved.is_empty() && worlds.refreshed == 0.0 || now - worlds.refreshed > 3.0 {
        worlds.refreshed = now;
        worlds.saved = persistence::saved_worlds();
        // Summaries load off the main thread; a save rewritten since gets a new key and is read again.
        for key in worlds.saved.clone() {
            if worlds.pending.insert(key.clone()) {
                let summaries = worlds.summaries.clone();
                std::thread::spawn(move || {
                    let summary = summarize(&key.0);
                    if let Ok(mut map) = summaries.lock() { map.insert(key, summary); }
                });
            }
        }
    }
    // Outside a panel or settings the panel model is closed; the menus and HUD draw from their own frames.
    if panels.open.is_none() && !settings_screen.open && model.open {
        *model = UiModel::default();
    }
    let summaries = worlds.summaries.lock().map(|m| m.clone()).unwrap_or_default();
    let summary = |key: &(String, std::time::SystemTime)| summaries.get(key).cloned().unwrap_or_else(|| WorldSummary { sub: "Reading save…".into(), day: "-".into(), cash: "-".into() });
    let mut frame = MenuFrame {
        screen: match *screen { Screen::Menu => "menu", Screen::Paused => "paused", Screen::Playing => "hidden" }.into(),
        status: session.status.clone(),
        version: format!("v{}", env!("CARGO_PKG_VERSION")),
        new_name: server::next_world_name(),
        address: DEFAULT_ADDRESS.into(),
        host_address: DEFAULT_PORT_ADDRESS.into(),
        worlds: worlds.saved.iter().map(|key| {
            let s = summary(key);
            WorldDto { name: key.0.clone(), sub: s.sub, day: s.day, cash: s.cash, last: ago(key.1) }
        }).collect(),
        ..default()
    };
    let last = prefs.as_ref().map(|p| p.last_session.clone()).unwrap_or_default();
    if let Some(key) = last.strip_prefix("world:").and_then(|w| worlds.saved.iter().find(|x| x.0 == w)) {
        let s = summary(key);
        frame.resume = Some(ResumeDto { sub: format!("{} · Day {} · Singleplayer", key.0, s.day), ago: ago(key.1) });
    } else if let Some(address) = last.strip_prefix("server:") {
        frame.resume = Some(ResumeDto { sub: format!("Join {address}"), ago: String::new() });
    }
    if *screen == Screen::Paused {
        frame.session = session.hosted().map_or("Connected to a server.".to_string(), |h| match &h.listener {
            Some(l) => format!("Hosting {} on {}", h.world_name, l.address),
            None => format!("Playing {}", h.world_name),
        });
    }
    sent.send(&mut commands, &generation, frame);
    if panels.open.is_some() || settings_screen.open {
        if settings_screen.open && !focus.0 {
            focus.0 = true;
        }
        return;
    }
    let wants_focus = *screen != Screen::Playing;
    if focus.0 != wants_focus {
        focus.0 = wants_focus;
    }
}
