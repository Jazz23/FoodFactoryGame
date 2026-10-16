// Build catalog icons for structure cards (walls, doors, back doors, windows and wall finishes). Equipment cards carry icons
// rendered ahead of time by AgentScripts/BuildRestaurantContent.cs, but structure styles have no equipment definition, so their
// cards had none. These are rendered once per tool and style at runtime from the same art-kit models the shell is drawn with
// (RestaurantStyleCatalog), in the same three-quarter view as the authored icons, and cached for the rest of the run.
// Presentation only; without a graphics device (batch mode) or a model there is no icon and the card shows its empty frame.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FoodFactoryGame.Session.Buildings
{
    public static class StructureIcons
    {
        private const int Size = 128;
        // An unnamed layer only the icon camera draws; the staged model also sits far below the world and lives for one render.
        private const int IconLayer = 31;
        private static readonly Vector3 StagePosition = new(0f, -5000f, 0f);
        // Matches RestaurantShellModel: a window's glazed unit sits on the sill; a single door's leaf hangs from its hinge.
        private const float SillHeight = 0.85f;
        private const float SingleHinge = 0.475f;

        private static readonly Dictionary<string, Sprite> Cache = new();

        // The icon for a structure card, or null when it cannot be drawn.
        public static Sprite Get(RestaurantStyleCatalog catalog, BuildTool tool, string style)
        {
            if (catalog == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            var key = $"{tool}:{style}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var root = new GameObject($"Structure icon {key}");
            root.transform.position = StagePosition;
            Sprite sprite = null;
            try
            {
                if (!Stage(root.transform, catalog, tool, style)) return null;
                sprite = Render(root, key);
            }
            finally
            {
                root.SetActive(false);
                Object.Destroy(root);
            }
            if (sprite != null) Cache[key] = sprite;
            return sprite;
        }

        // Places the models a card stands for under the root: a wall's 1 m piece, a finish on a 2 m piece, a doorway with its frame
        // and leaf, or a window's bay with its glazed unit. False when the style has no model.
        private static bool Stage(Transform root, RestaurantStyleCatalog catalog, BuildTool tool, string style)
        {
            switch (tool)
            {
                case BuildTool.Wall:
                    return Place(catalog.Finish(style)?.wall1m, root, Vector3.zero) != null;
                case BuildTool.Finish:
                    return Place(catalog.Finish(style)?.wall2m, root, Vector3.zero) != null;
                case BuildTool.Door:
                case BuildTool.BackDoor:
                {
                    var leaf = Place(catalog.Leaf(style)?.leaf, root, new Vector3(SingleHinge, 0.005f, 0f));
                    Place(catalog.SingleDoorway, root, Vector3.zero);
                    Place(catalog.SingleFrame, root, Vector3.zero);
                    return leaf != null;
                }
                case BuildTool.Window:
                {
                    var window = catalog.Window(style);
                    var bay = Place(window?.bay, root, Vector3.zero);
                    var glazing = Place(window?.window, root, Vector3.up * SillHeight);
                    return bay != null || glazing != null;
                }
                default:
                    return false;
            }
        }

        private static GameObject Place(GameObject prefab, Transform parent, Vector3 position)
        {
            if (prefab == null) return null;
            var instance = Object.Instantiate(prefab, parent, false);
            instance.transform.localPosition = position;
            return instance;
        }

        // A 128 px three-quarter view of everything under the root on a transparent background.
        private static Sprite Render(GameObject root, string name)
        {
            foreach (var light in root.GetComponentsInChildren<Light>()) light.enabled = false;
            foreach (var part in root.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = IconLayer;
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return null;
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);

            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var rig = new GameObject("Structure icon camera");
            rig.transform.SetParent(root.transform, false);
            try
            {
                var camera = rig.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << IconLayer;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(bounds.extents.magnitude, 0.1f) * 1.05f;
                camera.transform.position = bounds.center + new Vector3(-1f, 0.9f, 1.2f).normalized * 10f;
                camera.transform.LookAt(bounds.center);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 30f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.targetTexture = texture;
                // Key and fill lights like the authored icons'; they exist only for this render.
                for (var index = 0; index < 2; index++)
                {
                    var lightObject = new GameObject("Structure icon light");
                    lightObject.transform.SetParent(root.transform, false);
                    var light = lightObject.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.shadows = LightShadows.None;
                    light.cullingMask = 1 << IconLayer;
                    light.intensity = index == 0 ? 1.4f : 0.7f;
                    light.color = index == 0 ? new Color(1f, 0.93f, 0.84f) : new Color(0.82f, 0.9f, 1f);
                    light.transform.rotation = Quaternion.Euler(index == 0 ? 50f : 25f, index == 0 ? 130f : -60f, 0f);
                }
                var request = new RenderPipeline.StandardRequest { destination = texture };
                if (GraphicsSettings.currentRenderPipeline != null && RenderPipeline.SupportsRenderRequest(camera, request))
                    RenderPipeline.SubmitRenderRequest(camera, request);
                else camera.Render();
                RenderTexture.active = texture;
                var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = $"Structure icon {name}" };
                image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                image.Apply(false, true);
                camera.targetTexture = null;
                return Sprite.Create(image, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            }
            finally
            {
                RenderTexture.active = previous;
                texture.Release();
                Object.Destroy(texture);
            }
        }
    }
}
