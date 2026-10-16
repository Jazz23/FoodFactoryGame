// In-world screens (Unity PlayerHud machine and inventory screens, PhonePanel, LogisticsPanel, EmployeeScriptPanel and
// PropertyPanel), drawn with the shared UiModel: the inventory (carried goods and held machines), a machine (its input and
// output buffers, recipes, power, staff and pick-up), the phone (map places for sale, supplier shop, staff, sites, ledger,
// trucks and routes, farms), an employee (name, Luau program, run and stop) and a property (price and buy). Slots move a
// whole lot with one click: an inventory slot goes into the open machine, a machine slot comes back to the inventory. Every
// button sends a request; the next baseline shows what the server accepted.
use bevy::prelude::*;

use food_factory_bevy::content::OfferKind;
use food_factory_bevy::employees::tasks::{self, EmployeeTask, EmployeeTaskList, EmployeeTaskPower, EmployeeTaskType};
use food_factory_bevy::goods::{self, company, farms, readiness, rules, EquipmentState, Lot, Snapshot};
use food_factory_bevy::protocol::{ClientMessage, Command};

use crate::actions::{PlayerActions, UiFocus};
use crate::interaction::Cursor;
use crate::screens::{reason_text, Screen};
use crate::session::{money, Content, Joined, Session, SessionEvent};
use crate::space::Space;
use crate::ui::{UiEntry, UiFields, UiIntents, UiModel};

pub struct PanelsPlugin;

impl Plugin for PanelsPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<Panels>().add_systems(Update, (close_keys, handle, outcomes, draw).chain());
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum PhoneTab {
    Map,
    Shop,
    Staff,
    Places,
    Ledger,
    Trucks,
    Farms,
}

impl PhoneTab {
    const ALL: [PhoneTab; 7] = [PhoneTab::Map, PhoneTab::Shop, PhoneTab::Staff, PhoneTab::Places, PhoneTab::Ledger, PhoneTab::Trucks, PhoneTab::Farms];
    fn name(self) -> &'static str {
        match self {
            PhoneTab::Map => "Map",
            PhoneTab::Shop => "Shop",
            PhoneTab::Staff => "Staff",
            PhoneTab::Places => "Places",
            PhoneTab::Ledger => "Ledger",
            PhoneTab::Trucks => "Trucks",
            PhoneTab::Farms => "Farms",
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub enum Panel {
    Inventory,
    Machine(String),
    Phone(PhoneTab),
    Employee(String),
    Property(String),
    Build,
}

#[derive(Resource, Default)]
pub struct Panels {
    pub open: Option<Panel>,
    /// The last rejection, shown on the open screen.
    pub problem: String,
    /// Route editor choices on the trucks tab: pickup and dropoff dock indices.
    route_pickup: usize,
    route_dropoff: usize,
    farm_packs: i32,
    /// The employee screen's tab (tasks or program) and the task list being edited, by employee.
    employee_program: bool,
    draft: Option<(String, EmployeeTaskList)>,
}

impl Panels {
    pub fn any_open(&self) -> bool { self.open.is_some() && self.open != Some(Panel::Build) }
}

/// Esc closes any screen; E closes the inventory and machine screens, C the phone.
#[allow(clippy::too_many_arguments)]
fn close_keys(actions: Res<PlayerActions>, mut panels: ResMut<Panels>, typing: Res<crate::actions::UiTyping>, mut focus: ResMut<UiFocus>, mut screen: ResMut<Screen>, cursor: Res<Cursor>, settings: Res<crate::settings::SettingsScreen>) {
    if settings.open {
        return;
    }
    // Esc with nothing open (and an empty cursor) opens the pause menu, and closes it again.
    if actions.close_pressed && panels.open.is_none() && cursor.machine.is_none() {
        match *screen {
            Screen::Playing => {
                *screen = Screen::Paused;
                return;
            }
            Screen::Paused => {
                *screen = Screen::Playing;
                return;
            }
            Screen::Menu => {}
        }
    }
    if *screen != Screen::Playing {
        if panels.open.is_some() {
            panels.open = None;
        }
        return;
    }
    let close = match &panels.open {
        Some(Panel::Build) => false,
        Some(_) if actions.close_pressed => true,
        Some(Panel::Inventory | Panel::Machine(_)) if actions.inventory_pressed && !typing.0 => true,
        Some(Panel::Phone(_)) if actions.phone_pressed && !typing.0 => true,
        _ => false,
    };
    if close {
        panels.open = None;
        panels.problem.clear();
    }
    let wants = panels.open.is_some();
    if focus.0 != wants {
        focus.0 = wants;
    }
}

fn outcomes(mut events: MessageReader<SessionEvent>, mut panels: ResMut<Panels>) {
    for event in events.read() {
        if let SessionEvent::Outcome { outcome, .. } = event {
            panels.problem = if outcome.accepted { String::new() } else { reason_text(&outcome.reason) };
        }
    }
}

fn lots_in<'a>(view: &'a Snapshot, location: &str) -> Vec<&'a Lot> {
    let mut lots: Vec<&Lot> = view.lots.iter().filter(|l| l.location_id == location && l.quantity > 0).collect();
    lots.sort_by(|a, b| a.item_id.cmp(&b.item_id).then(a.id.cmp(&b.id)));
    lots
}

