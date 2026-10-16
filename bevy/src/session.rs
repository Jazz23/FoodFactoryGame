// The client side of a session (Unity SessionRoot client half, ClientSiteSubscription and DrawnSites): hosting or joining,
// the message link to the server, the latest baseline of every watched site, other players and employees, and requests in
// flight. Presentation reads this resource; nothing here mutates the simulation, which only the server owns.
use std::collections::{BTreeMap, HashMap};
use std::sync::mpsc::{Receiver, Sender, TryRecvError};
use std::sync::{Arc, Mutex};

use bevy::prelude::*;

use food_factory_bevy::content::GameContent;
use food_factory_bevy::goods::{self, customers::CrowdView, Outcome, PropertyOffer, Snapshot};
use food_factory_bevy::net;
use food_factory_bevy::persistence::{self, ClientSettings};
use food_factory_bevy::protocol::{AvatarState, ClientMessage, Command, EmployeeState, Pose, ServerMessage, PROTOCOL_VERSION};
use food_factory_bevy::server::{self, ServerHandle, ServerOptions};
use food_factory_bevy::world::{layout::WorldLayout, roads::RoadNetwork};

pub struct SessionPlugin;

impl Plugin for SessionPlugin {
    fn build(&self, app: &mut App) {
        let content = Arc::new(GameContent::bundled().expect("bundled content"));
        let settings = ClientSettings::open(&persistence::data_root().join("Settings").join("settings.db"));
        app.insert_resource(Content(content))
            .insert_resource(Settings(Mutex::new(settings)))
            .init_resource::<Session>()
            .add_message::<SessionEvent>()
            .add_systems(PreUpdate, poll);
    }
}

#[derive(Resource, Clone)]
pub struct Content(pub Arc<GameContent>);

#[derive(Resource)]
pub struct Settings(pub Mutex<ClientSettings>);

/// Something the client reacts to once (presentation rebuilds, notices, sounds).
#[derive(Message, Clone, Debug)]
pub enum SessionEvent {
    Joined,
    Left(String),
    Outcome { request: String, outcome: Outcome },
    SiteChanged(String),
    Notice(String),
}

/// A request sent and not yet answered; previews of transfers read these.
#[derive(Clone, Debug)]
pub struct Pending {
    pub command: Command,
    pub sent: f64,
    pub accepted_revision: Option<i64>,
}

#[derive(Clone, Debug)]
pub struct EmployeeDetail {
    pub source: String,
    pub tasks: String,
    pub status: String,
}

/// The world as this client knows it.
pub struct Joined {
    pub player: String,
    pub name: String,
    pub world_id: String,
    pub site: String,
    pub layout: Arc<WorldLayout>,
    pub roads: Arc<RoadNetwork>,
    pub offers: HashMap<String, PropertyOffer>,
    pub offers_by_lot: HashMap<String, PropertyOffer>,
    pub views: BTreeMap<String, Snapshot>,
    pub avatars: Vec<AvatarState>,
    pub employees: Vec<EmployeeState>,
    pub details: HashMap<String, EmployeeDetail>,
    pub crowd: CrowdView,
    pub spawn: Pose,
    pub spawned: bool,
    pub wage_warning: i64,
    pub watched: Vec<String>,
}

impl Joined {
    pub fn view(&self) -> Option<&Snapshot> { self.views.get(&self.site) }
    pub fn view_of(&self, site: &str) -> Option<&Snapshot> { self.views.get(site) }
    pub fn company(&self) -> Option<&goods::Company> { self.view().and_then(|v| v.companies.first()) }
    pub fn cash(&self) -> i64 { self.company().map_or(0, |c| c.cash) }
    pub fn inventory(&self) -> String { goods::inventory_location_id(&self.player) }
    /// The sites of this client's company (from the property records every view carries).
    pub fn company_sites(&self) -> Vec<String> { self.company().map(|c| c.site_ids.clone()).unwrap_or_default() }
}

pub enum Link {
    None,
    Connecting { to: Sender<ClientMessage>, from: Mutex<Receiver<ServerMessage>>, host: Option<Hosted> },
    Joined { to: Sender<ClientMessage>, from: Mutex<Receiver<ServerMessage>>, host: Option<Hosted>, world: Box<Joined> },
}

/// A world this game hosts: the server thread and, unless singleplayer, its listener.
pub struct Hosted {
    pub server: ServerHandle,
    pub listener: Option<net::Listener>,
    pub world_name: String,
}

#[derive(Resource)]
pub struct Session {
    pub link: Link,
    pub status: String,
    pub pending: BTreeMap<String, Pending>,
    request_counter: u64,
    pub secret: String,
    pub display_name: String,
}

