// Installs employees in the world-generation scene (decision 0039): SessionRoot.employeePrefab (so the server spawns saved
// employees and hires new ones), the employee prefab in WorldGen's spawnable-prefab catalog, and the employee script screen (its
// own UI document on the HUD's panel settings, drawn above the HUD), as BuildSampleScene.cs does for SampleScene. Generated
// lots get their NavMesh at runtime (SiteNavigation), so nothing is baked. Idempotent: an existing catalog entry or panel is kept.
// Re-run after BuildWorldGenScene.cs, which recopies the scene from DevSite without them; the vehicle and restaurant content
// scripts leave them alone. Run with: unity command run_script --file AgentScripts/InstallWorldGenEmployees.cs
// --entry InstallWorldGenEmployees.Run, with no unsaved scene open. The scene that was open is reopened at the end.
using System.Linq;
using UnityEngine;

public static class InstallWorldGenEmployees
{
    public static object Run()
    {
        const string scenePath = "Assets/Scenes/WorldGen.unity";
        const string catalogPath = "Assets/Network/WorldGenPrefabs.asset";
        const string prefabPath = "Assets/Prefabs/Employee/Employee.prefab";
        var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;

        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        // Loaded after opening the scene, which unloads assets loaded before it.
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) throw new System.InvalidOperationException("Employee prefab is missing; run BuildSampleScene.cs first.");
        var worker = prefab.GetComponent<FoodFactoryGame.Session.Employees.EmployeeWorker>();
        var networkObject = prefab.GetComponent<FishNet.Object.NetworkObject>();

        var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.SinglePrefabObjects>(catalogPath);
        if (catalog == null) throw new System.InvalidOperationException("WorldGen prefab catalog is missing; run BuildWorldGenScene.cs first.");
        var addedToCatalog = false;
        using (var serialized = new UnityEditor.SerializedObject(catalog))
        {
            var prefabs = serialized.FindProperty("_prefabs");
            var present = Enumerable.Range(0, prefabs.arraySize).Any(i => prefabs.GetArrayElementAtIndex(i).objectReferenceValue == networkObject);
            if (!present)
            {
                prefabs.arraySize++;
                prefabs.GetArrayElementAtIndex(prefabs.arraySize - 1).objectReferenceValue = networkObject;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                UnityEditor.EditorUtility.SetDirty(catalog);
                addedToCatalog = true;
            }
        }

        var session = Object.FindAnyObjectByType<FoodFactoryGame.Session.SessionRoot>();
        using (var serialized = new UnityEditor.SerializedObject(session))
        {
            serialized.FindProperty("employeePrefab").objectReferenceValue = worker;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        var addedPanel = false;
        if (Object.FindAnyObjectByType<FoodFactoryGame.Session.Employees.EmployeeScriptPanel>() == null)
        {
            var hud = Object.FindAnyObjectByType<FoodFactoryGame.Session.Equipment.PlayerHud>();
            var interaction = Object.FindAnyObjectByType<FoodFactoryGame.Session.Equipment.EquipmentInteraction>();
            var hudDocument = hud.GetComponent<UnityEngine.UIElements.UIDocument>();
            var panelObject = new GameObject("EmployeeScriptPanel");
            var panelDocument = panelObject.AddComponent<UnityEngine.UIElements.UIDocument>();
            panelDocument.panelSettings = hudDocument.panelSettings;
            panelDocument.sortingOrder = hudDocument.sortingOrder + 1;
            var panel = panelObject.AddComponent<FoodFactoryGame.Session.Employees.EmployeeScriptPanel>();
            using (var serialized = new UnityEditor.SerializedObject(panel))
            {
                serialized.FindProperty("document").objectReferenceValue = panelDocument;
                serialized.FindProperty("interaction").objectReferenceValue = interaction;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            addedPanel = true;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        UnityEditor.AssetDatabase.SaveAssets();
        if (!string.IsNullOrEmpty(previousScene) && previousScene != scenePath)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(previousScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
        return "WorldGen employees installed: catalog entry " + (addedToCatalog ? "added" : "kept") + ", panel " + (addedPanel ? "added" : "kept");
    }
}
