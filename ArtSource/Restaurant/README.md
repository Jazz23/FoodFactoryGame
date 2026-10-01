# Restaurant model kit

59 original, editable Blender models, with individual Unity FBXs and 16 shared URP Lit materials.
Warm cream, teal, walnut, oak, terracotta, and brass; beveled stylized geometry suitable for close indoor views.

**Delivered art only.** The restaurant-building features described in decision 0034 remain unimplemented by this delivery.
Models have no colliders, gameplay components, equipment definitions, prices, seats, ambience values, network registration,
or actual Light components. The Blender display scenes are art previews.

![Assembled Blender preview](Restaurant_Vignette.png)

## Locations

- Editable source: `ArtSource/Restaurant/Restaurant_Kit.blend` (Blender 5.2.2 LTS).
- Drag-and-drop model assets: `Assets/Art/Restaurant/Models/{Architecture,Surfaces,Furniture,Decor,Lighting,Service}/`.
- Materials: `Assets/Art/Restaurant/Materials/RT_*.mat`.
- `manifest.json`: exact names, relative FBX paths, dimensions, triangles, material-slot order, pivot notes.
- `Sheet_*.png`: labeled contact sheets of every model.
- `Unity_Vignette.png`, `Unity_Opening_Fit.png`: actual Unity Editor renders of imported models in disposable preview scenes.
- `unity-validation.json`: final authoring checks, including dimensions, triangle counts, UVs, material-slot mapping, and floor contact.

## Inventory

| Category | Count | Contents |
|---|---:|---|
| Architecture | 26 | Plaster, exposed brick, teal wainscot, kitchen tile walls in 1 m / 2 m lengths; four matching end/junction posts; half-height and slatted partitions; single/double doorway openings and frames; panel/glazed/kitchen hinged leaves; window opening and picture/mullioned windows; serving hatch; cornice |
| Surfaces | 5 | Repeating checker, terracotta, oak-plank, kitchen-tile floor modules; ceiling panel |
| Furniture | 8 | Bistro round, communal, high round tables; spindle, upholstered, bar-stool seats; upholstered banquette; slatted bench |
| Decor | 12 | Floor/table plants, divider planter, framed abstract art, wall/A-frame menu boards, clock, coat stand, waste bin, tabletop caddy, ceramic vase, bordered rug |
| Lighting | 4 | Pendant, sconce, floor lamp, ceiling strip meshes |
| Service | 4 | Prep table, open-basin sink, storage shelf, two-shelf trolley |

105,141 triangles total across the 59 unique assets; the largest is the 2 m modeled-brick wall (14,796 triangles).
Repeated instances do not require duplicate source meshes. This is not a runtime performance certification.

## Existing-asset audit / reuse

Inspected pending tracked changes and the three untracked restaurant design documents before authoring. Searched the
project's imported meshes and equipment prefabs, and inspected `ArtSource/World/build_world_models.py` and its inventory.

| Existing asset | Reuse decision |
|---|---|
| `Assets/Prefabs/Equipment/Oven.prefab`, `Assets/Art/Models/Oven/FF_Oven.fbx` | Existing Blender oven; no duplicate |
| `Assets/Prefabs/Equipment/Fridge.prefab` | Existing refrigerator; no duplicate |
| `Assets/Prefabs/Equipment/Dock.prefab` | Existing dock, deck, bumper, crate and door; no duplicate |
| `Assets/Prefabs/Equipment/Counter.prefab` | Existing counter with Register and Screen geometry; no duplicate register/counter |
| `Assets/Prefabs/Equipment/Table.prefab` | Existing square four-seat set; retained. New standalone round/communal/high tables and distinct seats supplement it |
| `Assets/Art/World/Models/WorldArt.fbx` | Existing complete restaurant exteriors, roofs/awnings, scenery, outdoor trees, roads, foundations; no duplicate full buildings |

The existing full exterior shells are not modular interior walls with real openings. The new wall/door/window components
fill that specific gap. Kitchen furniture here is unpowered set dressing rather than replacement cooking equipment.

