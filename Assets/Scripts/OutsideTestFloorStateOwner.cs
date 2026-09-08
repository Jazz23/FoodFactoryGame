// Owns persistent OutsideTest topology and floor state, registration, migration, and simulation.
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
        AnchorCell = Vector3Int.zero;
        FootprintSize = newInteriorSize;
    }

    public OutsideTestBuildingInfo(BuildingRecord record)
    {
        BuildingInstanceId = record.BuildingInstanceId;
        StoryCount = record.StoryCount;
        InteriorSize = record.FootprintSize;
        AnchorCell = record.AnchorCell;
        FootprintSize = record.FootprintSize;
    }

    public uint BuildingInstanceId { get; }
    public int StoryCount { get; }
    public Vector2Int InteriorSize { get; }
    public Vector3Int AnchorCell { get; }
    public Vector2Int FootprintSize { get; }
}

public sealed class OutsideTestFloorStateOwner
{
    public const int CurrentSaveVersion = 4;

    private readonly uint legacyBuildingInstanceId;
    private readonly Dictionary<OutsideTestFloorKey, OutsideTestFloorRecord> floorStates = new();
    private readonly Dictionary<uint, BuildingRecord> buildingRecords = new();
    private int lastLoadedVersion;
    private bool lastLoadHadBuildingRecords;

    public OutsideTestFloorStateOwner(uint newLegacyBuildingInstanceId)
    {
        legacyBuildingInstanceId = newLegacyBuildingInstanceId;
    }

    public IEnumerable<OutsideTestFloorRecord> FloorStates => floorStates.Values;
    public IEnumerable<BuildingRecord> BuildingRecords => buildingRecords.Values;
    public IEnumerable<OutsideTestBuildingInfo> Buildings => GetBuildingInfos();
    public int LastLoadedVersion => lastLoadedVersion;
    public bool LastLoadHadBuildingRecords => lastLoadHadBuildingRecords;

    public void MarkCurrentStateAsAuthoritative()
    {
        lastLoadedVersion = CurrentSaveVersion;
        lastLoadHadBuildingRecords = true;
    }

    public bool TryRegisterBuilding(
        BuildingRecord record,
        out string error)
    {
        error = string.Empty;
        if (record is null)
        {
            error = "Building record is required.";
            return false;
        }

        if (buildingRecords.TryGetValue(
                record.BuildingInstanceId,
                out var existingRecord))
        {
            if (!existingRecord.HasSameTopology(record))
            {
                error = $"Duplicate building ID {record.BuildingInstanceId} has conflicting layout data.";
                return false;
            }

            NormalizeBuildingFloorStates(existingRecord);
            return false;
        }

        if (!BuildingShellValidation.TryValidate(
                record,
                buildingRecords.Values,
                TestBuildingCreator.DefaultDoorCornerExclusionDistance,
                out error))
        {
            return false;
        }

        var ownedRecord = record.Clone();
        buildingRecords.Add(ownedRecord.BuildingInstanceId, ownedRecord);
        NormalizeBuildingFloorStates(ownedRecord);
        return true;
    }

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

        if (buildingRecords.TryGetValue(
                buildingInstanceId,
                out var existingRecord))
        {
            if (existingRecord.StoryCount != storyCount
                || existingRecord.FootprintSize != interiorSize)
            {
                error = $"Duplicate building ID {buildingInstanceId} has conflicting layout data.";
                return false;
            }

            NormalizeBuildingFloorStates(existingRecord);
            return false;
        }

