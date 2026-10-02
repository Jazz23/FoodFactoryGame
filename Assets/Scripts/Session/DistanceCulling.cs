// Presentation only: stops drawing a visual once it is small on screen (far away), with one Unity LOD level whose cull height
// scales with the visual's size, so a truck stays visible further than a chair. Culling hides renderers only; the server's
// simulation (machines running, trucks driving, customers eating) never depends on a visual, so nothing stops working.
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Session
{
    public static class DistanceCulling
    {
        // Fraction of the screen's height below which a piece is not drawn: about 70 m for a 1 m machine at a 60 degree field of
        // view (QualitySettings.lodBias scales it).
        public const float Pieces = 0.012f;
        // Building shells are big and give the street its shape, so they stay a little longer.
        public const float Shells = 0.006f;

        // Adds (or refreshes) the LOD group on root over every renderer under it. Call again after adding renderers.
        public static void Apply(GameObject root, float screenFraction = Pieces)
        {
            if (root == null) return;
            var renderers = root.GetComponentsInChildren<Renderer>(true).Where(x => x is not ParticleSystemRenderer).ToArray();
            if (renderers.Length == 0) return;
            if (!root.TryGetComponent<LODGroup>(out var group)) group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(screenFraction, renderers) });
            group.RecalculateBounds();
        }
    }
}
