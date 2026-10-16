// Employees, hiring, wages and walking between owned sites (Unity GoodsWorld.Employees.cs, .Wages.cs and .Entering.cs).
// An employee record lives beside its site grant and carried inventory (carried:<id>), so an employee, the goods in its hands,
// its pose and its script commit and recover together. Wages are charged at every game-hour boundary inside the clock step;
// an employee whose wage cannot be paid is unpaid and does no work until a later step finds the cash. Entering another owned
// lot moves the player's inventory and held machines there in one commit.
use super::state::*;
use super::company::LEDGER_WAGE;
use super::{grid, inventory_location_id, CashNote, GoodsWorld, Result, CARRIED_PREFIX, EMPLOYEE_PREFIX};
use crate::world::clock::HOUR_SECONDS;

pub const MAX_EMPLOYEE_SCRIPT_LENGTH: usize = 16000;
pub const MAX_EMPLOYEE_TASKS_LENGTH: usize = 16000;
pub const MAX_EMPLOYEE_NAME_LENGTH: usize = 32;
pub const GAME_HOUR_SECONDS: i64 = HOUR_SECONDS;
pub const WAGE_CENTS_PER_HOUR: i64 = 1000;
pub const FLOOR_CELLS_PER_EMPLOYEE: i64 = 25;
pub const HIRED_HAND_SLOTS: i32 = 4;
/// Presentation only: clock 0 reads as day 1, 08:00.
pub const GAME_CLOCK_START_HOUR: i64 = 8;
/// PROTOTYPE: how far outside its lot (metres) a player may stand and still enter it.
pub const ENTER_MARGIN_METRES: f32 = 2.0;

/// Day (from 1), hour and minute of the game clock at a world clock reading.
pub fn game_time(clock_seconds: i64) -> (i64, i32, i32) {
    let minutes = clock_seconds.max(0) + GAME_CLOCK_START_HOUR * GAME_HOUR_SECONDS;
    let hours = minutes / GAME_HOUR_SECONDS;
    (hours / 24 + 1, (hours % 24) as i32, (minutes % GAME_HOUR_SECONDS) as i32)
}

/// World clock second of the next wage charge.
pub fn next_wage_seconds(state: &Snapshot) -> i64 { (state.wages_paid_hour + 1) * GAME_HOUR_SECONDS }

/// How many employees a site allows: one per 25 floor cells over all storeys, at least one.
pub fn employee_cap(state: &Snapshot, site_id: &str) -> i32 {
    let cells: i64 = state.buildings.iter().filter(|b| b.site_id == site_id).map(|b| grid::interior_cells(b).len() as i64 * b.floors.max(1) as i64).sum();
    (cells / FLOOR_CELLS_PER_EMPLOYEE).clamp(1, i32::MAX as i64) as i32
}

/// Wages per game hour of every employee of the company that owns the site.
pub fn company_wages(state: &Snapshot, site_id: &str) -> i64 {
    let Some(company) = state.company_of_site(site_id) else { return 0 };
    state.employees.iter().filter(|e| company.site_ids.contains(&e.site_id)).count() as i64 * WAGE_CENTS_PER_HOUR
}

pub fn is_valid_employee_name(name: &str) -> bool {
    !name.trim().is_empty() && name == name.trim() && name.chars().count() <= MAX_EMPLOYEE_NAME_LENGTH && !name.chars().any(char::is_control)
}

fn valid_pose(x: f32, y: f32, z: f32, yaw: f32) -> bool { x.is_finite() && y.is_finite() && z.is_finite() && yaw.is_finite() }

impl GoodsWorld {
    /// Server-only seed/hire path: adds the record, its site grant and its carried inventory.
    pub fn bootstrap_employee(&mut self, employee: Employee, hand_slots: i32) -> Result<()> {
        if !employee.id.starts_with(EMPLOYEE_PREFIX) || self.state.employees.iter().any(|x| x.id == employee.id) || !Self::site_exists(&self.state, &employee.site_id)
            || hand_slots < 1 || !valid_pose(employee.x, employee.y, employee.z, employee.yaw) || employee.script.len() > MAX_EMPLOYEE_SCRIPT_LENGTH
            || employee.tasks.len() > MAX_EMPLOYEE_TASKS_LENGTH
        {
            return Err(format!("Invalid or duplicate employee {}.", employee.id));
        }
        let hands = inventory_location_id(&employee.id);
        match self.state.location(&hands) {
            Some(location) if location.site_id != employee.site_id => return Err(format!("{hands} belongs to another site.")),
            Some(_) => {}
            None => self.state.locations.push(Location { id: hands, site_id: employee.site_id.clone(), kind: "carried".into(), capacity: hand_slots, refrigerated: false }),
        }
        if !self.state.can_view(&employee.id, &employee.site_id) {
            self.state.grants.push(Grant { player_id: employee.id.clone(), site_id: employee.site_id.clone() });
        }
        self.state.employees.push(employee);
        self.state.revision += 1;
        Ok(())
    }

