// Installs the phone (decision 0047): gives WorldGen and DevSite a "PhonePanel" object with a UIDocument on the shared panel
// settings and a PhonePanel wired to the scene's EquipmentInteraction, LogisticsPanel (its Trucks app) and, where there is one, its
// WorldLayoutPresenter (DevSite has no map). Idempotent. Refuses to run while the open scene has unsaved changes. Run in Edit mode with run_script; it reopens
// the scene that was open at the end.
using System.Linq;
using System.Text;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.WorldMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class InstallPhonePanel
{
    const string PanelSettingsPath = "Assets/UI/DevPanelSettings.asset";

    public static string Run()
    {
        var log = new StringBuilder();
        var open = EditorSceneManager.GetActiveScene();
        if (open.isDirty) return $"Refused: {open.path} has unsaved changes.";
        var reopen = open.path;
        foreach (var scenePath in new[] { "Assets/Scenes/WorldGen.unity", "Assets/Scenes/DevSite.unity" })
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var interaction = Object.FindObjectsByType<EquipmentInteraction>(FindObjectsInactive.Include).SingleOrDefault();
            if (interaction == null)
            {
                log.AppendLine($"{scenePath}: no EquipmentInteraction, skipped");
                continue;
            }
            var settings = Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include).Select(x => x.panelSettings).FirstOrDefault(x => x != null)
                ?? AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var map = Object.FindObjectsByType<WorldLayoutPresenter>(FindObjectsInactive.Include).SingleOrDefault();
            var panel = Object.FindObjectsByType<PhonePanel>(FindObjectsInactive.Include).SingleOrDefault();
            if (panel == null)
            {
                var host = new GameObject("PhonePanel");
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                panel = host.AddComponent<PhonePanel>();
            }
            // Above the HUD (10), whose cash card would otherwise cover the phone's corner; the phone shows the cash itself.
            panel.GetComponent<UIDocument>().sortingOrder = 11;
            var serialized = new SerializedObject(panel);
            serialized.FindProperty("document").objectReferenceValue = panel.GetComponent<UIDocument>();
            serialized.FindProperty("interaction").objectReferenceValue = interaction;
            serialized.FindProperty("map").objectReferenceValue = map;
            serialized.FindProperty("logistics").objectReferenceValue = Object.FindObjectsByType<FoodFactoryGame.Session.Logistics.LogisticsPanel>(FindObjectsInactive.Include).Single();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"{scenePath}: PhonePanel (settings {(settings != null ? settings.name : "none")}, map {(map != null)})");
        }
        if (!string.IsNullOrEmpty(reopen)) EditorSceneManager.OpenScene(reopen, OpenSceneMode.Single);
        return log.ToString();
    }
}
