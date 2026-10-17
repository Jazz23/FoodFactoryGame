// Generator inputs, ported from Unity's WorldSettings. Every number is a PROTOTYPE placeholder; the GDD fixes only the city
// size (about 1 km), the four districts and their character (section 3).
use super::layout::{BuildingCategory, CuisineWeight, DistrictKind};

#[derive(Clone, Debug)]
pub struct DistrictProfile {
    pub kind: DistrictKind,
    pub id: &'static str,
    pub cuisines: Vec<CuisineWeight>,
    pub min_recipe_tier: i32,
    pub customers_per_hour: i32,
    pub traffic_percent: i32,
    pub price_percent: i32,
    pub min_local_streets: i32,
    pub max_local_streets: i32,
    pub min_gap: i32,
    pub max_gap: i32,
    pub street_tree_percent: i32,
    pub yard_tree_percent: i32,
    pub restaurant_weight: i32,
    pub factory_weight: i32,
    pub house_weight: i32,
    pub apartment_weight: i32,
    pub office_weight: i32,
}

/// Street-parallel (along) and perpendicular (deep) extent ranges, inclusive, in cells.
#[derive(Clone, Copy, Debug)]
pub struct SizeRange {
    pub min_along: i32,
    pub max_along: i32,
    pub min_deep: i32,
    pub max_deep: i32,
}

const fn size(min_along: i32, max_along: i32, min_deep: i32, max_deep: i32) -> SizeRange {
    SizeRange { min_along, max_along, min_deep, max_deep }
}

#[derive(Clone, Debug)]
pub struct WorldSettings {
    pub city_half_size: i32,
    pub map_half_size: i32,
    pub superblocks: i32,
    pub arterial_jitter: i32,
    pub local_street_jitter: i32,
    pub outer_ring_inset: i32,
    pub arterial_width: i32,
    pub local_width: i32,
    pub rural_width: i32,
    pub arterial_capacity: i32,
    pub local_capacity: i32,
    pub rural_capacity: i32,
    pub setback: i32,
    pub factory_setback: i32,
    pub farm_setback: i32,
    pub terrain_spacing: i32,
    pub terrain_lattice_metres: [i32; 3],
    pub terrain_amplitude_cm: [i32; 3],
    pub city_relief_percent: i32,
    pub terrain_city_fade: i32,
    pub rail_relief_percent: i32,
    pub rail_relief_fade: i32,
    pub max_footprint_rise_cm: i32,
    pub river_width: i32,
    pub river_bank: i32,
    pub river_surface_drop_cm: i32,
    pub river_valley_drop_cm: i32,
    pub river_valley_flat: i32,
    pub river_valley_fade: i32,
    pub river_meander_step: i32,
    pub river_street_clearance: i32,
    pub street_tree_spacing: i32,
    pub street_tree_junction_clear: i32,
    pub yard_tree_lattice: i32,
    pub country_tree_lattice: i32,
    pub woodland_lattice_metres: i32,
    pub woodland_threshold_percent: i32,
    pub woodland_tree_percent: i32,
    pub lone_tree_percent: i32,
    pub riverbank_tree_percent: i32,
    pub riverbank_tree_band: i32,
    pub rail_width: i32,
    pub farm_line_offset: i32,
    pub rail_clearance: i32,
    pub farm_line_stations: i32,
    pub station_length: i32,
    pub station_depth: i32,
    pub farm: SizeRange,
    pub farm_chance_percent: i32,
    pub min_farm_gap: i32,
    pub max_farm_gap: i32,
    pub farm_industrial_side_bonus_percent: i32,
    pub restaurant: SizeRange,
    pub service_yard_width: i32,
    pub factory: SizeRange,
    pub house: SizeRange,
    pub large_house: SizeRange,
    pub apartment: SizeRange,
    pub office: SizeRange,
    pub restaurant_for_sale_percent: i32,
    pub restaurant_cents_per_cell: i64,
    pub factory_cents_per_cell: i64,
    pub farm_cents_per_cell: i64,
    pub station_cents_per_cell: i64,
    pub start_max_area: i32,
    pub start_largest: bool,
    pub max_attempts: i32,
    pub districts: Vec<DistrictProfile>,
}

fn weights(list: &[(&str, i32)]) -> Vec<CuisineWeight> {
    list.iter().map(|(cuisine, weight)| CuisineWeight { cuisine: (*cuisine).to_string(), weight: *weight }).collect()
}

