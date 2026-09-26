// Adds the customer visual presenter to DevSite and binds its required scene and prefab references.
using System;
using FoodFactoryGame.Session;
using FoodFactoryGame.Session.Customers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class InstallCustomerPresenter
{
    public static string Run()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/DevSite.unity") throw new InvalidOperationException("Open DevSite first.");
        var session = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Customers/Customer.prefab");
        if (session == null || prefab == null) throw new InvalidOperationException("Required session or customer prefab is missing.");
        var presenter = UnityEngine.Object.FindAnyObjectByType<CustomerPresenter>();
        if (presenter == null) presenter = new GameObject("CustomerPresenter").AddComponent<CustomerPresenter>();
        var serialized = new SerializedObject(presenter);
        serialized.FindProperty("session").objectReferenceValue = session;
        serialized.FindProperty("customerPrefab").objectReferenceValue = prefab;
        serialized.FindProperty("maxVisible").intValue = 100;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Installed CustomerPresenter in DevSite with a 100-character visible cap.";
    }
}
