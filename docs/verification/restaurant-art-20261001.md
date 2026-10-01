# Restaurant model kit — 2026-10-01

Scope: asset production only for the pending restaurant-building design. No gameplay implementation or scene/content-catalog integration.

## Evidence

- Blender 5.2.2 LTS source: `ArtSource/Restaurant/Restaurant_Kit.blend`.
- 59 FBXs and 16 shared materials in `Assets/Art/Restaurant/`.
- Authoring run: **restaurant-art-20261001-final**.
- Requested filter: **Assets/Art/Restaurant/Models/**/*.fbx**; matched **59**, failures **0**.
- Artifact: [unity-validation.json](../../ArtSource/Restaurant/unity-validation.json).
- Checks: model/renderer presence; nonempty meshes; surface/lightmap UVs; exact material slot names and URP asset paths;
  exact Blender/Unity triangle counts; matching dimensions within 1 mm per axis; furniture/service ground contact;
  absence of gameplay components/colliders; file/manifest count agreement.
- [Blender vignette](../../ArtSource/Restaurant/Restaurant_Vignette.png) and four labeled `Sheet_*.png` images show all assets.
- [Unity vignette](../../ArtSource/Restaurant/Unity_Vignette.png): 88 art-only instances rendered in a disposable Editor preview scene.
- [Unity opening fit](../../ArtSource/Restaurant/Unity_Opening_Fit.png): 55 instances; frames, three door styles, both glazed
  window types, and a double door with one leaf rotated at its hinge.

## Findings resolved

- Initial visual check found floor substrate faces coincident with finish faces. Recessed substrates, re-exported all four floors,
  and verified the checker pattern in Blender and Unity.
- Independent senior-dev review identified 2–4 cm foot clearance on some furniture/service models. Extended feet to the placement
  plane, preserving seat/worktop heights; all eight affected models now report minimum Unity Y=0.
- Stricter validation found Blender/FBX importer triangulation differences. Explicitly triangulated the export meshes and removed
  collapsed bevel slivers; final triangle counts agree for all 59 models (105,141 total).
- Independent reviewer inspected the final Unity vignette, opening fit and validation report and confirmed no remaining concrete art blocker.

The Editor console baseline contained existing obsolete-API warnings. Preview cleanup initially released a render texture
before closing its camera's preview scene, producing three tool-caused console errors. Cleanup now closes the scene first;
a repeated capture produced no additional errors. Material validation compares exact names as a set because FBX import can
reorder submeshes and their associated slots together.
Gameplay/PlayMode/multiplayer tests do not apply to this standalone art pack. The evidence is Editor art rendering, not a claim
that the planned restaurant-building feature is implemented or accepted in a running game.
