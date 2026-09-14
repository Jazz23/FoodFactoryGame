// Stores compact authored building topology separately from generated shell geometry.
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    menuName = "Food Factory/Building Layout",
    fileName = "FactoryBuildingLayout")]
public sealed class FactoryBuildingLayoutAsset : ScriptableObject
{
    public const int CurrentSchemaVersion = 1;

    [SerializeField, HideInInspector] private int schemaVersion = CurrentSchemaVersion;
    [SerializeField] private List<BuildingRecord> buildingRecords = new();

    public int SchemaVersion => schemaVersion;
    public IReadOnlyList<BuildingRecord> BuildingRecords => GetRecords();
    public int Count => GetRecords().Count;

    public void ReplaceRecords(IEnumerable<BuildingRecord> records)
    {
        var replacement = new List<BuildingRecord>();
        if (records is not null)
        {
            foreach (var record in records)
            {
                if (record is not null)
                {
                    replacement.Add(record.Clone());
                }
            }
        }

        replacement.Sort(CompareRecords);
        buildingRecords = replacement;
        schemaVersion = CurrentSchemaVersion;
    }

    public bool TryAddRecord(BuildingRecord record, out string error)
    {
        error = string.Empty;
        if (record is null)
        {
            error = "A building record is required.";
            return false;
        }

        if (record.BuildingInstanceId == 0)
        {
            error = "A building record ID must be greater than zero.";
            return false;
        }

        if (TryGetRecord(record.BuildingInstanceId, out _))
        {
            error = $"Building {record.BuildingInstanceId} already exists in the authored layout.";
            return false;
        }

        var records = CloneRecords();
        records.Add(record);
        ReplaceRecords(records);
        return true;
    }

    public bool TryUpdateRecord(BuildingRecord record, out string error)
    {
        error = string.Empty;
        if (record is null)
        {
            error = "A building record is required.";
            return false;
        }

        var records = CloneRecords();
        for (var index = 0; index < records.Count; index++)
        {
            if (records[index].BuildingInstanceId != record.BuildingInstanceId)
            {
                continue;
            }

            records[index] = record.Clone();
            ReplaceRecords(records);
            return true;
        }

        error = $"Building {record.BuildingInstanceId} was not found in the authored layout.";
        return false;
    }

    public bool TryRemoveRecord(uint buildingInstanceId, out string error)
    {
        error = string.Empty;
        var records = CloneRecords();
        var removed = records.RemoveAll(
            record => record.BuildingInstanceId == buildingInstanceId) > 0;
        if (!removed)
        {
            error = $"Building {buildingInstanceId} was not found in the authored layout.";
            return false;
        }

        ReplaceRecords(records);
        return true;
    }

    public List<BuildingRecord> CloneRecords()
    {
        var clone = new List<BuildingRecord>(Count);
        foreach (var record in GetRecords())
        {
            if (record is not null)
            {
                clone.Add(record.Clone());
            }
        }

        return clone;
    }

    public bool TryGetRecord(
        uint buildingInstanceId,
        out BuildingRecord record)
    {
        foreach (var candidate in GetRecords())
        {
            if (candidate is not null
                && candidate.BuildingInstanceId == buildingInstanceId)
            {
                record = candidate;
                return true;
            }
        }

        record = null!;
        return false;
    }

    public bool HasSameRecords(IEnumerable<BuildingRecord> records)
    {
        var candidates = new List<BuildingRecord>();
        if (records is not null)
        {
            foreach (var record in records)
            {
                if (record is not null)
                {
                    candidates.Add(record);
                }
            }
        }

        candidates.Sort(CompareRecords);
        var authored = GetRecords();
        if (authored.Count != candidates.Count)
        {
            return false;
        }

        for (var index = 0; index < authored.Count; index++)
        {
            if (authored[index] is null
                || !authored[index].HasSameTopology(candidates[index]))
            {
                return false;
            }
        }

        return true;
    }

    public bool TryValidate(
        float doorCornerExclusionDistance,
        out string error)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            error = $"Building layout schema {schemaVersion} is unsupported; expected {CurrentSchemaVersion}.";
            return false;
        }

        return BuildingShellValidation.TryValidateRecords(
            GetRecords(),
            doorCornerExclusionDistance,
            out error);
    }

    private List<BuildingRecord> GetRecords()
    {
        if (buildingRecords is null)
        {
            buildingRecords = new List<BuildingRecord>();
        }

        return buildingRecords;
    }

    private static int CompareRecords(
        BuildingRecord left,
        BuildingRecord right)
    {
        var idComparison = left.BuildingInstanceId.CompareTo(right.BuildingInstanceId);
        if (idComparison != 0)
        {
            return idComparison;
        }

        var anchorComparison = left.AnchorCell.x.CompareTo(right.AnchorCell.x);
        if (anchorComparison != 0)
        {
            return anchorComparison;
        }

        return left.AnchorCell.y.CompareTo(right.AnchorCell.y);
    }
}
