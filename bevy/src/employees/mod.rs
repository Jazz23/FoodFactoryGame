// Scriptable employees on the server (Unity EmployeeWorker and EmployeeScript). A granted player sends a Lua program; only the
// server runs it (Luau, sandboxed: no file, OS or loading access), as a coroutine stepped every server frame. Blocking calls
// (walking, waiting, moving goods) start a Rust operation and yield until it ends, so a script reads top to bottom while the
// world keeps running; every resume has an interrupt budget, so `while true do end` only stalls its own employee. Every goods
// or machine action goes through the same validated, durable commands a player's does, with the employee as actor. The
// employee walks on its site's cell grid (navigation.rs) in site metres; its pose is recorded in the world and saved with the
// next commit. An unpaid employee stops wherever its script is, walks outside and restarts its script from the top once paid.
pub mod navigation;
pub mod tasks;

use std::cell::RefCell;
use std::collections::HashSet;
use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::Arc;

use mlua::thread::ThreadStatus;
use mlua::{Lua, MultiValue, Table, Thread, Value, VmState};

use crate::goods::{self, rules, Equipment, EquipmentState, JobState, Outcome, PropertyOffer, Snapshot, COUNTER_KIND};
use crate::protocol::{Command, EmployeeState, Pose};
use navigation::{find_path, Area};

pub const MAX_SOURCE_LENGTH: usize = 16000;
/// Interrupts (function calls and loop back-edges) per resume before a forced yield.
const INTERRUPT_BUDGET: u32 = 4000;
const STAND_TOLERANCE: f32 = 0.2;
const REACH: f32 = 1.3;
const WALK_TIMEOUT_SECONDS: f64 = 60.0;
const WALK_SPEED: f32 = 3.5;
const WORK_START_SECONDS: f64 = 2.0;
const WORK_LIMIT_SECONDS: f64 = 300.0;
const PATH_LIMIT: usize = 20000;

/// What the server offers an employee: the world to read, and commands applied with the employee as actor.
pub trait Host {
    fn state(&self) -> &Snapshot;
    fn command(&mut self, actor: &str, command: Command) -> Outcome;
    fn max_stack(&self, item: &str) -> i32;
    fn requires_power(&self, kind: &str) -> bool;
    fn requires_worker(&self, kind: &str) -> bool;
    fn selectable_recipes(&self, kind: &str) -> Vec<String>;
    fn recipe_inputs(&self, kind: &str) -> Vec<String>;
    /// The goods recipe of a kind that makes an item.
    fn recipe_making(&self, kind: &str, item: &str) -> Option<String>;
    fn offer_of_site(&self, site: &str) -> Option<PropertyOffer>;
    /// Saves the assignment; None when saved, otherwise the reason.
    fn record_script(&mut self, employee: &str, source: &str, running: bool, tasks: Option<&str>) -> Option<String>;
    fn record_pose(&mut self, employee: &str, x: f32, z: f32, yaw: f32);
}

/// A Lua value as an operation reads it, copied out of the interpreter.
#[derive(Clone, Debug, PartialEq)]
enum Arg {
    Nil,
    Bool(bool),
    Number(f64),
    Text(String),
    Table { list: Vec<Arg>, x: Option<f64>, z: Option<f64>, except: Option<Box<Arg>> },
}

impl Arg {
    fn from_lua(value: &Value, depth: u32) -> Arg {
        match value {
            Value::Nil => Arg::Nil,
            Value::Boolean(b) => Arg::Bool(*b),
            Value::Integer(i) => Arg::Number(*i as f64),
            Value::Number(n) => Arg::Number(*n),
            Value::String(s) => Arg::Text(s.to_string_lossy().to_string()),
            Value::Table(t) if depth < 4 => {
                let mut list = vec![];
                for index in 1..=256 {
                    match t.raw_get::<Value>(index) {
                        Ok(Value::Nil) | Err(_) => break,
                        Ok(v) => list.push(Arg::from_lua(&v, depth + 1)),
                    }
                }
                let number = |key: &str| match t.raw_get::<Value>(key) {
                    Ok(Value::Integer(i)) => Some(i as f64),
                    Ok(Value::Number(n)) => Some(n),
                    _ => None,
                };
                let except = match t.raw_get::<Value>("except") {
                    Ok(Value::Nil) | Err(_) => None,
                    Ok(v) => Some(Box::new(Arg::from_lua(&v, depth + 1))),
                };
                Arg::Table { list, x: number("x"), z: number("z"), except }
            }
            other => Arg::Text(format!("{other:?}")),
        }
    }

    fn text(&self) -> Option<String> {
        match self {
            Arg::Nil => None,
            Arg::Text(t) => Some(t.clone()),
            Arg::Number(n) => Some(if n.fract() == 0.0 { format!("{}", *n as i64) } else { n.to_string() }),
            Arg::Bool(b) => Some(b.to_string()),
            Arg::Table { .. } => Some("table".into()),
        }
    }

    fn number(&self) -> Option<f64> {
        match self {
            Arg::Number(n) => Some(*n),
            Arg::Text(t) => t.trim().parse().ok(),
            _ => None,
        }
    }

    /// A cell {x, z} or {x = .., z = ..}.
    fn cell(&self) -> Option<(i32, i32)> {
        let Arg::Table { list, x, z, .. } = self else { return None };
        match (list.first().and_then(Arg::number), list.get(1).and_then(Arg::number)) {
            (Some(a), Some(b)) => Some((a.round() as i32, b.round() as i32)),
            _ => match (x, z) {
                (Some(a), Some(b)) => Some((a.round() as i32, b.round() as i32)),
                _ => None,
            },
        }
    }

    fn is_nil(&self) -> bool { matches!(self, Arg::Nil) }
}

fn arg(args: &[Arg], index: usize) -> &Arg { args.get(index).unwrap_or(&Arg::Nil) }

/// A cell at args[index], or two numbers at index and index + 1; the index after it.
fn cell_at(args: &[Arg], index: usize) -> Option<((i32, i32), usize)> {
    if let Some(cell) = arg(args, index).cell() {
        return Some((cell, index + 1));
    }
    match (arg(args, index), arg(args, index + 1)) {
        (Arg::Number(x), Arg::Number(z)) => Some(((x.round() as i32, z.round() as i32), index + 2)),
        _ => None,
    }
}

fn optional_integer(args: &[Arg], index: usize, fallback: i32) -> Result<i32, String> {
    match arg(args, index) {
        Arg::Nil => Ok(fallback),
        value => value.number().map(|n| n.round() as i32).ok_or_else(|| format!("argument {} must be a number", index + 1)),
    }
}

fn optional_amount(args: &[Arg], index: usize) -> Result<i32, String> {
    match arg(args, index) {
        Arg::Nil => Ok(i32::MAX),
        value => value.number().map(|n| n.floor().clamp(0.0, i32::MAX as f64) as i32).ok_or_else(|| "amount must be a number".to_string()),
    }
}

/// Which items a goods call may move: nil (any), an item ID, a list, or {except = {...}}.
#[derive(Clone, Debug, Default)]
struct ItemFilter {
    only: Option<HashSet<String>>,
    except: HashSet<String>,
}

impl ItemFilter {
    fn from(value: &Arg) -> Self {
        match value {
            Arg::Nil => Self::default(),
            Arg::Table { list, except, .. } => {
                let listed = names(&Arg::Table { list: list.clone(), x: None, z: None, except: None });
                let except_names = except.as_deref().map(names).unwrap_or_default();
                let only = if !listed.is_empty() || except.is_none() { Some(listed) } else { None };
                Self { only, except: except_names }
            }
            other => Self { only: Some(other.text().into_iter().collect()), except: HashSet::new() },
        }
    }

    fn any(&self) -> bool { self.only.is_none() && self.except.is_empty() }

    fn allows(&self, item: &str) -> bool { self.only.as_ref().is_none_or(|o| o.contains(item)) && !self.except.contains(item) }

