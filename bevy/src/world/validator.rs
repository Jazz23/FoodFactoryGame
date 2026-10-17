// Independent checks of a finished layout, recomputed from its data rather than trusted from the generator (Unity
// WorldLayoutValidator): identity, road graph shape, shell rules (decision 0019), overlaps, lots and service yards, road access
// and connectivity, the starting restaurant, farms and stations, district placement (industrial on one city edge facing
// farmland, farms outside the city), land, junction controls, rivers and bridges, level crossings and trees. The generator
// always writes the newest layout format, so every format-gated check runs. Problems come back sorted.
use std::collections::{HashMap, HashSet, VecDeque};

use super::geometry::{self as geo, Box2, BoxIndex};
use super::layout::*;
use super::settings::WorldSettings;

pub const STATION_NEAR_FARM_METRES: i64 = 300;

type Nodes<'a> = HashMap<&'a str, &'a RoadNode>;

pub fn validate(layout: &WorldLayout, settings: &WorldSettings) -> Vec<String> {
    let mut problems = vec![];
    let (c, m) = (layout.city_half_size, layout.map_half_size);
    if c <= 0 || m <= c {
        problems.push("bounds: map must be larger than the city".to_string());
    }
    let mut ids: HashSet<&str> = HashSet::new();
    let all_ids = layout.districts.iter().map(|x| x.id.as_str())
        .chain(layout.nodes.iter().map(|x| x.id.as_str())).chain(layout.roads.iter().map(|x| x.id.as_str()))
        .chain(layout.rails.iter().map(|x| x.id.as_str())).chain(layout.buildings.iter().map(|x| x.id.as_str()))
        .chain(layout.rivers.iter().map(|x| x.id.as_str())).chain(layout.bridges.iter().map(|x| x.id.as_str()))
        .chain(layout.crossings.iter().map(|x| x.id.as_str())).chain(layout.lots.iter().map(|x| x.id.as_str()));
    for id in all_ids {
        if id.trim().is_empty() || id.chars().any(char::is_whitespace) || !ids.insert(id) {
            problems.push(format!("id: blank, spaced or duplicate '{id}'"));
        }
    }
    districts(layout, &mut problems);
    let mut nodes: Nodes = HashMap::new();
    for node in &layout.nodes { nodes.entry(node.id.as_str()).or_insert(node); }
    let roads = roads(layout, &nodes, &mut problems);
    let rails = rails(layout, &mut problems);
    buildings(layout, &roads, &rails, &mut problems);
    lots(layout, &roads, &rails, &mut problems);
    access(layout, settings, &roads, &mut problems);
    start(layout, settings, &mut problems);
    farms_and_stations(layout, &mut problems);
    land(layout, settings, &mut problems);
    controls(layout, &mut problems);
    rivers(layout, &nodes, &mut problems);
    level_crossings(layout, &nodes, &mut problems);
    trees(layout, &nodes, &roads, &rails, &mut problems);
    problems.sort();
    problems
}

const SIDES: [Facing; 4] = [Facing::North, Facing::East, Facing::South, Facing::West];
const KINDS: [DistrictKind; 4] = [DistrictKind::Downtown, DistrictKind::Residential, DistrictKind::Wealthy, DistrictKind::Industrial];

