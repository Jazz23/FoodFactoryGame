// Exports and validates compact building intent assets without serializing generated shell geometry.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Pipeline.Commands;

[Serializable]
public sealed class FactoryBuildingLayoutAssetResult
{
    public bool ok;
    public bool changed;
    public bool assigned;
    public bool saved;
    public string assetPath = string.Empty;
    public int recordCount;
    public List<uint> buildingIds = new();
    public List<string> issues = new();
}

public static class FactoryBuildingLayoutCommands
{
    [CliCommand(
        "factory_export_building_layout",
        "Export OutsideTest building topology to a compact asset and optionally assign it to the creator.",
        MainThreadRequired = true)]
    public static FactoryBuildingLayoutAssetResult Export(
        [CliArg("path", "Asset path, relative to Assets, ending in .asset.")] string path = "Authoring/OutsideTestBuildingLayout.asset",
        [CliArg("dry_run", "Preview the export without writing the asset or scene.")] bool dryRun = false,
        [CliArg("confirm", "Write the asset and scene when true.")] bool confirm = false,
        [CliArg("assign", "Assign the exported asset to the OutsideTest creator.")] bool assign = true)
    {
        return WithOutsideTestScene(
            scene => ExportInScene(scene, path, dryRun, confirm, assign));
    }

    [CliCommand(
        "factory_validate_building_layout",
        "Validate a compact building layout asset against OutsideTest generated layouts.",
        MainThreadRequired = true)]
    public static FactoryBuildingLayoutAssetResult Validate(
        [CliArg("path", "Optional asset path; defaults to the creator's assigned asset.")] string path = "")
    {
        return WithOutsideTestScene(scene => ValidateInScene(scene, path));
    }

    private static FactoryBuildingLayoutAssetResult ExportInScene(
        Scene scene,
        string requestedPath,
        bool dryRun,
        bool confirm,
        bool assign)
    {
        var result = new FactoryBuildingLayoutAssetResult
        {
            ok = false,
            assetPath = NormalizeAssetPath(requestedPath)
        };
        var creator = FindCreator(scene);
        if (creator is null || !creator)
        {
            result.issues.Add("OutsideTest has no TestBuildingCreator.");
            return result;
        }

        var records = creator.GeneratedBuildings is null || !creator.GeneratedBuildings
            ? new List<BuildingRecord>()
            : creator.GeneratedBuildings
                .GetComponentsInChildren<TestBuildingLayout>(true)
                .Select(layout => layout.ExportBuildingRecord())
                .ToList();
        records.Sort(CompareRecords);
        result.recordCount = records.Count;
        result.buildingIds.AddRange(records.Select(record => record.BuildingInstanceId));
        if (!BuildingShellValidation.TryValidateRecords(
                records,
                creator.DoorCornerExclusionDistance,
                out var validationError))
        {
            result.issues.Add(validationError);
            return result;
        }

        var existingAsset = AssetDatabase.LoadAssetAtPath<FactoryBuildingLayoutAsset>(result.assetPath);
        result.changed = existingAsset is null
            || !existingAsset.HasSameRecords(records)
            || (assign && creator.AuthoredLayout != existingAsset);
        result.ok = true;
        if (dryRun || !confirm)
        {
            return result;
        }

        if (!EnsureAssetFolder(result.assetPath, result.issues))
        {
            result.ok = false;
            return result;
        }

        var asset = existingAsset;
        if (asset is null)
        {
            asset = ScriptableObject.CreateInstance<FactoryBuildingLayoutAsset>();
            AssetDatabase.CreateAsset(asset, result.assetPath);
        }

        asset.ReplaceRecords(records);
        EditorUtility.SetDirty(asset);
        if (assign)
        {
            creator.SetAuthoredLayout(asset);
            EditorUtility.SetDirty(creator);
            EditorSceneManager.MarkSceneDirty(scene);
            result.assigned = true;
        }

        AssetDatabase.SaveAssets();
        if (assign && !EditorSceneManager.SaveScene(scene))
        {
            result.issues.Add("The OutsideTest scene could not be saved.");
            result.ok = false;
            return result;
        }

        result.saved = true;
        return result;
    }

