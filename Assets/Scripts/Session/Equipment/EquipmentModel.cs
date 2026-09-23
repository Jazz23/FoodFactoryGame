// Builds presentation copies of an equipment kind's visual prefab, centred on the footprint and standing on the floor
// whatever the asset's pivot. Placed visuals and the placement ghost share this, so the ghost shows exactly where and how
// the piece will stand.
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace FoodFactoryGame.Session.Equipment
{
    public static class EquipmentModel
    {
        public static GameObject Create(EquipmentDefinition definition, Transform root)
        {
            var model = Object.Instantiate(definition.VisualPrefab, root, false);
            Center(root, model);
            return model;
        }

        // A see-through copy: no behaviours (it never runs or glows), colliders (the aim ray and movement pass through it) or
        // lights, and every mesh drawn with the ghost material. The copy is built inactive so no behaviour ever wakes up.
        public static GameObject CreateGhost(EquipmentDefinition definition, Transform parent, Material material)
        {
            var root = new GameObject($"Ghost {definition.Kind}");
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            var model = Object.Instantiate(definition.VisualPrefab, root.transform, false);
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var light in model.GetComponentsInChildren<Light>(true)) light.enabled = false;
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers.Where(IsMesh))
            {
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            root.SetActive(true);
            // Centre with the same renderers as a placed visual, then hide anything that is not a mesh (glows, particles).
            Center(root.transform, model);
            foreach (var renderer in renderers.Where(x => !IsMesh(x))) renderer.enabled = false;
            return root;
        }

        private static bool IsMesh(Renderer renderer) => renderer is MeshRenderer || renderer is SkinnedMeshRenderer;

        private static void Center(Transform root, GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var item in renderers.Skip(1)) bounds.Encapsulate(item.bounds);
            var center = root.InverseTransformPoint(bounds.center);
            var floor = root.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)).y;
            model.transform.localPosition -= new Vector3(center.x, floor, center.z);
        }
    }
}
