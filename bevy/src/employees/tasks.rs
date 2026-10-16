// An employee's visual task list (Unity EmployeeTaskList, decision 0037) and the Lua program generated from it. The Lua is what
// runs on the server through the employee API; the list is how the player edits it, saved beside the script. Generation is
// deterministic, so a client can tell whether a script still matches its list (one that does not was edited by hand).
use serde::{Deserialize, Serialize};

pub const TASK_HEADER: &str = "-- Generated from this employee's Tasks tab. Editing it here detaches the employee from its tasks.\n";

#[derive(Clone, Copy, Debug, Default, Deserialize, Serialize, PartialEq, Eq)]
pub enum EmployeeTaskType {
    #[default]
    Move,
    Power,
    Work,
    Recipe,
}

#[derive(Clone, Copy, Debug, Default, Deserialize, Serialize, PartialEq, Eq)]
pub enum EmployeeTaskPower {
    #[default]
    On,
    Off,
    Toggle,
}

/// Stored JSON mirrors Unity JsonUtility's PascalCase members and enum spellings.
#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(default)]
pub struct EmployeeTask {
    #[serde(rename = "Type")]
    pub task_type: EmployeeTaskType,
    #[serde(rename = "Source")]
    pub source: String,
    #[serde(rename = "Destination")]
    pub destination: String,
    #[serde(rename = "AnyItem")]
    pub any_item: bool,
    #[serde(rename = "Whitelist")]
    pub whitelist: bool,
    #[serde(rename = "Items")]
    pub items: Vec<String>,
    #[serde(rename = "Machine")]
    pub machine: String,
    #[serde(rename = "Power")]
    pub power: EmployeeTaskPower,
    #[serde(rename = "Recipe")]
    pub recipe: String,
}

impl Default for EmployeeTask {
    fn default() -> Self {
        Self {
            task_type: EmployeeTaskType::Move, source: String::new(), destination: String::new(), any_item: true, whitelist: true,
            items: vec![], machine: String::new(), power: EmployeeTaskPower::On, recipe: String::new(),
        }
    }
}

pub const MOVE_LABEL: &str = "Move stuff from A to B";
pub const POWER_LABEL: &str = "Turn machine on";
pub const WORK_LABEL: &str = "Work at counter";
pub const RECIPE_LABEL: &str = "Set machine recipe";

#[derive(Clone, Debug, Default, Deserialize, Serialize, PartialEq, Eq)]
#[serde(default)]
pub struct EmployeeTaskList {
    #[serde(rename = "Tasks")]
    pub tasks: Vec<EmployeeTask>,
}

impl EmployeeTaskList {
    /// Unity treats empty or malformed task JSON as an empty list and drops null list entries.
    pub fn from_json(json: &str) -> Self {
        serde_json::from_str::<Self>(json).unwrap_or_default()
    }

    pub fn to_json(&self) -> Result<String, serde_json::Error> {
        serde_json::to_string(self)
    }

    /// Generate the same task program as Unity. This text is an assignment, not a Rust-side interpreter.
    pub fn to_lua(&self) -> String {
        let mut text = String::from(TASK_HEADER);
        if self.tasks.is_empty() {
            text.push_str("-- No tasks yet: add one with the + button.\n");
            return text;
        }
        if self.tasks.iter().any(|task| task.task_type == EmployeeTaskType::Move) {
            text.push_str("\n-- Carries one load from source to destination: only items the filter allows, as many as the destination has\n-- room for. Anything that does not fit goes back to the source, so the hands are empty for the next task.\nlocal function move(source, destination, items)\n  local space = room(destination, items)\n  if space == 0 or count(source, items) == 0 then return 0 end\n  if take(source, items, space) == 0 then return 0 end\n  local delivered = put(destination, items)\n  if carrying() > 0 then put(source) end\n  return delivered\nend\n");
        }
        text.push_str("\nwhile true do\n");
        for (index, task) in self.tasks.iter().enumerate() {
            let description = describe(task);
            text.push_str(&format!("  -- {}. {description}\n  {}\n", index + 1,
                statement(task).unwrap_or_else(|| "-- (not finished: nothing happens yet)".into())));
        }
        text.push_str("  wait(1)\nend\n");
        text
    }
}

