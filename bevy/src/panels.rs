// In-world screens (Unity PlayerHud machine and inventory screens, PhonePanel, LogisticsPanel, EmployeeScriptPanel and
// PropertyPanel), drawn with the shared UiModel: the inventory (carried goods and held machines), a machine (its input and
// output buffers, recipes, power, staff and pick-up), the phone (map places for sale, supplier shop, staff, sites, ledger,
// trucks and routes, farms), an employee (name, Luau program, run and stop) and a property (price and buy). Slots move a
// whole lot with one click: an inventory slot goes into the open machine, a machine slot comes back to the inventory. Every
// button sends a request; the next baseline shows what the server accepted.
use bevy::prelude::*;

use food_factory_bevy::content::OfferKind;
use food_factory_bevy::employees::tasks::{self, EmployeeTask, EmployeeTaskList, EmployeeTaskPower, EmployeeTaskType};
use food_factory_bevy::goods::{self, company, farms, readiness, rules, EquipmentState, Lot, Snapshot, TruckState};
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
    Recipes,
}

impl PhoneTab {
    const ALL: [PhoneTab; 8] = [PhoneTab::Map, PhoneTab::Shop, PhoneTab::Staff, PhoneTab::Places, PhoneTab::Ledger, PhoneTab::Trucks, PhoneTab::Farms, PhoneTab::Recipes];
    fn name(self) -> &'static str {
        match self {
            PhoneTab::Map => "Map",
            PhoneTab::Shop => "Shop",
            PhoneTab::Staff => "Staff",
            PhoneTab::Places => "Places",
            PhoneTab::Ledger => "Ledger",
            PhoneTab::Trucks => "Trucks",
            PhoneTab::Farms => "Farms",
            PhoneTab::Recipes => "Recipes",
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
    /// Route editor drafts (Unity LogisticsPanel drafts) by route ID, "new" for the route being created: pickup dock,
    /// dropoff dock and the one cargo item allowed (none for any).
    route_drafts: std::collections::HashMap<String, RouteDraft>,
    farm_packs: i32,
    /// Farm order options (Unity PhonePanel's farm form): which dock at this site, pickup by which parked company truck
    /// (none for delivery), and a repeat interval in game hours (none for a one-off order).
    farm_dock: usize,
    farm_truck: Option<String>,
    farm_repeat: Option<i32>,
    /// The employee screen's tab (tasks or program) and the task list being edited, by employee.
    employee_program: bool,
    draft: Option<(String, EmployeeTaskList)>,
    recipe: Option<String>,
    recipe_quantity: i64,
    recipe_expanded: std::collections::HashSet<String>,
    /// Goods on the cursor (Unity CursorGoods): a stack's location, item, spoilage and how many units it points at.
    pub held: Option<HeldStack>,
    /// An action pressed once that asks for a second press (Unity's fire and sell-truck confirmations), as "verb|id".
    pub confirming: Option<String>,
    /// The order of each container's stacks as the player arranged them by dragging (Unity's slot arrangement), by location:
    /// (item, spoiled) keys; stacks not listed follow in their natural order.
    arrangement: std::collections::HashMap<String, Vec<(String, bool)>>,
    /// The truck chosen on the phone map: highlighted there and on the Trucks tab.
    pub selected_truck: Option<String>,
    /// Picking a task target in the world (Unity InteractionScreen.PickPosition): the screen is hidden meanwhile.
    pub picking: Option<TaskPick>,
}

#[derive(Clone, Debug, Default, PartialEq)]
struct RouteDraft { pickup: String, dropoff: String, item: Option<String> }

/// The draft for a route (its applied values to start) or for the new route (the first two docks).
fn route_draft<'a>(panels: &'a mut Panels, world: &Joined, key: &str) -> &'a mut RouteDraft {
    if !panels.route_drafts.contains_key(key) {
        let docks = company_docks(world);
        let draft = match world.view().and_then(|v| v.routes.iter().find(|r| r.id == key)) {
            Some(route) => RouteDraft { pickup: route.pickup_dock_id.clone(), dropoff: route.dropoff_dock_id.clone(), item: route.allowed_item_ids.first().cloned() },
            None => RouteDraft { pickup: docks.first().map(|d| d.0.clone()).unwrap_or_default(), dropoff: docks.get(1).map(|d| d.0.clone()).unwrap_or_default(), item: None },
        };
        panels.route_drafts.insert(key.to_string(), draft);
    }
    panels.route_drafts.get_mut(key).unwrap()
}

/// A goods stack on the cursor. It only points at lots; dropping it sends ordinary server-checked transfers.
#[derive(Clone, Debug, PartialEq)]
pub struct HeldStack { pub location: String, pub item: String, pub spoiled: bool, pub quantity: i32 }

/// Moves up to `quantity` units of one stack to another container, as many as its free slots take, oldest exposure first
/// (Unity EquipmentInteraction.TransferStack and MoveGoods). Returns the transfers to send, or none when nothing fits.
pub fn stack_transfers(view: &Snapshot, content: &food_factory_bevy::content::GameContent, source: &str, item: &str, spoiled: bool, quantity: i32, destination: &str) -> Vec<Command> {
    if source == destination { return vec![]; }
    let location = view.locations.iter().find(|l| l.id == destination);
    let mut free = rules::free_units(location, &view.lots, item, spoiled, |i| content.max_stack(i)).min(quantity.max(0) as i64);
    let mut lots: Vec<&Lot> = view.lots.iter().filter(|l| l.location_id == source && l.item_id == item && l.spoiled == spoiled && l.quantity > 0).collect();
    lots.sort_by(|a, b| b.exposure_seconds.cmp(&a.exposure_seconds).then(a.id.cmp(&b.id)));
    let mut commands = vec![];
    for lot in lots {
        if free <= 0 { break; }
        let take = free.min(lot.quantity as i64);
        free -= take;
        commands.push(Command::Transfer { lot: lot.id.clone(), destination: destination.to_string(), quantity: take as i32 });
    }
    commands
}

