// Editor authoring script (run via Unity MCP run_script): creates the dev session prefabs, prefab catalog, equipment content,
// UI panel settings and the DevSite scene, then makes DevSite the only build scene. Safe to re-run: assets keep GUIDs.
// The scene holds no equipment instances; placed equipment is shown from replicated state by EquipmentPresenter.
using System.IO;
using System.Linq;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Server;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using FoodFactoryGame.Goods.Network;
using FoodFactoryGame.Session;
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

    public static string Run()
    {
        foreach (var folder in new[] { "Assets/UI", "Assets/Prefabs/Network", "Assets/Prefabs/Player", "Assets/Network", "Assets/Content/Equipment", "Assets/Materials" })
            Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
        var panelSettings = BuildPanelSettings();
        var bridge = BuildBridge();
        var player = BuildPlayer();
        var catalog = BuildCatalog(bridge, player);
        var oven = BuildOvenDefinition();
        var ghostMaterial = BuildGhostMaterial();
        BuildScene(catalog, bridge, player, panelSettings, oven, ghostMaterial);
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
    private static EquipmentDefinition BuildOvenDefinition()
    {
        var definition = AssetDatabase.LoadAssetAtPath<EquipmentDefinition>(OvenDefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<EquipmentDefinition>();
            AssetDatabase.CreateAsset(definition, OvenDefinitionPath);
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OvenPrefabPath);
        if (prefab == null) throw new System.InvalidOperationException($"Missing {OvenPrefabPath}.");
        using (var serialized = new SerializedObject(definition))
        {
            serialized.FindProperty("kind").stringValue = "oven";
            serialized.FindProperty("width").intValue = 3;
            serialized.FindProperty("depth").intValue = 3;
            serialized.FindProperty("inputCapacity").intValue = 10;
            serialized.FindProperty("outputCapacity").intValue = 4;
            serialized.FindProperty("outputRefrigerated").boolValue = false;
            serialized.FindProperty("visualPrefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(definition);
        return definition;
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
                serialized.FindProperty("orbitAction").objectReferenceValue = Action("Orbit");
                serialized.FindProperty("zoomAction").objectReferenceValue = Action("Zoom");
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
        EquipmentDefinition oven, Material ghostMaterial)
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
            definitions.arraySize = 1;
            definitions.GetArrayElementAtIndex(0).objectReferenceValue = oven;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        var presenterObject = new GameObject("EquipmentPresenter");
        var presenter = presenterObject.AddComponent<EquipmentPresenter>();
        using (var serialized = new SerializedObject(presenter))
        {
            serialized.FindProperty("session").objectReferenceValue = session;
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
            serialized.FindProperty("interactAction").objectReferenceValue = Action("Interact");
            serialized.FindProperty("placeAction").objectReferenceValue = Action("Place");
            serialized.FindProperty("rotateAction").objectReferenceValue = Action("Rotate");
            serialized.FindProperty("pointAction").objectReferenceValue = Action("Point");
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

        EditorSceneManager.SaveScene(scene, ScenePath);
    }
}
