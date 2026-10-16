// Station jobs (Unity GoodsWorld.Production.cs). A job keeps copies of its consumed inputs and its recipe output, so recovery
// and pickup never depend on registered recipe content. Stations start jobs on request, or by themselves when automatic jobs
// are on: an idle station starts the first ready recipe of its kind by ID (only its chosen recipe when it has one). Menu items
// (sale recipes) are bought by customers and never started. A manually powered kind runs only while switched on; a
// worker-bound kind runs only while a paid worker works it.
use super::state::*;
use super::{CashNote, GoodsWorld, RecipeDefinition, Result, AUTOMATIC_STARTER, COUNTER_KIND, EMPLOYEE_PREFIX};
use super::company::LEDGER_SALE;

/// Most-exposed edible lots are consumed first; lot ID breaks ties. None if any input is short. Pure, so clients ask it of a
/// baseline (which carries no reservations).
pub fn input_plan(state: &Snapshot, station: &Station, recipe: &RecipeDefinition) -> Option<Vec<(String, i32)>> {
    let available = |lot: &Lot| lot.quantity - state.reservations.iter().filter(|x| x.active && x.lot_id == lot.id).map(|x| x.quantity).sum::<i32>();
    let mut plan = vec![];
    for input in &recipe.inputs {
        let mut needed = input.quantity;
        let mut candidates: Vec<&Lot> = state.lots.iter()
            .filter(|x| x.location_id == station.input_location_id && x.item_id == input.item_id && x.owner_id == station.site_id && !x.spoiled && available(x) > 0)
            .collect();
        candidates.sort_by(|a, b| b.exposure_seconds.cmp(&a.exposure_seconds).then_with(|| a.id.cmp(&b.id)));
        for lot in candidates {
            if needed == 0 {
                break;
            }
            let take = needed.min(available(lot));
            plan.push((lot.id.clone(), take));
            needed -= take;
        }
        if needed > 0 {
            return None;
        }
    }
    Some(plan)
}

pub(crate) fn output_lot(job: &Job, station: &Station, location_id: &str, exposure: i64) -> Lot {
    let exposure = exposure.min(job.output_spoil_after_seconds);
    Lot {
        id: job.output_lot_id(), item_id: job.output_item_id.clone(), owner_id: station.site_id.clone(), location_id: location_id.to_string(),
        quantity: job.output_quantity, exposure_seconds: exposure, spoil_after_seconds: job.output_spoil_after_seconds,
        spoiled: exposure >= job.output_spoil_after_seconds, belt_position: 0,
    }
}

impl GoodsWorld {
    pub fn register_manual_power(&mut self, kind: &str) { self.content.manual_power_kinds.insert(kind.to_string()); }

    pub fn requires_power(&self, kind: &str) -> bool { self.content.manual_power_kinds.contains(kind) }

    pub fn register_worker_bound(&mut self, kind: &str) -> Result<()> {
        if kind.trim().is_empty() || kind == COUNTER_KIND {
            return Err("Invalid equipment kind.".into());
        }
        self.content.worker_kinds.insert(kind.to_string());
        Ok(())
    }

    pub fn requires_worker(&self, kind: &str) -> bool { self.content.worker_kinds.contains(kind) }

    /// Recipes a station kind can be set to: its goods recipes, by ID.
    pub fn selectable_recipes(&self, kind: &str) -> Vec<String> {
        self.content.recipes.values().filter(|x| x.station_kind == kind && !x.is_sale()).map(|x| x.id.clone()).collect()
    }

    /// Item IDs a station kind's recipes consume, in ID order; empty for a kind with no recipes, which takes any goods.
    pub fn recipe_inputs(&self, kind: &str) -> Vec<String> {
        let mut items: Vec<String> = self.content.recipes.values().filter(|x| x.station_kind == kind).flat_map(|x| x.inputs.iter().map(|i| i.item_id.clone())).collect();
        items.sort();
        items.dedup();
        items
    }

    pub fn register_recipe(&mut self, recipe: RecipeDefinition) -> Result<()> {
        let mut seen = std::collections::HashSet::new();
        let bad = recipe.id.trim().is_empty() || recipe.station_kind.trim().is_empty() || recipe.duration_seconds < 1 || recipe.inputs.is_empty()
            || recipe.inputs.iter().any(|x| x.item_id.trim().is_empty() || x.quantity < 1 || !seen.insert(x.item_id.clone()))
            || recipe.sale_cents < 0 || recipe.tier < 1
            || if recipe.is_sale() {
                !recipe.output_item_id.is_empty() || recipe.output_quantity != 0 || recipe.output_spoil_after_seconds != 0
            } else {
                recipe.output_item_id.trim().is_empty() || recipe.output_quantity < 1 || recipe.output_spoil_after_seconds < 1
            }
            || self.content.recipes.contains_key(&recipe.id);
        if bad {
            return Err(format!("Invalid or duplicate recipe {}.", recipe.id));
        }
        self.content.recipes.insert(recipe.id.clone(), recipe);
        self.invalidate_diners();
        Ok(())
    }

