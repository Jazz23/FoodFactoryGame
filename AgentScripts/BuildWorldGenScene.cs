// Authors the world-generation scene (decisions 0026, 0028): WorldGen.unity is a copy of DevSite plus the world layout bridge on
// SessionRoot (so a newly created world gets a generated layout) and a WorldLayoutPresenter that draws the replicated layout
// with the world art (ArtSource/World, installed by BuildWorldArt.cs). WorldGen uses its own spawnable-prefab catalog (DevSite's prefabs plus the layout bridge), so DevSite
// and GamePrefabs are untouched. DevSite's floor, landmarks and NavMesh are handed to the presenter to show only for dev-site
// worlds, a PropertyPanel (buy panel, decision 0028) and a SiteNavigation (runtime NavMesh, decision 0030) are added. Not a build scene. Idempotent: the scene is recopied from DevSite on every run; asset GUIDs of
// the bridge prefab and catalog are kept. The scene that was open is reopened at the end.
// Run the body of Run() with the Unity MCP execute_code tool (C# 6 / CodeDom compatible, no helper methods).
using UnityEngine;

public static class BuildWorldGenScene
{
    public static object Run()
    {
        const string devSitePath = "Assets/Scenes/DevSite.unity";
        const string scenePath = "Assets/Scenes/WorldGen.unity";
        const string devCatalogPath = "Assets/Network/GamePrefabs.asset";
        const string catalogPath = "Assets/Network/WorldGenPrefabs.asset";
        const string bridgePath = "Assets/Prefabs/Network/WorldLayoutBridge.prefab";
        // World art catalog from AgentScripts/BuildWorldArt.cs (run that first).
        const string artPath = "Assets/Art/World/WorldArtCatalog.asset";
        var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;

        // Loaded untyped: the value only goes into a serialized reference.
        var art = UnityEditor.AssetDatabase.LoadMainAssetAtPath(artPath);

        // Layout bridge prefab: a NetworkObject carrying only WorldLayoutBridge.
        var bridgeRoot = new GameObject("WorldLayoutBridge");
        bridgeRoot.AddComponent<FishNet.Object.NetworkObject>();
        bridgeRoot.AddComponent<FoodFactoryGame.Goods.Network.WorldLayoutBridge>();
        var bridge = UnityEditor.PrefabUtility.SaveAsPrefabAsset(bridgeRoot, bridgePath).GetComponent<FishNet.Object.NetworkObject>();
        UnityEngine.Object.DestroyImmediate(bridgeRoot);

        // Catalog: DevSite's prefabs in their order, then the layout bridge.
        var devCatalog = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.SinglePrefabObjects>(devCatalogPath);
        var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.SinglePrefabObjects>(catalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<FishNet.Managing.Object.SinglePrefabObjects>();
            UnityEditor.AssetDatabase.CreateAsset(catalog, catalogPath);
        }
        var devPrefabs = new UnityEditor.SerializedObject(devCatalog).FindProperty("_prefabs");
        var serializedCatalog = new UnityEditor.SerializedObject(catalog);
        var prefabs = serializedCatalog.FindProperty("_prefabs");
        prefabs.ClearArray();
        prefabs.arraySize = devPrefabs.arraySize + 1;
        for (var index = 0; index < devPrefabs.arraySize; index++)
            prefabs.GetArrayElementAtIndex(index).objectReferenceValue = devPrefabs.GetArrayElementAtIndex(index).objectReferenceValue;
        prefabs.GetArrayElementAtIndex(devPrefabs.arraySize).objectReferenceValue = bridge;
        serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
        UnityEditor.EditorUtility.SetDirty(catalog);
        UnityEditor.AssetDatabase.SaveAssets();

