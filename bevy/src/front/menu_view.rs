// The main menu, world select and connect pages (the "Main Menu" design) and the pause menu in the same style. Which page
// shows, the picked world, the play mode and the open dropdown are the UI's own state (MenuLocal); starting a world, joining
// or quitting goes to the game as the menu's intents.
use bevy::ui::UiTransform;

use super::*;

/// The main menu, world select and pause menu.
#[derive(Resource, Clone, PartialEq, Default)]
pub struct MenuFrame {
    /// "menu", "paused" or "hidden".
    pub screen: String,
    pub status: String,
    pub version: String,
    pub resume: Option<ResumeDto>,
    pub worlds: Vec<WorldDto>,
    pub new_name: String,
    pub address: String,
    pub host_address: String,
    /// The pause menu's line: what this session plays or hosts.
    pub session: String,
}

#[derive(Clone, PartialEq, Default)]
pub struct ResumeDto {
    pub sub: String,
    pub ago: String,
}

#[derive(Clone, PartialEq, Default)]
pub struct WorldDto {
    pub name: String,
    pub sub: String,
    pub day: String,
    pub cash: String,
    pub last: String,
}

#[derive(Clone, Copy, PartialEq, Default)]
pub enum Page {
    #[default]
    Main,
    Worlds,
    Connect,
}

/// The menus' own state. The world page's choices reset each time it opens.
#[derive(Resource, Clone, PartialEq, Default)]
pub struct MenuLocal {
    page: Page,
    /// The picked world's name, "" for a new one, or none for the first saved world.
    picked: Option<String>,
    host: bool,
    open: bool,
}

#[derive(Clone)]
pub enum MenuClick {
    Page(Page),
    Pick(String),
    Mode(bool),
    ToggleDropdown,
    Play,
    Leave,
}

const MODES: [(bool, &str, &str); 2] = [(false, "Singleplayer", "Only you. Nobody can join."), (true, "Host for others", "Others join this world at your host address.")];

fn selected<'a>(local: &MenuLocal, menu: &'a MenuFrame) -> Option<&'a WorldDto> {
    match local.picked.as_deref() {
        Some("") => None,
        Some(name) => menu.worlds.iter().find(|w| w.name == name).or(menu.worlds.first()),
        None => menu.worlds.first(),
    }
}

fn is_new(local: &MenuLocal, menu: &MenuFrame) -> bool { local.picked.as_deref() == Some("") || menu.worlds.is_empty() }

pub(super) fn menu_click(click: &MenuClick, local: &mut MenuLocal, menu: &MenuFrame, intents: &mut UiIntents) {
    match click {
        MenuClick::Page(page) => {
            if *page == Page::Worlds {
                *local = MenuLocal::default();
            }
            local.page = *page;
        }
        MenuClick::Pick(name) => local.picked = Some(name.clone()),
        MenuClick::Mode(host) => {
            local.host = *host;
            local.open = false;
        }
        MenuClick::ToggleDropdown => local.open = !local.open,
        MenuClick::Play => {
            let action = match (is_new(local, menu), local.host, selected(local, menu)) {
                (true, true, _) => "menu:host-new".to_string(),
                (true, false, _) => "menu:new".to_string(),
                (false, true, Some(w)) => format!("menu:host:{}", w.name),
                (false, false, Some(w)) => format!("menu:load:{}", w.name),
                (false, _, None) => return,
            };
            intents.0.push(action);
        }
        MenuClick::Leave => {
            local.page = Page::Main;
            intents.0.push("menu:leave".into());
        }
    }
}

pub(super) fn draw_menu(mut commands: Commands, ui: Ui, roots: Res<Roots>, menu: Res<MenuFrame>, local: Res<MenuLocal>, mut last: Local<Option<(MenuFrame, MenuLocal)>>) {
    let key = (menu.clone(), local.clone());
    if last.as_ref() == Some(&key) {
        return;
    }
    *last = Some(key);
    rebuild(&mut commands, roots.menu, |p| match menu.screen.as_str() {
        "menu" => match local.page {
            Page::Main => main(p, &ui, &menu),
            Page::Worlds => worlds(p, &ui, &menu, &local),
            Page::Connect => connect(p, &ui, &menu),
        },
        "paused" => paused(p, &ui, &menu),
        _ => {}
    });
}

