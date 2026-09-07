// Owns persistent OutsideTest building/floor state, registration, migration, and server-side simulation.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public readonly struct OutsideTestFloorKey : IEquatable<OutsideTestFloorKey>
{
    public OutsideTestFloorKey(uint newBuildingInstanceId, int newFloorIndex)
    {
        BuildingInstanceId = newBuildingInstanceId;
        FloorIndex = newFloorIndex;
    }

    public uint BuildingInstanceId { get; }
    public int FloorIndex { get; }

    public bool Equals(OutsideTestFloorKey other)
    {
        return BuildingInstanceId == other.BuildingInstanceId
            && FloorIndex == other.FloorIndex;
    }

    public override bool Equals(object obj)
    {
        return obj is OutsideTestFloorKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((int)BuildingInstanceId * 397) ^ FloorIndex;
        }
    }

    public override string ToString()
    {
        return $"Building {BuildingInstanceId} / Floor {FloorIndex}";
    }
}

public readonly struct OutsideTestBuildingInfo
{
    public OutsideTestBuildingInfo(
        uint newBuildingInstanceId,
        int newStoryCount,
        Vector2Int newInteriorSize)
    {
        BuildingInstanceId = newBuildingInstanceId;
        StoryCount = newStoryCount;
        InteriorSize = newInteriorSize;
    }

    public uint BuildingInstanceId { get; }
    public int StoryCount { get; }
    public Vector2Int InteriorSize { get; }
}

public sealed class OutsideTestFloorStateOwner
{
    public const int CurrentSaveVersion = 2;

    private readonly uint legacyBuildingInstanceId;
    private readonly Dictionary<OutsideTestFloorKey, OutsideTestFloorRecord> floorStates = new();
    private readonly Dictionary<uint, OutsideTestBuildingInfo> buildings = new();

    public OutsideTestFloorStateOwner(uint newLegacyBuildingInstanceId)
    {
        legacyBuildingInstanceId = newLegacyBuildingInstanceId;
    }

    public IEnumerable<OutsideTestFloorRecord> FloorStates => floorStates.Values;
    public IEnumerable<OutsideTestBuildingInfo> Buildings => buildings.Values;

    public bool TryRegisterBuilding(
        uint buildingInstanceId,
        int storyCount,
        Vector2Int interiorSize,
        out string error)
    {
        error = string.Empty;
        if (buildingInstanceId == 0)
        {
            error = "Building ID must be greater than zero.";
            return false;
        }

        if (storyCount < 1)
        {
            error = $"Building {buildingInstanceId} must have at least one floor.";
            return false;
        }

        if (!BuildingFootprint.IsValid(interiorSize))
        {
            error = $"Building {buildingInstanceId} has an invalid interior size {interiorSize}.";
            return false;
        }

        var registration = new OutsideTestBuildingInfo(
            buildingInstanceId,
            storyCount,
            interiorSize);
        if (buildings.TryGetValue(buildingInstanceId, out var existingRegistration))
        {
            if (existingRegistration.StoryCount != registration.StoryCount
                || existingRegistration.InteriorSize != registration.InteriorSize)
            {
                error = $"Duplicate building ID {buildingInstanceId} has conflicting layout data.";
                return false;
            }

            NormalizeBuildingFloorStates(registration);
            return false;
        }

        buildings.Add(buildingInstanceId, registration);
        NormalizeBuildingFloorStates(registration);
        return true;
    }

    public bool TryGetBuildingInfo(
        uint buildingInstanceId,
        out OutsideTestBuildingInfo info)
    {
        return buildings.TryGetValue(buildingInstanceId, out info);
    }

    public bool TryGetFloorState(
        uint buildingInstanceId,
        int floorIndex,
        out OutsideTestFloorRecord state)
    {
        return floorStates.TryGetValue(
            new OutsideTestFloorKey(buildingInstanceId, floorIndex),
            out state);
    }

    public bool TrySetFloorState(
        uint buildingInstanceId,
        int floorIndex,
        string label,
        float productionRate,
        Vector2 markerPosition)
    {
        var key = new OutsideTestFloorKey(buildingInstanceId, floorIndex);
        if (!floorStates.TryGetValue(key, out var state))
        {
            return false;
        }

        state.SetState(
            label,
            productionRate,
            state.AccumulatedProduction,
            ClampMarkerPosition(buildingInstanceId, markerPosition));
        return true;
    }