    fn label(&self) -> String {
        let sorted = |set: &HashSet<String>| {
            let mut v: Vec<&String> = set.iter().collect();
            v.sort();
            v.into_iter().cloned().collect::<Vec<_>>().join("/")
        };
        match &self.only {
            Some(only) if only.is_empty() => "nothing".into(),
            Some(only) => sorted(only),
            None if !self.except.is_empty() => format!("goods except {}", sorted(&self.except)),
            None => "goods".into(),
        }
    }
}

/// The strings in a Lua list (a lone string counts as a list of one).
fn names(value: &Arg) -> HashSet<String> {
    match value {
        Arg::Nil => HashSet::new(),
        Arg::Table { list, .. } => list.iter().filter_map(Arg::text).collect(),
        other => other.text().into_iter().collect(),
    }
}

/// A Lua call's answer.
#[derive(Clone, Debug, PartialEq)]
enum Answer {
    Nil,
    Bool(bool),
    Number(f64),
    Text(String),
}

fn answer_value(lua: &Lua, answer: &Answer) -> mlua::Result<Value> {
    Ok(match answer {
        Answer::Nil => Value::Nil,
        Answer::Bool(b) => Value::Boolean(*b),
        Answer::Number(n) => Value::Number(*n),
        Answer::Text(t) => Value::String(lua.create_string(t)?),
    })
}

/// A place resolved from a script's text.
#[derive(Clone, Debug, Default)]
struct Place {
    name: String,
    problem: Option<String>,
    area: Option<Area>,
    /// A cell to stand on, rather than a thing to stand beside.
    stand: Option<(f32, f32)>,
    equipment: Option<String>,
    take_from: Vec<String>,
    put_into: Option<String>,
}

impl Place {
    fn problem(name: &str, problem: String) -> Self { Place { name: name.to_string(), problem: Some(problem), ..Default::default() } }
}

#[derive(Clone, Debug)]
enum Then {
    MoveTo,
    Take { filter: ItemFilter, amount: i32 },
    Put { filter: ItemFilter, amount: i32 },
    Place { machine: String, x: i32, z: i32, rotation: i32 },
    PickUp,
    PlaceBelt { x: i32, z: i32, direction: i32 },
    Power { on: Option<bool> },
    SetRecipe { recipe: String },
    WorkRegister,
    WorkStation { recipe: Option<String> },
}

#[derive(Clone, Debug)]
enum Op {
    Walk { place: Place, then: Then, deadline: f64, next_path: f64, path: Vec<(f32, f32)>, pathed: bool },
    Path { waypoints: Vec<Arg>, index: usize, walking: Option<(Place, f64, f64, Vec<(f32, f32)>, bool)> },
    Wait { until: f64 },
    WorkWait { station: String, name: String, start_by: f64, give_up: f64, job: Option<String> },
}

const OPERATIONS: [&str; 13] = ["move_to", "path", "take", "put", "wait", "place", "pick_up", "place_belt", "turn_on", "turn_off", "toggle", "set_recipe", "work"];
const QUERIES: [&str; 9] = ["count", "find", "carrying", "holding", "position", "accepts", "room", "is_on", "say"];

fn prelude() -> String {
    let mut text = String::from("local api = ...\nlocal yield = coroutine.yield\nlocal function await(name, ...)\n  api.begin(name, ...)\n  while api.busy() do yield() end\n  return api.result()\nend\n");
    for name in OPERATIONS {
        text.push_str(&format!("function {name}(...) return await(\"{name}\", ...) end\n"));
    }
    for name in QUERIES {
        text.push_str(&format!("function {name}(...) return api.{name}(...) end\n"));
    }
    text.push_str("print = say\n");
    text
}

/// One employee's interpreter.
struct Script {
    lua: Lua,
    thread: Thread,
    api: Table,
    budget: Arc<AtomicU32>,
}

impl Script {
    fn new(source: &str) -> Result<Self, String> {
        if source.len() > MAX_SOURCE_LENGTH {
            return Err(format!("A script is at most {MAX_SOURCE_LENGTH} characters."));
        }
        let lua = Lua::new();
        let budget = Arc::new(AtomicU32::new(0));
        let counter = budget.clone();
        lua.set_interrupt(move |_| {
            if counter.fetch_add(1, Ordering::Relaxed) >= INTERRUPT_BUDGET {
                counter.store(0, Ordering::Relaxed);
                return Ok(VmState::Yield);
            }
            Ok(VmState::Continue)
        });
        let error = |e: mlua::Error| tidy(&e.to_string());
        let globals = lua.globals();
        for name in ["loadstring", "getfenv", "setfenv", "require", "collectgarbage", "newproxy", "gcinfo"] {
            globals.raw_set(name, Value::Nil).map_err(error)?;
        }
        let api = lua.create_table().map_err(error)?;
        lua.load(prelude()).set_name("prelude").call::<()>(api.clone()).map_err(error)?;
        let main = lua.load(source).set_name("script").into_function().map_err(error)?;
        let thread = lua.create_thread(main).map_err(error)?;
        Ok(Self { lua, thread, api, budget })
    }
}

/// Lua errors read as "script:12: message"; drop the stack traceback.
fn tidy(message: &str) -> String {
    let first = message.lines().next().unwrap_or(message);
    first.trim_start_matches("runtime error: ").trim_start_matches("syntax error: ").replace("[string \"script\"]", "script").to_string()
}

/// Parses a program without running it: None when it would load, or the error with its line.
pub fn check_syntax(source: &str) -> Option<String> { Script::new(source).err() }

/// Local authoring check with typical stand-in results, no Host and no world access. A bounded prefix is not proof that a
/// program will succeed against real stock/geometry; errors retain script line numbers. Infinite loops finish at the budget.
pub fn dry_run(source: &str) -> Result<String, String> {
    let script = Script::new(source)?;
    script.lua.set_memory_limit(8 * 1024 * 1024).map_err(|e| tidy(&e.to_string()))?;
    let calls = Arc::new(AtomicU32::new(0));
    let count = calls.clone();
    script.lua.set_interrupt(move |_| {
        if count.fetch_add(1, Ordering::Relaxed) >= 400 { Ok(VmState::Yield) } else { Ok(VmState::Continue) }
    });
    let result: mlua::Result<()> = (|| {
        let globals = script.lua.globals();
        for name in OPERATIONS {
            globals.set(name, script.lua.create_function(move |_, _: MultiValue| {
                Ok(if matches!(name, "take" | "put") { Value::Integer(1) } else { Value::Boolean(true) })
            })?)?;
        }
        for name in QUERIES {
            globals.set(name, script.lua.create_function(move |lua, _: MultiValue| {
                Ok(match name {
                    "count" | "carrying" | "room" => Value::Integer(1),
                    "is_on" => Value::Boolean(true),
                    "find" | "holding" | "accepts" => { let table = lua.create_table()?; table.set(1, "example")?; Value::Table(table) },
                    "position" => { let table = lua.create_table()?; table.set(1, 0)?; table.set(2, 0)?; table.set("x", 0)?; table.set("z", 0)?; Value::Table(table) },
                    _ => Value::Nil,
                })
            })?)?;
        }
        globals.set("print", globals.get::<mlua::Function>("say")?)?;
        script.thread.resume::<MultiValue>(())?;
        Ok(())
    })();
    result.map_err(|e| tidy(&e.to_string()))?;
    Ok(if calls.load(Ordering::Relaxed) >= 400 { "Checked a bounded prefix (400 interrupts); no error found." } else { "Dry run completed; no error found." }.into())
}

/// What a `begin` call asked for, kept until the step that turns it into an operation.
#[derive(Clone, Debug)]
struct Request {
    name: String,
    args: Vec<Arg>,
}

pub struct Agent {
    pub id: String,
    pub site: String,
    pub name: String,
    pub x: f32,
    pub z: f32,
    pub yaw: f32,
    pub speed: f32,
    pub carrying: bool,
    pub status: String,
    pub source: String,
    pub tasks: String,
    pub unpaid: bool,
    script: Option<Script>,
    op: Option<Op>,
    request: Option<Request>,
    result: Vec<Answer>,
    last_message: Option<String>,
    resume_script: bool,
    outside: Option<(f32, f32)>,
    post: Option<((f32, f32), (f32, f32))>,
    post_key: String,
    post_path: Vec<(f32, f32)>,
    clock: f64,
    next_carry_check: f64,
    next_pose_record: f64,
    next_pay_check: f64,
    next_post_check: f64,
    next_outside_path: f64,
    outside_path: Vec<(f32, f32)>,
    recorded: (f32, f32, f32),
}