fn districts(layout: &WorldLayout, problems: &mut Vec<String>) {
    let c = layout.city_half_size;
    for kind in KINDS {
        if layout.districts.iter().filter(|d| d.kind == kind).count() != 1 {
            problems.push(format!("district: need exactly one {kind:?}"));
        }
    }
    let city = Box2::new(-2 * c, -2 * c, 2 * c, 2 * c);
    let mut area: i64 = 0;
    let mut all: Vec<(&WorldDistrict, WorldRect)> = vec![];
    for district in &layout.districts {
        if district.areas.is_empty() { problems.push(format!("district {}: no area", district.id)); }
        if district.min_recipe_tier < 1 || district.customers_per_hour < 0 || district.traffic_percent < 0 || district.price_percent <= 0
            || district.cuisines.is_empty() || district.cuisines.iter().any(|x| x.cuisine.trim().is_empty() || x.weight <= 0) {
            problems.push(format!("district {}: invalid demand values", district.id));
        }
        for rect in &district.areas {
            let b = geo::of_rect(rect);
            if rect.width <= 0 || rect.depth <= 0 || b.x0 < city.x0 || b.z0 < city.z0 || b.x1 > city.x1 || b.z1 > city.z1 {
                problems.push(format!("district {}: area outside the city", district.id));
            }
            if all.iter().any(|x| geo::of_rect(&x.1).overlaps(&b)) { problems.push(format!("district {}: areas overlap", district.id)); }
            all.push((district, *rect));
            area += rect.width as i64 * rect.depth as i64;
        }
    }
    if area != 4 * c as i64 * c as i64 { problems.push("district: areas do not cover the city exactly".to_string()); }
    // Industrial must hold one whole city edge, which faces the farmland; downtown holds the centre.
    if layout.districts.iter().any(|d| d.kind == DistrictKind::Industrial && !d.areas.is_empty()) {
        match industrial_side(layout) {
            None => problems.push("district: industrial does not lie along a single city edge".to_string()),
            Some(side) if all.iter().any(|x| x.0.kind != DistrictKind::Industrial && touches_edge(&x.1, side, c)) => {
                problems.push("district: a non-industrial district shares the industrial city edge".to_string())
            }
            Some(side) if !layout.buildings.iter().any(|b| b.category == BuildingCategory::Farm && beyond(&b.footprint(), side, c)) => {
                problems.push("district: no farmland beyond the industrial edge".to_string())
            }
            _ => {}
        }
    }
    if let Some(downtown) = layout.districts.iter().find(|d| d.kind == DistrictKind::Downtown) {
        if !downtown.areas.iter().any(|a| geo::of_rect(a).contains_doubled(0, 0)) {
            problems.push("district: downtown does not hold the city centre".to_string());
        }
    }
}

/// The city edge that every industrial area touches, or none when there is no single such edge.
pub fn industrial_side(layout: &WorldLayout) -> Option<Facing> {
    let industrial = layout.districts.iter().find(|d| d.kind == DistrictKind::Industrial).filter(|d| !d.areas.is_empty())?;
    let mut found = None;
    for side in SIDES {
        if industrial.areas.iter().all(|a| touches_edge(a, side, layout.city_half_size)) {
            if found.is_some() { return None; }
            found = Some(side);
        }
    }
    found
}

fn touches_edge(rect: &WorldRect, side: Facing, c: i32) -> bool {
    match side {
        Facing::North => rect.z + rect.depth == c,
        Facing::South => rect.z == -c,
        Facing::East => rect.x + rect.width == c,
        Facing::West => rect.x == -c,
    }
}

pub fn beyond(rect: &WorldRect, side: Facing, c: i32) -> bool {
    match side {
        Facing::North => rect.z >= c,
        Facing::South => rect.z + rect.depth <= -c,
        Facing::East => rect.x >= c,
        Facing::West => rect.x + rect.width <= -c,
    }
}

fn roads(layout: &WorldLayout, nodes: &Nodes, problems: &mut Vec<String>) -> BoxIndex<usize> {
    let mut index = BoxIndex::new();
    for (i, road) in layout.roads.iter().enumerate() {
        let (Some(from), Some(to)) = (nodes.get(road.from_id.as_str()), nodes.get(road.to_id.as_str())) else {
            problems.push(format!("road {}: unknown node", road.id));
            continue;
        };
        if (from.x != to.x && from.z != to.z) || (from.x == to.x && from.z == to.z) {
            problems.push(format!("road {}: not an axis-aligned segment", road.id));
        }
        if road.width <= 0 || road.width % 2 != 0 || road.capacity_per_hour <= 0 { problems.push(format!("road {}: invalid width or capacity", road.id)); }
        if from.x.abs().max(from.z.abs()).max(to.x.abs()).max(to.z.abs()) > layout.map_half_size { problems.push(format!("road {}: outside the map", road.id)); }
        index.add(geo::strip(from.x, from.z, to.x, to.z, road.width), i);
    }
    index
}

