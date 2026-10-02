// Editor authoring script (decision 0034), run with `unity command run_script --file AgentScripts/BuildRestaurantContent.cs
// --entry BuildRestaurantContent.Run` after BuildDevSite.cs (and before BuildWorldGenScene.cs if that is re-run). Makes every
// model of the restaurant art kit (Assets/Art/Restaurant/Models) buildable by the player:
// - the 23 wall, doorway, frame, door-leaf, window and serving-hatch models become the RestaurantStyleCatalog that draws the
//   wall finishes, doors and windows players build (BuildingPresenter);
// - the other 36 models become equipment: a prefab (the model, one box collider (a trigger for decor that must not block
//   walking) and, for lamps, a soft point light), an EquipmentDefinition with its layer, mount, seats, storage and PROTOTYPE
//   ambience points, a supplier offer at a PROTOTYPE price, and an icon rendered from the model;
// - the register (the existing counter, decision 0034) and the four-seat table get display names;
// - DevSite and WorldGen get the new definitions and offers on SessionRoot, the style catalog on BuildingPresenter, and a
//   BuildMode with its own UI document and the Player/Build and Player/BuildConfirm actions.
// Idempotent: assets keep their GUIDs, scene objects are found and updated, lists are not duplicated. The scene that was open
// is reopened at the end.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FoodFactoryGame.Session;
using FoodFactoryGame.Session.Buildings;
using FoodFactoryGame.Session.Equipment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public static class BuildRestaurantContent
{
    private const string ModelRoot = "Assets/Art/Restaurant/Models";
    private const string PrefabFolder = "Assets/Prefabs/Restaurant";
    private const string DefinitionFolder = "Assets/Content/Restaurant";
    private const string OfferFolder = "Assets/Content/Offers/Restaurant";
    private const string IconFolder = "Assets/Art/Icons/Restaurant";
    private const string CatalogPath = "Assets/Content/Restaurant/RestaurantStyles.asset";
    private const string InputPath = "Assets/InputSystem_Actions.inputactions";
    private const string CounterDefinitionPath = "Assets/Content/Equipment/Counter.asset";
    private const string TableDefinitionPath = "Assets/Content/Equipment/Table.asset";
    private const string CellMaterialPath = "Assets/Materials/PlacementGhost.mat";
    private const string GhostModelMaterialPath = "Assets/Materials/EquipmentGhost.mat";
    private static readonly string[] Scenes = { "Assets/Scenes/DevSite.unity", "Assets/Scenes/WorldGen.unity" };

    // One buildable piece. PROTOTYPE values (decision 0034 PROPOSALS): price, ambience points, seats, storage slots, footprints.
    private sealed class Piece
    {
        public string Model, Kind, Name, Category, Layer = "";
        public EquipmentMount Mount;
        public float MountHeight = 1.5f;
        public int Width = 1, Depth = 1, Seats, Storage = 1, Ambience;
        public long Price;
        public bool Light, HideFromAbove, Opens;
        public float LightHeight;
    }

    private static Piece P(string model, string kind, string name, string category, int width, int depth, long price, int ambience,
        string layer = "", EquipmentMount mount = EquipmentMount.Floor, float mountHeight = 1.5f, int seats = 0, int storage = 1,
        bool light = false, float lightHeight = 0f, bool hideFromAbove = false) => new()
    {
        Model = model, Kind = kind, Name = name, Category = category, Width = width, Depth = depth, Price = price, Ambience = ambience,
        Layer = layer, Mount = mount, MountHeight = mountHeight, Seats = seats, Storage = storage, Light = light, LightHeight = lightHeight,
        HideFromAbove = hideFromAbove, Opens = seats > 0 || storage > 1
    };

    private static readonly Piece[] Pieces =
    {
        // Furniture: tables supply seats (decision 0034); chairs, the banquette and the bench are seating decor.
        P("Furniture/RT_Table_BistroRound", "rt-table-bistro", "Bistro table (2 seats)", "Furniture", 1, 1, 6000, 2, seats: 2),
        P("Furniture/RT_Table_HighRound", "rt-table-high", "High table (2 seats)", "Furniture", 1, 1, 7000, 2, seats: 2),
        P("Furniture/RT_Table_Communal", "rt-table-communal", "Communal table (6 seats)", "Furniture", 3, 1, 15000, 3, seats: 6),
        P("Furniture/RT_Chair_Spindle", "rt-chair-spindle", "Spindle chair", "Furniture", 1, 1, 2500, 1),
        P("Furniture/RT_Chair_Upholstered", "rt-chair-upholstered", "Upholstered chair", "Furniture", 1, 1, 3500, 2),
        P("Furniture/RT_Chair_BarStool", "rt-chair-stool", "Bar stool", "Furniture", 1, 1, 3000, 1),
        P("Furniture/RT_Booth_Straight_2m", "rt-booth", "Banquette", "Furniture", 2, 1, 18000, 4),
        P("Furniture/RT_Bench_Slatted_1_6m", "rt-bench", "Slatted bench", "Furniture", 2, 1, 9000, 2),
        // Decor.
        P("Decor/RT_Plant_Floor", "rt-plant-floor", "Floor plant", "Decor", 1, 1, 6000, 4),
        P("Decor/RT_Planter_Divider_1_6m", "rt-planter", "Planter divider", "Decor", 2, 1, 12000, 4),
        P("Decor/RT_CoatStand", "rt-coat-stand", "Coat stand", "Decor", 1, 1, 4000, 2),
        P("Decor/RT_MenuBoard_AFrame", "rt-menu-aframe", "A-frame menu board", "Decor", 1, 1, 3000, 2),
        P("Decor/RT_WasteBin", "rt-waste-bin", "Waste bin", "Decor", 1, 1, 1500, 0),
        P("Decor/RT_Rug_Bordered_2x3m", "rt-rug", "Bordered rug", "Decor", 2, 3, 9000, 4, "floor"),
        P("Decor/RT_MenuBoard_Wall", "rt-menu-wall", "Wall menu board", "Decor", 1, 1, 5000, 3, "wall", EquipmentMount.Wall, 1.5f),
        P("Decor/RT_WallArt_Abstract", "rt-wall-art", "Abstract wall art", "Decor", 1, 1, 7000, 4, "wall", EquipmentMount.Wall, 1.6f),
        P("Decor/RT_WallClock", "rt-wall-clock", "Wall clock", "Decor", 1, 1, 3500, 2, "wall", EquipmentMount.Wall, 2.1f),
        P("Decor/RT_Plant_Table", "rt-plant-table", "Table plant", "Decor", 1, 1, 1500, 1, "tabletop", EquipmentMount.Tabletop),
        P("Decor/RT_Table_Caddy", "rt-caddy", "Table caddy", "Decor", 1, 1, 800, 1, "tabletop", EquipmentMount.Tabletop),
        P("Decor/RT_Vase_Ceramic", "rt-vase", "Ceramic vase", "Decor", 1, 1, 2000, 1, "tabletop", EquipmentMount.Tabletop),
        // Lighting (the kit has no light sources; the prefabs add a soft point light).
        P("Lighting/RT_Light_Pendant", "rt-pendant", "Pendant light", "Lighting", 1, 1, 6000, 3, "ceiling", EquipmentMount.Ceiling, light: true, lightHeight: -0.75f),
        P("Lighting/RT_Light_CeilingStrip", "rt-ceiling-strip", "Ceiling strip light", "Lighting", 1, 1, 5000, 1, "ceiling", EquipmentMount.Ceiling, light: true, lightHeight: -0.15f),
        P("Lighting/RT_Light_Sconce", "rt-sconce", "Wall sconce", "Lighting", 1, 1, 4500, 2, "wall", EquipmentMount.Wall, 1.9f, light: true, lightHeight: 0.25f),
        P("Lighting/RT_Light_FloorLamp", "rt-floor-lamp", "Floor lamp", "Lighting", 1, 1, 7000, 3, light: true, lightHeight: 1.45f),
        // Surfaces: floor finishes lie flush under everything else; ceiling panels are hidden when looking down into the room.
        P("Surfaces/RT_Floor_Checker_1m", "rt-floor-checker", "Checker floor (1 m²)", "Surfaces", 1, 1, 1200, 1, "floor", EquipmentMount.Flush),
        P("Surfaces/RT_Floor_Terracotta_1m", "rt-floor-terracotta", "Terracotta floor (1 m²)", "Surfaces", 1, 1, 1200, 1, "floor", EquipmentMount.Flush),
        P("Surfaces/RT_Floor_OakPlank_1m", "rt-floor-oak", "Oak plank floor (1 m²)", "Surfaces", 1, 1, 1400, 1, "floor", EquipmentMount.Flush),
        P("Surfaces/RT_Floor_KitchenTile_1m", "rt-floor-kitchen", "Kitchen tile floor (1 m²)", "Surfaces", 1, 1, 1000, 1, "floor", EquipmentMount.Flush),
        P("Surfaces/RT_Ceiling_Panel_1m", "rt-ceiling-panel", "Ceiling panel (1 m²)", "Surfaces", 1, 1, 1000, 1, "ceiling", EquipmentMount.Ceiling, hideFromAbove: true),
        // Architecture pieces that are furnishings, not structure: room dividers and a wall-top cornice.
        P("Architecture/RT_Partition_HalfHeight_2m", "rt-partition-half", "Half-height partition", "Architecture", 2, 1, 9000, 2),
        P("Architecture/RT_Partition_Slatted_1m", "rt-partition-slatted", "Slatted screen", "Architecture", 1, 1, 6000, 2),
        P("Architecture/RT_Cornice_1m", "rt-cornice", "Cornice (1 m)", "Architecture", 1, 1, 1500, 1, "wall", EquipmentMount.Wall, 2.91f),
        // Service: back-of-house storage and fittings.
        P("Service/RT_StorageShelf_1_2m", "rt-shelf", "Storage shelf", "Service", 2, 1, 10000, 0, storage: 8),
        P("Service/RT_PrepTable_1_5m", "rt-prep-table", "Prep table", "Service", 2, 1, 15000, 0, storage: 4),
        P("Service/RT_Trolley_TwoShelf", "rt-trolley", "Service trolley", "Service", 1, 1, 6000, 0, storage: 2),
        P("Service/RT_Sink_Single", "rt-sink", "Sink", "Service", 2, 1, 20000, 0)
    };

    public static string Run()
    {
        foreach (var folder in new[] { PrefabFolder, DefinitionFolder, OfferFolder, IconFolder }) Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
        var catalog = BuildCatalog();
        var definitions = new List<EquipmentDefinition>();
        var offers = new List<OfferAsset>();
        foreach (var piece in Pieces)
        {
            var name = Path.GetFileName(piece.Model);
            var prefab = BuildPrefab(piece, name);
            var icon = BuildIcon(prefab, name);
            var definition = BuildDefinition(piece, name, prefab, icon);
            definitions.Add(definition);
            offers.Add(BuildOffer(piece, name, definition));
        }
        Rename(CounterDefinitionPath, "Register", "");
        Rename(TableDefinitionPath, "Table for four", "");
        AssetDatabase.SaveAssets();
        var previous = EditorSceneManager.GetActiveScene().path;
        // Opening a scene unloads assets loaded before it, so the installer reloads them by path in each scene.
        var definitionPaths = definitions.Select(AssetDatabase.GetAssetPath).ToList();
        var offerPaths = offers.Select(AssetDatabase.GetAssetPath).ToList();
        var installed = Scenes.Where(x => File.Exists(x)).Select(x => Install(x, definitionPaths, offerPaths)).ToList();
        if (!string.IsNullOrEmpty(previous) && !Scenes.Contains(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        var models = AssetDatabase.FindAssets("t:Model", new[] { ModelRoot }).Length;
        return $"Restaurant content: {definitions.Count} pieces, catalog {CatalogModels(catalog)} models, {models} kit models; scenes {string.Join(", ", installed)}";
    }

    private static GameObject Model(string relative) =>
        AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/{relative}.fbx") ?? throw new FileNotFoundException(relative);

    private static RestaurantStyleCatalog BuildCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<RestaurantStyleCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<RestaurantStyleCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        using var serialized = new SerializedObject(catalog);
        var walls = serialized.FindProperty("wallFinishes");
        var finishes = new[] { ("plaster", "Plaster"), ("brick", "Exposed brick"), ("wainscot", "Teal wainscot"), ("tile", "Kitchen tile") };
        walls.arraySize = finishes.Length;
        for (var i = 0; i < finishes.Length; i++)
        {
            var (style, display) = finishes[i];
            var title = char.ToUpperInvariant(style[0]) + style.Substring(1);
            var entry = walls.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("style").stringValue = style;
            entry.FindPropertyRelative("displayName").stringValue = display;
            entry.FindPropertyRelative("wall1m").objectReferenceValue = Model($"Architecture/RT_Wall_{title}_1m");
            entry.FindPropertyRelative("wall2m").objectReferenceValue = Model($"Architecture/RT_Wall_{title}_2m");
            entry.FindPropertyRelative("end").objectReferenceValue = Model($"Architecture/RT_WallEnd_{title}");
        }
        var leaves = serialized.FindProperty("doorLeaves");
        var doors = new[] { ("panel", "Panel door", "Panel"), ("glazed", "Glazed door", "Glazed"), ("kitchen", "Kitchen swing door", "Kitchen") };
        leaves.arraySize = doors.Length;
        for (var i = 0; i < doors.Length; i++)
        {
            var entry = leaves.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("style").stringValue = doors[i].Item1;
            entry.FindPropertyRelative("displayName").stringValue = doors[i].Item2;
            entry.FindPropertyRelative("leaf").objectReferenceValue = Model($"Architecture/RT_DoorLeaf_{doors[i].Item3}");
        }
        var windows = serialized.FindProperty("windowStyles");
        var types = new[] { ("picture", "Picture window", "Architecture/RT_WindowOpening_2m", "Architecture/RT_Window_Picture"),
            ("mullioned", "Mullioned window", "Architecture/RT_WindowOpening_2m", "Architecture/RT_Window_Mullioned"),
            ("hatch", "Serving hatch", "Architecture/RT_ServingHatch_2m", null) };
        windows.arraySize = types.Length;
        for (var i = 0; i < types.Length; i++)
        {
            var entry = windows.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("style").stringValue = types[i].Item1;
            entry.FindPropertyRelative("displayName").stringValue = types[i].Item2;
            entry.FindPropertyRelative("bay").objectReferenceValue = Model(types[i].Item3);
            entry.FindPropertyRelative("window").objectReferenceValue = types[i].Item4 == null ? null : Model(types[i].Item4);
        }
        serialized.FindProperty("singleDoorway").objectReferenceValue = Model("Architecture/RT_Doorway_Single_2m");
        serialized.FindProperty("doubleDoorway").objectReferenceValue = Model("Architecture/RT_Doorway_Double_3m");
        serialized.FindProperty("singleFrame").objectReferenceValue = Model("Architecture/RT_DoorFrame_Single");
        serialized.FindProperty("doubleFrame").objectReferenceValue = Model("Architecture/RT_DoorFrame_Double");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
        return catalog;
    }

    private static int CatalogModels(RestaurantStyleCatalog catalog) =>
        catalog.WallFinishes.SelectMany(x => new Object[] { x.wall1m, x.wall2m, x.end })
            .Concat(catalog.DoorLeaves.Select(x => (Object)x.leaf))
            .Concat(catalog.WindowStyles.SelectMany(x => new Object[] { x.bay, x.window }))
            .Concat(new Object[] { catalog.SingleDoorway, catalog.DoubleDoorway, catalog.SingleFrame, catalog.DoubleFrame })
            .Where(x => x != null).Distinct().Count();

    // The model under a root, one box collider around it (a trigger for decor off the object layer) and a lamp's light.
    private static GameObject BuildPrefab(Piece piece, string name)
    {
        var root = new GameObject(name);
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(Model(piece.Model));
            model.transform.SetParent(root.transform, false);
            var renderers = model.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = Vector3.Max(bounds.size, new Vector3(0.05f, 0.05f, 0.05f));
            collider.isTrigger = !string.IsNullOrEmpty(piece.Layer);
            if (piece.Light)
            {
                var lamp = new GameObject("Light");
                lamp.transform.SetParent(root.transform, false);
                lamp.transform.localPosition = new Vector3(0f, piece.LightHeight, piece.Mount == EquipmentMount.Wall ? 0.2f : 0f);
                var light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 4.5f;
                light.intensity = 1.6f;
                light.color = new Color(1f, 0.84f, 0.62f);
                light.shadows = LightShadows.None;
            }
            return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{name}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // A 128 px icon of the model, three-quarter view on a transparent background, rendered in a preview scene.
    private static Sprite BuildIcon(GameObject prefab, string name)
    {
        const int size = 128;
        var path = $"{IconFolder}/{name}.png";
        var preview = EditorSceneManager.NewPreviewScene();
        var texture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
            foreach (var light in instance.GetComponentsInChildren<Light>()) light.enabled = false;
            var renderers = instance.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Icon camera", HideFlags.HideAndDontSave, typeof(Camera));
            SceneManagerMove(cameraObject, preview);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(bounds.extents.magnitude, 0.1f) * 1.05f;
            camera.transform.position = bounds.center + new Vector3(-1f, 0.9f, 1.2f).normalized * 10f;
            camera.transform.LookAt(bounds.center);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.targetTexture = texture;
            for (var i = 0; i < 2; i++)
            {
                var lightObject = EditorUtility.CreateGameObjectWithHideFlags("Icon light", HideFlags.HideAndDontSave, typeof(Light));
                SceneManagerMove(lightObject, preview);
                var light = lightObject.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = i == 0 ? 1.4f : 0.7f;
                light.color = i == 0 ? new Color(1f, 0.93f, 0.84f) : new Color(0.82f, 0.9f, 1f);
                light.transform.rotation = Quaternion.Euler(i == 0 ? 50f : 25f, i == 0 ? 130f : -60f, 0f);
            }
            camera.Render();
            RenderTexture.active = texture;
            image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null;
        }
        finally
        {
            RenderTexture.active = previous;
            if (image != null) Object.DestroyImmediate(image);
            EditorSceneManager.ClosePreviewScene(preview);
            texture.Release();
            Object.DestroyImmediate(texture);
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void SceneManagerMove(GameObject gameObject, UnityEngine.SceneManagement.Scene scene) =>
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject, scene);

    private static EquipmentDefinition BuildDefinition(Piece piece, string name, GameObject prefab, Sprite icon)
    {
        var path = $"{DefinitionFolder}/{name}.asset";
        var definition = AssetDatabase.LoadAssetAtPath<EquipmentDefinition>(path);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<EquipmentDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }
        using var serialized = new SerializedObject(definition);
        serialized.FindProperty("kind").stringValue = piece.Kind;
        serialized.FindProperty("width").intValue = piece.Width;
        serialized.FindProperty("depth").intValue = piece.Depth;
        serialized.FindProperty("inputCapacity").intValue = piece.Storage;
        serialized.FindProperty("outputCapacity").intValue = 1;
        serialized.FindProperty("inputRefrigerated").boolValue = false;
        serialized.FindProperty("outputRefrigerated").boolValue = false;
        serialized.FindProperty("seats").intValue = piece.Seats;
        serialized.FindProperty("visualPrefab").objectReferenceValue = prefab;
        serialized.FindProperty("icon").objectReferenceValue = icon;
        serialized.FindProperty("layer").stringValue = piece.Layer;
        serialized.FindProperty("ambience").intValue = piece.Ambience;
        serialized.FindProperty("displayName").stringValue = piece.Name;
        serialized.FindProperty("category").stringValue = piece.Category;
        serialized.FindProperty("mount").enumValueIndex = (int)piece.Mount;
        serialized.FindProperty("mountHeight").floatValue = piece.MountHeight;
        serialized.FindProperty("hideFromAbove").boolValue = piece.HideFromAbove;
        serialized.FindProperty("opensScreen").boolValue = piece.Opens;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static OfferAsset BuildOffer(Piece piece, string name, EquipmentDefinition definition)
    {
        var path = $"{OfferFolder}/{name}.asset";
        var offer = AssetDatabase.LoadAssetAtPath<OfferAsset>(path);
        if (offer == null)
        {
            offer = ScriptableObject.CreateInstance<OfferAsset>();
            AssetDatabase.CreateAsset(offer, path);
        }
        using var serialized = new SerializedObject(offer);
        serialized.FindProperty("id").stringValue = "supplier-" + piece.Kind;
        serialized.FindProperty("equipment").objectReferenceValue = definition;
        serialized.FindProperty("truck").objectReferenceValue = null;
        serialized.FindProperty("itemId").stringValue = "";
        serialized.FindProperty("quantity").intValue = 1;
        serialized.FindProperty("priceCents").intValue = (int)piece.Price;
        serialized.FindProperty("spoilAfterSeconds").longValue = 1;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(offer);
        return offer;
    }

    private static void Rename(string path, string display, string category)
    {
        var definition = AssetDatabase.LoadAssetAtPath<EquipmentDefinition>(path);
        if (definition == null) return;
        using var serialized = new SerializedObject(definition);
        serialized.FindProperty("displayName").stringValue = display;
        serialized.FindProperty("category").stringValue = category;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
    }

    private static InputActionReference Action(string name) =>
        AssetDatabase.LoadAllAssetsAtPath(InputPath).OfType<InputActionReference>().FirstOrDefault(x => x.action != null && x.action.actionMap.name + "/" + x.action.name == name)
        ?? throw new System.InvalidOperationException($"Input action {name} has no reference; reimport {InputPath}.");

    private static string Install(string scenePath, List<string> definitionPaths, List<string> offerPaths)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var catalog = AssetDatabase.LoadAssetAtPath<RestaurantStyleCatalog>(CatalogPath);
        var definitions = definitionPaths.Select(AssetDatabase.LoadAssetAtPath<EquipmentDefinition>).ToList();
        var offers = offerPaths.Select(AssetDatabase.LoadAssetAtPath<OfferAsset>).ToList();
        var session = Object.FindAnyObjectByType<SessionRoot>();
        using (var serialized = new SerializedObject(session))
        {
            Append(serialized.FindProperty("equipmentDefinitions"), definitions.Cast<Object>());
            Append(serialized.FindProperty("offers"), offers.Cast<Object>());
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        var buildings = Object.FindAnyObjectByType<BuildingPresenter>();
        using (var serialized = new SerializedObject(buildings))
        {
            serialized.FindProperty("restaurantStyles").objectReferenceValue = catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        var interaction = Object.FindAnyObjectByType<EquipmentInteraction>();
        var mode = Object.FindAnyObjectByType<BuildMode>(FindObjectsInactive.Include);
        if (mode == null)
        {
            var host = new GameObject("BuildMode");
            host.AddComponent<UIDocument>();
            mode = host.AddComponent<BuildMode>();
        }
        var document = mode.GetComponent<UIDocument>();
        // The session readout's panel, so the document's sorting order puts the build panel over that readout.
        var readout = Object.FindAnyObjectByType<SessionPanel>().GetComponent<UIDocument>();
        document.panelSettings = readout.panelSettings;
        // Above the HUD, the session readout and the other panels: the build panel covers the left of the screen.
        document.sortingOrder = 10;
        using (var serialized = new SerializedObject(mode))
        {
            serialized.FindProperty("session").objectReferenceValue = session;
            serialized.FindProperty("interaction").objectReferenceValue = interaction;
            serialized.FindProperty("buildings").objectReferenceValue = buildings;
            serialized.FindProperty("document").objectReferenceValue = document;
            serialized.FindProperty("cellMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(CellMaterialPath);
            serialized.FindProperty("ghostModelMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(GhostModelMaterialPath);
            serialized.FindProperty("buildAction").objectReferenceValue = Action("Player/Build");
            serialized.FindProperty("confirmAction").objectReferenceValue = Action("Player/BuildConfirm");
            serialized.FindProperty("placeAction").objectReferenceValue = Action("Player/Place");
            serialized.FindProperty("removeAction").objectReferenceValue = Action("Player/Remove");
            serialized.FindProperty("rotateAction").objectReferenceValue = Action("Player/Rotate");
            serialized.FindProperty("pointAction").objectReferenceValue = Action("Player/Point");
            serialized.FindProperty("gridColor").colorValue = new Color(1f, 1f, 1f, 0.4f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"{Path.GetFileNameWithoutExtension(scenePath)} ({session.EquipmentDefinitions.Count} definitions, {session.Offers.Count} offers)";
    }

    // Appends the values that are not in the list yet, keeping the existing order; empty entries (left by an earlier run that
    // lost its references) are dropped first.
    private static void Append(SerializedProperty list, IEnumerable<Object> values)
    {
        for (var index = list.arraySize - 1; index >= 0; index--)
            if (list.GetArrayElementAtIndex(index).objectReferenceValue == null) list.DeleteArrayElementAtIndex(index);
        var present = new HashSet<Object>(Enumerable.Range(0, list.arraySize).Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue));
        foreach (var value in values.Where(x => !present.Contains(x)))
        {
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = value;
        }
    }
}
