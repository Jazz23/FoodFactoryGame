// The authoritative server (the port of Unity's SessionRoot server side, GeneratedWorld, SessionAdmission and
// GoodsNetworkBridge). It owns the world simulation, its SQLite save and the player registry, runs the one world clock (a
// second at a time in memory, committed every 10 s), applies every client and employee command durably before answering, and
// pushes revisioned baselines of the sites each connection watches. It runs on its own thread; clients reach it only through
// messages (an in-process link for the host's own client, TCP for others), so the host plays exactly like a remote player.
use std::collections::{BTreeMap, HashMap, HashSet};
use std::path::{Path, PathBuf};
use std::sync::mpsc::{self, Receiver, Sender, TryRecvError};
use std::sync::Arc;
use std::time::{Duration, Instant};

use sha2::{Digest, Sha256};

use crate::content::GameContent;
use crate::employees::{self, Agent, Host};
use crate::goods::{self, GoodsWorld, Outcome, PropertyOffer, Snapshot};
use crate::persistence::{PlayerRegistry, WorldStore};
use crate::protocol::{AvatarState, ClientMessage, Command, Pose, ServerMessage, PROTOCOL_VERSION};
use crate::world::{generator, layout::WorldLayout, random, roads::RoadNetwork, settings::WorldSettings};

pub const COMPANY_ID: &str = "company-1";
/// PROTOTYPE starting capital of the one company every player joins: $1,000,000.
pub const STARTING_CASH: i64 = 100_000_000;
pub const STARTER_DOUGH: i32 = 20;
pub const DOUGH_SPOIL_AFTER_SECONDS: i64 = 7200;
pub const INVENTORY_CAPACITY: i32 = 30;
pub const START_COUNTER_ID: &str = "start-counter";
pub const START_TABLE_ID: &str = "start-table";
pub const START_DOCK_ID: &str = "start-dock";
pub const MAX_PLAYERS: usize = 8;
/// Clock steps between periodic commits (decision 0016).
const COMMIT_EVERY_SECONDS: u32 = 10;
/// Outcomes kept for replay; older ones are dropped (the Unity save kept every one).
const OUTCOMES_KEPT: usize = 4000;

pub type ConnectionId = u64;

/// One message from a connection, or its end.
pub enum Inbound {
    Open { connection: ConnectionId, outbox: Sender<ServerMessage> },
    Message { connection: ConnectionId, message: ClientMessage },
    Closed { connection: ConnectionId },
}

/// What starting a server needs.
#[derive(Clone, Debug)]
pub struct ServerOptions {
    pub save_directory: PathBuf,
    pub world_id: String,
    /// Used only when this start creates the world; blank picks a random seed.
    pub seed_text: String,
    /// Singleplayer admits only this secret.
    pub only_secret: Option<String>,
}

struct Player {
    id: String,
    name: String,
    pose: Option<Pose>,
    watched: Vec<String>,
    sent: HashMap<String, i64>,
    last_crowd: String,
}

struct Connection {
    outbox: Sender<ServerMessage>,
    player: Option<Player>,
}

/// Everything a running world needs, owned by the server thread.
pub struct WorldServer {
    pub world: GoodsWorld,
    store: WorldStore,
    registry: PlayerRegistry,
    pub layout: Arc<WorldLayout>,
    pub layout_hash: String,
    pub content: Arc<GameContent>,
    start: PropertyOffer,
    offers: HashMap<String, PropertyOffer>,
    agents: Vec<Agent>,
    connections: BTreeMap<ConnectionId, Connection>,
    only_secret: Option<String>,
    clock_accumulator: f32,
    uncommitted_steps: u32,
    next_request: u64,
    persistence_failed: Option<String>,
    last_avatars: Instant,
    last_baselines: Instant,
}

fn layout_hash(layout: &WorldLayout) -> String { format!("{:x}", Sha256::digest(serde_json::to_vec(layout).unwrap_or_default())) }

/// Regenerates a stored layout from its seed (clients do this instead of receiving it).
pub fn regenerate_layout(requested_seed: &str, seed: u64) -> WorldLayout {
    let mut layout = generator::build(seed, &WorldSettings::default());
    layout.requested_seed = requested_seed.to_string();
    layout
}

/// The layout as a client rebuilds it: the attempt seed and the requested text.
pub fn client_layout(requested_seed: &str, attempt_seed: u64, attempt: i32) -> WorldLayout {
    let mut layout = regenerate_layout(requested_seed, attempt_seed);
    layout.attempt = attempt;
    layout
}

