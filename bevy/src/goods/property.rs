// Buying generated buildings (Unity GoodsWorld.Property.cs). Each purchasable building of the stored layout is listed as a
// PropertyOffer (content derived from the layout, never saved). A lot's site is created on its first purchase, in the one
// mutation that debits the buyer's company, records ownership as its own Property record and grants the buyer and every
// teammate the new site. A generated restaurant's back door comes with its shell, and its starter dock is placed beside it.
use std::collections::BTreeMap;

use super::buildings::{building_problem, is_corner};
use super::company::LEDGER_PROPERTY;
use super::grid;
use super::rules;
use super::state::*;
use super::*;

pub const MAX_SITE_NAME_LENGTH: usize = 32;

pub fn starter_dock_id(site_id: &str) -> String { format!("dock:{site_id}") }

/// The shell a listed lot comes with: its doors and back doors (a door record in the service role).
pub fn building_of(offer: &PropertyOffer) -> Building {
    Building {
        id: offer.building_id.clone(),
        site_id: offer.site_id.clone(),
        kind: offer.category.clone(),
        cell_x: offer.building_x,
        cell_z: offer.building_z,
        width: offer.building_width,
        depth: offer.building_depth,
        floors: offer.floors,
        doors: offer.doors.iter().chain(offer.back_doors.iter()).copied().collect(),
        structures: offer.back_doors.iter().map(|d| Structure {
            kind: DOOR_STRUCTURE.into(), x: d.x, z: d.z, style: BACK_DOOR_STYLE.into(), role: SERVICE_DOOR_ROLE.into(), ..Default::default()
        }).collect(),
        ..Default::default()
    }
}

fn offer_problem(offer: &PropertyOffer) -> Option<&'static str> {
    if offer.lot_id.trim().is_empty() || offer.site_id.trim().is_empty() || offer.site_id == ROAD_SITE_ID || offer.building_id.trim().is_empty()
        || offer.price_cents < 1 || offer.width < 1 || offer.depth < 1
        || (!offer.is_shell() && offer.category != FARM_CATEGORY && offer.category != STATION_CATEGORY)
        || offer.price_percent < 1 || offer.building_x < 0 || offer.building_z < 0 || offer.building_width < 1 || offer.building_depth < 1
        || offer.building_x + offer.building_width > offer.width || offer.building_z + offer.building_depth > offer.depth
    {
        return Some("invalid-offer");
    }
    if !offer.is_shell() {
        return None;
    }
    let mut probe = Snapshot::new("offer");
    probe.site_layouts.push(SiteLayout { site_id: offer.site_id.clone(), width: offer.width, depth: offer.depth });
    building_problem(&probe, &building_of(offer))
}

/// Gives a restaurant without a back door one, free of charge, on an outer wall cell whose doorstep the street reaches.
pub(crate) fn add_back_door(state: &mut Snapshot, building_index: usize, offer: Option<&PropertyOffer>) -> bool {
    let building = state.buildings[building_index].clone();
    if building.kind != RESTAURANT_KIND || !grid::service_doors(&building).is_empty() {
        return false;
    }
    let Some(layout) = state.layout_of(&building.site_id).cloned() else { return false };
    let walk = rules::walkable(state, &building.site_id, None, false);
    let reached = rules::reached(walk.as_ref(), rules::street_cells(Some(&layout), offer));
    let fronts: Vec<(i32, i32)> = grid::customer_doors(&building).iter().filter_map(|d| grid::doorstep(&building, d.0, d.1)).collect();
    let cells: Vec<(i32, i32)> = if building.free_walls {
        building.structures.iter().filter(|s| s.kind == PARTITION_STRUCTURE && !grid::has_structure(&building, DOOR_STRUCTURE, s.x, s.z) && !is_corner(&building, s.x, s.z)).map(|s| (s.x, s.z)).collect()
    } else {
        let mut cells = vec![];
        for x in building.cell_x..building.cell_x + building.width {
            for z in building.cell_z..building.cell_z + building.depth {
                if grid::is_door_cell(&building, x, z) && !building.doors.iter().any(|d| d.x == x && d.z == z) {
                    cells.push((x, z));
                }
            }
        }
        cells
    };
    let mut best: Option<((i32, i32), bool, i32)> = None;
    for door in cells.into_iter().filter(|c| grid::window_at(&building, c.0, c.1).is_none()) {
        let Some(step) = grid::doorstep(&building, door.0, door.1) else { continue };
        if !reached.contains(&step) || rules::covered(state, &building.site_id, step.0, step.1) {
            continue;
        }
        let (ix, iz) = (2 * door.0 - step.0, 2 * door.1 - step.1);
        let inside = walk.as_ref().is_some_and(|w| w.get(ix, iz));
        let far = fronts.iter().map(|f| (f.0 - step.0).abs() + (f.1 - step.1).abs()).min().unwrap_or(0);
        let better = match best {
            None => true,
            Some((d, i, f)) => (inside, far, std::cmp::Reverse(door.0), std::cmp::Reverse(door.1)) > (i, f, std::cmp::Reverse(d.0), std::cmp::Reverse(d.1)),
        };
        if better {
            best = Some((door, inside, far));
        }
    }
    let Some((door, _, _)) = best else { return false };
    let target = &mut state.buildings[building_index];
    if !target.free_walls {
        target.doors.push(GridCell { x: door.0, z: door.1 });
    }
    target.structures.push(Structure { kind: DOOR_STRUCTURE.into(), x: door.0, z: door.1, style: BACK_DOOR_STYLE.into(), role: SERVICE_DOOR_ROLE.into(), ..Default::default() });
    true
}

