// Installs the world art from ArtSource/World (decision 0026 presentation): texture import settings, one URP Lit material per
// WG_* slot (base map with smoothness in alpha, normal map, tint), the WorldArt.fbx material remap, the WorldArtCatalog of
// meshes, and the catalog reference on WorldGen's WorldLayoutPresenter. Copy ArtSource/World/Textures/*.png to
// Assets/Art/World/Textures and ArtSource/World/Export/WorldArt.fbx to Assets/Art/World/Models first. Idempotent; keeps GUIDs.
// Run the body of Run() with the Unity MCP execute_code tool (C# 6 / CodeDom compatible, no helper methods).
public static class BuildWorldArt
{
    public static object Run()
    {
        const string textureFolder = "Assets/Art/World/Textures";
        const string materialFolder = "Assets/Art/World/Materials";
        const string modelPath = "Assets/Art/World/Models/WorldArt.fbx";
        const string catalogPath = "Assets/Art/World/WorldArtCatalog.asset";
        const string scenePath = "Assets/Scenes/WorldGen.unity";

        // Textures: normal maps as normal maps; base maps sRGB with alpha as smoothness; all repeat, trilinear, 4x aniso.
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolder }))
        {
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
            var normal = path.EndsWith("_Normal.png");
            importer.textureType = normal ? UnityEditor.TextureImporterType.NormalMap : UnityEditor.TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.alphaSource = normal ? UnityEditor.TextureImporterAlphaSource.None : UnityEditor.TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }

        // Materials: name, texture (or ""), tint, smoothness (texture alpha scale, or the constant for flat ones), metallic.
        var specs = new object[][]
        {
            new object[] { "WG_Facade_Brick", "Facade_Brick", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Facade_Siding", "Facade_Siding", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Facade_Plaster", "Facade_Plaster", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Facade_Apartment", "Facade_Apartment", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Facade_Office", "Facade_Office", new Color(1f, 1f, 1f), 1f, 0.2f },
            new object[] { "WG_Facade_OfficeDark", "Facade_Office", new Color(0.6f, 0.62f, 0.66f), 1f, 0.3f },
            new object[] { "WG_Facade_Storefront", "Facade_Storefront", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Stucco", "Concrete", new Color(0.95f, 0.88f, 0.74f), 1f, 0f },
            new object[] { "WG_Trim", "Concrete", new Color(0.92f, 0.92f, 0.9f), 1f, 0f },
            new object[] { "WG_Concrete", "Concrete", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Metal_Wall", "Metal_Corrugated", new Color(0.62f, 0.7f, 0.78f), 1f, 0.3f },
            new object[] { "WG_Metal_Roof", "Metal_Corrugated", new Color(0.4f, 0.42f, 0.45f), 1f, 0.4f },
            new object[] { "WG_Window_Industrial", "Window_Industrial", new Color(1f, 1f, 1f), 1f, 0.1f },
            new object[] { "WG_Door_Roller", "Door_Roller", new Color(1f, 1f, 1f), 1f, 0.3f },
            new object[] { "WG_Door_Wood", "Door_Wood", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Door_Glass", "Door_Glass", new Color(1f, 1f, 1f), 1f, 0.1f },
            new object[] { "WG_Wood_Barn", "Wood_BarnPlanks", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Roof_Shingles", "Roof_Shingles", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Roof_ClayTiles", "Roof_ClayTiles", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Roof_Flat", "Roof_Flat", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Awning", "", new Color(0.75f, 0.15f, 0.12f), 0.25f, 0f },
            new object[] { "WG_Sign", "", new Color(0.1f, 0.1f, 0.12f), 0.5f, 0f },
            new object[] { "WG_Metal_Dark", "", new Color(0.2f, 0.21f, 0.22f), 0.5f, 0.6f },
            new object[] { "WG_Paint_Yellow", "", new Color(0.9f, 0.72f, 0.1f), 0.3f, 0f },
            new object[] { "WG_Steel", "", new Color(0.55f, 0.56f, 0.58f), 0.7f, 0.9f },
            new object[] { "WG_Road_Arterial", "Road_Arterial", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Road_Arterial_Crosswalk", "Road_Arterial_Crosswalk", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Road_Local", "Road_Local", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Road_Local_Crosswalk", "Road_Local_Crosswalk", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Road_Rural", "Road_Rural", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Road_Junction", "Road_Junction", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Rail_Ballast", "Rail_Ballast", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Ground", "Ground_Grass", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Ground_Grass", "Ground_Grass", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Ground_Paving", "Ground_Paving", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Ground_Yard", "Ground_Yard", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Field_Wheat", "Field_Wheat", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Field_Greens", "Field_Greens", new Color(1f, 1f, 1f), 1f, 0f },
            // Generator v2 (2026-09-28): water, river banks, trees, signs and signal lamps (lamps glow: 6th value is emission).
            new object[] { "WG_Water", "Water", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Ground_Bank", "Ground_Bank", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Bark", "Bark", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Foliage", "Foliage", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Foliage_Dark", "Foliage", new Color(0.55f, 0.75f, 0.6f), 1f, 0f },
            new object[] { "WG_Foliage_Light", "Foliage", new Color(1f, 1f, 0.7f), 1f, 0f },
            new object[] { "WG_Sign_Stop", "Sign_Stop", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Sign_Crossbuck", "Sign_Crossbuck", new Color(1f, 1f, 1f), 1f, 0f },
            new object[] { "WG_Lamp_Red", "", new Color(0.85f, 0.08f, 0.05f), 0.8f, 0f, new Color(0.9f, 0.05f, 0.02f) },
            new object[] { "WG_Lamp_Amber", "", new Color(0.45f, 0.28f, 0.03f), 0.8f, 0f, new Color(0.12f, 0.07f, 0f) },
            new object[] { "WG_Lamp_Green", "", new Color(0.05f, 0.4f, 0.18f), 0.8f, 0f, new Color(0.02f, 0.25f, 0.1f) },
            new object[] { "WG_Paint_Red", "", new Color(0.75f, 0.06f, 0.05f), 0.3f, 0f }
        };
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var materials = new System.Collections.Generic.Dictionary<string, Material>();
        foreach (var spec in specs)
        {
            var name = (string)spec[0];
            var texture = (string)spec[1];
            var path = materialFolder + "/" + name + ".mat";
            var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(lit);
                UnityEditor.AssetDatabase.CreateAsset(material, path);
            }
            material.shader = lit;
            material.SetColor("_BaseColor", (Color)spec[2]);
            material.SetFloat("_Metallic", (float)spec[4]);
            material.SetFloat("_Smoothness", (float)spec[3]);
            if (texture.Length > 0)
            {
                material.SetTexture("_BaseMap", UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(textureFolder + "/" + texture + ".png"));
                material.SetTexture("_BumpMap", UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(textureFolder + "/" + texture + "_Normal.png"));
                material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
                // Smoothness comes from the base map's alpha (glass is smooth, brick is not).
                material.SetFloat("_SmoothnessTextureChannel", 1f);
                material.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            }
            else
            {
                material.SetTexture("_BaseMap", null);
                material.SetTexture("_BumpMap", null);
                material.DisableKeyword("_NORMALMAP");
                material.SetFloat("_SmoothnessTextureChannel", 0f);
                material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            }
            if (spec.Length > 5)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", (Color)spec[5]);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
            material.enableInstancing = true;
            UnityEditor.EditorUtility.SetDirty(material);
            materials[name] = material;
        }
        UnityEditor.AssetDatabase.SaveAssets();

        // Model: readable meshes (merged and static-batched at runtime), imported normals, generated tangents, and every
        // embedded WG_* material remapped to the material above.
        var model = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(modelPath);
        model.globalScale = 1f;
        model.useFileScale = true;
        model.isReadable = true;
        model.importNormals = UnityEditor.ModelImporterNormals.Import;
        model.importTangents = UnityEditor.ModelImporterTangents.CalculateMikk;
        model.meshCompression = UnityEditor.ModelImporterMeshCompression.Off;
        model.importAnimation = false;
        model.animationType = UnityEditor.ModelImporterAnimationType.None;
        model.materialImportMode = UnityEditor.ModelImporterMaterialImportMode.ImportStandard;
        foreach (var pair in materials)
            model.AddRemap(new UnityEditor.AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
        model.SaveAndReimport();

        // Catalog: every mesh object in the model with its remapped materials.
        var root = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var pieces = new System.Collections.Generic.List<FoodFactoryGame.Session.WorldMap.WorldArtCatalog.Piece>();
        var missing = new System.Collections.Generic.List<string>();
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            foreach (var used in renderer.sharedMaterials)
                if (used == null || !materials.ContainsKey(used.name)) missing.Add(filter.name + ":" + (used == null ? "null" : used.name));
            var piece = new FoodFactoryGame.Session.WorldMap.WorldArtCatalog.Piece();
            piece.name = filter.name;
            piece.mesh = filter.sharedMesh;
            piece.materials = renderer.sharedMaterials;
            pieces.Add(piece);
        }
        pieces.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<FoodFactoryGame.Session.WorldMap.WorldArtCatalog>(catalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<FoodFactoryGame.Session.WorldMap.WorldArtCatalog>();
            UnityEditor.AssetDatabase.CreateAsset(catalog, catalogPath);
        }
        catalog.SetPieces(pieces.ToArray());
        catalog.grass = materials["WG_Ground_Grass"];
        catalog.paving = materials["WG_Ground_Paving"];
        catalog.yard = materials["WG_Ground_Yard"];
        catalog.wheat = materials["WG_Field_Wheat"];
        catalog.greens = materials["WG_Field_Greens"];
        catalog.water = materials["WG_Water"];
        catalog.bank = materials["WG_Ground_Bank"];
        UnityEditor.EditorUtility.SetDirty(catalog);
        UnityEditor.AssetDatabase.SaveAssets();
        // A catalog created in this run saved as an empty scene reference unless reloaded from disk first (seen 2026-09-27).
        UnityEditor.AssetDatabase.ImportAsset(catalogPath, UnityEditor.ImportAssetOptions.ForceUpdate);
        catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<FoodFactoryGame.Session.WorldMap.WorldArtCatalog>(catalogPath);

        // WorldGen's presenter uses the catalog.
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        var presenter = UnityEngine.Object.FindFirstObjectByType<FoodFactoryGame.Session.WorldMap.WorldLayoutPresenter>();
        var serialized = new UnityEditor.SerializedObject(presenter);
        serialized.FindProperty("art").objectReferenceValue = catalog;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        if (!string.IsNullOrEmpty(previous) && previous != scenePath)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previous, UnityEditor.SceneManagement.OpenSceneMode.Single);
        return "pieces " + pieces.Count + ", materials " + materials.Count + ", unmapped " + string.Join(", ", missing.ToArray());
    }
}