/// The listed lot of the layout's starting restaurant.
pub fn start_offer(layout: &WorldLayout) -> Result<PropertyOffer, String> {
    let lot = layout.lots.iter().find(|l| l.building_id == layout.start_restaurant_id).ok_or("The layout has no lot for its starting restaurant.")?;
    goods::layout_content::property_offers(layout).into_iter().find(|o| o.lot_id == lot.id).ok_or_else(|| "missing start offer".to_string())
}

/// Players arrive on the starting lot's apron two cells out from its first door, facing it, spread along the wall.
pub fn apron_spawn(offer: &PropertyOffer, index: usize, elevation: f32) -> Pose {
    let door = offer.doors[0];
    let outward = if door.z == offer.building_z + offer.building_depth - 1 {
        (0, 1)
    } else if door.z == offer.building_z {
        (0, -1)
    } else if door.x == offer.building_x {
        (-1, 0)
    } else {
        (1, 0)
    };
    let along = (outward.1, outward.0);
    let step = (index % 5) as i32;
    let offset = if step == 0 { 0 } else { (if step % 2 == 1 { 1 } else { -1 }) * 2 * ((step + 1) / 2) };
    let x = (door.x + outward.0 * 2 + along.0 * offset).clamp(0, offer.width - 1);
    let z = (door.z + outward.1 * 2 + along.1 * offset).clamp(0, offer.depth - 1);
    let yaw = (-outward.0 as f32).atan2(-outward.1 as f32).to_degrees();
    Pose { x: (offer.lot_x + x) as f32 + 0.5, y: elevation, z: (offer.lot_z + z) as f32 + 0.5, yaw }
}

impl WorldServer {
    /// Opens (or creates) a world save and registers its content; nothing listens yet.
    pub fn open(options: &ServerOptions, content: Arc<GameContent>) -> Result<Self, String> {
        std::fs::create_dir_all(&options.save_directory).map_err(|e| e.to_string())?;
        let mut store = WorldStore::open(&options.save_directory.join("world.db"))?;
        let layout = match store.load_layout()? {
            Some(layout) => layout,
            None => {
                if store.has_snapshots() {
                    return Err("This save has no generated layout.".into());
                }
                let (requested, seed) = random::resolve_seed(&options.seed_text);
                let result = generator::generate(&requested, seed, &WorldSettings::default())?;
                store.create_layout(&options.world_id, &result.layout)?;
                result.layout
            }
        };
        let layout = Arc::new(layout);
        let start = start_offer(&layout)?;
        let catalog = goods::layout_content::property_offers(&layout);
        let network = Arc::new(RoadNetwork::new(&layout)?);
        let mut world = if store.has_snapshots() {
            GoodsWorld::restore(store.load()?)
        } else {
            GoodsWorld::new(&options.world_id)
        };
        content.register_with(&mut world)?;
        world.register_property_offers(catalog.clone())?;
        world.register_roads(network)?;
        let created = !store.has_snapshots();
        if created {
            world.bootstrap_company(goods::Company { id: COMPANY_ID.into(), cash: STARTING_CASH, ..Default::default() })?;
            world.bootstrap_property(&start, COMPANY_ID)?;
            if start.has_dock {
                if let Some(dock) = content.equipment.get(goods::DOCK_KIND) {
                    let _ = world.bootstrap_equipment(dock.placed(START_DOCK_ID, &start.site_id, start.dock_x, start.dock_z, start.dock_rotation));
                }
            }
        }
        equip(&mut world, &start, &content);
        add_customers(&mut world, &layout)?;
        content.apply_capacities(&mut world);
        world.release_staff_of(None);
        store.save(&world)?;
        let registry = PlayerRegistry::open(&options.save_directory.join("players.db"))?;
        let mut server = WorldServer {
            world,
            store,
            registry,
            layout_hash: layout_hash(&layout),
            layout,
            content,
            offers: catalog.into_iter().map(|o| (o.site_id.clone(), o)).collect(),
            start,
            agents: vec![],
            connections: BTreeMap::new(),
            only_secret: options.only_secret.clone(),
            clock_accumulator: 0.0,
            uncommitted_steps: 0,
            next_request: 0,
            persistence_failed: None,
            last_avatars: Instant::now(),
            last_baselines: Instant::now(),
        };
        let records = server.world.state.employees.clone();
        for record in records {
            let agent = Agent::new(&record, &mut server);
            server.agents.push(agent);
        }
        Ok(server)
    }

    pub fn start_site(&self) -> &str { &self.start.site_id }

    fn send(&self, connection: ConnectionId, message: ServerMessage) {
        if let Some(c) = self.connections.get(&connection) {
            let _ = c.outbox.send(message);
        }
    }

    fn broadcast(&self, message: ServerMessage) {
        for connection in self.connections.values().filter(|c| c.player.is_some()) {
            let _ = connection.outbox.send(message.clone());
        }
    }