impl GoodsWorld {
    /// Server content, registered once at start.
    pub fn register_property_offers(&mut self, offers: Vec<PropertyOffer>) -> Result<()> {
        if self.content.property_offers.is_some() {
            return Err("Duplicate property catalog.".into());
        }
        let mut catalog = BTreeMap::new();
        let mut sites = std::collections::HashSet::new();
        for offer in offers {
            if offer_problem(&offer).is_some() || catalog.contains_key(&offer.lot_id) || !sites.insert(offer.site_id.clone()) {
                return Err(format!("Invalid or duplicate property offer '{}'.", offer.lot_id));
            }
            catalog.insert(offer.lot_id.clone(), offer);
        }
        for property in &self.state.properties {
            if !catalog.get(&property.lot_id).is_some_and(|o| o.site_id == property.site_id) {
                return Err(format!("Property {} is not a listed lot.", property.lot_id));
            }
        }
        self.content.property_offers = Some(catalog);
        Ok(())
    }

    pub fn property_offers(&self) -> Vec<PropertyOffer> { self.content.property_offers.as_ref().map(|c| c.values().cloned().collect()).unwrap_or_default() }

    fn owned(&self, offer: &PropertyOffer) -> bool {
        self.state.properties.iter().any(|x| x.lot_id == offer.lot_id || x.site_id == offer.site_id)
            || Self::site_exists(&self.state, &offer.site_id)
            || self.state.layout_of(&offer.site_id).is_some()
            || self.state.company_of_site(&offer.site_id).is_some()
    }

    /// Every player (not employee) granted any site of the company, in grant order.
    fn teammates(&self, company_id: &str) -> Vec<String> {
        let sites = self.state.companies.iter().find(|c| c.id == company_id).map(|c| c.site_ids.clone()).unwrap_or_default();
        let mut players = vec![];
        for grant in &self.state.grants {
            if sites.contains(&grant.site_id) && !grant.player_id.starts_with(EMPLOYEE_PREFIX) && !players.contains(&grant.player_id) {
                players.push(grant.player_id.clone());
            }
        }
        players
    }

    pub fn buy_property(&mut self, player: &str, request: &str, paying_site: &str, lot_id: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        let Some(offer) = self.content.property_offers.as_ref().and_then(|c| c.get(lot_id)).cloned() else { return self.reject(request, player, "unknown-lot") };
        if !offer.for_sale {
            return self.reject(request, player, "not-for-sale");
        }
        if self.owned(&offer) {
            return self.reject(request, player, "owned");
        }
        if !self.state.can_view(player, paying_site) {
            return self.reject(request, player, "no-grant");
        }
        let Some(company) = self.company_of_site(paying_site) else { return self.reject(request, player, "no-company") };
        if self.cash_of(&company) < offer.price_cents {
            return self.reject(request, player, "insufficient-funds");
        }
        self.try_debit(&company, offer.price_cents, CashNote::new(LEDGER_PROPERTY, paying_site).request(request).offer(lot_id));
        let teammates = self.teammates(&company);
        if self.create_property(&offer, &company).is_err() {
            return self.reject(request, player, "invalid-offer");
        }
        self.place_starter_dock(&offer);
        let _ = self.grant(player, &offer.site_id);
        for teammate in teammates {
            let _ = self.grant(&teammate, &offer.site_id);
        }
        self.record(request, player, true, "property-bought", "")
    }