fn rails(layout: &WorldLayout, problems: &mut Vec<String>) -> BoxIndex<usize> {
    let mut index = BoxIndex::new();
    if layout.rails.is_empty() { problems.push("rail: no lines".to_string()); }
    for (i, line) in layout.rails.iter().enumerate() {
        if line.points.len() < 2 || line.width <= 0 || line.width % 2 != 0 { problems.push(format!("rail {}: invalid line", line.id)); }
        for pair in line.points.windows(2) {
            let (a, b) = (pair[0], pair[1]);
            if (a.x != b.x && a.z != b.z) || a == b { problems.push(format!("rail {}: not axis-aligned", line.id)); }
            index.add(geo::strip(a.x, a.z, b.x, b.z, line.width), i);
        }
    }
    index
}

fn district_at(layout: &WorldLayout, doubled_x: i32, doubled_z: i32) -> Option<&WorldDistrict> {
    layout.districts.iter().find(|d| d.areas.iter().any(|a| doubled_x >= 2 * a.x && doubled_x < 2 * (a.x + a.width) && doubled_z >= 2 * a.z && doubled_z < 2 * (a.z + a.depth)))
}

fn buildings(layout: &WorldLayout, roads: &BoxIndex<usize>, rails: &BoxIndex<usize>, problems: &mut Vec<String>) {
    let (c, m) = (layout.city_half_size, layout.map_half_size);
    let city = Box2::new(-2 * c, -2 * c, 2 * c, 2 * c);
    let mut placed: BoxIndex<usize> = BoxIndex::new();
    let lines: HashSet<&str> = layout.rails.iter().map(|r| r.id.as_str()).collect();
    for (i, building) in layout.buildings.iter().enumerate() {
        let name = format!("building {}", building.id);
        let b = geo::of_building(building);
        let minimum = if building.is_shell() { 3 } else { 1 };
        if building.width < minimum || building.depth < minimum { problems.push(format!("{name}: too small")); }
        if building.x < -m || building.z < -m || building.x + building.width > m || building.z + building.depth > m { problems.push(format!("{name}: outside the map")); }
        if building.doors.is_empty() { problems.push(format!("{name}: no door")); }
        if building.doors.iter().any(|d| !geo::is_door_on_facing(building, *d)) { problems.push(format!("{name}: door not a non-corner cell of the street wall")); }
        if building.doors.iter().collect::<HashSet<_>>().len() != building.doors.len() { problems.push(format!("{name}: duplicate door")); }
        if building.is_shell() != building.model_key.is_empty() && building.category != BuildingCategory::Station {
            problems.push(format!("{name}: shells have no model key and premade buildings need one"));
        }
        if building.category == BuildingCategory::Station && (!lines.contains(building.line_id.as_str()) || !building.model_key.is_empty()) {
            problems.push(format!("{name}: station without a rail line"));
        }
        if building.is_shell() && building.floors != 1 { problems.push(format!("{name}: a generated shell has one storey")); }
        if building.floors < 1 { problems.push(format!("{name}: no storeys")); }
        let expected = match building.category {
            BuildingCategory::Restaurant => building.ownership == Ownership::ForSale || building.ownership == Ownership::Competitor
                || (building.ownership == Ownership::Player && building.id == layout.start_restaurant_id),
            BuildingCategory::Factory | BuildingCategory::Farm | BuildingCategory::Station => building.ownership == Ownership::ForSale,
            _ => building.ownership == Ownership::Scenery,
        };
        if !expected { problems.push(format!("{name}: ownership {:?} not allowed for a {:?}", building.ownership, building.category)); }
        if (building.ownership == Ownership::Scenery) != (building.price_cents == 0) || building.price_cents < 0 {
            problems.push(format!("{name}: price must be positive exactly for property"));
        }
        let district = district_at(layout, 2 * building.x + building.width, 2 * building.z + building.depth);
        if district.map_or("", |d| d.id.as_str()) != building.district_id { problems.push(format!("{name}: district does not match its position")); }
        let misplaced = if building.category == BuildingCategory::Farm { b.overlaps(&city) } else { building.category != BuildingCategory::Station && building.district_id.is_empty() };
        if misplaced { problems.push(format!("{name}: farms lie outside the city and city buildings inside a district")); }
        if roads.any(&b) { problems.push(format!("{name}: overlaps a road")); }
        if rails.any(&b) { problems.push(format!("{name}: overlaps a rail line")); }
        for other in placed.overlapping(&b) { problems.push(format!("{name}: overlaps {}", layout.buildings[other].id)); }
        placed.add(b, i);
    }
}