    fn connected(&self, player: &str) -> bool { self.connections.values().any(|c| c.player.as_ref().is_some_and(|p| p.id == player)) }

    fn handle(&mut self, inbound: Inbound) {
        match inbound {
            Inbound::Open { connection, outbox } => {
                self.connections.insert(connection, Connection { outbox, player: None });
            }
            Inbound::Closed { connection } => self.close(connection),
            Inbound::Message { connection, message } => self.message(connection, message),
        }
    }

    fn close(&mut self, connection: ConnectionId) {
        let Some(closed) = self.connections.remove(&connection) else { return };
        let Some(player) = closed.player else { return };
        if let Some(pose) = player.pose {
            self.registry.save_pose(&player.id, [pose.x, pose.y, pose.z, pose.yaw]);
        }
        if self.world.release_staff_of(Some(&player.id)) {
            self.commit_now();
        }
    }

    fn message(&mut self, connection: ConnectionId, message: ClientMessage) {
        let player = self.connections.get(&connection).and_then(|c| c.player.as_ref().map(|p| p.id.clone()));
        match (message, player) {
            (ClientMessage::Hello { version, name, secret }, None) => self.admit(connection, version, &name, &secret),
            (_, None) => {}
            (ClientMessage::Watch { sites }, Some(player)) => {
                let allowed: Vec<String> = sites.into_iter().filter(|s| self.world.state.can_view(&player, s)).take(16).collect();
                if let Some(p) = self.connections.get_mut(&connection).and_then(|c| c.player.as_mut()) {
                    p.sent.retain(|site, _| allowed.contains(site));
                    p.watched = allowed;
                }
            }
            (ClientMessage::Pose { pose }, Some(_)) => {
                if pose.x.is_finite() && pose.z.is_finite() && pose.y.is_finite() && pose.yaw.is_finite() {
                    if let Some(p) = self.connections.get_mut(&connection).and_then(|c| c.player.as_mut()) {
                        p.pose = Some(pose);
                    }
                }
            }
            (ClientMessage::Command { request, command }, Some(player)) => {
                let command = self.resolve_positions(connection, &player, command);
                let outcome = match command {
                    Ok(command) => self.apply(&player, &request, command),
                    Err(reason) => Outcome { request_id: request.clone(), player_id: player.clone(), reason: reason.into(), revision: self.world.state.revision, ..Default::default() },
                };
                if outcome.accepted && outcome.reason == "entered" {
                    let site = self.world.carried_site_of(&player).unwrap_or_default();
                    self.send(connection, ServerMessage::CurrentSite { site });
                }
                if outcome.accepted && outcome.reason == "hired" {
                    if let Some(record) = self.world.state.employees.iter().find(|e| e.id == outcome.equipment_id).cloned() {
                        let agent = Agent::new(&record, self);
                        self.agents.push(agent);
                    }
                }
                if outcome.accepted && outcome.reason == "fired" {
                    self.agents.retain(|a| a.id != outcome.equipment_id);
                }
                self.send(connection, ServerMessage::Outcome { request, outcome });
            }
            (ClientMessage::EmployeeRun { employee, source, tasks }, Some(player)) => {
                if self.may_command(&player, &employee) {
                    self.with_agent(&employee, |agent, host| agent.run(host, &source, tasks.as_deref()));
                    self.send_detail(connection, &employee);
                }
            }
            (ClientMessage::EmployeeStop { employee }, Some(player)) => {
                if self.may_command(&player, &employee) {
                    self.with_agent(&employee, |agent, host| agent.stop(host));
                    self.send_detail(connection, &employee);
                }
            }
            (ClientMessage::EmployeeRename { employee, name }, Some(player)) => {
                if self.may_command(&player, &employee) {
                    match self.world.rename_employee(&employee, &name) {
                        Ok(true) => {
                            self.commit_now();
                            if let Some(agent) = self.agents.iter_mut().find(|a| a.id == employee) {
                                agent.name = name.trim().to_string();
                            }
                        }
                        Ok(false) => {}
                        Err(problem) => self.send(connection, ServerMessage::Notice { text: format!("Could not rename: {problem}") }),
                    }
                }
            }
            (ClientMessage::EmployeeDetail { employee }, Some(player)) => {
                if self.may_command(&player, &employee) {
                    self.send_detail(connection, &employee);
                }
            }
            (ClientMessage::SetWageWarning { cents }, Some(player)) => {
                self.registry.set_wage_warning(&player, cents);
            }
            (ClientMessage::Leave, Some(_)) => self.close(connection),
            (ClientMessage::Hello { .. }, Some(_)) => {}
        }
    }

