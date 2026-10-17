// The always-on HUD (Unity PlayerHud crosshair and hotbar): a centre crosshair while the cursor is locked to it, and the nine
// hotbar slots along the bottom with each held machine kind's icon, its count and the slot on the cursor highlighted.
use bevy::prelude::*;
use bevy::window::{CursorGrabMode, CursorOptions, PrimaryWindow};

use food_factory_bevy::goods::EquipmentState;

use crate::interaction::{Cursor, Hotbar, HotbarEntry, HOTBAR_SIZE};
use crate::interaction::Aim;
use crate::screens::Screen;
use crate::session::{Content, Session};
use crate::sites::Pick;

pub struct HudPlugin;

impl Plugin for HudPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Startup, spawn_hud).add_systems(Update, (update_hud, update_cursor_tooltip));
    }
}

#[derive(Component)]
struct Crosshair;

#[derive(Component)]
struct HotbarStrip;

#[derive(Component)]
struct HotbarSlot(usize);

#[derive(Component)]
struct HotbarIcon(usize);

#[derive(Component)]
struct HotbarLabel(usize);

#[derive(Component)]
struct CursorTooltip;

fn spawn_hud(mut commands: Commands) {
    commands.spawn((
        Crosshair,
        Node { position_type: PositionType::Absolute, left: percent(50), top: percent(50), width: px(6), height: px(6), margin: UiRect::all(px(-3)), border_radius: BorderRadius::all(px(3)), ..default() },
        BackgroundColor(Color::srgba(1.0, 1.0, 1.0, 0.85)),
        Visibility::Hidden,
    ));
    commands.spawn((
        CursorTooltip,
        Text::new(""),
        TextFont { font_size: FontSize::Px(14.0), ..default() },
        TextColor(Color::srgb(0.94, 0.95, 0.91)),
        Node { position_type: PositionType::Absolute, left: px(0), top: px(0), padding: UiRect::axes(px(9), px(5)), ..default() },
        BackgroundColor(Color::srgba(0.04, 0.06, 0.08, 0.86)),
        Visibility::Hidden,
        Name::new("Cursor tooltip"),
    ));
    let strip = commands
        .spawn((
            HotbarStrip,
            Node { position_type: PositionType::Absolute, bottom: px(14), left: px(0), right: px(0), justify_content: JustifyContent::Center, column_gap: px(6), ..default() },
            Visibility::Hidden,
        ))
        .id();
    for index in 0..HOTBAR_SIZE {
        let slot = commands
            .spawn((
                HotbarSlot(index),
                Node { width: px(58), height: px(58), flex_direction: FlexDirection::Column, align_items: AlignItems::Center, justify_content: JustifyContent::Center, border_radius: BorderRadius::all(px(6)), ..default() },
                BackgroundColor(Color::srgba(0.04, 0.06, 0.08, 0.7)),
            ))
            .id();
        let icon = commands.spawn((HotbarIcon(index), ImageNode::default(), Node { width: px(38), height: px(38), ..default() }, Visibility::Hidden)).id();
        let label = commands.spawn((HotbarLabel(index), Text::new(format!("{}", index + 1)), TextFont { font_size: FontSize::Px(11.0), ..default() }, TextColor(Color::srgb(0.9, 0.93, 0.95)))).id();
        commands.entity(slot).add_children(&[icon, label]);
        commands.entity(strip).add_child(slot);
    }
}

/// Names a usable hover target beside the free pointer; the crosshair HUD hint remains the locked-pointer fallback.
fn update_cursor_tooltip(
    aim: Res<Aim>, session: Res<Session>, content: Res<Content>, screen: Res<Screen>,
    window: Query<(&Window, &CursorOptions), With<PrimaryWindow>>,
    mut tooltip: Query<(&mut Text, &mut Node, &mut Visibility), With<CursorTooltip>>,
) {
    let Ok((mut text, mut node, mut visibility)) = tooltip.single_mut() else { return };
    let Some((window, _cursor)) = window.single().ok().filter(|(_, c)| c.grab_mode == CursorGrabMode::None) else {
        *visibility = Visibility::Hidden;
        return;
    };
    let Some(world) = session.joined() else { *visibility = Visibility::Hidden; return };
    let picked = if aim.in_reach { match aim.pick.as_ref() {
        Some(Pick::Equipment(id)) => world.views.values().find_map(|v| v.equipment.iter().find(|e| &e.id == id)).map(|e| content.0.equipment_name(&e.kind)),
        Some(Pick::Employee(id)) => world.employees.iter().find(|e| &e.id == id).map(|e| e.name.clone()),
        _ => None,
    }} else { None };
    let Some(label) = picked.filter(|_| *screen == Screen::Playing).zip(window.cursor_position()) else {
        *visibility = Visibility::Hidden;
        return;
    };
    let value = format!("E  {}", label.0);
    if text.0 != value { text.0 = value; }
    node.left = px((label.1.x + 16.0).min(window.width() - 160.0).max(4.0));
    node.top = px((label.1.y + 18.0).min(window.height() - 42.0).max(4.0));
    *visibility = Visibility::Inherited;
}

