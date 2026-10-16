// The phone's world map picture (Unity PhoneMapView, decision 0047): the layout's districts, rivers, roads and every lot from
// above, coloured by what the lot is and who owns it (yours green, for sale yellow, competitors red, farms brown, stations and
// scenery grey), with the avatar as a white dot and the company's trucks as blue dots. Map x runs right and z up. Redrawn twice
// a second while the phone's map tab is open. Presentation only. Simplification: no zoom, pan or click-to-select on the
// picture itself; the places list beside it opens each lot.
use bevy::asset::RenderAssetUsages;
use bevy::prelude::*;
use bevy::render::render_resource::{Extent3d, TextureDimension, TextureFormat};

use food_factory_bevy::world::layout::{BuildingCategory, DistrictKind, Ownership};

use crate::panels::{Panel, Panels, PhoneTab};
use crate::session::Session;
use crate::space::Space;

pub struct PhoneMapPlugin;

impl Plugin for PhoneMapPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<PhoneMap>().add_systems(Update, draw_map);
    }
}

pub const MAP_PIXELS: u32 = 480;

#[derive(Resource, Default)]
pub struct PhoneMap {
    pub image: Option<Handle<Image>>,
    timer: f32,
    /// The map area drawn: centre (map metres) and half size.
    pub centre: Vec2,
    pub half: f32,
}

struct Canvas {
    data: Vec<u8>,
    centre: Vec2,
    half: f32,
}

impl Canvas {
    fn pixel(&self, x: f32, z: f32) -> (i32, i32) {
        let u = (x - (self.centre.x - self.half)) / (2.0 * self.half) * MAP_PIXELS as f32;
        let v = ((self.centre.y + self.half) - z) / (2.0 * self.half) * MAP_PIXELS as f32;
        (u.floor() as i32, v.floor() as i32)
    }

    fn put(&mut self, x: i32, y: i32, colour: [u8; 4]) {
        if x < 0 || y < 0 || x >= MAP_PIXELS as i32 || y >= MAP_PIXELS as i32 {
            return;
        }
        let i = ((y as u32 * MAP_PIXELS + x as u32) * 4) as usize;
        self.data[i..i + 4].copy_from_slice(&colour);
    }

    fn rect(&mut self, x0: f32, z0: f32, x1: f32, z1: f32, colour: [u8; 4]) {
        let (a, b) = (self.pixel(x0, z1), self.pixel(x1, z0));
        for y in a.1..=b.1.max(a.1) {
            for x in a.0..=b.0.max(a.0) {
                self.put(x, y, colour);
            }
        }
    }

    fn line(&mut self, from: Vec2, to: Vec2, width: f32, colour: [u8; 4]) {
        let length = from.distance(to);
        let metres_per_pixel = 2.0 * self.half / MAP_PIXELS as f32;
        let steps = (length / metres_per_pixel).ceil().max(1.0) as i32;
        let radius = (width * 0.5 / metres_per_pixel).max(0.5);
        for step in 0..=steps {
            let p = from.lerp(to, step as f32 / steps as f32);
            let (cx, cy) = self.pixel(p.x, p.y);
            let r = radius.ceil() as i32;
            for dy in -r..=r {
                for dx in -r..=r {
                    if ((dx * dx + dy * dy) as f32) <= radius * radius + 0.25 {
                        self.put(cx + dx, cy + dy, colour);
                    }
                }
            }
        }
    }

    fn dot(&mut self, x: f32, z: f32, radius: i32, colour: [u8; 4], outline: [u8; 4]) {
        let (cx, cy) = self.pixel(x, z);
        for dy in -radius - 1..=radius + 1 {
            for dx in -radius - 1..=radius + 1 {
                let d = dx * dx + dy * dy;
                if d <= radius * radius {
                    self.put(cx + dx, cy + dy, colour);
                } else if d <= (radius + 1) * (radius + 1) {
                    self.put(cx + dx, cy + dy, outline);
                }
            }
        }
    }
}

