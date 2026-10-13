// Editor authoring script, run with `unity command run_script --file AgentScripts/InstallShelfBoxes.cs --entry InstallShelfBoxes.Run`
// after ArtSource/Restaurant/build_shelf_boxes.py has exported RT_StorageShelf_Boxes.fbx. It:
// - makes RT_Cardboard and RT_Tape (URP Lit, copied from RT_Oak) in Assets/Art/Restaurant/Materials;
// - imports the FBX with its RT_* materials remapped to the kit's materials (the label is RT_Ivory);
// - puts the boxes model under the storage shelf prefab as "Boxes", every stack inactive, and gives the prefab root a
//   StorageFillDisplay whose stages are Boxes_1 .. Boxes_8 in fill order (one per slot of the shelf's 8).
// Idempotent: materials keep their GUIDs and an existing "Boxes" child is replaced. Touches no scene.
using System.Linq;
using System.Text;
using FoodFactoryGame.Session.Equipment;
using UnityEditor;
using UnityEngine;

public static class InstallShelfBoxes
{
    const string MaterialFolder = "Assets/Art/Restaurant/Materials";
    const string ModelPath = "Assets/Art/Restaurant/Models/Service/RT_StorageShelf_Boxes.fbx";
    const string PrefabPath = "Assets/Prefabs/Restaurant/RT_StorageShelf_1_2m.prefab";

    public static string Run()
    {
        var log = new StringBuilder();
        Material(log, "RT_Cardboard", new Color32(0xb8, 0x89, 0x5a, 0xff), 0.12f);
        Material(log, "RT_Tape", new Color32(0x9c, 0x6c, 0x3f, 0xff), 0.55f);
        AssetDatabase.SaveAssets();

        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        if (importer == null) return "missing " + ModelPath + "; run build_shelf_boxes.py first";
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        foreach (var name in new[] { "RT_Cardboard", "RT_Tape", "RT_Ivory" })
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat");
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material);
        }
        importer.SaveAndReimport();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var old = root.transform.Find("Boxes");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var boxes = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            boxes.name = "Boxes";
            boxes.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            var stages = Enumerable.Range(1, 8).Select(i => boxes.transform.Find($"Boxes_{i}")?.gameObject).ToArray();
            if (stages.Any(x => x == null)) return "the model lacks Boxes_1 .. Boxes_8";
            foreach (var stage in stages)
            {
                stage.SetActive(false);
                foreach (var renderer in stage.GetComponentsInChildren<Renderer>(true))
                    if (renderer.sharedMaterials.Any(x => x == null || !x.name.StartsWith("RT_"))) return $"{stage.name} has an unmapped material";
            }
            // GetComponent returns a fake null in the Editor, so no ??.
            var display = root.GetComponent<StorageFillDisplay>();
            if (display == null) display = root.AddComponent<StorageFillDisplay>();
            var serialized = new SerializedObject(display);
            var array = serialized.FindProperty("stages");
            array.arraySize = stages.Length;
            for (var index = 0; index < stages.Length; index++) array.GetArrayElementAtIndex(index).objectReferenceValue = stages[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            log.AppendLine($"shelf prefab: {stages.Length} box stages, materials {string.Join(", ", stages.SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).SelectMany(x => x.sharedMaterials).Select(x => x.name).Distinct())}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return log.ToString();
    }

    static void Material(StringBuilder log, string name, Color color, float smoothness)
    {
        var path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            AssetDatabase.CopyAsset($"{MaterialFolder}/RT_Oak.mat", path);
            material = AssetDatabase.LoadAssetAtPath<Material>(path);
            log.AppendLine("created " + path);
        }
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
    }
}
