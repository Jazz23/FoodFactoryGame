// Imports only the restaurant model pack and its shared URP materials; never registers gameplay content or edits scenes.
// Execute the Run body with Unity MCP execute_code after running ArtSource/Restaurant/build_restaurant.py.
public static class ImportRestaurantArt
{
    public static object Run()
    {
        var folder = "Assets/Art/Restaurant";
        UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
        if (!UnityEditor.AssetDatabase.IsValidFolder(folder + "/Materials"))
            UnityEditor.AssetDatabase.CreateFolder(folder, "Materials");
        var names = new[] { "Cream", "Teal", "Walnut", "Oak", "Charcoal", "Brass", "Steel", "Terracotta", "Brick", "Sage", "Leaf", "Ivory", "Grout", "Glow", "Glass", "Chalk" };
        var colors = new[] { "#eadfc9", "#285d59", "#67402c", "#ba8954", "#293237", "#ba9150", "#adbcbf", "#b86143", "#985a44", "#6c8760", "#315b3e", "#f5efdc", "#a69984", "#ffdda1", "#91bcb9", "#d8d5b9" };
        var roughness = new[] { .78f, .55f, .52f, .6f, .52f, .3f, .3f, .8f, .85f, .8f, .8f, .4f, .9f, .3f, .12f, .85f };
        var metal = new[] { 0f, 0f, 0f, 0f, .25f, .7f, .75f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new System.InvalidOperationException("URP Lit unavailable.");
        var materials = new System.Collections.Generic.Dictionary<string, Material>();
        for (var i = 0; i < names.Length; i++)
        {
            var name = "RT_" + names[i];
            var path = folder + "/Materials/" + name + ".mat";
            var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                UnityEditor.AssetDatabase.CreateAsset(material, path);
            }
            var color = Color.white;
            ColorUtility.TryParseHtmlString(colors[i], out color);
            var glass = names[i] == "Glass";
            if (glass) color.a = .22f;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 1f - roughness[i]);
            material.SetFloat("_Metallic", metal[i]);
            material.SetFloat("_Surface", glass ? 1f : 0f);
            material.SetFloat("_SrcBlend", glass ? 5f : 1f);
            material.SetFloat("_DstBlend", glass ? 10f : 0f);
            material.SetFloat("_ZWrite", glass ? 0f : 1f);
            material.SetFloat("_Cull", glass ? 0f : 2f);
            material.SetOverrideTag("RenderType", glass ? "Transparent" : "Opaque");
            material.renderQueue = glass ? 3000 : 2000;
            if (glass) material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetShaderPassEnabled("ShadowCaster", !glass);
            if (names[i] == "Glow")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * .4f);
            }
            material.enableInstancing = true;
            UnityEditor.EditorUtility.SetDirty(material);
            materials[name] = material;
        }
        var paths = System.IO.Directory.GetFiles(folder + "/Models", "*.fbx", System.IO.SearchOption.AllDirectories);
        foreach (var rawPath in paths)
        {
            var path = rawPath.Replace((char)92, (char)47);
            var importer = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(path);
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importAnimation = false;
            importer.animationType = UnityEditor.ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.importNormals = UnityEditor.ModelImporterNormals.Import;
            importer.importTangents = UnityEditor.ModelImporterTangents.CalculateMikk;
            importer.generateSecondaryUV = true;
            importer.materialImportMode = UnityEditor.ModelImporterMaterialImportMode.ImportStandard;
            foreach (var pair in materials)
                importer.AddRemap(new UnityEditor.AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.SaveAndReimport();
        }
        UnityEditor.AssetDatabase.SaveAssets();
        return new { models = paths.Length, materials = materials.Count, folder = folder };
    }
}
