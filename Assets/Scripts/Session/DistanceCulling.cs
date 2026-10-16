// Presentation only: stops drawing a visual once it is small on screen (far away), with one Unity LOD level whose cull height
// scales with the visual's size, so a truck stays visible further than a chair. Culling hides renderers only; the server's
// simulation (machines running, trucks driving, customers eating) never depends on a visual, so nothing stops working.
// ViewCulling answers whether a point can be seen by any of this client's on-screen cameras (frustum plus that same size
// cut-off), so ambient presenters can appear and vanish only out of sight and skip drawing what nobody can see.
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

    // Presentation only. The cameras are read once per frame (from their last pose, so callers pad by a margin that grows with
    // distance). The views are the local player's camera and any other enabled camera drawing to the screen (one rendering into
    // a texture, such as the icon camera, is not a view). With no camera nothing is seen.
    public static class ViewCulling
    {
        // Sideways padding of the frustum: a fixed amount plus a share of the distance, so a camera turning or moving between
        // the test and the frame being drawn does not reveal something hidden.
        private const float Margin = 2f;
        private const float AngularMargin = 0.12f;
        // Something smaller on screen than this share of the LOD cut-off counts as unseen (it is not drawn, or nearly so).
        private const float SizeSlack = 0.8f;
        private const int MaxViews = 4;

        private static readonly Plane[][] Planes = { new Plane[6], new Plane[6], new Plane[6], new Plane[6] };
        private static readonly Vector3[] Eyes = new Vector3[MaxViews];
        // Per view: screen-height fraction per metre of size at one metre away (0 for an orthographic camera).
        private static readonly float[] SizeScale = new float[MaxViews];
        private static readonly float[] Far = new float[MaxViews];
        private static Camera[] _cameras = new Camera[8];
        private static int _views;
        private static int _frame = -1;

        // Whether a sphere (centre and radius in scene metres) of an object `size` metres across can be seen by any view this
        // frame: inside a padded frustum and not below the LOD cut-off for its size (size 0 skips that test).
        public static bool Seen(Vector3 centre, float radius, float size)
        {
            Refresh();
            for (var v = 0; v < _views; v++)
            {
                var distance = Vector3.Distance(Eyes[v], centre);
                if (distance - radius > Far[v]) continue;
                if (size > 0f && SizeScale[v] > 0f && size * SizeScale[v] / Mathf.Max(distance, 0.01f) < DistanceCulling.Pieces * SizeSlack)
                    continue;
                var pad = radius + Margin + AngularMargin * distance;
                var planes = Planes[v];
                var inside = true;
                for (var p = 0; p < 6 && inside; p++)
                    if (planes[p].GetDistanceToPoint(centre) < -pad) inside = false;
                if (inside) return true;
            }
            return false;
        }

        // Distance in scene metres from a point to the nearest view's camera; infinity with none.
        public static float NearestView(Vector3 point)
        {
            Refresh();
            var nearest = float.PositiveInfinity;
            for (var v = 0; v < _views; v++) nearest = Mathf.Min(nearest, Vector3.Distance(Eyes[v], point));
            return nearest;
        }

        private static void Refresh()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _views = 0;
            if (_cameras.Length < Camera.allCamerasCount) _cameras = new Camera[Camera.allCamerasCount * 2];
            var count = Camera.GetAllCameras(_cameras);
            // The local player's view always counts, even if it renders through a texture.
            var main = Belts.BeltPresenter.ViewCamera();
            if (main != null) Add(main);
            for (var i = 0; i < count; i++)
            {
                var camera = _cameras[i];
                _cameras[i] = null;
                if (_views < MaxViews && camera != null && camera != main && camera.isActiveAndEnabled && camera.targetTexture == null) Add(camera);
            }
        }

        private static void Add(Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, Planes[_views]);
            Eyes[_views] = camera.transform.position;
            Far[_views] = camera.farClipPlane;
            SizeScale[_views] = camera.orthographic
                ? 0f
                : QualitySettings.lodBias / (2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            _views++;
        }
    }
}
