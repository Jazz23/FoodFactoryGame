// Bakes DevSite's walkable floor for local customer paths; runtime building and equipment obstacles carve it.
using System;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class BuildDevSiteNavigation
{
    public static string Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/DevSite.unity") throw new InvalidOperationException("Open DevSite first.");
        var navigation = GameObject.Find("Navigation");
        if (navigation == null) navigation = new GameObject("Navigation");
        var surface = navigation.GetComponent<NavMeshSurface>();
        if (surface == null) surface = navigation.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
        const string folder = "Assets/Scenes/DevSite";
        const string path = folder + "/NavMesh-Navigation.asset";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Scenes", "DevSite");
        var asset = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
        if (asset == null) AssetDatabase.CreateAsset(surface.navMeshData, path);
        else EditorUtility.CopySerialized(surface.navMeshData, asset);
        var serialized = new SerializedObject(surface);
        serialized.FindProperty("m_NavMeshData").objectReferenceValue = AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return "Baked DevSite walkable NavMesh to " + path;
    }
}
