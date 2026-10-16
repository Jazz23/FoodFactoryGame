// Port of the Unity Player.controller animator. Parameters come from how the avatar actually moves (as in the Unity
// PlayerAnimation), not from input. States: Locomotion (1D blend of Idle/Walk/Run on Speed, clips phase-synced like a
// Unity blend tree), Jump, Fall and Land, with Unity's transition conditions and fixed crossfade durations. Every clip
// stays paused in the Bevy AnimationPlayer; this module sets each clip's time and weight itself every frame.
use bevy::gltf::{Gltf, GltfMaterialName};
use bevy::prelude::*;
use bevy::world_serialization::{WorldAssetRoot, WorldInstanceReady};

use crate::player::PlayerSet;

pub struct CharacterAnimationPlugin;

impl Plugin for CharacterAnimationPlugin {
    fn build(&self, app: &mut App) {
        app.add_systems(
            Update,
            (spawn_models, drive_animator.after(PlayerSet::Move)),
        );
    }
}

// Clip names as exported from ArtSource/Player (glTF animation names).
const CLIP_NAMES: [&str; 6] = ["Idle", "Walk", "Run", "Jump", "Fall", "Land"];
const IDLE: usize = 0;
const WALK: usize = 1;
const RUN: usize = 2;
const JUMP: usize = 3;
const FALL: usize = 4;
const LAND: usize = 5;
// Locomotion blend tree thresholds (m/s) for Idle, Walk, Run; Walk and Run are authored for these ground speeds.
const LOCOMOTION_THRESHOLDS: [f32; 3] = [0.0, 1.6, 6.0];

// How far above the floor (metres) still counts as underfoot, so a jump's first instant does not read as airborne.
const GROUND_PROBE: f32 = 0.25;
// A position change larger than this in one frame is a teleport rather than motion.
const TELEPORT_DISTANCE: f32 = 1.5;
// Velocity smoothing rate (1/s).
const SMOOTHING: f32 = 12.0;
const SPEED_DAMP_SECONDS: f32 = 0.1;
// Shirt colour; Unity picks a stable hue per player name, and this sandbox has a single player.
const SHIRT_COLOR: Color = Color::srgb(0.25, 0.52, 0.80);

#[derive(Component)]
pub struct CharacterModel {
    gltf: Handle<Gltf>,
    spawned: bool,
}

impl CharacterModel {
    pub fn new(gltf: Handle<Gltf>) -> Self {
        Self {
            gltf,
            spawned: false,
        }
    }
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
enum State {
    Locomotion,
    Jump,
    Fall,
    Land,
}

impl State {
    const ALL: [State; 4] = [State::Locomotion, State::Jump, State::Fall, State::Land];

    fn index(self) -> usize {
        self as usize
    }
}

// Lives on the character's root (the moving entity), pointing at the glTF AnimationPlayer below it.
#[derive(Component)]
struct Animator {
    animation_player: Entity,
    nodes: [AnimationNodeIndex; 6],
    durations: [f32; 6],
    state: State,
    // Seconds since each state was entered; it keeps running while the state fades out.
    state_time: [f32; 4],
    // Locomotion normalized time shared by Idle, Walk and Run.
    locomotion_phase: f32,
    weights: [f32; 4],
    fade_from: [f32; 4],
    fade_elapsed: f32,
    fade_duration: f32,
    last_position: Option<Vec3>,
    velocity: Vec3,
    speed: f32,
    speed_velocity: f32,
}

impl Animator {
    fn new(animation_player: Entity, nodes: [AnimationNodeIndex; 6], durations: [f32; 6]) -> Self {
        Self {
            animation_player,
            nodes,
            durations,
            state: State::Locomotion,
            state_time: [0.0; 4],
            locomotion_phase: 0.0,
            weights: [1.0, 0.0, 0.0, 0.0],
            fade_from: [1.0, 0.0, 0.0, 0.0],
            fade_elapsed: 0.0,
            fade_duration: 0.0,
            last_position: None,
            velocity: Vec3::ZERO,
            speed: 0.0,
            speed_velocity: 0.0,
        }
    }

    fn normalized_time(&self, state: State, clip: usize) -> f32 {
        self.state_time[state.index()] / self.durations[clip]
    }

    // Unity's transitions, in the order the controller lists them; the first whose conditions hold wins.
    fn next_transition(&self, grounded: bool, vertical_speed: f32) -> Option<(State, f32)> {
        let launched = !grounded && vertical_speed > 1.0;
        match self.state {
            State::Locomotion if launched => Some((State::Jump, 0.1)),
            State::Locomotion if !grounded && vertical_speed < -1.0 => Some((State::Fall, 0.2)),
            State::Jump if grounded => Some((State::Land, 0.05)),
            State::Jump if vertical_speed < 0.0 => Some((State::Fall, 0.3)),
            State::Fall if grounded => Some((State::Land, 0.05)),
            State::Land if launched => Some((State::Jump, 0.1)),
            State::Land if self.speed > 3.0 => Some((State::Locomotion, 0.2)),
            State::Land if self.normalized_time(State::Land, LAND) >= 0.8 => {
                Some((State::Locomotion, 0.15))
            }
            _ => None,
        }
    }