    /// False only for a manually powered station whose piece is switched off.
    fn powered(&self, station: &Station) -> bool {
        !self.content.manual_power_kinds.contains(&station.kind) || self.state.equipment.iter().any(|x| x.id == station.id && x.powered_on)
    }

    /// False only for a worker-bound station nobody works, or whose worker is an unpaid employee.
    fn worked(&self, station: &Station) -> bool {
        if !self.content.worker_kinds.contains(&station.kind) {
            return true;
        }
        let staff = self.state.equipment.iter().find(|x| x.id == station.id).map(|x| x.staff_id.as_str()).unwrap_or("");
        !staff.is_empty() && !self.unpaid(staff)
    }

    pub(crate) fn unpaid(&self, actor: &str) -> bool {
        !actor.is_empty() && actor.starts_with(EMPLOYEE_PREFIX) && self.state.employees.iter().any(|x| x.id == actor && x.unpaid)
    }

    /// Sets the recipe a placed machine makes: one of its kind's goods recipes, or empty for automatic.
    pub fn select_recipe(&mut self, player: &str, request: &str, equipment_id: &str, recipe_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(index) = self.state.equipment.iter().position(|x| x.id == equipment_id) else { return self.record(request, player, false, "forbidden", "") };
        let piece = self.state.equipment[index].clone();
        if !self.state.can_view(player, &piece.site_id) {
            return self.record(request, player, false, "forbidden", "");
        }
        if !piece.placed() {
            return self.record(request, player, false, "not-placed", "");
        }
        if !recipe_id.is_empty() && !self.content.recipes.get(recipe_id).is_some_and(|r| !r.is_sale() && r.station_kind == piece.kind) {
            return self.record(request, player, false, "invalid-recipe", "");
        }
        self.state.equipment[index].recipe_id = recipe_id.to_string();
        if self.content.automatic_jobs {
            self.start_ready_job(&piece.id);
        }
        self.record(request, player, true, "recipe-set", "")
    }

