// The phone's world map picture (Unity PhoneMapView, decision 0047): the layout's districts, rivers, roads and every lot from
// above, coloured by what the lot is and who owns it (yours green, for sale yellow, competitors red, farms brown, stations and
// scenery grey), with the avatar as a white dot and the company's trucks as blue dots. Map x runs right and z up. Redrawn twice
// a second while the phone's map tab is open. Scroll zooms at the pointer; dragging pans; clicking selects a lot.
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
        app.init_resource::<PhoneMap>().add_systems(Update, (navigate, draw_map).chain());
    }
}

pub const MAP_PIXELS: u32 = 480;

#[derive(Component)]
pub struct MapSurface;

#[derive(Resource, Default)]
pub struct PhoneMap {
    pub image: Option<Handle<Image>>,
    timer: f32,
    /// The map area drawn: centre (map metres) and half size.
    pub centre: Vec2,
    pub half: f32,
    world: String,
    drag: Option<(Vec2, Vec2, bool)>,
}

impl PhoneMap {
    fn point(&self, relative: Vec2) -> Vec2 { self.centre + Vec2::new(relative.x, -relative.y) * self.half * 2.0 }
    fn zoom(&mut self, relative: Vec2, factor: f32, limit: f32) {
        let anchor = self.point(relative);
        self.half = (self.half * factor).clamp(20.0, limit);
        self.centre += anchor - self.point(relative);
        self.timer = 0.0;
    }
}

fn navigate(mut map: ResMut<PhoneMap>, session: Res<Session>, panels: Res<Panels>, surfaces: Query<&bevy::ui::RelativeCursorPosition, With<MapSurface>>,
    mouse: Res<ButtonInput<MouseButton>>, wheel: Res<bevy::input::mouse::AccumulatedMouseScroll>, mut intents: ResMut<crate::ui::UiIntents>) {
    let Some(world) = session.joined() else { map.world.clear(); map.drag = None; return };
    let limit = world.layout.city_half_size as f32 + 120.0;
    if map.world != world.world_id { map.world = world.world_id.clone(); map.centre = Vec2::ZERO; map.half = limit; map.timer = 0.0; }
    if panels.open != Some(Panel::Phone(PhoneTab::Map)) { map.drag = None; return; }
    let actions = intents.0.iter().filter(|a| a.starts_with("map:")).cloned().collect::<Vec<_>>();
    intents.0.retain(|a| !a.starts_with("map:"));
    for action in actions {
        match action.as_str() {
            "map:in" => map.zoom(Vec2::ZERO, 0.5, limit),
            "map:out" => map.zoom(Vec2::ZERO, 2.0, limit),
            "map:reset" => { map.centre = Vec2::ZERO; map.half = limit; },
            "map:left" => { let half = map.half; map.centre.x -= half * 0.5; },
            "map:right" => { let half = map.half; map.centre.x += half * 0.5; },
            "map:up" => { let half = map.half; map.centre.y += half * 0.5; },
            "map:down" => { let half = map.half; map.centre.y -= half * 0.5; },
            _ => {},
        }
        map.timer = 0.0;
    }
    if let Ok(cursor) = surfaces.single() {
        if let Some(p) = cursor.normalized {
            if cursor.cursor_over() && wheel.delta.y != 0.0 { map.zoom(p, (-wheel.delta.y * 0.18).exp(), limit); }
            if cursor.cursor_over() && mouse.just_pressed(MouseButton::Left) { map.drag = Some((p, map.centre, false)); }
            if let Some((start, centre, dragged)) = map.drag {
                let dragged = dragged || (p - start).length() * MAP_PIXELS as f32 > 4.0;
                if mouse.pressed(MouseButton::Left) && dragged {
                    map.centre = centre - Vec2::new(p.x - start.x, start.y - p.y) * map.half * 2.0;
                    map.timer = 0.0;
                }
                map.drag = Some((start, centre, dragged));
                if mouse.just_released(MouseButton::Left) {
                    if !dragged && cursor.cursor_over() {
                        let at = map.point(p);
                        let reach = 6.0 * map.half * 2.0 / MAP_PIXELS as f32;
                        let truck = truck_points(world).into_iter().map(|(id, p)| (p.distance(at), id)).filter(|(d, _)| *d <= reach).min_by(|a, b| a.0.total_cmp(&b.0));
                        if let Some((_, id)) = truck {
                            intents.0.push(format!("p:select-truck|{id}"));
                        } else if let Some(lot) = lot_at(&world.layout.lots, at) {
                            intents.0.push(format!("p:property|{}", lot.id));
                        }
                    }
                    map.drag = None;
                }
            }
        }
    }
    map.centre = map.centre.clamp(Vec2::splat(-limit), Vec2::splat(limit));
}

