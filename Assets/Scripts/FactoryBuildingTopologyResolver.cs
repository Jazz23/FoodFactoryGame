// Resolves authored and persisted building topology without deciding scene lifecycle behavior.
using System.Collections.Generic;

public static class FactoryBuildingTopologyResolver
{
    public static bool TryResolveImports(
        IEnumerable<BuildingRecord> persistedRecords,
        IEnumerable<BuildingRecord> authoredRecords,
        bool hasAuthoredLayout,
        bool importAuthoredRecords,
        float doorCornerExclusionDistance,
        out List<BuildingRecord> recordsToImport,
        out string error)
    {
        recordsToImport = new List<BuildingRecord>();
        error = string.Empty;
        if (!importAuthoredRecords)
        {
            return true;
        }

        var sourceRecords = new List<BuildingRecord>();
        if (authoredRecords is not null)
        {
            foreach (var record in authoredRecords)
            {
                if (record is not null)
                {
                    sourceRecords.Add(record.Clone());
                }
            }
        }

        if (!BuildingShellValidation.TryValidateRecords(
                sourceRecords,
                doorCornerExclusionDistance,
                out error))
        {
            return false;
        }

        var persistedById = new Dictionary<uint, BuildingRecord>();
        if (persistedRecords is not null)
        {
            foreach (var record in persistedRecords)
            {
                if (record is null)
                {
                    continue;
                }

                if (!persistedById.TryAdd(record.BuildingInstanceId, record.Clone()))
                {
                    error = $"Persisted topology contains duplicate building ID {record.BuildingInstanceId}.";
                    return false;
                }
            }
        }

        foreach (var record in sourceRecords)
        {
            if (persistedById.TryGetValue(record.BuildingInstanceId, out var persistedRecord))
            {
                if (hasAuthoredLayout && !persistedRecord.HasSameTopology(record))
                {
                    error = $"Duplicate building ID {record.BuildingInstanceId} has conflicting layout data.";
                    return false;
                }

                continue;
            }

            recordsToImport.Add(record);
        }

        return true;
    }
}
