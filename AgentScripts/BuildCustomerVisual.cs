// Builds the presentation-only customer prefab from the project's animated character, without its worker or network components.
using System;
using UnityEditor;
using UnityEngine;

public static class BuildCustomerVisual
{
    public static string Run()
    {
        var employee = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Employee/Employee.prefab");
        if (employee == null) throw new InvalidOperationException("Employee prefab is missing.");
        var source = employee.GetComponentInChildren<Animator>(true);
        if (source == null) throw new InvalidOperationException("Employee model Animator is missing.");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Customers"))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Customers");
        var root = new GameObject("Customer");
        try
        {
            var model = UnityEngine.Object.Instantiate(source.gameObject, root.transform, false);
            model.name = "Model";
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var part = renderer.name;
                if (part.Contains("CarryBox") || part.Contains("Apron") || part.Contains("IDBadge")
                    || part.Contains("SafetyStripe") || part.Contains("Cap"))
                    renderer.enabled = false;
            }
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Customers/Customer.prefab");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
        AssetDatabase.SaveAssets();
        return "Built Assets/Prefabs/Customers/Customer.prefab";
    }
}