fn slot(content: &Content, action: String, lot: &Lot) -> UiEntry {
    let item = content.0.items.get(&lot.item_id);
    let name = item.map_or(lot.item_id.clone(), |i| i.display_name.clone());
    let label = format!("{name}\n{}{}", lot.quantity, if lot.spoiled { " spoiled" } else { "" });
    UiEntry::button(action, label).with_icon(item.map(|i| i.icon.clone()).unwrap_or_default())
}

fn view_of_equipment<'a>(world: &'a Joined, id: &str) -> Option<(&'a Snapshot, &'a goods::Equipment)> {
    world.views.values().find_map(|v| v.equipment.iter().find(|e| e.id == id).map(|e| (v, e)))
}

fn company_docks(world: &Joined) -> Vec<(String, String)> {
    let mut docks: Vec<(String, String)> = world.views.values().flat_map(|v| {
        v.equipment.iter().filter(|e| e.kind == goods::DOCK_KIND && e.placed() && world.company_sites().contains(&e.site_id)).map(|e| {
            let site = v.sites.iter().find(|s| s.id == e.site_id).map(|s| s.name.clone()).unwrap_or_else(|| e.site_id.clone());
            (e.id.clone(), format!("{site} dock"))
        }).collect::<Vec<_>>()
    }).collect();
    docks.sort();
    docks.dedup();
    docks
}

#[allow(clippy::too_many_arguments)]
/// Places a task can name: the employee's hands and every placed machine on its site.
fn task_places(world: &Joined, employee: &str) -> Vec<String> {
    let site = world.employees.iter().find(|e| e.id == employee).map(|e| e.site.clone()).unwrap_or_else(|| world.site.clone());
    let mut places = vec!["hands".to_string()];
    if let Some(view) = world.view_of(&site) {
        places.extend(view.equipment.iter().filter(|e| e.site_id == site && e.placed() && e.level == 0 && (e.input_capacity > 0 || e.output_capacity > 0 || e.kind == goods::COUNTER_KIND)).map(|e| e.id.clone()));
    }
    places
}

/// The task list being edited for an employee, started from its saved tasks.
fn draft_of<'a>(panels: &'a mut Panels, world: &Joined, employee: &str) -> &'a mut EmployeeTaskList {
    if panels.draft.as_ref().is_none_or(|d| d.0 != employee) {
        let saved = world.details.get(employee).map(|d| d.tasks.clone()).filter(|t| !t.is_empty())
            .or_else(|| world.views.values().find_map(|v| v.employees.iter().find(|e| e.id == employee)).map(|e| e.tasks.clone()))
            .unwrap_or_default();
        panels.draft = Some((employee.to_string(), EmployeeTaskList::from_json(&saved)));
    }
    &mut panels.draft.as_mut().expect("draft set above").1
}

