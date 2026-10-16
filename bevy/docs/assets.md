# Bevy asset port

## Status and regeneration

Ported runtime art is staged under `bevy/assets/`; Unity `Assets/` and editable `.blend` files under `ArtSource/` remain the sources of truth and are not modified by this pipeline. The checked-in `assets/models/player.glb` character sandbox asset is preserved: the Unity player FBX is exported separately as `models/Models/Player/Player.glb`.

Use Blender 5.2+ (tested with 5.2.2) and Python 3.11+ from the repository root:

```powershell
python bevy/tools/convert_assets.py --blender "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
```

The script copies runtime images and WAVs and invokes Blender CLI once per FBX to import and export binary glTF (`.glb`), embedding available image textures. Pass `--copy-only` to refresh only copied images/audio. Repeated runs overwrite generated paths, do not delete user files, and never write into Unity source. Set `--project` to a different source checkout if needed. Export warnings (for example >4 joint influences) should be reviewed if animation/skin fidelity is critical.

## Asset locations and Unity content mapping

The source-relative subtrees are retained to make lookup and future replacement straightforward.

| Bevy path | Unity source / intended presentation use |
|---|---|
| `assets/models/World/Models/WorldArt.glb` | `Assets/Art/World/Models/WorldArt.fbx`; one GLB with named mesh objects for roads/crosswalks/junctions, rail and crossings, bridges, houses, apartments, offices, restaurants, factories, barn, station, signs, lights, foundations and trees. The mesh names match the source catalog (e.g. `Factory_a`, `Road_Local`, `Tree_Broadleaf_a`). |
| `assets/models/Vehicles/Models/Vehicles.glb` | `Assets/Art/Vehicles/Models/Vehicles.fbx`; named box truck, bus, hatchback, pickup, sedan, taxi and van meshes. Box truck is the delivery truck presentation; others support town traffic. |
| `assets/models/Models/Belt/*.glb` | Straight and left/right 90-degree conveyor belt pieces; `assets/Art/Models/Belt/Conveyor_Tread_BaseColor.png` is the tread texture. |
| `assets/models/Models/Oven/FF_Oven.glb`, `assets/models/Kitchen/Kitchen.glb` | Existing oven equipment, and kitchen equipment/set meshes respectively. The oven model is equipment presentation, not cooking logic. |
| `assets/models/Characters/*.glb` | Employee and two pedestrian character models (`Employee.fbx`, `Pedestrian_A/B.fbx`); use the pedestrians for world NPC presentation and employee for staffed production visuals. |
| `assets/models/Restaurant/**/*.glb` | 59 modular restaurant interior models: architecture, surfaces, furniture, decor, lights (meshes only), and service. Refer to names (`RT_Wall_*`, `RT_Floor_*`, `RT_Table_*`, `RT_Chair_*`, `RT_Light_*`, `RT_Sink_*`) and source `ArtSource/Restaurant/manifest.json` / README for dimensions and placement. Existing oven, fridge, dock and counter equipment remain the gameplay equipment assets; these additions do not replace their behavior. |
| `assets/Art/World/Textures/` | World base-color/smoothness and `_Normal` maps for roads, rail, façades, roofs, grounds, fields, doors, foliage, water and signs. Map source names to `WG_*` material names in `Assets/Art/World/Materials`; material definitions themselves are Unity `.mat`, not ported. |
| `assets/Art/Icons/` | Food, recipe, equipment and restaurant furnishing sprites for UI. `Restaurant/` contains catalog icons corresponding to `RT_*` model names. |
| `assets/Art/Models/Player/Textures/`, `assets/Art/Models/Belt/` | Player palette/metallic textures and belt tread image. |
| `assets/Audio/Resources/Audio/*.wav` | UI (`ui_open`, `ui_click`), placement (`place`), production ambience/effects (`mixer`, `sizzle`, `hum`, `clank_*`), vehicles (`engine`, `honk_*`), footstep variants, environment (`city`, `farm`, `factory`, `bird_*`) and menu music / cash. |

Blender preserves FBX object names, hierarchy, mesh/material slots, UVs, transforms and supported animation data where the FBX importer/exporter supports it. Unity-specific prefab transforms, scripts, colliders, URP shader graphs, material assets, Animator/controller setup, gameplay and localization are not ported. The world GLB is one named-object catalog rather than a placed scene. Bevy code should choose objects and apply placement transforms from the game/domain scene data; source world conventions are metres, Blender Z-up, front toward -Y. Restaurant-specific dimensions and Unity coordinate conventions are recorded in `ArtSource/Restaurant/README.md` and `ArtSource/World/README.md`. Verify axis/scale and material appearance in Bevy before relying on them for a final scene.

## Inventory and verification

Conversion run with Blender 5.2.2 CLI generated 71 GLBs from the FBXs under `Assets/Art` and copied 155 image files and 27 WAVs. There are 72 total GLBs including the preserved sandbox `player.glb`. All 72 outputs were checked for a valid `glTF` binary header and non-trivial size; no LFS pointer text was found. The source art includes 59 restaurant models, the complete multi-object world art catalog, vehicles, conveyors, kitchen/oven, employee and pedestrians. The unrelated FishNet demo `Tree.fbx` outside `Assets/Art` is intentionally excluded.

Source-side art descriptions/credits are in the README files under `ArtSource/{Restaurant,World,Kitchen,Vehicles,Oven}`. This repository does not provide an explicit third-party license/attribution record for the runtime art/audio inspected for this port. No license is inferred or changed here; retain source repository provenance and resolve rights/attribution before external redistribution. Unity materials are authored assets rather than third-party license metadata and are not directly loadable by Bevy.