    fn enter(&mut self, state: State, duration: f32) {
        self.fade_from = self.weights;
        self.fade_elapsed = 0.0;
        self.fade_duration = duration;
        self.state = state;
        self.state_time[state.index()] = 0.0;
        if state == State::Locomotion {
            self.locomotion_phase = 0.0;
        }
    }

    fn advance(&mut self, delta: f32, position: Vec3) {
        // Animator parameters, measured from motion.
        match self.last_position {
            Some(last)
                if last.distance_squared(position) < TELEPORT_DISTANCE * TELEPORT_DISTANCE =>
            {
                let measured = (position - last) / delta;
                self.velocity = self
                    .velocity
                    .lerp(measured, 1.0 - (-SMOOTHING * delta).exp());
            }
            _ => self.velocity = Vec3::ZERO,
        }
        self.last_position = Some(position);
        let ground_speed = self.velocity.xz().length();
        self.speed = smooth_damp(
            self.speed,
            ground_speed,
            &mut self.speed_velocity,
            SPEED_DAMP_SECONDS,
            delta,
        );
        let grounded = position.y <= GROUND_PROBE;

        if let Some((state, duration)) = self.next_transition(grounded, self.velocity.y) {
            self.enter(state, duration);
        }

        for time in &mut self.state_time {
            *time += delta;
        }
        let blend = locomotion_blend(self.speed);
        // A Unity blend tree plays its children at one shared normalized time, at the weighted average rate.
        let rate: f32 = (0..3).map(|i| blend[i] / self.durations[i]).sum();
        self.locomotion_phase = (self.locomotion_phase + rate * delta).fract();

        self.fade_elapsed += delta;
        let k = if self.fade_duration > 0.0 {
            (self.fade_elapsed / self.fade_duration).min(1.0)
        } else {
            1.0
        };
        for state in State::ALL {
            let target = if state == self.state { 1.0 } else { 0.0 };
            self.weights[state.index()] = self.fade_from[state.index()].lerp(target, k);
        }
    }