impl Default for Session {
    fn default() -> Self {
        let identity = persistence::data_root().join("Identity").join("identity.db");
        let secret = persistence::client_secret(&identity).unwrap_or_else(|_| "development-only-secret-0000".into());
        Self { link: Link::None, status: String::new(), pending: BTreeMap::new(), request_counter: 0, secret, display_name: "Player".into() }
    }
}

impl Session {
    pub fn joined(&self) -> Option<&Joined> {
        match &self.link {
            Link::Joined { world, .. } => Some(world),
            _ => None,
        }
    }

    pub fn joined_mut(&mut self) -> Option<&mut Joined> {
        match &mut self.link {
            Link::Joined { world, .. } => Some(world),
            _ => None,
        }
    }

    pub fn in_world(&self) -> bool { matches!(self.link, Link::Joined { .. }) }

    pub fn hosted(&self) -> Option<&Hosted> {
        match &self.link {
            Link::Connecting { host, .. } | Link::Joined { host, .. } => host.as_ref(),
            Link::None => None,
        }
    }

    fn sender(&self) -> Option<&Sender<ClientMessage>> {
        match &self.link {
            Link::Connecting { to, .. } | Link::Joined { to, .. } => Some(to),
            Link::None => None,
        }
    }

    pub fn send(&self, message: ClientMessage) {
        if let Some(to) = self.sender() {
            let _ = to.send(message);
        }
    }

    /// Sends a command under a new request ID and remembers it until answered; returns the ID.
    pub fn command(&mut self, command: Command, now: f64) -> String {
        self.request_counter += 1;
        let nanos = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_nanos();
        let request = format!("c{nanos:x}-{}", self.request_counter);
        self.pending.insert(request.clone(), Pending { command: command.clone(), sent: now, accepted_revision: None });
        self.send(ClientMessage::Command { request: request.clone(), command });
        request
    }

    /// Hosts a saved or new world and joins it as this player.
    pub fn host(&mut self, content: &Arc<GameContent>, world_name: &str, seed: &str, singleplayer: bool, address: &str) -> Result<(), String> {
        self.leave();
        let directory = server::default_save_directory(world_name);
        let options = ServerOptions {
            save_directory: directory,
            world_id: world_name.to_string(),
            seed_text: seed.to_string(),
            only_secret: if singleplayer { Some(self.secret.clone()) } else { None },
        };
        let server = ServerHandle::start(options, content.clone())?;
        let listener = if singleplayer { None } else { Some(net::Listener::start(address, server.inbound.clone(), server.connection_ids())?) };
        let (to, from) = server.connect();
        to.send(ClientMessage::Hello { version: PROTOCOL_VERSION, name: self.display_name.clone(), secret: self.secret.clone() }).map_err(|e| e.to_string())?;
        self.status = match &listener {
            Some(l) => format!("Hosting {world_name} on {}", l.address),
            None => format!("Loading {world_name}..."),
        };
        self.link = Link::Connecting { to, from: Mutex::new(from), host: Some(Hosted { server, listener, world_name: world_name.to_string() }) };
        Ok(())
    }

    /// Joins a server at an address.
    pub fn join(&mut self, address: &str) {
        self.leave();
        let (to, from) = net::connect(address);
        let _ = to.send(ClientMessage::Hello { version: PROTOCOL_VERSION, name: self.display_name.clone(), secret: self.secret.clone() });
        self.status = format!("Connecting to {address}...");
        self.link = Link::Connecting { to, from: Mutex::new(from), host: None };
    }

    /// Leaves the session; a hosted world saves and stops.
    pub fn leave(&mut self) {
        self.send(ClientMessage::Leave);
        let link = std::mem::replace(&mut self.link, Link::None);
        let host = match link {
            Link::Connecting { host, .. } | Link::Joined { host, .. } => host,
            Link::None => None,
        };
        if let Some(mut hosted) = host {
            drop(hosted.listener.take());
            hosted.server.stop();
        }
        self.pending.clear();
    }
}