#[allow(clippy::too_many_arguments)]
fn handle(
    time: Res<Time>, content: Res<Content>, mut intents: ResMut<UiIntents>, mut panels: ResMut<Panels>, mut session: ResMut<Session>, mut fields: ResMut<UiFields>, mut cursor: ResMut<Cursor>,
    space: Option<Res<Space>>, players: Query<&Transform, With<crate::player::Player>>,
) {
    let actions: Vec<String> = intents.0.iter().filter(|a| a.starts_with("p:")).cloned().collect();
    intents.0.retain(|a| !a.starts_with("p:"));
    let now = time.elapsed_secs_f64();
    for action in actions {
        let parts: Vec<&str> = action["p:".len()..].splitn(4, '|').collect();
        let Some(world) = session.joined() else { return };
        let site = world.site.clone();
        let inventory = world.inventory();
        let command = match parts.as_slice() {
            ["tab", tab] => {
                if let Some(tab) = PhoneTab::ALL.iter().find(|t| t.name() == *tab) {
                    panels.open = Some(Panel::Phone(*tab));
                }
                None
            }
            ["close"] => {
                panels.open = None;
                None
            }
            ["take", lot] => {
                let quantity = world.views.values().find_map(|v| v.lots.iter().find(|l| l.id == *lot)).map_or(0, |l| l.quantity);
                Some(Command::Transfer { lot: lot.to_string(), destination: inventory.clone(), quantity })
            }
            ["put", lot, destination] => {
                let quantity = world.views.values().find_map(|v| v.lots.iter().find(|l| l.id == *lot)).map_or(0, |l| l.quantity);
                Some(Command::Transfer { lot: lot.to_string(), destination: destination.to_string(), quantity })
            }
            ["hold", equipment] => {
                cursor.machine = Some(equipment.to_string());
                panels.open = None;
                None
            }
            ["pickup", equipment] => Some(Command::PickUp { equipment: equipment.to_string() }),
            ["sell", equipment] => Some(Command::Sell { equipment: equipment.to_string() }),
            ["recipe", equipment, recipe] => Some(Command::SelectRecipe { equipment: equipment.to_string(), recipe: recipe.to_string() }),
            ["start", station, recipe] => Some(Command::StartJob { station: station.to_string(), recipe: recipe.to_string() }),
            ["power", equipment, on] => Some(Command::SetPower { equipment: equipment.to_string(), on: *on == "on" }),
            ["staff", register, staff] => Some(Command::Staff { register: register.to_string(), staff: staff.to_string() }),
            ["buy", offer] => Some(Command::Buy { site: site.clone(), offer: offer.to_string() }),
            ["hire"] => Some(Command::Hire),
            ["fire", employee] => Some(Command::Fire { employee: employee.to_string() }),
            ["etab", tab] => {
                panels.employee_program = *tab == "program";
                None
            }
            ["task-add", kind] | ["task-del", kind] | ["task-up", kind] | ["task-src", kind] | ["task-dst", kind] | ["task-machine", kind] | ["task-any", kind]
            | ["task-item", kind] | ["task-power", kind] | ["task-recipe", kind] | ["task-type", kind] => {
                let verb = parts[0];
                if let Some(Panel::Employee(employee)) = panels.open.clone() {
                    let places = task_places(world, &employee);
                    let recipes_of = |machine: &str| -> Vec<String> {
                        let kind = world.views.values().find_map(|v| v.equipment.iter().find(|e| e.id == machine)).map(|e| e.kind.clone()).unwrap_or_default();
                        content.0.recipes.values().filter(|r| r.station_kind == kind).map(|r| r.id.clone()).collect()
                    };
                    let items: Vec<String> = content.0.items.keys().cloned().collect();
                    let draft = draft_of(&mut panels, world, &employee);
                    let index = kind.parse::<usize>().unwrap_or(usize::MAX);
                    let cycle = |list: &[String], current: &str| -> String {
                        let at = list.iter().position(|x| x == current).map_or(0, |i| (i + 1) % list.len().max(1));
                        list.get(at).cloned().unwrap_or_default()
                    };
                    match verb {
                        "task-add" => {
                            let task_type = match *kind { "Power" => EmployeeTaskType::Power, "Work" => EmployeeTaskType::Work, "Recipe" => EmployeeTaskType::Recipe, _ => EmployeeTaskType::Move };
                            draft.tasks.push(EmployeeTask { task_type, ..Default::default() });
                        }
                        "task-del" if index < draft.tasks.len() => {
                            draft.tasks.remove(index);
                        }
                        "task-up" if index > 0 && index < draft.tasks.len() => draft.tasks.swap(index, index - 1),
                        _ if index < draft.tasks.len() => {
                            let task = &mut draft.tasks[index];
                            match verb {
                                "task-src" => task.source = cycle(&places, &task.source),
                                "task-dst" => task.destination = cycle(&places, &task.destination),
                                "task-machine" => task.machine = cycle(&places[1..], &task.machine),
                                "task-any" => task.any_item = !task.any_item,
                                "task-item" => task.items = vec![cycle(&items, task.items.first().map_or("", |x| x.as_str()))],
                                "task-power" => task.power = match task.power { EmployeeTaskPower::On => EmployeeTaskPower::Off, EmployeeTaskPower::Off => EmployeeTaskPower::Toggle, EmployeeTaskPower::Toggle => EmployeeTaskPower::On },
                                "task-recipe" => {
                                    let mut list = vec![String::new()];
                                    list.extend(recipes_of(&task.machine));
                                    task.recipe = cycle(&list, &task.recipe);
                                }
                                _ => {}
                            }
                        }
                        _ => {}
                    }
                }
                None
            }
            ["task-run", employee] => {
                if let Some(list) = panels.draft.as_ref().filter(|d| d.0 == *employee).map(|d| d.1.clone()) {
                    let tasks = list.to_json().ok();
                    session.send(ClientMessage::EmployeeRun { employee: employee.to_string(), source: list.to_lua(), tasks });
                }
                None
            }
            ["employee", employee] => {
                panels.draft = None;
                session.send(ClientMessage::EmployeeDetail { employee: employee.to_string() });
                fields.0.remove(&format!("script-{employee}"));
                fields.0.remove(&format!("name-{employee}"));
                panels.open = Some(Panel::Employee(employee.to_string()));
                None
            }
            ["run", employee] => {
                let source = fields.0.get(&format!("script-{employee}")).cloned().or_else(|| world.details.get(*employee).map(|d| d.source.clone())).unwrap_or_default();
                session.send(ClientMessage::EmployeeRun { employee: employee.to_string(), source, tasks: None });
                None
            }
            ["stop", employee] => {
                session.send(ClientMessage::EmployeeStop { employee: employee.to_string() });
                None
            }
            ["rename-employee", employee] => {
                let name = fields.0.get(&format!("name-{employee}")).cloned().unwrap_or_default();
                session.send(ClientMessage::EmployeeRename { employee: employee.to_string(), name });
                None
            }
            ["rename-site", target] => {
                let name = fields.0.get(&format!("site-name-{target}")).cloned().unwrap_or_default();
                Some(Command::RenameSite { site: target.to_string(), name })
            }
            ["property", lot] => {
                panels.open = Some(Panel::Property(lot.to_string()));
                None
            }
            ["buy-property", lot] => Some(Command::BuyProperty { paying_site: site.clone(), lot: lot.to_string() }),
            ["route-pickup"] => {
                panels.route_pickup += 1;
                None
            }
            ["route-dropoff"] => {
                panels.route_dropoff += 1;
                None
            }
            ["create-route"] => {
                let docks = company_docks(world);
                if docks.len() < 2 {
                    panels.problem = "A route needs two docks.".into();
                    None
                } else {
                    let pickup = docks[panels.route_pickup % docks.len()].0.clone();
                    let dropoff = docks[panels.route_dropoff % docks.len()].0.clone();
                    Some(Command::CreateRoute { pickup, dropoff, items: vec![] })
                }
            }
            ["delete-route", route] => Some(Command::DeleteRoute { route: route.to_string() }),
            ["assign", truck, route] => Some(Command::AssignTruck { truck: truck.to_string(), route: route.to_string() }),
            ["sell-truck", truck] => Some(Command::SellTruck { truck: truck.to_string() }),
            ["packs", delta] => {
                panels.farm_packs = (panels.farm_packs.max(1) + delta.parse::<i32>().unwrap_or(0)).clamp(1, farms::MAX_FARM_ORDER_PACKS);
                None
            }
            ["farm-order", farm, offer, dock] => Some(Command::OrderFromFarm { farm: farm.to_string(), offer: offer.to_string(), packs: panels.farm_packs.max(1), dock: dock.to_string(), truck: String::new() }),
            ["cancel-schedule", schedule] => Some(Command::CancelFarmSchedule { schedule: schedule.to_string() }),
            other => {
                warn!("[Panels] Unknown action {other:?}");
                None
            }
        };
        let _ = (&space, &players);
        if let Some(command) = command {
            panels.problem.clear();
            session.command(command, now);
        }
    }
}