fn hands(id: &str) -> String { goods::inventory_location_id(id) }

fn flat((ax, az): (f32, f32), (bx, bz): (f32, f32)) -> f32 { ((ax - bx).powi(2) + (az - bz).powi(2)).sqrt() }

/// Unity yaw (degrees, clockwise from +Z seen from above) of a direction in site metres.
fn yaw_of(dx: f32, dz: f32) -> f32 { dx.atan2(dz).to_degrees() }

impl Agent {
    /// From the saved record; a running script restarts from its first line (interpreter state is not saved).
    pub fn new(record: &goods::Employee, host: &mut dyn Host) -> Self {
        let mut agent = Agent {
            id: record.id.clone(), site: record.site_id.clone(), name: if record.name.trim().is_empty() { record.id.clone() } else { record.name.clone() },
            x: record.x, z: record.z, yaw: record.yaw, speed: 0.0, carrying: false, status: "Idle".into(), source: record.script.clone(), tasks: record.tasks.clone(),
            unpaid: false, script: None, op: None, request: None, result: vec![], last_message: None, resume_script: false, outside: None, post: None,
            post_key: String::new(), post_path: vec![], clock: 0.0, next_carry_check: 0.0, next_pose_record: 0.0, next_pay_check: 0.0, next_post_check: 0.0,
            next_outside_path: 0.0, outside_path: vec![], recorded: (record.x, record.z, record.yaw),
        };
        agent.place_on_site(host);
        if record.unpaid {
            agent.resume_script = record.script_running && !record.script.is_empty();
            agent.go_unpaid(host);
        } else if record.script_running && !record.script.is_empty() {
            agent.begin(host, &record.script.clone(), true, None);
        }
        agent
    }

    pub fn state(&self) -> EmployeeState {
        EmployeeState {
            id: self.id.clone(), site: self.site.clone(), name: self.name.clone(), pose: Pose { x: self.x, y: 0.0, z: self.z, yaw: self.yaw },
            carrying: self.carrying, status: self.status.clone(), unpaid: self.unpaid, running: self.script.is_some() || self.resume_script,
        }
    }

    fn walk_grid(&self, host: &dyn Host) -> Option<rules::Walk> { rules::walkable(host.state(), &self.site, None, false) }

    /// Off the grid (a new hire beside a player, a shell change around it): to the nearest open cell of its site.
    fn place_on_site(&mut self, host: &dyn Host) {
        let Some(walk) = self.walk_grid(host) else { return };
        let (cx, cz) = navigation::cell_of(self.x, self.z);
        if walk.get(cx, cz) {
            return;
        }
        let mut best: Option<((i32, i32), f32)> = None;
        for x in 0..walk.width {
            for z in 0..walk.depth {
                if walk.get(x, z) {
                    let distance = flat((x as f32 + 0.5, z as f32 + 0.5), (self.x, self.z));
                    if best.is_none_or(|b| distance < b.1) {
                        best = Some(((x, z), distance));
                    }
                }
            }
        }
        if let Some(((x, z), _)) = best {
            self.x = x as f32 + 0.5;
            self.z = z as f32 + 0.5;
        }
    }

    /// Run a program, replacing any running one; goods in the employee's hands stay there.
    pub fn run(&mut self, host: &mut dyn Host, source: &str, tasks: Option<&str>) {
        self.halt(host);
        self.begin(host, source, false, tasks);
        if !self.unpaid || self.script.is_none() {
            return;
        }
        self.script = None;
        self.resume_script = true;
        self.status = "Unpaid: will run the script once the company can pay its wage".into();
    }

    pub fn stop(&mut self, host: &mut dyn Host) {
        let was_running = self.script.is_some() || self.resume_script;
        self.resume_script = false;
        self.halt(host);
        if was_running {
            let source = self.source.clone();
            host.record_script(&self.id, &source, false, None);
        }
        self.status = if self.unpaid { "Stopped (unpaid: waiting outside)".into() } else { "Stopped".into() };
    }

    fn begin(&mut self, host: &mut dyn Host, source: &str, restarted: bool, tasks: Option<&str>) {
        let script = match Script::new(source) {
            Ok(script) => script,
            Err(error) => {
                self.status = format!("Error: {error}");
                if restarted {
                    host.record_script(&self.id, source, false, None);
                }
                return;
            }
        };
        if let Some(problem) = host.record_script(&self.id, source, true, tasks) {
            self.status = format!("Error: could not save the script ({problem})");
            return;
        }
        self.last_message = None;
        if restarted {
            self.leave_stations(host);
        }
        self.script = Some(script);
        self.op = None;
        self.request = None;
        self.result.clear();
        self.source = source.to_string();
        if let Some(tasks) = tasks {
            self.tasks = tasks.to_string();
        }
        self.status = if restarted { "Running (restarted after the server restarted)".into() } else { "Running".into() };
    }

    fn halt(&mut self, host: &mut dyn Host) {
        self.script = None;
        self.op = None;
        self.request = None;
        self.leave_stations(host);
    }

    /// An employee works a worker-bound station only while its work() call stands there.
    fn leave_stations(&mut self, host: &mut dyn Host) {
        let stations: Vec<String> = host.state().equipment.iter().filter(|x| x.staff_id == self.id && host.requires_worker(&x.kind)).map(|x| x.id.clone()).collect();
        for station in stations {
            host.command(&self.id, Command::Staff { register: station, staff: String::new() });
        }
    }

    /// One server frame of `dt` seconds.
    pub fn update(&mut self, host: &mut dyn Host, dt: f32) {
        self.clock += dt as f64;
        let before = (self.x, self.z);
        if self.clock >= self.next_carry_check {
            self.next_carry_check = self.clock + 0.5;
            let hands = hands(&self.id);
            self.carrying = host.state().lots.iter().any(|l| l.location_id == hands);
        }
        if self.clock >= self.next_pay_check {
            self.next_pay_check = self.clock + 0.5;
            let unpaid = host.state().employees.iter().any(|e| e.id == self.id && e.unpaid);
            if unpaid && !self.unpaid {
                self.resume_script = self.script.is_some();
                self.go_unpaid(host);
            } else if !unpaid && self.unpaid {
                self.go_paid(host);
            }
        }
        if self.unpaid {
            self.walk_outside(host, dt);
        } else if self.script.is_none() {
            self.hold_post(host, dt);
        } else {
            self.step_script(host, dt);
        }
        let moved = flat(before, (self.x, self.z));
        let measured = if dt > 0.0 { moved / dt } else { 0.0 };
        self.speed += (measured - self.speed) * (1.0 - (-10.0 * dt).exp());
        self.record_pose(host);
    }

    fn record_pose(&mut self, host: &mut dyn Host) {
        if self.clock < self.next_pose_record {
            return;
        }
        self.next_pose_record = self.clock + 1.0;
        let (x, z, yaw) = self.recorded;
        if (self.x - x).powi(2) + (self.z - z).powi(2) < 0.0025 && (self.yaw - yaw).abs() < 2.0 {
            return;
        }
        self.recorded = (self.x, self.z, self.yaw);
        host.record_pose(&self.id, self.x, self.z, self.yaw);
    }

    fn go_unpaid(&mut self, host: &mut dyn Host) {
        self.halt(host);
        self.unpaid = true;
        self.outside = self.outside_point(host);
        self.next_outside_path = 0.0;
        self.status = "Unpaid: waiting outside until the company can pay its wage".into();
    }

    fn go_paid(&mut self, host: &mut dyn Host) {
        self.unpaid = false;
        self.outside = None;
        self.outside_path.clear();
        let resume = self.resume_script && !self.source.is_empty();
        self.resume_script = false;
        if !resume {
            self.status = "Idle (paid again)".into();
            return;
        }
        let source = self.source.clone();
        self.begin(host, &source, false, None);
        if self.script.is_some() {
            self.status = "Running (paid again, restarted from the top)".into();
        }
    }