        var legacyRecord = new BuildingRecord(
            buildingInstanceId,
            Vector3Int.zero,
            interiorSize,
            storyCount);
        buildingRecords.Add(buildingInstanceId, legacyRecord);
        NormalizeBuildingFloorStates(legacyRecord);
        return true;
    }

    public bool TryUpdateBuildingRecord(
        BuildingRecord record,
        float doorCornerExclusionDistance,
        out string error)
    {
        error = string.Empty;
        if (record is null)
        {
            error = "Building record is required.";
            return false;
        }

        var otherRecords = new List<BuildingRecord>();
        foreach (var existingRecord in buildingRecords.Values)
        {
            if (existingRecord.BuildingInstanceId != record.BuildingInstanceId)
            {
                otherRecords.Add(existingRecord);
            }
        }

        if (!BuildingShellValidation.TryValidate(
                record,
                otherRecords,
                doorCornerExclusionDistance,
                out error))
        {
            return false;
        }

        buildingRecords[record.BuildingInstanceId] = record.Clone();
        var staleKeys = new List<OutsideTestFloorKey>();
        foreach (var pair in floorStates)
        {
            if (pair.Key.BuildingInstanceId == record.BuildingInstanceId
                && pair.Key.FloorIndex >= record.StoryCount)
            {
                staleKeys.Add(pair.Key);
            }
        }

        foreach (var key in staleKeys)
        {
            floorStates.Remove(key);
        }

        NormalizeBuildingFloorStates(buildingRecords[record.BuildingInstanceId]);
        return true;
    }

    public bool TryGetBuildingInfo(
        uint buildingInstanceId,
        out OutsideTestBuildingInfo info)
    {
        if (buildingRecords.TryGetValue(buildingInstanceId, out var record))
        {
            info = new OutsideTestBuildingInfo(record);
            return true;
        }

        info = default;
        return false;
    }

    public bool TryGetBuildingRecord(
        uint buildingInstanceId,
        out BuildingRecord record)
    {
        if (buildingRecords.TryGetValue(buildingInstanceId, out var existingRecord))
        {
            record = existingRecord.Clone();
            return true;
        }

        record = null!;
        return false;
    }

    public uint GetNextBuildingId()
    {
        return GetNextBuildingId(Array.Empty<uint>());
    }

    public uint GetNextBuildingId(IEnumerable<uint> additionalIds)
    {
        var maximumId = 0u;
        foreach (var buildingId in buildingRecords.Keys)
        {
            if (buildingId > maximumId)
            {
                maximumId = buildingId;
            }
        }

        foreach (var floor in floorStates.Values)
        {
            if (floor.BuildingInstanceId > maximumId)
            {
                maximumId = floor.BuildingInstanceId;
            }
        }

        if (additionalIds is not null)
        {
            foreach (var buildingId in additionalIds)
            {
                if (buildingId > maximumId)
                {
                    maximumId = buildingId;
                }
            }
        }

        return maximumId == uint.MaxValue ? 0u : maximumId + 1u;
    }

    public bool RemoveBuilding(uint buildingInstanceId)
    {
        var removed = buildingRecords.Remove(buildingInstanceId);
        var staleKeys = new List<OutsideTestFloorKey>();
        foreach (var pair in floorStates)
        {
            if (pair.Key.BuildingInstanceId == buildingInstanceId)
            {
                staleKeys.Add(pair.Key);
            }
        }

        foreach (var key in staleKeys)
        {
            removed |= floorStates.Remove(key);
        }

        return removed;
    }

    public bool RemoveBuildingAndFloors(uint buildingInstanceId)
    {
        return RemoveBuilding(buildingInstanceId);
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
        if (!buildingRecords.TryGetValue(buildingInstanceId, out var registration))
        {
            return OutsideTestFloorRecord.SanitizeMarkerPosition(markerPosition);
        }

        return OutsideTestFloorRecord.ClampMarkerPosition(
            markerPosition,
            registration.FootprintSize);
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
        Vector2 markerPosition,
        FactoryEntitySnapshot[] entitySnapshots)
    {
        if (buildingInstanceId == 0 || floorIndex < 0)
        {
            return false;
        }

        var hasRegistration = buildingRecords.TryGetValue(
            buildingInstanceId,
            out var registration);
        if (hasRegistration
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
        }

        state.SetState(
            label,
            productionRate,
            accumulatedProduction,
            ClampMarkerPosition(buildingInstanceId, markerPosition));
        state.SetEntitySnapshots(entitySnapshots);
        if (hasRegistration)
        {
            NormalizeFloorState(registration, state);
        }

        if (!floorStates.ContainsKey(key))
        {
            floorStates.Add(key, state);
        }

        return true;
    }

    public bool LoadFromFile(string path)
    {
        return LoadFromFile(
            path,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance);
    }

    public bool LoadFromFile(string path, float doorCornerExclusionDistance)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            return LoadFromJson(
                File.ReadAllText(path),
                doorCornerExclusionDistance);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool LoadFromJson(string json)
    {
        return LoadFromJson(
            json,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance);
    }

    public bool LoadFromJson(string json, float doorCornerExclusionDistance)
    {
        if (string.IsNullOrWhiteSpace(json)
            || !HasJsonArrayProperty(json, "Floors"))
        {
            return false;
        }

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

            var loadedBuildingRecords = new Dictionary<uint, BuildingRecord>();
            if (version >= CurrentSaveVersion)
            {
                if (!HasJsonArrayProperty(json, "Buildings")
                    || data.Buildings is null
                    || !BuildingShellValidation.TryValidateRecords(
                        data.Buildings,
                        doorCornerExclusionDistance,
                        out _))
                {
                    return false;
                }

                foreach (var savedRecord in data.Buildings)
                {
                    if (savedRecord is null
                        || !loadedBuildingRecords.TryAdd(
                            savedRecord.BuildingInstanceId,
                            savedRecord.Clone()))
                    {
                        return false;
                    }
                }
            }
            else
            {
                foreach (var pair in buildingRecords)
                {
                    loadedBuildingRecords.Add(pair.Key, pair.Value.Clone());
                }
            }

            var loadedFloorStates = new Dictionary<OutsideTestFloorKey, OutsideTestFloorRecord>();
            foreach (var savedState in data.Floors)
            {
                if (savedState is null || savedState.FloorIndex < 0)
                {
                    return false;
                }

                var buildingInstanceId = savedState.BuildingInstanceId;
                if (version == 1 && buildingInstanceId == 0)
                {
                    buildingInstanceId = legacyBuildingInstanceId;
                }

                if (buildingInstanceId == 0)
                {
                    return false;
                }

                if (loadedBuildingRecords.TryGetValue(
                        buildingInstanceId,
                        out var registration)
                    && savedState.FloorIndex >= registration.StoryCount)
                {
                    return false;
                }

                var migratedState = new OutsideTestFloorRecord(
                    buildingInstanceId,
                    savedState.FloorIndex,
                    savedState.Label,
                    savedState.ProductionRate,
                    savedState.AccumulatedProduction,
                    savedState.MarkerPosition);
                migratedState.SetEntities(savedState.Entities);
                if (version < 3 && migratedState.Entities.Count == 0)
                {
                    migratedState.EnsureDefaultEntity();
                }

                if (loadedBuildingRecords.TryGetValue(
                        buildingInstanceId,
                        out registration))
                {
                    NormalizeFloorState(registration, migratedState);
                }

                var key = new OutsideTestFloorKey(
                    buildingInstanceId,
                    savedState.FloorIndex);
                if (!loadedFloorStates.TryAdd(key, migratedState))
                {
                    return false;
                }
            }

            foreach (var registration in loadedBuildingRecords.Values)
            {
                for (var floorIndex = 0;
                    floorIndex < registration.StoryCount;
                    floorIndex++)
                {
                    var key = new OutsideTestFloorKey(
                        registration.BuildingInstanceId,
                        floorIndex);
                    if (!loadedFloorStates.TryGetValue(key, out var state))
                    {
                        state = OutsideTestFloorRecord.CreateDefault(
                            registration.BuildingInstanceId,
                            floorIndex);
                        loadedFloorStates.Add(key, state);
                    }

                    NormalizeFloorState(registration, state);
                }
            }

            buildingRecords.Clear();
            foreach (var pair in loadedBuildingRecords)
            {
                buildingRecords.Add(pair.Key, pair.Value);
            }

            floorStates.Clear();
            foreach (var pair in loadedFloorStates)
            {
                floorStates.Add(pair.Key, pair.Value);
            }

            lastLoadedVersion = version;
            lastLoadHadBuildingRecords = version >= CurrentSaveVersion;
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
        var data = new OutsideTestFloorSaveData(
            GetSortedBuildingRecords(),
            GetSortedFloorStates())
        {
            Version = CurrentSaveVersion
        };
        return JsonUtility.ToJson(data, true);
    }

    private IEnumerable<OutsideTestBuildingInfo> GetBuildingInfos()
    {
        foreach (var record in buildingRecords.Values)
        {
            yield return new OutsideTestBuildingInfo(record);
        }
    }

    private void NormalizeBuildingFloorStates(BuildingRecord registration)
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

            NormalizeFloorState(registration, state);
        }
    }

    private void NormalizeFloorState(
        BuildingRecord registration,
        OutsideTestFloorRecord state)
    {
        state.SetState(
            state.Label,
            state.ProductionRate,
            state.AccumulatedProduction,
            OutsideTestFloorRecord.ClampMarkerPosition(
                state.MarkerPosition,
                registration.FootprintSize));
        state.ClampEntityPositions(registration.FootprintSize);
    }

    private List<BuildingRecord> GetSortedBuildingRecords()
    {
        var result = new List<BuildingRecord>(buildingRecords.Values);
        result.Sort((left, right) => left.BuildingInstanceId.CompareTo(right.BuildingInstanceId));
        return result;
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

    private static bool HasJsonArrayProperty(string json, string propertyName)
    {
        var propertyToken = $"\"{propertyName}\"";
        var propertyIndex = json.IndexOf(propertyToken, StringComparison.Ordinal);
        if (propertyIndex < 0)
        {
            return false;
        }

        var valueIndex = json.IndexOf(
            ':',
            propertyIndex + propertyToken.Length);
        if (valueIndex < 0)
        {
            return false;
        }

        valueIndex++;
        while (valueIndex < json.Length
            && char.IsWhiteSpace(json[valueIndex]))
        {
            valueIndex++;
        }

        return valueIndex < json.Length && json[valueIndex] == '[';
    }
}
