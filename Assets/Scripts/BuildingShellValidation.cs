// Validates persistent OutsideTest shell topology shared by editor and runtime creation.
using System;
using System.Collections.Generic;
using UnityEngine;

public static class BuildingShellValidation
{
    public const int MinimumFootprintDimension = 2;

    public static bool TryValidate(
        BuildingRecord record,
        IEnumerable<BuildingRecord> existingRecords,
        float doorCornerExclusionDistance,
        out string error)
    {
        error = string.Empty;
        if (record is null)
        {
            error = "Building record is required.";
            return false;
        }

        if (record.BuildingInstanceId == 0)
        {
            error = "Building ID must be greater than zero.";
            return false;
        }

        if (record.FootprintSize.x < MinimumFootprintDimension
            || record.FootprintSize.y < MinimumFootprintDimension)
        {
            error = $"Building {record.BuildingInstanceId} must have a footprint of at least "
                + $"{MinimumFootprintDimension} x {MinimumFootprintDimension} cells.";
            return false;
        }

        if (record.StoryCount <= 0)
        {
            error = $"Building {record.BuildingInstanceId} must have a positive story count.";
            return false;
        }

        if (float.IsNaN(doorCornerExclusionDistance)
            || float.IsInfinity(doorCornerExclusionDistance)
            || doorCornerExclusionDistance < 0f)
        {
            error = "Door corner exclusion distance must be finite and non-negative.";
            return false;
        }

        var spans = new List<TestBuildingCreator.ExteriorWallSpan>();
        TestBuildingCreator.GetExteriorWallSpans(
            record.AnchorCell,
            record.FootprintSize,
            spans);
        var seenDoors = new HashSet<string>();
        foreach (var door in record.Doors)
        {
            if (door is null)
            {
                error = $"Building {record.BuildingInstanceId} contains a null door record.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(door.WallId))
            {
                error = $"Building {record.BuildingInstanceId} contains a door without a wall span ID.";
                return false;
            }

            if (float.IsNaN(door.NormalizedOffset)
                || float.IsInfinity(door.NormalizedOffset)
                || door.NormalizedOffset < 0f
                || door.NormalizedOffset > 1f)
            {
                error = $"Building {record.BuildingInstanceId} has a door offset outside 0..1.";
                return false;
            }

            if (!TryFindSpan(spans, door.WallId, out var wall))
            {
                error = $"Building {record.BuildingInstanceId} has a door on unknown wall span '{door.WallId}'.";
                return false;
            }

            if (wall.IsCorner)
            {
                error = $"Building {record.BuildingInstanceId} doors must use straight wall spans.";
                return false;
            }

            var segmentLength = Vector2.Distance(wall.LogicalStart, wall.LogicalEnd);
            var minimumOffset = doorCornerExclusionDistance / segmentLength;
            if (door.NormalizedOffset < minimumOffset
                || door.NormalizedOffset > 1f - minimumOffset)
            {
                error = $"Building {record.BuildingInstanceId} door '{door.WallId}' is too close to a corner.";
                return false;
            }

            var doorKey = $"{door.WallId}:{door.NormalizedOffset:0.######}";
            if (!seenDoors.Add(doorKey))
            {
                error = $"Building {record.BuildingInstanceId} contains duplicate door '{door.WallId}'.";
                return false;
            }
        }

        if (existingRecords is null)
        {
            return true;
        }

        var candidateCells = new List<Vector3Int>();
        BuildingFootprint.GetCells(
            record.AnchorCell,
            record.FootprintSize,
            candidateCells);
        var candidateCellSet = new HashSet<Vector3Int>(candidateCells);
        foreach (var existingRecord in existingRecords)
        {
            if (existingRecord is null
                || existingRecord.BuildingInstanceId == record.BuildingInstanceId)
            {
                continue;
            }

            var existingCells = new List<Vector3Int>();
            BuildingFootprint.GetCells(
                existingRecord.AnchorCell,
                existingRecord.FootprintSize,
                existingCells);
            foreach (var cell in existingCells)
            {
                if (!candidateCellSet.Contains(cell))
                {
                    continue;
                }

                error = $"Building {record.BuildingInstanceId} overlaps building "
                    + $"{existingRecord.BuildingInstanceId} at cell {cell}.";
                return false;
            }
        }

        return true;
    }

    public static bool TryValidate(
        BuildingRecord record,
        IEnumerable<BuildingRecord> existingRecords,
        out string error)
    {
        return TryValidate(
            record,
            existingRecords,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance,
            out error);
    }

    public static bool TryValidateRecords(
        IEnumerable<BuildingRecord> records,
        float doorCornerExclusionDistance,
        out string error)
    {
        error = string.Empty;
        if (records is null)
        {
            return true;
        }

        var list = new List<BuildingRecord>();
        var ids = new HashSet<uint>();
        foreach (var record in records)
        {
            if (record is null)
            {
                error = "Building records cannot contain null entries.";
                return false;
            }

            if (!ids.Add(record.BuildingInstanceId))
            {
                error = $"Duplicate building ID {record.BuildingInstanceId} was found.";
                return false;
            }

            if (!TryValidate(record, list, doorCornerExclusionDistance, out error))
            {
                return false;
            }

            list.Add(record);
        }

        return true;
    }

    public static bool TryValidateRecords(
        IEnumerable<BuildingRecord> records,
        out string error)
    {
        return TryValidateRecords(
            records,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance,
            out error);
    }

    private static bool TryFindSpan(
        IReadOnlyList<TestBuildingCreator.ExteriorWallSpan> spans,
        string wallId,
        out TestBuildingCreator.ExteriorWallSpan wall)
    {
        foreach (var candidate in spans)
        {
            if (candidate.StableId == wallId)
            {
                wall = candidate;
                return true;
            }
        }

        wall = default;
        return false;
    }
}