    /// A street-edge cell of the lot outside every building, the shortest walk first.
    fn outside_point(&self, host: &dyn Host) -> Option<(f32, f32)> {
        let walk = self.walk_grid(host)?;
        let state = host.state();
        let offer = host.offer_of_site(&self.site);
        let street: HashSet<(i32, i32)> = rules::street_cells(state.layout_of(&self.site), offer.as_ref()).into_iter().collect();
        let inside = |x: i32, z: i32| state.buildings.iter().any(|b| b.site_id == self.site && goods::grid::overlaps(x, z, 1, 1, b.cell_x, b.cell_z, b.width, b.depth));
        let (path, _) = find_path(&walk, (self.x, self.z), |x, z| street.contains(&(x, z)) && !inside(x, z), PATH_LIMIT)?;
        path.last().copied().or(Some((self.x, self.z)))
    }

    fn walk_outside(&mut self, host: &dyn Host, dt: f32) {
        let Some(target) = self.outside else { return };
        if flat((self.x, self.z), target) <= STAND_TOLERANCE {
            return;
        }
        if self.clock >= self.next_outside_path || self.outside_path.is_empty() {
            self.next_outside_path = self.clock + 1.0;
            if let Some(walk) = self.walk_grid(host) {
                let (tx, tz) = navigation::cell_of(target.0, target.1);
                self.outside_path = find_path(&walk, (self.x, self.z), |x, z| x == tx && z == tz, PATH_LIMIT).map(|p| p.0).unwrap_or_default();
            }
        }
        let mut path = std::mem::take(&mut self.outside_path);
        self.follow(&mut path, dt);
        self.outside_path = path;
    }

    /// Moves along waypoints at walking speed; true once the last is reached.
    fn follow(&mut self, path: &mut Vec<(f32, f32)>, dt: f32) -> bool {
        let mut budget = WALK_SPEED * dt;
        while let Some(&next) = path.first() {
            let (dx, dz) = (next.0 - self.x, next.1 - self.z);
            let distance = (dx * dx + dz * dz).sqrt();
            if distance > 0.001 {
                self.yaw = yaw_of(dx, dz);
            }
            if distance <= budget {
                self.x = next.0;
                self.z = next.1;
                budget -= distance;
                path.remove(0);
                continue;
            }
            self.x += dx / distance * budget;
            self.z += dz / distance * budget;
            return false;
        }
        true
    }

    fn turn_towards(&mut self, point: (f32, f32)) {
        let (dx, dz) = (point.0 - self.x, point.1 - self.z);
        if dx * dx + dz * dz > 0.0001 {
            self.yaw = yaw_of(dx, dz);
        }
    }

    /// While no script runs, an employee that staffs a register stands behind it facing it.
    fn hold_post(&mut self, host: &dyn Host, dt: f32) {
        if self.clock >= self.next_post_check {
            self.next_post_check = self.clock + 1.0;
            let register = host.state().equipment.iter().find(|x| x.staff_id == self.id && x.kind == COUNTER_KIND && x.placed() && x.level == 0).cloned();
            let Some(register) = register else {
                self.post = None;
                self.post_key.clear();
                self.post_path.clear();
                return;
            };
            let key = format!("{}:{}:{}:{}", register.id, register.cell_x, register.cell_z, register.rotation);
            if key != self.post_key || self.post.is_none() {
                self.post_key = key;
                self.post = self.operator_spot(host, &register);
                self.post_path.clear();
            }
            if let Some((spot, _)) = self.post {
                if flat(spot, (self.x, self.z)) > STAND_TOLERANCE && self.post_path.is_empty() {
                    if let Some(walk) = self.walk_grid(host) {
                        let (tx, tz) = navigation::cell_of(spot.0, spot.1);
                        self.post_path = find_path(&walk, (self.x, self.z), |x, z| x == tx && z == tz, PATH_LIMIT).map(|p| p.0).unwrap_or_default();
                    }
                }
            }
        }
        let Some((spot, face)) = self.post else { return };
        if flat(spot, (self.x, self.z)) > STAND_TOLERANCE {
            let mut path = std::mem::take(&mut self.post_path);
            self.follow(&mut path, dt);
            self.post_path = path;
            return;
        }
        self.turn_towards(face);
    }

    /// Where a register's operator stands: a walkable cell beside its footprint, behind it first, nearest its middle.
    fn operator_spot(&self, host: &dyn Host, register: &Equipment) -> Option<((f32, f32), (f32, f32))> {
        let walk = self.walk_grid(host)?;
        let (width, depth) = register.footprint();
        let (fx, fz) = goods::grid::facing(register.rotation);
        let mut beside = vec![];
        for x in register.cell_x..register.cell_x + width {
            beside.push((x, register.cell_z - 1));
            beside.push((x, register.cell_z + depth));
        }
        for z in register.cell_z..register.cell_z + depth {
            beside.push((register.cell_x - 1, z));
            beside.push((register.cell_x + width, z));
        }
        let cx = register.cell_x as f32 + (width - 1) as f32 * 0.5;
        let cz = register.cell_z as f32 + (depth - 1) as f32 * 0.5;
        beside.sort_by(|a, b| {
            let ka = ((a.0 as f32 - cx) * fx as f32 + (a.1 as f32 - cz) * fz as f32, (a.0 as f32 - cx).abs() + (a.1 as f32 - cz).abs());
            let kb = ((b.0 as f32 - cx) * fx as f32 + (b.1 as f32 - cz) * fz as f32, (b.0 as f32 - cx).abs() + (b.1 as f32 - cz).abs());
            ka.partial_cmp(&kb).unwrap_or(std::cmp::Ordering::Equal)
        });
        let face = (register.cell_x as f32 + width as f32 * 0.5, register.cell_z as f32 + depth as f32 * 0.5);
        for (x, z) in beside {
            if !walk.get(x, z) {
                continue;
            }
            if find_path(&walk, (self.x, self.z), |px, pz| px == x && pz == z, PATH_LIMIT).is_some() || navigation::cell_of(self.x, self.z) == (x, z) {
                return Some(((x as f32 + 0.5, z as f32 + 0.5), face));
            }
        }
        None
    }

    // ------------------------------------------------------------------------------------------------- script stepping

    fn step_script(&mut self, host: &mut dyn Host, dt: f32) {
        if let Some(request) = self.request.take() {
            self.start_op(host, request);
        }
        if let Some(op) = self.op.take() {
            match self.advance(host, op, dt) {
                Some(op) => {
                    self.op = Some(op);
                    return;
                }
                None => {}
            }
        }
        self.resume(host);
    }

    fn finish(&mut self, values: Vec<Answer>) { self.result = values; }