#[allow(clippy::too_many_arguments)]
fn update_hud(
    session: Res<Session>, screen: Res<Screen>, hotbar: Res<Hotbar>, cursor: Res<Cursor>, content: Res<Content>, assets: Res<AssetServer>, window: Query<&CursorOptions, With<PrimaryWindow>>,
    mut crosshair: Query<&mut Visibility, (With<Crosshair>, Without<HotbarStrip>, Without<HotbarIcon>)>, mut strip: Query<&mut Visibility, (With<HotbarStrip>, Without<Crosshair>, Without<HotbarIcon>)>,
    mut slots: Query<(&HotbarSlot, &mut BackgroundColor)>, mut icons: Query<(&HotbarIcon, &mut ImageNode, &mut Visibility), (Without<Crosshair>, Without<HotbarStrip>)>, mut labels: Query<(&HotbarLabel, &mut Text)>,
) {
    let playing = *screen == Screen::Playing && session.in_world();
    let locked = window.single().is_ok_and(|c| c.grab_mode != CursorGrabMode::None);
    for mut v in &mut crosshair {
        let want = if playing && locked { Visibility::Inherited } else { Visibility::Hidden };
        if *v != want {
            *v = want;
        }
    }
    for mut v in &mut strip {
        let want = if playing { Visibility::Inherited } else { Visibility::Hidden };
        if *v != want {
            *v = want;
        }
    }
    let Some(world) = session.joined() else { return };
    let Some(view) = world.view() else { return };
    let current = cursor.machine.as_ref().and_then(|id| view.equipment.iter().find(|e| &e.id == id)).map(|e| HotbarEntry::Machine(e.kind.clone())).or_else(|| cursor.item.clone().map(HotbarEntry::Item));
    for (slot, mut background) in &mut slots {
        let selected = hotbar.entries.get(slot.0).is_some_and(|k| Some(k) == current.as_ref());
        let colour = if selected { Color::srgba(0.22, 0.45, 0.38, 0.9) } else { Color::srgba(0.04, 0.06, 0.08, 0.7) };
        if background.0 != colour {
            background.0 = colour;
        }
    }
    for (icon, mut image, mut visibility) in &mut icons {
        let path = hotbar.entries.get(icon.0).map(|entry| match entry {
            HotbarEntry::Machine(kind) => content.0.equipment.get(kind).map(|k| k.icon.clone()).unwrap_or_default(),
            HotbarEntry::Item(item) => content.0.items.get(item).map(|i| i.icon.clone()).unwrap_or_default(),
        });
        match path {
            Some(path) if !path.is_empty() => {
                let handle: Handle<Image> = assets.load(path);
                if image.image != handle {
                    image.image = handle;
                }
                if *visibility != Visibility::Inherited {
                    *visibility = Visibility::Inherited;
                }
            }
            _ => {
                if *visibility != Visibility::Hidden {
                    *visibility = Visibility::Hidden;
                }
            }
        }
    }
    for (label, mut text) in &mut labels {
        let inventory = world.inventory();
        let count = match hotbar.entries.get(label.0) {
            Some(HotbarEntry::Machine(k)) => view.equipment.iter().filter(|e| e.state == EquipmentState::Held && e.holder_id == world.player && &e.kind == k).count(),
            Some(HotbarEntry::Item(item)) => view.lots.iter().filter(|l| l.location_id == inventory && &l.item_id == item).map(|l| l.quantity.max(0) as usize).sum(),
            None => 0,
        };
        let value = if count > 0 { format!("{}  x{count}", label.0 + 1) } else { format!("{}", label.0 + 1) };
        if text.0 != value {
            text.0 = value;
        }
    }
}