#[allow(clippy::too_many_arguments)]
fn draw_map(time: Res<Time>, panels: Res<Panels>, session: Res<Session>, space: Option<Res<Space>>, player: Query<&Transform, With<crate::player::Player>>, mut map: ResMut<PhoneMap>, mut images: ResMut<Assets<Image>>) {
    if panels.open != Some(Panel::Phone(PhoneTab::Map)) {
        map.timer = 0.0;
        return;
    }
    let (Some(world), Some(space)) = (session.joined(), space) else { return };
    map.timer -= time.delta_secs();
    if map.timer > 0.0 && map.image.is_some() {
        return;
    }
    map.timer = 0.5;
    let layout = &world.layout;
    let half = layout.city_half_size as f32 + 120.0;
    let mut canvas = Canvas { data: vec![0; (MAP_PIXELS * MAP_PIXELS * 4) as usize], centre: Vec2::ZERO, half };
    // Ground and districts.
    canvas.rect(-half, -half, half, half, [124, 160, 96, 255]);
    for district in &layout.districts {
        let colour = match district.kind {
            DistrictKind::Downtown => [176, 170, 160, 255],
            DistrictKind::Industrial => [150, 142, 128, 255],
            DistrictKind::Wealthy => [132, 172, 108, 255],
            DistrictKind::Residential => [140, 168, 110, 255],
        };
        for area in &district.areas {
            canvas.rect(area.x as f32, area.z as f32, (area.x + area.width) as f32, (area.z + area.depth) as f32, colour);
        }
    }
    for river in &layout.rivers {
        for pair in river.points.windows(2) {
            canvas.line(Vec2::new(pair[0].x as f32, pair[0].z as f32), Vec2::new(pair[1].x as f32, pair[1].z as f32), river.width as f32, [70, 120, 170, 255]);
        }
    }
    let nodes: std::collections::HashMap<&str, Vec2> = layout.nodes.iter().map(|n| (n.id.as_str(), Vec2::new(n.x as f32, n.z as f32))).collect();
    for road in &layout.roads {
        canvas.line(nodes[road.from_id.as_str()], nodes[road.to_id.as_str()], road.width as f32, [72, 72, 76, 255]);
    }
    for rail in &layout.rails {
        for pair in rail.points.windows(2) {
            canvas.line(Vec2::new(pair[0].x as f32, pair[0].z as f32), Vec2::new(pair[1].x as f32, pair[1].z as f32), 3.0, [110, 90, 70, 255]);
        }
    }
    // Lots by kind and owner.
    let view = world.view();
    let own = world.company().map(|c| c.id.clone()).unwrap_or_default();
    for lot in &layout.lots {
        let Some(building) = layout.building(&lot.building_id) else { continue };
        let owner = view.and_then(|v| v.properties.iter().find(|p| p.lot_id == lot.id)).map(|p| p.company_id.clone());
        let colour = match (&owner, building.category, building.ownership) {
            (Some(o), _, _) if *o == own => [60, 190, 90, 255],
            (Some(_), _, _) => [200, 70, 60, 255],
            (None, BuildingCategory::Farm, _) => [150, 110, 60, 255],
            (None, _, Ownership::Player) => [60, 190, 90, 255],
            (None, _, Ownership::ForSale) => [235, 200, 60, 255],
            (None, _, Ownership::Competitor) => [200, 70, 60, 255],
            _ => [120, 120, 125, 255],
        };
        canvas.rect(lot.x as f32, lot.z as f32, (lot.x + lot.width) as f32, (lot.z + lot.depth) as f32, colour);
    }
    // The company's trucks on the road.
    if let Some(view) = view {
        for truck in view.trucks.iter().filter(|t| t.company_id == own && t.on_road()) {
            if let Some(segment) = world.roads.segment_named(&truck.leg_segment_id) {
                let (x, z) = world.roads.position(segment, truck.leg_to as f64);
                canvas.dot(x as f32, z as f32, 2, [70, 140, 240, 255], [20, 30, 60, 255]);
            }
        }
    }
    if let Ok(player) = player.single() {
        let (x, z, _) = space.to_map(player.translation);
        canvas.dot(x, z, 3, [255, 255, 255, 255], [20, 20, 20, 255]);
    }
    map.centre = canvas.centre;
    map.half = half;
    let image = Image::new(Extent3d { width: MAP_PIXELS, height: MAP_PIXELS, depth_or_array_layers: 1 }, TextureDimension::D2, canvas.data, TextureFormat::Rgba8UnormSrgb, RenderAssetUsages::RENDER_WORLD | RenderAssetUsages::MAIN_WORLD);
    let existing = map.image.clone().filter(|h| images.get(h).is_some());
    match existing {
        Some(handle) => {
            if let Some(mut slot) = images.get_mut(&handle) {
                *slot = image;
            }
        }
        None => map.image = Some(images.add(image)),
    }
}