/// Decision 0028: exactly one lot per property and none for scenery, with IDs derived from the building. A lot holds its
/// building and overlaps no other lot or building, road or rail line (or water, in rivers); its access cell borders its edge
/// on a road (reachability is checked in access).
fn lots(layout: &WorldLayout, roads: &BoxIndex<usize>, rails: &BoxIndex<usize>, problems: &mut Vec<String>) {
    let mut buildings: HashMap<&str, &WorldBuilding> = HashMap::new();
    for b in &layout.buildings { buildings.entry(b.id.as_str()).or_insert(b); }
    let mut footprints: BoxIndex<usize> = BoxIndex::new();
    for (i, b) in layout.buildings.iter().enumerate() { footprints.add(geo::of_building(b), i); }
    let mut by_building: HashMap<&str, &WorldLot> = HashMap::new();
    let mut sites: HashSet<&str> = HashSet::new();
    let mut placed: BoxIndex<usize> = BoxIndex::new();
    for (i, lot) in layout.lots.iter().enumerate() {
        let name = format!("lot {}", lot.id);
        let Some(building) = buildings.get(lot.building_id.as_str()).filter(|b| b.has_lot()) else {
            problems.push(format!("{name}: not the lot of a property"));
            continue;
        };
        if by_building.insert(building.id.as_str(), lot).is_some() { problems.push(format!("{name}: {} has more than one lot", building.id)); }
        if lot.id != WorldLot::id_for(&building.id) || lot.site_id != WorldLot::site_id_for(&building.id) { problems.push(format!("{name}: IDs not derived from its building")); }
        if !sites.insert(lot.site_id.as_str()) { problems.push(format!("{name}: duplicate site ID '{}'", lot.site_id)); }
        let rect = lot.rect();
        let b = geo::of_rect(&rect);
        if lot.width < 1 || lot.depth < 1 || !geo::contains(&rect, &building.footprint()) { problems.push(format!("{name}: does not contain its building")); }
        if roads.any(&b) { problems.push(format!("{name}: overlaps a road")); }
        if rails.any(&b) { problems.push(format!("{name}: overlaps a rail line")); }
        for other in placed.overlapping(&b) { problems.push(format!("{name}: overlaps {}", layout.lots[other].id)); }
        for other in footprints.overlapping(&b).into_iter().filter(|o| layout.buildings[*o].id != building.id) {
            problems.push(format!("{name}: overlaps building {}", layout.buildings[other].id));
        }
        placed.add(b, i);
        if !borders(&rect, lot.access) {
            problems.push(format!("{name}: access is not a cell beside its edge"));
        } else if roads.find(2 * lot.access.x + 1, 2 * lot.access.z + 1).is_none() {
            problems.push(format!("{name}: access is not on a road"));
        }
    }
    for building in &layout.buildings {
        let lot = by_building.get(building.id.as_str()).copied();
        if building.has_lot() && lot.is_none() { problems.push(format!("building {}: property without a lot", building.id)); }
        if building.site_id != lot.map_or("", |l| l.site_id.as_str()) { problems.push(format!("building {}: site ID must be its lot's, and empty without one", building.id)); }
        service_yard(building, lot, problems);
    }
}