    fn resume(&mut self, host: &mut dyn Host) {
        let Some(script) = self.script.take() else { return };
        script.budget.store(0, Ordering::Relaxed);
        let reader: &dyn Host = &*host;
        let cell = RefCell::new(&mut *self);
        let me = &cell;
        let outcome = script.lua.scope(|scope| {
            let api = &script.api;
            api.set("begin", scope.create_function(move |_, args: MultiValue| {
                let mut values: Vec<Arg> = args.iter().map(|v| Arg::from_lua(v, 0)).collect();
                let name = values.first().and_then(Arg::text).unwrap_or_default();
                if !OPERATIONS.contains(&name.as_str()) {
                    return Err(mlua::Error::runtime(format!("unknown operation '{name}'")));
                }
                values.remove(0);
                let mut agent = me.borrow_mut();
                agent.result.clear();
                agent.request = Some(Request { name, args: values });
                Ok(())
            })?)?;
            api.set("busy", scope.create_function(move |_, ()| {
                let agent = me.borrow();
                Ok(agent.request.is_some() || agent.op.is_some())
            })?)?;
            api.set("result", scope.create_function(move |lua, ()| {
                let agent = me.borrow();
                let values: mlua::Result<Vec<Value>> = agent.result.iter().map(|a| answer_value(lua, a)).collect();
                Ok(MultiValue::from_vec(values?))
            })?)?;
            api.set("say", scope.create_function(move |_, args: MultiValue| {
                let text = args.iter().map(|v| match v {
                    Value::String(s) => s.to_string_lossy().to_string(),
                    Value::Nil => "nil".into(),
                    Value::Boolean(b) => b.to_string(),
                    Value::Integer(i) => i.to_string(),
                    Value::Number(n) => if n.fract() == 0.0 && n.abs() < 1e15 { format!("{}", *n as i64) } else { n.to_string() },
                    other => format!("{}", other.type_name()),
                }).collect::<Vec<_>>().join(" ");
                let mut agent = me.borrow_mut();
                agent.status = format!("Running: {text}");
                agent.last_message = Some(text);
                Ok(())
            })?)?;
            for name in QUERIES.iter().filter(|n| **n != "say") {
                let name = *name;
                api.set(name, scope.create_function(move |lua, args: MultiValue| {
                    let values: Vec<Arg> = args.iter().map(|v| Arg::from_lua(v, 0)).collect();
                    let agent = me.borrow();
                    agent.query(lua, reader, name, &values)
                })?)?;
            }
            script.thread.resume::<MultiValue>(())
        });
        let finished = script.thread.status();
        drop(cell);
        match outcome {
            Err(error) => self.fail(host, tidy(&error.to_string())),
            Ok(_) if finished == ThreadStatus::Finished => {
                self.status = match &self.last_message {
                    Some(message) => format!("Finished (last said: {message})"),
                    None => "Finished".into(),
                };
                let source = self.source.clone();
                host.record_script(&self.id, &source, false, None);
            }
            Ok(_) if finished == ThreadStatus::Error => self.fail(host, "the script stopped with an error".into()),
            Ok(_) => self.script = Some(script),
        }
    }

    fn fail(&mut self, host: &mut dyn Host, message: String) {
        self.status = format!("Error: {message}");
        self.op = None;
        self.request = None;
        let source = self.source.clone();
        host.record_script(&self.id, &source, false, None);
    }

    // ------------------------------------------------------------------------------------------------- queries

    fn query(&self, lua: &Lua, host: &dyn Host, name: &str, args: &[Arg]) -> mlua::Result<MultiValue> {
        let state = host.state();
        let one = |value: Value| Ok(MultiValue::from_vec(vec![value]));
        match name {
            "count" => {
                let target = arg(args, 0).text().unwrap_or_else(|| "hands".into());
                let filter = ItemFilter::from(arg(args, 1));
                let place = self.resolve_name(state, &target);
                if place.problem.is_some() {
                    return one(Value::Number(0.0));
                }
                let locations: HashSet<String> = place.take_from.iter().cloned().chain(place.put_into.clone()).collect();
                let total: i64 = state.lots.iter().filter(|l| locations.contains(&l.location_id) && filter.allows(&l.item_id)).map(|l| l.quantity as i64).sum();
                one(Value::Number(total as f64))
            }
            "find" => {
                let kind = arg(args, 0).text();
                let mut found: Vec<&Equipment> = state.equipment.iter().filter(|e| e.site_id == self.site && e.placed() && e.level == 0 && kind.as_ref().is_none_or(|k| &e.kind == k)).collect();
                found.sort_by(|a, b| self.distance_to_piece(a).partial_cmp(&self.distance_to_piece(b)).unwrap_or(std::cmp::Ordering::Equal));
                let table = lua.create_table()?;
                for (index, piece) in found.iter().enumerate() {
                    table.raw_set(index + 1, piece.id.as_str())?;
                }
                one(Value::Table(table))
            }
            "carrying" => {
                let hands = hands(&self.id);
                one(Value::Number(state.lots.iter().filter(|l| l.location_id == hands).map(|l| l.quantity as i64).sum::<i64>() as f64))
            }
            "holding" => {
                let kind = arg(args, 0).text();
                let mut held: Vec<&Equipment> = state.equipment.iter().filter(|e| e.state == EquipmentState::Held && e.holder_id == self.id && kind.as_ref().is_none_or(|k| &e.kind == k)).collect();
                held.sort_by(|a, b| a.id.cmp(&b.id));
                let table = lua.create_table()?;
                for (index, piece) in held.iter().enumerate() {
                    table.raw_set(index + 1, piece.id.as_str())?;
                }
                one(Value::Table(table))
            }
            "position" => {
                let (x, z) = navigation::cell_of(self.x, self.z);
                let table = lua.create_table()?;
                table.raw_set(1, x)?;
                table.raw_set(2, z)?;
                table.raw_set("x", x)?;
                table.raw_set("z", z)?;
                one(Value::Table(table))
            }
            "accepts" => {
                let place = self.resolve(state, arg(args, 0));
                let excluded = names(arg(args, 1));
                let kind = place.equipment.as_ref().and_then(|id| state.equipment.iter().find(|e| &e.id == id)).map(|e| e.kind.clone());
                let inputs = kind.map(|k| host.recipe_inputs(&k)).unwrap_or_default();
                let table = lua.create_table()?;
                if inputs.is_empty() {
                    if excluded.is_empty() {
                        return one(Value::Nil);
                    }
                    let except = lua.create_table()?;
                    let mut sorted: Vec<&String> = excluded.iter().collect();
                    sorted.sort();
                    for (index, name) in sorted.iter().enumerate() {
                        except.raw_set(index + 1, name.as_str())?;
                    }
                    table.raw_set("except", except)?;
                    return one(Value::Table(table));
                }
                for (index, input) in inputs.iter().filter(|x| !excluded.contains(*x)).enumerate() {
                    table.raw_set(index + 1, input.as_str())?;
                }
                one(Value::Table(table))
            }
            "room" => {
                let place = self.resolve(state, arg(args, 0));
                let filter = ItemFilter::from(arg(args, 1));
                let Some(into) = place.put_into.filter(|_| place.problem.is_none()) else { return one(Value::Number(0.0)) };
                let candidates: HashSet<String> = match &filter.only {
                    Some(only) => only.clone(),
                    None => state.lots.iter().filter(|l| state.location(&l.location_id).is_some_and(|loc| loc.site_id == self.site)).map(|l| l.item_id.clone()).filter(|i| filter.allows(i)).collect(),
                };
                let room = candidates.iter().map(|item| rules::free_units(state.location(&into), &state.lots, item, false, |i| host.max_stack(i))).max().unwrap_or(0);
                one(Value::Number(room as f64))
            }
            "is_on" => {
                let place = self.resolve(state, arg(args, 0));
                let on = place.equipment.as_ref().and_then(|id| state.equipment.iter().find(|e| &e.id == id)).is_some_and(|e| e.powered_on);
                one(Value::Boolean(on))
            }
            _ => one(Value::Nil),
        }
    }

    fn distance_to_piece(&self, piece: &Equipment) -> f32 {
        let (w, d) = piece.footprint();
        flat((piece.cell_x as f32 + w as f32 * 0.5, piece.cell_z as f32 + d as f32 * 0.5), (self.x, self.z))
    }

    // ------------------------------------------------------------------------------------------------- places

    fn resolve(&self, state: &Snapshot, target: &Arg) -> Place {
        match target.cell() {
            Some((x, z)) => self.cell_place(state, x, z, true),
            None => self.resolve_name(state, &target.text().unwrap_or_default()),
        }
    }

    /// A container goods can be taken from or put into.
    fn container(&self, state: &Snapshot, target: &Arg) -> Place {
        let mut place = if target.is_nil() { self.resolve_name(state, "storage") } else { self.resolve(state, target) };
        if place.problem.is_none() && place.put_into.is_none() {
            place.problem = Some(format!("{} holds no goods (give a container such as a machine or shelf ID)", place.name));
        }
        place
    }