impl Default for WorldSettings {
    fn default() -> Self {
        let blank = DistrictProfile {
            kind: DistrictKind::Downtown, id: "", cuisines: vec![], min_recipe_tier: 1, customers_per_hour: 0,
            traffic_percent: 0, price_percent: 100, min_local_streets: 0, max_local_streets: 0, min_gap: 0, max_gap: 0,
            street_tree_percent: 0, yard_tree_percent: 0, restaurant_weight: 0, factory_weight: 0, house_weight: 0,
            apartment_weight: 0, office_weight: 0,
        };
        Self {
            city_half_size: 500, map_half_size: 950, superblocks: 5, arterial_jitter: 24, local_street_jitter: 8,
            outer_ring_inset: 40, arterial_width: 14, local_width: 10, rural_width: 8, arterial_capacity: 1800,
            local_capacity: 600, rural_capacity: 400, setback: 2, factory_setback: 12, farm_setback: 8,
            terrain_spacing: 20, terrain_lattice_metres: [480, 200, 80], terrain_amplitude_cm: [2400, 650, 150],
            city_relief_percent: 22, terrain_city_fade: 220, rail_relief_percent: 20, rail_relief_fade: 120,
            max_footprint_rise_cm: 150, river_width: 22, river_bank: 12, river_surface_drop_cm: 160,
            river_valley_drop_cm: 150, river_valley_flat: 30, river_valley_fade: 150, river_meander_step: 160,
            river_street_clearance: 10, street_tree_spacing: 13, street_tree_junction_clear: 14, yard_tree_lattice: 8,
            country_tree_lattice: 10, woodland_lattice_metres: 220, woodland_threshold_percent: 62,
            woodland_tree_percent: 70, lone_tree_percent: 3, riverbank_tree_percent: 55, riverbank_tree_band: 20,
            rail_width: 8, farm_line_offset: 110, rail_clearance: 25, farm_line_stations: 3, station_length: 30,
            station_depth: 10, farm: size(40, 70, 40, 70), farm_chance_percent: 25, min_farm_gap: 30, max_farm_gap: 80,
            farm_industrial_side_bonus_percent: 10, restaurant: size(16, 24, 14, 20), service_yard_width: 4,
            factory: size(20, 40, 18, 35), house: size(8, 12, 8, 12), large_house: size(12, 18, 12, 18),
            apartment: size(14, 22, 12, 18), office: size(16, 28, 12, 24), restaurant_for_sale_percent: 30,
            restaurant_cents_per_cell: 15000, factory_cents_per_cell: 8000, farm_cents_per_cell: 1500,
            station_cents_per_cell: 12000, start_max_area: 480, start_largest: true, max_attempts: 8,
            districts: vec![
                DistrictProfile {
                    kind: DistrictKind::Downtown, id: "downtown", min_recipe_tier: 1, customers_per_hour: 90,
                    traffic_percent: 90, price_percent: 250, min_local_streets: 3, max_local_streets: 3, min_gap: 0,
                    max_gap: 1, street_tree_percent: 70, yard_tree_percent: 15, restaurant_weight: 30, office_weight: 45,
                    apartment_weight: 25,
                    cuisines: weights(&[("fast-food", 40), ("noodles", 30), ("bakery", 20), ("grill", 10)]),
                    ..blank.clone()
                },
                DistrictProfile {
                    kind: DistrictKind::Residential, id: "residential", min_recipe_tier: 1, customers_per_hour: 50,
                    traffic_percent: 40, price_percent: 60, min_local_streets: 2, max_local_streets: 3, min_gap: 1,
                    max_gap: 3, street_tree_percent: 75, yard_tree_percent: 35, restaurant_weight: 20, house_weight: 55,
                    apartment_weight: 25,
                    cuisines: weights(&[("bakery", 30), ("fast-food", 30), ("noodles", 25), ("grill", 15)]),
                    ..blank.clone()
                },
                DistrictProfile {
                    kind: DistrictKind::Wealthy, id: "wealthy", min_recipe_tier: 3, customers_per_hour: 35,
                    traffic_percent: 30, price_percent: 300, min_local_streets: 2, max_local_streets: 2, min_gap: 2,
                    max_gap: 4, street_tree_percent: 90, yard_tree_percent: 50, restaurant_weight: 20, house_weight: 70,
                    apartment_weight: 10,
                    cuisines: weights(&[("fine-dining", 40), ("bakery", 30), ("grill", 20), ("noodles", 10)]),
                    ..blank.clone()
                },
                DistrictProfile {
                    kind: DistrictKind::Industrial, id: "industrial", min_recipe_tier: 1, customers_per_hour: 12,
                    traffic_percent: 60, price_percent: 40, min_local_streets: 1, max_local_streets: 2, min_gap: 2,
                    max_gap: 4, street_tree_percent: 15, yard_tree_percent: 6, restaurant_weight: 8, factory_weight: 92,
                    cuisines: weights(&[("fast-food", 60), ("grill", 30), ("noodles", 10)]),
                    ..blank
                },
            ],
        }
    }
}

impl WorldSettings {
    pub fn profile(&self, kind: DistrictKind) -> &DistrictProfile {
        self.districts.iter().find(|d| d.kind == kind).expect("every district kind has a profile")
    }

    pub fn setback_for(&self, category: BuildingCategory) -> i32 {
        match category {
            BuildingCategory::Factory => self.factory_setback,
            BuildingCategory::Farm => self.farm_setback,
            _ => self.setback,
        }
    }
}