/// The centred 440 px menu column.
fn menu_col() -> (Node, UiTransform, Pickable) {
    (
        Node { position_type: PositionType::Absolute, top: px(0.0), bottom: px(0.0), left: pct(50.0), width: px(440.0), justify_content: JustifyContent::Center, row_gap: px(44.0), ..col() },
        UiTransform::from_translation(Val2::new(pct(-50.0), px(0.0))),
        Pickable::IGNORE,
    )
}

fn resume(p: &mut Kids, ui: &Ui, resume: Option<&ResumeDto>) {
    let node = Node { padding: UiRect::axes(px(20.0), px(18.0)), border_radius: radius(14.0), column_gap: px(16.0), ..row() };
    button(p, node, Look::new(ACCENT, CLEAR).hover_bg(ACCENT_HOVER), Click::Intent("menu:resume".into())).with_children(|p| {
        boxed(p, Node { width: px(44.0), height: px(44.0), border_radius: radius(22.0), justify_content: JustifyContent::Center, flex_shrink: 0.0, ..row() }, INK, CLEAR)
            .with_children(|p| icon(p, ui, "play", 18.0, "f2a33a"));
        match resume {
            Some(r) => {
                div(p, Node { row_gap: px(3.0), flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..col() }).with_children(|p| {
                    text(p, ui, "Resume", Face::Medium, 22.0, INK);
                    text(p, ui, &r.sub, Face::Regular, 13.0, ACCENT_INK);
                });
                text(p, ui, &r.ago, Face::Mono, 12.0, ACCENT_INK).insert(Node { align_self: AlignSelf::FlexStart, margin: UiRect::top(px(4.0)), ..default() });
            }
            None => {
                text(p, ui, "Resume", Face::Medium, 22.0, INK).insert(Node { flex_grow: 1.0, ..default() });
            }
        }
    });
}

fn nav_button(p: &mut Kids, ui: &Ui, icon_name: &str, label: &str, quiet: bool, click: Click) {
    let node = Node { height: px(if quiet { 52.0 } else { 60.0 }), padding: UiRect::horizontal(px(18.0)), border: UiRect::all(px(1.0)), border_radius: radius(12.0), column_gap: px(14.0), ..row() };
    let look = Look::new(if quiet { GLASS_SOFT } else { GLASS }, LINE).hover_bg(RAISED).hover_border(LINE_3);
    button(p, node, look, click).with_children(|p| {
        icon(p, ui, icon_name, 20.0, "a8a49b");
        text(p, ui, label, Face::Medium, if quiet { 16.0 } else { 17.0 }, if quiet { TEXT_2 } else { TEXT }).insert(Node { flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..default() });
        if !quiet {
            icon(p, ui, "chevron-right", 16.0, "6a675f");
        }
    });
}

fn status(p: &mut Kids, ui: &Ui, status: &str, centre: bool) {
    if status.is_empty() {
        return;
    }
    let mut line = text(p, ui, status, Face::Regular, 13.0, AMBER);
    if centre {
        line.insert((centred(), Node { margin: UiRect::top(px(6.0)), ..default() }));
    }
}

