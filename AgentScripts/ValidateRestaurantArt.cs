// Checks every restaurant FBX against its Blender manifest and writes a bounded authoring-validation report.
// Execute the Run body through Unity MCP. This does not run or modify the restaurant simulation.
public static class ValidateRestaurantArt
{
    public static object Run()
    {
        UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
        var source = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("ArtSource/Restaurant/manifest.json"));
        var specs = (Newtonsoft.Json.Linq.JArray)source["assets"];
        var records = new System.Collections.Generic.List<object>();
        var failures = new System.Collections.Generic.List<string>();
        foreach (var spec in specs)
        {
            var path = (string)spec["path"];
            var model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) { failures.Add(path + ": missing model"); continue; }
            var meshes = model.GetComponentsInChildren<MeshFilter>(true);
            var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) { failures.Add(path + ": no renderer"); continue; }
            var bounds = renderers[0].bounds;
            // FBX import may reorder submeshes together with their slots; compare the named material set.
            var expectedMaterials = spec["materials"].Select(token => (string)token).OrderBy(name => name).ToArray();
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                if (!renderer.sharedMaterials.Select(material => material != null ? material.name : "").OrderBy(name => name).SequenceEqual(expectedMaterials))
                    failures.Add(path + ": material names differ from Blender manifest");
                foreach (var material in renderer.sharedMaterials)
                    if (material == null || material.shader.name != "Universal Render Pipeline/Lit"
                        || !UnityEditor.AssetDatabase.GetAssetPath(material).StartsWith("Assets/Art/Restaurant/Materials/"))
                        failures.Add(path + ": material mapping");
            }
            var triangles = 0;
            foreach (var filter in meshes)
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) { failures.Add(path + ": empty mesh"); continue; }
                for (var sub = 0; sub < mesh.subMeshCount; sub++) triangles += (int)mesh.GetIndexCount(sub) / 3;
                if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)
                    || !mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1))
                    failures.Add(path + ": missing surface/lightmap UVs");
            }
            if (triangles != (int)spec["triangles"]) failures.Add(path + ": triangle count differs from Blender");
            var dimensions = spec["dimensions_blender_m"];
            var expected = new Vector3((float)dimensions[0], (float)dimensions[2], (float)dimensions[1]);
            if ((bounds.size - expected).sqrMagnitude > .000003f) failures.Add(path + ": dimensions differ from Blender");
            var category = (string)spec["category"];
            if ((category == "Furniture" || category == "Service") && Mathf.Abs(bounds.min.y) > .001f)
                failures.Add(path + ": feet do not touch floor pivot");
            if (model.GetComponentsInChildren<MonoBehaviour>(true).Length > 0 || model.GetComponentsInChildren<Collider>(true).Length > 0)
                failures.Add(path + ": unexpected gameplay component");
            records.Add(new { name = model.name, triangles = triangles,
                size = new[] { bounds.size.x, bounds.size.y, bounds.size.z },
                min = new[] { bounds.min.x, bounds.min.y, bounds.min.z },
                max = new[] { bounds.max.x, bounds.max.y, bounds.max.z }, meshCount = meshes.Length });
        }
        var imported = System.IO.Directory.GetFiles("Assets/Art/Restaurant/Models", "*.fbx", System.IO.SearchOption.AllDirectories).Length;
        if (specs.Count == 0 || imported != specs.Count) failures.Add("Manifest/imported file count mismatch or empty selection");
        var report = new { run = "restaurant-art-20261001-final", filter = "Assets/Art/Restaurant/Models/**/*.fbx",
            matched = records.Count, failures = failures, models = records };
        var artifact = "ArtSource/Restaurant/unity-validation.json";
        System.IO.File.WriteAllText(artifact, Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return new { matched = records.Count, failures = failures, artifact = artifact };
    }
}