    fn send_detail(&self, connection: ConnectionId, employee: &str) {
        let Some(agent) = self.agents.iter().find(|a| a.id == employee) else { return };
        self.send(connection, ServerMessage::EmployeeDetail { employee: employee.into(), source: agent.source.clone(), tasks: agent.tasks.clone(), status: agent.status.clone() });
    }

    fn may_command(&self, player: &str, employee: &str) -> bool {
        self.world.state.employees.iter().any(|e| e.id == employee && self.world.state.can_view(player, &e.site_id))
    }

    fn with_agent(&mut self, employee: &str, action: impl FnOnce(&mut Agent, &mut dyn Host)) {
        let Some(index) = self.agents.iter().position(|a| a.id == employee) else { return };
        let mut agent = self.agents.remove(index);
        action(&mut agent, self);
        self.agents.insert(index, agent);
    }

    /// Commands whose position comes from the server's copy of the avatar.
    fn resolve_positions(&self, connection: ConnectionId, player: &str, command: Command) -> Result<Command, &'static str> {
        let pose = self.connections.get(&connection).and_then(|c| c.player.as_ref()).and_then(|p| p.pose);
        match command {
            Command::EnterSite { .. } | Command::Hire if pose.is_none() => Err("no-position"),
            Command::Hire => {
                let _ = player;
                Ok(Command::Hire)
            }
            other => Ok(other),
        }
    }

    fn admit(&mut self, connection: ConnectionId, version: u32, name: &str, secret: &str) {
        let reject = |server: &Self, reason: &str| server.send(connection, ServerMessage::Rejected { reason: reason.into() });
        if version != PROTOCOL_VERSION {
            return reject(self, "version-mismatch");
        }
        if self.only_secret.as_deref().is_some_and(|s| s != secret) {
            return reject(self, "singleplayer");
        }
        if self.connections.values().filter(|c| c.player.is_some()).count() >= MAX_PLAYERS {
            return reject(self, "server-full");
        }
        if let Some(existing) = self.registry.find_by_secret(secret) {
            if self.connected(&existing.player_id) {
                return reject(self, "already-connected");
            }
        }
        let identity = match self.registry.register_or_resolve(name, secret) {
            Ok(identity) => identity,
            Err(reason) => return reject(self, reason),
        };
        let primary = self.start.site_id.clone();
        let starter = vec![("dough".to_string(), STARTER_DOUGH, DOUGH_SPOIL_AFTER_SECONDS)];
        let before = self.world.state.clone();
        let mut granted = self.world.ensure_grant(&identity.player_id, &primary, INVENTORY_CAPACITY, &starter).is_ok();
        let others: Vec<String> = self.world.state.company_of_site(&primary).map(|c| c.site_ids.clone()).unwrap_or_default().into_iter().filter(|s| *s != primary).collect();
        for site in others {
            granted &= self.world.ensure_grant(&identity.player_id, &site, 0, &[]).is_ok();
        }
        if !granted || self.store.save(&self.world).is_err() {
            self.world.replace_state(before);
            return reject(self, "persistence-unavailable");
        }
        let site = self.world.carried_site_of(&identity.player_id).unwrap_or(primary);
        let spawn = self.registry.pose_of(&identity.player_id).map(|p| Pose { x: p[0], y: p[1], z: p[2], yaw: p[3] }).unwrap_or_else(|| {
            let index = self.connections.values().filter(|c| c.player.is_some()).count();
            let offer = self.offers.get(&site).unwrap_or(&self.start);
            let elevation = self.layout.building(&offer.building_id).map_or(0.0, |b| b.elevation_cm as f32 / 100.0);
            apron_spawn(offer, index, elevation)
        });
        let wage_warning = self.registry.wage_warning(&identity.player_id);
        if let Some(c) = self.connections.get_mut(&connection) {
            c.player = Some(Player { id: identity.player_id.clone(), name: identity.display_name.clone(), pose: None, watched: vec![site.clone()], sent: HashMap::new(), last_crowd: String::new() });
        }
        self.send(connection, ServerMessage::Welcome {
            player: identity.player_id, name: identity.display_name, site, world_id: self.world.state.world_id.clone(), requested_seed: self.layout.requested_seed.clone(),
            seed: self.layout.seed, layout_hash: format!("{}:{}", self.layout.attempt, self.layout_hash), spawn, wage_warning,
        });
    }

    /// Applies one command durably: committed before it is acknowledged; a failed commit restores the prior state.
    pub fn apply(&mut self, actor: &str, request: &str, command: Command) -> Outcome {
        if let Some(problem) = &self.persistence_failed {
            let _ = problem;
            return Outcome { request_id: request.into(), player_id: actor.into(), reason: "persistence-unavailable".into(), revision: self.world.state.revision, ..Default::default() };
        }
        let before = self.world.state.clone();
        let outcome = self.dispatch(actor, request, command);
        if self.world.state.revision != before.revision {
            if self.world.state.outcomes.len() > OUTCOMES_KEPT {
                let excess = self.world.state.outcomes.len() - OUTCOMES_KEPT;
                self.world.state.outcomes.drain(..excess);
            }
            if let Err(error) = self.store.save(&self.world) {
                eprintln!("[Server] commit failed: {error}");
                self.world.replace_state(before);
                return Outcome { request_id: request.into(), player_id: actor.into(), reason: "persistence-unavailable".into(), revision: self.world.state.revision, ..Default::default() };
            }
            self.uncommitted_steps = 0;
        }
        outcome
    }

    fn dispatch(&mut self, actor: &str, request: &str, command: Command) -> Outcome {
        let w = &mut self.world;
        match command {
            Command::Transfer { lot, destination, quantity } => w.transfer(actor, request, &lot, &destination, quantity, ""),
            Command::PickUp { equipment } => w.pick_up(actor, request, &equipment),
            Command::Place { equipment, x, z, rotation, level } => w.place(actor, request, &equipment, x, z, rotation, level),
            Command::Give { equipment, employee } => w.give(actor, request, &equipment, &employee),
            Command::PlaceBelt { site, x, z, direction, level } => w.place_belt(actor, request, &site, x, z, direction, level),
            Command::PlaceLift { site, x, z, direction, level, lift } => w.place_lift(actor, request, &site, x, z, direction, level, lift),
            Command::RemoveBelt { belt } => w.remove_belt(actor, request, &belt),
            Command::PlaceOnBelt { lot, belt } => w.place_on_belt(actor, request, &lot, &belt),
            Command::TakeFromBelt { lot } => w.take_from_belt(actor, request, &lot),
            Command::Buy { site, offer } => w.buy(actor, request, &site, &offer),
            Command::BuyAndPlace { order } => w.buy_and_place(actor, request, &order),
            Command::Sell { equipment } => w.sell(actor, request, &equipment),
            Command::Staff { register, staff } => w.staff(actor, request, &register, &staff),
            Command::SelectRecipe { equipment, recipe } => w.select_recipe(actor, request, &equipment, &recipe),
            Command::SetPower { equipment, on } => w.set_power(actor, request, &equipment, on),
            Command::StartJob { station, recipe } => w.start_job(actor, request, &station, &recipe),
            Command::AddFloor { building, x, z } => w.add_floor(actor, request, &building, x, z),
            Command::OrderShell { order } => w.order_shell(actor, request, &order),
            Command::CreateRoute { pickup, dropoff, items } => w.create_route(actor, request, &pickup, &dropoff, items),
            Command::SetRoute { route, pickup, dropoff, items } => w.set_route(actor, request, &route, &pickup, &dropoff, items),
            Command::DeleteRoute { route } => w.delete_route(actor, request, &route),
            Command::AssignTruck { truck, route } => w.assign_truck(actor, request, &truck, &route),
            Command::SellTruck { truck } => w.sell_truck(actor, request, &truck),
            Command::BuyProperty { paying_site, lot } => w.buy_property(actor, request, &paying_site, &lot),
            Command::RenameSite { site, name } => w.rename_site(actor, request, &site, &name),
            Command::EnterSite { site } => {
                let pose = self.pose_of(actor);
                let (x, z) = pose.map_or((f32::NAN, f32::NAN), |p| (p.x, p.z));
                self.world.enter_site(actor, request, &site, x, z)
            }
            Command::Hire => {
                let pose = self.pose_of(actor).unwrap_or_default();
                let site = self.world.carried_site_of(actor).unwrap_or_default();
                let origin = self.offers.get(&site).map_or((0, 0), |o| (o.lot_x, o.lot_z));
                self.world.hire(actor, request, pose.x - origin.0 as f32, 0.0, pose.z - origin.1 as f32, pose.yaw)
            }
            Command::Fire { employee } => w.fire(actor, request, &employee),
            Command::OrderFromFarm { farm, offer, packs, dock, truck } => w.order_from_farm(actor, request, &farm, &offer, packs, &dock, &truck),
            Command::ScheduleFarmOrder { farm, offer, packs, dock, truck, every_hours } => w.schedule_farm_order(actor, request, &farm, &offer, packs, &dock, &truck, every_hours),
            Command::CancelFarmSchedule { schedule } => w.cancel_farm_schedule(actor, request, &schedule),
        }
    }

    fn pose_of(&self, player: &str) -> Option<Pose> {
        self.connections.values().filter_map(|c| c.player.as_ref()).find(|p| p.id == player).and_then(|p| p.pose)
    }

    fn commit_now(&mut self) {
        match self.store.save(&self.world) {
            Ok(()) => {
                self.uncommitted_steps = 0;
                self.persistence_failed = None;
            }
            Err(error) => {
                eprintln!("[Server] commit failed: {error}");
                self.persistence_failed = Some(error);
            }
        }
    }

    /// One server frame: clock, employees and replication.
    pub fn frame(&mut self, dt: f32) {
        if self.persistence_failed.is_none() {
            self.clock_accumulator += dt;
        }
        while self.clock_accumulator >= 1.0 {
            self.clock_accumulator -= 1.0;
            let before = self.world.state.clone();
            if let Err(error) = self.world.advance(1) {
                eprintln!("[Server] clock step failed: {error}");
                self.world.replace_state(before);
                break;
            }
            self.uncommitted_steps += 1;
            if self.uncommitted_steps >= COMMIT_EVERY_SECONDS {
                self.commit_now();
            }
        }
        let mut agents = std::mem::take(&mut self.agents);
        for agent in &mut agents {
            agent.update(self, dt);
        }
        self.agents.append(&mut agents);
        self.replicate();
    }

    fn replicate(&mut self) {
        let now = Instant::now();
        if now.duration_since(self.last_baselines) >= Duration::from_millis(200) {
            self.last_baselines = now;
            let revision = self.world.state.revision;
            let ids: Vec<ConnectionId> = self.connections.keys().copied().collect();
            for id in ids {
                let Some(player) = self.connections.get(&id).and_then(|c| c.player.as_ref()) else { continue };
                let player_id = player.id.clone();
                let due: Vec<String> = player.watched.iter().filter(|s| player.sent.get(*s).is_none_or(|r| *r != revision)).cloned().collect();
                for site in due {
                    if let Some(view) = self.world.view(&player_id, &site) {
                        self.send(id, ServerMessage::Baseline { site: site.clone(), view: Box::new(view) });
                    }
                    if let Some(p) = self.connections.get_mut(&id).and_then(|c| c.player.as_mut()) {
                        p.sent.insert(site, revision);
                    }
                }
                let pose = self.connections.get(&id).and_then(|c| c.player.as_ref()).and_then(|p| p.pose);
                if let Some(pose) = pose {
                    let crowd = self.world.crowd_near(pose.x, pose.z, goods::customers::CROWD_RADIUS_METRES);
                    let signature = goods::customers::crowd_signature(&crowd);
                    let changed = self.connections.get(&id).and_then(|c| c.player.as_ref()).is_some_and(|p| p.last_crowd != signature);
                    if changed {
                        self.send(id, ServerMessage::Crowd { crowd });
                        if let Some(p) = self.connections.get_mut(&id).and_then(|c| c.player.as_mut()) {
                            p.last_crowd = signature;
                        }
                    }
                }
            }
        }
        if now.duration_since(self.last_avatars) >= Duration::from_millis(100) {
            self.last_avatars = now;
            let avatars: Vec<AvatarState> = self.connections.values().filter_map(|c| c.player.as_ref()).filter_map(|p| p.pose.map(|pose| AvatarState { player: p.id.clone(), name: p.name.clone(), pose })).collect();
            self.broadcast(ServerMessage::Avatars { avatars });
            let employees = self.agents.iter().map(Agent::state).collect();
            self.broadcast(ServerMessage::Employees { employees });
        }
    }

    /// A clean stop: pending ticks and every pose are saved.
    pub fn shutdown(&mut self) {
        let ids: Vec<ConnectionId> = self.connections.keys().copied().collect();
        for id in ids {
            self.close(id);
        }
        self.commit_now();
    }
}