    private static FactoryBuildingLayoutAssetResult ValidateInScene(
        Scene scene,
        string requestedPath)
    {
        var result = new FactoryBuildingLayoutAssetResult { ok = false };
        var creator = FindCreator(scene);
        if (creator is null || !creator)
        {
            result.issues.Add("OutsideTest has no TestBuildingCreator.");
            return result;
        }

        var assetPath = NormalizeAssetPath(requestedPath);
        var asset = string.IsNullOrWhiteSpace(requestedPath)
            ? creator.AuthoredLayout
            : AssetDatabase.LoadAssetAtPath<FactoryBuildingLayoutAsset>(assetPath);
        result.assetPath = asset is null
            ? assetPath
            : AssetDatabase.GetAssetPath(asset);
        if (asset is null || !asset)
        {
            result.issues.Add("No compact building layout asset was found.");
            return result;
        }

        result.recordCount = asset.Count;
        result.buildingIds.AddRange(asset.BuildingRecords.Select(record => record.BuildingInstanceId));
        if (!asset.TryValidate(creator.DoorCornerExclusionDistance, out var validationError))
        {
            result.issues.Add(validationError);
        }

        var sceneRecords = creator.GeneratedBuildings is null || !creator.GeneratedBuildings
            ? new List<BuildingRecord>()
            : creator.GeneratedBuildings
                .GetComponentsInChildren<TestBuildingLayout>(true)
                .Select(layout => layout.ExportBuildingRecord())
                .ToList();
        if (!asset.HasSameRecords(sceneRecords))
        {
            result.issues.Add("Compact layout records do not match the generated scene layouts.");
        }

        result.ok = result.issues.Count == 0;
        return result;
    }

    private static TestBuildingCreator FindCreator(Scene scene)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<TestBuildingCreator>(true))
            .SingleOrDefault(candidate => candidate.gameObject.scene == scene)!;
    }

    private static string NormalizeAssetPath(string requestedPath)
    {
        var path = (requestedPath ?? string.Empty).Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "Authoring/OutsideTestBuildingLayout.asset";
        }

        if (!path.StartsWith("Assets/", StringComparison.Ordinal))
        {
            path = "Assets/" + path.TrimStart('/');
        }

        return Path.GetExtension(path).Length == 0
            ? path + ".asset"
            : path;
    }

    private static bool EnsureAssetFolder(
        string assetPath,
        List<string> issues)
    {
        var folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(folder) || folder == "Assets")
        {
            return true;
        }

        var segments = folder.Split('/');
        var current = segments[0];
        for (var index = 1; index < segments.Length; index++)
        {
            var next = current + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(next)
                && AssetDatabase.CreateFolder(current, segments[index]) == string.Empty)
            {
                issues.Add($"Could not create asset folder '{next}'.");
                return false;
            }

            current = next;
        }

        return true;
    }

    private static int CompareRecords(
        BuildingRecord left,
        BuildingRecord right)
    {
        return left.BuildingInstanceId.CompareTo(right.BuildingInstanceId);
    }

    private static T WithOutsideTestScene<T>(Func<Scene, T> operation)
    {
        var previousSetup = EditorSceneManager.GetSceneManagerSetup();
        var scene = SceneManager.GetSceneByPath(FactoryPipelineCommands.OutsideTestScenePath);
        var openedForOperation = !scene.isLoaded;
        if (openedForOperation)
        {
            scene = EditorSceneManager.OpenScene(
                FactoryPipelineCommands.OutsideTestScenePath,
                OpenSceneMode.Additive);
        }

        try
        {
            return operation(scene);
        }
        finally
        {
            if (previousSetup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
            else if (openedForOperation && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