#[allow(clippy::too_many_arguments)]
fn draw(panels: Res<Panels>, session: Res<Session>, content: Res<Content>, mut model: ResMut<UiModel>, space: Option<Res<Space>>, players: Query<&Transform, With<crate::player::Player>>, screen: Res<Screen>, phone_map: Res<crate::phone_map::PhoneMap>) {
    if *screen != Screen::Playing {
        return;
    }
    let Some(open) = panels.open.clone().filter(|p| *p != Panel::Build) else { return };
    let Some(world) = session.joined() else { return };
    let Some(view) = world.view() else { return };
    let mut next = UiModel { open: true, ..default() };
    let inventory = world.inventory();
    match &open {
        Panel::Inventory => {
            next.heading = "Inventory".into();
            next.status = format!("Cash {}", money(world.cash()));
            let lots = lots_in(view, &inventory);
            let used = rules::slots_used(lots.iter().copied(), |i| content.0.max_stack(i));
            let capacity = view.locations.iter().find(|l| l.id == inventory).map_or(0, |l| l.capacity);
            next.entries.push(UiEntry::label("carried", format!("Carried goods ({used}/{capacity} slots)")));
            next.entries.push(UiEntry::row("inventory-slots", lots.iter().map(|l| slot(&content, format!("p:none|{}", l.id), l)).collect()));
            let held: Vec<&goods::Equipment> = view.equipment.iter().filter(|e| e.state == EquipmentState::Held && e.holder_id == world.player).collect();
            next.entries.push(UiEntry::label("held", if held.is_empty() { "No machines held. Buy them on the phone (C).".to_string() } else { "Held machines (pick one to place it; hotbar keys 1-9 do the same)".to_string() }));
            next.entries.push(UiEntry::row("held-slots", held.iter().map(|e| {
                let kind = content.0.equipment.get(&e.kind);
                UiEntry::button(format!("p:hold|{}", e.id), kind.map_or(e.kind.clone(), |k| k.display_name.clone())).with_icon(kind.map(|k| k.icon.clone()).unwrap_or_default())
            }).collect()));
        }
        Panel::Machine(id) => {
            let Some((site_view, piece)) = view_of_equipment(world, id) else { return };
            let kind = content.0.equipment.get(&piece.kind);
            next.wide = true;
            next.heading = kind.map_or(piece.kind.clone(), |k| k.display_name.clone());
            let job = site_view.jobs.iter().find(|j| j.station_id == piece.id);
            let mut status = match job {
                Some(job) if job.state == goods::JobState::Running => format!("Making {} ({} s left)", content.0.recipe_name(&job.recipe_id), job.remaining_seconds),
                Some(job) => format!("Blocked: {} waits for room in the output", content.0.recipe_name(&job.recipe_id)),
                None if piece.input_capacity > 0 && piece.output_capacity == 0 => "Storage".to_string(),
                None => "Idle".to_string(),
            };
            if kind.is_some_and(|k| k.manual_power) {
                status.push_str(if piece.powered_on { "   Power on" } else { "   Power off" });
            }
            next.status = status;
            let input = piece.input_location_id();
            let output = piece.output_location_id();
            if piece.input_capacity > 0 {
                let lots = lots_in(site_view, &input);
                let used = rules::slots_used(lots.iter().copied(), |i| content.0.max_stack(i));
                next.entries.push(UiEntry::label("input", format!("{} ({used}/{} slots){}", if piece.output_capacity > 0 { "Input" } else { "Contents" }, piece.input_capacity, if piece.input_refrigerated { ", refrigerated" } else { "" })));
                next.entries.push(UiEntry::row("input-slots", lots.iter().map(|l| slot(&content, format!("p:take|{}", l.id), l)).collect()));
            }
            if piece.output_capacity > 0 {
                let lots = lots_in(site_view, &output);
                next.entries.push(UiEntry::label("output", format!("Output ({} slots)", piece.output_capacity)));
                next.entries.push(UiEntry::row("output-slots", lots.iter().map(|l| slot(&content, format!("p:take|{}", l.id), l)).collect()));
            }
            if piece.input_capacity > 0 && piece.site_id == world.site {
                let lots = lots_in(view, &inventory);
                next.entries.push(UiEntry::label("carried", "Your goods (click to put in)"));
                next.entries.push(UiEntry::row("inventory-slots", lots.iter().map(|l| slot(&content, format!("p:put|{}|{input}", l.id), l)).collect()));
            }
            let recipes: Vec<&goods::RecipeDefinition> = content.0.recipes.values().filter(|r| r.station_kind == piece.kind).collect();
            if !recipes.is_empty() {
                next.entries.push(UiEntry::label("recipes", if piece.kind == goods::COUNTER_KIND { "Menu" } else { "Recipes" }));
                for recipe in &recipes {
                    let inputs: Vec<String> = recipe.inputs.iter().map(|i| format!("{} {}", i.quantity, content.0.item_name(&i.item_id))).collect();
                    let output = if recipe.is_sale() { format!("sells for {}", money(recipe.sale_cents)) } else { format!("{} {}", recipe.output_quantity, content.0.item_name(&recipe.output_item_id)) };
                    let label = format!("{}: {} -> {} ({} s)", content.0.recipe_name(&recipe.id), inputs.join(", "), output, recipe.duration_seconds);
                    let selected = piece.recipe_id == recipe.id;
                    let mut row = vec![UiEntry::button(format!("p:recipe|{}|{}", piece.id, recipe.id), label).selected(selected)];
                    if !recipe.is_sale() {
                        row.push(UiEntry::button(format!("p:start|{}|{}", piece.id, recipe.id), "Start"));
                    }
                    next.entries.push(UiEntry::row(format!("recipe-{}", recipe.id), row));
                }
            }
            if kind.is_some_and(|k| k.manual_power) {
                next.entries.push(UiEntry::button(format!("p:power|{}|{}", piece.id, if piece.powered_on { "off" } else { "on" }), if piece.powered_on { "Switch off" } else { "Switch on" }));
            }
            if piece.kind == goods::COUNTER_KIND || kind.is_some_and(|k| k.worker_bound) {
                let staff = if piece.staff_id.is_empty() { "nobody".to_string() } else if piece.staff_id == world.player { "you".to_string() } else {
                    site_view.employees.iter().find(|e| e.id == piece.staff_id).map(|e| e.name.clone()).unwrap_or_else(|| piece.staff_id.clone())
                };
                next.entries.push(UiEntry::label("staff", format!("Staffed by {staff}")));
                let mut row = vec![UiEntry::button(format!("p:staff|{}|{}", piece.id, world.player), "Staff it yourself"), UiEntry::button(format!("p:staff|{}|", piece.id), "Nobody")];
                for employee in site_view.employees.iter().filter(|e| e.site_id == piece.site_id) {
                    row.push(UiEntry::button(format!("p:staff|{}|{}", piece.id, employee.id), employee.name.clone()).selected(piece.staff_id == employee.id));
                }
                next.entries.push(UiEntry::row("staff-row", row));
            }
            if goods::is_table(piece) {
                next.entries.push(UiEntry::label("seats", format!("Seats {}", piece.seats)));
            }
            if piece.site_id == world.site {
                next.entries.push(UiEntry::row("machine-actions", vec![UiEntry::button(format!("p:pickup|{}", piece.id), "Pick up"), UiEntry::button(format!("p:sell|{}", piece.id), "Sell back")]));
            }
        }
        Panel::Phone(tab) => {
            next.wide = true;
            next.heading = "Phone".into();
            next.status = format!("{}   Cash {}", world.view().and_then(|v| v.sites.iter().find(|s| s.id == world.site)).map_or(world.site.clone(), |s| s.name.clone()), money(world.cash()));
            next.entries.push(UiEntry::row("tabs", PhoneTab::ALL.iter().map(|t| UiEntry::button(format!("p:tab|{}", t.name()), t.name()).selected(t == tab)).collect()));
            phone(&mut next, *tab, world, view, &content, &panels, space.as_deref(), players.single().ok(), phone_map.image.clone());
        }
        Panel::Employee(id) => {
            next.wide = true;
            let state = world.employees.iter().find(|e| &e.id == id);
            let record = world.views.values().find_map(|v| v.employees.iter().find(|e| &e.id == id));
            let name = state.map(|e| e.name.clone()).or_else(|| record.map(|e| e.name.clone())).unwrap_or_else(|| id.clone());
            next.heading = name.clone();
            let detail = world.details.get(id);
            next.status = match (state, detail) {
                (Some(s), _) if s.unpaid => "Unpaid: waits outside until wages can be paid.".to_string(),
                (_, Some(d)) if !d.status.is_empty() => d.status.clone(),
                (Some(s), _) => if s.running { format!("Running. {}", s.status) } else { "Idle".to_string() },
                _ => String::new(),
            };
            next.entries.push(UiEntry::row("name-row", vec![UiEntry::field(format!("name-{id}"), name), UiEntry::button(format!("p:rename-employee|{id}"), "Rename")]));
            next.entries.push(UiEntry::row("employee-tabs", vec![
                UiEntry::button("p:etab|tasks", "Tasks").selected(!panels.employee_program),
                UiEntry::button("p:etab|program", "Program").selected(panels.employee_program),
            ]));
            if panels.employee_program {
                next.entries.push(UiEntry::label("script-label", "Program (Luau): move(from, to, items), take(item, n), put(machine), work(machine), wait(s), say(text) ..."));
                let source = detail.map(|d| d.source.clone()).or_else(|| record.map(|r| r.script.clone())).unwrap_or_default();
                next.entries.push(UiEntry::area(format!("script-{id}"), source));
                next.entries.push(UiEntry::row("script-actions", vec![UiEntry::button(format!("p:run|{id}"), "Run"), UiEntry::button(format!("p:stop|{id}"), "Stop"), UiEntry::button(format!("p:fire|{id}"), "Fire")]));
            } else {
                let saved = detail.map(|d| d.tasks.clone()).filter(|t| !t.is_empty()).or_else(|| record.map(|r| r.tasks.clone())).unwrap_or_default();
                let list = panels.draft.as_ref().filter(|d| &d.0 == id).map(|d| d.1.clone()).unwrap_or_else(|| EmployeeTaskList::from_json(&saved));
                let name_of = |place: &str| -> String {
                    if place.is_empty() { return "(choose)".into(); }
                    if place == "hands" { return "hands".into(); }
                    world.views.values().find_map(|v| v.equipment.iter().find(|e| e.id == place)).map_or(place.to_string(), |e| format!("{} ({}, {})", content.0.equipment_name(&e.kind), e.cell_x, e.cell_z))
                };
                next.entries.push(UiEntry::label("tasks-help", "The employee repeats these in order. Run sends them as its program."));
                for (index, task) in list.tasks.iter().enumerate() {
                    let mut row = vec![UiEntry::label(format!("task-{index}"), format!("{}. {}", index + 1, tasks::describe(task)))];
                    match task.task_type {
                        EmployeeTaskType::Move => {
                            row.push(UiEntry::button(format!("p:task-src|{index}"), format!("From {}", name_of(&task.source))));
                            row.push(UiEntry::button(format!("p:task-dst|{index}"), format!("To {}", name_of(&task.destination))));
                            row.push(UiEntry::button(format!("p:task-any|{index}"), if task.any_item { "Any item" } else { "Only" }));
                            if !task.any_item {
                                row.push(UiEntry::button(format!("p:task-item|{index}"), task.items.first().map_or("(item)".to_string(), |i| content.0.item_name(i))));
                            }
                        }
                        EmployeeTaskType::Power => {
                            row.push(UiEntry::button(format!("p:task-machine|{index}"), name_of(&task.machine)));
                            row.push(UiEntry::button(format!("p:task-power|{index}"), format!("{:?}", task.power)));
                        }
                        EmployeeTaskType::Work | EmployeeTaskType::Recipe => {
                            row.push(UiEntry::button(format!("p:task-machine|{index}"), name_of(&task.machine)));
                            row.push(UiEntry::button(format!("p:task-recipe|{index}"), if task.recipe.is_empty() { "Any recipe".to_string() } else { content.0.recipe_name(&task.recipe) }));
                        }
                    }
                    row.push(UiEntry::button(format!("p:task-up|{index}"), "Up"));
                    row.push(UiEntry::button(format!("p:task-del|{index}"), "Remove"));
                    next.entries.push(UiEntry::row(format!("task-row-{index}"), row));
                }
                next.entries.push(UiEntry::row("task-add", vec![
                    UiEntry::button("p:task-add|Move", tasks::MOVE_LABEL),
                    UiEntry::button("p:task-add|Power", tasks::POWER_LABEL),
                    UiEntry::button("p:task-add|Work", tasks::WORK_LABEL),
                    UiEntry::button("p:task-add|Recipe", tasks::RECIPE_LABEL),
                ]));
                next.entries.push(UiEntry::row("task-actions", vec![UiEntry::button(format!("p:task-run|{id}"), "Run tasks"), UiEntry::button(format!("p:stop|{id}"), "Stop"), UiEntry::button(format!("p:fire|{id}"), "Fire")]));
            }
        }
        Panel::Property(lot) => {
            let offer = world.offers_by_lot.get(lot);
            next.heading = offer.map_or("Property".to_string(), |o| format!("{} lot", o.category));
            let owner = view.properties.iter().find(|p| &p.lot_id == lot).map(|p| p.company_id.clone());
            let competitor = view.competitors.iter().find(|c| &c.lot_id == lot);
            next.status = match (&owner, competitor, offer) {
                (Some(owner), _, _) if world.company().is_some_and(|c| &c.id == owner) => "Your company owns this.".to_string(),
                (Some(_), _, _) => "Owned by another company.".to_string(),
                (None, Some(c), Some(o)) => format!("{} ({}) - {}", c.name, c.cuisine, money(o.price_cents)),
                (None, None, Some(o)) => format!("For sale: {}", money(o.price_cents)),
                _ => "Not for sale.".to_string(),
            };
            if let Some(o) = offer {
                next.entries.push(UiEntry::label("size", format!("Lot {} x {} m, building {} x {} m, {} floor(s)", o.width, o.depth, o.building_width, o.building_depth, o.floors.max(1))));
                if owner.is_none() && o.for_sale {
                    next.entries.push(UiEntry::button(format!("p:buy-property|{lot}"), format!("Buy for {}", money(o.price_cents))));
                }
            }
            next.entries.push(UiEntry::button("p:tab|Map", "Back to the map"));
        }
        Panel::Build => {}
    }
    if !panels.problem.is_empty() {
        next.footer = panels.problem.clone();
    } else {
        next.footer = "Esc closes".into();
    }
    if *model != next {
        *model = next;
    }
}