fn main(p: &mut Kids, ui: &Ui, menu: &MenuFrame) {
    scene(p, true);
    p.spawn(menu_col()).with_children(|p| {
        div(p, Node { align_items: AlignItems::Center, row_gap: px(12.0), ..col() }).with_children(|p| {
            div(p, Node { column_gap: px(12.0), ..row() }).with_children(|p| {
                let tile = Node { width: px(40.0), height: px(40.0), border: UiRect::all(px(1.0)), border_radius: radius(10.0), justify_content: JustifyContent::Center, ..row() };
                boxed(p, tile, RAISED, LINE).with_children(|p| icon(p, ui, "factory", 22.0, "f2a33a"));
                text(p, ui, "FACTORY · RESTAURANT", Face::Mono, 12.0, MUTED).insert(spacing(1.7));
            });
            text(p, ui, "Food Factory", Face::Bold, 64.0, TEXT).insert(spacing(-1.3));
        });
        div(p, Node { row_gap: px(10.0), ..col() }).with_children(|p| {
            if let Some(r) = &menu.resume {
                resume(p, ui, Some(r));
            }
            nav_button(p, ui, "home", "Select World", false, Click::Menu(MenuClick::Page(Page::Worlds)));
            nav_button(p, ui, "server", "Connect to Server", false, Click::Menu(MenuClick::Page(Page::Connect)));
            nav_button(p, ui, "sliders", "Settings", false, Click::Intent("s:open".into()));
            divider(p, 6.0);
            nav_button(p, ui, "quit", "Quit", true, Click::Intent("menu:quit".into()));
            status(p, ui, &menu.status, true);
        });
    });
    p.spawn((
        Node { position_type: PositionType::Absolute, left: pct(50.0), bottom: px(28.0), column_gap: px(14.0), ..row() },
        UiTransform::from_translation(Val2::new(pct(-50.0), px(0.0))),
        Pickable::IGNORE,
    ))
    .with_children(|p| {
        hint(p, ui, "Tab", "Navigate");
        hint(p, ui, "Enter", "Select");
    });
    text(p, ui, &menu.version, Face::Mono, 12.0, DIM).insert(Node { position_type: PositionType::Absolute, right: px(28.0), bottom: px(28.0), ..default() });
}

fn ghost(p: &mut Kids, ui: &Ui, icon_name: Option<&str>, label: &str, back: bool, click: Click) {
    let node = Node {
        height: px(44.0),
        padding: UiRect::new(px(if back { 12.0 } else { 16.0 }), px(16.0), px(0.0), px(0.0)),
        border: UiRect::all(px(1.0)),
        border_radius: radius(10.0),
        column_gap: px(8.0),
        flex_shrink: 0.0,
        ..row()
    };
    button(p, node, Look::new(GLASS, LINE).hover_bg(RAISED), click).with_children(|p| {
        if let Some(name) = icon_name {
            icon(p, ui, name, 16.0, "ece8e1");
        }
        text(p, ui, label, Face::Medium, 14.0, TEXT);
    });
}

/// A page's header: Back, the title, and the page's own buttons.
pub(super) fn header(p: &mut Kids, ui: &Ui, title: &str, back: Click, extra: impl FnOnce(&mut Kids)) {
    div(p, Node { column_gap: px(20.0), ..row() }).with_children(|p| {
        ghost(p, ui, Some("chevron-left"), "Back", true, back);
        text(p, ui, title, Face::Medium, 32.0, TEXT).insert((spacing(-0.3), Node { flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..default() }));
        extra(p);
    });
}

pub(super) fn page() -> Node { Node { padding: UiRect::axes(px(96.0), px(48.0)), row_gap: px(28.0), flex_direction: FlexDirection::Column, ..fill() } }

pub(super) fn glass(node: Node) -> (Node, BackgroundColor, BorderColor, Pickable) {
    (Node { border: UiRect::all(px(1.0)), border_radius: radius(14.0), ..node }, BackgroundColor(GLASS), BorderColor::all(LINE), Pickable::IGNORE)
}

pub(super) fn caps(p: &mut Kids, ui: &Ui, label: &str, size: f32) { text(p, ui, label, Face::Regular, size, MUTED).insert(spacing(1.0)); }

/// The menus' text field (`.field`).
pub(super) fn menu_field(p: &mut Kids, ui: &Ui, id: &str, value: &str, placeholder: &str) {
    let style = FieldStyle {
        node: Node { height: px(44.0), padding: UiRect::new(px(12.0), px(12.0), px(12.0), px(0.0)), border: UiRect::all(px(1.0)), border_radius: radius(10.0), flex_grow: 1.0, ..default() },
        bg: RAISED,
        border: LINE,
        face: Face::Regular,
        size: 15.0,
        colour: TEXT,
        multiline: false,
    };
    field(p, ui, id, value, placeholder, style);
}

fn labelled_field(p: &mut Kids, ui: &Ui, label: &str, id: &str, value: &str, placeholder: &str) {
    div(p, Node { row_gap: px(6.0), ..col() }).with_children(|p| {
        caps(p, ui, label, 12.0);
        menu_field(p, ui, id, value, placeholder);
    });
}

