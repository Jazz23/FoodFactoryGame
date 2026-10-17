// The window and taskbar icon: the main menu's logo tile (the orange factory glyph on a dark rounded square), drawn once from
// the embedded glyph and handed to winit as soon as the primary window exists. winit windows live on the main thread only.
use bevy::ecs::system::NonSendMarker;
use bevy::prelude::*;
use bevy::window::PrimaryWindow;
use bevy::winit::WINIT_WINDOWS;

pub struct WindowIconPlugin;

impl Plugin for WindowIconPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(Update, set_icon);
    }
}

const GLYPH: &[u8] = include_bytes!("../assets/ui/game/icons/factory-f2a33a.png");
const SIZE: u32 = 128;
const GLYPH_SIZE: u32 = 84;
const RADIUS: f32 = 26.0;
const BORDER: f32 = 3.0;
/// The design's RAISED tile and its LINE border.
const TILE: [u8; 3] = [0x2A, 0x2C, 0x30];
const EDGE: [u8; 3] = [0x4A, 0x4D, 0x53];

fn set_icon(_main_thread: NonSendMarker, window: Query<Entity, With<PrimaryWindow>>, mut done: Local<bool>) {
    if *done {
        return;
    }
    let Ok(entity) = window.single() else { return };
    let Some(icon) = icon() else {
        *done = true;
        return;
    };
    WINIT_WINDOWS.with_borrow(|windows| {
        if let Some(window) = windows.get_window(entity) {
            window.set_window_icon(Some(icon));
            *done = true;
        }
    });
}

/// Coverage of a pixel by a rounded square inset by `inset`, antialiased over one pixel.
fn rounded(x: f32, y: f32, inset: f32) -> f32 {
    let half = SIZE as f32 * 0.5 - inset;
    let radius = (RADIUS - inset).max(0.0);
    let centre = SIZE as f32 * 0.5;
    let qx = ((x - centre).abs() - (half - radius)).max(0.0);
    let qy = ((y - centre).abs() - (half - radius)).max(0.0);
    let distance = (qx * qx + qy * qy).sqrt() - radius;
    (0.5 - distance).clamp(0.0, 1.0)
}

fn icon() -> Option<winit::window::Icon> {
    let glyph = image::load_from_memory(GLYPH).ok()?.into_rgba8();
    let glyph = image::imageops::resize(&glyph, GLYPH_SIZE, GLYPH_SIZE, image::imageops::FilterType::Lanczos3);
    let offset = (SIZE - GLYPH_SIZE) / 2;
    let mut rgba = Vec::with_capacity((SIZE * SIZE * 4) as usize);
    for y in 0..SIZE {
        for x in 0..SIZE {
            let (px, py) = (x as f32 + 0.5, y as f32 + 0.5);
            let outer = rounded(px, py, 0.0);
            let inner = rounded(px, py, BORDER);
            // The border colour where only the outer shape covers, the tile inside it.
            let mut colour = [0.0f32; 3];
            for c in 0..3 {
                colour[c] = EDGE[c] as f32 * (1.0 - inner) + TILE[c] as f32 * inner;
            }
            if (offset..offset + GLYPH_SIZE).contains(&x) && (offset..offset + GLYPH_SIZE).contains(&y) {
                let g = glyph.get_pixel(x - offset, y - offset).0;
                let a = g[3] as f32 / 255.0;
                for c in 0..3 {
                    colour[c] = colour[c] * (1.0 - a) + g[c] as f32 * a;
                }
            }
            rgba.extend([colour[0].round() as u8, colour[1].round() as u8, colour[2].round() as u8, (outer * 255.0).round() as u8]);
        }
    }
    winit::window::Icon::from_rgba(rgba, SIZE, SIZE).ok()
}
