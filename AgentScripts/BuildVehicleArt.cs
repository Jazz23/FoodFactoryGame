// Editor authoring script (decision 0032, piece 4c), run with `unity command run_script --file AgentScripts/BuildVehicleArt.cs
// --entry BuildVehicleArt.Run` after ArtSource/Vehicles/build_vehicle_models.py has exported Export/Vehicles.fbx, and again
// after BuildDevSite.cs or BuildWorldGenScene.cs (which rebuild the scenes with the placeholder truck). It:
// - copies the export to Assets/Art/Vehicles/Models/Vehicles.fbx and imports it without animation, cameras or lights, with each
//   VH_* material remapped to a URP Lit material in Assets/Art/Vehicles/Materials (VH_Body stays white: it is tinted per
//   vehicle at runtime);
// - makes a prefab per vehicle in Assets/Prefabs/Vehicles (the mesh under a root at the ground, no collider);
// - in DevSite, SampleScene and WorldGen, puts the box truck on every TruckPresenter and adds or updates a CityTrafficPresenter
//   with the city cars (sedans and hatchbacks more often than the rest) and a palette of paints.
// Idempotent: assets keep their GUIDs and scene objects are found and updated. Refuses to run with an unsaved scene open; the
// scene that was open is reopened at the end.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FoodFactoryGame.Session;
using FoodFactoryGame.Session.Logistics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildVehicleArt
{
    private const string Source = "ArtSource/Vehicles/Export/Vehicles.fbx";
    private const string ModelPath = "Assets/Art/Vehicles/Models/Vehicles.fbx";
    private const string MaterialFolder = "Assets/Art/Vehicles/Materials";
    private const string PrefabFolder = "Assets/Prefabs/Vehicles";
    private static readonly string[] Scenes = { "Assets/Scenes/DevSite.unity", "Assets/Scenes/SampleScene.unity", "Assets/Scenes/WorldGen.unity" };
    // City cars in draw order: a key picks one uniformly, so repeats make a model more common.
    private static readonly string[] CityCars =
    {
        "Vehicle_Sedan", "Vehicle_Hatchback", "Vehicle_Sedan", "Vehicle_Hatchback", "Vehicle_Sedan", "Vehicle_Van", "Vehicle_Pickup",
        "Vehicle_Hatchback", "Vehicle_Taxi", "Vehicle_Bus"
    };
    // PROTOTYPE paints for VH_Body.
    private static readonly Color[] Paints =
    {
        new(0.85f, 0.85f, 0.83f), new(0.12f, 0.12f, 0.13f), new(0.55f, 0.57f, 0.6f), new(0.62f, 0.1f, 0.1f), new(0.12f, 0.25f, 0.5f),
        new(0.3f, 0.45f, 0.62f), new(0.2f, 0.36f, 0.22f), new(0.72f, 0.66f, 0.52f), new(0.45f, 0.3f, 0.2f), new(0.9f, 0.9f, 0.88f)
    };

    // name: colour, smoothness, metallic, emission
    private static readonly Dictionary<string, (Color Colour, float Smoothness, float Metallic, float Emission)> Materials = new()
    {
        ["VH_Body"] = (Color.white, 0.65f, 0.3f, 0f),
        ["VH_Glass"] = (new Color(0.1f, 0.14f, 0.18f), 0.9f, 0.6f, 0f),
        ["VH_Tyre"] = (new Color(0.04f, 0.04f, 0.045f), 0.1f, 0f, 0f),
        ["VH_Trim"] = (new Color(0.12f, 0.12f, 0.13f), 0.4f, 0.2f, 0f),
        ["VH_Chrome"] = (new Color(0.7f, 0.72f, 0.75f), 0.75f, 0.9f, 0f),
        ["VH_Headlight"] = (new Color(1f, 0.96f, 0.82f), 0.8f, 0f, 1.2f),
        ["VH_Taillight"] = (new Color(0.75f, 0.05f, 0.04f), 0.7f, 0f, 0.6f),
        ["VH_Box"] = (new Color(0.93f, 0.93f, 0.9f), 0.5f, 0f, 0f),
        ["VH_Taxi"] = (new Color(0.95f, 0.72f, 0.08f), 0.65f, 0.2f, 0f),
        ["VH_Bus"] = (new Color(0.15f, 0.42f, 0.7f), 0.6f, 0.2f, 0f),
        ["VH_Sign"] = (new Color(0.98f, 0.98f, 0.95f), 0.6f, 0f, 0.4f),
    };

    public static string Run()
    {
        if (Enumerable.Range(0, EditorSceneManager.sceneCount).Any(i => EditorSceneManager.GetSceneAt(i).isDirty))
            return "Refused: save or discard the open scene's changes first.";
        foreach (var folder in new[] { Path.GetDirectoryName(ModelPath), MaterialFolder, PrefabFolder }) Directory.CreateDirectory(folder);
        File.Copy(Source, ModelPath, true);
        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);

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
            if (filter == null || renderer == null) continue;
            var root = new GameObject(child.name);
            var part = new GameObject("Model");
            part.transform.SetParent(root.transform, false);
            part.transform.localRotation = child.localRotation;
            part.transform.localScale = child.localScale;
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var drawn = part.AddComponent<MeshRenderer>();
            drawn.sharedMaterials = renderer.sharedMaterials;
            drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            prefabs[child.name] = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{child.name}.prefab");
            Object.DestroyImmediate(root);
        }
        if (!prefabs.ContainsKey("Vehicle_BoxTruck")) return "Vehicles.fbx has no Vehicle_BoxTruck.";

        var previous = EditorSceneManager.GetActiveScene().path;
        var prefabPaths = prefabs.ToDictionary(x => x.Key, x => AssetDatabase.GetAssetPath(x.Value));
        var installed = Scenes.Where(File.Exists).Select(x => Install(x, prefabPaths)).ToList();
        if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        return $"Vehicles: {prefabs.Count} prefabs ({string.Join(", ", prefabs.Keys)}); scenes {string.Join(", ", installed)}";
    }

    private static string Install(string scenePath, Dictionary<string, string> prefabPaths)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var truck = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths["Vehicle_BoxTruck"]);
        var presenters = Object.FindObjectsByType<TruckPresenter>(FindObjectsInactive.Include);
        foreach (var presenter in presenters)
            using (var serialized = new SerializedObject(presenter))
            {
                serialized.FindProperty("truckPrefab").objectReferenceValue = truck;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        var session = Object.FindAnyObjectByType<SessionRoot>(FindObjectsInactive.Include);
        var traffic = Object.FindAnyObjectByType<CityTrafficPresenter>(FindObjectsInactive.Include);
        if (session != null)
        {
            if (traffic == null) traffic = new GameObject("CityTrafficPresenter").AddComponent<CityTrafficPresenter>();
            using var serialized = new SerializedObject(traffic);
            serialized.FindProperty("session").objectReferenceValue = session;
            var cars = serialized.FindProperty("carPrefabs");
            cars.arraySize = CityCars.Length;
            for (var i = 0; i < CityCars.Length; i++)
                cars.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[CityCars[i]]);
            var tints = serialized.FindProperty("tints");
            tints.arraySize = Paints.Length;
            for (var i = 0; i < Paints.Length; i++) tints.GetArrayElementAtIndex(i).colorValue = Paints[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"{Path.GetFileNameWithoutExtension(scenePath)} ({presenters.Length} truck presenter(s){(session != null ? ", city traffic" : "")})";
    }
}