#[allow(clippy::too_many_arguments)]
fn phone(next: &mut UiModel, tab: PhoneTab, world: &Joined, view: &Snapshot, content: &Content, panels: &Panels, space: Option<&Space>, player: Option<&Transform>, map_image: Option<Handle<Image>>) {
    let here = match (space, player) {
        (Some(space), Some(t)) => {
            let (x, z, _) = space.to_map(t.translation);
            Vec2::new(x, z)
        }
        _ => Vec2::ZERO,
    };
    let company = world.company();
    match tab {
        PhoneTab::Map => {
            if let Some(image) = map_image {
                next.entries.push(UiEntry::picture("phone-map", image, 480.0));
                next.entries.push(UiEntry::label("map-key", "White: you. Green: yours. Yellow: for sale. Red: competitors. Brown: farms."));
            }
            next.entries.push(UiEntry::label("map-help", "Places near you (open one to see its price)"));
            let owned: Vec<&str> = view.properties.iter().map(|p| p.lot_id.as_str()).collect();
            let mut offers: Vec<(f32, &goods::PropertyOffer)> = world.offers_by_lot.values().filter(|o| o.category != goods::FARM_CATEGORY || o.for_sale).map(|o| {
                let centre = Vec2::new(o.lot_x as f32 + o.width as f32 * 0.5, o.lot_z as f32 + o.depth as f32 * 0.5);
                (centre.distance(here), o)
            }).collect();
            offers.sort_by(|a, b| a.0.total_cmp(&b.0));
            for (distance, offer) in offers.into_iter().take(14) {
                let state = if owned.contains(&offer.lot_id.as_str()) { "owned".to_string() } else if offer.for_sale { money(offer.price_cents) } else { "not for sale".to_string() };
                next.entries.push(UiEntry::button(format!("p:property|{}", offer.lot_id), format!("{} {} - {:.0} m - {state}", offer.category, offer.building_id, distance)));
            }
        }
        PhoneTab::Shop => {
            for (heading, wanted) in [("Ingredients", 0), ("Machines and furniture", 1), ("Trucks", 2)] {
                next.entries.push(UiEntry::label(format!("shop-{wanted}"), heading));
                let mut row = vec![];
                for offer in &content.0.offers {
                    let (label, icon) = match (&offer.kind, wanted) {
                        (OfferKind::Goods(g), 0) => (format!("{} x{}\n{}", content.0.item_name(&g.item_id), g.quantity, money(offer.price_cents)), content.0.items.get(&g.item_id).map(|i| i.icon.clone()).unwrap_or_default()),
                        (OfferKind::Equipment(e), 1) => (format!("{}\n{}", content.0.equipment_name(&e.equipment.kind), money(offer.price_cents)), content.0.equipment.get(&e.equipment.kind).map(|k| k.icon.clone()).unwrap_or_default()),
                        (OfferKind::Truck(t), 2) => (format!("{} ({} slots)\n{}", t.name, t.cargo_slots, money(offer.price_cents)), String::new()),
                        _ => continue,
                    };
                    row.push(UiEntry::button(format!("p:buy|{}", offer.id), label).with_icon(icon));
                }
                next.entries.push(UiEntry::row(format!("shop-row-{wanted}"), row));
            }
        }
        PhoneTab::Staff => {
            let sites = world.company_sites();
            let wages = view.company_wage_cents_per_hour;
            next.entries.push(UiEntry::label("staff-help", format!("Wages {} per game hour. New staff stand beside you.", money(wages))));
            next.entries.push(UiEntry::button("p:hire", format!("Hire ({} per hour)", money(goods::employees::WAGE_CENTS_PER_HOUR))));
            for employee in world.employees.iter().filter(|e| sites.contains(&e.site)) {
                let status = if employee.unpaid { "unpaid".to_string() } else if employee.running { format!("working {}", employee.status) } else { "idle".to_string() };
                next.entries.push(UiEntry::row(format!("staff-{}", employee.id), vec![
                    UiEntry::button(format!("p:employee|{}", employee.id), format!("{} - {status}", employee.name)),
                    UiEntry::button(format!("p:fire|{}", employee.id), "Fire"),
                ]));
            }
        }
        PhoneTab::Places => {
            for site in world.company_sites() {
                let Some(record) = world.views.values().find_map(|v| v.sites.iter().find(|s| s.id == site)) else {
                    next.entries.push(UiEntry::label(format!("site-{site}"), site.clone()));
                    continue;
                };
                next.entries.push(UiEntry::row(format!("site-row-{site}"), vec![UiEntry::field(format!("site-name-{site}"), record.name.clone()), UiEntry::button(format!("p:rename-site|{site}"), "Rename")]));
                if let Some(site_view) = world.view_of(&site) {
                    let menu: Vec<&goods::RecipeDefinition> = content.0.recipes.values().filter(|r| rules::is_menu_item(r)).collect();
                    if let Some(ready) = readiness::evaluate(site_view, &site, world.offers.get(&site), menu) {
                        let text = match &ready.blocker {
                            None => "Open for customers.".to_string(),
                            Some(blocker) => format!("Not ready: {}", reason_text(blocker)),
                        };
                        next.entries.push(UiEntry::label(format!("ready-{site}"), text));
                    }
                }
            }
        }
        PhoneTab::Ledger => {
            let Some(company) = company else { return };
            for entry in company::ledger_of(view, &company.id).into_iter().rev().take(24) {
                let hour = entry.clock_seconds / food_factory_bevy::world::clock::HOUR_SECONDS;
                next.entries.push(UiEntry::label(format!("ledger-{}", entry.id), format!("h{hour:>4}  {:<18} {:>12}", entry.kind, money(entry.cents))));
            }
        }
        PhoneTab::Trucks => {
            let docks = company_docks(world);
            let routes: Vec<&goods::Route> = view.routes.iter().filter(|r| company.is_some_and(|c| c.id == r.company_id)).collect();
            next.entries.push(UiEntry::label("trucks", "Trucks"));
            for truck in view.trucks.iter().filter(|t| company.is_some_and(|c| c.id == t.company_id) && !t.supplier) {
                let mut row = vec![UiEntry::label(format!("truck-{}", truck.id), format!("{} ({:?})", truck.name, truck.state))];
                for route in &routes {
                    row.push(UiEntry::button(format!("p:assign|{}|{}", truck.id, route.id), format!("Route {}", route.id.rsplit(':').next().unwrap_or(""))).selected(truck.route_id == route.id));
                }
                row.push(UiEntry::button(format!("p:assign|{}|", truck.id), "Park").selected(truck.route_id.is_empty()));
                row.push(UiEntry::button(format!("p:sell-truck|{}", truck.id), "Sell"));
                next.entries.push(UiEntry::row(format!("truck-row-{}", truck.id), row));
            }
            next.entries.push(UiEntry::label("routes", "Routes"));
            let dock_name = |id: &str| docks.iter().find(|d| d.0 == id).map(|d| d.1.clone()).unwrap_or_else(|| id.to_string());
            for route in &routes {
                next.entries.push(UiEntry::row(format!("route-{}", route.id), vec![
                    UiEntry::label(format!("route-label-{}", route.id), format!("{} -> {}", dock_name(&route.pickup_dock_id), dock_name(&route.dropoff_dock_id))),
                    UiEntry::button(format!("p:delete-route|{}", route.id), "Delete"),
                ]));
            }
            if docks.len() >= 2 {
                let pickup = &docks[panels.route_pickup % docks.len()].1;
                let dropoff = &docks[panels.route_dropoff % docks.len()].1;
                next.entries.push(UiEntry::row("new-route", vec![
                    UiEntry::button("p:route-pickup", format!("From {pickup}")),
                    UiEntry::button("p:route-dropoff", format!("To {dropoff}")),
                    UiEntry::button("p:create-route", "Create route"),
                ]));
            } else {
                next.entries.push(UiEntry::label("route-help", "Routes run between two of your docks."));
            }
        }
        PhoneTab::Farms => {
            let docks: Vec<(String, String)> = company_docks(world).into_iter().filter(|d| view.equipment.iter().any(|e| e.id == d.0 && e.site_id == world.site)).collect();
            let Some((dock, dock_name)) = docks.first().cloned() else {
                next.entries.push(UiEntry::label("farm-help", "Farm deliveries need a dock at this site."));
                return;
            };
            let packs = panels.farm_packs.max(1);
            next.entries.push(UiEntry::row("packs", vec![
                UiEntry::button("p:packs|-1", "-"),
                UiEntry::label("packs-label", format!("{packs} pack(s), delivered to {dock_name} (+{} delivery)", money(farms::FARM_DELIVERY_FEE_CENTS))),
                UiEntry::button("p:packs|1", "+"),
            ]));
            let kinds = farms::farm_kinds(world.offers_by_lot.values());
            let mut nearby: Vec<(f32, &goods::PropertyOffer)> = world.offers_by_lot.values().filter(|o| o.category == goods::FARM_CATEGORY).map(|o| (Vec2::new(o.lot_x as f32, o.lot_z as f32).distance(here), o)).collect();
            nearby.sort_by(|a, b| a.0.total_cmp(&b.0));
            for (distance, farm) in nearby.into_iter().take(6) {
                let Some(kind) = kinds.get(&farm.lot_id) else { continue };
                let owned = company.is_some_and(|c| farms::owns_lot(view, &c.id, &farm.lot_id));
                next.entries.push(UiEntry::label(format!("farm-{}", farm.lot_id), format!("{} - {:.0} m{}", farms::farm_name(*kind), distance, if owned { " (yours, half price)" } else { "" })));
                let mut row = vec![];
                for item in farms::farm_items(*kind) {
                    let Some(offer) = content.0.goods_offer_for(item) else { continue };
                    let cents = farms::farm_goods_cents(offer.price_cents, packs, owned);
                    row.push(UiEntry::button(format!("p:farm-order|{}|{}|{dock}", farm.lot_id, offer.id), format!("{} x{}\n{}", content.0.item_name(item), offer.quantity, money(cents))).with_icon(content.0.items.get(*item).map(|i| i.icon.clone()).unwrap_or_default()));
                }
                next.entries.push(UiEntry::row(format!("farm-row-{}", farm.lot_id), row));
            }
            for schedule in view.farm_schedules.iter().filter(|s| company.is_some_and(|c| c.id == s.company_id)) {
                next.entries.push(UiEntry::row(format!("schedule-{}", schedule.id), vec![
                    UiEntry::label(format!("schedule-label-{}", schedule.id), format!("Every {} h: {} pack(s) {}", schedule.every_hours, schedule.packs, schedule.offer_id)),
                    UiEntry::button(format!("p:cancel-schedule|{}", schedule.id), "Cancel"),
                ]));
            }
        }
    }
}
