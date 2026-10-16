// The land surface of a generated world: heights in centimetres on a square grid of samples covering the map, sampled
// bilinearly. The integer sampler is the generator's and validator's; the float sampler is for presentation and agrees with
// it to within rounding.
use serde::{Deserialize, Serialize};

use super::geometry::floor_div;
use super::layout::RoadKind;
use super::layout::JunctionControl;

#[derive(Clone, Debug, Serialize, Deserialize, Default)]
pub struct WorldTerrain {
    /// Metres between samples; 0 means flat land at height 0.
    pub spacing: i32,
    /// Samples per side, centred on the origin: sample 0 sits at -(samples - 1) * spacing / 2 on both axes.
    pub samples: i32,
    /// Row-major by Z then X.
    pub heights_cm: Vec<i32>,
}

impl WorldTerrain {
    pub fn flat() -> Self { Self::default() }

    pub fn is_flat(&self) -> bool { self.spacing == 0 || self.samples < 2 }

    pub fn origin(&self) -> i32 { -(self.samples - 1) * self.spacing / 2 }

    pub fn sample(&self, x: i32, z: i32) -> i32 {
        self.heights_cm[(z.clamp(0, self.samples - 1) * self.samples + x.clamp(0, self.samples - 1)) as usize]
    }

    /// Bilinear height in centimetres at a point given in doubled metres, rounded down. Points past the grid take the edge.
    pub fn height_cm(&self, doubled_x: i32, doubled_z: i32) -> i32 {
        if self.is_flat() {
            return 0;
        }
        let cell = 2i64 * self.spacing as i64;
        let span = cell * (self.samples as i64 - 1);
        let px = (doubled_x as i64 - 2 * self.origin() as i64).clamp(0, span);
        let pz = (doubled_z as i64 - 2 * self.origin() as i64).clamp(0, span);
        let ix = (px / cell).min(self.samples as i64 - 2) as i32;
        let iz = (pz / cell).min(self.samples as i64 - 2) as i32;
        let fx = px - ix as i64 * cell;
        let fz = pz - iz as i64 * cell;
        let sum = self.sample(ix, iz) as i64 * (cell - fx) * (cell - fz)
            + self.sample(ix + 1, iz) as i64 * fx * (cell - fz)
            + self.sample(ix, iz + 1) as i64 * (cell - fx) * fz
            + self.sample(ix + 1, iz + 1) as i64 * fx * fz;
        floor_div(sum, cell * cell) as i32
    }

    /// Height of a whole-metre cell's centre.
    pub fn cell_height_cm(&self, x: i32, z: i32) -> i32 { self.height_cm(2 * x + 1, 2 * z + 1) }

    /// Height in metres at a point in metres (presentation).
    pub fn height(&self, x: f32, z: f32) -> f32 {
        if self.is_flat() {
            return 0.0;
        }
        let span = (self.spacing * (self.samples - 1)) as f32;
        let origin = self.origin() as f32;
        let px = (x - origin).clamp(0.0, span) / self.spacing as f32;
        let pz = (z - origin).clamp(0.0, span) / self.spacing as f32;
        let ix = (px as i32).min(self.samples - 2);
        let iz = (pz as i32).min(self.samples - 2);
        let fx = px - ix as f32;
        let fz = pz - iz as f32;
        let h = self.sample(ix, iz) as f32 * (1.0 - fx) * (1.0 - fz)
            + self.sample(ix + 1, iz) as f32 * fx * (1.0 - fz)
            + self.sample(ix, iz + 1) as f32 * (1.0 - fx) * fz
            + self.sample(ix + 1, iz + 1) as f32 * fx * fz;
        h / 100.0
    }

    /// The same land turned a quarter clockwise about the origin.
    pub fn turned(&self) -> Self {
        if self.is_flat() {
            return self.clone();
        }
        let mut heights_cm = vec![0; self.heights_cm.len()];
        for z in 0..self.samples {
            for x in 0..self.samples {
                heights_cm[(z * self.samples + x) as usize] = self.sample(self.samples - 1 - z, x);
            }
        }
        Self { spacing: self.spacing, samples: self.samples, heights_cm }
    }
}

pub fn junction_rank(kind: RoadKind) -> i32 {
    match kind {
        RoadKind::Arterial => 2,
        RoadKind::Local => 1,
        RoadKind::Rural => 0,
    }
}

/// Whether traffic arriving on a road of kind `approach` must stop at a node with this control, given every kind there.
pub fn junction_stops(control: JunctionControl, approach: RoadKind, kinds_at_node: impl IntoIterator<Item = RoadKind>) -> bool {
    if control != JunctionControl::StopSign {
        return false;
    }
    let mut highest = 0;
    let mut lowest = i32::MAX;
    for kind in kinds_at_node {
        highest = highest.max(junction_rank(kind));
        lowest = lowest.min(junction_rank(kind));
    }
    highest == lowest || junction_rank(approach) < highest
}