/// Decision 0037: every restaurant property has a service yard inside its lot and outside its shell that reaches the lot's
/// street edge; its back door is a non-corner wall cell on the yard side; the doorstep outside it and the 2 x 1 starter dock
/// lie in the yard, the dock touching the doorstep edge to edge without covering it. Nothing else has a yard.
fn service_yard(building: &WorldBuilding, lot: Option<&WorldLot>, problems: &mut Vec<String>) {
    let name = format!("building {}", building.id);
    let wanted = building.category == BuildingCategory::Restaurant && building.has_lot();
    let has = building.service_yard.is_some() || building.back_door.is_some() || building.service_dock.is_some();
    if !wanted {
        if has { problems.push(format!("{name}: only a restaurant property has a service yard")); }
        return;
    }
    let (Some(yard), Some(back), Some(dock), Some(lot)) = (building.service_yard, building.back_door, building.service_dock, lot) else {
        problems.push(format!("{name}: restaurant without a service yard, back door and dock"));
        return;
    };
    let footprint = building.footprint();
    let doorstep = geo::doorstep(footprint, back);
    if !geo::contains(&lot.rect(), &yard) || overlaps(&yard, &footprint) { problems.push(format!("{name}: service yard not in its lot beside the shell")); }
    match doorstep {
        Some(step) if !building.doors.contains(&back) => {
            if !yard.contains(step.x, step.z) { problems.push(format!("{name}: back door does not open into the service yard")); }
        }
        _ => problems.push(format!("{name}: back door is not a free non-corner wall cell")),
    }
    if !((dock.width == 2 && dock.depth == 1) || (dock.width == 1 && dock.depth == 2)) || !geo::contains(&yard, &dock) {
        problems.push(format!("{name}: starter dock is not a 2 x 1 piece in the yard"));
    } else if let Some(step) = doorstep {
        if dock.contains(step.x, step.z) || !borders(&dock, step) { problems.push(format!("{name}: starter dock does not touch the back door's doorstep")); }
    }
    // The yard runs to the lot's street edge, so trucks and players reach the dock from the street along it.
    let (sx, sz) = geo::step(building.facing);
    let reaches = if sx > 0 { yard.x + yard.width == lot.x + lot.width } else if sx < 0 { yard.x == lot.x }
        else if sz > 0 { yard.z + yard.depth == lot.z + lot.depth } else { yard.z == lot.z };
    if !reaches { problems.push(format!("{name}: service yard does not reach the street edge")); }
}

fn overlaps(a: &WorldRect, b: &WorldRect) -> bool { a.x < b.x + b.width && b.x < a.x + a.width && a.z < b.z + b.depth && b.z < a.z + a.depth }

/// A cell just outside the rectangle, sharing an edge with it (not a corner).
pub fn borders(rect: &WorldRect, cell: WorldCell) -> bool {
    let along_x = cell.x >= rect.x && cell.x < rect.x + rect.width;
    let along_z = cell.z >= rect.z && cell.z < rect.z + rect.depth;
    (along_x && (cell.z == rect.z - 1 || cell.z == rect.z + rect.depth)) || (along_z && (cell.x == rect.x - 1 || cell.x == rect.x + rect.width))
}

/// The road a building's door opens onto: stepping straight out of a door cell must reach pavement within its setback.
pub fn access_road(building: &WorldBuilding, settings: &WorldSettings, road_at: impl Fn(i32, i32) -> Option<usize>) -> Option<usize> {
    let (dx, dz) = geo::step(building.facing);
    for door in &building.doors {
        for step in 1..=settings.setback_for(building.category) + 2 {
            if let Some(road) = road_at(2 * (door.x + dx * step) + 1, 2 * (door.z + dz * step) + 1) {
                return Some(road);
            }
        }
    }
    None
}

fn access(layout: &WorldLayout, settings: &WorldSettings, roads: &BoxIndex<usize>, problems: &mut Vec<String>) {
    let road_at = |x: i32, z: i32| roads.find(x, z);
    let start = layout.building(&layout.start_restaurant_id);
    let Some(start_road) = start.and_then(|s| access_road(s, settings, road_at)) else {
        problems.push("access: the starting restaurant has no road".to_string());
        return;
    };
    let mut edges: HashMap<&str, Vec<&str>> = HashMap::new();
    for road in &layout.roads {
        edges.entry(road.from_id.as_str()).or_default().push(road.to_id.as_str());
        edges.entry(road.to_id.as_str()).or_default().push(road.from_id.as_str());
    }
    let first = layout.roads[start_road].from_id.as_str();
    let mut reached: HashSet<&str> = HashSet::from([first]);
    let mut queue = VecDeque::from([first]);
    while let Some(node) = queue.pop_front() {
        for next in edges.get(node).into_iter().flatten() {
            if reached.insert(next) {
                queue.push_back(next);
            }
        }
    }
    for road in layout.roads.iter().filter(|r| !reached.contains(r.from_id.as_str())) {
        problems.push(format!("access: road {} is not connected to the network", road.id));
    }
    for building in &layout.buildings {
        match access_road(building, settings, road_at) {
            None => problems.push(format!("access: building {} has no road at its door", building.id)),
            Some(road) if !reached.contains(layout.roads[road].from_id.as_str()) => {
                problems.push(format!("access: building {} is not reachable by road", building.id))
            }
            _ => {}
        }
    }
    for lot in &layout.lots {
        if let Some(road) = roads.find(2 * lot.access.x + 1, 2 * lot.access.z + 1) {
            if !reached.contains(layout.roads[road].from_id.as_str()) {
                problems.push(format!("access: lot {} is not reachable by road", lot.id));
            }
        }
    }
}

