// The generated world (GDD section 3): districts, the fixed road graph, rail lines, and every building, farm and station.
// A layout is created once when a world is created and then only read: loading never regenerates it (decision 0026).
// Coordinates are whole metres on the building cell grid (1 m cells), with the city centre at the origin, +X east, +Z north.
// A rectangle's x/z is its minimum cell and width/depth count cells along X/Z. Roads and rails are centrelines with a width.
use serde::{Deserialize, Serialize};

use super::terrain::WorldTerrain;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, Serialize, Deserialize)]
pub enum DistrictKind { Downtown, Residential, Wealthy, Industrial }

/// The side a building's doors face, toward its street. Index = quarter turns clockwise seen from above (yaw / 90).
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, Deserialize, Default)]
pub enum Facing { #[default] North, East, South, West }

impl Facing {
    pub fn index(self) -> i32 { self as i32 }
    pub fn from_index(index: i32) -> Self {
        match index.rem_euclid(4) { 0 => Facing::North, 1 => Facing::East, 2 => Facing::South, _ => Facing::West }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, Serialize, Deserialize)]
pub enum BuildingCategory { Restaurant, Factory, Farm, Station, House, Apartment, Office }

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum Ownership {
    /// Premade scenery: never sold, holds no population.
    Scenery,
    ForSale,
    Competitor,
    /// The player's starting restaurant.
    Player,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, Serialize, Deserialize)]
pub enum RoadKind { Arterial, Local, Rural }

/// How a road node is controlled. A stop sign stops the approaches of the lowest road kind meeting there, or every approach
/// when all are the same kind (`junction_stops`).
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, Deserialize, Default)]
pub enum JunctionControl { #[default] None, StopSign, TrafficLight }

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum TreeKind { Broadleaf, Conifer, Poplar, Bush }

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, PartialOrd, Ord, Serialize, Deserialize, Default)]
pub struct WorldCell { pub x: i32, pub z: i32 }

impl WorldCell {
    pub const fn new(x: i32, z: i32) -> Self { Self { x, z } }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, Deserialize, Default)]
pub struct WorldRect { pub x: i32, pub z: i32, pub width: i32, pub depth: i32 }

impl WorldRect {
    pub const fn new(x: i32, z: i32, width: i32, depth: i32) -> Self { Self { x, z, width, depth } }
    pub fn contains(&self, x: i32, z: i32) -> bool { x >= self.x && x < self.x + self.width && z >= self.z && z < self.z + self.depth }
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct CuisineWeight { pub cuisine: String, pub weight: i32 }

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct WorldDistrict {
    pub id: String,
    pub kind: DistrictKind,
    pub areas: Vec<WorldRect>,
    pub cuisines: Vec<CuisineWeight>,
    pub min_recipe_tier: i32,
    /// Customers per game hour.
    pub customers_per_hour: i32,
    pub traffic_percent: i32,
    /// Building purchase price multiplier, percent of the base price.
    pub price_percent: i32,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct RoadNode { pub id: String, pub x: i32, pub z: i32, pub control: JunctionControl }

/// An axis-aligned piece of road between two adjacent nodes.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct RoadSegment {
    pub id: String,
    pub from_id: String,
    pub to_id: String,
    pub kind: RoadKind,
    pub width: i32,
    pub capacity_per_hour: i32,
}

/// Axis-aligned polyline of track centreline points.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct RailLine { pub id: String, pub width: i32, pub points: Vec<WorldCell> }

/// A river's centreline (points about 20 m apart, any direction) and its water width.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct WorldRiver { pub id: String, pub width: i32, pub surface_drop_cm: i32, pub points: Vec<WorldCell> }

/// A road segment or rail line carried over a river between two points of its centreline.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct WorldBridge { pub id: String, pub carries_id: String, pub river_id: String, pub from: WorldCell, pub to: WorldCell }

/// A rail line crossing a road segment at grade.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct LevelCrossing { pub id: String, pub road_id: String, pub rail_id: String, pub x: i32, pub z: i32 }

/// Scenery only. Scale is a percentage of the model's authored size.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct WorldTree { pub x: i32, pub z: i32, pub kind: TreeKind, pub scale: i32 }

/// Restaurants and factories are generated shells; everything else records a premade model slot (model key plus footprint
/// and facing). Doors are entrance cells used for road access. Stations belong to a rail line.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct WorldBuilding {
    pub id: String,
    pub category: BuildingCategory,
    /// Empty outside the city.
    pub district_id: String,
    pub x: i32,
    pub z: i32,
    pub width: i32,
    pub depth: i32,
    pub facing: Facing,
    pub doors: Vec<WorldCell>,
    pub model_key: String,
    pub floors: i32,
    pub ownership: Ownership,
    pub price_cents: i64,
    pub line_id: String,
    pub site_id: String,
    pub elevation_cm: i32,
    pub service_yard: Option<WorldRect>,
    pub back_door: Option<WorldCell>,
    pub service_dock: Option<WorldRect>,
}

