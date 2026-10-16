// Where things stand in the scene (Unity SitePlacement and SiteGridSpace): the one rule shared by the map, every site, the
// avatar and placement. Map metres have +X east and +Z north; the scene is Bevy's right-handed space, so north is -Z (the
// default forward) and a map point (x, z) stands at scene (x - ox, h - oy, -(z - oz)), with the starting lot's centre at
// the origin and its ground floor at y = 0. A site cell (x, z) is world cell (lot.x + x, lot.z + z); every lot is levelled to
// its building's ground floor and each building level stands one 3 m storey higher. Yaw is Unity's: degrees clockwise from
// north seen from above.
use std::collections::HashMap;

use bevy::prelude::*;

use food_factory_bevy::world::layout::{WorldLayout, WorldLot};
use food_factory_bevy::world::terrain::WorldTerrain;

pub const LEVEL_HEIGHT: f32 = 3.0;
/// Lots blend back to the raw terrain within this many metres.
pub const LOT_BLEND: f32 = 8.0;
const LOT_BUCKET: f32 = 64.0;

#[derive(Clone, Debug)]
pub struct LotFrame {
    pub site: String,
    pub lot_id: String,
    pub building: String,
    pub x: i32,
    pub z: i32,
    pub width: i32,
    pub depth: i32,
    pub elevation: f32,
}

#[derive(Resource, Clone)]
pub struct Space {
    pub origin: Vec3,
    pub terrain: WorldTerrain,
    pub lots: HashMap<String, LotFrame>,
    buckets: HashMap<(i32, i32), Vec<usize>>,
    lot_list: Vec<LotFrame>,
}

impl Space {
    pub fn new(layout: &WorldLayout) -> Self {
        let elevations: HashMap<&str, f32> = layout.buildings.iter().map(|b| (b.id.as_str(), b.elevation_cm as f32 / 100.0)).collect();
        let start = layout.lots.iter().find(|l| l.building_id == layout.start_restaurant_id);
        let origin = match start {
            Some(lot) => Vec3::new(lot.x as f32 + lot.width as f32 / 2.0, elevations[lot.building_id.as_str()], lot.z as f32 + lot.depth as f32 / 2.0),
            None => Vec3::ZERO,
        };
        let lot_list: Vec<LotFrame> = layout.lots.iter().map(|l: &WorldLot| LotFrame {
            site: l.site_id.clone(), lot_id: l.id.clone(), building: l.building_id.clone(), x: l.x, z: l.z, width: l.width, depth: l.depth,
            elevation: elevations.get(l.building_id.as_str()).copied().unwrap_or(0.0),
        }).collect();
        let mut buckets: HashMap<(i32, i32), Vec<usize>> = HashMap::new();
        for (index, lot) in lot_list.iter().enumerate() {
            let x0 = ((lot.x as f32 - LOT_BLEND) / LOT_BUCKET).floor() as i32;
            let x1 = ((lot.x as f32 + lot.width as f32 + LOT_BLEND) / LOT_BUCKET).floor() as i32;
            let z0 = ((lot.z as f32 - LOT_BLEND) / LOT_BUCKET).floor() as i32;
            let z1 = ((lot.z as f32 + lot.depth as f32 + LOT_BLEND) / LOT_BUCKET).floor() as i32;
            for i in x0..=x1 {
                for j in z0..=z1 {
                    buckets.entry((i, j)).or_default().push(index);
                }
            }
        }
        Self { origin, terrain: layout.terrain.clone(), lots: lot_list.iter().map(|l| (l.site.clone(), l.clone())).collect(), buckets, lot_list }
    }

    /// Scene point of a map point at an absolute height (metres).
    pub fn to_scene(&self, map_x: f32, map_z: f32, height: f32) -> Vec3 { Vec3::new(map_x - self.origin.x, height - self.origin.y, -(map_z - self.origin.z)) }

    /// Map point (x, z) and absolute height of a scene point.
    pub fn to_map(&self, scene: Vec3) -> (f32, f32, f32) { (scene.x + self.origin.x, -scene.z + self.origin.z, scene.y + self.origin.y) }