fn start(layout: &WorldLayout, settings: &WorldSettings, problems: &mut Vec<String>) {
    let start = layout.building(&layout.start_restaurant_id);
    let residential = layout.districts.iter().find(|d| d.kind == DistrictKind::Residential).map(|d| d.id.clone());
    match start {
        None => problems.push("start: no starting restaurant".to_string()),
        Some(start) => {
            if start.category != BuildingCategory::Restaurant
                || Some(&start.district_id) != residential.as_ref()
                || start.ownership != Ownership::Player
                || start.width * start.depth > settings.start_max_area
            {
                problems.push("start: must be a player-owned restaurant shell within the start size in the residential district".to_string());
            }
            if layout.district(&start.district_id).map(|d| d.min_recipe_tier) != Some(1) {
                problems.push("start: its district's minimum recipe tier must be 1".to_string());
            }
        }
    }
    if layout.buildings.iter().filter(|b| b.ownership == Ownership::Player).count() != 1 {
        problems.push("start: exactly one player-owned building".to_string());
    }
    let restaurants: Vec<_> = layout.buildings.iter().filter(|b| b.category == BuildingCategory::Restaurant).collect();
    if !restaurants.iter().any(|b| b.ownership == Ownership::ForSale) {
        problems.push("property: no restaurant for sale".to_string());
    }
    if !restaurants.iter().any(|b| b.ownership == Ownership::Competitor) {
        problems.push("property: no competitor restaurant".to_string());
    }
    if !layout.buildings.iter().any(|b| b.category == BuildingCategory::Factory) {
        problems.push("property: no factory".to_string());
    }
}

fn farms_and_stations(layout: &WorldLayout, problems: &mut Vec<String>) {
    let farms: Vec<_> = layout.buildings.iter().filter(|b| b.category == BuildingCategory::Farm).collect();
    let stations: Vec<_> = layout.buildings.iter().filter(|b| b.category == BuildingCategory::Station).collect();
    if farms.is_empty() {
        problems.push("farms: none".to_string());
    }
    if stations.is_empty() {
        problems.push("stations: none".to_string());
    }
    let limit = 4 * STATION_NEAR_FARM_METRES * STATION_NEAR_FARM_METRES;
    let squared = |v: i64| v * v;
    let near = stations.iter().any(|s| {
        farms.iter().any(|f| {
            squared((2 * s.x + s.width - 2 * f.x - f.width) as i64) + squared((2 * s.z + s.depth - 2 * f.z - f.depth) as i64) <= limit
        })
    });
    if !near {
        problems.push(format!("stations: none within {STATION_NEAR_FARM_METRES} m of a farm"));
    }
}

/// The land covers the map; every building's entrance is at land height, and every footprint but a farm's is near level.
fn land(layout: &WorldLayout, settings: &WorldSettings, problems: &mut Vec<String>) {
    let terrain = &layout.terrain;
    if terrain.is_flat() || terrain.spacing * (terrain.samples - 1) != 2 * layout.map_half_size || terrain.heights_cm.len() != (terrain.samples * terrain.samples) as usize {
        problems.push("land: the terrain must cover the map".to_string());
        return;
    }
    for building in &layout.buildings {
        let Some(door) = building.doors.first() else { continue };
        let level = terrain.cell_height_cm(door.x, door.z);
        if building.elevation_cm != level {
            problems.push(format!("building {}: elevation is not the land height at its entrance", building.id));
        }
        if building.category == BuildingCategory::Farm {
            continue;
        }
        let (x0, z0, x1, z1) = (2 * building.x, 2 * building.z, 2 * (building.x + building.width), 2 * (building.z + building.depth));
        let heights = [terrain.height_cm(x0, z0), terrain.height_cm(x1, z0), terrain.height_cm(x0, z1), terrain.height_cm(x1, z1)];
        if heights.iter().any(|h| (h - level).abs() > settings.max_footprint_rise_cm) {
            problems.push(format!("building {}: stands on land too steep", building.id));
        }
    }
}

