// Local presentation: draws an outline around anything the crosshair can open (employees, machines, the storage) while it is
// highlighted, over every renderer under the highlighted root. A mesh renderer gets a child "shell" renderer that redraws the
// same mesh with the outline material on every submesh: an extra material appended to a renderer only draws its LAST submesh,
// so a one-mesh model with several materials (the stove) used to outline just one part (its pot). The shells leave the
// model's own materials untouched; they are hidden and destroyed when the highlight ends. Skinned meshes (employees)
// keep the appended material, captured and restored around the highlight. Each part grows from its own mesh centre
// (_OutlineCenter), so flat-shaded parts get a closed hull. On shells the target's own outline is drawn in a muted hover green;
// an override material (the pick outlines) keeps its colour. An optional tint material (the see-through pick tint) is drawn too.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace FoodFactoryGame.Session
{
    [DisallowMultipleComponent]
    public sealed class HoverOutline : MonoBehaviour
    {
        private static readonly int OutlineCenter = Shader.PropertyToID("_OutlineCenter");
        private static readonly int OutlineColor = Shader.PropertyToID("_Color");
        private const string ShellName = "HoverOutlineShell";

        // The plain hover outline's colour: a muted green that reads on wood, steel and tile without glowing.
        public static readonly Color HoverColor = new(0.33f, 0.62f, 0.38f, 1f);

        [SerializeField] private Material outlineMaterial;

        private readonly List<GameObject> _shells = new();
        private Renderer[] _appended = Array.Empty<Renderer>();
        private Material[][] _plain = Array.Empty<Material[]>();
        private MaterialPropertyBlock _block;
        private bool _highlighted;
        private Material _shown;
        private Material _shownTint;

        // The target's outline, added on first use with the given material when it has none of its own.
        public static HoverOutline For(Component target, Material material)
        {
            var outline = target.GetComponent<HoverOutline>();
            if (outline != null) return outline;
            outline = target.gameObject.AddComponent<HoverOutline>();
            outline.outlineMaterial = material;
            return outline;
        }

        // True for a shell this component added (never outlined itself).
        public static bool IsShell(Component component) => component != null && component.gameObject.name == ShellName;

        private void OnDisable() => SetHighlighted(false);

        // Material overrides the target's own outline (the red one used while picking a world position); tint, when given, is
        // drawn too.
        public void SetHighlighted(bool highlighted, Material material = null, Material tint = null)
        {
            material = material != null ? material : outlineMaterial;
            if (highlighted && _highlighted && (material != _shown || tint != _shownTint)) SetHighlighted(false);
            if (highlighted == _highlighted || (highlighted && material == null)) return;
            _highlighted = highlighted;
            _shown = highlighted ? material : null;
            _shownTint = highlighted ? tint : null;
            if (!highlighted)
            {
                RemoveOutline();
                return;
            }
            _block ??= new MaterialPropertyBlock();
            var color = material == outlineMaterial ? HoverColor : (Color?)null;
            var appended = new List<Renderer>();
            // Shells switched off just now (destroyed at the end of the frame) are skipped by name.
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (IsShell(renderer) || !renderer.enabled || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null) continue;
                    Append(skinned, skinned.sharedMesh, material, tint);
                    appended.Add(skinned);
                    continue;
                }
                if (renderer is not MeshRenderer) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                // A statically batched renderer draws a range of a combined mesh; a shell would draw all of it.
                if (renderer.isPartOfStaticBatch)
                {
                    Append(renderer, mesh, material, tint);
                    appended.Add(renderer);
                    continue;
                }
                AddShell(renderer, mesh, material, color);
                if (tint != null) AddShell(renderer, mesh, tint, null);
            }
            _appended = appended.ToArray();
        }

        private void RemoveOutline()
        {
            foreach (var shell in _shells)
            {
                if (shell == null) continue;
                // Not SetActive: this also runs from OnDisable while the target's hierarchy is being deactivated.
                shell.GetComponent<MeshRenderer>().enabled = false;
                Destroy(shell);
            }
            _shells.Clear();
            for (var index = 0; index < _appended.Length; index++)
                if (_appended[index] != null) _appended[index].sharedMaterials = _plain[index];
            _appended = Array.Empty<Renderer>();
            _plain = Array.Empty<Material[]>();
        }

        // A child renderer drawing the part's mesh again with the material on every submesh.
        private void AddShell(Renderer source, Mesh mesh, Material material, Color? color)
        {
            var shell = new GameObject(ShellName) { layer = source.gameObject.layer };
            shell.transform.SetParent(source.transform, false);
            shell.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = shell.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = Enumerable.Repeat(material, Math.Max(1, mesh.subMeshCount)).ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _block.Clear();
            _block.SetVector(OutlineCenter, mesh.bounds.center);
            if (color is { } tint) _block.SetColor(OutlineColor, tint);
            renderer.SetPropertyBlock(_block);
            _shells.Add(shell);
        }

        // The old path for skinned and statically batched parts: the material appended (it draws the last submesh). No colour
        // override here: the block is the part's own, and _Color may be one of its own materials' properties.
        private void Append(Renderer renderer, Mesh mesh, Material material, Material tint)
        {
            _plain = _plain.Append(renderer.sharedMaterials).ToArray();
            renderer.GetPropertyBlock(_block);
            _block.SetVector(OutlineCenter, mesh.bounds.center);
            renderer.SetPropertyBlock(_block);
            var materials = renderer.sharedMaterials.Append(material);
            renderer.sharedMaterials = (tint != null ? materials.Append(tint) : materials).ToArray();
        }
    }
}