    /// Server-only: records where the employee stands, in memory; saved by the next commit.
    pub fn set_employee_pose(&mut self, id: &str, x: f32, y: f32, z: f32, yaw: f32) -> bool {
        if !valid_pose(x, y, z, yaw) {
            return false;
        }
        let Some(employee) = self.state.employees.iter_mut().find(|e| e.id == id) else { return false };
        if employee.x == x && employee.y == y && employee.z == z && employee.yaw == yaw {
            return true;
        }
        employee.x = x;
        employee.y = y;
        employee.z = z;
        employee.yaw = yaw;
        self.state.revision += 1;
        true
    }

    /// Server-only: assigns a program (and whether it runs) and, unless tasks is None, the task list it came from.
    pub fn set_employee_script(&mut self, id: &str, script: &str, running: bool, tasks: Option<&str>) -> std::result::Result<bool, &'static str> {
        if script.len() > MAX_EMPLOYEE_SCRIPT_LENGTH {
            return Err("script-too-long");
        }
        if tasks.is_some_and(|t| t.len() > MAX_EMPLOYEE_TASKS_LENGTH) {
            return Err("tasks-too-long");
        }
        let Some(employee) = self.state.employees.iter_mut().find(|e| e.id == id) else { return Err("unknown-employee") };
        let next_tasks = tasks.map(str::to_string).unwrap_or_else(|| employee.tasks.clone());
        if employee.script == script && employee.script_running == running && employee.tasks == next_tasks {
            return Ok(false);
        }
        employee.script = script.to_string();
        employee.script_running = running;
        employee.tasks = next_tasks;
        self.state.revision += 1;
        Ok(true)
    }

    pub fn rename_employee(&mut self, id: &str, name: &str) -> std::result::Result<bool, &'static str> {
        let name = name.trim();
        if !self.state.employees.iter().any(|e| e.id == id) {
            return Err("unknown-employee");
        }
        if !is_valid_employee_name(name) {
            return Err("invalid-name");
        }
        let employee = self.state.employees.iter_mut().find(|e| e.id == id).expect("checked above");
        if employee.name == name {
            return Ok(false);
        }
        employee.name = name.to_string();
        self.state.revision += 1;
        Ok(true)
    }

    pub fn is_unpaid(&self, id: &str) -> bool { self.state.employees.iter().any(|e| e.id == id && e.unpaid) }

    /// Hires an employee for the site the player stands on, at the given pose; the new ID is the outcome's entity.
    pub fn hire(&mut self, player: &str, request: &str, x: f32, y: f32, z: f32, yaw: f32) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let site = self.state.location(&inventory_location_id(player)).map(|l| l.site_id.clone());
        let Some(site) = site.filter(|s| self.state.can_view(player, s)) else { return self.record(request, player, false, "forbidden", "") };
        if self.company_of_site(&site).is_none() {
            return self.record(request, player, false, "no-company", "");
        }
        if self.state.employees.iter().filter(|e| e.site_id == site).count() as i32 >= employee_cap(&self.state, &site) {
            return self.record(request, player, false, "employee-cap", "");
        }
        if !valid_pose(x, y, z, yaw) {
            return self.record(request, player, false, "invalid-position", "");
        }
        let id = loop {
            self.state.next_employee_number += 1;
            let id = format!("{EMPLOYEE_PREFIX}{}", self.state.next_employee_number);
            if !self.state.employees.iter().any(|e| e.id == id) && !self.state.grants.iter().any(|g| g.player_id == id) && self.state.location(&inventory_location_id(&id)).is_none() {
                break id;
            }
        };
        let name = format!("Employee {}", self.state.next_employee_number);
        if let Err(error) = self.bootstrap_employee(Employee { id: id.clone(), site_id: site, name, x, y, z, yaw, ..Default::default() }, HIRED_HAND_SLOTS) {
            return self.record(request, player, false, if error.is_empty() { "invalid-position" } else { "forbidden" }, "");
        }
        self.record_with(request, player, "hired", 0, &id, "")
    }

    /// Fires an employee; refused while its hands hold goods or a machine, so firing never loses anything.
    pub fn fire(&mut self, player: &str, request: &str, employee_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(employee) = self.state.employees.iter().find(|e| e.id == employee_id).cloned() else { return self.record(request, player, false, "forbidden", "") };
        if !self.state.can_view(player, &employee.site_id) {
            return self.record(request, player, false, "forbidden", "");
        }
        let hands = inventory_location_id(&employee.id);
        if self.state.lots.iter().any(|l| l.location_id == hands) || self.state.equipment.iter().any(|e| e.state == EquipmentState::Held && e.holder_id == employee.id) {
            return self.record(request, player, false, "holding", "");
        }
        if self.state.reservations.iter().any(|r| r.active && r.player_id == employee.id) {
            return self.record(request, player, false, "busy", "");
        }
        self.release_staff(&employee.id);
        self.state.employees.retain(|e| e.id != employee.id);
        self.state.grants.retain(|g| g.player_id != employee.id);
        self.state.locations.retain(|l| l.id != hands);
        self.invalidate_diners();
        self.record_with(request, player, "fired", 0, &employee.id, "")
    }

    /// Inside a clock step: charges every game hour the clock has passed, then pays any unpaid employee the cash now covers.
    pub(crate) fn pay_wages(&mut self) {
        let hour = self.state.clock_seconds / GAME_HOUR_SECONDS;
        let mut changed = false;
        while self.state.wages_paid_hour < hour {
            self.state.wages_paid_hour += 1;
            let at = self.state.wages_paid_hour * GAME_HOUR_SECONDS;
            for index in 0..self.state.employees.len() {
                if self.state.employees[index].unpaid {
                    continue;
                }
                let (id, site) = (self.state.employees[index].id.clone(), self.state.employees[index].site_id.clone());
                let Some(company) = self.company_of_site(&site) else { continue };
                if self.try_debit(&company, WAGE_CENTS_PER_HOUR, CashNote::new(LEDGER_WAGE, &site).equipment(&id).at(at)) {
                    continue;
                }
                self.state.employees[index].unpaid = true;
                changed = true;
            }
        }
        for index in 0..self.state.employees.len() {
            if !self.state.employees[index].unpaid {
                continue;
            }
            let (id, site) = (self.state.employees[index].id.clone(), self.state.employees[index].site_id.clone());
            if let Some(company) = self.company_of_site(&site) {
                if !self.try_debit(&company, WAGE_CENTS_PER_HOUR, CashNote::new(LEDGER_WAGE, &site).equipment(&id)) {
                    continue;
                }
            }
            self.state.employees[index].unpaid = false;
            changed = true;
        }
        if changed {
            self.invalidate_diners();
        }
    }

    /// Walking into another listed lot the player is granted moves their inventory and held machines there.
    pub fn enter_site(&mut self, player: &str, request: &str, site_id: &str, map_x: f32, map_z: f32) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        if !self.state.can_view(player, site_id) {
            return self.reject(request, player, "forbidden");
        }
        let Some(lot) = self.offer_of_site(site_id).filter(|_| Self::site_exists(&self.state, site_id)) else { return self.reject(request, player, "unknown-site") };
        let margin = ENTER_MARGIN_METRES;
        if map_x.is_nan() || map_z.is_nan() || map_x < lot.lot_x as f32 - margin || map_x > (lot.lot_x + lot.width) as f32 + margin
            || map_z < lot.lot_z as f32 - margin || map_z > (lot.lot_z + lot.depth) as f32 + margin
        {
            return self.reject(request, player, "not-on-lot");
        }
        let inventory = inventory_location_id(player);
        let Some(index) = self.state.locations.iter().position(|l| l.id == inventory) else { return self.reject(request, player, "no-inventory") };
        if self.state.locations[index].site_id == site_id {
            return self.reject(request, player, "already-there");
        }
        if self.state.lots.iter().any(|l| l.location_id == inventory && self.reserved(&l.id)) {
            return self.reject(request, player, "reserved");
        }
        self.state.locations[index].site_id = site_id.to_string();
        for lot in self.state.lots.iter_mut().filter(|l| l.location_id == inventory) {
            lot.owner_id = site_id.to_string();
        }
        for held in self.state.equipment.iter_mut().filter(|e| e.state == EquipmentState::Held && e.holder_id == player) {
            held.site_id = site_id.to_string();
        }
        if self.release_staff(player) {
            self.invalidate_diners();
        }
        self.record(request, player, true, "entered", "")
    }

    /// The site holding an actor's carried location: where the actor works.
    pub fn carried_site_of(&self, player: &str) -> Option<String> { self.state.location(&inventory_location_id(player)).map(|l| l.site_id.clone()) }

    /// Carried locations whose holder lost the grant, for validation.
    pub fn carrying_problem(&self) -> Option<String> {
        for location in self.state.locations.iter().filter(|l| l.id.starts_with(CARRIED_PREFIX)) {
            let holder = &location.id[CARRIED_PREFIX.len()..];
            if !self.state.can_view(holder, &location.site_id) {
                return Some(format!("{} is on a site its holder is not granted.", location.id));
            }
        }
        None
    }
}