        // Scene: a fresh copy of DevSite, then the layout bridge, catalog and presenter.
        if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(scenePath) != null) UnityEditor.AssetDatabase.DeleteAsset(scenePath);
        UnityEditor.AssetDatabase.CopyAsset(devSitePath, scenePath);
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        var manager = UnityEngine.Object.FindFirstObjectByType<FishNet.Managing.NetworkManager>();
        manager.SpawnablePrefabs = catalog;
        UnityEditor.EditorUtility.SetDirty(manager);
        var session = UnityEngine.Object.FindFirstObjectByType<FoodFactoryGame.Session.SessionRoot>();
        var serializedSession = new UnityEditor.SerializedObject(session);
        serializedSession.FindProperty("worldLayoutBridgePrefab").objectReferenceValue = bridge;
        // Its own save folder: hosting WorldGen must never open the DevSite world, which can never gain a layout.
        serializedSession.FindProperty("saveFolder").stringValue = "worldgen";
        serializedSession.ApplyModifiedPropertiesWithoutUndo();
        var presenterObject = new GameObject("WorldLayoutPresenter");
        var presenter = presenterObject.AddComponent<FoodFactoryGame.Session.WorldMap.WorldLayoutPresenter>();
        var serializedPresenter = new UnityEditor.SerializedObject(presenter);
        serializedPresenter.FindProperty("session").objectReferenceValue = session;
        // Reloaded here: opening the scene unloads assets loaded before it, which would leave the reference empty.
        art = UnityEditor.AssetDatabase.LoadMainAssetAtPath(artPath);
        serializedPresenter.FindProperty("art").objectReferenceValue = art;
        // DevSite's floor, landmarks and baked NavMesh belong to the dev site: the presenter shows them only for worlds that keep
        // it (no layout, or layout format 1 or 2). A generated world's ground is the levelled terrain with its lots paved.
        var devOnly = new System.Collections.Generic.List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Floor" || root.name.StartsWith("Landmark") || root.GetComponent<Unity.AI.Navigation.NavMeshSurface>() != null)
                devOnly.Add(root);
        var devOnlyProperty = serializedPresenter.FindProperty("devSiteOnly");
        devOnlyProperty.arraySize = devOnly.Count;
        for (var index = 0; index < devOnly.Count; index++) devOnlyProperty.GetArrayElementAtIndex(index).objectReferenceValue = devOnly[index];
        serializedPresenter.ApplyModifiedPropertiesWithoutUndo();

        // Buy panel (decision 0028): a UI document like the logistics panel's, reading the presenter's offers.
        var logistics = UnityEngine.Object.FindFirstObjectByType<FoodFactoryGame.Session.Logistics.LogisticsPanel>();
        var logisticsDocument = logistics.GetComponent<UnityEngine.UIElements.UIDocument>();
        var interaction = UnityEngine.Object.FindFirstObjectByType<FoodFactoryGame.Session.Equipment.EquipmentInteraction>();
        var panelObject = new GameObject("PropertyPanel");
        var panelDocument = panelObject.AddComponent<UnityEngine.UIElements.UIDocument>();
        panelDocument.panelSettings = logisticsDocument.panelSettings;
        panelDocument.sortingOrder = logisticsDocument.sortingOrder;
        var panel = panelObject.AddComponent<FoodFactoryGame.Session.WorldMap.PropertyPanel>();
        var serializedPanel = new UnityEditor.SerializedObject(panel);
        serializedPanel.FindProperty("document").objectReferenceValue = panelDocument;
        serializedPanel.FindProperty("interaction").objectReferenceValue = interaction;
        serializedPanel.FindProperty("map").objectReferenceValue = presenter;
        serializedPanel.ApplyModifiedPropertiesWithoutUndo();

        // Runtime NavMesh over the drawn generated lot and its street (decision 0030), replacing DevSite's baked one there.
        var navigation = new GameObject("SiteNavigation").AddComponent<FoodFactoryGame.Session.Customers.SiteNavigation>();
        var serializedNavigation = new UnityEditor.SerializedObject(navigation);
        serializedNavigation.FindProperty("session").objectReferenceValue = session;
        serializedNavigation.ApplyModifiedPropertiesWithoutUndo();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

        if (!string.IsNullOrEmpty(previousScene) && previousScene != scenePath)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previousScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
        return "WorldGen scene built: catalog " + prefabs.arraySize + " prefabs, bridge " + bridgePath;
    }
}