fn cta(p: &mut Kids, ui: &Ui, label: &str, click: Click) {
    let node = Node { height: px(56.0), border_radius: radius(12.0), justify_content: JustifyContent::Center, column_gap: px(10.0), flex_shrink: 0.0, ..row() };
    button(p, node, Look::new(ACCENT, CLEAR).hover_bg(ACCENT_HOVER), click).with_children(|p| {
        icon(p, ui, "play", 16.0, "16171a");
        text(p, ui, label, Face::Medium, 17.0, INK);
    });
}

fn tile44(p: &mut Kids, ui: &Ui, icon_name: &str, colour: &str) {
    boxed(p, Node { width: px(44.0), height: px(44.0), border_radius: radius(10.0), justify_content: JustifyContent::Center, flex_shrink: 0.0, ..row() }, RAISED, CLEAR)
        .with_children(|p| icon(p, ui, icon_name, 20.0, colour));
}

fn grow() -> Node { Node { flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), ..default() } }

fn worlds(p: &mut Kids, ui: &Ui, menu: &MenuFrame, local: &MenuLocal) {
    let new = is_new(local, menu);
    let picked = selected(local, menu);
    scene(p, false);
    div(p, page()).with_children(|p| {
        header(p, ui, "Select world", Click::Menu(MenuClick::Page(Page::Main)), |p| {
            ghost(p, ui, Some("plus"), "New world", false, Click::Menu(MenuClick::Pick(String::new())));
        });
        div(p, Node { column_gap: px(24.0), align_items: AlignItems::Stretch, flex_grow: 1.0, flex_shrink: 1.0, min_height: px(0.0), ..row() }).with_children(|p| {
            p.spawn(glass(Node { flex_grow: 1.0, flex_shrink: 1.0, min_width: px(0.0), overflow: Overflow::clip(), ..col() })).with_children(|p| {
                let head = Node { padding: UiRect::axes(px(20.0), px(14.0)), border: UiRect::bottom(px(1.0)), column_gap: px(16.0), ..row() };
                boxed(p, head, CLEAR, LINE).with_children(|p| {
                    caps(p, ui, "WORLD", 12.0);
                    p.spawn((grow(), Pickable::IGNORE));
                    text(p, ui, "DAY", Face::Regular, 12.0, MUTED).insert((spacing(1.0), Node { width: px(120.0), ..default() }));
                    text(p, ui, "CASH", Face::Regular, 12.0, MUTED).insert((spacing(1.0), Node { width: px(120.0), ..default() }));
                    div(p, Node { width: px(140.0), justify_content: JustifyContent::FlexEnd, ..row() }).with_children(|p| caps(p, ui, "LAST PLAYED", 12.0));
                });
                let list = Node { padding: UiRect::all(px(8.0)), row_gap: px(4.0), flex_grow: 1.0, flex_shrink: 1.0, min_height: px(0.0), overflow: scroll_y(), ..col() };
                p.spawn((list, scroller("worlds", ui))).with_children(|p| {
                    if menu.worlds.is_empty() {
                        text(p, ui, "No saved worlds yet. Make one with New world.", Face::Regular, 14.0, MUTED).insert(Node { padding: UiRect::axes(px(12.0), px(24.0)), ..default() });
                    }
                    for w in &menu.worlds {
                        let on = !new && picked.is_some_and(|s| s.name == w.name);
                        let node = Node { padding: UiRect::axes(px(12.0), px(14.0)), border: UiRect::all(px(2.0)), border_radius: radius(10.0), column_gap: px(16.0), flex_shrink: 0.0, ..row() };
                        let look = if on { Look::new(RAISED, ACCENT) } else { Look::new(CLEAR, CLEAR).hover_bg(RAISED) };
                        button(p, node, look, Click::Menu(MenuClick::Pick(w.name.clone()))).with_children(|p| {
                            div(p, Node { column_gap: px(14.0), ..Node { flex_direction: FlexDirection::Row, align_items: AlignItems::Center, ..grow() } }).with_children(|p| {
                                tile44(p, ui, "home", "cfcbc3");
                                div(p, Node { row_gap: px(3.0), ..col() }).with_children(|p| {
                                    text(p, ui, &w.name, Face::Medium, 16.0, TEXT);
                                    text(p, ui, &w.sub, Face::Regular, 12.0, MUTED);
                                });
                            });
                            text(p, ui, &w.day, Face::Mono, 14.0, TEXT_2).insert(Node { width: px(120.0), ..default() });
                            text(p, ui, &w.cash, Face::Mono, 14.0, TEXT_2).insert(Node { width: px(120.0), ..default() });
                            div(p, Node { width: px(140.0), justify_content: JustifyContent::FlexEnd, ..row() }).with_children(|p| {
                                text(p, ui, &w.last, Face::Regular, 13.0, MUTED);
                            });
                        });
                    }
                });
            });
            aside(p, ui, menu, local, new, picked);
        });
    });
}

