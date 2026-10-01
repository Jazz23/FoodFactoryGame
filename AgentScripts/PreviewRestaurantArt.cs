// Renders the Blender-authored display layout using imported FBXs in an isolated, disposable Unity preview scene.
// Execute the Run body through Unity MCP. Writes art evidence only; does not save or alter a game scene.
public static class PreviewRestaurantArt
{
    public static object Run()
    {
        var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var texture = new RenderTexture(1600, 1200, 24);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            var entries = Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText("ArtSource/Restaurant/vignette-layout.json"));
            foreach (var entry in entries)
            {
                var name = (string)entry["model"];
                var paths = UnityEditor.AssetDatabase.FindAssets(name + " t:Model", new[] { "Assets/Art/Restaurant/Models" });
                var path = paths.Select(UnityEditor.AssetDatabase.GUIDToAssetPath).Single(p => System.IO.Path.GetFileNameWithoutExtension(p) == name);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(asset, preview);
                var position = entry["position"];
                instance.transform.position = new Vector3(-(float)position[0], (float)position[2], -(float)position[1]);
                instance.transform.rotation = Quaternion.Euler(0f, -(float)entry["angle"], 0f);
            }
            var cameraObject = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags("Restaurant art preview camera", HideFlags.HideAndDontSave, typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, preview);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview;
            camera.transform.position = new Vector3(-12f, 13f, 13f);
            camera.transform.LookAt(new Vector3(-3.7f, 1f, -3f));
            camera.orthographic = true;
            camera.orthographicSize = 4.8f;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.28f, .3f, .31f);
            camera.targetTexture = texture;
            for (var i = 0; i < 2; i++)
            {
                var lightObject = UnityEditor.EditorUtility.CreateGameObjectWithHideFlags("Art studio light", HideFlags.HideAndDontSave, typeof(Light));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, preview);
                var light = lightObject.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = i == 0 ? 1.3f : .65f;
                light.color = i == 0 ? new Color(1f, .92f, .81f) : new Color(.82f, .9f, 1f);
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(i == 0 ? 50f : 35f, i == 0 ? -30f : 150f, 0f);
            }
            camera.Render();
            RenderTexture.active = texture;
            image = new Texture2D(1600, 1200, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 1200), 0, 0);
            image.Apply();
            var output = "ArtSource/Restaurant/Unity_Vignette.png";
            System.IO.File.WriteAllBytes(output, image.EncodeToPNG());
            return new { preview = output, instances = entries.Count };
        }
        finally
        {
            RenderTexture.active = previous;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
