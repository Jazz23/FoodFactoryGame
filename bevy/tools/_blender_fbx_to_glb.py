"""Blender CLI worker: import one Unity FBX and export a self-contained glTF binary."""
import bpy
import sys
from pathlib import Path

argv = sys.argv[sys.argv.index("--") + 1:]
source, destination = map(Path, argv)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(source), use_image_search=True)
# FBX sources generally carry materials; pack linked images before GLB export when present.
for image in bpy.data.images:
    if image.source == "FILE" and image.filepath and Path(bpy.path.abspath(image.filepath)).is_file():
        try:
            image.pack()
        except RuntimeError:
            pass
destination.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(destination), export_format="GLB", export_image_format="AUTO", export_materials="EXPORT", export_apply=True)
