// Defines the numbered scene names used by each test building's interior floors.
using UnityEngine;

public static class TestBuildingFloorScenes
{
    public const string TemplateSceneName = "insidefactory0";
    public const string TemplateScenePath = "Assets/Scenes/insidefactory0.unity";

    public static string GetSceneName(uint buildingInstanceId, int floorIndex)
    {
        return $"insidefactory_{buildingInstanceId}_{Mathf.Max(0, floorIndex)}";
    }

    public static string GetScenePath(uint buildingInstanceId, int floorIndex)
    {
        return $"Assets/Scenes/{GetSceneName(buildingInstanceId, floorIndex)}.unity";
    }
}