    fn cell_place(&self, state: &Snapshot, x: i32, z: i32, stand: bool) -> Place {
        let name = format!("({x}, {z})");
        let Some(layout) = state.layout_of(&self.site) else { return Place::problem(&name, "the world is not available".into()) };
        if x < 0 || z < 0 || x >= layout.width || z >= layout.depth {
            return Place::problem(&name, format!("cell {name} is off the site"));
        }
        if !stand {
            return Place { name, area: Some(Area::cells(x, z, 1, 1)), ..Default::default() };
        }
        let walk = rules::walkable(state, &self.site, None, false);
        if !walk.as_ref().is_some_and(|w| w.get(x, z)) {
            return Place::problem(&name, format!("cell {name} is not walkable"));
        }
        let point = (x as f32 + 0.5, z as f32 + 0.5);
        Place { name, stand: Some(point), area: Some(Area::point(point.0, point.1)), ..Default::default() }
    }

    /// "hands", a machine ID, a machine kind (the nearest), or a buffer "<id>:in" / "<id>:out".
    fn resolve_name(&self, state: &Snapshot, target: &str) -> Place {
        let target = target.trim();
        if target.is_empty() {
            return Place::problem("", "no place given".into());
        }
        let hands = hands(&self.id);
        if target == "hands" || target == hands {
            return Place { name: "hands".into(), take_from: vec![hands.clone()], put_into: Some(hands), area: Some(Area::point(self.x, self.z)), ..Default::default() };
        }
        let buffer = target.ends_with(":in") || target.ends_with(":out");
        let machine_id = if buffer { &target[..target.rfind(':').unwrap_or(target.len())] } else { target };
        let placed: Vec<&Equipment> = state.equipment.iter().filter(|e| e.site_id == self.site && e.placed()).collect();
        let machine = placed.iter().find(|e| e.id == machine_id).copied().or_else(|| {
            let mut kind: Vec<&Equipment> = placed.iter().filter(|e| e.kind == target && e.level == 0).copied().collect();
            kind.sort_by(|a, b| self.distance_to_piece(a).partial_cmp(&self.distance_to_piece(b)).unwrap_or(std::cmp::Ordering::Equal));
            kind.first().copied()
        });
        let Some(machine) = machine else {
            return Place::problem(target, format!("no place called '{target}' (try a machine kind such as \"oven\", or an ID from find())"));
        };
        if machine.level != 0 {
            return Place::problem(&machine.id, format!("{} is on an upper floor", machine.id));
        }
        let (width, depth) = machine.footprint();
        Place {
            name: machine.id.clone(),
            equipment: Some(machine.id.clone()),
            area: Some(Area::cells(machine.cell_x, machine.cell_z, width, depth)),
            take_from: if buffer { vec![target.to_string()] } else { vec![machine.output_location_id(), machine.input_location_id()] },
            put_into: Some(if buffer { target.to_string() } else { machine.input_location_id() }),
            ..Default::default()
        }
    }

    // ------------------------------------------------------------------------------------------------- operations

    fn walk_op(&self, place: Place, then: Then) -> Op { Op::Walk { place, then, deadline: self.clock + WALK_TIMEOUT_SECONDS, next_path: 0.0, path: vec![], pathed: false } }

    /// Turns a script's call into an operation, or answers it at once when it cannot start.
    fn start_op(&mut self, host: &mut dyn Host, request: Request) {
        let args = request.args;
        let state = host.state();
        let fail = |text: String| vec![Answer::Bool(false), Answer::Text(text)];
        let none = |text: String| vec![Answer::Number(0.0), Answer::Text(text)];
        match request.name.as_str() {
            "move_to" => {
                let place = match cell_at(&args, 0) {
                    Some(((x, z), _)) => self.cell_place(state, x, z, true),
                    None => self.resolve(state, arg(&args, 0)),
                };
                if let Some(problem) = place.problem {
                    return self.finish(fail(problem));
                }
                self.status = format!("Running: walking to {}", place.name);
                self.op = Some(self.walk_op(place, Then::MoveTo));
            }
            "path" => {
                let mut waypoints = args.clone();
                if waypoints.len() == 1 && arg(&waypoints, 0).cell().is_none() {
                    if let Arg::Table { list, .. } = &waypoints[0] {
                        waypoints = list.clone();
                    }
                }
                if waypoints.is_empty() {
                    return self.finish(fail("no waypoints given".into()));
                }
                self.op = Some(Op::Path { waypoints, index: 0, walking: None });
            }
            "take" | "put" => {
                let filter = ItemFilter::from(arg(&args, 1));
                let amount = match optional_amount(&args, 2) {
                    Ok(amount) => amount,
                    Err(error) => return self.fail(host, error),
                };
                let place = self.container(state, arg(&args, 0));
                if let Some(problem) = place.problem {
                    return self.finish(none(problem));
                }
                let take = request.name == "take";
                self.status = if take { format!("Running: fetching {} from {}", filter.label(), place.name) } else { format!("Running: delivering {} to {}", filter.label(), place.name) };
                let then = if take { Then::Take { filter, amount } } else { Then::Put { filter, amount } };
                self.op = Some(self.walk_op(place, then));
            }
            "wait" => {
                let seconds = arg(&args, 0).number().unwrap_or(0.0).clamp(0.0, 3600.0);
                self.op = Some(Op::Wait { until: self.clock + seconds });
            }
            "place" => {
                let what = arg(&args, 0).text();
                let Some(((x, z), next)) = cell_at(&args, 1) else { return self.finish(fail("place(machine, {x, z}, rotation) needs a cell".into())) };
                let rotation = match optional_integer(&args, next, 0) {
                    Ok(r) => r,
                    Err(error) => return self.fail(host, error),
                };
                let mut held: Vec<&Equipment> = state.equipment.iter().filter(|e| e.state == EquipmentState::Held && e.holder_id == self.id && what.as_ref().is_some_and(|w| &e.id == w || &e.kind == w)).collect();
                held.sort_by(|a, b| (what.as_deref() != Some(a.id.as_str()), &a.id).cmp(&(what.as_deref() != Some(b.id.as_str()), &b.id)));
                let machine = held.first().copied().cloned();
                let problem = match &machine {
                    None => Some(format!("not holding {} (give one on the employee screen)", what.map(|w| format!("'{w}'")).unwrap_or("a machine".into()))),
                    Some(_) if !(0..=3).contains(&rotation) => Some("rotation must be 0, 1, 2 or 3".into()),
                    Some(m) => goods::grid::placement_problem(state, m, x, z, rotation, 0).map(str::to_string),
                };
                if let Some(problem) = problem {
                    return self.finish(fail(problem));
                }
                let machine = machine.expect("checked");
                let (w, d) = goods::grid::footprint(machine.width, machine.depth, rotation);
                let place = Place { name: format!("({x}, {z})"), area: Some(Area::cells(x, z, w, d)), ..Default::default() };
                self.status = format!("Running: placing {} at {}", machine.kind, place.name);
                self.op = Some(self.walk_op(place, Then::Place { machine: machine.id, x, z, rotation }));
            }
            "pick_up" => {
                let mut place = self.resolve(state, arg(&args, 0));
                if place.problem.is_none() && place.equipment.is_none() {
                    place.problem = Some(format!("{} is not a machine", place.name));
                }
                if let Some(problem) = place.problem {
                    return self.finish(fail(problem));
                }
                self.status = format!("Running: picking up {}", place.name);
                self.op = Some(self.walk_op(place, Then::PickUp));
            }
            "place_belt" => {
                let Some(((x, z), next)) = cell_at(&args, 0) else { return self.finish(fail("place_belt({x, z}, direction) needs a cell".into())) };
                let direction = match optional_integer(&args, next, 0) {
                    Ok(d) => d,
                    Err(error) => return self.fail(host, error),
                };
                let mut place = self.cell_place(state, x, z, false);
                if place.problem.is_none() && !(0..=3).contains(&direction) {
                    place.problem = Some("direction must be 0, 1, 2 or 3".into());
                }
                if let Some(problem) = place.problem {
                    return self.finish(fail(problem));
                }
                self.status = format!("Running: laying a belt at {}", place.name);
                self.op = Some(self.walk_op(place, Then::PlaceBelt { x, z, direction }));
            }
            "turn_on" | "turn_off" | "toggle" => {
                let on = match request.name.as_str() {
                    "turn_on" => Some(true),
                    "turn_off" => Some(false),
                    _ => None,
                };
                let mut place = self.resolve(state, arg(&args, 0));
                let machine = place.equipment.as_ref().and_then(|id| state.equipment.iter().find(|e| &e.id == id)).cloned();
                if place.problem.is_none() && machine.is_none() {
                    place.problem = Some(format!("{} is not a machine", place.name));
                }
                if place.problem.is_none() && !machine.as_ref().is_some_and(|m| host.requires_power(&m.kind)) {
                    place.problem = Some(format!("{} has no power switch", place.name));
                }
                if place.problem.is_none() && machine.as_ref().is_some_and(|m| m.held_off && !m.powered_on) && on != Some(false) {
                    place.problem = Some(format!("{} was switched off by a player", place.name));
                }
                if let Some(problem) = place.problem {
                    return self.finish(fail(problem));
                }
                self.status = format!("Running: {} {}", match on { None => "toggling", Some(true) => "switching on", Some(false) => "switching off" }, place.name);
                self.op = Some(self.walk_op(place, Then::Power { on }));
            }
            "set_recipe" => {
                let mut place = self.resolve(state, arg(&args, 0));
                let kind = place.equipment.as_ref().and_then(|id| state.equipment.iter().find(|e| &e.id == id)).map(|e| e.kind.clone());
                if place.problem.is_none() && kind.is_none() {
                    place.problem = Some(format!("{} is not a machine", place.name));
                }
                let mut recipe = String::new();
                if place.problem.is_none() {
                    match self.recipe_for(host, kind.as_deref().unwrap_or(""), arg(&args, 1).text(), &place.name) {
                        Ok(r) => recipe = r,
                        Err(problem) => place.problem = Some(problem),
                    }
                }
                if let Some(problem) = place.problem {
                    return self.finish(fail(problem));
                }
                self.status = format!("Running: setting {} to {}", place.name, if recipe.is_empty() { "automatic" } else { recipe.as_str() });
                self.op = Some(self.walk_op(place, Then::SetRecipe { recipe }));
            }
            "work" => {
                let mut place = self.resolve(state, arg(&args, 0));
                let machine = place.equipment.as_ref().and_then(|id| state.equipment.iter().find(|e| &e.id == id)).cloned();
                let register = machine.as_ref().is_some_and(|m| m.kind == COUNTER_KIND);
                if place.problem.is_none() && machine.is_none() {
                    place.problem = Some(format!("{} is not a machine", place.name));
                }
                if place.problem.is_none() && !register && !machine.as_ref().is_some_and(|m| host.requires_worker(&m.kind)) {
                    place.problem = Some(format!("{} does not need a worker", place.name));
                }
                let mut recipe = None;
                if place.problem.is_none() && !register {
                    if let Some(given) = arg(&args, 1).text() {
                        match self.recipe_for(host, &machine.as_ref().map(|m| m.kind.clone()).unwrap_or_default(), Some(given), &place.name) {
                            Ok(r) => recipe = Some(r),
                            Err(problem) => place.problem = Some(problem),
                        }
                    }
                }
                if let Some(problem) = place.problem {
                    return self.finish(none(problem));
                }
                self.status = format!("Running: walking to work at {}", place.name);
                if let Some(counter) = machine.filter(|_| register) {
                    if let Some((spot, _)) = self.operator_spot(host, &counter) {
                        place = Place { name: place.name, equipment: place.equipment, stand: Some(spot), area: Some(Area::point(spot.0, spot.1)), ..Default::default() };
                    }
                    self.op = Some(self.walk_op(place, Then::WorkRegister));
                } else {
                    self.op = Some(self.walk_op(place, Then::WorkStation { recipe }));
                }
            }
            _ => self.fail(host, format!("unknown operation '{}'", request.name)),
        }
    }