impl Host for WorldServer {
    fn state(&self) -> &Snapshot { &self.world.state }

    fn command(&mut self, actor: &str, command: Command) -> Outcome {
        self.next_request += 1;
        let request = format!("worker-{}-{}", self.world.state.revision, self.next_request);
        self.apply(actor, &request, command)
    }

    fn max_stack(&self, item: &str) -> i32 { self.world.max_stack(item) }
    fn requires_power(&self, kind: &str) -> bool { self.world.requires_power(kind) }
    fn requires_worker(&self, kind: &str) -> bool { self.world.requires_worker(kind) }
    fn selectable_recipes(&self, kind: &str) -> Vec<String> { self.world.selectable_recipes(kind) }
    fn recipe_inputs(&self, kind: &str) -> Vec<String> { self.world.recipe_inputs(kind) }

    fn recipe_making(&self, kind: &str, item: &str) -> Option<String> {
        self.content.recipes.values().find(|r| !r.is_sale() && r.station_kind == kind && r.output_item_id == item).map(|r| r.id.clone())
    }

    fn offer_of_site(&self, site: &str) -> Option<PropertyOffer> { self.offers.get(site).cloned() }

    fn record_script(&mut self, employee: &str, source: &str, running: bool, tasks: Option<&str>) -> Option<String> {
        let before = self.world.state.clone();
        match self.world.set_employee_script(employee, source, running, tasks) {
            Ok(false) => None,
            Ok(true) => match self.store.save(&self.world) {
                Ok(()) => None,
                Err(_) => {
                    self.world.replace_state(before);
                    Some("persistence-unavailable".into())
                }
            },
            Err(problem) => Some(problem.into()),
        }
    }

