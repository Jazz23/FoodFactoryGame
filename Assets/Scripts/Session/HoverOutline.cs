// Local presentation: draws an outline around anything the crosshair can open (employees, machines, the storage) by
// appending an inverted-hull outline material to each of its renderers while highlighted. Each part grows from its own
// mesh centre (_OutlineCenter), so flat-shaded parts get a closed hull. The renderers' materials are captured when the
// highlight turns on, so material changes made while it is off (belt treads, model tints) are kept.
using System;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Session
{
    [DisallowMultipleComponent]
    public sealed class HoverOutline : MonoBehaviour
    {
        private static readonly int OutlineCenter = Shader.PropertyToID("_OutlineCenter");

        [SerializeField] private Material outlineMaterial;

        private Renderer[] _renderers = Array.Empty<Renderer>();
        private Material[][] _plain = Array.Empty<Material[]>();
        private MaterialPropertyBlock _block;
        private bool _highlighted;

        // The target's outline, added on first use with the given material when it has none of its own.
        public static HoverOutline For(Component target, Material material)
        {
            var outline = target.GetComponent<HoverOutline>();
            if (outline != null) return outline;
            outline = target.gameObject.AddComponent<HoverOutline>();
            outline.outlineMaterial = material;
            return outline;
        }

        private void OnDisable() => SetHighlighted(false);

        public void SetHighlighted(bool highlighted)
        {
            if (highlighted == _highlighted || (highlighted && outlineMaterial == null)) return;
            _highlighted = highlighted;
            if (!highlighted)
            {
                for (var index = 0; index < _renderers.Length; index++)
                    if (_renderers[index] != null) _renderers[index].sharedMaterials = _plain[index];
                return;
            }
            _block ??= new MaterialPropertyBlock();
            _renderers = GetComponentsInChildren<Renderer>().Where(x => x is MeshRenderer or SkinnedMeshRenderer).ToArray();
            _plain = _renderers.Select(x => x.sharedMaterials).ToArray();
            foreach (var renderer in _renderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                renderer.GetPropertyBlock(_block);
                _block.SetVector(OutlineCenter, mesh.bounds.center);
                renderer.SetPropertyBlock(_block);
                renderer.sharedMaterials = renderer.sharedMaterials.Append(outlineMaterial).ToArray();
            }
        }
    }
}
