// Stores the persistent topology of one OutsideTest building shell.
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BuildingRecord
{
    [Serializable]
    public sealed class DoorPlacement
    {
        [SerializeField] private string wallId = string.Empty;
        [SerializeField, Range(0f, 1f)] private float normalizedOffset = 0.5f;

        public DoorPlacement()
        {
        }

        public DoorPlacement(string newWallId, float newNormalizedOffset)
        {
            wallId = newWallId ?? string.Empty;
            normalizedOffset = Mathf.Clamp01(newNormalizedOffset);
        }

        public string WallId => wallId;
        public float NormalizedOffset => normalizedOffset;

        public DoorPlacement Clone()
        {
            return new DoorPlacement(wallId, normalizedOffset);
        }
    }

    [SerializeField] private uint buildingInstanceId;
    [SerializeField] private Vector3Int anchorCell;
    [SerializeField] private Vector2Int footprintSize;
    [SerializeField, Min(1)] private int storyCount = 1;
    [SerializeField] private List<DoorPlacement> doors = new();

    public BuildingRecord()
    {
    }

    public BuildingRecord(
        uint newBuildingInstanceId,
        Vector3Int newAnchorCell,
        Vector2Int newFootprintSize,
        int newStoryCount,
        IEnumerable<DoorPlacement> newDoors = null)
    {
        buildingInstanceId = newBuildingInstanceId;
        anchorCell = newAnchorCell;
        footprintSize = newFootprintSize;
        storyCount = newStoryCount;
        SetDoors(newDoors);
    }

    public uint BuildingInstanceId => buildingInstanceId;
    public uint BuildingId => buildingInstanceId;
    public Vector3Int AnchorCell => anchorCell;
    public Vector2Int FootprintSize => footprintSize;
    public Vector2Int Size => footprintSize;
    public int StoryCount => storyCount;
    public IReadOnlyList<DoorPlacement> Doors => GetDoors();

    public void SetTopology(
        uint newBuildingInstanceId,
        Vector3Int newAnchorCell,
        Vector2Int newFootprintSize,
        int newStoryCount)
    {
        buildingInstanceId = newBuildingInstanceId;
        anchorCell = newAnchorCell;
        footprintSize = newFootprintSize;
        storyCount = newStoryCount;
    }

    public void SetDoors(IEnumerable<DoorPlacement> newDoors)
    {
        var doorList = GetDoors();
        doorList.Clear();
        if (newDoors is null)
        {
            return;
        }

        foreach (var door in newDoors)
        {
            if (door is not null)
            {
                doorList.Add(door.Clone());
            }
        }
    }

    public BuildingRecord Clone()
    {
        return new BuildingRecord(
            buildingInstanceId,
            anchorCell,
            footprintSize,
            storyCount,
            Doors);
    }

    public bool HasSameTopology(BuildingRecord other)
    {
        if (other is null
            || buildingInstanceId != other.buildingInstanceId
            || anchorCell != other.anchorCell
            || footprintSize != other.footprintSize
            || storyCount != other.storyCount
            || Doors.Count != other.Doors.Count)
        {
            return false;
        }

        for (var index = 0; index < Doors.Count; index++)
        {
            var left = Doors[index];
            var right = other.Doors[index];
            if (left is null
                || right is null
                || left.WallId != right.WallId
                || !Mathf.Approximately(
                    left.NormalizedOffset,
                    right.NormalizedOffset))
            {
                return false;
            }
        }

        return true;
    }

    private List<DoorPlacement> GetDoors()
    {
        if (doors is null)
        {
            doors = new List<DoorPlacement>();
        }

        return doors;
    }
}
