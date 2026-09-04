// Creates, registers, and removes the unique interior scenes owned by test buildings.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TestBuildingFloorSceneUtility
{
    public static bool EnsureFloorScenes(TestBuildingLayout layout)
    {
        if (layout.BuildingInstanceId == 0)
        {
            return false;
        }

        var changed = false;
        for (var floorIndex = 0; floorIndex < layout.StoryCount; floorIndex++)
        {
            var path = TestBuildingFloorScenes.GetScenePath(
                layout.BuildingInstanceId,
                floorIndex);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) is null)
            {
                if (!AssetDatabase.CopyAsset(TestBuildingFloorScenes.TemplateScenePath, path))
                {
                    Debug.LogError(
                        $"Could not create floor scene '{path}' from '"
                        + $"{TestBuildingFloorScenes.TemplateScenePath}'.",
                        layout);
                    continue;
                }

                changed = true;
            }

            changed |= AddToBuildSettings(path);
        }

        if (changed)
        {
            AssetDatabase.SaveAssets();
        }

        return changed;
    }

    public static bool AddStory(TestBuildingLayout layout)
    {
        if (layout.BuildingInstanceId == 0)
        {
            Debug.LogError("Cannot add a story to a building without an instance ID.", layout);
            return false;
        }

        var template = AssetDatabase.LoadAssetAtPath<SceneAsset>(
            TestBuildingFloorScenes.TemplateScenePath);
        if (template is null)
        {
            Debug.LogError(
                $"The floor template '{TestBuildingFloorScenes.TemplateScenePath}' does not exist.",
                layout);
            return false;
        }

        EnsureFloorScenes(layout);

        var nextFloorIndex = layout.StoryCount;
        var path = TestBuildingFloorScenes.GetScenePath(
            layout.BuildingInstanceId,
            nextFloorIndex);
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) is null
            && !AssetDatabase.CopyAsset(TestBuildingFloorScenes.TemplateScenePath, path))
        {
            Debug.LogError($"Could not create floor scene '{path}'.", layout);
            return false;
        }

        AddToBuildSettings(path);
        layout.SetStoryCount(nextFloorIndex + 1);
        EditorUtility.SetDirty(layout);
        AssetDatabase.SaveAssets();
        return true;
    }

    public static bool DeleteTopStory(TestBuildingLayout layout)
    {
        if (layout.BuildingInstanceId == 0)
        {
            Debug.LogError("Cannot delete a story from a building without an instance ID.", layout);
            return false;
        }

        if (layout.StoryCount <= 1)
        {
            return false;
        }

        var floorIndex = layout.StoryCount - 1;
        var path = TestBuildingFloorScenes.GetScenePath(
            layout.BuildingInstanceId,
            floorIndex);
        var loadedScene = SceneManager.GetSceneByPath(path);
        if (loadedScene.isLoaded)
        {
            Debug.LogWarning(
                $"Close floor scene '{path}' before deleting the top story.",
                layout);
            return false;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) is not null
            && !AssetDatabase.DeleteAsset(path))
        {
            Debug.LogError($"Could not delete floor scene '{path}'.", layout);
            return false;
        }

        RemoveFromBuildSettings(path);
        layout.SetStoryCount(floorIndex);
        EditorUtility.SetDirty(layout);
        AssetDatabase.SaveAssets();
        return true;
    }

    public static bool DeleteAllFloorScenes(out int deletedSceneCount)
    {
        var paths = GetFloorScenePaths();
        return DeleteFloorScenes(paths, out deletedSceneCount);
    }

    public static bool DeleteFloorScenes(
        IReadOnlyCollection<string> paths,
        out int deletedSceneCount)
    {
        deletedSceneCount = 0;
        foreach (var path in paths)
        {
            var loadedScene = SceneManager.GetSceneByPath(path);
            if (!loadedScene.isLoaded)
            {
                continue;
            }

            Debug.LogWarning(
                $"Close floor scene '{path}' before clearing generated buildings.");
            return false;
        }

        var changed = false;
        foreach (var path in paths)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) is not null)
            {
                if (!AssetDatabase.DeleteAsset(path))
                {
                    Debug.LogError($"Could not delete floor scene '{path}'.");
                    return false;
                }

                deletedSceneCount++;
                changed = true;
            }

            changed |= RemoveFromBuildSettings(path);
        }

        if (changed)
        {
            AssetDatabase.SaveAssets();
        }

        return true;
    }

    private static HashSet<string> GetFloorScenePaths()
    {
        var paths = new HashSet<string>();
        foreach (var guid in AssetDatabase.FindAssets(
                     "t:Scene",
                     new[] { "Assets/Scenes" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (IsFloorScenePath(path))
            {
                paths.Add(path);
            }
        }

        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (IsFloorScenePath(scene.path))
            {
                paths.Add(scene.path);
            }
        }

        return paths;
    }

    private static bool IsFloorScenePath(string path)
    {
        if (!path.StartsWith("Assets/Scenes/", System.StringComparison.Ordinal)
            || !path.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = Path.GetFileNameWithoutExtension(path).Split('_');
        return parts.Length == 3
            && parts[0] == "insidefactory"
            && uint.TryParse(parts[1], out var buildingInstanceId)
            && buildingInstanceId > 0
            && int.TryParse(parts[2], out var floorIndex)
            && floorIndex >= 0;
    }

    private static bool AddToBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var scene in scenes)
        {
            if (scene.path == path)
            {
                return false;
            }
        }

        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        return true;
    }

    private static bool RemoveFromBuildSettings(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>();
        var changed = false;
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.path != path)
            {
                scenes.Add(scene);
                continue;
            }

            changed = true;
        }

        if (changed)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        return changed;
    }
}