fn aside(p: &mut Kids, ui: &Ui, menu: &MenuFrame, local: &MenuLocal, new: bool, picked: Option<&WorldDto>) {
    p.spawn(glass(Node { width: px(460.0), padding: UiRect::all(px(24.0)), row_gap: px(20.0), flex_shrink: 0.0, ..col() })).with_children(|p| {
        boxed(p, Node { height: px(180.0), border_radius: radius(10.0), justify_content: JustifyContent::Center, align_items: AlignItems::Center, row_gap: px(12.0), flex_shrink: 0.0, ..col() }, RAISED, CLEAR)
            .with_children(|p| {
                icon(p, ui, "factory", 22.0, "f2a33a");
                text(p, ui, if new { "NEW WORLD" } else { "SAVED WORLD" }, Face::Mono, 12.0, FAINT).insert(spacing(1.9));
            });
        match picked.filter(|_| !new) {
            Some(w) => {
                div(p, Node { row_gap: px(6.0), ..col() }).with_children(|p| {
                    text(p, ui, &w.name, Face::Medium, 24.0, TEXT);
                    text(p, ui, format!("{} · Day {} · {}", w.sub, w.day, w.cash), Face::Regular, 13.0, MUTED);
                });
            }
            None => {
                div(p, Node { row_gap: px(12.0), ..col() }).with_children(|p| {
                    text(p, ui, "New world", Face::Medium, 24.0, TEXT);
                    labelled_field(p, ui, "NAME", "world-name", &menu.new_name, "");
                    labelled_field(p, ui, "SEED (OPTIONAL)", "seed", "", "Random");
                });
            }
        }
        divider(p, 0.0);
        div(p, Node { row_gap: px(8.0), position_type: PositionType::Relative, ..col() }).with_children(|p| {
            caps(p, ui, "PLAY AS", 12.0);
            let (_, label, desc) = MODES[usize::from(local.host)];
            let node = Node { min_height: px(60.0), padding: UiRect::axes(px(14.0), px(10.0)), border: UiRect::all(px(1.0)), border_radius: radius(10.0), column_gap: px(12.0), ..row() };
            button(p, node, Look::new(RAISED, if local.open { ACCENT } else { LINE }), Click::Menu(MenuClick::ToggleDropdown)).with_children(|p| {
                boxed(p, Node { width: px(36.0), height: px(36.0), border_radius: radius(8.0), justify_content: JustifyContent::Center, flex_shrink: 0.0, ..row() }, INK, CLEAR).with_children(|p| {
                    if local.host {
                        icon(p, ui, "users", 18.0, "f2a33a");
                    } else {
                        icon(p, ui, "user", 18.0, "ece8e1");
                    }
                });
                div(p, Node { row_gap: px(2.0), ..Node { flex_direction: FlexDirection::Column, ..grow() } }).with_children(|p| {
                    text(p, ui, label, Face::Medium, 15.0, TEXT);
                    text(p, ui, desc, Face::Regular, 12.0, MUTED);
                });
                icon(p, ui, "chevron-down", 16.0, "a8a49b");
            });
            if local.open {
                let list = Node {
                    position_type: PositionType::Absolute,
                    left: px(0.0),
                    right: px(0.0),
                    top: pct(100.0),
                    margin: UiRect::top(px(6.0)),
                    padding: UiRect::all(px(6.0)),
                    row_gap: px(2.0),
                    border: UiRect::all(px(1.0)),
                    border_radius: radius(12.0),
                    ..col()
                };
                p.spawn((list, BackgroundColor(hex(0x1C1D21)), BorderColor::all(LINE_3), BoxShadow::new(rgba(0, 0, 0, 0.45), px(0.0), px(16.0), px(0.0), px(40.0)), GlobalZIndex(14)))
                    .with_children(|p| {
                        for (host, label, desc) in MODES {
                            let on = local.host == host;
                            let node = Node { min_height: px(52.0), padding: UiRect::axes(px(10.0), px(8.0)), border_radius: radius(8.0), column_gap: px(12.0), ..row() };
                            let look = if on { Look::new(RAISED, CLEAR) } else { Look::new(CLEAR, CLEAR).hover_bg(RAISED) };
                            button(p, node, look, Click::Menu(MenuClick::Mode(host))).with_children(|p| {
                                div(p, Node { row_gap: px(2.0), ..Node { flex_direction: FlexDirection::Column, ..grow() } }).with_children(|p| {
                                    text(p, ui, label, Face::Medium, 14.0, TEXT);
                                    text(p, ui, desc, Face::Regular, 12.0, MUTED);
                                });
                                if on {
                                    icon(p, ui, "check", 16.0, "f2a33a");
                                }
                            });
                        }
                    });
            }
        });
        if local.host {
            labelled_field(p, ui, "HOST ADDRESS", "host-address", &menu.host_address, "");
        }
        div(p, Node { flex_grow: 1.0, ..default() });
        status(p, ui, &menu.status, false);
        let label = if local.host { "Host and play" } else if new { "Create and play" } else { "Play" };
        cta(p, ui, label, Click::Menu(MenuClick::Play));
    });
}

