# World art — roads, rail, buildings, farms, station

Low-poly, textured art for the generated world (decision 0026 presentation). Everything is produced by two Blender scripts,
so it can be regenerated and changed in code.

- `build_world_textures.py`: seamless procedural textures (numpy) in `Textures/`: base maps (RGBA, smoothness in alpha) and
  `*_Normal.png` tangent-space normal maps. Roads carry sidewalks, curbs, lane markings and crosswalks; façades are one window
  bay by one storey (brick, siding, plaster, apartment panels, office curtain wall, storefront); plus corrugated metal,
  industrial windows, roller/wood/glass doors, barn planks, shingles, clay tiles, flat roof, concrete, grass, paving, yard,
  wheat and green crop fields, and rail ballast with sleepers.
- `build_world_models.py`: builds every asset with bmesh into `World_Assets.blend` and exports `Export/WorldArt.fbx` (one
  object per asset). UVs are world-scaled per material; façade walls snap to whole window bays and storeys.

Run both in Blender 5.2 (the models script starts a fresh file):

```python
exec(open(r'G:/Unity/FoodFactoryGame/ArtSource/World/build_world_textures.py').read())
exec(open(r'G:/Unity/FoodFactoryGame/ArtSource/World/build_world_models.py').read())
```

Then copy `Textures/*.png` to `Assets/Art/World/Textures/` and `Export/WorldArt.fbx` to `Assets/Art/World/Models/`, and run
the body of `AgentScripts/BuildWorldArt.cs` through the Unity MCP `execute_code` tool (import settings, URP Lit materials in
`Assets/Art/World/Materials`, material remap, `Assets/Art/World/WorldArtCatalog.asset`, and the reference on WorldGen's
presenter). Mesh references survive a re-export as long as object names stay the same.

## Assets (triangles)

| Asset | Tris | Notes |
|---|---|---|
| `Road_Arterial`, `Road_Local` (+ `_Crosswalk`) | 14 | 10 m tiles along +Y; 14 m / 10 m wide with raised sidewalks and curbs |
| `Road_Rural` | 6 | 8 m wide, gravel shoulders |
| `Road_Junction` | 2 | unit quad, scaled to each crossing |
| `Rail_Track` | 18 | 6 m tile: ballast, sleepers (texture), two rails |
| `Ground_Quad` | 2 | unit quad for ground, lots and fields |
| `House_Small_a/b/c` | 52 | 10 x 9 m, gable roof, chimney, porch step |
| `House_Large_a/b/c` | 74 | 15 x 12 m, two storeys, hip roof, porch, garage |
| `Apartment_a/b` `_Ground/_Middle/_Roof` | 28 / 18 / 52 | 18 x 15 m modules, 3 m storeys, stacked by floors |
| `Office_a/b` `_Ground/_Middle/_Roof` | 20 / 8 / 52 | 22 x 19 m modules, 3.5 m storeys |
| `Restaurant_a/b` | 76 | 12 x 11 m storefront; `WG_Awning` is tinted per owner in Unity |
| `Factory_a/b` | 84 / 134 | 30 x 26 m; pitched or flat roof, roller doors, window bands |
| `Barn_a/b` | 108 | gambrel barn with silo, placed at authored size |
| `Station` | 124 | 10 x 30 m platform, ramp, shelter, benches, sign |

## Conventions

Metres, Z up. Building fronts (doors) face -Y, which imports as Unity +Z; X runs along the street; pivot at the footprint
centre on the ground. Tiles run along +Y from 0 (Unity -Z). Materials are `WG_*` and map one-to-one to Unity materials.
Unity mirrors X on import, which only flips textures left-to-right (all assets are symmetric enough for that).
`World_Assets_Preview.png` is a Blender render of the set. Poly counts are deliberately tiny; detail is in the textures.
