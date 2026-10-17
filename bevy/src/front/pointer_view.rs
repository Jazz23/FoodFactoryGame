// What rides on the free pointer, above every panel: build mode's hold-to-remove ring (dots that light up clockwise as the
// right click is held, all lit once removing starts) and the goods stack picked up from an inventory slot, drawn as its icon
// and count under the pointer like a normal inventory. Both only ignore the pointer, so clicks still reach the slot below.
use bevy::picking::Pickable;
use bevy::prelude::*;
use bevy::text::FontSize;
use bevy::window::PrimaryWindow;

use super::{Face, Fonts, ACCENT, LINE_3, TEXT};
use crate::panels::{Panel, Panels};
use crate::session::{Content, Session};

const DOTS: usize = 16;
const RING_RADIUS: f32 = 18.0;
const DOT_SIZE: f32 = 6.0;
const CARRIED_SIZE: f32 = 40.0;

#[derive(Component)]
pub(super) struct RemoveRing;

#[derive(Component)]
pub(super) struct RingDot(usize);

#[derive(Component)]
pub(super) struct Carried;

#[derive(Component)]
pub(super) struct CarriedIcon;

#[derive(Component)]
pub(super) struct CarriedCount;

pub(super) fn mount(mut commands: Commands, fonts: Res<Fonts>) {
    let root = commands.spawn((Node { position_type: PositionType::Absolute, left: px(0.0), top: px(0.0), right: px(0.0), bottom: px(0.0), ..default() }, Pickable::IGNORE, GlobalZIndex(14), Name::new("Pointer"))).id();
    let size = RING_RADIUS * 2.0 + DOT_SIZE;
    let ring = commands.spawn((RemoveRing, Node { position_type: PositionType::Absolute, width: px(size), height: px(size), ..default() }, Visibility::Hidden, Pickable::IGNORE)).id();
    for index in 0..DOTS {
        let angle = index as f32 / DOTS as f32 * std::f32::consts::TAU;
        let (x, y) = (RING_RADIUS + angle.sin() * RING_RADIUS, RING_RADIUS - angle.cos() * RING_RADIUS);
        let dot = commands.spawn((RingDot(index), Node { position_type: PositionType::Absolute, left: px(x), top: px(y), width: px(DOT_SIZE), height: px(DOT_SIZE), border_radius: BorderRadius::all(px(DOT_SIZE * 0.5)), ..default() },
            BackgroundColor(LINE_3), Pickable::IGNORE)).id();
        commands.entity(ring).add_child(dot);
    }
    let carried = commands.spawn((Carried, Node { position_type: PositionType::Absolute, width: px(CARRIED_SIZE), height: px(CARRIED_SIZE), ..default() }, Visibility::Hidden, Pickable::IGNORE)).id();
    let icon = commands.spawn((CarriedIcon, ImageNode::default(), Node { width: px(CARRIED_SIZE), height: px(CARRIED_SIZE), ..default() }, Pickable::IGNORE)).id();
    let count = commands.spawn((CarriedCount, Text::new(""), TextFont { font: fonts.get(Face::MonoBold).into(), font_size: FontSize::Px(12.0), ..default() }, TextColor(TEXT),
        Node { position_type: PositionType::Absolute, right: px(0.0), bottom: px(-2.0), ..default() }, Pickable::IGNORE)).id();
    commands.entity(carried).add_children(&[icon, count]);
    commands.entity(root).add_children(&[ring, carried]);
}

#[allow(clippy::type_complexity, clippy::too_many_arguments)]
pub(super) fn follow_pointer(
    window: Query<&Window, With<PrimaryWindow>>, scale: Res<UiScale>, build: Res<crate::build::Build>, panels: Res<Panels>, content: Res<Content>, session: Res<Session>, assets: Res<AssetServer>,
    mut ring: Query<(&mut Node, &mut Visibility), (With<RemoveRing>, Without<Carried>)>, mut dots: Query<(&RingDot, &mut BackgroundColor)>,
    mut carried: Query<(&mut Node, &mut Visibility), (With<Carried>, Without<RemoveRing>)>, mut icon: Query<&mut ImageNode, With<CarriedIcon>>, mut count: Query<&mut Text, With<CarriedCount>>,
    mut shown_icon: Local<String>,
) {
    let pointer = window.single().ok().and_then(|w| w.cursor_position()).map(|p| p / scale.0.max(0.01));
    // The ring around the pointer while build mode's right click is held.
    if let Ok((mut node, mut visibility)) = ring.single_mut() {
        match (build.remove_progress, pointer) {
            (Some(progress), Some(at)) if build.active.is_some() => {
                let half = RING_RADIUS + DOT_SIZE * 0.5;
                node.left = px(at.x - half);
                node.top = px(at.y - half);
                visibility.set_if_neq(Visibility::Inherited);
                let lit = (progress * DOTS as f32).ceil() as usize;
                for (dot, mut colour) in &mut dots {
                    let next = if dot.0 < lit { ACCENT } else { LINE_3 };
                    if colour.0 != next { colour.0 = next; }
                }
            }
            _ => { visibility.set_if_neq(Visibility::Hidden); }
        }
    }
    // The picked-up stack under the pointer while an inventory screen is open.
    let Ok((mut node, mut visibility)) = carried.single_mut() else { return };
    let held = panels.held.as_ref().filter(|_| panels.open.as_ref().is_some_and(|p| *p != Panel::Build) && session.joined().is_some());
    let (Some(held), Some(at)) = (held, pointer) else {
        visibility.set_if_neq(Visibility::Hidden);
        return;
    };
    node.left = px(at.x - CARRIED_SIZE * 0.35);
    node.top = px(at.y - CARRIED_SIZE * 0.35);
    visibility.set_if_neq(Visibility::Inherited);
    let path = content.0.items.get(&held.item).map(|i| i.icon.clone()).unwrap_or_default();
    if *shown_icon != path {
        if let Ok(mut image) = icon.single_mut() {
            image.image = if path.is_empty() { Handle::default() } else { assets.load(path.clone()) };
        }
        *shown_icon = path;
    }
    if let Ok(mut text) = count.single_mut() {
        let next = if held.quantity > 1 { held.quantity.to_string() } else { String::new() };
        if text.0 != next { text.0 = next; }
    }
}