    // Each clip's (seek time, weight) for this frame.
    fn clip_poses(&self) -> [(f32, f32); 6] {
        let blend = locomotion_blend(self.speed);
        let locomotion = self.weights[State::Locomotion.index()];
        let one_shot =
            |state: State, clip: usize| self.state_time[state.index()].min(self.durations[clip]);
        let looping = |state: State, clip: usize| {
            self.state_time[state.index()].rem_euclid(self.durations[clip])
        };
        let mut poses = [(0.0, 0.0); 6];
        for clip in [IDLE, WALK, RUN] {
            poses[clip] = (
                self.locomotion_phase * self.durations[clip],
                locomotion * blend[clip],
            );
        }
        poses[JUMP] = (
            one_shot(State::Jump, JUMP),
            self.weights[State::Jump.index()],
        );
        poses[FALL] = (
            looping(State::Fall, FALL),
            self.weights[State::Fall.index()],
        );
        poses[LAND] = (
            one_shot(State::Land, LAND),
            self.weights[State::Land.index()],
        );
        poses
    }
}

// 1D blend weights for Idle, Walk, Run: linear between the two neighbouring thresholds, clamped at the ends.
fn locomotion_blend(speed: f32) -> [f32; 3] {
    let [idle, walk, run] = LOCOMOTION_THRESHOLDS;
    if speed <= idle {
        [1.0, 0.0, 0.0]
    } else if speed < walk {
        let k = (speed - idle) / (walk - idle);
        [1.0 - k, k, 0.0]
    } else if speed < run {
        let k = (speed - walk) / (run - walk);
        [0.0, 1.0 - k, k]
    } else {
        [0.0, 0.0, 1.0]
    }
}

// Critically damped approach to target (Unity's Mathf.SmoothDamp, which Animator.SetFloat damping follows).
fn smooth_damp(current: f32, target: f32, velocity: &mut f32, smooth_time: f32, delta: f32) -> f32 {
    let omega = 2.0 / smooth_time.max(0.0001);
    let x = omega * delta;
    let exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
    let change = current - target;
    let temp = (*velocity + omega * change) * delta;
    *velocity = (*velocity - omega * temp) * exp;
    target + (change + temp) * exp
}

// Once the glTF has loaded: build the animation graph and spawn the model's scene under the CharacterModel entity.
fn spawn_models(
    mut commands: Commands,
    gltfs: Res<Assets<Gltf>>,
    clips: Res<Assets<AnimationClip>>,
    mut graphs: ResMut<Assets<AnimationGraph>>,
    mut materials: ResMut<Assets<StandardMaterial>>,
    mut models: Query<(Entity, &mut CharacterModel, &ChildOf)>,
) {
    for (entity, mut model, child_of) in &mut models {
        if model.spawned {
            continue;
        }
        let Some(gltf) = gltfs.get(&model.gltf) else {
            continue;
        };
        let handles = CLIP_NAMES.map(|name| gltf.named_animations[name].clone());
        // The clips are labelled sub-assets of the glTF, so they are loaded together with it.
        let durations = handles
            .each_ref()
            .map(|handle| clips.get(handle).map_or(1.0, AnimationClip::duration));
        let (graph, nodes) = AnimationGraph::from_clips(handles);
        let nodes: [AnimationNodeIndex; 6] = nodes.try_into().expect("one node per clip");
        let graph = AnimationGraphHandle(graphs.add(graph));
        let shirt = materials.add(StandardMaterial {
            base_color: SHIRT_COLOR,
            perceptual_roughness: 0.8,
            double_sided: true,
            cull_mode: None,
            ..default()
        });
        let character = child_of.parent();
        model.spawned = true;
        commands
            .entity(entity)
            .insert(WorldAssetRoot(gltf.scenes[0].clone()))
            .observe(
                move |ready: On<WorldInstanceReady>,
                      mut commands: Commands,
                      children: Query<&Children>,
                      mut players: Query<&mut AnimationPlayer>,
                      named_materials: Query<&GltfMaterialName>| {
                    for descendant in children.iter_descendants(ready.event().entity) {
                        if named_materials
                            .get(descendant)
                            .is_ok_and(|name| name.0 == "PL_Shirt")
                        {
                            commands
                                .entity(descendant)
                                .insert(MeshMaterial3d(shirt.clone()));
                        }
                        let Ok(mut player) = players.get_mut(descendant) else {
                            continue;
                        };
                        for (index, node) in nodes.iter().enumerate() {
                            player.play(*node).pause().set_weight(if index == IDLE {
                                1.0
                            } else {
                                0.0
                            });
                        }
                        commands.entity(descendant).insert(graph.clone());
                        commands
                            .entity(character)
                            .insert(Animator::new(descendant, nodes, durations));
                    }
                },
            );
    }
}

fn drive_animator(
    time: Res<Time>,
    mut animators: Query<(&mut Animator, &Transform)>,
    mut players: Query<&mut AnimationPlayer>,
) {
    let delta = time.delta_secs();
    if delta <= 0.0 {
        return;
    }
    for (mut animator, transform) in &mut animators {
        animator.advance(delta, transform.translation);
        let Ok(mut player) = players.get_mut(animator.animation_player) else {
            continue;
        };
        for (node, (seek, weight)) in animator.nodes.iter().zip(animator.clip_poses()) {
            if let Some(active) = player.animation_mut(*node) {
                active.set_seek_time(seek).set_weight(weight);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn animator() -> Animator {
        Animator::new(
            Entity::PLACEHOLDER,
            [AnimationNodeIndex::new(0); 6],
            [2.0, 1.0, 0.667, 0.4, 1.0, 0.467],
        )
    }

    #[test]
    fn blend_tree_weights_follow_thresholds() {
        for (speed, expected) in [
            (0.0, [1.0, 0.0, 0.0]),
            (0.8, [0.5, 0.5, 0.0]),
            (3.8, [0.0, 0.5, 0.5]),
            (9.0, [0.0, 0.0, 1.0]),
        ] {
            let weights = locomotion_blend(speed);
            assert!(
                weights
                    .iter()
                    .zip(expected)
                    .all(|(a, b)| (a - b).abs() < 1e-5),
                "{speed}: {weights:?}"
            );
        }
    }

    #[test]
    fn jump_arc_runs_jump_fall_land_then_locomotion() {
        let mut animator = animator();
        let delta = 1.0 / 60.0;
        let (gravity, mut vertical, mut y) = (-20.0_f32, (2.0_f32 * 20.0 * 1.2).sqrt(), 0.0_f32);
        let mut visited = vec![animator.state];
        for _ in 0..240 {
            vertical += gravity * delta;
            y = (y + vertical * delta).max(0.0);
            animator.advance(delta, Vec3::new(0.0, y, 0.0));
            if visited.last() != Some(&animator.state) {
                visited.push(animator.state);
            }
        }
        assert_eq!(
            visited,
            [
                State::Locomotion,
                State::Jump,
                State::Fall,
                State::Land,
                State::Locomotion
            ]
        );
        let total: f32 = animator.clip_poses().iter().map(|(_, weight)| weight).sum();
        assert!((total - 1.0).abs() < 1e-4);
    }
}