fn connect(p: &mut Kids, ui: &Ui, menu: &MenuFrame) {
    scene(p, false);
    div(p, page()).with_children(|p| {
        header(p, ui, "Connect to server", Click::Menu(MenuClick::Page(Page::Main)), |_| {});
        div(p, Node { justify_content: JustifyContent::Center, padding: UiRect::top(px(80.0)), ..row() }).with_children(|p| {
            p.spawn(glass(Node { width: px(520.0), padding: UiRect::all(px(24.0)), row_gap: px(18.0), ..col() })).with_children(|p| {
                div(p, Node { column_gap: px(14.0), ..row() }).with_children(|p| {
                    tile44(p, ui, "server", "a8a49b");
                    div(p, Node { row_gap: px(3.0), ..col() }).with_children(|p| {
                        text(p, ui, "Join a hosted world", Face::Medium, 18.0, TEXT);
                        text(p, ui, "The host shares its address and port.", Face::Regular, 12.0, MUTED);
                    });
                });
                labelled_field(p, ui, "SERVER ADDRESS", "address", &menu.address, "");
                status(p, ui, &menu.status, false);
                cta(p, ui, "Join", Click::Intent("menu:join".into()));
            });
        });
    });
}

fn paused(p: &mut Kids, ui: &Ui, menu: &MenuFrame) {
    // The pause menu blocks the world under it.
    p.spawn((fill(), Pickable::default())).with_children(|p| {
        vignette_fill(p);
        boxed(p, fill(), GLASS.with_alpha(0.55), CLEAR);
    });
    p.spawn(menu_col()).with_children(|p| {
        div(p, Node { align_items: AlignItems::Center, row_gap: px(10.0), ..col() }).with_children(|p| {
            text(p, ui, "Paused", Face::Bold, 48.0, TEXT).insert(spacing(-1.3));
            text(p, ui, &menu.session, Face::Regular, 14.0, MUTED);
        });
        div(p, Node { row_gap: px(10.0), ..col() }).with_children(|p| {
            resume(p, ui, None);
            nav_button(p, ui, "sliders", "Settings", false, Click::Intent("s:open".into()));
            nav_button(p, ui, "home", "Save and leave", false, Click::Menu(MenuClick::Leave));
            divider(p, 6.0);
            nav_button(p, ui, "quit", "Quit", true, Click::Intent("menu:quit".into()));
        });
    });
}