    fn record_pose(&mut self, employee: &str, x: f32, z: f32, yaw: f32) { self.world.set_employee_pose(employee, x, 0.0, z, yaw); }
}

/// A new world's starting restaurant comes pre-equipped with a register and a table inside its shell, once.
fn equip(world: &mut GoodsWorld, start: &PropertyOffer, content: &GameContent) {
    let has = |world: &GoodsWorld, kind: &str, id: &str| world.state.equipment.iter().any(|x| x.kind == kind || x.id == id);
    let mut counter = None;
    if let Some(definition) = content.equipment.get(goods::COUNTER_KIND) {
        if !has(world, goods::COUNTER_KIND, START_COUNTER_ID) {
            counter = place_in(world, start, definition, START_COUNTER_ID, counter_cells(start), None);
        }
    }
    if let Some(definition) = content.equipment.get(goods::TABLE_KIND) {
        if !has(world, goods::TABLE_KIND, START_TABLE_ID) {
            place_in(world, start, definition, START_TABLE_ID, table_cells(start), counter.as_ref());
        }
    }
}

fn interior(start: &PropertyOffer) -> (i32, i32, i32, i32) {
    (start.building_x + 1, start.building_z + 1, start.building_x + start.building_width - 2, start.building_z + start.building_depth - 2)
}

