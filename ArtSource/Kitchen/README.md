# Kitchen stations — mixer, stove, fryer, grill and kitchen counter

Low-poly, flat-coloured station models for decision 0043, made by one Blender script so they can be regenerated and changed in
code: `build_kitchen_models.py` builds every station with bmesh into `Kitchen.blend`, renders `Kitchen_Preview.png` (workbench)
and exports `Export/Kitchen.fbx` (one object per station; Unity reads meshes by object name).

Run it headless from the project root (Blender 5.2):

```bash
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python ArtSource/Kitchen/build_kitchen_models.py
```

Then run `AgentScripts/BuildKitchenContent.cs` in Unity (see `docs/development.md`): it copies the export to
`Assets/Art/Kitchen/Models/Kitchen.fbx`, remaps each `KT_*` material to a URP Lit material in `Assets/Art/Kitchen/Materials`,
makes a prefab per station in `Assets/Prefabs/Equipment` (one box collider on the root) and installs the content in the scenes.

## Stations (triangles)

| Object | Tris | Footprint | Notes |
|---|---|---|---|
| `Kitchen_Mixer` | 476 | 1 x 1 m, 1.52 m tall | stand cabinet with a teal planetary mixer, bowl of dough, flour sack |
| `Kitchen_Stove` | 832 | 2 x 1 m | four burners with flame rings, a sauce pot and a pan, two oven doors, knob row |
| `Kitchen_Fryer` | 380 | 1 x 1 m | two golden oil wells, one basket down, one hanging, control panel |
| `Kitchen_Grill` | 660 | 2 x 1 m | flat-top griddle with patties and cheese, heat glow, grease trough, hood |
| `Kitchen_Counter` | 896 | 2 x 1 m | wood-topped prep counter with teal cupboards, cutting board, knife, vegetables, plates, a burger, ticket rail |

## Conventions

As `ArtSource/Vehicles`: metres, Z up, the working front faces -Y (imports as Unity +Z), width along X, pivot on the floor at the
centre of the footprint, every model inside its whole-cell footprint. Materials map one-to-one to Unity: `KT_Steel`,
`KT_DarkSteel`, `KT_Black`, `KT_Enamel` (teal), `KT_Cream`, `KT_Accent` (orange knobs), `KT_Wood`, `KT_Board`, `KT_Glass`,
`KT_Oil`, `KT_Food`, `KT_Tomato`, `KT_Leaf`, and the emissive `KT_Flame`. Nothing is animated.