fn poll(time: Res<Time>, mut session: ResMut<Session>, content: Res<Content>, mut events: MessageWriter<SessionEvent>) {
    let now = time.elapsed_secs_f64();
    let session = &mut *session;
    let mut messages = vec![];
    let mut disconnected = false;
    if let Link::Connecting { from, .. } | Link::Joined { from, .. } = &session.link {
        let from = from.lock().unwrap_or_else(|e| e.into_inner());
        loop {
            match from.try_recv() {
                Ok(message) => messages.push(message),
                Err(TryRecvError::Empty) => break,
                Err(TryRecvError::Disconnected) => {
                    disconnected = true;
                    break;
                }
            }
        }
    }
    for message in messages {
        match message {
            ServerMessage::Welcome { player, name, site, world_id, requested_seed, seed, layout_hash, spawn, wage_warning } => {
                let (attempt, hash) = layout_hash.split_once(':').map(|(a, h)| (a.parse().unwrap_or(0), h.to_string())).unwrap_or((0, layout_hash.clone()));
                let layout = match &session.link {
                    Link::Connecting { host: Some(_), .. } | Link::Connecting { host: None, .. } => server::client_layout(&requested_seed, seed, attempt),
                    Link::Joined { world, .. } => (*world.layout).clone(),
                    Link::None => continue,
                };
                let digest = {
                    use sha2::Digest;
                    format!("{:x}", sha2::Sha256::digest(serde_json::to_vec(&layout).unwrap_or_default()))
                };
                if digest != hash {
                    warn!("[Session] The regenerated world layout differs from the server's ({digest} != {hash}).");
                }
                let roads = Arc::new(RoadNetwork::new(&layout).unwrap_or_default());
                let offers: Vec<PropertyOffer> = goods::layout_content::property_offers(&layout);
                let link = std::mem::replace(&mut session.link, Link::None);
                let (to, from, host) = match link {
                    Link::Connecting { to, from, host } | Link::Joined { to, from, host, .. } => (to, from, host),
                    Link::None => continue,
                };
                let world = Joined {
                    player, name, world_id, site: site.clone(), layout: Arc::new(layout), roads,
                    offers: offers.iter().map(|o| (o.site_id.clone(), o.clone())).collect(),
                    offers_by_lot: offers.into_iter().map(|o| (o.lot_id.clone(), o)).collect(),
                    views: BTreeMap::new(), avatars: vec![], employees: vec![], details: HashMap::new(), crowd: CrowdView::default(), spawn, spawned: false,
                    wage_warning, watched: vec![site],
                };
                session.status = String::new();
                session.link = Link::Joined { to, from, host, world: Box::new(world) };
                events.write(SessionEvent::Joined);
            }
            ServerMessage::Rejected { reason } => {
                session.status = reason.clone();
                session.leave();
                events.write(SessionEvent::Left(reason));
            }
            ServerMessage::Baseline { site, view } => {
                if let Some(world) = session.joined_mut() {
                    world.views.insert(site, *view);
                }
                let revision = session.joined().and_then(|w| w.view()).map_or(0, |v| v.revision);
                session.pending.retain(|_, p| p.accepted_revision.is_none_or(|r| r > revision));
            }
            ServerMessage::Outcome { request, outcome } => {
                if let Some(pending) = session.pending.get_mut(&request) {
                    if outcome.accepted {
                        pending.accepted_revision = Some(outcome.revision);
                    } else {
                        session.pending.remove(&request);
                    }
                }
                events.write(SessionEvent::Outcome { request, outcome });
            }
            ServerMessage::CurrentSite { site } => {
                if let Some(world) = session.joined_mut() {
                    world.site = site.clone();
                }
                events.write(SessionEvent::SiteChanged(site));
            }
            ServerMessage::Avatars { avatars } => {
                if let Some(world) = session.joined_mut() {
                    world.avatars = avatars;
                }
            }
            ServerMessage::Employees { employees } => {
                if let Some(world) = session.joined_mut() {
                    world.employees = employees;
                }
            }
            ServerMessage::EmployeeDetail { employee, source, tasks, status } => {
                if let Some(world) = session.joined_mut() {
                    world.details.insert(employee, EmployeeDetail { source, tasks, status });
                }
            }
            ServerMessage::Crowd { crowd } => {
                if let Some(world) = session.joined_mut() {
                    world.crowd = crowd;
                }
            }
            ServerMessage::Notice { text } => {
                events.write(SessionEvent::Notice(text));
            }
        }
    }
    // Requests older than 10 s are dropped from previews.
    session.pending.retain(|_, p| now - p.sent < 10.0);
    if disconnected && session.in_world() {
        session.leave();
        session.status = "Disconnected from the server.".into();
        events.write(SessionEvent::Left("Disconnected from the server.".into()));
    }
    let _ = &content;
}

/// The baseline-derived wait for a request: true while any request is unanswered.
pub fn busy(session: &Session) -> bool { session.pending.values().any(|p| p.accepted_revision.is_none()) }

pub fn money(cents: i64) -> String {
    let negative = cents < 0;
    let cents = cents.unsigned_abs();
    let dollars = cents / 100;
    let mut text = dollars.to_string();
    let mut grouped = String::new();
    while text.len() > 3 {
        let split = text.len() - 3;
        grouped = format!(",{}{grouped}", &text[split..]);
        text.truncate(split);
    }
    format!("{}${text}{grouped}.{:02}", if negative { "-" } else { "" }, cents % 100)
}
