// Local presentation: draws an outline around an employee (hover feedback) by adding an inverted-hull outline material to
// each of its renderers. Each part grows from its own mesh centre (_OutlineCenter), so flat-shaded parts get a closed hull.
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Session.Employees
{
    [DisallowMultipleComponent]
    public sealed class EmployeeOutline : MonoBehaviour
    {
        private static readonly int OutlineCenter = Shader.PropertyToID("_OutlineCenter");

        [SerializeField] private Material outlineMaterial;

        private Renderer[] _renderers;
        private Material[][] _plain;
        private Material[][] _outlined;
        private bool _highlighted;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true).Where(x => x is MeshRenderer or SkinnedMeshRenderer).ToArray();
            _plain = _renderers.Select(x => x.sharedMaterials).ToArray();
            _outlined = _plain.Select(x => x.Append(outlineMaterial).ToArray()).ToArray();
            var block = new MaterialPropertyBlock();
            foreach (var renderer in _renderers)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetVector(OutlineCenter, mesh.bounds.center);
                renderer.SetPropertyBlock(block);
            }
        }

        public void SetHighlighted(bool highlighted)
        {
            if (highlighted == _highlighted) return;
            _highlighted = highlighted;
            for (var index = 0; index < _renderers.Length; index++)
                _renderers[index].sharedMaterials = highlighted ? _outlined[index] : _plain[index];
        }
    }
}
