// Editor authoring script (run via Unity MCP run_script): creates the dev session prefabs, prefab catalog, equipment content,
// recipe and item content, icon imports, ghost materials, UI panel settings and the DevSite scene, then makes DevSite the
// only build scene. Safe to re-run: assets keep GUIDs.
// The scene holds no equipment instances; placed equipment is shown from replicated state by EquipmentPresenter.
// Belts: the three belt models (ArtSource/Belt, copied to Assets/Art/Models/Belt) get project URP materials and are wrapped
// in tile-centred prefabs that travel +Z; BeltPresenter draws placed belts and riding goods from replicated state.
using System.IO;
using System.Linq;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Server;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Goods.Network;
using FoodFactoryGame.Session;
using FoodFactoryGame.Session.Belts;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public static class BuildDevSite
{
    private const string InputPath = "Assets/InputSystem_Actions.inputactions";
    private const string ThemePath = "Assets/UI/DevRuntimeTheme.tss";
    private const string PanelSettingsPath = "Assets/UI/SessionPanelSettings.asset";
    private const string BridgePath = "Assets/Prefabs/Network/GoodsNetworkBridge.prefab";
    private const string PlayerPath = "Assets/Prefabs/Player/Player.prefab";
    private const string CatalogPath = "Assets/Network/GamePrefabs.asset";
    private const string ScenePath = "Assets/Scenes/DevSite.unity";
    private const string OvenPrefabPath = "Assets/Prefabs/Equipment/Oven.prefab";
    private const string OvenDefinitionPath = "Assets/Content/Equipment/Oven.asset";
    private const string GhostMaterialPath = "Assets/Materials/PlacementGhost.mat";
    private const string BreadRecipePath = "Assets/Content/Recipes/Bread.asset";
    private const string GhostModelMaterialPath = "Assets/Materials/EquipmentGhost.mat";
    private const string IconFolder = "Assets/Art/Icons";
    private const string ItemFolder = "Assets/Content/Items";
    private const string BeltModelFolder = "Assets/Art/Models/Belt";
    private const string BeltMaterialFolder = "Assets/Materials/Belt";
    private const string BeltPrefabFolder = "Assets/Prefabs/Belts";
    private const string BeltItemSpritePath = "Assets/Materials/BeltItemSprite.mat";
    private const string CounterPrefabPath = "Assets/Prefabs/Equipment/Counter.prefab";
    private const string CounterDefinitionPath = "Assets/Content/Equipment/Counter.asset";
    private const string CounterMaterialFolder = "Assets/Materials/Counter";
    private const string SellBreadRecipePath = "Assets/Content/Recipes/SellBread.asset";
    private const string FridgePrefabPath = "Assets/Prefabs/Equipment/Fridge.prefab";
    private const string FridgeDefinitionPath = "Assets/Content/Equipment/Fridge.asset";
    private const string FridgeMaterialFolder = "Assets/Materials/Fridge";
    private const string OfferFolder = "Assets/Content/Offers";

    public static string Run()
    {
        foreach (var folder in new[] { "Assets/UI", "Assets/Prefabs/Network", "Assets/Prefabs/Player", "Assets/Network", "Assets/Content/Equipment", "Assets/Content/Recipes", "Assets/Content/Items", "Assets/Materials", BeltMaterialFolder, BeltPrefabFolder, CounterMaterialFolder, FridgeMaterialFolder, "Assets/Prefabs/Equipment", OfferFolder })
            Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
        var panelSettings = BuildPanelSettings();
        var bridge = BuildBridge();
        var player = BuildPlayer();
        var catalog = BuildCatalog(bridge, player);
        var oven = BuildOvenDefinition(ImportIcon("Oven"));
        var bread = BuildBreadRecipe();
        var counter = BuildEquipmentDefinition(CounterDefinitionPath, DevWorld.CounterKind, 2, 1, 2, 1, BuildCounterPrefab(), ImportIcon("Counter"));
        var sellBread = BuildSellBreadRecipe();
        // PROTOTYPE fridge (decision 0018): 1x1 m, 8 refrigerated storage slots where goods do not spoil, no recipes; the output
        // slot is unused (equipment always has both buffers).
        var fridge = BuildEquipmentDefinition(FridgeDefinitionPath, "fridge", 1, 1, 8, 1, BuildFridgePrefab(), ImportIcon("Fridge"), true);
        // PROTOTYPE supplier prices (decision 0014): dough at 50 cents a unit leaves $2.00 margin on a $2.50 bread. An oven
        // (decision 0017) costs $150.00, 75 breads of margin, so the $500.00 start can afford one while keeping ingredient money.
        // A fridge costs $80.00.
        var offers = new[]
        {
            BuildOffer("Dough5", "supplier-dough-5", DevWorld.DoughItemId, 5, 250, DevWorld.DoughSpoilAfterSeconds),
            BuildOffer("Belt10", "supplier-belt-10", GoodsWorld.BeltItemId, 10, 500, GoodsWorld.NonPerishableSeconds),
            BuildOffer("Oven1", "supplier-oven", "", 1, 15000, 1, oven),
            BuildOffer("Fridge1", "supplier-fridge", "", 1, 8000, 1, fridge)
        };
        // PROTOTYPE stack sizes: dough and bread 20, belts 100 (Factorio's belt stack).
        var items = new[] { BuildItem(DevWorld.DoughItemId, "Dough", 20), BuildItem("bread", "Bread", 20), BuildItem(GoodsWorld.BeltItemId, "Belt", 100) };
        var ghostMaterial = BuildGhostMaterial();
        var ghostModelMaterial = BuildGhostModelMaterial();
        var tread = BuildBeltMaterials();
        var beltPrefabs = new[] { BuildBeltPrefab("Conveyor_Straight_1m", "BeltStraight"), BuildBeltPrefab("Conveyor_Corner_Left_90", "BeltCornerLeft"), BuildBeltPrefab("Conveyor_Corner_Right_90", "BeltCornerRight") };
        var itemSprite = BuildItemSpriteMaterial();
        BuildScene(catalog, bridge, player, panelSettings, new[] { oven, counter, fridge }, new[] { bread, sellBread }, offers, items, ghostMaterial,
            ghostModelMaterial, beltPrefabs, tread, itemSprite);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        return "DevSite authored";
    }

    private static PanelSettings BuildPanelSettings()
    {
        if (!File.Exists(ThemePath))
        {
            File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset(ThemePath);
        }
        var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
        }
        settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        EditorUtility.SetDirty(settings);
        return settings;
    }

    // DEVELOPMENT content: the oven measures about 2.6 x 2.2 m, so it takes a 3x3-cell footprint with clearance.
    // Buffer capacities are placeholders until recipe content exists.
    // Capacity counts slots (decision 0009): one input stack and one output stack, like a Factorio furnace.
    private static EquipmentDefinition BuildOvenDefinition(Sprite icon)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OvenPrefabPath);
        if (prefab == null) throw new System.InvalidOperationException($"Missing {OvenPrefabPath}.");
        return BuildEquipmentDefinition(OvenDefinitionPath, "oven", 3, 3, 1, 1, prefab, icon);
    }

    private static EquipmentDefinition BuildEquipmentDefinition(string path, string kind, int width, int depth, int inputCapacity,
        int outputCapacity, GameObject prefab, Sprite icon, bool inputRefrigerated = false)
    {
        var definition = AssetDatabase.LoadAssetAtPath<EquipmentDefinition>(path);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<EquipmentDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }
        using (var serialized = new SerializedObject(definition))
        {
            serialized.FindProperty("kind").stringValue = kind;
            serialized.FindProperty("width").intValue = width;
            serialized.FindProperty("depth").intValue = depth;
            serialized.FindProperty("inputCapacity").intValue = inputCapacity;
            serialized.FindProperty("outputCapacity").intValue = outputCapacity;
            serialized.FindProperty("inputRefrigerated").boolValue = inputRefrigerated;
            serialized.FindProperty("outputRefrigerated").boolValue = false;
            serialized.FindProperty("visualPrefab").objectReferenceValue = prefab;
            serialized.FindProperty("icon").objectReferenceValue = icon;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(definition);
        return definition;
    }

    // DEVELOPMENT placeholder art for the decision-0013 sell counter: a 2 x 1 m wooden counter with a cream top and a teal
    // register, built from primitives. One box collider on the root lets aim rays name it and keeps players out of it.
    // The equipment has a 2-slot input and an unused 1-slot output (equipment always has both buffers).
    private static GameObject BuildCounterPrefab()
    {
        var wood = LitMaterial(CounterMaterialFolder, "CounterWood", new Color(0.55f, 0.33f, 0.16f), 0f, 0.35f);
        var top = LitMaterial(CounterMaterialFolder, "CounterTop", new Color(0.95f, 0.93f, 0.86f), 0f, 0.6f);
        var register = LitMaterial(CounterMaterialFolder, "CounterRegister", new Color(0.18f, 0.5f, 0.53f), 0.2f, 0.5f);
        var screen = LitMaterial(CounterMaterialFolder, "CounterScreen", new Color(0.6f, 0.9f, 0.77f), 0f, 0.8f);
        var root = new GameObject("Counter");
        try
        {
            Block(root, "Body", wood, new Vector3(0f, 0.45f, 0f), new Vector3(1.9f, 0.9f, 0.8f));
            Block(root, "Top", top, new Vector3(0f, 0.93f, 0f), new Vector3(2f, 0.06f, 0.9f));
            Block(root, "Register", register, new Vector3(0.5f, 1.08f, 0f), new Vector3(0.45f, 0.24f, 0.35f));
            Block(root, "Screen", screen, new Vector3(0.5f, 1.27f, 0.08f), new Vector3(0.32f, 0.14f, 0.04f));
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.6f, 0f);
            box.size = new Vector3(2f, 1.2f, 0.9f);
            return PrefabUtility.SaveAsPrefabAsset(root, CounterPrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // DEVELOPMENT placeholder art for the decision-0018 fridge: a 0.9 x 0.8 x 1.9 m white two-door fridge with steel handles,
    // dark feet and an ice-blue badge, built from primitives. One box collider on the root lets aim rays name it.
    private static GameObject BuildFridgePrefab()
    {
        var shell = LitMaterial(FridgeMaterialFolder, "FridgeShell", new Color(0.93f, 0.95f, 0.96f), 0.1f, 0.7f);
        var steel = LitMaterial(FridgeMaterialFolder, "FridgeSteel", new Color(0.55f, 0.6f, 0.63f), 0.9f, 0.6f);
        var trim = LitMaterial(FridgeMaterialFolder, "FridgeTrim", new Color(0.12f, 0.16f, 0.18f), 0f, 0.3f);
        var badge = LitMaterial(FridgeMaterialFolder, "FridgeBadge", new Color(0.36f, 0.75f, 0.92f), 0f, 0.8f);
        var root = new GameObject("Fridge");
        try
        {
            Block(root, "Body", shell, new Vector3(0f, 0.97f, 0f), new Vector3(0.9f, 1.86f, 0.8f));
            // The door seam and handles face -Z, the front of an unrotated piece.
            Block(root, "Seam", trim, new Vector3(0f, 1.3f, -0.402f), new Vector3(0.9f, 0.02f, 0.01f));
            Block(root, "FreezerHandle", steel, new Vector3(0.33f, 1.55f, -0.43f), new Vector3(0.04f, 0.3f, 0.05f));
            Block(root, "FridgeHandle", steel, new Vector3(0.33f, 0.95f, -0.43f), new Vector3(0.04f, 0.5f, 0.05f));
            Block(root, "Badge", badge, new Vector3(-0.15f, 0.8f, -0.405f), new Vector3(0.22f, 0.22f, 0.01f));
            Block(root, "Feet", trim, new Vector3(0f, 0.02f, 0f), new Vector3(0.8f, 0.04f, 0.7f));
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.95f, 0f);
            box.size = new Vector3(0.9f, 1.9f, 0.8f);
            return PrefabUtility.SaveAsPrefabAsset(root, FridgePrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void Block(GameObject parent, string name, Material material, Vector3 position, Vector3 scale)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        Object.DestroyImmediate(block.GetComponent<BoxCollider>());
        block.transform.SetParent(parent.transform, false);
        block.transform.localPosition = position;
        block.transform.localScale = scale;
        block.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // DEVELOPMENT content (decision 0013): the counter serves one customer every 5 s, each buying one edible bread for $2.50.
    private static RecipeAsset BuildSellBreadRecipe()
    {
        var recipe = AssetDatabase.LoadAssetAtPath<RecipeAsset>(SellBreadRecipePath);
        if (recipe == null)
        {
            recipe = ScriptableObject.CreateInstance<RecipeAsset>();
            AssetDatabase.CreateAsset(recipe, SellBreadRecipePath);
        }
        using (var serialized = new SerializedObject(recipe))
        {
            serialized.FindProperty("id").stringValue = "counter-sell-bread";
            serialized.FindProperty("displayName").stringValue = "Sell bread";
            serialized.FindProperty("stationKind").stringValue = DevWorld.CounterKind;
            serialized.FindProperty("durationSeconds").intValue = 5;
            var inputs = serialized.FindProperty("inputs");
            inputs.arraySize = 1;
            inputs.GetArrayElementAtIndex(0).FindPropertyRelative("itemId").stringValue = "bread";
            inputs.GetArrayElementAtIndex(0).FindPropertyRelative("quantity").intValue = 1;
            serialized.FindProperty("outputItemId").stringValue = "";
            serialized.FindProperty("saleCents").intValue = 250;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(recipe);
        return recipe;
    }

    // DEVELOPMENT content: the owner's first oven recipe, one dough to one bread in 10 s. Bread spoils after an hour ambient.
    private static RecipeAsset BuildBreadRecipe()
    {
        var recipe = AssetDatabase.LoadAssetAtPath<RecipeAsset>(BreadRecipePath);
        if (recipe == null)
        {
            recipe = ScriptableObject.CreateInstance<RecipeAsset>();
            AssetDatabase.CreateAsset(recipe, BreadRecipePath);
        }
        using (var serialized = new SerializedObject(recipe))
        {
            serialized.FindProperty("id").stringValue = "oven-bread";
            serialized.FindProperty("displayName").stringValue = "Bread";
            serialized.FindProperty("stationKind").stringValue = "oven";
            serialized.FindProperty("durationSeconds").intValue = 10;
            var inputs = serialized.FindProperty("inputs");
            inputs.arraySize = 1;
            inputs.GetArrayElementAtIndex(0).FindPropertyRelative("itemId").stringValue = DevWorld.DoughItemId;
            inputs.GetArrayElementAtIndex(0).FindPropertyRelative("quantity").intValue = 1;
            serialized.FindProperty("outputItemId").stringValue = "bread";
            serialized.FindProperty("outputQuantity").intValue = 1;
            serialized.FindProperty("outputSpoilAfterSeconds").intValue = 3600;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(recipe);
        return recipe;
    }

    private static OfferAsset BuildOffer(string asset, string id, string itemId, int quantity, int priceCents, long spoilAfterSeconds,
        EquipmentDefinition equipment = null)
    {
        var path = $"{OfferFolder}/{asset}.asset";
        var offer = AssetDatabase.LoadAssetAtPath<OfferAsset>(path);
        if (offer == null)
        {
            offer = ScriptableObject.CreateInstance<OfferAsset>();
            AssetDatabase.CreateAsset(offer, path);
        }
        using (var serialized = new SerializedObject(offer))
        {
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("equipment").objectReferenceValue = equipment;
            serialized.FindProperty("itemId").stringValue = itemId;
            serialized.FindProperty("quantity").intValue = quantity;
            serialized.FindProperty("priceCents").intValue = priceCents;
            serialized.FindProperty("spoilAfterSeconds").longValue = spoilAfterSeconds;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(offer);
        return offer;
    }

    // DEVELOPMENT icons drawn by AgentScripts/DrawItemIcons.ps1, imported as UI sprites.
    private static Sprite ImportIcon(string name)
    {
        var path = $"{IconFolder}/{name}.png";
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) throw new System.InvalidOperationException($"Missing {path}; run DrawItemIcons.ps1.");
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static ItemDefinition BuildItem(string id, string displayName, int maxStack)
    {
        var path = $"{ItemFolder}/{displayName}.asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemDefinition>();
            AssetDatabase.CreateAsset(item, path);
        }
        using (var serialized = new SerializedObject(item))
        {
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("icon").objectReferenceValue = ImportIcon(displayName);
            serialized.FindProperty("maxStack").intValue = maxStack;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(item);
        return item;
    }

    // URP Lit materials for the belt models, remapped onto every belt FBX by name; returns the tread (belt surface) material,
    // whose base map is the repeating arrow texture. BeltPresenter scrolls a runtime copy of it.
    private static Material BuildBeltMaterials()
    {
        var texturePath = $"{BeltModelFolder}/Conveyor_Tread_BaseColor.png";
        if (!(AssetImporter.GetAtPath(texturePath) is TextureImporter textureImporter)) throw new System.InvalidOperationException($"Missing {texturePath}.");
        textureImporter.wrapMode = TextureWrapMode.Repeat;
        textureImporter.anisoLevel = 4;
        textureImporter.SaveAndReimport();
        var tread = BeltMaterial("BeltTread", Color.white, 0.15f, 0.32f);
        tread.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        tread.SetTextureScale("_BaseMap", Vector2.one);
        EditorUtility.SetDirty(tread);
        // Source material names in the FBX files -> project materials (README_Corners.md lists the slots).
        var materials = new (string Match, Material Material)[]
        {
            ("tread", tread),
            ("Rubber", BeltMaterial("BeltRubber", new Color(0.07f, 0.07f, 0.075f), 0f, 0.2f)),
            ("hardware", BeltMaterial("BeltHardware", new Color(0.62f, 0.64f, 0.66f), 0.9f, 0.55f)),
            ("frame", BeltMaterial("BeltFrame", new Color(0.17f, 0.18f, 0.2f), 0.4f, 0.45f)),
            ("amber", BeltMaterial("BeltRail", new Color(0.95f, 0.62f, 0.08f), 0f, 0.5f))
        };
        foreach (var model in new[] { "Conveyor_Straight_1m", "Conveyor_Corner_Left_90", "Conveyor_Corner_Right_90" })
        {
            var path = $"{BeltModelFolder}/{model}.fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) throw new System.InvalidOperationException($"Missing {path}.");
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            var names = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(x => x.name)
                .Concat(importer.GetExternalObjectMap().Keys.Where(x => x.type == typeof(Material)).Select(x => x.name)).Distinct().ToList();
            foreach (var name in names)
            {
                var target = materials.FirstOrDefault(x => name.IndexOf(x.Match, System.StringComparison.OrdinalIgnoreCase) >= 0).Material;
                if (target == null) throw new System.InvalidOperationException($"No belt material for '{name}' in {path}.");
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), target);
            }
            importer.SaveAndReimport();
        }
        return tread;
    }

    private static Material BeltMaterial(string name, Color color, float metallic, float smoothness) =>
        LitMaterial(BeltMaterialFolder, name, color, metallic, smoothness);

    private static Material LitMaterial(string folder, string name, Color color, float metallic, float smoothness)
    {
        var path = $"{folder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    // A tile-centred belt prefab that travels +Z. The FBX modules travel -Z from an inlet at their origin (Blender +Y), so
    // the model is turned half a turn and moved back half a tile. A trigger box covering the tile lets aim rays name the
    // belt (BeltVisual is added by BeltPresenter) without blocking player movement.
    private static GameObject BuildBeltPrefab(string model, string name)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{BeltModelFolder}/{model}.fbx");
        if (source == null) throw new System.InvalidOperationException($"Missing {model}.fbx.");
        var root = new GameObject(name);
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * instance.transform.localRotation;
            instance.transform.localPosition = new Vector3(0f, 0f, -0.5f);
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0.45f, 0f);
            box.size = new Vector3(1f, 0.9f, 1f);
            return PrefabUtility.SaveAsPrefabAsset(root, $"{BeltPrefabFolder}/{name}.prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }

    // Goods riding belts are camera-facing icon sprites; unlit so they read the same as the HUD icons.
    private static Material BuildItemSpriteMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(BeltItemSpritePath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) throw new System.InvalidOperationException("Missing the URP unlit sprite shader.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, BeltItemSpritePath);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    // URP Lit, transparent (alpha blended, no depth write), so the machine ghost is see-through but still shaded.
    private static Material BuildGhostModelMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(GhostModelMaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, GhostModelMaterialPath);
        }
        material.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.45f));
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Smoothness", 0.2f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material BuildGhostMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(material, GhostMaterialPath);
        }
        material.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static NetworkObject BuildBridge()
    {
        var root = new GameObject("GoodsNetworkBridge");
        try
        {
            root.AddComponent<NetworkObject>();
            root.AddComponent<GoodsNetworkBridge>();
            return PrefabUtility.SaveAsPrefabAsset(root, BridgePath).GetComponent<NetworkObject>();
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static T Ensure<T>(GameObject target) where T : Component
        => target.TryGetComponent<T>(out var existing) ? existing : target.AddComponent<T>();

    private static InputActionReference Action(string name)
    {
        var reference = AssetDatabase.LoadAllAssetsAtPath(InputPath).OfType<InputActionReference>()
            .FirstOrDefault(x => x.action != null && x.action.actionMap.name == "Player" && x.action.name == name);
        if (reference == null) throw new System.InvalidOperationException($"Missing Player/{name} action reference.");
        return reference;
    }

    private static NetworkObject BuildPlayer()
    {
        var root = new GameObject("Player");
        try
        {
            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkTransform>();
            var controller = root.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 1f, 0f);
            var avatar = root.AddComponent<PlayerAvatar>();

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            // A facing marker so remote orientation is visible on the placeholder capsule.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Facing";
            Object.DestroyImmediate(nose.GetComponent<BoxCollider>());
            nose.transform.SetParent(body.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.45f, 0.45f);
            nose.transform.localScale = new Vector3(0.5f, 0.15f, 0.2f);

            var rig = new GameObject("CameraRig");
            rig.transform.SetParent(root.transform, false);
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(rig.transform, false);
            cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            var orbit = rig.AddComponent<OrbitCameraRig>();
            rig.SetActive(false);

            using (var serialized = new SerializedObject(orbit))
            {
                serialized.FindProperty("target").objectReferenceValue = root.transform;
                serialized.FindProperty("cameraTransform").objectReferenceValue = cameraObject.transform;
                serialized.FindProperty("lookAction").objectReferenceValue = Action("Look");
                serialized.FindProperty("zoomAction").objectReferenceValue = Action("Zoom");
                serialized.FindProperty("switchViewAction").objectReferenceValue = Action("SwitchCamera");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            using (var serialized = new SerializedObject(avatar))
            {
                serialized.FindProperty("controller").objectReferenceValue = controller;
                serialized.FindProperty("cameraRig").objectReferenceValue = orbit;
                serialized.FindProperty("body").objectReferenceValue = body.GetComponent<Renderer>();
                serialized.FindProperty("moveAction").objectReferenceValue = Action("Move");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return PrefabUtility.SaveAsPrefabAsset(root, PlayerPath).GetComponent<NetworkObject>();
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static SinglePrefabObjects BuildCatalog(NetworkObject bridge, NetworkObject player)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        using (var serialized = new SerializedObject(catalog))
        {
            var prefabs = serialized.FindProperty("_prefabs");
            prefabs.ClearArray();
            prefabs.arraySize = 2;
            prefabs.GetArrayElementAtIndex(0).objectReferenceValue = player;
            prefabs.GetArrayElementAtIndex(1).objectReferenceValue = bridge;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    private static void BuildScene(SinglePrefabObjects catalog, NetworkObject bridge, NetworkObject player, PanelSettings panelSettings,
        EquipmentDefinition[] equipment, RecipeAsset[] recipeAssets, OfferAsset[] offerAssets, ItemDefinition[] items, Material ghostMaterial, Material ghostModelMaterial,
        GameObject[] beltPrefabs, Material tread, Material itemSprite)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var lightObject = new GameObject("Directional Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(4f, 1f, 4f);
        // Static reference blocks so movement and camera orbit are visible in captures.
        for (var index = 0; index < 4; index++)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = $"Landmark {index + 1}";
            var angle = index * Mathf.PI / 2f;
            block.transform.position = new Vector3(Mathf.Cos(angle) * 8f, 0.5f, Mathf.Sin(angle) * 8f);
        }

        var managerObject = new GameObject("NetworkManager");
        var manager = managerObject.AddComponent<NetworkManager>();
        var tugboat = Ensure<Tugboat>(managerObject);
        var transport = Ensure<TransportManager>(managerObject);
        transport.Transport = tugboat;
        var server = Ensure<ServerManager>(managerObject);
        var authenticator = managerObject.AddComponent<DevAuthenticator>();
        manager.SpawnablePrefabs = catalog;
        // SessionRoot lives in this scene and references the manager, so the manager must share the scene's lifetime.
        using (var serialized = new SerializedObject(manager))
        {
            serialized.FindProperty("_dontDestroyOnLoad").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        using (var serialized = new SerializedObject(server))
        {
            serialized.FindProperty("_authenticator").objectReferenceValue = authenticator;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        var sessionObject = new GameObject("SessionRoot");
        var session = sessionObject.AddComponent<SessionRoot>();
        var spawnRoot = new GameObject("SpawnPoints").transform;
        spawnRoot.SetParent(sessionObject.transform, false);
        var spawns = new Transform[4];
        for (var index = 0; index < spawns.Length; index++)
        {
            spawns[index] = new GameObject($"Spawn {index + 1}").transform;
            spawns[index].SetParent(spawnRoot, false);
            spawns[index].position = new Vector3(-3f + index * 2f, 0.05f, 0f);
        }
        using (var serialized = new SerializedObject(session))
        {
            serialized.FindProperty("networkManager").objectReferenceValue = manager;
            serialized.FindProperty("authenticator").objectReferenceValue = authenticator;
            serialized.FindProperty("bridgePrefab").objectReferenceValue = bridge;
            serialized.FindProperty("playerPrefab").objectReferenceValue = player;
            var spawnProperty = serialized.FindProperty("spawnPoints");
            spawnProperty.arraySize = spawns.Length;
            for (var index = 0; index < spawns.Length; index++)
                spawnProperty.GetArrayElementAtIndex(index).objectReferenceValue = spawns[index];
            var definitions = serialized.FindProperty("equipmentDefinitions");
            definitions.arraySize = equipment.Length;
            for (var index = 0; index < equipment.Length; index++)
                definitions.GetArrayElementAtIndex(index).objectReferenceValue = equipment[index];
            var recipes = serialized.FindProperty("recipes");
            recipes.arraySize = recipeAssets.Length;
            for (var index = 0; index < recipeAssets.Length; index++)
                recipes.GetArrayElementAtIndex(index).objectReferenceValue = recipeAssets[index];
            var offersProperty = serialized.FindProperty("offers");
            offersProperty.arraySize = offerAssets.Length;
            for (var index = 0; index < offerAssets.Length; index++)
                offersProperty.GetArrayElementAtIndex(index).objectReferenceValue = offerAssets[index];
            var itemsProperty = serialized.FindProperty("items");
            itemsProperty.arraySize = items.Length;
            for (var index = 0; index < items.Length; index++)
                itemsProperty.GetArrayElementAtIndex(index).objectReferenceValue = items[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        var presenterObject = new GameObject("EquipmentPresenter");
        var presenter = presenterObject.AddComponent<EquipmentPresenter>();
        using (var serialized = new SerializedObject(presenter))
        {
            serialized.FindProperty("session").objectReferenceValue = session;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        var beltObject = new GameObject("BeltPresenter");
        var belts = beltObject.AddComponent<BeltPresenter>();
        using (var serialized = new SerializedObject(belts))
        {
            serialized.FindProperty("session").objectReferenceValue = session;
            serialized.FindProperty("straightPrefab").objectReferenceValue = beltPrefabs[0];
            serialized.FindProperty("leftCornerPrefab").objectReferenceValue = beltPrefabs[1];
            serialized.FindProperty("rightCornerPrefab").objectReferenceValue = beltPrefabs[2];
            serialized.FindProperty("treadMaterial").objectReferenceValue = tread;
            serialized.FindProperty("itemMaterial").objectReferenceValue = itemSprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        var interactionObject = new GameObject("EquipmentInteraction");
        var interaction = interactionObject.AddComponent<EquipmentInteraction>();
        var ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ghost.name = "PlacementGhost";
        // The ghost must never intercept the pickup raycast or block movement.
        Object.DestroyImmediate(ghost.GetComponent<BoxCollider>());
        ghost.transform.SetParent(interactionObject.transform, false);
        var ghostRenderer = ghost.GetComponent<MeshRenderer>();
        ghostRenderer.sharedMaterial = ghostMaterial;
        ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ghost.SetActive(false);
        using (var serialized = new SerializedObject(interaction))
        {
            serialized.FindProperty("session").objectReferenceValue = session;
            serialized.FindProperty("ghost").objectReferenceValue = ghostRenderer;
            serialized.FindProperty("ghostModelMaterial").objectReferenceValue = ghostModelMaterial;
            serialized.FindProperty("placeAction").objectReferenceValue = Action("Place");
            serialized.FindProperty("removeAction").objectReferenceValue = Action("Remove");
            serialized.FindProperty("rotateAction").objectReferenceValue = Action("Rotate");
            serialized.FindProperty("pointAction").objectReferenceValue = Action("Point");
            serialized.FindProperty("inventoryAction").objectReferenceValue = Action("Inventory");
            serialized.FindProperty("clearCursorAction").objectReferenceValue = Action("ClearCursor");
            serialized.FindProperty("closeScreenAction").objectReferenceValue = Action("CloseScreen");
            serialized.FindProperty("hotbarAction").objectReferenceValue = Action("Hotbar");
            serialized.FindProperty("quickTransferAction").objectReferenceValue = Action("QuickTransfer");
            serialized.FindProperty("placeItemAction").objectReferenceValue = Action("PlaceItem");
            serialized.FindProperty("takeItemAction").objectReferenceValue = Action("TakeItem");
            serialized.FindProperty("belts").objectReferenceValue = belts;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        var panelObject = new GameObject("SessionPanel");
        var document = panelObject.AddComponent<UIDocument>();
        // The UIDocument.panelSettings setter does not serialize in edit mode; write the field directly.
        using (var serialized = new SerializedObject(document))
        {
            serialized.FindProperty("m_PanelSettings").objectReferenceValue = panelSettings;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        var panel = panelObject.AddComponent<SessionPanel>();
        using (var serialized = new SerializedObject(panel))
        {
            serialized.FindProperty("document").objectReferenceValue = document;
            serialized.FindProperty("session").objectReferenceValue = session;
            serialized.FindProperty("equipment").objectReferenceValue = interaction;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // The HUD has its own document on the same panel, drawn above the session readout.
        var hudObject = new GameObject("PlayerHud");
        var hudDocument = hudObject.AddComponent<UIDocument>();
        using (var serialized = new SerializedObject(hudDocument))
        {
            serialized.FindProperty("m_PanelSettings").objectReferenceValue = panelSettings;
            serialized.FindProperty("m_SortingOrder").floatValue = 1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        var hud = hudObject.AddComponent<PlayerHud>();
        using (var serialized = new SerializedObject(hud))
        {
            serialized.FindProperty("document").objectReferenceValue = hudDocument;
            serialized.FindProperty("interaction").objectReferenceValue = interaction;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
    }
}