fn line(text: &str) -> String { text.replace('\n', " ").replace('\r', " ") }
pub fn quote(text: &str) -> String {
    format!("\"{}\"", text.replace('\\', "\\\\").replace('"', "\\\"").replace('\n', "\\n").replace('\r', "\\r"))
}
fn named(place: &str, missing: &str) -> String { if place.is_empty() { missing.into() } else { line(place) } }
fn statement(task: &EmployeeTask) -> Option<String> {
    match task.task_type {
        EmployeeTaskType::Work if task.machine.is_empty() => None,
        EmployeeTaskType::Work if task.recipe.is_empty() => Some(format!("work({})", quote(&task.machine))),
        EmployeeTaskType::Work => Some(format!("work({}, {})", quote(&task.machine), quote(&task.recipe))),
        EmployeeTaskType::Recipe if task.machine.is_empty() => None,
        EmployeeTaskType::Recipe => Some(format!("set_recipe({}, {})", quote(&task.machine), quote(&task.recipe))),
        EmployeeTaskType::Power if task.machine.is_empty() => None,
        EmployeeTaskType::Power => {
            let machine = quote(&task.machine);
            Some(match task.power {
                EmployeeTaskPower::On => format!("if not is_on({machine}) then turn_on({machine}) end"),
                EmployeeTaskPower::Off => format!("if is_on({machine}) then turn_off({machine}) end"),
                EmployeeTaskPower::Toggle => format!("toggle({machine})"),
            })
        }
        EmployeeTaskType::Move if task.source.is_empty() || task.destination.is_empty() => None,
        EmployeeTaskType::Move => {
            let destination = quote(&task.destination);
            let items = if task.any_item || (!task.whitelist && task.items.is_empty()) {
                format!("accepts({destination})")
            } else if task.whitelist {
                if task.items.is_empty() { return None; }
                format!("{{{}}}", task.items.iter().map(|x| quote(x)).collect::<Vec<_>>().join(", "))
            } else {
                format!("accepts({destination}, {{{}}})", task.items.iter().map(|x| quote(x)).collect::<Vec<_>>().join(", "))
            };
            Some(format!("move({}, {destination}, {items})", quote(&task.source)))
        }
    }
}
pub fn describe(task: &EmployeeTask) -> String {
    match task.task_type {
        EmployeeTaskType::Work => format!("Work at {} for one batch{}", named(&task.machine, "a counter"), if task.recipe.is_empty() { String::new() } else { format!(" of {}", line(&task.recipe)) }),
        EmployeeTaskType::Recipe => format!("Set {} to make {}", named(&task.machine, "a machine"), if task.recipe.is_empty() { "automatically".into() } else { line(&task.recipe) }),
        EmployeeTaskType::Power => {
            let machine = named(&task.machine, "a machine");
            match task.power { EmployeeTaskPower::On => format!("Turn {machine} on"), EmployeeTaskPower::Off => format!("Turn {machine} off"), EmployeeTaskPower::Toggle => format!("Toggle {machine}") }
        }
        EmployeeTaskType::Move => {
            let what = if task.any_item { "any valid item".into() } else if task.whitelist { if task.items.is_empty() { "nothing (the whitelist is empty)".into() } else { task.items.iter().map(|x|line(x)).collect::<Vec<_>>().join(", ") } } else if task.items.is_empty() { "any valid item".into() } else { format!("any valid item except {}", task.items.iter().map(|x|line(x)).collect::<Vec<_>>().join(", ")) };
            format!("Move {what} from {} to {}", named(&task.source, "?"), named(&task.destination, "?"))
        }
    }
}
