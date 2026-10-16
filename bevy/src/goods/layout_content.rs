// Content derived from a stored world layout (Unity WorldLayoutShells and WorldLayoutCustomers): the property catalog (one
// offer per lot, site cells translated from world cells), one spawning district per block of each layout district, and one
// competitor per competitor-owned restaurant lot. Pure functions of the layout, so IDs are stable and never stored separately.
use std::collections::HashMap;

use super::state::{Competitor, District, GridCell};
use super::{PropertyOffer, FACTORY_KIND, FARM_CATEGORY, RESTAURANT_KIND, STATION_CATEGORY};
use crate::world::layout::{BuildingCategory, DistrictKind, Ownership, WorldBuilding, WorldDistrict, WorldLayout, WorldLot};

pub const LIKED_CUISINE_WEIGHT: i32 = 25;
pub const APPEARANCE_VARIANTS: i32 = 4;
/// PROTOTYPE: every district's dine-in share; the rest order to go.
pub const DINE_IN_PERCENT: i32 = 100 - super::customers::TAKEAWAY_PERCENT;
/// PROTOTYPE testing value: customers enter on the street this close to the restaurant they chose.
pub const STREET_SPAWN_METRES: i32 = 10;

pub fn property_offers(layout: &WorldLayout) -> Vec<PropertyOffer> {
    let buildings: HashMap<&str, &WorldBuilding> = layout.buildings.iter().map(|b| (b.id.as_str(), b)).collect();
    let percents: HashMap<&str, i32> = layout.districts.iter().map(|d| (d.id.as_str(), d.price_percent)).collect();
    layout.lots.iter().map(|lot| {
        let building = buildings[lot.building_id.as_str()];
        let percent = percents.get(building.district_id.as_str()).copied().filter(|p| *p > 0).unwrap_or(100);
        to_offer(lot, building, percent)
    }).collect()
}

fn category_of(category: BuildingCategory) -> &'static str {
    match category {
        BuildingCategory::Restaurant => RESTAURANT_KIND,
        BuildingCategory::Factory => FACTORY_KIND,
        BuildingCategory::Farm => FARM_CATEGORY,
        BuildingCategory::Station => STATION_CATEGORY,
        _ => "",
    }
}

/// The dock content is 2 x 1 along X at rotation 0 and faces +Z; the starter dock faces the shell.
fn dock_rotation(building: &WorldBuilding) -> i32 {
    let Some(dock) = building.service_dock else { return 0 };
    if dock.depth > dock.width {
        if dock.x < building.x { 1 } else { 3 }
    } else if dock.z < building.z {
        0
    } else {
        2
    }
}

pub fn to_offer(lot: &WorldLot, building: &WorldBuilding, price_percent: i32) -> PropertyOffer {
    PropertyOffer {
        lot_id: lot.id.clone(),
        site_id: lot.site_id.clone(),
        building_id: building.id.clone(),
        category: category_of(building.category).to_string(),
        for_sale: building.ownership == Ownership::ForSale,
        price_cents: building.price_cents,
        lot_x: lot.x,
        lot_z: lot.z,
        width: lot.width,
        depth: lot.depth,
        access_x: lot.access.x,
        access_z: lot.access.z,
        building_x: building.x - lot.x,
        building_z: building.z - lot.z,
        building_width: building.width,
        building_depth: building.depth,
        doors: building.doors.iter().map(|d| GridCell { x: d.x - lot.x, z: d.z - lot.z }).collect(),
        back_doors: building.back_door.map(|d| vec![GridCell { x: d.x - lot.x, z: d.z - lot.z }]).unwrap_or_default(),
        has_dock: building.service_dock.is_some(),
        dock_x: building.service_dock.map_or(0, |d| d.x - lot.x),
        dock_z: building.service_dock.map_or(0, |d| d.z - lot.z),
        dock_rotation: dock_rotation(building),
        floors: if building.is_shell() { building.floors } else { 1 },
        price_percent,
    }
}

fn tuning(kind: DistrictKind) -> (i32, i32) {
    match kind {
        DistrictKind::Downtown => (55, 350),
        DistrictKind::Residential => (40, 400),
        DistrictKind::Wealthy => (85, 450),
        DistrictKind::Industrial => (25, 300),
    }
}

pub fn district_id(district: &WorldDistrict, block: usize) -> String { format!("district-{}-{block}", district.id) }

