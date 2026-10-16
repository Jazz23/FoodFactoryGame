"""Exports the Food Factory player (ArtSource/Player/Player_Asset.blend) to bevy/assets/models/player.glb with every
action as its own glTF animation. Run:
    blender -b ArtSource/Player/Player_Asset.blend --python bevy/tools/export_player_glb.py -- <out.glb>
"""
import sys
import bpy

out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.object.select_all(action='DESELECT')
for obj in bpy.data.objects:
    if obj.type in {'ARMATURE', 'MESH'}:
        obj.select_set(True)
        print('export object', obj.name, obj.type)
print('actions', [(a.name, tuple(a.frame_range)) for a in bpy.data.actions])
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=True, export_animations=True,
                          export_animation_mode='ACTIONS', export_force_sampling=True, export_frame_step=1,
                          export_optimize_animation_size=False, export_def_bones=False, export_skins=True,
                          export_image_format='AUTO', export_apply=True)