/// Which task field a world pick fills, for which employee and task.
#[derive(Clone, Debug, PartialEq)]
pub struct TaskPick {
    pub employee: String,
    pub index: usize,
    pub role: PickRole,
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub enum PickRole { Source, Destination, Machine }

impl PickRole {
    fn key(self) -> &'static str { match self { PickRole::Source => "src", PickRole::Destination => "dst", PickRole::Machine => "machine" } }
    pub fn noun(self) -> &'static str { match self { PickRole::Source => "source", PickRole::Destination => "destination", PickRole::Machine => "machine" } }
}

/// A button's label, or "Confirm <label>" while its first press waits for the second.
fn confirm_label(panels: &Panels, verb: &str, id: &str, label: &str) -> String {
    if panels.confirming.as_deref() == Some(format!("{verb}|{id}").as_str()) { format!("Confirm {}", label.to_lowercase()) } else { label.to_string() }
}

/// The machines a pick may choose: everything with goods for a source or destination, the same places for a machine.
pub fn pick_targets(world: &Joined, pick: &TaskPick) -> Vec<String> {
    task_places(world, &pick.employee).into_iter().filter(|p| p != "hands").collect()
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
    if let Some(pick) = panels.picking.clone() {
        if actions.close_pressed {
            panels.picking = None;
            panels.open = Some(Panel::Employee(pick.employee));
        }
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
    // The Map key opens the phone on its map, or turns an open phone to it (Unity PhonePanel.OpenMap).
    if actions.map_pressed && !typing.0 && matches!(panels.open, None | Some(Panel::Phone(_))) {
        panels.open = Some(Panel::Phone(PhoneTab::Map));
        let wants = true;
        if focus.0 != wants { focus.0 = wants; }
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
        panels.confirming = None;
        panels.held = None;
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


/// What a sale station's unspoiled input would sell for (Unity PlayerHud.SaleValueCents): each item's first sale recipe by
/// ID prices whole portions of it.
pub fn sale_value_cents(lots: &[&Lot], sales: &[&goods::RecipeDefinition]) -> i64 {
    let mut recipes: Vec<&&goods::RecipeDefinition> = sales.iter().filter(|r| r.is_sale() && !r.inputs.is_empty()).collect();
    recipes.sort_by(|a, b| a.id.cmp(&b.id));
    let mut prices: std::collections::HashMap<&str, (i64, i64)> = std::collections::HashMap::new();
    for recipe in recipes {
        let ingredient = &recipe.inputs[0];
        if !ingredient.item_id.is_empty() { prices.entry(ingredient.item_id.as_str()).or_insert((recipe.sale_cents, ingredient.quantity.max(1) as i64)); }
    }
    let mut totals: std::collections::BTreeMap<&str, i64> = std::collections::BTreeMap::new();
    for lot in lots.iter().filter(|l| !l.spoiled) { *totals.entry(lot.item_id.as_str()).or_default() += lot.quantity as i64; }
    totals.iter().filter_map(|(item, units)| prices.get(item).map(|(cents, per)| units / per * cents)).sum()
}

/// The fixed spot in front of a lot (Unity PlayerAvatar.TryLotFront): the middle of its building's street-facing edge, 1.5 m
/// out on the sidewalk, in map metres, with the yaw that looks back at the building.
pub fn lot_front(layout: &food_factory_bevy::world::layout::WorldLayout, lot_id: &str) -> Option<(f32, f32, f32)> {
    let lot = layout.lots.iter().find(|l| l.id == lot_id)?;
    let building = layout.buildings.iter().find(|b| b.id == lot.building_id)?;
    let (sx, sz) = food_factory_bevy::world::geometry::step(building.facing);
    let (sx, sz) = (sx as f32, sz as f32);
    let x = lot.x as f32 + lot.width as f32 * 0.5 + sx * (lot.width as f32 * 0.5 + 1.5);
    let z = lot.z as f32 + lot.depth as f32 * 0.5 + sz * (lot.depth as f32 * 0.5 + 1.5);
    Some((x, z, (-sx).atan2(-sz).to_degrees()))
}

/// Ledger rows shown on the phone (Unity PlayerHud.LedgerRowsShown).
const LEDGER_ROWS_SHOWN: usize = 20;

/// A ledger entry's "What": its kind and, for a sale, what was sold ("2 x Bread" for a single-item sale, else the menu name).
fn ledger_what(content: &Content, entry: &goods::LedgerEntry) -> String {
    let kind = crate::screens::ledger_kind(&entry.kind);
    if entry.kind != company::LEDGER_SALE || entry.offer_id.is_empty() { return kind; }
    let Some(recipe) = content.0.recipes.get(&entry.offer_id) else { return kind };
    let sold = match recipe.inputs.as_slice() {
        [single] if !single.item_id.is_empty() => format!("{} x {}", single.quantity.max(1), content.0.item_name(&single.item_id)),
        _ => content.0.recipe_name(&recipe.id),
    };
    if sold.is_empty() { kind } else { format!("{kind}: {sold}") }
}

/// A truck's live status (Unity LogisticsPanel.TruckStatus), with the time left estimated at the current traffic on the road.
fn truck_status(world: &Joined, view: &Snapshot, content: &Content, truck: &goods::Truck) -> String {
    let _ = content;
    let name = |id: &str| view.sites.iter().find(|s| s.id == id).map_or(id.to_string(), |s| s.name.clone());
    let (here, there) = (name(&truck.site_id), name(&truck.destination_site_id));
    let left = match goods::trucks::estimate_road_seconds(view, &world.roads, truck) {
        Some(seconds) => format!("about {}{}", crate::screens::format_duration(seconds, false), if truck.remaining_seconds == 0 { " (in traffic)" } else { "" }),
        None => crate::screens::format_duration(truck.remaining_seconds, false),
    };
    let order = !truck.order_id.is_empty();
    let dock_note = || {
        let Some(dock) = goods::trucks::working_dock_of(view, truck) else { return String::new() };
        let placed = view.equipment.iter().any(|e| e.id == dock && e.placed());
        if !placed { return " (dock not placed)".into(); }
        let others = view.trucks.iter().any(|x| x.id != truck.id && x.docked && matches!(x.state, TruckState::Loading | TruckState::Unloading)
            && goods::trucks::working_dock_of(view, x).as_deref() == Some(dock.as_str()));
        if !truck.docked && others { " (waiting for the dock)".into() } else { String::new() }
    };
    match truck.state {
        TruckState::Parked => format!("Parked at {here}: no route"),
        TruckState::ToPickup if order => format!("Driving to {there} for a farm order: {left}"),
        TruckState::ToDropoff if order => format!("Bringing a farm order to {there}: {left}"),
        TruckState::ToPickup => format!("Driving to {there} to load: {left}"),
        TruckState::ToDropoff => format!("Driving to {there} with cargo: {left}"),
        TruckState::ToPark => format!("Driving to {there} to park: {left}"),
        TruckState::Loading if order => format!("Loading a farm order at {here}"),
        TruckState::Loading => format!("Loading at {here}{}", dock_note()),
        TruckState::Unloading => format!("Unloading at {here}{}", dock_note()),
    }
}

/// Where a truck is headed, or where its work sends it next (Unity LogisticsPanel.DestinationText).
fn truck_destination(view: &Snapshot, truck: &goods::Truck, site_name: &dyn Fn(&str) -> String, dock_site: &dyn Fn(&str) -> String) -> String {
    if truck.driving() { return format!("Destination: {}", site_name(&truck.destination_site_id)); }
    let order = view.farm_orders.iter().find(|o| !truck.order_id.is_empty() && o.id == truck.order_id);
    let route = view.routes.iter().find(|r| !truck.route_id.is_empty() && r.id == truck.route_id);
    match (truck.state, order, route) {
        (TruckState::Loading, Some(order), _) => format!("Next: {}, after loading", site_name(&order.site_id)),
        (TruckState::Unloading, Some(_), _) => "Next: parks here once unloaded".into(),
        (TruckState::Loading, None, Some(route)) => format!("Next: {}, after loading", site_name(&dock_site(&route.dropoff_dock_id))),
        (TruckState::Unloading, None, Some(route)) => format!("Next: back to {}, once empty", site_name(&dock_site(&route.pickup_dock_id))),
        _ => "Destination: none".into(),
    }
}

/// The route a truck repeats, the farm order it serves, or none (Unity LogisticsPanel.RouteText).
fn truck_route(view: &Snapshot, content: &Content, truck: &goods::Truck, route_name: &dyn Fn(&goods::Route) -> String, site_name: &dyn Fn(&str) -> String) -> String {
    if let Some(order) = view.farm_orders.iter().find(|o| !truck.order_id.is_empty() && o.id == truck.order_id) {
        let farm = view.sites.iter().find(|s| s.id.ends_with(order.farm_lot_id.trim_start_matches("lot-"))).map_or(order.farm_lot_id.clone(), |s| s.name.clone());
        return format!("Farm order: {} {} from {farm} to {}", order.units, content.0.item_name(&order.item_id), site_name(&order.site_id));
    }
    match view.routes.iter().find(|r| !truck.route_id.is_empty() && r.id == truck.route_id) {
        Some(route) => format!("Route: {}", route_name(route)),
        None => "Route: none (parked)".into(),
    }
}

/// What a truck carries (Unity LogisticsPanel.CargoText).
fn truck_cargo(view: &Snapshot, content: &Content, truck: &goods::Truck) -> String {
    let cargo = truck.cargo_location_id();
    let lots = lots_in(view, &cargo);
    let used = rules::slots_used(lots.iter().copied(), |i| content.0.max_stack(i));
    let mut goods: Vec<(String, bool, i32)> = vec![];
    for lot in &lots {
        match goods.iter_mut().find(|g| g.0 == lot.item_id && g.1 == lot.spoiled) { Some(g) => g.2 += lot.quantity, None => goods.push((lot.item_id.clone(), lot.spoiled, lot.quantity)) }
    }
    goods.sort();
    let text = if goods.is_empty() { "empty".to_string() } else {
        goods.iter().map(|(i, s, q)| format!("{q} {}{}", content.0.item_name(i), if *s { " (spoiled)" } else { "" })).collect::<Vec<_>>().join(", ")
    };
    format!("Cargo {used}/{} slots: {text}", truck.cargo_slots)
}

/// Moves a stack key in a container's arrangement to just before another, or to the end.
fn arrange(order: &mut Vec<(String, bool)>, key: (String, bool), before: Option<(String, bool)>) {
    order.retain(|k| *k != key);
    match before.and_then(|b| order.iter().position(|k| *k == b)) {
        Some(at) => order.insert(at, key),
        None => order.push(key),
    }
}

/// A container as Unity's slot grid: each (item, spoilage) stack split into slots of up to its max stack, in the order its
/// stacks first appear, then the empty slots up to capacity. A filled slot picks its stack up (and is highlighted while that
/// stack is on the cursor); an empty one drops the held stack into the container.
fn grid(panels: &Panels, content: &Content, view: &Snapshot, location: &str, capacity: i32) -> Vec<UiEntry> {
    let lots = lots_in(view, location);
    let mut stacks: Vec<(&str, bool, &str, i64)> = vec![];
    for lot in &lots {
        match stacks.iter_mut().find(|s| s.0 == lot.item_id && s.1 == lot.spoiled) {
            Some(stack) => stack.3 += lot.quantity as i64,
            None => stacks.push((lot.item_id.as_str(), lot.spoiled, lot.id.as_str(), lot.quantity as i64)),
        }
    }
    if let Some(order) = panels.arrangement.get(location) {
        let rank = |item: &str, spoiled: bool| order.iter().position(|k| k.0 == item && k.1 == spoiled).unwrap_or(usize::MAX);
        stacks.sort_by_key(|s| rank(s.0, s.1));
    }
    let mut slots = vec![];
    for (item, spoiled, first_lot, total) in stacks {
        let max = content.0.max_stack(item).max(1) as i64;
        let definition = content.0.items.get(item);
        let name = definition.map_or(item.to_string(), |i| i.display_name.clone());
        let holding = panels.held.as_ref().is_some_and(|h| h.location == location && h.item == item && h.spoiled == spoiled);
        let pieces = (total + max - 1) / max;
        for k in 0..pieces {
            let count = max.min(total - k * max);
            let mut entry = UiEntry::button(format!("p:slot|{location}|{first_lot}"), format!("{name}\n{count}{}", if spoiled { " spoiled" } else { "" }))
                .with_icon(definition.map(|i| i.icon.clone()).unwrap_or_default()).as_slot().selected(holding);
            entry.id = format!("slot-{location}-{item}-{spoiled}-{k}");
            slots.push(entry);
        }
    }
    let used = slots.len() as i32;
    for index in used..capacity.max(used) {
        let mut entry = UiEntry::button(format!("p:drop|{location}"), "").as_slot();
        entry.id = format!("slot-{location}-empty-{index}");
        slots.push(entry);
    }
    slots
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
#[allow(clippy::too_many_arguments)]
fn handle(
    time: Res<Time>, content: Res<Content>, mut intents: ResMut<UiIntents>, mut panels: ResMut<Panels>, mut session: ResMut<Session>, mut fields: ResMut<UiFields>, mut cursor: ResMut<Cursor>,
    space: Option<Res<Space>>, mut players: Query<&mut Transform, With<crate::player::Player>>, keys: Res<PlayerActions>,
) {
    let drags: Vec<String> = intents.0.iter().filter(|a| a.starts_with("drag>")).cloned().collect();
    intents.0.retain(|a| !a.starts_with("drag>"));
    for drag in drags {
        let Some(world) = session.joined() else { break };
        let mut pieces = drag.split('>').skip(1);
        let (Some(source), Some(target)) = (pieces.next(), pieces.next()) else { continue };
        let source: Vec<&str> = source.trim_start_matches("p:").split('|').collect();
        let target: Vec<&str> = target.trim_start_matches("p:").split('|').collect();
        let (["slot", from, lot_id, ..], [verb @ ("slot" | "drop"), to, rest @ ..]) = (source.as_slice(), target.as_slice()) else { continue };
        let Some((view, lot)) = world.views.values().find_map(|v| v.lots.iter().find(|l| l.id == *lot_id).map(|l| (v, l))) else { continue };
        let key = (lot.item_id.clone(), lot.spoiled);
        // Where it lands: before the stack dropped on, or last on an empty slot.
        let before = if *verb == "slot" { rest.first().and_then(|t| view.lots.iter().find(|l| l.id == *t)).map(|l| (l.item_id.clone(), l.spoiled)) } else { None };
        // Start from the order shown now, so stacks the player never moved keep their places.
        let mut order: Vec<(String, bool)> = vec![];
        for l in lots_in(view, to) { let k = (l.item_id.clone(), l.spoiled); if !order.contains(&k) { order.push(k); } }
        if let Some(saved) = panels.arrangement.get(*to) {
            order.sort_by_key(|k| saved.iter().position(|s| s == k).unwrap_or(usize::MAX));
        }
        arrange(&mut order, key.clone(), before);
        let to_owned = to.to_string();
        if from != to {
            if to.ends_with(":out") { panels.problem = "An output only gives; put goods into the input.".into(); continue; }
            let total: i32 = view.lots.iter().filter(|l| l.location_id == *from && l.item_id == key.0 && l.spoiled == key.1).map(|l| l.quantity).sum();
            let commands = stack_transfers(view, &content.0, from, &key.0, key.1, total, to);
            if commands.is_empty() { panels.problem = "There is no room for that there.".into(); continue; }
            for command in commands { session.command(command, time.elapsed_secs_f64()); }
        }
        panels.arrangement.insert(to_owned, order);

    }
    let actions: Vec<String> = intents.0.iter().filter(|a| a.starts_with("p:")).cloned().collect();
    intents.0.retain(|a| !a.starts_with("p:"));
    let now = time.elapsed_secs_f64();
    for action in actions {
        let parts: Vec<&str> = action["p:".len()..].splitn(4, '|').collect();
        let Some(world) = session.joined() else { return };
        let site = world.site.clone();
        let inventory = world.inventory();
        if !matches!(parts.first(), Some(&"fire") | Some(&"sell-truck")) && panels.confirming.take().is_some() {
            panels.problem.clear();
        }
        let command = match parts.as_slice() {
            ["recipe-open", id] => {
                panels.recipe = Some(id.to_string()); panels.recipe_quantity = 1; panels.recipe_expanded.clear();
                fields.0.insert("recipe-quantity".into(), "1".into()); None
            }
            ["recipe-back"] => { panels.recipe = None; None }
            ["recipe-quantity", step] => {
                let typed = fields.0.get("recipe-quantity").and_then(|s| s.parse::<i64>().ok()).unwrap_or(panels.recipe_quantity);
                panels.recipe_quantity = typed.saturating_add(step.parse::<i64>().unwrap_or(0)).clamp(1, 9999);
                fields.0.insert("recipe-quantity".into(), panels.recipe_quantity.to_string()); None
            }
            ["recipe-expand", path] => {
                if !panels.recipe_expanded.remove(*path) { panels.recipe_expanded.insert(path.to_string()); } None
            }
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
            // Unity PlayerHud.ClickSlot / ClickSlotHalf / QuickTransferSlot over the open screen's containers.
            ["slot", location, lot_id] | ["slot", location, lot_id, "alt" | "shift"] => {
                let half = parts.get(3) == Some(&"alt");
                let shift = keys.quick_transfer || parts.get(3) == Some(&"shift");
                let Some((view, lot)) = world.views.values().find_map(|v| v.lots.iter().find(|l| l.id == *lot_id).map(|l| (v, l))) else { continue };
                let total: i32 = view.lots.iter().filter(|l| l.location_id == *location && l.item_id == lot.item_id && l.spoiled == lot.spoiled).map(|l| l.quantity).sum();
                let output = location.ends_with(":out");
                if shift && !half {
                    let target = match panels.open.as_ref() {
                        Some(Panel::Machine(machine)) if *location == inventory => view.equipment.iter().find(|e| &e.id == machine).filter(|e| e.input_capacity > 0).map(|e| e.input_location_id()),
                        Some(Panel::Machine(_)) => Some(inventory.clone()),
                        _ => None,
                    };
                    if let Some(target) = target {
                        let commands = stack_transfers(view, &content.0, location, &lot.item_id, lot.spoiled, total, &target);
                        if commands.is_empty() { panels.problem = "There is no room for that there.".into(); }
                        for command in commands { session.command(command, now); }
                    }
                    None
                } else if panels.held.is_none() {
                    let quantity = if half { (total + 1) / 2 } else { total };
                    panels.held = Some(HeldStack { location: location.to_string(), item: lot.item_id.clone(), spoiled: lot.spoiled, quantity });
                    None
                } else if half {
                    None
                } else if panels.held.as_ref().is_some_and(|h| h.location == *location) || output {
                    // Its own container takes it back; an output only gives, so the stack stays on the cursor there.
                    if !output { panels.held = None; }
                    None
                } else {
                    let held = panels.held.take().unwrap();
                    let commands = stack_transfers(view, &content.0, &held.location, &held.item, held.spoiled, held.quantity, location);
                    if commands.is_empty() { panels.problem = "There is no room for that there.".into(); }
                    for command in commands { session.command(command, now); }
                    None
                }
            }
            ["drop", location] if location.ends_with(":out") => {
                if panels.held.is_some() { panels.problem = "An output only gives; put goods into the input.".into(); }
                None
            }
            ["drop", location] => {
                if let (Some(held), Some(view)) = (panels.held.take(), world.view_of(&world.site)) {
                    let commands = stack_transfers(view, &content.0, &held.location, &held.item, held.spoiled, held.quantity, location);
                    if commands.is_empty() { panels.problem = "There is no room for that there.".into(); }
                    for command in commands { session.command(command, now); }
                }
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
            // The first press asks for a second; the second asks the server (Unity EmployeeScriptPanel.ClickFire).
            ["fire", employee] => {
                let key = format!("fire|{employee}");
                if panels.confirming.as_deref() == Some(key.as_str()) {
                    panels.confirming = None;
                    Some(Command::Fire { employee: employee.to_string() })
                } else {
                    let name = world.employees.iter().find(|e| e.id == *employee).map_or(employee.to_string(), |e| e.name.clone());
                    panels.problem = format!("Fire {name}? Press again to confirm.");
                    panels.confirming = Some(key);
                    None
                }
            }
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
            ["task-pick", role, index] => {
                if let (Some(Panel::Employee(employee)), Ok(index)) = (panels.open.clone(), index.parse::<usize>()) {
                    let role = match *role { "src" => PickRole::Source, "dst" => PickRole::Destination, _ => PickRole::Machine };
                    panels.picking = Some(TaskPick { employee, index, role });
                    panels.open = None;
                }
                None
            }
            ["task-set", role, index, place] => {
                if let (Some(pick), Ok(index)) = (panels.picking.clone(), index.parse::<usize>()) {
                    let draft = draft_of(&mut panels, world, &pick.employee);
                    if let Some(task) = draft.tasks.get_mut(index) {
                        match *role { "src" => task.source = place.to_string(), "dst" => task.destination = place.to_string(), _ => task.machine = place.to_string() }
                    }
                    panels.picking = None;
                    panels.open = Some(Panel::Employee(pick.employee));
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
            ["dry-run", employee] => {
                let source = fields.0.get(&format!("script-{employee}")).cloned().or_else(|| world.details.get(*employee).map(|d| d.source.clone())).unwrap_or_default();
                panels.problem = food_factory_bevy::employees::dry_run(&source).unwrap_or_else(|error| error);
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
            // Unity LogisticsPanel.CycleDraft: pickup and dropoff step through the company's docks, cargo through any item or one.
            ["draft", key, field, step] => {
                let step: i64 = step.parse().unwrap_or(1);
                let docks: Vec<String> = company_docks(world).into_iter().map(|d| d.0).collect();
                let items: Vec<Option<String>> = std::iter::once(None).chain(content.0.items.keys().cloned().map(Some)).collect();
                let draft = route_draft(&mut panels, world, key);
                let wrap = |index: i64, count: usize| (index.rem_euclid(count.max(1) as i64)) as usize;
                match *field {
                    "cargo" => { let at = items.iter().position(|i| *i == draft.item).unwrap_or(0) as i64; draft.item = items[wrap(at + step, items.len())].clone(); }
                    "pickup" | "dropoff" if !docks.is_empty() => {
                        let current = if *field == "pickup" { &draft.pickup } else { &draft.dropoff };
                        let at = docks.iter().position(|d| d == current).map_or(-1, |i| i as i64);
                        let next = docks[wrap(at + step, docks.len())].clone();
                        if *field == "pickup" { draft.pickup = next } else { draft.dropoff = next }
                    }
                    _ => {}
                }
                None
            }
            ["create-route"] => {
                let draft = route_draft(&mut panels, world, "new").clone();
                if draft.pickup.is_empty() || draft.dropoff.is_empty() || draft.pickup == draft.dropoff {
                    panels.problem = "A route needs two different docks.".into();
                    None
                } else {
                    panels.route_drafts.remove("new");
                    Some(Command::CreateRoute { pickup: draft.pickup, dropoff: draft.dropoff, items: draft.item.into_iter().collect() })
                }
            }
            ["apply-route", route] => {
                let draft = route_draft(&mut panels, world, route).clone();
                Some(Command::SetRoute { route: route.to_string(), pickup: draft.pickup, dropoff: draft.dropoff, items: draft.item.into_iter().collect() })
            }
            // Unity LogisticsPanel.MoveStack: up to one max stack between a remote site's storage and its dock.
            ["move-stack", from, to, spec] => {
                let (item, spoiled) = spec.rsplit_once('#').map_or((*spec, false), |(i, s)| (i, s == "1"));
                if let Some(view) = world.views.values().find(|v| v.locations.iter().any(|l| l.id == *from)) {
                    let commands = stack_transfers(view, &content.0, from, item, spoiled, content.0.max_stack(item), to);
                    if commands.is_empty() { panels.problem = "There is no room for that there.".into(); }
                    for command in commands { session.command(command, now); }
                }
                None
            }
            ["delete-route", route] => Some(Command::DeleteRoute { route: route.to_string() }),
            ["assign", truck, route] => Some(Command::AssignTruck { truck: truck.to_string(), route: route.to_string() }),
            // Unity LogisticsPanel.AskSellTruck / ConfirmSellTruck: what the sale pays and what it cancels, then a second press.
            ["sell-truck", truck] => {
                let key = format!("sell-truck|{truck}");
                if panels.confirming.as_deref() == Some(key.as_str()) {
                    panels.confirming = None;
                    Some(Command::SellTruck { truck: truck.to_string() })
                } else {
                    if let Some(record) = world.view().and_then(|v| v.trucks.iter().find(|t| t.id == *truck)) {
                        let offers = content.0.truck_offers();
                        let price = match goods::trucks::truck_offer_of(record, &offers) {
                            Some(offer) => format!("{}% of its {} list price", goods::trucks::TRUCK_RESALE_PERCENT, money(offer.price_cents)),
                            None => "no listed price, so it sells for nothing".to_string(),
                        };
                        let schedules = world.view().map_or(0, |v| v.farm_schedules.iter().filter(|f| f.truck_id == *truck).count());
                        let cancelled = if schedules == 0 { String::new() } else { format!(" {schedules} repeating farm order{} picked up by it will be cancelled.", if schedules == 1 { "" } else { "s" }) };
                        let route = if record.route_id.is_empty() { "" } else { " Its route stays for the company's other trucks." };
                        panels.problem = format!("Sell {} for {} ({price})? It leaves at once.{route}{cancelled} Press Sell again to confirm.",
                            record.name, money(goods::trucks::truck_resale_cents(record, &offers)));
                    }
                    panels.confirming = Some(key);
                    None
                }
            }
            ["packs", delta] => {
                panels.farm_packs = (panels.farm_packs.max(1) + delta.parse::<i32>().unwrap_or(0)).clamp(1, farms::MAX_FARM_ORDER_PACKS);
                None
            }
            // Unity PlayerAvatar.TeleportToLot (the phone map's Teleport for the company's own places).
            ["teleport", lot] => {
                if let (Some(space), Ok(mut transform), Some((x, z, yaw))) = (space.as_ref(), players.single_mut(), lot_front(&world.layout, lot)) {
                    transform.translation = space.to_scene(x, z, space.land(x, z)) + Vec3::Y * 0.2;
                    transform.rotation = crate::space::yaw_rotation(yaw);
                    panels.open = None;
                }
                None
            }
            // Opens a machine's screen without aiming at it (capture scripts; the hover path opens it in play).
            ["open-machine", id] => { panels.open = Some(Panel::Machine(id.to_string())); None }
            ["select-truck", truck] => {
                panels.selected_truck = Some(truck.to_string());
                panels.open = Some(Panel::Phone(PhoneTab::Trucks));
                None
            }
            ["farm-dock"] => { panels.farm_dock += 1; None }
            // Delivery, or pickup by each parked company truck in turn.
            ["farm-truck"] => {
                let parked: Vec<String> = world.view().map(|v| v.trucks.iter().filter(|t| world.company().is_some_and(|c| c.id == t.company_id) && !t.supplier && t.state == TruckState::Parked).map(|t| t.id.clone()).collect()).unwrap_or_default();
                let at = panels.farm_truck.as_ref().and_then(|t| parked.iter().position(|p| p == t));
                panels.farm_truck = match at { None => parked.first().cloned(), Some(i) => parked.get(i + 1).cloned() };
                None
            }
            ["farm-repeat"] => { panels.farm_repeat = if panels.farm_repeat.is_some() { None } else { Some(1) }; None }
            ["farm-every", delta] => {
                if let Some(hours) = panels.farm_repeat.as_mut() { *hours = (*hours + delta.parse::<i32>().unwrap_or(0)).clamp(1, 24); }
                None
            }
            ["farm-order", farm, offer, dock] => {
                let (farm, offer, dock, packs) = (farm.to_string(), offer.to_string(), dock.to_string(), panels.farm_packs.max(1));
                let truck = panels.farm_truck.clone().unwrap_or_default();
                Some(match panels.farm_repeat {
                    Some(every_hours) => Command::ScheduleFarmOrder { farm, offer, packs, dock, truck, every_hours },
                    None => Command::OrderFromFarm { farm, offer, packs, dock, truck },
                })
            }
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
fn draw(panels: Res<Panels>, session: Res<Session>, content: Res<Content>, mut model: ResMut<UiModel>, space: Option<Res<Space>>, players: Query<&Transform, With<crate::player::Player>>, screen: Res<Screen>, phone_map: Res<crate::phone_map::PhoneMap>,
    indoors: Res<crate::sites::Indoors>) {
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
            next.entries.push(UiEntry::row("inventory-slots", grid(&panels, &content, view, &inventory, capacity)));
            let held: Vec<&goods::Equipment> = view.equipment.iter().filter(|e| e.state == EquipmentState::Held && e.holder_id == world.player).collect();
            next.entries.push(UiEntry::label("held", if held.is_empty() { "No machines held. Buy them on the phone (C).".to_string() } else { "Held machines (pick one to place it; hotbar keys 1-9 do the same)".to_string() }));
            next.entries.push(UiEntry::row("held-slots", held.iter().map(|e| {
                let kind = content.0.equipment.get(&e.kind);
                UiEntry::button(format!("p:hold|{}", e.id), kind.map_or(e.kind.clone(), |k| k.display_name.clone())).with_icon(kind.map(|k| k.icon.clone()).unwrap_or_default())
            }).collect()));
            // Unity PlayerHud.ConstructionWindow: inside a factory, its floors and the price of the next.
            if let Some(building) = indoors.building.as_ref().and_then(|id| view.buildings.iter().find(|b| &b.id == id)).filter(|b| b.kind == goods::FACTORY_KIND) {
                let max = food_factory_bevy::content::FLOOR_OFFER.max_floors;
                next.entries.push(UiEntry::label("floors", format!("CONSTRUCTION  Floors: {} of {max}", building.floors)));
                if building.floors >= max {
                    next.entries.push(UiEntry::label("top-floor", "This factory has as many floors as it can."));
                } else {
                    let price = goods::buildings::floor_price_cents(building, Some(&food_factory_bevy::content::FLOOR_OFFER));
                    next.entries.push(UiEntry::button("b:add-floor", format!("Add a floor, {}", money(price))));
                    if !building.has_elevator() { next.entries.push(UiEntry::label("elevator-note", "The first extra floor brings an elevator; stand on it and press PageUp or PageDown.")); }
                }
            }
            // Unity PlayerHud's inventory screen also carries the Supplier (packs and machines; trucks are on the logistics
            // screen, furnishings in build mode) and this place's Staff summary.
            next.entries.push(UiEntry::label("supplier", "SUPPLIER"));
            let offers: Vec<UiEntry> = content.0.offers.iter().filter_map(|offer| {
                let (label, icon) = match &offer.kind {
                    OfferKind::Goods(g) => (format!("{} x{}
{}", content.0.item_name(&g.item_id), g.quantity, money(offer.price_cents)), content.0.items.get(&g.item_id).map(|i| i.icon.clone()).unwrap_or_default()),
                    OfferKind::Equipment(e) if content.0.equipment.get(&e.equipment.kind).is_none_or(|k| k.category.is_empty()) => (format!("{}
{}", content.0.equipment_name(&e.equipment.kind), money(offer.price_cents)), content.0.equipment.get(&e.equipment.kind).map(|k| k.icon.clone()).unwrap_or_default()),
                    _ => return None,
                };
                Some(UiEntry::button(format!("p:buy|{}", offer.id), label).with_icon(icon))
            }).collect();
            next.entries.push(UiEntry::row("supplier-offers", offers));
            if let Some(company) = world.company() {
                let count = world.employees.iter().filter(|e| e.site == world.site).count();
                let cap = goods::employees::employee_cap(view, &world.site);
                let wages = view.company_wage_cents_per_hour;
                let covers = if wages > 0 { format!("{} h", company.cash / wages) } else { "-".to_string() };
                next.entries.push(UiEntry::label("staff-summary", format!("STAFF  Employees: {count} of {cap}, {} per game hour each. Wages {} / h; cash covers {covers}.",
                    money(goods::employees::WAGE_CENTS_PER_HOUR), money(wages))));
                if (count as i32) < cap { next.entries.push(UiEntry::button("p:hire", format!("Hire, {} / h", money(goods::employees::WAGE_CENTS_PER_HOUR)))); }
            }
        }
        Panel::Machine(id) => {
            let Some((site_view, piece)) = view_of_equipment(world, id) else { return };
            let kind = content.0.equipment.get(&piece.kind);
            next.wide = true;
            next.heading = content.0.equipment_name(&piece.kind);
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
            // Unity PlayerHud: a kind without recipes is storage (its grid alone); a sale station shows what it will sell and
            // its value, without an output; other machines show input and output.
            let kind_recipes: Vec<&goods::RecipeDefinition> = content.0.recipes.values().filter(|r| r.station_kind == piece.kind).collect();
            let storage = kind_recipes.is_empty();
            let sales: Vec<&goods::RecipeDefinition> = kind_recipes.iter().copied().filter(|r| r.is_sale()).collect();
            // A table's screen is its seats and standing only (Unity PlayerHud.TableWindow).
            let table = goods::is_table(piece);
            if piece.input_capacity > 0 && !table {
                let lots = lots_in(site_view, &input);
                let used = rules::slots_used(lots.iter().copied(), |i| content.0.max_stack(i));
                let dock = piece.kind == goods::DOCK_KIND;
                let caption = if dock { "Outgoing: trucks load" } else if storage { if piece.input_refrigerated { "Refrigerated storage" } else { "Storage" } } else if !sales.is_empty() { "To sell" } else { "Input" };
                next.entries.push(UiEntry::label("input", format!("{caption} ({used}/{} slots)", piece.input_capacity)));
                next.entries.push(UiEntry::row("input-slots", grid(&panels, &content, site_view, &input, piece.input_capacity)));
                if !sales.is_empty() {
                    next.entries.push(UiEntry::label("sale-value", format!("Sale value {}", money(sale_value_cents(&lots, &sales)))));
                }
            }
            if piece.output_capacity > 0 && !table && (piece.kind == goods::DOCK_KIND || (!storage && sales.is_empty())) {
                let used = rules::slots_used(lots_in(site_view, &output), |i| content.0.max_stack(i));
                let caption = if piece.kind == goods::DOCK_KIND { "Incoming: trucks unload" } else { "Output" };
                next.entries.push(UiEntry::label("output", format!("{caption} ({used}/{} slots)", piece.output_capacity)));
                next.entries.push(UiEntry::row("output-slots", grid(&panels, &content, site_view, &output, piece.output_capacity)));
            }
            if piece.input_capacity > 0 && !table && piece.site_id == world.site {
                next.entries.push(UiEntry::label("carried", "Your goods (click picks a stack up, right click half, Shift+click moves it across)"));
                let capacity = view.locations.iter().find(|l| l.id == inventory).map_or(0, |l| l.capacity);
                next.entries.push(UiEntry::row("inventory-slots", grid(&panels, &content, view, &inventory, capacity)));
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
            // Unity PlayerHud.DockWindow: who serves the dock, and whether a restaurant dock stands by a back door.
            if piece.kind == goods::DOCK_KIND {
                let mut lines: Vec<String> = site_view.trucks.iter().filter_map(|t| {
                    let route = site_view.routes.iter().find(|r| !t.route_id.is_empty() && r.id == t.route_id)?;
                    if route.pickup_dock_id == piece.id { Some(format!("{}: picks up here", t.name)) } else if route.dropoff_dock_id == piece.id { Some(format!("{}: delivers here", t.name)) } else { None }
                }).collect();
                lines.extend(site_view.farm_orders.iter().filter(|o| o.dock_id == piece.id).map(|o| format!("Farm order: {} {} on the way ({} unloaded)", o.units, content.0.item_name(&o.item_id), o.unloaded_units)));
                next.entries.push(UiEntry::label("dock-trucks", if lines.is_empty() { "No truck serves this dock; press L to set up a route, or C to order from a farm.".to_string() } else { lines.join("\n") }));
                if rules::is_restaurant_site(site_view, &piece.site_id) {
                    if let Some(building) = site_view.buildings.iter().find(|b| b.site_id == piece.site_id && b.kind == goods::RESTAURANT_KIND) {
                        if !rules::beside_back_door_of(site_view, building, piece) { next.entries.push(UiEntry::label("dock-back-door", "This dock is not beside a back door.")); }
                    }
                }
            }
            if goods::is_table(piece) {
                // Unity PlayerHud.TableWindow: seats taken and how the restaurant stands with its customers.
                let seated = site_view.customers.iter().filter(|c| c.table_id == piece.id).count();
                next.entries.push(UiEntry::label("seats", format!("Seats taken: {seated}/{}", piece.seats)));
                next.entries.push(UiEntry::label("standing", match site_view.diners.first() {
                    Some(d) => format!("Served {}, walked out {}, reputation {:+}", d.served, d.walked_out, d.reputation),
                    None => "No customers yet.".to_string(),
                }));
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
                next.entries.push(UiEntry::label("script-label", "Luau: move_to(place), take(place, item, n), put(place), work(machine), wait(s), say(text). Dry run uses stand-ins, not real stock or paths."));
                let source = detail.map(|d| d.source.clone()).or_else(|| record.map(|r| r.script.clone())).unwrap_or_default();
                next.entries.push(UiEntry::area(format!("script-{id}"), source));
                next.entries.push(UiEntry::row("script-actions", vec![UiEntry::button(format!("p:dry-run|{id}"), "Dry run"), UiEntry::button(format!("p:run|{id}"), "Run"), UiEntry::button(format!("p:stop|{id}"), "Stop"), UiEntry::button(format!("p:fire|{id}"), confirm_label(&panels, "fire", id, "Fire"))]));
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
                            row.push(UiEntry::button(format!("p:task-pick|{}|{index}", PickRole::Source.key()), "Pick"));
                            row.push(UiEntry::button(format!("p:task-dst|{index}"), format!("To {}", name_of(&task.destination))));
                            row.push(UiEntry::button(format!("p:task-pick|{}|{index}", PickRole::Destination.key()), "Pick"));
                            row.push(UiEntry::button(format!("p:task-any|{index}"), if task.any_item { "Any item" } else { "Only" }));
                            if !task.any_item {
                                row.push(UiEntry::button(format!("p:task-item|{index}"), task.items.first().map_or("(item)".to_string(), |i| content.0.item_name(i))));
                            }
                        }
                        EmployeeTaskType::Power => {
                            row.push(UiEntry::button(format!("p:task-machine|{index}"), name_of(&task.machine)));
                            row.push(UiEntry::button(format!("p:task-pick|{}|{index}", PickRole::Machine.key()), "Pick"));
                            row.push(UiEntry::button(format!("p:task-power|{index}"), format!("{:?}", task.power)));
                        }
                        EmployeeTaskType::Work | EmployeeTaskType::Recipe => {
                            row.push(UiEntry::button(format!("p:task-machine|{index}"), name_of(&task.machine)));
                            row.push(UiEntry::button(format!("p:task-pick|{}|{index}", PickRole::Machine.key()), "Pick"));
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
                next.entries.push(UiEntry::row("task-actions", vec![UiEntry::button(format!("p:task-run|{id}"), "Run tasks"), UiEntry::button(format!("p:stop|{id}"), "Stop"), UiEntry::button(format!("p:fire|{id}"), confirm_label(&panels, "fire", id, "Fire"))]));
            }
        }
        Panel::Property(lot) => {
            let offer = world.offers_by_lot.get(lot);
            next.heading = offer.map_or("Property".to_string(), |o| format!("{} lot", o.category));
            let owner = view.properties.iter().find(|p| &p.lot_id == lot).map(|p| p.company_id.clone());
            let competitor = view.competitors.iter().find(|c| &c.lot_id == lot);
            // Unity PropertyPanel: owned by this company or another, else for sale or not; a competitor's name when known.
            let state = match (&owner, offer) {
                (Some(owner), _) if world.company().is_some_and(|c| &c.id == owner) => "Owned by your company".to_string(),
                (Some(_), _) => "Owned by another company".to_string(),
                (None, Some(o)) if o.for_sale => format!("For sale: {}", money(o.price_cents)),
                _ => "Not for sale".to_string(),
            };
            next.status = match competitor { Some(c) => format!("{} ({}). {state}", c.name, c.cuisine), None => state };
            let reason = match (&owner, offer) {
                (Some(owner), _) if world.company().is_some_and(|c| &c.id == owner) => Some("Your company already owns this place."),
                (Some(_), _) => Some("Owned by a competitor: another company bought it."),
                (None, Some(o)) if !o.for_sale => Some("Not for sale: this building is never sold."),
                _ => None,
            };
            if let Some(reason) = reason { next.entries.push(UiEntry::label("why", reason)); }
            // The company's own places: rename and Teleport (Unity PhonePanel's lot card).
            let ours = owner.as_ref().is_some_and(|o| world.company().is_some_and(|c| &c.id == o));
            if let (true, Some(o)) = (ours, offer) {
                let name = world.views.values().find_map(|v| v.sites.iter().find(|s| s.id == o.site_id)).map_or(o.site_id.clone(), |s| s.name.clone());
                next.entries.push(UiEntry::row("lot-rename", vec![UiEntry::field(format!("site-name-{}", o.site_id), name), UiEntry::button(format!("p:rename-site|{}", o.site_id), "Rename")]));
                next.entries.push(UiEntry::button(format!("p:teleport|{lot}"), "Teleport"));
            }
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
    next.footer = if !panels.problem.is_empty() {
        panels.problem.clone()
    } else if let Some(held) = &panels.held {
        format!("Holding {} {}{}: click a slot in another container to put it there; its own container takes it back.",
            held.quantity, content.0.item_name(&held.item), if held.spoiled { " (spoiled)" } else { "" })
    } else {
        "Esc closes".into()
    };
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
        PhoneTab::Recipes => {
            if let Some(recipe) = panels.recipe.as_ref().and_then(|id| content.0.recipes.get(id)) {
                let quantity = panels.recipe_quantity.clamp(1, 9999);
                next.entries.push(UiEntry::button("p:recipe-back", "Back to recipes"));
                next.entries.push(UiEntry::label("recipe-title", content.0.recipe_name(&recipe.id)));
                next.entries.push(UiEntry::row("recipe-quantity-controls", vec![UiEntry::button("p:recipe-quantity|-1", "-"), UiEntry::field("recipe-quantity", quantity.to_string()), UiEntry::button("p:recipe-quantity|1", "+"), UiEntry::button("p:recipe-quantity|0", "Apply quantity")]));
                let output = food_factory_bevy::recipes::per_batch(recipe);
                let summary = if recipe.is_sale() { format!("{quantity} orders, {} in sales", money(recipe.sale_cents.saturating_mul(quantity))) }
                    else { format!("{}, {} seconds on one {}", food_factory_bevy::recipes::batch_text(quantity, output), food_factory_bevy::recipes::batches(quantity, output).saturating_mul(recipe.duration_seconds), content.0.equipment_name(&recipe.station_kind)) };
                next.entries.push(UiEntry::label("recipe-summary", summary));
                for ingredient in food_factory_bevy::recipes::ingredients(&content.0.recipes, recipe, quantity, &panels.recipe_expanded) {
                    let maker = ingredient.maker.as_ref().and_then(|id| content.0.recipes.get(id));
                    let about = if ingredient.cycle { "Loop: already being made above".to_string() }
                        else if let Some(maker) = maker { format!("{}, {} ({} spare)", content.0.equipment_name(&maker.station_kind), food_factory_bevy::recipes::plural(ingredient.batches, "batch", "batches"), ingredient.spare) }
                        else { "Raw: farm or supplier".into() };
                    let label = format!("{}{} {} x{} - {about}", "  ".repeat(ingredient.depth), if ingredient.expanded { "v" } else if maker.is_some() && !ingredient.cycle { ">" } else { "-" }, content.0.item_name(&ingredient.item), ingredient.need);
                    next.entries.push(if maker.is_some() && !ingredient.cycle { UiEntry::button(format!("p:recipe-expand|{}", ingredient.path), label) }
                        else { UiEntry::label(format!("ingredient{}", ingredient.path), label) });
                }
                if recipe.inputs.is_empty() { next.entries.push(UiEntry::label("recipe-empty", "Needs nothing.")); }
            } else {
                // Unity PhonePanel.BuildRecipes: recipes grouped by the machine that makes them, then the menu; a card opens
                // the calculator.
                let all: Vec<&goods::RecipeDefinition> = content.0.recipes.values().collect();
                let mut values: std::collections::HashMap<&str, i64> = std::collections::HashMap::new();
                for sale in all.iter().filter(|r| r.is_sale() && r.inputs.len() == 1 && !r.inputs[0].item_id.is_empty()) {
                    let each = sale.sale_cents / sale.inputs[0].quantity.max(1) as i64;
                    let best = values.entry(sale.inputs[0].item_id.as_str()).or_insert(each);
                    *best = (*best).max(each);
                }
                let card = |recipe: &goods::RecipeDefinition, icon_item: &str, result: String| {
                    let needs = if recipe.inputs.is_empty() { "Nothing".to_string() } else {
                        recipe.inputs.iter().map(|i| if i.quantity > 1 { format!("{} x {}", i.quantity, content.0.item_name(&i.item_id)) } else { content.0.item_name(&i.item_id) }).collect::<Vec<_>>().join(" + ")
                    };
                    UiEntry::button(format!("p:recipe-open|{}", recipe.id), format!("{}  {}
Needs {needs}
{result}", content.0.recipe_name(&recipe.id), crate::screens::format_duration(recipe.duration_seconds, true)))
                        .with_icon(content.0.items.get(icon_item).map(|i| i.icon.clone()).unwrap_or_default())
                };
                let mut machines: Vec<&str> = all.iter().filter(|r| !r.is_sale()).map(|r| r.station_kind.as_str()).collect();
                machines.sort_by_key(|k| content.0.equipment_name(k));
                machines.dedup();
                for machine in machines {
                    next.entries.push(UiEntry::label(format!("recipes-{machine}"), content.0.equipment_name(machine).to_uppercase()));
                    let mut list: Vec<&&goods::RecipeDefinition> = all.iter().filter(|r| !r.is_sale() && r.station_kind == machine).collect();
                    list.sort_by_key(|r| content.0.recipe_name(&r.id));
                    let cards = list.into_iter().map(|r| {
                        let sells = values.get(r.output_item_id.as_str());
                        let mut result = match sells { Some(c) => format!("Sells for {}{} at a register", money(*c), if r.output_quantity > 1 { " each" } else { "" }), None => "Not sold at a register".to_string() };
                        if r.output_quantity > 1 { result.push_str(&format!(", makes {}", r.output_quantity)); }
                        card(r, &r.output_item_id, result)
                    }).collect();
                    next.entries.push(UiEntry::row(format!("recipe-cards-{machine}"), cards));
                }
                let mut menu: Vec<&&goods::RecipeDefinition> = all.iter().filter(|r| r.is_sale()).collect();
                menu.sort_by_key(|r| (r.tier, content.0.recipe_name(&r.id)));
                if !menu.is_empty() {
                    next.entries.push(UiEntry::label("recipes-menu", "MENU: SOLD AT THE REGISTER"));
                    let cards = menu.into_iter().map(|r| card(r, r.inputs.first().map_or("", |i| i.item_id.as_str()), format!("Sells for {}", money(r.sale_cents)))).collect();
                    next.entries.push(UiEntry::row("recipe-cards-menu", cards));
                }
            }
        }
        PhoneTab::Map => {
            next.entries.push(UiEntry::row("map-controls", vec![UiEntry::button("map:in", "+"), UiEntry::button("map:out", "-"), UiEntry::button("map:left", "West"), UiEntry::button("map:right", "East"), UiEntry::button("map:up", "North"), UiEntry::button("map:down", "South"), UiEntry::button("map:reset", "Reset")]));
            next.entries.push(UiEntry::label("map-controls-help", "Scroll to zoom, drag to pan, click a lot to select it."));
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
            // Unity PhonePanel.BuildStaff: this place's employees against its cap, with wages, status, Tasks and Fire.
            let here: Vec<&food_factory_bevy::protocol::EmployeeState> = world.employees.iter().filter(|e| e.site == world.site).collect();
            let cap = goods::employees::employee_cap(view, &world.site);
            let place = view.sites.iter().find(|s| s.id == world.site).map_or(world.site.clone(), |s| s.name.clone());
            next.entries.push(UiEntry::label("staff-site", place));
            if company.is_none() {
                next.entries.push(UiEntry::label("staff-count", "This place belongs to no company; nobody can be hired here."));
            } else {
                next.entries.push(UiEntry::label("staff-count", format!("Employees here: {} of {cap}, {} per game hour each, wages {} / h for the company",
                    here.len(), money(goods::employees::WAGE_CENTS_PER_HOUR), money(view.company_wage_cents_per_hour))));
                if (here.len() as i32) < cap {
                    next.entries.push(UiEntry::button("p:hire", format!("Hire, {} / h", money(goods::employees::WAGE_CENTS_PER_HOUR))));
                } else {
                    next.entries.push(UiEntry::label("staff-full", "This place has as many employees as it can take."));
                }
            }
            if here.is_empty() { next.entries.push(UiEntry::label("staff-none", "Nobody works here yet. Staff are hired for the place you stand in.")); }
            for employee in here {
                let hands = goods::inventory_location_id(&employee.id);
                let holding = view.lots.iter().any(|l| l.location_id == hands) || view.equipment.iter().any(|e| e.state == EquipmentState::Held && e.holder_id == employee.id);
                let pay = if employee.unpaid { "Unpaid, waiting outside" } else if holding { "Holding goods (empty its hands to fire)" } else { "Paid" };
                let doing = if employee.status.is_empty() { String::new() } else { format!(", {}", employee.status) };
                let running = if employee.running { "
Running tasks" } else { "" };
                next.entries.push(UiEntry::row(format!("staff-{}", employee.id), vec![
                    UiEntry::label(format!("staff-name-{}", employee.id), format!("{}
{pay}{doing}{running}", employee.name)),
                    UiEntry::button(format!("p:employee|{}", employee.id), "Tasks"),
                    UiEntry::button(format!("p:fire|{}", employee.id), confirm_label(&panels, "fire", &employee.id, "Fire")),
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
                    if let Some(ready) = readiness::evaluate(site_view, &site, world.offers.get(&site), menu.iter().copied()) {
                        let text = match &ready.blocker {
                            None => "Open for customers.".to_string(),
                            Some(blocker) => format!("Not ready: {}", crate::screens::blocker_text(blocker, &ready, &content.0, &menu)),
                        };
                        next.entries.push(UiEntry::label(format!("ready-{site}"), text));
                    }
                }
            }
        }
        PhoneTab::Ledger => {
            let Some(company) = company else { return };
            // Unity PhonePanel.BuildLedger: the latest cash changes, newest first, under column titles, then the totals.
            let entries: Vec<_> = company::ledger_of(view, &company.id).into_iter().take(LEDGER_ROWS_SHOWN).collect();
            if entries.is_empty() {
                next.entries.push(UiEntry::label("ledger-empty", "No cash changes recorded yet."));
            } else {
                next.entries.push(UiEntry::label("ledger-header", format!("{:<9} {:<34} {:<18} {:>12}", "Time", "What", "Place", "Amount")));
            }
            for entry in entries {
                let what = ledger_what(&content, entry);
                let place = world.view().and_then(|v| v.sites.iter().find(|s| s.id == entry.site_id)).map_or(entry.site_id.clone(), |s| s.name.clone());
                let amount = format!("{}{}", if entry.cents > 0 { "+" } else { "-" }, money(entry.cents.abs()));
                next.entries.push(UiEntry::label(format!("ledger-{}", entry.id),
                    format!("{:<9} {:<34} {:<18} {:>12}", crate::screens::format_duration(entry.clock_seconds, false), what, place, amount)));
            }
            next.entries.push(UiEntry::label("ledger-totals", format!("Opening {}, earlier entries {}, now {}", money(company.opening_cents), money(company.ledger_carried_cents), money(company.cash))));
        }
        PhoneTab::Trucks => {
            // Unity LogisticsPanel: trucks with their live status, routes with their editors, then each site's stock.
            let docks = company_docks(world);
            let dock_name = |id: &str| docks.iter().find(|d| d.0 == id).map(|d| d.1.clone()).unwrap_or_else(|| if id.is_empty() { "(choose a dock)".into() } else { "(unknown dock)".into() });
            let dock_site = |id: &str| world.views.values().find_map(|v| v.equipment.iter().find(|e| e.id == id)).map(|e| e.site_id.clone()).unwrap_or_default();
            let site_name = |id: &str| world.views.values().find_map(|v| v.sites.iter().find(|s| s.id == id)).map_or(id.to_string(), |s| s.name.clone());
            let item_list = |items: &[String]| items.iter().map(|i| content.0.item_name(i)).collect::<Vec<_>>().join(", ");
            let route_name = |r: &goods::Route| format!("{} to {}{}", site_name(&dock_site(&r.pickup_dock_id)), site_name(&dock_site(&r.dropoff_dock_id)),
                if r.allowed_item_ids.is_empty() { String::new() } else { format!(" ({})", item_list(&r.allowed_item_ids)) });
            let mut routes: Vec<&goods::Route> = view.routes.iter().filter(|r| company.is_some_and(|c| c.id == r.company_id)).collect();
            routes.sort_by(|a, b| a.id.cmp(&b.id));
            next.entries.push(UiEntry::label("trucks", "TRUCKS"));
            let trucks: Vec<&goods::Truck> = view.trucks.iter().filter(|t| company.is_some_and(|c| c.id == t.company_id) && !t.supplier).collect();
            if trucks.is_empty() { next.entries.push(UiEntry::label("no-trucks", "No trucks yet: buy one in the Shop.")); }
            for truck in trucks {
                let marker = if panels.selected_truck.as_deref() == Some(truck.id.as_str()) { "> " } else { "" };
                next.entries.push(UiEntry::label(format!("truck-{}", truck.id), format!("{marker}{}\n{}\n{}\n{}\n{}", truck.name, truck_status(world, view, &content, truck),
                    truck_destination(view, truck, &site_name, &dock_site), truck_route(view, &content, truck, &route_name, &site_name), truck_cargo(view, &content, truck))));
                let mut row = vec![];
                for route in &routes {
                    row.push(UiEntry::button(format!("p:assign|{}|{}", truck.id, route.id), route_name(route)).selected(truck.route_id == route.id));
                }
                row.push(UiEntry::button(format!("p:assign|{}|", truck.id), "Parked").selected(truck.route_id.is_empty()));
                row.push(UiEntry::button(format!("p:sell-truck|{}", truck.id), confirm_label(&panels, "sell-truck", &truck.id, "Sell")).selected(panels.confirming.as_deref() == Some(format!("sell-truck|{}", truck.id).as_str())));
                next.entries.push(UiEntry::row(format!("truck-row-{}", truck.id), row));
            }
            next.entries.push(UiEntry::label("routes", "ROUTES"));
            let chooser = |key: &str, field: &str, caption: &str, value: String| vec![
                UiEntry::label(format!("{key}-{field}-caption"), caption.to_string()),
                UiEntry::button(format!("p:draft|{key}|{field}|-1"), "<"),
                UiEntry::label(format!("{key}-{field}-value"), value),
                UiEntry::button(format!("p:draft|{key}|{field}|1"), ">"),
            ];
            let draft_of_key = |key: &str| -> RouteDraft {
                panels.route_drafts.get(key).cloned().unwrap_or_else(|| match routes.iter().find(|r| r.id == key) {
                    Some(r) => RouteDraft { pickup: r.pickup_dock_id.clone(), dropoff: r.dropoff_dock_id.clone(), item: r.allowed_item_ids.first().cloned() },
                    None => RouteDraft { pickup: docks.first().map(|d| d.0.clone()).unwrap_or_default(), dropoff: docks.get(1).map(|d| d.0.clone()).unwrap_or_default(), item: None },
                })
            };
            let cargo_name = |item: &Option<String>| item.as_ref().map_or("Any cargo".to_string(), |i| content.0.item_name(i));
            for route in &routes {
                let on: Vec<String> = view.trucks.iter().filter(|t| t.route_id == route.id).map(|t| t.name.clone()).collect();
                next.entries.push(UiEntry::label(format!("route-title-{}", route.id), format!("{}\n{}", route_name(route),
                    if on.is_empty() { "No trucks: assign one above.".to_string() } else { format!("Trucks: {}", on.join(", ")) })));
                let draft = draft_of_key(&route.id);
                let applied = route.pickup_dock_id == draft.pickup && route.dropoff_dock_id == draft.dropoff && route.allowed_item_ids.first() == draft.item.as_ref() && route.allowed_item_ids.len() <= 1;
                let mut row = chooser(&route.id, "pickup", "From", dock_name(&draft.pickup));
                row.extend(chooser(&route.id, "dropoff", "To", dock_name(&draft.dropoff)));
                row.extend(chooser(&route.id, "cargo", "Cargo", cargo_name(&draft.item)));
                next.entries.push(UiEntry::row(format!("route-edit-{}", route.id), row));
                next.entries.push(UiEntry::row(format!("route-actions-{}", route.id), vec![
                    UiEntry::button(format!("p:apply-route|{}", route.id), if applied { "Applied" } else { "Apply" }).selected(!applied),
                    UiEntry::button(format!("p:delete-route|{}", route.id), "Delete"),
                ]));
            }
            if docks.len() >= 2 {
                let draft = draft_of_key("new");
                next.entries.push(UiEntry::label("new-route-title", "New route"));
                let mut row = chooser("new", "pickup", "From", dock_name(&draft.pickup));
                row.extend(chooser("new", "dropoff", "To", dock_name(&draft.dropoff)));
                row.extend(chooser("new", "cargo", "Cargo", cargo_name(&draft.item)));
                next.entries.push(UiEntry::row("new-route", row));
                next.entries.push(UiEntry::button("p:create-route", "Create route"));
            } else {
                next.entries.push(UiEntry::label("route-help", "Routes run between two of your docks."));
            }
            // Each company site's storage and dock stock, with buttons that move one stack (Unity LogisticsPanel.SiteCard).
            next.entries.push(UiEntry::label("sites", "SITES"));
            for site in world.company_sites() {
                let distance = goods::trucks::road_metres(view, &world.site, &site).map_or(String::new(), |m| format!("  ({m} m by road)"));
                next.entries.push(UiEntry::label(format!("site-stock-{site}"), format!("{}{distance}", site_name(&site))));
                let Some(site_view) = world.view_of(&site) else {
                    next.entries.push(UiEntry::label(format!("site-loading-{site}"), "Loading..."));
                    continue;
                };
                let storage = site_view.locations.iter().find(|l| l.site_id == site && l.kind == "storage").map(|l| l.id.clone());
                let mut site_docks: Vec<&goods::Equipment> = site_view.equipment.iter().filter(|e| e.site_id == site && e.kind == goods::DOCK_KIND && e.placed()).collect();
                site_docks.sort_by(|a, b| a.id.cmp(&b.id));
                let dock = site_docks.first().copied();
                let mut stock = |location: &str, title: &str, target: Option<(&str, String)>| {
                    let capacity = site_view.locations.iter().find(|l| l.id == location).map_or(0, |l| l.capacity);
                    let lots = lots_in(site_view, location);
                    let used = rules::slots_used(lots.iter().copied(), |i| content.0.max_stack(i));
                    next.entries.push(UiEntry::label(format!("stock-{location}"), format!("{title}  {used}/{capacity}")));
                    let mut groups: Vec<(&str, bool)> = lots.iter().map(|l| (l.item_id.as_str(), l.spoiled)).collect();
                    groups.sort();
                    groups.dedup();
                    if groups.is_empty() { next.entries.push(UiEntry::label(format!("stock-empty-{location}"), "empty")); }
                    for (item, spoiled) in groups {
                        let units: i32 = lots.iter().filter(|l| l.item_id == item && l.spoiled == spoiled).map(|l| l.quantity).sum();
                        let mut row = vec![UiEntry::label(format!("stock-{location}-{item}-{spoiled}"), format!("{}{} x{units}", content.0.item_name(item), if spoiled { " (spoiled)" } else { "" }))];
                        if let Some((label, to)) = &target {
                            row.push(UiEntry::button(format!("p:move-stack|{location}|{to}|{item}#{}", u8::from(spoiled)), *label));
                        }
                        next.entries.push(UiEntry::row(format!("stock-row-{location}-{item}-{spoiled}"), row));
                    }
                };
                if let Some(storage) = &storage { stock(storage, "Storage", dock.map(|d| ("Ship", d.input_location_id()))); }
                match dock {
                    Some(d) => {
                        stock(&d.input_location_id(), "Dock outgoing", storage.clone().map(|s| ("Unstage", s)));
                        stock(&d.output_location_id(), "Dock incoming", storage.clone().map(|s| ("Store", s)));
                    }
                    None => next.entries.push(UiEntry::label(format!("no-dock-{site}"), "No dock here: trucks cannot serve this site.")),
                }
            }
        }
        PhoneTab::Farms => {
            let docks: Vec<(String, String)> = company_docks(world).into_iter().filter(|d| view.equipment.iter().any(|e| e.id == d.0 && e.site_id == world.site)).collect();
            // Open orders with their trucks' progress, and repeating orders with Cancel (Unity's Orders app).
            let site_name = |id: &str| world.views.values().find_map(|v| v.sites.iter().find(|s| s.id == id)).map_or(id.to_string(), |s| s.name.clone());
            let open: Vec<&goods::FarmOrder> = view.farm_orders.iter().filter(|o| company.is_some_and(|c| c.id == o.company_id)).collect();
            next.entries.push(UiEntry::label("orders", if open.is_empty() { "No open farm orders.".to_string() } else { "OPEN ORDERS".to_string() }));
            for order in open {
                let truck = view.trucks.iter().find(|t| t.order_id == order.id);
                let progress = truck.map_or("Waiting for a truck".to_string(), |t| truck_status(world, view, &content, t));
                next.entries.push(UiEntry::label(format!("order-{}", order.id), format!("{} {} to {}{}: {progress}", order.units, content.0.item_name(&order.item_id),
                    site_name(&order.site_id), if order.pickup { " (pickup)" } else { "" })));
            }
            for schedule in view.farm_schedules.iter().filter(|s| company.is_some_and(|c| c.id == s.company_id)) {
                let item = content.0.offers.iter().find(|o| o.id == schedule.offer_id).and_then(|o| match &o.kind { OfferKind::Goods(g) => Some(content.0.item_name(&g.item_id)), _ => None }).unwrap_or_else(|| schedule.offer_id.clone());
                let problem = if schedule.last_problem.is_empty() { String::new() } else { format!(" (last time: {})", crate::screens::reason_text(&schedule.last_problem)) };
                next.entries.push(UiEntry::row(format!("schedule-{}", schedule.id), vec![
                    UiEntry::label(format!("schedule-label-{}", schedule.id), format!("Every {} h: {} pack(s) of {item}{problem}", schedule.every_hours, schedule.packs)),
                    UiEntry::button(format!("p:cancel-schedule|{}", schedule.id), "Cancel"),
                ]));
            }
            let Some((dock, dock_name)) = (!docks.is_empty()).then(|| docks[panels.farm_dock % docks.len()].clone()) else {
                next.entries.push(UiEntry::label("farm-help", "Farm orders need a dock at this site."));
                return;
            };
            let packs = panels.farm_packs.max(1);
            let truck_name = panels.farm_truck.as_ref().and_then(|t| view.trucks.iter().find(|x| &x.id == t)).map(|t| t.name.clone());
            next.entries.push(UiEntry::label("farm-form", "ORDER FROM A FARM"));
            next.entries.push(UiEntry::row("packs", vec![
                UiEntry::button("p:packs|-1", "-"),
                UiEntry::label("packs-label", format!("{packs} pack(s)")),
                UiEntry::button("p:packs|1", "+"),
                UiEntry::button("p:farm-dock", format!("To {dock_name}")),
                UiEntry::button("p:farm-truck", match &truck_name { Some(name) => format!("Pickup by {name}"), None => format!("Delivery (+{})", money(farms::FARM_DELIVERY_FEE_CENTS)) }),
                UiEntry::button("p:farm-repeat", if panels.farm_repeat.is_some() { "Repeat order: on" } else { "Repeat order: off" }).selected(panels.farm_repeat.is_some()),
            ]));
            if let Some(hours) = panels.farm_repeat {
                next.entries.push(UiEntry::row("farm-every", vec![
                    UiEntry::button("p:farm-every|-1", "-"),
                    UiEntry::label("farm-every-label", format!("Repeat every {hours} game h (first order at the next game hour)")),
                    UiEntry::button("p:farm-every|1", "+"),
                ]));
            }
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
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use food_factory_bevy::goods::Location;

    #[test]
    fn lot_front_is_on_the_sidewalk_facing_the_building() {
        let layout = food_factory_bevy::world::generator::generate("1", 20260927, &food_factory_bevy::world::settings::WorldSettings::default()).unwrap().layout;
        for lot in layout.lots.iter().take(40) {
            let (x, z, yaw) = lot_front(&layout, &lot.id).unwrap();
            let inside = x >= lot.x as f32 && x <= (lot.x + lot.width) as f32 && z >= lot.z as f32 && z <= (lot.z + lot.depth) as f32;
            assert!(!inside, "{} front {x},{z} lies on the lot", lot.id);
            // Looking along the yaw (Unity: x = sin, z = cos) points back toward the lot's centre.
            let (cx, cz) = (lot.x as f32 + lot.width as f32 * 0.5 - x, lot.z as f32 + lot.depth as f32 * 0.5 - z);
            let (fx, fz) = (yaw.to_radians().sin(), yaw.to_radians().cos());
            assert!(fx * cx + fz * cz > 0.0, "{} faces away", lot.id);
        }
    }

    #[test]
    fn arranging_moves_a_stack_before_another_or_last() {
        let k = |s: &str| (s.to_string(), false);
        let mut order = vec![k("a"), k("b"), k("c")];
        arrange(&mut order, k("c"), Some(k("a")));
        assert_eq!(order, vec![k("c"), k("a"), k("b")]);
        arrange(&mut order, k("c"), None);
        assert_eq!(order, vec![k("a"), k("b"), k("c")]);
        arrange(&mut order, k("d"), Some(k("b")));
        assert_eq!(order, vec![k("a"), k("d"), k("b"), k("c")]);
    }

    #[test]
    fn sale_value_prices_whole_unspoiled_portions() {
        let sale = goods::RecipeDefinition { id: "sell-bread".into(), sale_cents: 250, inputs: vec![goods::RecipeInput { item_id: "bread".into(), quantity: 2 }], ..Default::default() };
        assert!(sale.is_sale());
        let lots = [Lot { item_id: "bread".into(), quantity: 5, ..Default::default() }, Lot { item_id: "bread".into(), quantity: 9, spoiled: true, ..Default::default() }];
        let refs: Vec<&Lot> = lots.iter().collect();
        assert_eq!(sale_value_cents(&refs, &[&sale]), 500);
    }

    /// A dropped stack moves only what the destination's free slots take, oldest exposure first, split over its lots.
    #[test]
    fn stack_transfer_fills_free_slots_oldest_first() {
        let content = food_factory_bevy::content::GameContent::bundled().unwrap();
        let item = content.items.keys().next().unwrap().clone();
        let stack = content.max_stack(&item);
        let lot = |id: &str, quantity: i32, exposure: i64| Lot { id: id.into(), location_id: "a".into(), item_id: item.clone(), quantity, exposure_seconds: exposure, ..Default::default() };
        let view = Snapshot {
            locations: vec![Location { id: "a".into(), capacity: 10, ..Default::default() }, Location { id: "b".into(), capacity: 1, ..Default::default() }],
            lots: vec![lot("young", stack, 5), lot("old", stack, 50)],
            ..Default::default()
        };
        let commands = stack_transfers(&view, &content, "a", &item, false, 2 * stack, "b");
        assert_eq!(commands.len(), 1);
        let Command::Transfer { lot, destination, quantity } = &commands[0] else { panic!() };
        assert_eq!((lot.as_str(), destination.as_str(), *quantity), ("old", "b", stack));
        assert!(stack_transfers(&view, &content, "a", &item, false, 5, "a").is_empty());
    }
}