## Placement conventions (authoring dimensions, not gameplay rules)

- Metres; FBXs import at scale 1. Blender Z-up / front -Y becomes Unity Y-up / front +Z.
  Blender X maps to Unity -X with this export convention (as with the existing world art).
- Ground objects have their pivot at floor height, centered in footprint. Tabletop props sit on their local zero plane.
  Seat heights: dining approximately 0.46 m; stool 0.75 m. Table tops: dining 0.75 m; high table 1.05 m.
- Walls are 3 m high, 1 m / 2 m wide; main body 0.18 m thick, trim projects beyond it.
  Wall pivots lie at the base centerline; straight pieces meet at their half-width endpoints. Rotate 90 degrees for corners
  and use the end/junction post to cover junctions. Mesh thickness does **not** decide cell-versus-edge occupancy.
- Doorway single: 2 m wall bay / 1.04 m opening; double: 3 m bay / 2.04 m opening. Both openings are 2.24 m high.
  Place matching frames at the same origin; trim overlaps the surrounding wall slightly.
- Door leaves are 0.95 m wide x 2.18 m high, hinge origin at Blender X=0, floor height, with geometry extending +X.
  For a centered single frame, Blender leaf origin is X=-0.475, Z=0.005 (Unity X=+0.475, Y=0.005).
  Reverse/rotate the leaf around its hinge for an opposite opening. `opening-layout.json` demonstrates paired double leaves
  and an open leaf without adding animation or behavior.
- Window opening: 1.6 m wide, sill 0.85 m, head 2.5 m. Place either 1.6 x 1.65 m window at Z=0.85 in Blender / Y=0.85 in Unity.
  Frame sill overlaps the wall. Glass is a separate material slot, transparent, double-sided, and does not cast shadows.
- Floor modules repeat on 1 m centers. Their top is zero and thickness extends to -0.07 m; floor substrates are recessed
  below the finish so there are no coincident visible surfaces. Ceiling panel extends above its zero plane.
- Cornice, wall decor and sconces attach at their rear plane, facing forward. Pendant/ceiling-strip pivots are at the ceiling
  and their geometry hangs below zero. There is no generated light source inside lighting models.
- UV0 is metre-projected, intentionally overlapping for flat-color materials. Unity generates non-overlapping UV1 for lightmaps.
  Every model is explicitly triangulated, with collapsed bevel slivers removed before export. No external textures are needed.

## Regeneration

Use a fresh Blender file. The generator adds a new `Restaurant Kit` scene and preserves existing scenes; it refuses to
regenerate into an existing scene with that name. Save any unrelated work separately before generating the deliverable.

```python
RESTAURANT_ROOT = r'G:/Unity/FoodFactoryGame/ArtSource/Restaurant'
g = {'RESTAURANT_ROOT': RESTAURANT_ROOT}
for name in ['build_restaurant.py', 'build_previews.py', 'build_contact_sheets.py', 'build_opening_preview.py']:
    exec(compile(open(RESTAURANT_ROOT + '/' + name, encoding='utf-8').read(), name, 'exec'), g)
```

Then execute the `Run()` body of `AgentScripts/ImportRestaurantArt.cs` through Unity MCP `execute_code`. This imports the
FBXs, makes/remaps shared URP materials, and preserves existing Unity GUIDs. Execute `ValidateRestaurantArt.cs` for the
authoring report and `PreviewRestaurantArt.cs` for the Unity vignette. These files are outside `Assets` and do not compile
into the game. The scripts use the project's established in-memory CodeDom workflow.

For the opening-fit Unity capture, use `opening-layout.json`, output `Unity_Opening_Fit.png`, camera position
`(-11, 11, 22)`, look-at `(-7.5, 1, 0)`, and orthographic size `7.5` in the preview body.

Keep the `.blend`, generator scripts, FBXs, materials, and Unity `.meta` files together in Git. `.blend1` is an ignored backup.