pub fn competitor_id(building_id: &str) -> String { format!("competitor-{building_id}") }

pub fn districts(layout: &WorldLayout) -> Vec<District> {
    let mut result = vec![];
    for district in &layout.districts {
        let (wealth, range) = tuning(district.kind);
        let mut liked: Vec<&crate::world::layout::CuisineWeight> = district.cuisines.iter().filter(|c| c.weight >= LIKED_CUISINE_WEIGHT).collect();
        liked.sort_by(|a, b| b.weight.cmp(&a.weight).then_with(|| a.cuisine.cmp(&b.cuisine)));
        let liked: Vec<String> = liked.into_iter().map(|c| c.cuisine.clone()).collect();
        let areas: Vec<i64> = district.areas.iter().map(|a| a.width as i64 * a.depth as i64).collect();
        let shares = share(district.customers_per_hour, &areas);
        for (block, area) in district.areas.iter().enumerate() {
            result.push(District {
                id: district_id(district, block),
                name: format!("{:?} {}", district.kind, block + 1),
                map_x: area.x + area.width / 2,
                map_z: area.z + area.depth / 2,
                customers_per_hour: shares[block],
                wealth_percent: wealth,
                appearance: district.id.clone(),
                appearance_variants: APPEARANCE_VARIANTS,
                liked_cuisines: liked.clone(),
                dine_in_percent: DINE_IN_PERCENT,
                range_metres: range,
                spawn_progress: 0,
                spawn_walk_metres: STREET_SPAWN_METRES,
            });
        }
    }
    result
}

pub fn competitors(layout: &WorldLayout) -> Vec<Competitor> {
    let lots: HashMap<&str, &WorldLot> = layout.lots.iter().map(|l| (l.building_id.as_str(), l)).collect();
    let mut result = vec![];
    for building in layout.buildings.iter().filter(|b| b.ownership == Ownership::Competitor) {
        let Some(lot) = lots.get(building.id.as_str()) else { continue };
        let district = layout.district(&building.district_id).unwrap_or(&layout.districts[0]);
        let tier = district.min_recipe_tier.max(1);
        result.push(Competitor {
            id: competitor_id(&building.id),
            name: building.id.clone(),
            lot_id: lot.id.clone(),
            map_x: lot.access.x,
            map_z: lot.access.z,
            cuisine: cuisine(district, hash(&building.id, "cuisine")),
            tier,
            price_cents: 300 + (hash(&building.id, "price") % 7) as i64 * 100 + (tier - 1) as i64 * 300,
            servers: 1 + (hash(&building.id, "servers") % 3) as i32,
            service_seconds: 10 + (hash(&building.id, "service") % 21) as i64,
            seats: 4 + (hash(&building.id, "seats") % 5) as i32 * 4,
        });
    }
    result
}

fn cuisine(district: &WorldDistrict, hash: u64) -> String {
    let total: i32 = district.cuisines.iter().map(|c| c.weight.max(0)).sum();
    if total == 0 {
        return String::new();
    }
    let mut pick = (hash % total as u64) as i64;
    for weight in &district.cuisines {
        pick -= weight.weight.max(0) as i64;
        if pick < 0 {
            return weight.cuisine.clone();
        }
    }
    district.cuisines.last().map(|c| c.cuisine.clone()).unwrap_or_default()
}

/// Shares a total by area with the largest remainder, so blocks add up to the district's rate exactly.
fn share(total: i32, areas: &[i64]) -> Vec<i32> {
    let sum: i64 = areas.iter().sum();
    if sum == 0 {
        return vec![0; areas.len()];
    }
    let mut shares: Vec<i32> = areas.iter().map(|a| (total as i64 * a / sum) as i32).collect();
    let mut order: Vec<usize> = (0..areas.len()).collect();
    order.sort_by(|a, b| (total as i64 * areas[*b] % sum).cmp(&(total as i64 * areas[*a] % sum)).then_with(|| a.cmp(b)));
    let remainder = total - shares.iter().sum::<i32>();
    for index in 0..remainder.max(0) as usize {
        shares[order[index]] += 1;
    }
    shares
}

fn hash(id: &str, salt: &str) -> u64 {
    let mut hash = 14_695_981_039_346_656_037u64;
    for character in format!("{id}:{salt}").encode_utf16() {
        hash ^= u64::from(character);
        hash = hash.wrapping_mul(1_099_511_628_211);
    }
    hash
}
