# Road vehicles — the player's truck and city cars

Low-poly, flat-coloured vehicles for the generated world's roads (decision 0032, piece 4c), made by one Blender script so they
can be regenerated and changed in code: `build_vehicle_models.py` builds every vehicle with bmesh into `Vehicles.blend` and
exports `Export/Vehicles.fbx` (one object per vehicle; Unity reads meshes by object name). `Vehicles_Preview.png` is a
workbench render of the line-up.

Run it headless from the project root (Blender 5.2):

```bash
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python ArtSource/Vehicles/build_vehicle_models.py
```

or inside a running Blender with `VEHICLE_ART_ROOT` set to this folder (the script only empties the open file's objects,
meshes and materials; it does not reset preferences or add-ons). Then run `AgentScripts/BuildVehicleArt.cs` in Unity (see
`docs/development.md`): it copies the export to `Assets/Art/Vehicles/Models/Vehicles.fbx`, remaps each `VH_*` material to a
URP Lit material in `Assets/Art/Vehicles/Materials`, makes the prefabs in `Assets/Prefabs/Vehicles` and installs them in the
session scenes.

## Vehicles (triangles)

| Object | Tris | Notes |
|---|---|---|
| `Vehicle_BoxTruck` | 528 | 7.5 x 2.4 m, 3.35 m tall: painted cab, white cargo box, three axles; the player's truck |
| `Vehicle_Hatchback` | 364 | 3.9 x 1.75 m |
| `Vehicle_Sedan` | 364 | 4.6 x 1.8 m |
| `Vehicle_Van` | 364 | 5.0 x 1.95 m, 2.3 m tall |
| `Vehicle_Pickup` | 332 | 5.3 x 1.95 m, open bed |
| `Vehicle_Taxi` | 376 | the sedan in taxi yellow with a roof sign |
| `Vehicle_Bus` | 344 | 11.5 x 2.5 m city bus |

## Conventions

As `ArtSource/World`: metres, Z up, the vehicle's front faces -Y (imports as Unity +Z), length along Y, pivot on the ground at
the centre of the vehicle. Materials: `VH_Body` is the paint and stays white, because Unity tints it per car
(`CityTrafficPresenter`) or per company (`TruckPresenter`) through `_BaseColor`; `VH_Taxi` and `VH_Bus` are fixed liveries;
`VH_Glass`, `VH_Tyre`, `VH_Trim`, `VH_Chrome`, `VH_Box`, `VH_Sign`, and the emissive `VH_Headlight` and `VH_Taillight`. Wheels
are part of the body mesh (they do not turn).