fn counter_cells(start: &PropertyOffer) -> Vec<(i32, i32)> {
    let (x0, z0, x1, z1) = interior(start);
    let centre = x0 + (x1 - x0 - 1) / 2;
    let mut cells = vec![];
    for z in z0 + 2..=z1 {
        for step in 0..=x1 - x0 {
            let x = centre + if step % 2 == 0 { step / 2 } else { -(step + 1) / 2 };
            if x >= x0 && x < x1 {
                cells.push((x, z));
            }
        }
    }
    cells
}

fn table_cells(start: &PropertyOffer) -> Vec<(i32, i32)> {
    let (x0, z0, x1, z1) = interior(start);
    vec![(x1 - 1, z1), (x0, z1), (x1 - 1, z0), (x0, z0)]
}

fn place_in(world: &mut GoodsWorld, start: &PropertyOffer, definition: &crate::content::EquipmentContent, id: &str, cells: Vec<(i32, i32)>, avoid: Option<&goods::Equipment>) -> Option<goods::Equipment> {
    let inward: HashSet<(i32, i32)> = start.doors.iter().chain(start.back_doors.iter()).map(|d| {
        (d.x.clamp(start.building_x + 1, start.building_x + start.building_width - 2), d.z.clamp(start.building_z + 1, start.building_z + start.building_depth - 2))
    }).collect();
    for (x, z) in cells {
        let piece = definition.placed(id, &start.site_id, x, z, 0);
        let blocked = (0..piece.width).any(|dx| (0..piece.depth).any(|dz| {
            inward.contains(&(x + dx, z + dz))
                || avoid.is_some_and(|a| x + dx >= a.cell_x && x + dx < a.cell_x + a.width && z + dz >= a.cell_z - 2 && z + dz < a.cell_z + a.depth)
        }));
        if blocked {
            continue;
        }
        if world.bootstrap_equipment(piece.clone()).is_ok() {
            return Some(piece);
        }
    }
    None
}

/// Every district and competitor the layout derives that the world does not have yet.
fn add_customers(world: &mut GoodsWorld, layout: &WorldLayout) -> Result<(), String> {
    let districts: HashSet<String> = world.state.districts.iter().map(|d| d.id.clone()).collect();
    let competitors: HashSet<String> = world.state.competitors.iter().map(|c| c.id.clone()).collect();
    for district in goods::layout_content::districts(layout) {
        if !districts.contains(&district.id) {
            world.bootstrap_district(district)?;
        } else {
            world.retune_district_dining(&district.id, district.dine_in_percent);
        }
    }
    for competitor in goods::layout_content::competitors(layout) {
        if !competitors.contains(&competitor.id) {
            world.bootstrap_competitor(competitor)?;
        }
    }
    Ok(())
}

