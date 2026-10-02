// Builds presentation copies of an equipment kind's visual prefab, centred on the footprint and standing on the floor
// whatever the asset's pivot. Placed visuals and the placement ghost share this, so the ghost shows exactly where and how
// the piece will stand. Belt ghosts reuse the ghost conversion on their already tile-centred prefabs. Decor mounts (decision 0034)
// shift the centred model further: a floor finish sinks flush, a wall piece puts its back on the root's plane facing +Z, and
// a ceiling piece hangs its top at the root; MountOffset lifts or pushes the root to its wall face or the ceiling.
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FoodFactoryGame.Session.Equipment
{
    public static class EquipmentModel
    {
        // Distance from a wall cell's centreline to the face wall decor hangs on (kit walls are 0.18-0.30 m thick with trim).
        public const float WallFace = 0.16f;
        // A flush floor finish's top stands this far above the floor, over the shell's floor tint.
        private const float FlushLift = 0.015f;

        public static GameObject Create(EquipmentDefinition definition, Transform root)
        {
            var model = Object.Instantiate(definition.VisualPrefab, root, false);
            Center(root, model);
            Mount(root, model, definition.Mount);
            return model;
        }

        public static GameObject CreateGhost(EquipmentDefinition definition, Transform parent, Material material)
        {
            var ghost = CreateGhost(definition.VisualPrefab, $"Ghost {definition.Kind}", parent, material, true);
            if (ghost.transform.childCount > 0) Mount(ghost.transform, ghost.transform.GetChild(0).gameObject, definition.Mount);
            return ghost;
        }

        // Where a mounted piece's root stands relative to its footprint's centre on the floor (tabletop height is the caller's).
        public static Vector3 MountOffset(EquipmentDefinition definition, Quaternion rotation) => definition.Mount switch
        {
            EquipmentMount.Wall => rotation * Vector3.forward * WallFace + Vector3.up * definition.MountHeight,
            EquipmentMount.Ceiling => Vector3.up * (SiteGridSpace.LevelHeight - 0.005f),
            _ => Vector3.zero
        };

        private static void Mount(Transform root, GameObject model, EquipmentMount mount)
        {
            if (mount is EquipmentMount.Floor or EquipmentMount.Tabletop) return;
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var item in renderers.Skip(1)) bounds.Encapsulate(item.bounds);
            var size = root.InverseTransformVector(bounds.size);
            model.transform.localPosition += mount switch
            {
                EquipmentMount.Flush => Vector3.down * (Mathf.Abs(size.y) - FlushLift),
                EquipmentMount.Wall => new Vector3(0f, -Mathf.Abs(size.y) * 0.5f, Mathf.Abs(size.z) * 0.5f),
                EquipmentMount.Ceiling => Vector3.down * Mathf.Abs(size.y),
                _ => Vector3.zero
            };
        }

        // A see-through copy: no behaviours (it never runs or glows), colliders (the aim ray and movement pass through it) or
        // lights, and every mesh drawn with the ghost material, except materials matching keep (if given), which get
        // kept instead. The copy is built inactive so no behaviour ever wakes up. center stands it on the floor centred on
        // its root, like a placed equipment visual.
        public static GameObject CreateGhost(GameObject prefab, string name, Transform parent, Material material, bool center,
            Func<Material, bool> keep = null, Material kept = null)
        {
            var root = new GameObject(name);
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            var model = Object.Instantiate(prefab, root.transform, false);
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var light in model.GetComponentsInChildren<Light>(true)) light.enabled = false;
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers.Where(IsMesh))
            {
                renderer.sharedMaterials = renderer.sharedMaterials
                    .Select(x => keep != null && kept != null && keep(x) ? kept : material).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            root.SetActive(true);
            // Centre with the same renderers as a placed visual, then hide anything that is not a mesh (glows, particles).
            if (center) Center(root.transform, model);
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
