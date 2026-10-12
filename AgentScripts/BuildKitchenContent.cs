// Editor authoring script (decision 0043), run with `unity command run_script --file AgentScripts/BuildKitchenContent.cs
// --entry BuildKitchenContent.Run --timeout 600` with no unsaved scene open, after ArtSource/Kitchen/build_kitchen_models.py
// has exported Export/Kitchen.fbx and AgentScripts/DrawFoodIcons.ps1 has drawn the icons, and again after BuildDevSite.cs (and
// after BuildRestaurantContent.cs, which it does not touch). It:
// - copies the export to Assets/Art/Kitchen/Models/Kitchen.fbx with each KT_* material remapped to a URP Lit material, and
//   makes a prefab per station in Assets/Prefabs/Equipment (the mesh under a root at the floor, one box collider);
// - authors the item definitions, the mixer, stove, fryer, grill and kitchen counter definitions and supplier offers, every
//   recipe, and the FoodBalance asset with every food price (ingredient packs and dishes), and gives the oven 3 input and
//   2 output slots for its multi-ingredient recipes;
// - in DevSite, SampleScene and WorldGen, adds them to the SessionRoot and sets its food balance. The old sell-bread recipe and
//   dough offer assets are replaced by FoodBalance rows with the same IDs, so they are taken out of the scenes and deleted.
// Idempotent: assets keep their GUIDs and lists are not duplicated. The scene that was open is reopened at the end.
// Every number here is a PROTOTYPE default listed in decision 0043; prices are edited in Assets/Content/FoodBalance.asset.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FoodFactoryGame.Session;
using FoodFactoryGame.Session.Equipment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildKitchenContent
{
    private const string Source = "ArtSource/Kitchen/Export/Kitchen.fbx";
    private const string ModelPath = "Assets/Art/Kitchen/Models/Kitchen.fbx";
    private const string MaterialFolder = "Assets/Art/Kitchen/Materials";
    private const string PrefabFolder = "Assets/Prefabs/Equipment";
    private const string IconFolder = "Assets/Art/Icons";
    private const string ItemFolder = "Assets/Content/Items";
    private const string RecipeFolder = "Assets/Content/Recipes";
    private const string EquipmentFolder = "Assets/Content/Equipment";
    private const string OfferFolder = "Assets/Content/Offers";
    private const string BalancePath = "Assets/Content/FoodBalance.asset";
    private const string OvenPath = "Assets/Content/Equipment/Oven.asset";
    private static readonly string[] Retired = { "Assets/Content/Recipes/SellBread.asset", "Assets/Content/Offers/Dough5.asset" };
    private static readonly string[] Scenes = { "Assets/Scenes/DevSite.unity", "Assets/Scenes/SampleScene.unity", "Assets/Scenes/WorldGen.unity" };

    // name: colour, smoothness, metallic, emission
    private static readonly Dictionary<string, (Color Colour, float Smoothness, float Metallic, float Emission)> Materials = new()
    {
        ["KT_Steel"] = (new Color(0.62f, 0.65f, 0.68f), 0.65f, 0.85f, 0f),
        ["KT_DarkSteel"] = (new Color(0.16f, 0.17f, 0.19f), 0.55f, 0.7f, 0f),
        ["KT_Black"] = (new Color(0.03f, 0.03f, 0.035f), 0.3f, 0.1f, 0f),
        ["KT_Enamel"] = (new Color(0.05f, 0.36f, 0.38f), 0.65f, 0.3f, 0f),
        ["KT_Cream"] = (new Color(0.86f, 0.8f, 0.64f), 0.6f, 0.1f, 0f),
        ["KT_Accent"] = (new Color(0.93f, 0.32f, 0.07f), 0.65f, 0.2f, 0f),
        ["KT_Wood"] = (new Color(0.45f, 0.28f, 0.15f), 0.3f, 0f, 0f),
        ["KT_Board"] = (new Color(0.8f, 0.62f, 0.4f), 0.25f, 0f, 0f),
        ["KT_Glass"] = (new Color(0.1f, 0.14f, 0.17f), 0.9f, 0.5f, 0f),
        ["KT_Flame"] = (new Color(1f, 0.42f, 0.05f), 0.7f, 0f, 2.5f),
        ["KT_Oil"] = (new Color(0.92f, 0.66f, 0.12f), 0.85f, 0f, 0.3f),
        ["KT_Food"] = (new Color(0.55f, 0.3f, 0.13f), 0.2f, 0f, 0f),
        ["KT_Tomato"] = (new Color(0.8f, 0.12f, 0.08f), 0.6f, 0f, 0f),
        ["KT_Leaf"] = (new Color(0.25f, 0.6f, 0.18f), 0.4f, 0f, 0f),
    };

    // Items: ID, shown name, icon and asset file name. Every food item stacks to 20, like dough and bread.
    private static readonly (string Id, string Name, string File)[] Items =
    {
        ("flour", "Flour", "Flour"), ("potato", "Potato", "Potato"), ("tomato", "Tomato", "Tomato"), ("lettuce", "Lettuce", "Lettuce"),
        ("milk", "Milk", "Milk"), ("egg", "Egg", "Egg"), ("beef", "Beef", "Beef"), ("chicken", "Chicken", "Chicken"),
        ("cheese", "Cheese", "Cheese"), ("fish", "Fish", "Fish"),
        ("cut-potatoes", "Cut Potatoes", "CutPotatoes"), ("fries", "Fries", "Fries"), ("tomato-sauce", "Tomato Sauce", "TomatoSauce"),
        ("tomato-soup", "Tomato Soup", "TomatoSoup"), ("bun", "Bun", "Bun"), ("patty", "Patty", "Patty"),
        ("cooked-patty", "Cooked Patty", "CookedPatty"), ("chopped-lettuce", "Chopped Lettuce", "ChoppedLettuce"),
        ("pizza-base", "Pizza Base", "PizzaBase"), ("pasta", "Pasta", "Pasta"), ("ground-beef", "Ground Beef", "GroundBeef"),
        ("bolognese", "Bolognese Sauce", "Bolognese"), ("breading", "Breading", "Breading"), ("fried-chicken", "Fried Chicken", "FriedChicken"),
        ("batter", "Batter", "Batter"), ("fried-fish", "Fried Fish", "FriedFish"),
        ("pancakes", "Pancakes", "Pancakes"), ("grilled-cheese", "Grilled Cheese", "GrilledCheese"), ("soup-and-bread", "Soup & Bread", "SoupAndBread"),
        ("cheeseburger", "Cheeseburger", "Cheeseburger"), ("pizza", "Margherita Pizza", "Pizza"), ("spaghetti", "Spaghetti Bolognese", "Spaghetti"),
        ("chicken-and-fries", "Chicken & Fries", "ChickenAndFries"), ("fish-and-chips", "Fish & Chips", "FishAndChips"), ("lasagna", "Lasagna", "Lasagna"),
    };

    // Stations: kind, shown name, asset/prefab/icon name, model object, footprint, buffer slots, power switch, worker-bound,
    // supplier price in cents.
    private static readonly (string Kind, string Name, string File, string Model, int Width, int Depth, int In, int Out, bool Power, bool Worker, int Price)[] Stations =
    {
        ("mixer", "Mixer", "Mixer", "Kitchen_Mixer", 1, 1, 3, 2, true, false, 12000),
        ("stove", "Stove", "Stove", "Kitchen_Stove", 2, 1, 2, 2, true, false, 18000),
        ("fryer", "Fryer", "Fryer", "Kitchen_Fryer", 1, 1, 2, 2, true, false, 14000),
        ("grill", "Grill", "Grill", "Kitchen_Grill", 2, 1, 2, 2, true, false, 16000),
        ("kitchen-counter", "Kitchen Counter", "KitchenCounter", "Kitchen_Counter", 2, 1, 4, 2, false, true, 9000),
    };

    // Recipes: ID, asset name, station kind, seconds, inputs (item x quantity), output item, output quantity, seconds the output
    // stays edible outside refrigeration.
    private static readonly (string Id, string File, string Kind, int Seconds, (string Item, int Quantity)[] Inputs, string Output, int Quantity, int Spoil)[] Recipes =
    {
        ("mixer-dough", "MixerDough", "mixer", 6, new[] { ("flour", 1) }, "dough", 1, 7200),
        ("mixer-pasta", "MixerPasta", "mixer", 6, new[] { ("flour", 1), ("egg", 1) }, "pasta", 1, 1440),
        ("mixer-breading", "MixerBreading", "mixer", 6, new[] { ("flour", 1), ("egg", 1) }, "breading", 2, 1440),
        ("mixer-batter", "MixerBatter", "mixer", 6, new[] { ("flour", 1), ("egg", 1), ("milk", 1) }, "batter", 2, 720),
        ("stove-tomato-sauce", "StoveTomatoSauce", "stove", 10, new[] { ("tomato", 2) }, "tomato-sauce", 1, 1440),
        ("stove-tomato-soup", "StoveTomatoSoup", "stove", 8, new[] { ("tomato-sauce", 1), ("milk", 1) }, "tomato-soup", 1, 900),
        ("stove-bolognese", "StoveBolognese", "stove", 12, new[] { ("ground-beef", 1), ("tomato-sauce", 1) }, "bolognese", 1, 720),
        ("stove-spaghetti", "StoveSpaghetti", "stove", 8, new[] { ("pasta", 1), ("bolognese", 1) }, "spaghetti", 1, 900),
        ("stove-pancakes", "StovePancakes", "stove", 8, new[] { ("batter", 1) }, "pancakes", 1, 900),
        ("fryer-fries", "FryerFries", "fryer", 6, new[] { ("cut-potatoes", 1) }, "fries", 1, 900),
        ("fryer-fried-chicken", "FryerFriedChicken", "fryer", 8, new[] { ("chicken", 1), ("breading", 1) }, "fried-chicken", 1, 900),
        ("fryer-fried-fish", "FryerFriedFish", "fryer", 8, new[] { ("fish", 1), ("batter", 1) }, "fried-fish", 1, 900),
        ("grill-cooked-patty", "GrillCookedPatty", "grill", 8, new[] { ("patty", 1) }, "cooked-patty", 1, 900),
        ("grill-grilled-cheese", "GrillGrilledCheese", "grill", 6, new[] { ("bread", 1), ("cheese", 1) }, "grilled-cheese", 1, 900),
        ("oven-bun", "OvenBun", "oven", 10, new[] { ("dough", 1) }, "bun", 1, 3600),
        ("oven-pizza", "OvenPizza", "oven", 12, new[] { ("pizza-base", 1), ("tomato-sauce", 1), ("cheese", 1) }, "pizza", 1, 1200),
        ("oven-lasagna", "OvenLasagna", "oven", 20, new[] { ("pasta", 1), ("bolognese", 1), ("cheese", 1) }, "lasagna", 1, 1800),
        ("kitchen-cut-potatoes", "KitchenCutPotatoes", "kitchen-counter", 4, new[] { ("potato", 1) }, "cut-potatoes", 2, 1440),
        ("kitchen-patty", "KitchenPatty", "kitchen-counter", 4, new[] { ("beef", 1) }, "patty", 1, 240),
        ("kitchen-ground-beef", "KitchenGroundBeef", "kitchen-counter", 4, new[] { ("beef", 1) }, "ground-beef", 1, 240),
        ("kitchen-chopped-lettuce", "KitchenChoppedLettuce", "kitchen-counter", 4, new[] { ("lettuce", 1) }, "chopped-lettuce", 2, 720),
        ("kitchen-pizza-base", "KitchenPizzaBase", "kitchen-counter", 4, new[] { ("dough", 1) }, "pizza-base", 1, 1440),
        ("kitchen-soup-and-bread", "KitchenSoupAndBread", "kitchen-counter", 6, new[] { ("tomato-soup", 1), ("bread", 1) }, "soup-and-bread", 1, 900),
        ("kitchen-cheeseburger", "KitchenCheeseburger", "kitchen-counter", 6,
            new[] { ("bun", 1), ("cooked-patty", 1), ("cheese", 1), ("chopped-lettuce", 1) }, "cheeseburger", 1, 900),
        ("kitchen-chicken-and-fries", "KitchenChickenAndFries", "kitchen-counter", 6, new[] { ("fried-chicken", 1), ("fries", 1) }, "chicken-and-fries", 1, 900),
        ("kitchen-fish-and-chips", "KitchenFishAndChips", "kitchen-counter", 6, new[] { ("fried-fish", 1), ("fries", 1) }, "fish-and-chips", 1, 900),
    };

    // Ingredient packs of five: item, price of the pack in cents, seconds edible outside refrigeration (one game hour is 60 s).
    private static readonly (string Item, int PackCents, long Spoil)[] Supplies =
    {
        ("dough", 250, 7200), ("flour", 200, 10080), ("potato", 150, 4320), ("tomato", 200, 1440), ("lettuce", 200, 1440),
        ("milk", 250, 720), ("egg", 150, 720), ("cheese", 500, 720), ("beef", 750, 240), ("chicken", 600, 240), ("fish", 750, 240),
    };

    // Dishes on the menu: item, shown name, price in cents, tier, cuisine.
    private static readonly (string Item, string Name, int Cents, int Tier, string Cuisine)[] Dishes =
    {
        ("bread", "Bread", 250, 1, "bakery"), ("fries", "Fries", 200, 1, "fast-food"), ("pancakes", "Pancakes", 300, 1, "bakery"),
        ("grilled-cheese", "Grilled Cheese", 350, 1, "grill"), ("soup-and-bread", "Soup & Bread", 400, 1, "bakery"),
        ("pizza", "Margherita Pizza", 700, 2, "fast-food"), ("chicken-and-fries", "Chicken & Fries", 750, 2, "fast-food"),
        ("cheeseburger", "Cheeseburger", 800, 2, "grill"), ("spaghetti", "Spaghetti Bolognese", 850, 2, "noodles"),
        ("fish-and-chips", "Fish & Chips", 900, 2, "fast-food"), ("lasagna", "Lasagna", 1300, 3, "fine-dining"),
    };

    public static string Run()
    {
        if (Enumerable.Range(0, EditorSceneManager.sceneCount).Any(i => EditorSceneManager.GetSceneAt(i).isDirty))
            return "Refused: save or discard the open scene's changes first.";
        foreach (var folder in new[] { Path.GetDirectoryName(ModelPath), MaterialFolder, PrefabFolder, ItemFolder, RecipeFolder, EquipmentFolder, OfferFolder })
            Directory.CreateDirectory(folder);
        File.Copy(Source, ModelPath, true);
        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);

        var prefabs = BuildPrefabs();
        var items = Items.Select(x => BuildItem(x.Id, x.Name, x.File)).ToList();
        var definitions = new List<EquipmentDefinition>();
        var offers = new List<OfferAsset>();
        foreach (var station in Stations)
        {
            var definition = BuildDefinition(station, prefabs[station.Model]);
            definitions.Add(definition);
            offers.Add(BuildOffer(station.File + "1", "supplier-" + station.Kind, definition, station.Price));
        }
        var recipes = Recipes.Select(BuildRecipe).ToList();
        var balance = BuildBalance();
        WidenOven();
        AssetDatabase.SaveAssets();

        var previous = EditorSceneManager.GetActiveScene().path;
        // Opening a scene unloads unreferenced assets, so each scene loads them again by path.
        var paths = new Dictionary<string, List<string>>
        {
            ["items"] = items.Select(AssetDatabase.GetAssetPath).ToList(),
            ["equipmentDefinitions"] = definitions.Select(AssetDatabase.GetAssetPath).ToList(),
            ["offers"] = offers.Select(AssetDatabase.GetAssetPath).ToList(),
            ["recipes"] = recipes.Select(AssetDatabase.GetAssetPath).ToList(),
        };
        var installed = Scenes.Where(File.Exists).Select(x => Install(x, paths)).ToList();
        foreach (var path in Retired.Where(x => AssetDatabase.LoadAssetAtPath<Object>(x) != null)) AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        return $"Kitchen: {prefabs.Count} prefabs, {items.Count} items, {definitions.Count} stations, {recipes.Count} recipes, "
            + $"{Supplies.Length} supplies, {Dishes.Length} dishes; scenes {string.Join(", ", installed)}";
    }

    private static Dictionary<string, GameObject> BuildPrefabs()
    {
        var materials = new Dictionary<string, Material>();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var (name, spec) in Materials)
        {
            var path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", spec.Colour);
            material.SetFloat("_Smoothness", spec.Smoothness);
            material.SetFloat("_Metallic", spec.Metallic);
            if (spec.Emission > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                material.SetColor("_EmissionColor", spec.Colour * spec.Emission);
            }
            else material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            materials[name] = material;
        }
        AssetDatabase.SaveAssets();

        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        foreach (var (name, material) in materials) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
        importer.SaveAndReimport();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var prefabs = new Dictionary<string, GameObject>();
        foreach (Transform child in model.transform)
        {
            var filter = child.GetComponent<MeshFilter>();
            var renderer = child.GetComponent<MeshRenderer>();
            var station = Stations.FirstOrDefault(x => x.Model == child.name);
            if (filter == null || renderer == null || station.Model == null) continue;
            var root = new GameObject(station.File);
            var part = new GameObject("Model");
            part.transform.SetParent(root.transform, false);
            part.transform.localRotation = child.localRotation;
            part.transform.localScale = child.localScale;
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var drawn = part.AddComponent<MeshRenderer>();
            drawn.sharedMaterials = renderer.sharedMaterials;
            drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            // One box on the root lets aim rays name the station and keeps players out of it.
            var bounds = drawn.bounds;
            var box = root.AddComponent<BoxCollider>();
            box.center = bounds.center - root.transform.position;
            box.size = bounds.size;
            prefabs[child.name] = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{station.File}.prefab");
            Object.DestroyImmediate(root);
        }
        var missing = Stations.Where(x => !prefabs.ContainsKey(x.Model)).Select(x => x.Model).ToList();
        if (missing.Count > 0) throw new System.InvalidOperationException($"Kitchen.fbx lacks {string.Join(", ", missing)}.");
        return prefabs;
    }

    private static Sprite ImportIcon(string name)
    {
        var path = $"{IconFolder}/{name}.png";
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) throw new System.InvalidOperationException($"Missing {path}; run DrawFoodIcons.ps1.");
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static T Load<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static ItemDefinition BuildItem(string id, string name, string file)
    {
        var item = Load<ItemDefinition>($"{ItemFolder}/{file}.asset");
        using (var serialized = new SerializedObject(item))
        {
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = name;
            serialized.FindProperty("icon").objectReferenceValue = ImportIcon(file);
            serialized.FindProperty("maxStack").intValue = 20;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(item);
        return item;
    }

    private static EquipmentDefinition BuildDefinition(
        (string Kind, string Name, string File, string Model, int Width, int Depth, int In, int Out, bool Power, bool Worker, int Price) station,
        GameObject prefab)
    {
        var definition = Load<EquipmentDefinition>($"{EquipmentFolder}/{station.File}.asset");
        using (var serialized = new SerializedObject(definition))
        {
            serialized.FindProperty("kind").stringValue = station.Kind;
            serialized.FindProperty("width").intValue = station.Width;
            serialized.FindProperty("depth").intValue = station.Depth;
            serialized.FindProperty("inputCapacity").intValue = station.In;
            serialized.FindProperty("outputCapacity").intValue = station.Out;
            serialized.FindProperty("inputRefrigerated").boolValue = false;
            serialized.FindProperty("outputRefrigerated").boolValue = false;
            serialized.FindProperty("seats").intValue = 0;
            serialized.FindProperty("visualPrefab").objectReferenceValue = prefab;
            serialized.FindProperty("icon").objectReferenceValue = ImportIcon(station.File);
            serialized.FindProperty("displayName").stringValue = station.Name;
            serialized.FindProperty("category").stringValue = "";
            serialized.FindProperty("manualPower").boolValue = station.Power;
            serialized.FindProperty("workerBound").boolValue = station.Worker;
            serialized.FindProperty("opensScreen").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static OfferAsset BuildOffer(string file, string id, EquipmentDefinition definition, int priceCents)
    {
        var offer = Load<OfferAsset>($"{OfferFolder}/{file}.asset");
        using (var serialized = new SerializedObject(offer))
        {
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("equipment").objectReferenceValue = definition;
            serialized.FindProperty("truck").objectReferenceValue = null;
            serialized.FindProperty("itemId").stringValue = "";
            serialized.FindProperty("quantity").intValue = 1;
            serialized.FindProperty("priceCents").intValue = priceCents;
            serialized.FindProperty("spoilAfterSeconds").longValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(offer);
        return offer;
    }

    private static RecipeAsset BuildRecipe(
        (string Id, string File, string Kind, int Seconds, (string Item, int Quantity)[] Inputs, string Output, int Quantity, int Spoil) spec)
    {
        var recipe = Load<RecipeAsset>($"{RecipeFolder}/{spec.File}.asset");
        using (var serialized = new SerializedObject(recipe))
        {
            serialized.FindProperty("id").stringValue = spec.Id;
            // Dough and bread are the dev items (BuildDevSite.cs).
            var name = Items.FirstOrDefault(x => x.Id == spec.Output).Name;
            serialized.FindProperty("displayName").stringValue = name ?? char.ToUpperInvariant(spec.Output[0]) + spec.Output.Substring(1);
            serialized.FindProperty("stationKind").stringValue = spec.Kind;
            serialized.FindProperty("durationSeconds").intValue = spec.Seconds;
            var inputs = serialized.FindProperty("inputs");
            inputs.arraySize = spec.Inputs.Length;
            for (var index = 0; index < spec.Inputs.Length; index++)
            {
                inputs.GetArrayElementAtIndex(index).FindPropertyRelative("itemId").stringValue = spec.Inputs[index].Item;
                inputs.GetArrayElementAtIndex(index).FindPropertyRelative("quantity").intValue = spec.Inputs[index].Quantity;
            }
            serialized.FindProperty("outputItemId").stringValue = spec.Output;
            serialized.FindProperty("outputQuantity").intValue = spec.Quantity;
            serialized.FindProperty("outputSpoilAfterSeconds").intValue = spec.Spoil;
            serialized.FindProperty("saleCents").intValue = 0;
            serialized.FindProperty("tier").intValue = 1;
            serialized.FindProperty("cuisine").stringValue = "";
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(recipe);
        return recipe;
    }

    private static FoodBalance BuildBalance()
    {
        var balance = Load<FoodBalance>(BalancePath);
        using (var serialized = new SerializedObject(balance))
        {
            var supplies = serialized.FindProperty("supplies");
            supplies.arraySize = Supplies.Length;
            for (var index = 0; index < Supplies.Length; index++)
            {
                var row = supplies.GetArrayElementAtIndex(index);
                row.FindPropertyRelative("itemId").stringValue = Supplies[index].Item;
                row.FindPropertyRelative("packQuantity").intValue = 5;
                row.FindPropertyRelative("packPriceCents").intValue = Supplies[index].PackCents;
                row.FindPropertyRelative("spoilAfterSeconds").longValue = Supplies[index].Spoil;
            }
            var dishes = serialized.FindProperty("dishes");
            dishes.arraySize = Dishes.Length;
            for (var index = 0; index < Dishes.Length; index++)
            {
                var row = dishes.GetArrayElementAtIndex(index);
                row.FindPropertyRelative("itemId").stringValue = Dishes[index].Item;
                row.FindPropertyRelative("displayName").stringValue = Dishes[index].Name;
                row.FindPropertyRelative("saleCents").intValue = Dishes[index].Cents;
                row.FindPropertyRelative("tier").intValue = Dishes[index].Tier;
                row.FindPropertyRelative("cuisine").stringValue = Dishes[index].Cuisine;
                row.FindPropertyRelative("serveSeconds").intValue = 5;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(balance);
        return balance;
    }

    // Pizza and lasagna need three different ingredients in the oven, and it now makes several things.
    private static void WidenOven()
    {
        var oven = AssetDatabase.LoadAssetAtPath<EquipmentDefinition>(OvenPath);
        using var serialized = new SerializedObject(oven);
        serialized.FindProperty("inputCapacity").intValue = 3;
        serialized.FindProperty("outputCapacity").intValue = 2;
        serialized.FindProperty("displayName").stringValue = "Oven";
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(oven);
    }

    private static string Install(string scenePath, Dictionary<string, List<string>> paths)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var session = Object.FindAnyObjectByType<SessionRoot>();
        if (session == null) return $"{Path.GetFileNameWithoutExtension(scenePath)} (no SessionRoot)";
        using (var serialized = new SerializedObject(session))
        {
            foreach (var (list, assets) in paths)
                Replace(serialized.FindProperty(list), assets.Select(AssetDatabase.LoadAssetAtPath<Object>).ToList());
            serialized.FindProperty("foodBalance").objectReferenceValue = AssetDatabase.LoadAssetAtPath<FoodBalance>(BalancePath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return Path.GetFileNameWithoutExtension(scenePath);
    }

    // Drops the retired assets (and missing references) from a list and appends what it lacks.
    private static void Replace(SerializedProperty list, List<Object> added)
    {
        if (added.Any(x => x == null)) throw new System.InvalidOperationException($"A {list.name} asset failed to load.");
        for (var index = list.arraySize - 1; index >= 0; index--)
        {
            var current = list.GetArrayElementAtIndex(index).objectReferenceValue;
            if (current == null || Retired.Contains(AssetDatabase.GetAssetPath(current))) list.DeleteArrayElementAtIndex(index);
        }
        var present = Enumerable.Range(0, list.arraySize).Select(x => list.GetArrayElementAtIndex(x).objectReferenceValue).ToList();
        foreach (var asset in added.Where(x => !present.Contains(x)))
        {
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = asset;
        }
    }
}