/// A server running on its own thread; drop or `stop` to save and end it.
pub struct ServerHandle {
    pub inbound: Sender<Inbound>,
    stop: Sender<()>,
    thread: Option<std::thread::JoinHandle<()>>,
    next_connection: Arc<std::sync::atomic::AtomicU64>,
    pub start_site: String,
}

impl ServerHandle {
    /// Opens the world on a new thread; the returned handle accepts connections.
    pub fn start(options: ServerOptions, content: Arc<GameContent>) -> Result<Self, String> {
        let (ready_tx, ready_rx) = mpsc::channel();
        let (inbound_tx, inbound_rx) = mpsc::channel::<Inbound>();
        let (stop_tx, stop_rx) = mpsc::channel::<()>();
        let thread = std::thread::Builder::new()
            .name("food-factory-server".into())
            .spawn(move || {
                let mut server = match WorldServer::open(&options, content) {
                    Ok(server) => {
                        let _ = ready_tx.send(Ok(server.start_site().to_string()));
                        server
                    }
                    Err(error) => {
                        let _ = ready_tx.send(Err(error));
                        return;
                    }
                };
                run(&mut server, inbound_rx, stop_rx);
                server.shutdown();
            })
            .map_err(|e| e.to_string())?;
        let start_site = ready_rx.recv().map_err(|_| "The server thread ended while starting.".to_string())??;
        Ok(Self { inbound: inbound_tx, stop: stop_tx, thread: Some(thread), next_connection: Arc::new(std::sync::atomic::AtomicU64::new(1)), start_site })
    }

    /// A new in-process connection: messages in, messages out.
    pub fn connect(&self) -> (Sender<ClientMessage>, Receiver<ServerMessage>) {
        let id = self.next_connection.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        let (out_tx, out_rx) = mpsc::channel();
        let _ = self.inbound.send(Inbound::Open { connection: id, outbox: out_tx });
        let (client_tx, client_rx) = mpsc::channel::<ClientMessage>();
        let inbound = self.inbound.clone();
        std::thread::Builder::new()
            .name("food-factory-local-link".into())
            .spawn(move || {
                while let Ok(message) = client_rx.recv() {
                    if inbound.send(Inbound::Message { connection: id, message }).is_err() {
                        return;
                    }
                }
                let _ = inbound.send(Inbound::Closed { connection: id });
            })
            .ok();
        (client_tx, out_rx)
    }

    pub fn connection_ids(&self) -> Arc<std::sync::atomic::AtomicU64> { self.next_connection.clone() }

    pub fn stop(&mut self) {
        let _ = self.stop.send(());
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

impl Drop for ServerHandle {
    fn drop(&mut self) { self.stop(); }
}

fn run(server: &mut WorldServer, inbound: Receiver<Inbound>, stop: Receiver<()>) {
    let mut last = Instant::now();
    loop {
        if !matches!(stop.try_recv(), Err(TryRecvError::Empty)) {
            return;
        }
        loop {
            match inbound.try_recv() {
                Ok(message) => server.handle(message),
                Err(TryRecvError::Empty) => break,
                Err(TryRecvError::Disconnected) => return,
            }
        }
        let now = Instant::now();
        let dt = now.duration_since(last).as_secs_f32().min(0.25);
        last = now;
        server.frame(dt);
        std::thread::sleep(Duration::from_millis(15));
    }
}

pub fn default_save_directory(world: &str) -> PathBuf { crate::persistence::saves_root().join(world) }

pub fn world_exists(directory: &Path) -> bool { directory.join("world.db").exists() }

/// The first unused "world-N" name.
pub fn next_world_name() -> String {
    let existing: HashSet<String> = crate::persistence::saved_worlds().into_iter().map(|w| w.0).collect();
    (1..).map(|n| format!("world-{n}")).find(|n| !existing.contains(n)).unwrap_or_else(|| "world".into())
}

impl WorldServer {
    /// Runs a program on an employee as the employee screen's Run does (tools and tests).
    pub fn run_employee(&mut self, employee: &str, source: &str) {
        if !self.agents.iter().any(|a| a.id == employee) {
            if let Some(record) = self.world.state.employees.iter().find(|e| e.id == employee).cloned() {
                let agent = Agent::new(&record, self);
                self.agents.push(agent);
            }
        }
        self.with_agent(employee, |agent, host| agent.run(host, source, None));
    }

    pub fn employee_status(&self, employee: &str) -> Option<String> { self.agents.iter().find(|a| a.id == employee).map(|a| a.status.clone()) }
}

pub use employees::check_syntax;