    /// Renames a site of the player's company.
    pub fn rename_site(&mut self, player: &str, request: &str, site_id: &str, name: &str) -> Outcome {
        if let Some(done) = self.begin(player, request) {
            return done;
        }
        if self.state.site(site_id).is_none() || !self.state.can_view(player, site_id) {
            return self.reject(request, player, "forbidden");
        }
        if self.company_of_site(site_id).is_none() {
            return self.reject(request, player, "no-company");
        }
        let trimmed = name.trim();
        if !employees::is_valid_employee_name(trimmed) || trimmed.chars().count() > MAX_SITE_NAME_LENGTH {
            return self.reject(request, player, "invalid-name");
        }
        if let Some(site) = self.state.sites.iter_mut().find(|s| s.id == site_id) {
            site.name = trimmed.to_string();
        }
        self.record(request, player, true, "renamed", "")
    }

    /// Server-only, for world creation: gives a listed lot to a company without charge or grant.
    pub fn bootstrap_property(&mut self, offer: &PropertyOffer, company_id: &str) -> Result<()> {
        if offer_problem(offer).is_some() || !self.state.companies.iter().any(|c| c.id == company_id) || self.owned(offer) {
            return Err("Invalid, unlisted or already owned property, or an unknown company.".into());
        }
        let before = self.state.clone();
        if let Err(error) = self.create_property(offer, company_id) {
            self.replace_state(before);
            return Err(error);
        }
        Ok(())
    }

    /// The dock that came with a bought restaurant, placed free of charge from the supplier's dock content.
    pub(crate) fn place_starter_dock(&mut self, offer: &PropertyOffer) {
        let Some(template) = self.content.equipment_offers.values().map(|x| x.equipment.clone()).find(|x| x.kind == DOCK_KIND) else { return };
        if !offer.has_dock {
            return;
        }
        let mut dock = template;
        dock.id = starter_dock_id(&offer.site_id);
        dock.site_id = offer.site_id.clone();
        dock.state = EquipmentState::Placed;
        dock.holder_id.clear();
        dock.cell_x = offer.dock_x;
        dock.cell_z = offer.dock_z;
        dock.rotation = offer.dock_rotation;
        dock.level = 0;
        dock.charged_cents = 0;
        dock.staff_id.clear();
        if self.state.equipment.iter().any(|x| x.id == dock.id) || grid::placement_problem(&self.state, &dock, dock.cell_x, dock.cell_z, dock.rotation, 0).is_some() {
            return;
        }
        self.add_placed_parts(&dock);
        self.state.equipment.push(dock);
    }

    fn create_property(&mut self, offer: &PropertyOffer, company_id: &str) -> Result<()> {
        if self.state.site(&offer.site_id).is_some() {
            return Err("site exists".into());
        }
        self.state.sites.push(Site { id: offer.site_id.clone(), name: offer.building_id.clone(), map_x: offer.access_x, map_z: offer.access_z });
        self.bootstrap_layout(SiteLayout { site_id: offer.site_id.clone(), width: offer.width, depth: offer.depth })?;
        if offer.is_shell() {
            self.bootstrap_building(building_of(offer))?;
            let index = self.state.buildings.iter().position(|b| b.id == offer.building_id).expect("just added");
            add_back_door(&mut self.state, index, Some(offer));
        }
        self.state.properties.push(Property { lot_id: offer.lot_id.clone(), site_id: offer.site_id.clone(), company_id: company_id.to_string() });
        if let Some(company) = self.state.companies.iter_mut().find(|c| c.id == company_id) {
            company.site_ids.push(offer.site_id.clone());
        }
        self.site_points.remove(&offer.site_id);
        self.invalidate_diners();
        self.state.revision += 1;
        Ok(())
    }
}