/// A junction (three or more segments) is controlled; a bend or a straight join is not.
fn controls(layout: &WorldLayout, problems: &mut Vec<String>) {
    let mut degree: HashMap<&str, usize> = HashMap::new();
    for road in &layout.roads {
        *degree.entry(road.from_id.as_str()).or_default() += 1;
        *degree.entry(road.to_id.as_str()).or_default() += 1;
    }
    for node in &layout.nodes {
        let d = degree.get(node.id.as_str()).copied().unwrap_or(0);
        if (d >= 3) != (node.control != JunctionControl::None) { problems.push(format!("node {}: control {:?} at {d} segments", node.id, node.control)); }
    }
}

/// An axis-aligned way: (vertical, fixed coordinate, from, to, width).
type Line = (bool, i32, i32, i32, i32);

fn line(road: &RoadSegment, nodes: &Nodes) -> Option<Line> {
    let (a, b) = (nodes.get(road.from_id.as_str())?, nodes.get(road.to_id.as_str())?);
    Some(if a.x == b.x { (true, a.x, a.z.min(b.z), a.z.max(b.z), road.width) } else { (false, a.z, a.x.min(b.x), a.x.max(b.x), road.width) })
}

fn rail_pieces(layout: &WorldLayout) -> Vec<(&str, Line)> {
    let mut pieces = vec![];
    for rail in &layout.rails {
        for pair in rail.points.windows(2) {
            let (a, b) = (pair[0], pair[1]);
            pieces.push((rail.id.as_str(), if a.x == b.x { (true, a.x, a.z.min(b.z), a.z.max(b.z), rail.width) } else { (false, a.z, a.x.min(b.x), a.x.max(b.x), rail.width) }));
        }
    }
    pieces
}

/// Rivers stay on the map and off buildings; wherever a road or rail line crosses one, a bridge on that way spans it.
fn rivers(layout: &WorldLayout, nodes: &Nodes, problems: &mut Vec<String>) {
    let m = layout.map_half_size;
    let mut ways: Vec<(&str, Line)> = layout.roads.iter().filter_map(|r| line(r, nodes).map(|l| (r.id.as_str(), l))).collect();
    ways.extend(rail_pieces(layout));
    for river in &layout.rivers {
        if river.width <= 0 || river.width % 2 != 0 || river.surface_drop_cm <= 0 || river.points.len() < 2
            || river.points.iter().any(|p| p.x.abs() > m || p.z.abs() > m) || river.points.windows(2).any(|w| w[0] == w[1]) {
            problems.push(format!("river {}: invalid line", river.id));
            continue;
        }
        let half = river.width / 2;
        let mut water: BoxIndex<usize> = BoxIndex::new();
        for b in geo::corridor(&river.points, half) { water.add(b, 0); }
        for building in layout.buildings.iter().filter(|b| water.any(&geo::of_building(b))) {
            problems.push(format!("building {}: stands in river {}", building.id, river.id));
        }
        for lot in layout.lots.iter().filter(|l| water.any(&geo::of_rect(&l.rect()))) {
            problems.push(format!("lot {}: lies in river {}", lot.id, river.id));
        }
        for (id, (vertical, fixed, from, to, _)) in &ways {
            for (along, _, _) in geo::crossings(&river.points, *vertical, *fixed, *from, *to) {
                let covered = layout.bridges.iter().any(|b| {
                    let (b0, b1) = if *vertical { (b.from.z, b.to.z) } else { (b.from.x, b.to.x) };
                    b.carries_id == *id && b.river_id == river.id
                        && (if *vertical { b.from.x == *fixed && b.to.x == *fixed } else { b.from.z == *fixed && b.to.z == *fixed })
                        && b0.min(b1) <= along - half && b0.max(b1) >= along + half
                });
                if !covered { problems.push(format!("river {}: {id} crosses it without a bridge", river.id)); }
            }
        }
    }
    let rivers: HashSet<&str> = layout.rivers.iter().map(|r| r.id.as_str()).collect();
    for bridge in &layout.bridges {
        let on_way = ways.iter().any(|(id, (vertical, fixed, from, to, _))| *id == bridge.carries_id && if *vertical {
            bridge.from.x == *fixed && bridge.to.x == *fixed && bridge.from.z.min(bridge.to.z) >= *from && bridge.from.z.max(bridge.to.z) <= *to
        } else {
            bridge.from.z == *fixed && bridge.to.z == *fixed && bridge.from.x.min(bridge.to.x) >= *from && bridge.from.x.max(bridge.to.x) <= *to
        });
        if !on_way || !rivers.contains(bridge.river_id.as_str()) || bridge.from == bridge.to {
            problems.push(format!("bridge {}: not on the centreline of what it carries, or over no river", bridge.id));
        }
    }
}

