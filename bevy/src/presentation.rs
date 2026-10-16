// Reconciles server-derived visual records into local render entities; visibility never owns gameplay state.
use std::collections::BTreeMap;
use bevy::prelude::*;
use bevy::world_serialization::WorldAssetRoot;
use crate::player::SolidBox;

pub struct PresentationPlugin;
impl Plugin for PresentationPlugin {
    fn build(&self, app: &mut App) {
        app.init_resource::<SceneModel>().add_systems(Update, reconcile);
    }
}

#[derive(Clone, PartialEq)]
pub struct Visual {
    pub id: String,
    pub position: Vec3,
    pub size: Vec3,
    pub yaw: f32,
    pub color: Color,
    pub model: Option<String>,
    pub solid: bool,
}
#[derive(Resource, Default)]
pub struct SceneModel(pub Vec<Visual>);
#[derive(Component)]
struct WorldVisual;

fn reconcile(
    mut commands: Commands,
    scene: Res<SceneModel>,
    assets: Res<AssetServer>,
    mut meshes: ResMut<Assets<Mesh>>,
    mut materials: ResMut<Assets<StandardMaterial>>,
    mut rendered: Local<BTreeMap<String, (Visual, Entity)>>,
    mut cube: Local<Option<Handle<Mesh>>>,
    mut palette: Local<BTreeMap<String, Handle<StandardMaterial>>>,
) {
    if !scene.is_changed() { return; }
    let cube = cube.get_or_insert_with(|| meshes.add(Cuboid::new(1.0, 1.0, 1.0))).clone();
    let live: std::collections::BTreeSet<_> = scene.0.iter().map(|v| v.id.as_str()).collect();
    rendered.retain(|id, (_, entity)| {
        if live.contains(id.as_str()) { true } else { commands.entity(*entity).despawn(); false }
    });
    for visual in &scene.0 {
        if let Some((old, entity)) = rendered.get_mut(&visual.id) {
            if old == visual { continue; }
            if old.size == visual.size && old.color == visual.color && old.model == visual.model && !visual.solid {
                commands.entity(*entity).insert(Transform::from_translation(visual.position)
                    .with_rotation(Quat::from_rotation_y(visual.yaw)));
                *old = visual.clone();
                continue;
            }
            commands.entity(*entity).despawn();
        }
        let entity = commands.spawn((WorldVisual, Name::new(visual.id.clone()),
            Transform::from_translation(visual.position).with_rotation(Quat::from_rotation_y(visual.yaw)),
            Visibility::default())).id();
        if visual.solid {
            let rotation = Quat::from_rotation_y(visual.yaw);
            let half = (rotation * (visual.size * 0.5)).abs();
            commands.entity(entity).insert(SolidBox { min: visual.position - half, max: visual.position + half });
        }
        if let Some(path) = &visual.model {
            let child = commands.spawn((WorldAssetRoot(assets.load(GltfAssetLabel::Scene(0).from_asset(path.clone()))),
                Transform::from_xyz(0.0, -visual.size.y * 0.5, 0.0))).id();
            commands.entity(entity).add_child(child);
        } else {
            let key = format!("{:?}", visual.color);
            let material = palette.entry(key).or_insert_with(|| materials.add(StandardMaterial {
                base_color: visual.color, perceptual_roughness: 0.8, ..default()
            })).clone();
            let child = commands.spawn((Mesh3d(cube.clone()), MeshMaterial3d(material),
                Transform::from_scale(visual.size))).id();
            commands.entity(entity).add_child(child);
        }
        rendered.insert(visual.id.clone(), (visual.clone(), entity));
    }
}