impl WorldBuilding {
    pub fn new(id: String, category: BuildingCategory) -> Self {
        Self {
            id, category, district_id: String::new(), x: 0, z: 0, width: 0, depth: 0, facing: Facing::North, doors: vec![],
            model_key: String::new(), floors: 1, ownership: Ownership::Scenery, price_cents: 0, line_id: String::new(),
            site_id: String::new(), elevation_cm: 0, service_yard: None, back_door: None, service_dock: None,
        }
    }
    pub fn is_shell(&self) -> bool { matches!(self.category, BuildingCategory::Restaurant | BuildingCategory::Factory) }
    pub fn is_purchasable(&self) -> bool { matches!(self.ownership, Ownership::ForSale | Ownership::Competitor) }
    pub fn has_lot(&self) -> bool { self.ownership != Ownership::Scenery }
    pub fn footprint(&self) -> WorldRect { WorldRect::new(self.x, self.z, self.width, self.depth) }
    pub fn yaw_degrees(&self) -> i32 { self.facing.index() * 90 }
}

/// A purchasable building's lot (decision 0028): the footprint extended forward to its street. Site cell (x, z) is world
/// cell (lot.x + x, lot.z + z). Access is the street cell in front of the lot, level with the first door.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct WorldLot {
    pub id: String,
    pub building_id: String,
    pub site_id: String,
    pub x: i32,
    pub z: i32,
    pub width: i32,
    pub depth: i32,
    pub access: WorldCell,
}

impl WorldLot {
    pub fn rect(&self) -> WorldRect { WorldRect::new(self.x, self.z, self.width, self.depth) }
    pub fn id_for(building_id: &str) -> String { format!("lot-{building_id}") }
    pub fn site_id_for(building_id: &str) -> String { format!("site-{building_id}") }
}

#[derive(Clone, Debug, Serialize, Deserialize, Default)]
pub struct WorldLayout {
    pub generator_version: i32,
    pub requested_seed: String,
    pub seed: u64,
    pub attempt: i32,
    pub city_half_size: i32,
    pub map_half_size: i32,
    pub start_restaurant_id: String,
    pub districts: Vec<WorldDistrict>,
    pub nodes: Vec<RoadNode>,
    pub roads: Vec<RoadSegment>,
    pub rails: Vec<RailLine>,
    pub buildings: Vec<WorldBuilding>,
    pub terrain: WorldTerrain,
    pub rivers: Vec<WorldRiver>,
    pub bridges: Vec<WorldBridge>,
    pub crossings: Vec<LevelCrossing>,
    pub trees: Vec<WorldTree>,
    pub lots: Vec<WorldLot>,
}

impl WorldLayout {
    pub fn building(&self, id: &str) -> Option<&WorldBuilding> { self.buildings.iter().find(|b| b.id == id) }
    pub fn lot(&self, id: &str) -> Option<&WorldLot> { self.lots.iter().find(|l| l.id == id) }
    pub fn lot_of_site(&self, site: &str) -> Option<&WorldLot> { self.lots.iter().find(|l| l.site_id == site) }
    pub fn district(&self, id: &str) -> Option<&WorldDistrict> { self.districts.iter().find(|d| d.id == id) }
}
