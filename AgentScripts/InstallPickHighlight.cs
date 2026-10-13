// Installs the employee-task pick highlight (owner feedback 2026-10-12): creates PickTint.mat (FoodFactory/XRayTint, light green,
// see-through) and gives every scene's EquipmentInteraction its pick tint and, where it was missing (WorldGen, DevSite), the
// hover outline material. Run in Edit mode with run_script; it reopens WorldGen at the end.
using System.Text;
using FoodFactoryGame.Session.Equipment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class InstallPickHighlight
{
    const string TintPath = "Assets/Materials/Highlight/PickTint.mat";
    const string OutlinePath = "Assets/Materials/Employee/EmployeeOutline.mat";

    public static string Run()
    {
        var log = new StringBuilder();
        if (AssetDatabase.LoadAssetAtPath<Material>(TintPath) == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials/Highlight")) AssetDatabase.CreateFolder("Assets/Materials", "Highlight");
            var shader = Shader.Find("FoodFactory/XRayTint");
            if (shader == null) return "FoodFactory/XRayTint not found";
            var material = new Material(shader) { name = "PickTint" };
            material.SetColor("_Color", new Color(0.55f, 1f, 0.6f, 0.28f));
            AssetDatabase.CreateAsset(material, TintPath);
            AssetDatabase.SaveAssets();
            log.AppendLine("created " + TintPath);
        }
        foreach (var scenePath in new[] { "Assets/Scenes/DevSite.unity", "Assets/Scenes/SampleScene.unity", "Assets/Scenes/WorldGen.unity" })
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var tint = AssetDatabase.LoadAssetAtPath<Material>(TintPath);
            var outline = AssetDatabase.LoadAssetAtPath<Material>(OutlinePath);
            foreach (var interaction in Object.FindObjectsByType<EquipmentInteraction>(FindObjectsInactive.Include))
            {
                var serialized = new SerializedObject(interaction);
                serialized.FindProperty("pickTintMaterial").objectReferenceValue = tint;
                var hover = serialized.FindProperty("hoverOutlineMaterial");
                if (hover.objectReferenceValue == null) hover.objectReferenceValue = outline;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine($"{scenePath}: {interaction.name} tint {tint != null}, outline {hover.objectReferenceValue != null}");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        return log.ToString();
    }
}