    public Vector2 ClampMarkerPosition(uint buildingInstanceId, Vector2 markerPosition)
    {
        if (!buildings.TryGetValue(buildingInstanceId, out var registration))
        {
            return OutsideTestFloorRecord.SanitizeMarkerPosition(markerPosition);
        }

        return OutsideTestFloorRecord.ClampMarkerPosition(
            markerPosition,
            registration.InteriorSize);
    }

    public void AdvanceProduction(float deltaTime)
    {
        foreach (var state in floorStates.Values)
        {
            state.Advance(deltaTime);
        }
    }

    public bool ApplySnapshot(
        uint buildingInstanceId,
        int floorIndex,
        string label,
        float productionRate,
        float accumulatedProduction,
        Vector2 markerPosition)
    {
        if (buildingInstanceId == 0 || floorIndex < 0)
        {
            return false;
        }

        if (buildings.TryGetValue(buildingInstanceId, out var registration)
            && floorIndex >= registration.StoryCount)
        {
            return false;
        }

        var key = new OutsideTestFloorKey(buildingInstanceId, floorIndex);
        if (!floorStates.TryGetValue(key, out var state))
        {
            state = new OutsideTestFloorRecord(
                buildingInstanceId,
                floorIndex,
                label,
                productionRate,
                accumulatedProduction,
                ClampMarkerPosition(buildingInstanceId, markerPosition));
            floorStates.Add(key, state);
            return true;
        }

        state.SetState(
            label,
            productionRate,
            accumulatedProduction,
            ClampMarkerPosition(buildingInstanceId, markerPosition));
        return true;
    }

    public bool LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            return LoadFromJson(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool LoadFromJson(string json)
    {
        try
        {
            var data = JsonUtility.FromJson<OutsideTestFloorSaveData>(json);
            if (data is null || data.Floors is null)
            {
                return false;
            }

            var version = data.Version <= 0 ? 1 : data.Version;
            if (version > CurrentSaveVersion)
            {
                return false;
            }

            floorStates.Clear();
            foreach (var savedState in data.Floors)
            {
                if (savedState is null || savedState.FloorIndex < 0)
                {
                    continue;
                }

                var buildingInstanceId = savedState.BuildingInstanceId;
                if (version == 1 && buildingInstanceId == 0)
                {
                    buildingInstanceId = legacyBuildingInstanceId;
                }

                if (buildingInstanceId == 0)
                {
                    continue;
                }

                var migratedState = new OutsideTestFloorRecord(
                    buildingInstanceId,
                    savedState.FloorIndex,
                    savedState.Label,
                    savedState.ProductionRate,
                    savedState.AccumulatedProduction,
                    savedState.MarkerPosition);
                var key = new OutsideTestFloorKey(
                    buildingInstanceId,
                    savedState.FloorIndex);
                if (!floorStates.ContainsKey(key))
                {
                    floorStates.Add(key, migratedState);
                }
            }

            foreach (var registration in buildings.Values)
            {
                NormalizeBuildingFloorStates(registration);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool SaveToFile(string path)
    {
        try
        {
            File.WriteAllText(path, ToJson());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string ToJson()
    {
        var data = new OutsideTestFloorSaveData(GetSortedFloorStates())
        {
            Version = CurrentSaveVersion
        };
        return JsonUtility.ToJson(data, true);
    }

    private void NormalizeBuildingFloorStates(OutsideTestBuildingInfo registration)
    {
        for (var floorIndex = 0; floorIndex < registration.StoryCount; floorIndex++)
        {
            var key = new OutsideTestFloorKey(
                registration.BuildingInstanceId,
                floorIndex);
            if (!floorStates.TryGetValue(key, out var state))
            {
                state = OutsideTestFloorRecord.CreateDefault(
                    registration.BuildingInstanceId,
                    floorIndex);
                floorStates.Add(key, state);
            }

            state.SetState(
                state.Label,
                state.ProductionRate,
                state.AccumulatedProduction,
                ClampMarkerPosition(
                    registration.BuildingInstanceId,
                    state.MarkerPosition));
        }
    }

    private List<OutsideTestFloorRecord> GetSortedFloorStates()
    {
        var result = new List<OutsideTestFloorRecord>(floorStates.Values);
        result.Sort((left, right) =>
        {
            var buildingComparison = left.BuildingInstanceId.CompareTo(right.BuildingInstanceId);
            return buildingComparison != 0
                ? buildingComparison
                : left.FloorIndex.CompareTo(right.FloorIndex);
        });
        return result;
    }
}