/// The lot under a point in map metres (x east, y north).
fn lot_at(lots: &[food_factory_bevy::world::layout::WorldLot], at: Vec2) -> Option<&food_factory_bevy::world::layout::WorldLot> {
    lots.iter().find(|l| at.x >= l.x as f32 && at.x < (l.x + l.width) as f32 && at.y >= l.z as f32 && at.y < (l.z + l.depth) as f32)
}

/// Where each company truck shows on the map, in map metres: on its road leg while driving, else at its site's lot centre.
pub fn truck_points(world: &crate::session::Joined) -> Vec<(String, Vec2)> {
    let Some(view) = world.view() else { return vec![] };
    let Some(own) = world.company().map(|c| c.id.clone()) else { return vec![] };
    view.trucks.iter().filter(|t| t.company_id == own && !t.supplier).filter_map(|truck| {
        let at = if truck.on_road() {
            let segment = world.roads.segment_named(&truck.leg_segment_id)?;
            let (x, z) = world.roads.position(segment, truck.leg_to as f64);
            Vec2::new(x as f32, z as f32)
        } else {
            let lot = world.layout.lots.iter().find(|l| l.site_id == truck.site_id)?;
            Vec2::new(lot.x as f32 + lot.width as f32 * 0.5, lot.z as f32 + lot.depth as f32 * 0.5)
        };
        Some((truck.id.clone(), at))
    }).collect()
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
        for y in a.1.max(0)..=b.1.max(a.1).min(MAP_PIXELS as i32 - 1) {
            for x in a.0.max(0)..=b.0.max(a.0).min(MAP_PIXELS as i32 - 1) {
                self.put(x, y, colour);
            }
        }
    }

    fn line(&mut self, from: Vec2, to: Vec2, width: f32, colour: [u8; 4]) {
        let delta = to - from;
        let mut near = 0.0f32; let mut far = 1.0f32;
        for axis in 0..2 {
            let lo = self.centre[axis] - self.half - width; let hi = self.centre[axis] + self.half + width;
            if delta[axis].abs() < 1e-6 { if from[axis] < lo || from[axis] > hi { return; } }
            else { let a = (lo - from[axis]) / delta[axis]; let b = (hi - from[axis]) / delta[axis]; near = near.max(a.min(b)); far = far.min(a.max(b)); }
        }
        if near > far { return; }
        let (from, to) = (from + delta * near, from + delta * far);
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
    let half = map.half.max(20.0);
    let mut canvas = Canvas { data: vec![0; (MAP_PIXELS * MAP_PIXELS * 4) as usize], centre: map.centre, half };
    // Ground and districts.
    canvas.rect(map.centre.x - half, map.centre.y - half, map.centre.x + half, map.centre.y + half, [124, 160, 96, 255]);
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
    // The company's fleet (Unity LogisticsPanel feeding PhoneMapView.SetTrucks): road trucks on their leg, others at their
    // site; the truck selected on the Trucks tab is drawn larger and outlined.
    for (id, at) in truck_points(world) {
        let selected = panels.selected_truck.as_deref() == Some(id.as_str());
        canvas.dot(at.x, at.y, if selected { 4 } else { 2 }, [70, 140, 240, 255], if selected { [255, 220, 60, 255] } else { [20, 30, 60, 255] });
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

#[cfg(test)]
mod tests {
    use super::*;
    use food_factory_bevy::world::{generator, settings::WorldSettings};

    /// A click (centre-origin normalized pointer, +y down) lands on the lot drawn under it, zoomed and panned.
    #[test]
    fn click_on_a_drawn_lot_selects_that_lot() {
        let layout = generator::generate("phone-map", 20260927, &WorldSettings::default()).unwrap().layout;
        let lot = &layout.lots[layout.lots.len() / 2];
        let middle = Vec2::new(lot.x as f32 + lot.width as f32 * 0.5, lot.z as f32 + lot.depth as f32 * 0.5);
        let map = PhoneMap { centre: middle + Vec2::new(30.0, -45.0), half: 120.0, ..default() };
        let canvas = Canvas { data: vec![], centre: map.centre, half: map.half };
        let (u, v) = canvas.pixel(middle.x, middle.y);
        let pointer = Vec2::new((u as f32 + 0.5) / MAP_PIXELS as f32 - 0.5, (v as f32 + 0.5) / MAP_PIXELS as f32 - 0.5);
        assert_eq!(lot_at(&layout.lots, map.point(pointer)).map(|l| &l.id), Some(&lot.id));
    }
}
