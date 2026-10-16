// Checks of a finished layout recomputed from its data, ported from Unity's WorldLayoutValidator. The port keeps the checks a
// generated attempt can actually fail (road access and connectivity, lot access, the starting restaurant, required property,
// farms and stations, steep land); the structural invariants the builder guarantees by construction are left out.
use std::collections::{HashMap, HashSet, VecDeque};

use super::geometry::{self as geo, BoxIndex};
use super::layout::*;
use super::settings::WorldSettings;

pub const STATION_NEAR_FARM_METRES: i64 = 300;

pub fn validate(layout: &WorldLayout, settings: &WorldSettings) -> Vec<String> {
    let mut problems = vec![];
    if layout.city_half_size <= 0 || layout.map_half_size <= layout.city_half_size {
        problems.push("bounds: map must be larger than the city".to_string());
    }
    let nodes: HashMap<&str, &RoadNode> = layout.nodes.iter().map(|n| (n.id.as_str(), n)).collect();
    let mut roads: BoxIndex<usize> = BoxIndex::new();
    for (index, road) in layout.roads.iter().enumerate() {
        let (Some(from), Some(to)) = (nodes.get(road.from_id.as_str()), nodes.get(road.to_id.as_str())) else {
            problems.push(format!("road {}: unknown node", road.id));
            continue;
        };
        roads.add(geo::strip(from.x, from.z, to.x, to.z, road.width), index);
    }
    lots(layout, &roads, &mut problems);
    access(layout, settings, &roads, &mut problems);
    start(layout, settings, &mut problems);
    farms_and_stations(layout, &mut problems);
    land(layout, settings, &mut problems);
    problems.sort();
    problems
}

/// A cell just outside the rectangle, sharing an edge with it (not a corner).
pub fn borders(rect: &WorldRect, cell: WorldCell) -> bool {
    let along_x = cell.x >= rect.x && cell.x < rect.x + rect.width;
    let along_z = cell.z >= rect.z && cell.z < rect.z + rect.depth;
    (along_x && (cell.z == rect.z - 1 || cell.z == rect.z + rect.depth)) || (along_z && (cell.x == rect.x - 1 || cell.x == rect.x + rect.width))
}

fn lots(layout: &WorldLayout, roads: &BoxIndex<usize>, problems: &mut Vec<String>) {
    for lot in &layout.lots {
        if !borders(&lot.rect(), lot.access) {
            problems.push(format!("lot {}: access is not a cell beside its edge", lot.id));
        } else if roads.find(2 * lot.access.x + 1, 2 * lot.access.z + 1).is_none() {
            problems.push(format!("lot {}: access is not on a road", lot.id));
        }
    }
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

fn land(layout: &WorldLayout, settings: &WorldSettings, problems: &mut Vec<String>) {
    let terrain = &layout.terrain;
    if terrain.is_flat() {
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