    /// The nearest lot's elevation and how far outside it a point lies, when one is within LOT_BLEND.
    fn nearest_lot(&self, x: f32, z: f32) -> Option<(f32, f32, usize)> {
        let list = self.buckets.get(&((x / LOT_BUCKET).floor() as i32, (z / LOT_BUCKET).floor() as i32))?;
        let mut best: Option<(f32, f32, usize)> = None;
        for &index in list {
            let lot = &self.lot_list[index];
            let dx = (lot.x as f32 - x).max(x - (lot.x + lot.width) as f32).max(0.0);
            let dz = (lot.z as f32 - z).max(z - (lot.z + lot.depth) as f32).max(0.0);
            let distance = dx.max(dz);
            if best.is_none_or(|b| distance < b.1) {
                best = Some((lot.elevation, distance, index));
            }
        }
        best.filter(|b| b.1 < LOT_BLEND)
    }

    /// Drawn land height at a map point: lots are levelled to their building's ground floor, blending back to the terrain.
    pub fn land(&self, x: f32, z: f32) -> f32 {
        let raw = self.terrain.height(x, z);
        match self.nearest_lot(x, z) {
            None => raw,
            Some((elevation, outside, _)) => {
                let t = (outside / LOT_BLEND).clamp(0.0, 1.0);
                let t = t * t * (3.0 - 2.0 * t);
                elevation + (raw - elevation) * t
            }
        }
    }

    /// The site whose lot holds a map point, if any.
    pub fn site_at(&self, x: f32, z: f32) -> Option<&LotFrame> {
        let list = self.buckets.get(&((x / LOT_BUCKET).floor() as i32, (z / LOT_BUCKET).floor() as i32))?;
        list.iter().map(|&i| &self.lot_list[i]).find(|l| x >= l.x as f32 && x < (l.x + l.width) as f32 && z >= l.z as f32 && z < (l.z + l.depth) as f32)
    }

    /// Scene point of site metres (x, z within the lot) on a level.
    pub fn site_point(&self, site: &str, x: f32, z: f32, level: i32) -> Vec3 {
        match self.lots.get(site) {
            Some(lot) => self.to_scene(lot.x as f32 + x, lot.z as f32 + z, lot.elevation + level as f32 * LEVEL_HEIGHT),
            None => Vec3::new(x, level as f32 * LEVEL_HEIGHT, -z),
        }
    }

    /// Site metres of a scene point on a site.
    pub fn to_site(&self, site: &str, scene: Vec3) -> (f32, f32) {
        let (mx, mz, _) = self.to_map(scene);
        match self.lots.get(site) {
            Some(lot) => (mx - lot.x as f32, mz - lot.z as f32),
            None => (scene.x, -scene.z),
        }
    }

    /// Scene height of a site's level floor.
    pub fn floor_height(&self, site: &str, level: i32) -> f32 {
        self.lots.get(site).map_or(0.0, |l| l.elevation) + level as f32 * LEVEL_HEIGHT - self.origin.y
    }

    /// The level whose floor a scene height stands on, on a site.
    pub fn level_at(&self, site: &str, height: f32) -> i32 { ((height - self.floor_height(site, 0)) / LEVEL_HEIGHT).round().max(0.0) as i32 }

    /// Scene ground height under a scene point (land or a lot's floor).
    pub fn ground(&self, scene: Vec3) -> f32 {
        let (mx, mz, _) = self.to_map(scene);
        self.land(mx, mz) - self.origin.y
    }
}

/// Unity yaw (clockwise from north, degrees) as a scene rotation.
pub fn yaw_rotation(yaw: f32) -> Quat { Quat::from_rotation_y(-yaw.to_radians()) }

/// Unity yaw of a scene direction.
pub fn yaw_of(direction: Vec3) -> f32 { direction.x.atan2(-direction.z).to_degrees() }

/// A site grid rotation (quarter turns clockwise, 0 facing north) as a scene rotation.
pub fn grid_rotation(rotation: i32) -> Quat { yaw_rotation(rotation as f32 * 90.0) }