    /// Switches a placed, manually powered machine. A player's switch-off holds against employees until a player switches on.
    pub fn set_power(&mut self, player: &str, request: &str, equipment_id: &str, on: bool) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(index) = self.state.equipment.iter().position(|x| x.id == equipment_id) else { return self.record(request, player, false, "forbidden", "") };
        let piece = self.state.equipment[index].clone();
        if !self.state.can_view(player, &piece.site_id) {
            return self.record(request, player, false, "forbidden", "");
        }
        if !piece.placed() {
            return self.record(request, player, false, "not-placed", "");
        }
        if !self.content.manual_power_kinds.contains(&piece.kind) {
            return self.record(request, player, false, "no-power-switch", "");
        }
        let employee = self.state.employees.iter().any(|x| x.id == player);
        if on && employee && piece.held_off {
            return self.record(request, player, false, "held-off", "");
        }
        let target = &mut self.state.equipment[index];
        target.powered_on = on;
        target.held_off = !on && (target.held_off || !employee);
        if on && self.content.automatic_jobs {
            self.start_ready_job(&piece.id);
        }
        self.record(request, player, true, if on { "powered-on" } else { "powered-off" }, "")
    }

    /// Starts one batch on an idle station on request.
    pub fn start_job(&mut self, player: &str, request: &str, station_id: &str, recipe_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(station) = self.state.stations.iter().find(|x| x.id == station_id).cloned() else { return self.record(request, player, false, "forbidden", "") };
        if !self.state.can_view(player, &station.site_id) {
            return self.record(request, player, false, "forbidden", "");
        }
        let Some(recipe) = self.content.recipes.get(recipe_id).filter(|r| r.station_kind == station.kind).cloned() else {
            return self.record(request, player, false, "invalid-recipe", "");
        };
        if recipe.is_sale() {
            return self.record(request, player, false, "customers-only", "");
        }
        if self.state.jobs.iter().any(|x| x.station_id == station.id) {
            return self.record(request, player, false, "station-busy", "");
        }
        let Some(plan) = input_plan(&self.state, &station, &recipe) else { return self.record(request, player, false, "missing-inputs", "") };
        if !self.powered(&station) {
            return self.record(request, player, false, "powered-off", "");
        }
        if !self.worked(&station) {
            return self.record(request, player, false, "unstaffed", "");
        }
        let job = self.add_job(&station, &recipe, plan, player);
        let mut result = self.record(request, player, true, "started", "");
        result.job_id = job.clone();
        if let Some(last) = self.state.outcomes.last_mut() {
            last.job_id = job;
        }
        result
    }

    fn add_job(&mut self, station: &Station, recipe: &RecipeDefinition, plan: Vec<(String, i32)>, started_by: &str) -> String {
        let id = self.new_unique("job-");
        let mut job = Job {
            id: id.clone(), station_id: station.id.clone(), recipe_id: recipe.id.clone(), started_by: started_by.to_string(),
            started_at_seconds: self.state.clock_seconds, duration_seconds: recipe.duration_seconds, remaining_seconds: recipe.duration_seconds,
            state: JobState::Running, output_item_id: recipe.output_item_id.clone(), output_quantity: recipe.output_quantity,
            output_spoil_after_seconds: recipe.output_spoil_after_seconds, sale_cents: recipe.sale_cents, inputs: vec![],
        };
        for (lot_id, take) in plan {
            let index = self.state.lots.iter().position(|x| x.id == lot_id).expect("planned lot exists");
            let lot = &mut self.state.lots[index];
            job.inputs.push(Lot {
                id: format!("{id}:in:{}", job.inputs.len()), item_id: lot.item_id.clone(), owner_id: lot.owner_id.clone(), location_id: String::new(),
                quantity: take, exposure_seconds: lot.exposure_seconds, spoil_after_seconds: lot.spoil_after_seconds, spoiled: false, belt_position: 0,
            });
            lot.quantity -= take;
            if lot.quantity == 0 {
                self.state.lots.remove(index);
            }
        }
        self.state.jobs.push(job);
        id
    }

    pub(crate) fn start_ready_jobs(&mut self) {
        if !self.content.automatic_jobs {
            return;
        }
        let ids: Vec<String> = self.state.stations.iter().map(|x| x.id.clone()).collect();
        for id in ids {
            self.start_ready_job(&id);
        }
    }

    /// Starts the first ready recipe on an idle station; true when one started.
    pub(crate) fn start_ready_job(&mut self, station_id: &str) -> bool {
        let Some(station) = self.state.stations.iter().find(|x| x.id == station_id).cloned() else { return false };
        if self.state.jobs.iter().any(|x| x.station_id == station.id) || !self.powered(&station) || !self.worked(&station) {
            return false;
        }
        let chosen = self.state.equipment.iter().find(|x| x.id == station.id).map(|x| x.recipe_id.clone()).unwrap_or_default();
        let candidates: Vec<RecipeDefinition> = self.content.recipes.values()
            .filter(|x| x.station_kind == station.kind && !x.is_sale() && (chosen.is_empty() || x.id == chosen)).cloned().collect();
        for recipe in candidates {
            if !self.fits(&station.output_location_id, &recipe.output_item_id, false, recipe.output_quantity) {
                continue;
            }
            let Some(plan) = input_plan(&self.state, &station, &recipe) else { continue };
            self.add_job(&station, &recipe, plan, AUTOMATIC_STARTER);
            return true;
        }
        false
    }

    pub(crate) fn emit_blocked_outputs(&mut self) {
        let blocked: Vec<Job> = self.state.jobs.iter().filter(|x| x.state == JobState::Blocked).cloned().collect();
        for job in blocked {
            let Some(station) = self.state.stations.iter().find(|x| x.id == job.station_id).cloned() else { continue };
            let lot = output_lot(&job, &station, &station.output_location_id, 0);
            if !self.fits(&lot.location_id, &lot.item_id, lot.spoiled, lot.quantity) {
                continue;
            }
            self.state.lots.push(lot);
            self.state.jobs.retain(|x| x.id != job.id);
        }
    }

    /// Completion is exactly started + duration; ambient overshoot past that counts as output exposure.
    pub(crate) fn progress_jobs(&mut self, seconds: i64) {
        let paused: Vec<String> = self.state.stations.iter().filter(|x| !self.powered(x) || !self.worked(x)).map(|x| x.id.clone()).collect();
        let running: Vec<String> = self.state.jobs.iter().filter(|x| x.state == JobState::Running && !paused.contains(&x.station_id)).map(|x| x.id.clone()).collect();
        for job_id in running {
            let Some(index) = self.state.jobs.iter().position(|x| x.id == job_id) else { continue };
            let worked = seconds.min(self.state.jobs[index].remaining_seconds);
            self.state.jobs[index].remaining_seconds -= worked;
            if self.state.jobs[index].remaining_seconds > 0 {
                continue;
            }
            let job = self.state.jobs[index].clone();
            let Some(station) = self.state.stations.iter().find(|x| x.id == job.station_id).cloned() else { continue };
            if job.is_sale() {
                self.try_complete_sale(&job, &station);
                continue;
            }
            let Some(output) = self.state.location(&station.output_location_id).cloned() else { continue };
            let lot = output_lot(&job, &station, &output.id, if output.refrigerated { 0 } else { seconds - worked });
            if !self.fits(&output.id, &lot.item_id, lot.spoiled, lot.quantity) {
                self.state.jobs[index].state = JobState::Blocked;
                continue;
            }
            self.state.lots.push(lot);
            self.state.jobs.remove(index);
        }
    }

    /// A legacy sale job completes by paying the site's company.
    fn try_complete_sale(&mut self, job: &Job, station: &Station) -> bool {
        let Some(company) = self.state.company_of_site(&station.site_id).map(|c| (c.id.clone(), c.cash)) else { return false };
        if company.1 > i64::MAX - job.sale_cents {
            return false;
        }
        let at = self.state.clock_seconds.min(job.started_at_seconds + job.duration_seconds);
        self.try_credit(&company.0, job.sale_cents, CashNote::new(LEDGER_SALE, &station.site_id).equipment(&station.id).offer(&job.recipe_id).at(at));
        self.state.jobs.retain(|x| x.id != job.id);
        true
    }
}