    /// A recipe for a kind from a script's text: "" or nil for automatic, a recipe ID, or the item a recipe makes.
    fn recipe_for(&self, host: &dyn Host, kind: &str, text: Option<String>, machine: &str) -> Result<String, String> {
        let Some(text) = text.filter(|t| !t.is_empty()) else { return Ok(String::new()) };
        let selectable = host.selectable_recipes(kind);
        if selectable.contains(&text) {
            return Ok(text);
        }
        if let Some(made) = host.recipe_making(kind, &text).filter(|r| selectable.contains(r)) {
            return Ok(made);
        }
        Err(format!("{machine} cannot make '{text}'"))
    }

    /// Advances an operation; Some(op) while it continues, None once it has set its answer.
    fn advance(&mut self, host: &mut dyn Host, op: Op, dt: f32) -> Option<Op> {
        match op {
            Op::Wait { until } => {
                if self.clock < until {
                    return Some(Op::Wait { until });
                }
                self.finish(vec![Answer::Bool(true)]);
                None
            }
            Op::Walk { place, then, deadline, mut next_path, mut path, mut pathed } => {
                match self.walk_step(host, &place, deadline, &mut next_path, &mut path, &mut pathed, dt) {
                    WalkStep::Walking => Some(Op::Walk { place, then, deadline, next_path, path, pathed }),
                    WalkStep::Failed(reason) => {
                        let zero = matches!(then, Then::Take { .. } | Then::Put { .. } | Then::WorkRegister | Then::WorkStation { .. });
                        self.finish(vec![if zero { Answer::Number(0.0) } else { Answer::Bool(false) }, Answer::Text(reason)]);
                        None
                    }
                    WalkStep::Arrived => self.arrive(host, place, then),
                }
            }
            Op::Path { waypoints, mut index, mut walking } => {
                loop {
                    if index >= waypoints.len() {
                        self.finish(vec![Answer::Bool(true)]);
                        return None;
                    }
                    if walking.is_none() {
                        let place = self.resolve(host.state(), &waypoints[index]);
                        if let Some(problem) = &place.problem {
                            self.finish(vec![Answer::Bool(false), Answer::Text(format!("waypoint {}: {problem}", index + 1)), Answer::Number((index + 1) as f64)]);
                            return None;
                        }
                        self.status = format!("Running: walking to {} ({}/{})", place.name, index + 1, waypoints.len());
                        walking = Some((place, self.clock + WALK_TIMEOUT_SECONDS, 0.0, vec![], false));
                    }
                    let (place, deadline, mut next_path, mut path, mut pathed) = walking.take().expect("set above");
                    match self.walk_step(host, &place, deadline, &mut next_path, &mut path, &mut pathed, dt) {
                        WalkStep::Walking => {
                            walking = Some((place, deadline, next_path, path, pathed));
                            return Some(Op::Path { waypoints, index, walking });
                        }
                        WalkStep::Failed(reason) => {
                            self.finish(vec![Answer::Bool(false), Answer::Text(format!("waypoint {}: {reason}", index + 1)), Answer::Number((index + 1) as f64)]);
                            return None;
                        }
                        WalkStep::Arrived => index += 1,
                    }
                }
            }
            Op::WorkWait { station, name, start_by, give_up, mut job } => {
                let current = host.state().jobs.iter().find(|j| j.station_id == station).map(|j| (j.id.clone(), j.state));
                let mut made = None;
                if self.clock >= give_up {
                    made = Some(0);
                } else if job.is_none() {
                    match &current {
                        Some((id, _)) => job = Some(id.clone()),
                        None if self.clock > start_by => made = Some(0),
                        None => {}
                    }
                } else if current.as_ref().is_none_or(|(id, state)| Some(id) != job.as_ref() || *state == JobState::Blocked) {
                    made = Some(1);
                }
                let Some(made) = made else { return Some(Op::WorkWait { station, name, start_by, give_up, job }) };
                host.command(&self.id, Command::Staff { register: station, staff: String::new() });
                if made > 0 {
                    self.finish(vec![Answer::Number(1.0)]);
                } else {
                    let reason = if job.is_none() { format!("nothing to make at {name} (are the ingredients in?)") } else { format!("gave up waiting at {name}") };
                    self.finish(vec![Answer::Number(0.0), Answer::Text(reason)]);
                }
                None
            }
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn walk_step(&mut self, host: &dyn Host, place: &Place, deadline: f64, next_path: &mut f64, path: &mut Vec<(f32, f32)>, pathed: &mut bool, dt: f32) -> WalkStep {
        let area = place.area.unwrap_or(Area::point(self.x, self.z));
        let within = if place.stand.is_some() { STAND_TOLERANCE } else { REACH };
        if area.distance(self.x, self.z) <= within {
            if place.stand.is_none() {
                self.turn_towards(area.centre());
            }
            return WalkStep::Arrived;
        }
        if self.clock > deadline {
            return WalkStep::Failed(format!("could not reach {}", place.name));
        }
        if self.clock >= *next_path {
            *next_path = self.clock + 0.5;
            let Some(walk) = self.walk_grid(host) else { return WalkStep::Failed(format!("no path to {}", place.name)) };
            let found = match place.stand {
                Some(point) => {
                    let (tx, tz) = navigation::cell_of(point.0, point.1);
                    find_path(&walk, (self.x, self.z), |x, z| x == tx && z == tz, PATH_LIMIT).map(|(mut p, c)| {
                        if let Some(last) = p.last_mut() {
                            *last = point;
                        }
                        (p, c)
                    })
                }
                None => find_path(&walk, (self.x, self.z), |x, z| {
                    let (cx, cz) = (x as f32 + 0.5, z as f32 + 0.5);
                    !area.contains(cx, cz) && area.distance(cx, cz) <= REACH - 0.1
                }, PATH_LIMIT),
            };
            match found {
                Some((p, _)) => {
                    *path = p;
                    *pathed = true;
                }
                None => return WalkStep::Failed(format!("no path to {}", place.name)),
            }
        }
        let done = self.follow(path, dt);
        if done && *pathed && area.distance(self.x, self.z) > within + 0.05 {
            return WalkStep::Failed(format!("could not reach {}", place.name));
        }
        WalkStep::Walking
    }

    /// The action an operation takes on arriving at its place.
    fn arrive(&mut self, host: &mut dyn Host, place: Place, then: Then) -> Option<Op> {
        let reason = |outcome: &Outcome| if outcome.accepted { Answer::Nil } else { Answer::Text(outcome.reason.clone()) };
        match then {
            Then::MoveTo => self.finish(vec![Answer::Bool(true)]),
            Then::Take { filter, amount } => {
                let (moved, why) = self.move_goods(host, &place.take_from, &hands(&self.id), &filter, amount, false, &place);
                self.finish(vec![Answer::Number(moved as f64), if moved > 0 { Answer::Nil } else { Answer::Text(why) }]);
            }
            Then::Put { filter, amount } => {
                let into = place.put_into.clone().unwrap_or_default();
                let (moved, why) = self.move_goods(host, &[hands(&self.id)], &into, &filter, amount, true, &place);
                self.finish(vec![Answer::Number(moved as f64), if moved > 0 { Answer::Nil } else { Answer::Text(why) }]);
            }
            Then::Place { machine, x, z, rotation } => {
                let outcome = host.command(&self.id, Command::Place { equipment: machine, x, z, rotation, level: 0 });
                self.finish(vec![Answer::Bool(outcome.accepted), reason(&outcome)]);
            }
            Then::PickUp => {
                let outcome = host.command(&self.id, Command::PickUp { equipment: place.equipment.unwrap_or_default() });
                self.finish(vec![Answer::Bool(outcome.accepted), reason(&outcome)]);
            }
            Then::PlaceBelt { x, z, direction } => {
                let outcome = host.command(&self.id, Command::PlaceBelt { site: self.site.clone(), x, z, direction, level: 0 });
                self.finish(vec![Answer::Bool(outcome.accepted), reason(&outcome)]);
            }
            Then::Power { on } => {
                let id = place.equipment.unwrap_or_default();
                let target = on.unwrap_or_else(|| !host.state().equipment.iter().any(|e| e.id == id && e.powered_on));
                let outcome = host.command(&self.id, Command::SetPower { equipment: id, on: target });
                self.finish(vec![Answer::Bool(outcome.accepted), reason(&outcome)]);
            }
            Then::SetRecipe { recipe } => {
                let outcome = host.command(&self.id, Command::SelectRecipe { equipment: place.equipment.unwrap_or_default(), recipe });
                self.finish(vec![Answer::Bool(outcome.accepted), reason(&outcome)]);
            }
            Then::WorkRegister => {
                let outcome = host.command(&self.id, Command::Staff { register: place.equipment.clone().unwrap_or_default(), staff: self.id.clone() });
                let taken = outcome.accepted || outcome.reason == "unchanged";
                if taken {
                    self.status = format!("Running: serving at {}", place.name);
                }
                self.finish(vec![Answer::Number(if taken { 1.0 } else { 0.0 }), if taken { Answer::Nil } else { Answer::Text(outcome.reason) }]);
            }
            Then::WorkStation { recipe } => {
                let station = place.equipment.clone().unwrap_or_default();
                if let Some(recipe) = recipe {
                    let current = host.state().equipment.iter().find(|e| e.id == station).map(|e| e.recipe_id.clone());
                    if current.as_deref() != Some(recipe.as_str()) {
                        let set = host.command(&self.id, Command::SelectRecipe { equipment: station.clone(), recipe });
                        if !set.accepted {
                            self.finish(vec![Answer::Number(0.0), Answer::Text(set.reason)]);
                            return None;
                        }
                    }
                }
                let staffed = host.command(&self.id, Command::Staff { register: station.clone(), staff: self.id.clone() });
                if !staffed.accepted && staffed.reason != "unchanged" {
                    self.finish(vec![Answer::Number(0.0), Answer::Text(staffed.reason)]);
                    return None;
                }
                self.status = format!("Running: working at {}", place.name);
                return Some(Op::WorkWait { station, name: place.name, start_by: self.clock + WORK_START_SECONDS, give_up: self.clock + WORK_LIMIT_SECONDS, job: None });
            }
        }
        None
    }

    /// Moves up to `amount` units from the sources into the destination, one transfer per lot.
    #[allow(clippy::too_many_arguments)]
    fn move_goods(&mut self, host: &mut dyn Host, sources: &[String], destination: &str, filter: &ItemFilter, amount: i32, include_spoiled: bool, place: &Place) -> (i32, String) {
        if place.area.is_some_and(|a| a.distance(self.x, self.z) > REACH + 0.25) && place.name != "hands" {
            return (0, "too far away".into());
        }
        let mut moved = 0;
        let mut reason = if filter.any() { "nothing there".to_string() } else { format!("no {} there", filter.label()) };
        let hands = hands(&self.id);
        let mut request = 0;
        for source in sources {
            while moved < amount {
                let state = host.state();
                let mut lots: Vec<&goods::Lot> = state.lots.iter().filter(|l| &l.location_id == source && filter.allows(&l.item_id) && (include_spoiled || !l.spoiled)).collect();
                lots.sort_by(|a, b| b.exposure_seconds.cmp(&a.exposure_seconds).then_with(|| a.id.cmp(&b.id)));
                let free = |lot: &goods::Lot| rules::free_units(state.location(destination), &state.lots, &lot.item_id, lot.spoiled, |i| host.max_stack(i));
                let Some(lot) = lots.into_iter().find(|l| free(l) > 0).cloned() else {
                    if state.lots.iter().any(|l| &l.location_id == source && filter.allows(&l.item_id)) {
                        reason = if *source == hands { "no room there".into() } else { "hands are full".into() };
                    }
                    break;
                };
                let take = (free(&lot).min(lot.quantity as i64).min((amount - moved) as i64)) as i32;
                request += 1;
                let _ = request;
                let outcome = host.command(&self.id, Command::Transfer { lot: lot.id.clone(), destination: destination.to_string(), quantity: take });
                if !outcome.accepted {
                    reason = outcome.reason;
                    break;
                }
                moved += take;
            }
        }
        self.carrying = host.state().lots.iter().any(|l| l.location_id == hands);
        (moved, reason)
    }
}

enum WalkStep {
    Walking,
    Arrived,
    Failed(String),
}