/// Every at-grade meeting of a rail line and a road segment is a level crossing, and every level crossing is one.
fn level_crossings(layout: &WorldLayout, nodes: &Nodes, problems: &mut Vec<String>) {
    let mut expected: HashSet<(&str, &str, i32, i32)> = HashSet::new();
    for (rail, (vertical, fixed, from, to, _)) in rail_pieces(layout) {
        for road in &layout.roads {
            let Some((road_vertical, road_fixed, road_from, road_to, _)) = line(road, nodes) else { continue };
            if road_vertical == vertical { continue; }
            if fixed > road_from && fixed < road_to && road_fixed > from && road_fixed < to {
                expected.insert((road.id.as_str(), rail, if vertical { fixed } else { road_fixed }, if vertical { road_fixed } else { fixed }));
            }
        }
    }
    let mut recorded: HashSet<(&str, &str, i32, i32)> = HashSet::new();
    for crossing in &layout.crossings {
        let key = (crossing.road_id.as_str(), crossing.rail_id.as_str(), crossing.x, crossing.z);
        if !recorded.insert(key) || !expected.contains(&key) {
            problems.push(format!("crossing {}: not where {} meets {}", crossing.id, crossing.rail_id, crossing.road_id));
        }
    }
    let mut missing: Vec<_> = expected.iter().filter(|x| !recorded.contains(*x)).collect();
    missing.sort();
    for (road, rail, _, _) in missing { problems.push(format!("crossing: {rail} meets {road} without a level crossing")); }
}

/// Trees stand on the map, never in a building or on a lot, on a rail line, in the water, on a carriageway or in a junction;
/// the only trees on a road are sidewalk trees within 1.5 m of a city street's edge.
fn trees(layout: &WorldLayout, nodes: &Nodes, roads: &BoxIndex<usize>, rails: &BoxIndex<usize>, problems: &mut Vec<String>) {
    let m = layout.map_half_size;
    let mut solid: BoxIndex<usize> = BoxIndex::new();
    for b in &layout.buildings { solid.add(geo::of_building(b), 0); }
    for lot in &layout.lots { solid.add(geo::of_rect(&lot.rect()), 0); }
    let water: Vec<(Vec<WorldCell>, i64)> = layout.rivers.iter().map(|r| (r.points.iter().map(|p| WorldCell::new(2 * p.x, 2 * p.z)).collect(), r.width as i64)).collect();
    let mut bad = 0;
    let mut first: Option<String> = None;
    for tree in &layout.trees {
        let (x, z) = (2 * tree.x + 1, 2 * tree.z + 1);
        let point = Box2::new(x - 1, z - 1, x + 1, z + 1);
        let mut wrong = x.abs() > 2 * m || z.abs() > 2 * m || tree.scale < 25 || tree.scale > 400 || solid.any(&point) || rails.any(&point)
            || water.iter().any(|(points, radius)| geo::within_distance_line(points, x as i64, z as i64, *radius));
        if !wrong {
            let on = roads.overlapping(&point);
            let mut directions = HashSet::new();
            for road in &on {
                let road = &layout.roads[*road];
                let Some((vertical, fixed, _, _, _)) = line(road, nodes) else { continue };
                directions.insert(vertical);
                let offset = ((if vertical { x } else { z }) - 2 * fixed).abs();
                if road.kind == RoadKind::Rural || offset < road.width - 3 { wrong = true; }
            }
            if directions.len() > 1 { wrong = true; }
        }
        if !wrong { continue; }
        bad += 1;
        first.get_or_insert_with(|| format!("{},{}", tree.x, tree.z));
    }
    if bad > 0 { problems.push(format!("trees: {bad} misplaced (first at {})", first.unwrap_or_default())); }
}
