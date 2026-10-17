// Messages between a client and the authoritative server (the port of Unity's GoodsNetworkBridge RPCs, the session
// authenticator and the avatar/employee replication). Clients request actions; the server validates every one against its own
// state and answers with an outcome, and pushes revisioned baselines of the sites a client watches. Newline-free JSON frames.
use serde::{Deserialize, Serialize};

use crate::goods::{buildings::ShellOrder, customers::CrowdView, equipment::FurnishOrder, Outcome, Snapshot};

pub const PROTOCOL_VERSION: u32 = 1;

/// Every player action the server accepts. The actor is always the connection's authenticated player.
#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum Command {
    Transfer { lot: String, destination: String, quantity: i32 },
    /// Destroys goods from the player's own carried inventory.
    Discard { lot: String, quantity: i32 },
    PickUp { equipment: String },
    Place { equipment: String, x: i32, z: i32, rotation: i32, level: i32 },
    Give { equipment: String, employee: String },
    PlaceBelt { site: String, x: i32, z: i32, direction: i32, level: i32 },
    PlaceLift { site: String, x: i32, z: i32, direction: i32, level: i32, lift: i32 },
    RemoveBelt { belt: String },
    PlaceOnBelt { lot: String, belt: String },
    TakeFromBelt { lot: String },
    Buy { site: String, offer: String },
    BuyAndPlace { order: FurnishOrder },
    Sell { equipment: String },
    Staff { register: String, staff: String },
    SelectRecipe { equipment: String, recipe: String },
    SetPower { equipment: String, on: bool },
    StartJob { station: String, recipe: String },
    AddFloor { building: String, x: i32, z: i32 },
    OrderShell { order: ShellOrder },
    CreateRoute { pickup: String, dropoff: String, items: Vec<String> },
    SetRoute { route: String, pickup: String, dropoff: String, items: Vec<String> },
    DeleteRoute { route: String },
    AssignTruck { truck: String, route: String },
    SellTruck { truck: String },
    BuyProperty { paying_site: String, lot: String },
    RenameSite { site: String, name: String },
    /// The server checks its own copy of the avatar's position against the lot.
    EnterSite { site: String },
    /// The new employee stands beside the hiring avatar (the server's copy of its pose).
    Hire,
    Fire { employee: String },
    OrderFromFarm { farm: String, offer: String, packs: i32, dock: String, truck: String },
    ScheduleFarmOrder { farm: String, offer: String, packs: i32, dock: String, truck: String, every_hours: i32 },
    CancelFarmSchedule { schedule: String },
}

/// An avatar's pose and the motion its animation is read from.
#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq)]
pub struct Pose {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub yaw: f32,
}

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
pub struct AvatarState {
    pub player: String,
    pub name: String,
    pub pose: Pose,
}

/// What every peer draws of an employee; script text travels only on request (EmployeeDetail).
#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
pub struct EmployeeState {
    pub id: String,
    pub site: String,
    pub name: String,
    pub pose: Pose,
    pub carrying: bool,
    pub status: String,
    pub unpaid: bool,
    pub running: bool,
}

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ClientMessage {
    Hello { version: u32, name: String, secret: String },
    /// The sites whose baselines this client wants (its current site first).
    Watch { sites: Vec<String> },
    Command { request: String, command: Command },
    Pose { pose: Pose },
    EmployeeRun { employee: String, source: String, tasks: Option<String> },
    EmployeeStop { employee: String },
    EmployeeRename { employee: String, name: String },
    EmployeeDetail { employee: String },
    SetWageWarning { cents: i64 },
    Leave,
}

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
#[serde(tag = "type", rename_all = "snake_case")]
pub enum ServerMessage {
    /// Admission: the player's identity and current site, and the world's layout seed (clients regenerate the layout
    /// deterministically and check its hash).
    Welcome { player: String, name: String, site: String, world_id: String, requested_seed: String, seed: u64, layout_hash: String, spawn: Pose, wage_warning: i64 },
    Rejected { reason: String },
    Baseline { site: String, view: Box<Snapshot> },
    Outcome { request: String, outcome: Outcome },
    /// The player's current site changed (after entering a lot).
    CurrentSite { site: String },
    Avatars { avatars: Vec<AvatarState> },
    Employees { employees: Vec<EmployeeState> },
    EmployeeDetail { employee: String, source: String, tasks: String, status: String },
    Crowd { crowd: CrowdView },
    Notice { text: String },
}

/// Writes one frame: JSON on one line.
pub fn write_frame<W: std::io::Write, T: Serialize>(writer: &mut W, message: &T) -> std::io::Result<()> {
    let mut text = serde_json::to_vec(message).map_err(std::io::Error::other)?;
    text.push(b'\n');
    writer.write_all(&text)?;
    writer.flush()
}
