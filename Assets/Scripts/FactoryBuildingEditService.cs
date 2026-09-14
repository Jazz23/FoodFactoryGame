// Applies validated building topology edits without silently dropping populated floor state.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public enum FactoryBuildingTopologyOperation
{
    Update,
    Delete,
    Create
}

public sealed class FactoryBuildingTopologyChange
{
    public FactoryBuildingTopologyOperation Operation { get; internal set; }
    public uint BuildingInstanceId { get; internal set; }
    public Guid SavedBuildingGuid { get; internal set; }
    public BuildingRecord CurrentRecord { get; internal set; } = null!;
    public BuildingRecord ProposedRecord { get; internal set; } = null!;
    public Vector2Int CurrentInteriorSize { get; internal set; }
    public Vector2Int ProposedInteriorSize { get; internal set; }
    public int EquipmentCount { get; internal set; }
    public List<uint> AffectedEntityIds { get; } = new();
    public List<Guid> AffectedEntityGuids { get; } = new();
    public List<Guid> AffectedFloorGuids { get; } = new();
    public List<Guid> AffectedConnectionGuids { get; } = new();
    public List<Guid> AffectedRouteGuids { get; } = new();
    public List<Guid> AffectedTruckGuids { get; } = new();
    public List<FactoryBuildingEntityRelocation> Relocations { get; } = new();
    public List<uint> RecoveryEntityIds { get; } = new();
    public List<string> ValidationErrors { get; } = new();

    public bool Changed => IsDelete
        || IsCreate
        || ProposedRecord is null
        || !CurrentRecord.HasSameTopology(ProposedRecord);
    public bool IsDelete => Operation == FactoryBuildingTopologyOperation.Delete;
    public bool IsCreate => Operation == FactoryBuildingTopologyOperation.Create;
    public bool IsDestructive => IsDelete;
    public bool IsValid => ValidationErrors.Count == 0;
}

public sealed class FactoryBuildingTopologyPlan
{
    private readonly List<FactoryBuildingTopologyChange> changes = new();
    private readonly List<string> validationErrors = new();
    private readonly List<BuildingRecord> authoredRecords = new();

    public string DatabasePath { get; internal set; } = string.Empty;
    public string AuthoredFingerprint { get; internal set; } = string.Empty;
    public string DatabaseFingerprint { get; internal set; } = string.Empty;
    public FactoryWorldSnapshot SourceSnapshot { get; internal set; } = null!;
    public IReadOnlyList<FactoryBuildingTopologyChange> Changes => changes;
    public IReadOnlyList<string> ValidationErrors => validationErrors;
    public bool HasChanges => changes.Count > 0;
    public bool IsValid => validationErrors.Count == 0;

    internal List<FactoryBuildingTopologyChange> ChangesInternal => changes;
    internal List<string> ValidationErrorsInternal => validationErrors;
    internal List<BuildingRecord> AuthoredRecordsInternal => authoredRecords;

    public bool TryGetChange(
        uint buildingInstanceId,
        out FactoryBuildingTopologyChange change)
    {
        foreach (var candidate in changes)
        {
            if (candidate.BuildingInstanceId == buildingInstanceId)
            {
                change = candidate;
                return true;
            }
        }

        change = null!;
        return false;
    }
}

public sealed class FactoryBuildingTopologyApplyResult
{
    public string DatabasePath { get; internal set; } = string.Empty;
    public bool Changed { get; internal set; }
    public bool Saved { get; internal set; }
    public List<uint> AppliedBuildingIds { get; } = new();
    public List<Guid> CreatedBuildingGuids { get; } = new();
    public List<Guid> CreatedFloorGuids { get; } = new();
    public List<Guid> CreatedEntityGuids { get; } = new();
    public List<uint> DeletedBuildingIds { get; } = new();
    public List<Guid> DeletedBuildingGuids { get; } = new();
    public List<Guid> DeletedFloorGuids { get; } = new();
    public List<Guid> DeletedEntityGuids { get; } = new();
    public List<Guid> DeletedConnectionGuids { get; } = new();
    public List<Guid> DeletedRouteGuids { get; } = new();
    public List<Guid> DeletedTruckGuids { get; } = new();
    public List<FactoryBuildingEntityRelocation> Relocations { get; } = new();
    public List<uint> RecoveryEntityIds { get; } = new();
    public int PreservedBuildingCount { get; internal set; }
    public int PreservedFloorCount { get; internal set; }
    public int PreservedEntityCount { get; internal set; }
    public int PreservedConnectionCount { get; internal set; }
    public int PreservedRouteCount { get; internal set; }
    public int PreservedTruckCount { get; internal set; }
}

public sealed class FactoryBuildingEditResult
{
    public bool Changed { get; internal set; }
    public IReadOnlyList<FactoryBuildingEntityRelocation> Relocations => relocations;
    public IReadOnlyList<uint> RecoveryEntityIds => recoveryEntityIds;

    internal List<FactoryBuildingEntityRelocation> RelocationsInternal => relocations;
    internal List<uint> RecoveryEntityIdsInternal => recoveryEntityIds;

    private readonly List<FactoryBuildingEntityRelocation> relocations = new();
    private readonly List<uint> recoveryEntityIds = new();
}

public sealed class FactoryBuildingEntityRelocation
{
    public uint EntityId { get; internal set; }
    public Guid EntityGuid { get; internal set; }
    public int FromFloorIndex { get; internal set; }
    public int ToFloorIndex { get; internal set; }
    public Vector2 FromPosition { get; internal set; }
    public Vector2 ToPosition { get; internal set; }
}

public static class FactoryBuildingEditService
{
    public static bool TryCreateTopologyPlan(
        string databasePath,
        IEnumerable<BuildingRecord> authoredRecords,
        float doorCornerExclusionDistance,
        out FactoryBuildingTopologyPlan plan,
        out string error)
    {
        plan = new FactoryBuildingTopologyPlan
        {
            DatabasePath = NormalizeDatabasePath(databasePath)
        };
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            error = "An explicit factory database path is required.";
            plan.ValidationErrorsInternal.Add(error);
            return false;
        }

        try
        {
            var snapshot = new FactoryWorldSqliteStore(plan.DatabasePath).Load();
            var valid = TryCreateTopologyPlan(
                snapshot,
                authoredRecords,
                doorCornerExclusionDistance,
                out plan,
                out error);
            plan.DatabasePath = NormalizeDatabasePath(databasePath);
            return valid;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            plan.ValidationErrorsInternal.Add(error);
            return false;
        }
    }

    public static bool TryCreateTopologyPlan(
        FactoryWorldSnapshot savedWorld,
        IEnumerable<BuildingRecord> authoredRecords,
        float doorCornerExclusionDistance,
        out FactoryBuildingTopologyPlan plan,
        out string error)
    {
        plan = new FactoryBuildingTopologyPlan
        {
            SourceSnapshot = savedWorld is null ? null! : savedWorld.Clone()
        };
        error = string.Empty;
        if (savedWorld is null)
        {
            return AddPlanError(plan, "A saved factory world is required.", out error);
        }

        plan.DatabaseFingerprint = GetSnapshotFingerprint(savedWorld);

        if (!FactoryWorldValidation.TryValidate(savedWorld, out var snapshotError))
        {
            return AddPlanError(plan, snapshotError, out error);
        }

        var authoredById = new Dictionary<uint, BuildingRecord>();
        if (authoredRecords is not null)
        {
            foreach (var authoredRecord in authoredRecords)
            {
                if (authoredRecord is null)
                {
                    AddPlanError(plan, "Authored building records cannot contain null entries.", out _);
                    continue;
                }

                if (!authoredById.TryAdd(
                        authoredRecord.BuildingInstanceId,
                        authoredRecord.Clone()))
                {
                    AddPlanError(
                        plan,
                        $"Duplicate authored building ID {authoredRecord.BuildingInstanceId}.",
                        out _);
                }
            }
        }

        foreach (var authoredRecord in authoredById.Values)
        {
            plan.AuthoredRecordsInternal.Add(authoredRecord.Clone());
        }

        plan.AuthoredFingerprint = GetRecordsFingerprint(plan.AuthoredRecordsInternal);

        var savedById = new Dictionary<uint, FactoryWorldBuildingRecord>();
        foreach (var savedBuilding in savedWorld.Buildings)
        {
            if (savedBuilding is null || savedBuilding.InteriorOnly)
            {
                continue;
            }

            if (!savedById.TryAdd(savedBuilding.LegacyBuildingId, savedBuilding))
            {
                AddPlanError(
                    plan,
                    $"Saved world contains duplicate exterior building ID {savedBuilding.LegacyBuildingId}.",
                    out _);
            }
        }

        var effectiveRecords = new List<BuildingRecord>();
        foreach (var savedBuilding in savedById.Values)
        {
            effectiveRecords.Add(
                authoredById.TryGetValue(
                    savedBuilding.LegacyBuildingId,
                    out var authoredRecord)
                    ? authoredRecord.Clone()
                     : ToBuildingRecord(savedBuilding));
        }

        foreach (var authoredPair in authoredById)
        {
            if (!savedById.ContainsKey(authoredPair.Key))
            {
                effectiveRecords.Add(authoredPair.Value.Clone());
            }
        }

        if (!BuildingShellValidation.TryValidateRecords(
                effectiveRecords,
                doorCornerExclusionDistance,
                out var effectiveError))
        {
            AddPlanError(plan, effectiveError, out _);
        }

        foreach (var authoredPair in authoredById)
        {
            if (!savedById.TryGetValue(authoredPair.Key, out var savedBuilding))
            {
                var createdRecord = authoredPair.Value.Clone();
                var createChange = new FactoryBuildingTopologyChange
                {
                    Operation = FactoryBuildingTopologyOperation.Create,
                    BuildingInstanceId = authoredPair.Key,
                    SavedBuildingGuid = FactoryGuidMigration.ForBuilding(authoredPair.Key),
                    ProposedRecord = createdRecord,
                    CurrentInteriorSize = Vector2Int.zero,
                    ProposedInteriorSize = GetInteriorSize(createdRecord, false)
                };
                if (!TryValidateNewBuilding(
                        savedWorld,
                        createdRecord,
                        doorCornerExclusionDistance,
                        out var createError))
                {
                    AddChangeError(plan, createChange, createError);
                }

                plan.ChangesInternal.Add(createChange);
                continue;
            }

            var currentRecord = ToBuildingRecord(savedBuilding);
            var proposedRecord = authoredPair.Value.Clone();
            if (currentRecord.HasSameTopology(proposedRecord))
            {
                continue;
            }

            var change = new FactoryBuildingTopologyChange
            {
                BuildingInstanceId = authoredPair.Key,
                SavedBuildingGuid = savedBuilding.Guid,
                CurrentRecord = currentRecord,
                ProposedRecord = proposedRecord,
                CurrentInteriorSize = GetInteriorSize(savedBuilding),
                ProposedInteriorSize = GetInteriorSize(
                    proposedRecord,
                    savedBuilding.InteriorOnly)
            };
            PopulateAffectedEntities(savedWorld, savedBuilding.Guid, change);

            var candidate = savedWorld.Clone();
            if (!TryApplyBuildingRecordToSnapshot(
                    candidate,
                    proposedRecord,
                    doorCornerExclusionDistance,
                    change,
                    out var changeError))
            {
                AddChangeError(plan, change, changeError);
            }

            plan.ChangesInternal.Add(change);
        }

        foreach (var savedPair in savedById)
        {
            if (authoredById.ContainsKey(savedPair.Key))
            {
                continue;
            }

            var change = new FactoryBuildingTopologyChange
            {
                Operation = FactoryBuildingTopologyOperation.Delete,
                BuildingInstanceId = savedPair.Key,
                SavedBuildingGuid = savedPair.Value.Guid,
                CurrentRecord = ToBuildingRecord(savedPair.Value),
                ProposedRecord = null!,
                CurrentInteriorSize = GetInteriorSize(savedPair.Value),
                ProposedInteriorSize = Vector2Int.zero
            };
            PopulateDeletionDependencies(savedWorld, savedPair.Value.Guid, change);
            plan.ChangesInternal.Add(change);
        }

        if (plan.ValidationErrors.Count > 0)
        {
            error = plan.ValidationErrors[0];
            return false;
        }

        return true;
    }

    public static bool TryApplyTopologyChanges(
        FactoryWorldSnapshot savedWorld,
        IEnumerable<BuildingRecord> authoredRecords,
        IEnumerable<uint> selectedBuildingIds,
        float doorCornerExclusionDistance,
        out FactoryWorldSnapshot updatedWorld,
        out FactoryBuildingTopologyApplyResult result,
        out string error)
    {
        return TryApplyTopologyChanges(
            savedWorld,
            authoredRecords,
            selectedBuildingIds,
            doorCornerExclusionDistance,
            false,
            out updatedWorld,
            out result,
            out error);
    }

    public static bool TryApplyTopologyChanges(
        FactoryWorldSnapshot savedWorld,
        IEnumerable<BuildingRecord> authoredRecords,
        IEnumerable<uint> selectedBuildingIds,
        float doorCornerExclusionDistance,
        bool confirmDestructiveDeletes,
        out FactoryWorldSnapshot updatedWorld,
        out FactoryBuildingTopologyApplyResult result,
        out string error)
    {
        updatedWorld = savedWorld is null ? null! : savedWorld.Clone();
        result = new FactoryBuildingTopologyApplyResult();
        error = string.Empty;
        if (savedWorld is null)
        {
            error = "A saved factory world is required.";
            return false;
        }

        if (!TryCreateTopologyPlan(
                savedWorld,
                authoredRecords,
                doorCornerExclusionDistance,
                out var plan,
                out error))
        {
            return false;
        }

        var selectedIds = new HashSet<uint>();
        if (selectedBuildingIds is not null)
        {
            foreach (var buildingId in selectedBuildingIds)
            {
                if (buildingId != 0)
                {
                    selectedIds.Add(buildingId);
                }
            }
        }

        var applyAll = selectedIds.Count == 0;
        foreach (var change in plan.Changes)
        {
            if (!applyAll && !selectedIds.Contains(change.BuildingInstanceId))
            {
                continue;
            }

            if (!change.IsValid)
            {
                error = change.ValidationErrors[0];
                return false;
            }

            if (change.IsDelete && !confirmDestructiveDeletes)
            {
                error = "Whole-building deletion requires destructive confirmation.";
                return false;
            }

            var appliedChange = new FactoryBuildingTopologyChange
            {
                Operation = change.Operation,
                BuildingInstanceId = change.BuildingInstanceId,
                SavedBuildingGuid = change.SavedBuildingGuid,
                CurrentRecord = change.CurrentRecord is null
                    ? null!
                    : change.CurrentRecord.Clone(),
                ProposedRecord = change.ProposedRecord is null
                    ? null!
                    : change.ProposedRecord.Clone(),
                CurrentInteriorSize = change.CurrentInteriorSize,
                ProposedInteriorSize = change.ProposedInteriorSize
            };
            if (change.IsDelete)
            {
                PopulateDeletionDependencies(
                    updatedWorld,
                    change.SavedBuildingGuid,
                    appliedChange);
            }
            else if (!change.IsCreate)
            {
                PopulateAffectedEntities(
                    updatedWorld,
                    change.SavedBuildingGuid,
                    appliedChange);
            }
            if (appliedChange.IsDelete)
            {
                ApplyDeletionToSnapshot(updatedWorld, appliedChange, result);
            }
            else if (appliedChange.IsCreate)
            {
                ApplyCreationToSnapshot(
                    updatedWorld,
                    appliedChange,
                    result);
            }
            else if (!TryApplyBuildingRecordToSnapshot(
                         updatedWorld,
                         appliedChange.ProposedRecord,
                         doorCornerExclusionDistance,
                         appliedChange,
                         out error))
            {
                return false;
            }

            result.Changed = true;
            if (appliedChange.IsDelete)
            {
                result.DeletedBuildingIds.Add(change.BuildingInstanceId);
            }
            else
            {
                result.AppliedBuildingIds.Add(change.BuildingInstanceId);
            }
            result.Relocations.AddRange(appliedChange.Relocations);
            result.RecoveryEntityIds.AddRange(appliedChange.RecoveryEntityIds);
        }

        if (!result.Changed)
        {
            return true;
        }

        if (!FactoryWorldValidation.TryValidate(updatedWorld, out error))
        {
            return false;
        }

        if (!TryVerifyTopologyResult(savedWorld, updatedWorld, result, out error))
        {
            return false;
        }

        PopulatePreservationCounts(savedWorld, updatedWorld, result);
        return true;
    }

    public static bool TryApplyTopologyToDatabase(
        string databasePath,
        IEnumerable<BuildingRecord> authoredRecords,
        IEnumerable<uint> selectedBuildingIds,
        float doorCornerExclusionDistance,
        out FactoryBuildingTopologyApplyResult result,
        out string error)
    {
        result = new FactoryBuildingTopologyApplyResult
        {
            DatabasePath = NormalizeDatabasePath(databasePath)
        };
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            error = "An explicit factory database path is required.";
            return false;
        }

        try
        {
            var store = new FactoryWorldSqliteStore(result.DatabasePath);
            var source = store.Load();
            if (!TryApplyTopologyChanges(
                    source,
                    authoredRecords,
                    selectedBuildingIds,
                    doorCornerExclusionDistance,
                    out var updated,
                    out result,
                    out error))
            {
                result.DatabasePath = NormalizeDatabasePath(databasePath);
                return false;
            }

            result.DatabasePath = NormalizeDatabasePath(databasePath);
            if (!result.Changed)
            {
                return true;
            }

            if (result.DeletedBuildingIds.Count > 0)
            {
                error = "Whole-building deletion requires destructive confirmation.";
                return false;
            }

            store.SaveAtomically(updated);
            var persisted = store.Load();

            if (!TryVerifyTopologyResult(source, persisted, result, out error)
                || !HasAppliedTopologies(persisted, updated, result.AppliedBuildingIds, out error))
            {
                try
                {
                    store.SaveAtomically(source);
                }
                catch (Exception restoreException)
                {
                    error += $" Restore failed: {restoreException.Message}";
                }

                result.Saved = false;
                return false;
            }

            result.Saved = true;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            result.Saved = false;
            return false;
        }
    }

    public static bool TryApplyTopologyPlan(
        string databasePath,
        FactoryBuildingTopologyPlan plan,
        IEnumerable<BuildingRecord> authoredRecords,
        IEnumerable<uint> selectedBuildingIds,
        bool confirmDestructiveDeletes,
        float doorCornerExclusionDistance,
        out FactoryBuildingTopologyApplyResult result,
        out string error)
    {
        result = new FactoryBuildingTopologyApplyResult
        {
            DatabasePath = NormalizeDatabasePath(databasePath)
        };
        error = string.Empty;
        if (plan is null || !plan.IsValid)
        {
            error = plan?.ValidationErrors.Count > 0
                ? plan.ValidationErrors[0]
                : "A valid topology plan is required.";
            return false;
        }

        if (!string.Equals(
                result.DatabasePath,
                NormalizeDatabasePath(plan.DatabasePath),
                StringComparison.OrdinalIgnoreCase))
        {
            error = "The selected database does not match the previewed topology plan.";
            return false;
        }

        var selectedIds = new HashSet<uint>();
        if (selectedBuildingIds is not null)
        {
            foreach (var buildingId in selectedBuildingIds)
            {
                if (buildingId != 0)
                {
                    selectedIds.Add(buildingId);
                }
            }
        }

        var store = new FactoryWorldSqliteStore(result.DatabasePath);
        var current = store.Load();
        if (!string.Equals(
                plan.DatabaseFingerprint,
                GetSnapshotFingerprint(current),
                StringComparison.Ordinal))
        {
            error = "The selected database changed after the topology preview; preview it again.";
            return false;
        }

        if (!string.Equals(
                plan.AuthoredFingerprint,
                GetRecordsFingerprint(authoredRecords),
                StringComparison.Ordinal))
        {
            error = "The authored layout changed after the topology preview; preview it again.";
            return false;
        }

        if (!TryApplyTopologyChanges(
                current,
                plan.AuthoredRecordsInternal,
                selectedIds,
                doorCornerExclusionDistance,
                confirmDestructiveDeletes,
                out var updated,
                out result,
                out error))
        {
            result.DatabasePath = NormalizeDatabasePath(databasePath);
            return false;
        }

        result.DatabasePath = NormalizeDatabasePath(databasePath);
        if (!result.Changed)
        {
            return true;
        }

        if (result.DeletedBuildingIds.Count > 0 && !confirmDestructiveDeletes)
        {
            error = "Whole-building deletion requires destructive confirmation.";
            return false;
        }

        try
        {
            store.SaveAtomically(updated);
            var persisted = store.Load();
            if (!FactoryWorldValidation.TryValidate(persisted, out error)
                || !HasAppliedTopologies(persisted, updated, result.AppliedBuildingIds, out error)
                || !HasAppliedDeletions(persisted, result, out error))
            {
                store.SaveAtomically(current);
                return false;
            }

            result.Saved = true;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            result.Saved = false;
            return false;
        }
    }

    private static bool TryValidateNewBuilding(
        FactoryWorldSnapshot snapshot,
        BuildingRecord proposedRecord,
        float doorCornerExclusionDistance,
        out string error)
    {
        var buildingGuid = FactoryGuidMigration.ForBuilding(proposedRecord.BuildingInstanceId);
        foreach (var building in snapshot.Buildings)
        {
            if (building is not null
                && (building.LegacyBuildingId == proposedRecord.BuildingInstanceId
                    || building.Guid == buildingGuid))
            {
                error = $"Building {proposedRecord.BuildingInstanceId} already exists in the saved world.";
                return false;
            }
        }

        var otherRecords = new List<BuildingRecord>();
        foreach (var building in snapshot.Buildings)
        {
            if (building is not null && !building.InteriorOnly)
            {
                otherRecords.Add(ToBuildingRecord(building));
            }
        }

        return BuildingShellValidation.TryValidate(
            proposedRecord,
            otherRecords,
            doorCornerExclusionDistance,
            out error);
    }

    private static void ApplyCreationToSnapshot(
        FactoryWorldSnapshot snapshot,
        FactoryBuildingTopologyChange change,
        FactoryBuildingTopologyApplyResult result)
    {
        var buildingId = change.BuildingInstanceId;
        var buildingGuid = change.SavedBuildingGuid;
        var record = change.ProposedRecord;
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingGuid,
            "outside-test-building",
            record.AnchorCell,
            record.FootprintSize,
            record.StoryCount,
            false,
            buildingId,
            record.Doors));
        AddMigrationMapping(snapshot, "building", buildingId.ToString(), buildingGuid);
        result.CreatedBuildingGuids.Add(buildingGuid);

        for (var floorIndex = 0; floorIndex < record.StoryCount; floorIndex++)
        {
            var floor = CreateDefaultWorldFloor(
                buildingId,
                buildingGuid,
                floorIndex,
                record.FootprintSize);
            snapshot.Floors.Add(floor);
            AddMigrationMapping(
                snapshot,
                "floor",
                $"{buildingId}:{floorIndex}",
                floor.Guid);
            result.CreatedFloorGuids.Add(floor.Guid);
            foreach (var entity in floor.Entities)
            {
                AddMigrationMapping(
                    snapshot,
                    "entity",
                    $"{buildingId}:{floorIndex}:{entity.LegacyEntityId}",
                    entity.Guid);
                result.CreatedEntityGuids.Add(entity.Guid);
            }
        }
    }

    private static void AddMigrationMapping(
        FactoryWorldSnapshot snapshot,
        string kind,
        string legacyKey,
        Guid guid)
    {
        foreach (var mapping in snapshot.MigrationMappings)
        {
            if (mapping.Kind == kind && mapping.LegacyKey == legacyKey)
            {
                return;
            }
        }

        snapshot.MigrationMappings.Add(new FactoryWorldMigrationMapping(kind, legacyKey, guid));
    }

    private static bool TryApplyBuildingRecordToSnapshot(
        FactoryWorldSnapshot snapshot,
        BuildingRecord proposedRecord,
        float doorCornerExclusionDistance,
        FactoryBuildingTopologyChange change,
        out string error)
    {
        error = string.Empty;
        change.Relocations.Clear();
        change.RecoveryEntityIds.Clear();
        var buildingIndex = -1;
        FactoryWorldBuildingRecord currentBuilding = null!;
        for (var index = 0; index < snapshot.Buildings.Count; index++)
        {
            var candidate = snapshot.Buildings[index];
            if (candidate is not null
                && candidate.LegacyBuildingId == proposedRecord.BuildingInstanceId
                && !candidate.InteriorOnly)
            {
                buildingIndex = index;
                currentBuilding = candidate;
                break;
            }
        }

        if (buildingIndex < 0)
        {
            error = $"Saved exterior building {proposedRecord.BuildingInstanceId} was not found.";
            return false;
        }

        var otherRecords = new List<BuildingRecord>();
        foreach (var building in snapshot.Buildings)
        {
            if (building is not null
                && !building.InteriorOnly
                && building.LegacyBuildingId != proposedRecord.BuildingInstanceId)
            {
                otherRecords.Add(ToBuildingRecord(building));
            }
        }

        if (!BuildingShellValidation.TryValidate(
                proposedRecord,
                otherRecords,
                doorCornerExclusionDistance,
                out error))
        {
            return false;
        }

        var currentRecord = ToBuildingRecord(currentBuilding);
        if (currentRecord.HasSameTopology(proposedRecord))
        {
            return true;
        }

        var targetBuildingGuid = currentBuilding.Guid;
        var existingFloors = new Dictionary<int, FactoryWorldFloorRecord>();
        foreach (var floor in snapshot.Floors)
        {
            if (floor.BuildingGuid == targetBuildingGuid)
            {
                existingFloors.Add(floor.FloorIndex, floor);
            }
        }

        var retainedFloors = new List<FloorPlacementState>();
        for (var floorIndex = 0; floorIndex < proposedRecord.StoryCount; floorIndex++)
        {
            var floor = existingFloors.TryGetValue(floorIndex, out var existingFloor)
                ? existingFloor.Clone()
                : CreateDefaultWorldFloor(
                    proposedRecord.BuildingInstanceId,
                    targetBuildingGuid,
                    floorIndex,
                    proposedRecord.FootprintSize);
            floor.SetState(
                floor.Label,
                floor.ProductionRate,
                floor.AccumulatedProduction,
                OutsideTestFloorRecord.ClampMarkerPosition(
                    floor.MarkerPosition,
                    GetInteriorSize(proposedRecord, false)));
            retainedFloors.Add(new FloorPlacementState(floor));
        }

        var displaced = new List<EntityPlacementRequest>();
        foreach (var floorState in retainedFloors)
        {
            var entities = new List<FactoryWorldEntityRecord>(floorState.Floor.Entities);
            entities.Sort(CompareWorldEntities);
            foreach (var entity in entities)
            {
                if (entity is null)
                {
                    continue;
                }

                var cell = Vector2Int.FloorToInt(entity.LocalPosition);
                if (BuildingFootprint.IsUsableInteriorPosition(
                        entity.LocalPosition,
                        GetInteriorSize(proposedRecord, false))
                    && floorState.OccupiedCells.Add(cell)
                    && floorState.EntityIds.Add(entity.LegacyEntityId))
                {
                    continue;
                }

                displaced.Add(new EntityPlacementRequest(
                    floorState,
                    entity,
                    floorState.Floor.FloorIndex));
            }
        }

        var removedFloorEntities = new List<EntityPlacementRequest>();
        foreach (var pair in existingFloors)
        {
            if (pair.Key < proposedRecord.StoryCount)
            {
                continue;
            }

            foreach (var entity in pair.Value.Entities)
            {
                if (entity is not null)
                {
                    removedFloorEntities.Add(new EntityPlacementRequest(
                        null!,
                        entity,
                        pair.Key,
                        pair.Value));
                }
            }
        }

        displaced.AddRange(removedFloorEntities);
        displaced.Sort(ComparePlacementRequests);
        var endpointMoves = new Dictionary<Guid, FactoryWorldEndpoint>();
        foreach (var request in displaced)
        {
            if (!TryFindTargetFloor(
                    request,
                    retainedFloors,
                    GetInteriorSize(proposedRecord, false),
                    out var targetFloor,
                    out var targetCell))
            {
                change.RecoveryEntityIds.Add(request.Entity.LegacyEntityId);
                error = $"Building {proposedRecord.BuildingInstanceId} cannot retain entity "
                    + $"{request.Entity.LegacyEntityId} while applying the proposed topology.";
                return false;
            }

            var targetPosition = (Vector2)targetCell + Vector2.one * 0.5f;
            var sourceFloorIndex = request.SourceFloorIndex;
            var sourcePosition = request.Entity.LocalPosition;
            if (request.Source is not null && request.Source == targetFloor)
            {
                request.Entity.SetLocalPosition(targetPosition);
            }
            else
            {
                if (request.Source is not null)
                {
                    RemoveWorldEntity(request.Source.Floor, request.Entity.Guid);
                }
                else
                {
                    RemoveWorldEntity(request.RemovedFloor, request.Entity.Guid);
                }

                var targetEntities = new List<FactoryWorldEntityRecord>(targetFloor.Floor.Entities)
                {
                    CloneWorldEntity(
                        request.Entity,
                        targetFloor.Floor.Guid,
                        targetPosition)
                };
                targetFloor.Floor.SetEntities(targetEntities);
            }

            targetFloor.OccupiedCells.Add(targetCell);
            targetFloor.EntityIds.Add(request.Entity.LegacyEntityId);
            if (request.Source is null || request.Source != targetFloor)
            {
                endpointMoves[request.Entity.Guid] = new FactoryWorldEndpoint(
                    targetBuildingGuid,
                    targetFloor.Floor.Guid,
                    request.Entity.Guid,
                    targetFloor.Floor.FloorIndex);
            }

            change.Relocations.Add(new FactoryBuildingEntityRelocation
            {
                EntityId = request.Entity.LegacyEntityId,
                EntityGuid = request.Entity.Guid,
                FromFloorIndex = sourceFloorIndex,
                ToFloorIndex = targetFloor.Floor.FloorIndex,
                FromPosition = sourcePosition,
                ToPosition = targetPosition
            });
        }

        snapshot.Buildings[buildingIndex] = new FactoryWorldBuildingRecord(
            currentBuilding.Guid,
            currentBuilding.DefinitionId,
            proposedRecord.AnchorCell,
            proposedRecord.FootprintSize,
            proposedRecord.StoryCount,
            currentBuilding.InteriorOnly,
            currentBuilding.LegacyBuildingId,
            proposedRecord.Doors);

        var replacementFloors = new List<FactoryWorldFloorRecord>();
        foreach (var floor in snapshot.Floors)
        {
            if (floor.BuildingGuid != targetBuildingGuid)
            {
                replacementFloors.Add(floor);
            }
        }

        foreach (var floorState in retainedFloors)
        {
            replacementFloors.Add(floorState.Floor);
        }

        replacementFloors.Sort((left, right) =>
        {
            var buildingComparison = left.BuildingGuid.CompareTo(right.BuildingGuid);
            return buildingComparison != 0
                ? buildingComparison
                : left.FloorIndex.CompareTo(right.FloorIndex);
        });
        snapshot.Floors = replacementFloors;
        RemapConnections(snapshot, endpointMoves);
        RemapRoutes(snapshot, endpointMoves);
        return true;
    }

    public static bool TryApplyTopologyPlan(
        string databasePath,
        FactoryBuildingTopologyPlan plan,
        IEnumerable<uint> selectedBuildingIds,
        bool confirmDestructiveDeletes,
        float doorCornerExclusionDistance,
        out FactoryBuildingTopologyApplyResult result,
        out string error)
    {
        return TryApplyTopologyPlan(
            databasePath,
            plan,
            plan?.AuthoredRecordsInternal,
            selectedBuildingIds,
            confirmDestructiveDeletes,
            doorCornerExclusionDistance,
            out result,
            out error);
    }

    private static void PopulateDeletionDependencies(
        FactoryWorldSnapshot snapshot,
        Guid buildingGuid,
        FactoryBuildingTopologyChange change)
    {
        var floorGuids = new HashSet<Guid>();
        var entityGuids = new HashSet<Guid>();
        foreach (var floor in snapshot.Floors)
        {
            if (floor.BuildingGuid != buildingGuid)
            {
                continue;
            }

            floorGuids.Add(floor.Guid);
            change.AffectedFloorGuids.Add(floor.Guid);
            foreach (var entity in floor.Entities)
            {
                if (entity is null)
                {
                    continue;
                }

                entityGuids.Add(entity.Guid);
                change.EquipmentCount++;
                change.AffectedEntityIds.Add(entity.LegacyEntityId);
                change.AffectedEntityGuids.Add(entity.Guid);
            }
        }

        foreach (var connection in snapshot.Connections)
        {
            if (entityGuids.Contains(connection.Source.EntityGuid)
                || entityGuids.Contains(connection.Destination.EntityGuid))
            {
                change.AffectedConnectionGuids.Add(connection.Guid);
            }
        }

        var routeGuids = new HashSet<Guid>();
        foreach (var route in snapshot.Routes)
        {
            if (!entityGuids.Contains(route.Source.EntityGuid)
                && !entityGuids.Contains(route.Destination.EntityGuid))
            {
                continue;
            }

            routeGuids.Add(route.Guid);
            change.AffectedRouteGuids.Add(route.Guid);
        }

        foreach (var truck in snapshot.Trucks)
        {
            if (routeGuids.Contains(truck.RouteGuid))
            {
                change.AffectedTruckGuids.Add(truck.Guid);
            }
        }
    }

    private static void ApplyDeletionToSnapshot(
        FactoryWorldSnapshot snapshot,
        FactoryBuildingTopologyChange change,
        FactoryBuildingTopologyApplyResult result)
    {
        var buildingGuid = change.SavedBuildingGuid;
        var floorGuids = new HashSet<Guid>(change.AffectedFloorGuids);
        var entityGuids = new HashSet<Guid>(change.AffectedEntityGuids);
        var connectionGuids = new HashSet<Guid>(change.AffectedConnectionGuids);
        var routeGuids = new HashSet<Guid>(change.AffectedRouteGuids);
        var truckGuids = new HashSet<Guid>(change.AffectedTruckGuids);

        snapshot.Buildings.RemoveAll(building => building.Guid == buildingGuid);
        snapshot.Floors.RemoveAll(floor => floorGuids.Contains(floor.Guid));
        snapshot.Connections.RemoveAll(connection => connectionGuids.Contains(connection.Guid));
        snapshot.Routes.RemoveAll(route => routeGuids.Contains(route.Guid));
        snapshot.Trucks.RemoveAll(truck => truckGuids.Contains(truck.Guid));
        snapshot.MigrationMappings.RemoveAll(mapping =>
            mapping.Guid == buildingGuid
            || floorGuids.Contains(mapping.Guid)
            || entityGuids.Contains(mapping.Guid)
            || connectionGuids.Contains(mapping.Guid)
            || routeGuids.Contains(mapping.Guid)
            || truckGuids.Contains(mapping.Guid));

        result.DeletedBuildingGuids.Add(buildingGuid);
        result.DeletedFloorGuids.AddRange(floorGuids);
        result.DeletedEntityGuids.AddRange(entityGuids);
        result.DeletedConnectionGuids.AddRange(connectionGuids);
        result.DeletedRouteGuids.AddRange(routeGuids);
        result.DeletedTruckGuids.AddRange(truckGuids);
    }

    private static bool TryFindTargetFloor(
        EntityPlacementRequest request,
        IReadOnlyList<FloorPlacementState> floors,
        Vector2Int interiorSize,
        out FloorPlacementState targetFloor,
        out Vector2Int targetCell)
    {
        targetFloor = null!;
        targetCell = default;
        var found = false;
        var bestFloorDistance = int.MaxValue;
        var bestDistance = float.PositiveInfinity;
        foreach (var floor in floors)
        {
            if (floor.EntityIds.Contains(request.Entity.LegacyEntityId))
            {
                continue;
            }

            for (var y = 0; y < interiorSize.y; y++)
            {
                for (var x = 0; x < interiorSize.x; x++)
                {
                    var candidate = new Vector2Int(x, y);
                    if (floor.OccupiedCells.Contains(candidate))
                    {
                        continue;
                    }

                    var floorDistance = Mathf.Abs(
                        floor.Floor.FloorIndex - request.SourceFloorIndex);
                    var center = (Vector2)candidate + Vector2.one * 0.5f;
                    var distance = (center - request.Entity.LocalPosition).sqrMagnitude;
                    if (found
                        && !IsBetterTarget(
                            floorDistance,
                            distance,
                            floor.Floor.FloorIndex,
                            candidate,
                            bestFloorDistance,
                            bestDistance,
                            targetFloor.Floor.FloorIndex,
                            targetCell))
                    {
                        continue;
                    }

                    found = true;
                    bestFloorDistance = floorDistance;
                    bestDistance = distance;
                    targetFloor = floor;
                    targetCell = candidate;
                }
            }
        }

        return found;
    }

    private static void RemapConnections(
        FactoryWorldSnapshot snapshot,
        IReadOnlyDictionary<Guid, FactoryWorldEndpoint> endpointMoves)
    {
        for (var index = 0; index < snapshot.Connections.Count; index++)
        {
            var connection = snapshot.Connections[index];
            var source = endpointMoves.TryGetValue(
                    connection.Source.EntityGuid,
                    out var movedSource)
                ? movedSource
                : connection.Source;
            var destination = endpointMoves.TryGetValue(
                    connection.Destination.EntityGuid,
                    out var movedDestination)
                ? movedDestination
                : connection.Destination;
            snapshot.Connections[index] = new FactoryWorldConnectionRecord(
                connection.Guid,
                source,
                destination);
        }
    }

    private static void RemapRoutes(
        FactoryWorldSnapshot snapshot,
        IReadOnlyDictionary<Guid, FactoryWorldEndpoint> endpointMoves)
    {
        for (var index = 0; index < snapshot.Routes.Count; index++)
        {
            var route = snapshot.Routes[index];
            var source = endpointMoves.TryGetValue(
                    route.Source.EntityGuid,
                    out var movedSource)
                ? movedSource
                : route.Source;
            var destination = endpointMoves.TryGetValue(
                    route.Destination.EntityGuid,
                    out var movedDestination)
                ? movedDestination
                : route.Destination;
            snapshot.Routes[index] = new FactoryWorldTruckRouteRecord(
                route.Guid,
                route.TruckGuid,
                source,
                destination,
                route.ItemId,
                route.CargoCapacity,
                route.TransferRateItemsPerSecond,
                route.OutboundTravelSeconds,
                route.ReturnTravelSeconds,
                route.PartialLoadDepartureWindowSeconds);
        }
    }

    private static void PopulateAffectedEntities(
        FactoryWorldSnapshot snapshot,
        Guid buildingGuid,
        FactoryBuildingTopologyChange change)
    {
        foreach (var floor in snapshot.Floors)
        {
            if (floor.BuildingGuid != buildingGuid)
            {
                continue;
            }

            foreach (var entity in floor.Entities)
            {
                if (entity is null)
                {
                    continue;
                }

                change.EquipmentCount++;
                change.AffectedEntityIds.Add(entity.LegacyEntityId);
                change.AffectedEntityGuids.Add(entity.Guid);
            }
        }
    }

    private static bool TryVerifyTopologyResult(
        FactoryWorldSnapshot before,
        FactoryWorldSnapshot after,
        FactoryBuildingTopologyApplyResult result,
        out string error)
    {
        if (result.DeletedBuildingIds.Count == 0)
        {
            return TryVerifyPreservedState(before, after, result, out error);
        }

        return TryVerifyRemainingState(before, after, result, out error);
    }

    private static bool TryVerifyPreservedState(
        FactoryWorldSnapshot before,
        FactoryWorldSnapshot after,
        FactoryBuildingTopologyApplyResult result,
        out string error)
    {
        var createdBuildingCount = result?.CreatedBuildingGuids.Count ?? 0;
        var createdFloorCount = result?.CreatedFloorGuids.Count ?? 0;
        var createdEntityCount = result?.CreatedEntityGuids.Count ?? 0;
        var createdMappingCount = createdBuildingCount + createdFloorCount + createdEntityCount;
        error = string.Empty;
        if (before.Buildings.Count + createdBuildingCount != after.Buildings.Count
            || before.Floors.Count + createdFloorCount != after.Floors.Count
            || before.Connections.Count != after.Connections.Count
            || before.Routes.Count != after.Routes.Count
            || before.Trucks.Count != after.Trucks.Count
            || before.MigrationMappings.Count + createdMappingCount != after.MigrationMappings.Count)
        {
            error = "Topology application changed unrelated world identities.";
            return false;
        }

        foreach (var building in before.Buildings)
        {
            var matching = after.Buildings.Find(candidate => candidate.Guid == building.Guid);
            if (matching is null
                || matching.LegacyBuildingId != building.LegacyBuildingId
                || matching.DefinitionId != building.DefinitionId
                || matching.InteriorOnly != building.InteriorOnly)
            {
                error = $"Building identity {building.Guid:D} was not preserved.";
                return false;
            }
        }

        var beforeEntities = GetWorldEntities(before);
        var afterEntities = GetWorldEntities(after);
        if (beforeEntities.Count + createdEntityCount != afterEntities.Count)
        {
            error = "Topology application lost or created equipment records unexpectedly.";
            return false;
        }

        foreach (var pair in beforeEntities)
        {
            if (!afterEntities.TryGetValue(pair.Key, out var current)
                || !SameEntityState(pair.Value, current))
            {
                error = $"Equipment identity {pair.Key:D} or gameplay state was not preserved.";
                return false;
            }
        }

        foreach (var connection in before.Connections)
        {
            var matching = after.Connections.Find(candidate => candidate.Guid == connection.Guid);
            if (matching is null)
            {
                error = $"Connection identity {connection.Guid:D} was not preserved.";
                return false;
            }
        }

        foreach (var route in before.Routes)
        {
            var matching = after.Routes.Find(candidate => candidate.Guid == route.Guid);
            if (matching is null
                || matching.TruckGuid != route.TruckGuid
                || matching.ItemId != route.ItemId
                || matching.CargoCapacity != route.CargoCapacity)
            {
                error = $"Route identity {route.Guid:D} or configuration was not preserved.";
                return false;
            }
        }

        foreach (var truck in before.Trucks)
        {
            var matching = after.Trucks.Find(candidate => candidate.Guid == truck.Guid);
            if (matching is null
                || matching.RouteGuid != truck.RouteGuid
                || matching.State != truck.State
                || matching.CargoCount != truck.CargoCount
                || matching.CargoItemId != truck.CargoItemId)
            {
                error = $"Truck identity {truck.Guid:D} or cargo state was not preserved.";
                return false;
            }
        }

        return true;
    }

    private static bool TryVerifyRemainingState(
        FactoryWorldSnapshot before,
        FactoryWorldSnapshot after,
        FactoryBuildingTopologyApplyResult result,
        out string error)
    {
        error = string.Empty;
        var deletedBuildings = new HashSet<Guid>(result.DeletedBuildingGuids);
        var deletedFloors = new HashSet<Guid>(result.DeletedFloorGuids);
        var deletedEntities = new HashSet<Guid>(result.DeletedEntityGuids);
        var deletedConnections = new HashSet<Guid>(result.DeletedConnectionGuids);
        var deletedRoutes = new HashSet<Guid>(result.DeletedRouteGuids);
        var deletedTrucks = new HashSet<Guid>(result.DeletedTruckGuids);

        foreach (var building in before.Buildings)
        {
            if (deletedBuildings.Contains(building.Guid))
            {
                continue;
            }

            var current = after.Buildings.Find(candidate => candidate.Guid == building.Guid);
            if (current is null
                || current.LegacyBuildingId != building.LegacyBuildingId
                || current.DefinitionId != building.DefinitionId
                || current.AnchorCell != building.AnchorCell
                || current.FootprintSize != building.FootprintSize
                || current.StoryCount != building.StoryCount
                || current.InteriorOnly != building.InteriorOnly)
            {
                error = $"Unrelated building identity {building.Guid:D} changed during deletion.";
                return false;
            }
        }

        foreach (var floor in before.Floors)
        {
            if (deletedFloors.Contains(floor.Guid))
            {
                continue;
            }

            var current = after.Floors.Find(candidate => candidate.Guid == floor.Guid);
            if (current is null || !SameFloorState(floor, current))
            {
                error = $"Unrelated floor identity {floor.Guid:D} changed during deletion.";
                return false;
            }
        }

        VerifyRemainingEntities(before, after, deletedEntities, out error);
        if (!string.IsNullOrEmpty(error))
        {
            return false;
        }

        foreach (var connection in before.Connections)
        {
            if (deletedConnections.Contains(connection.Guid))
            {
                continue;
            }

            var current = after.Connections.Find(candidate => candidate.Guid == connection.Guid);
            if (current is null
                || !current.Source.Equals(connection.Source)
                || !current.Destination.Equals(connection.Destination))
            {
                error = $"Unrelated connection identity {connection.Guid:D} changed during deletion.";
                return false;
            }
        }

        foreach (var route in before.Routes)
        {
            if (deletedRoutes.Contains(route.Guid))
            {
                continue;
            }

            var current = after.Routes.Find(candidate => candidate.Guid == route.Guid);
            if (current is null
                || current.TruckGuid != route.TruckGuid
                || !current.Source.Equals(route.Source)
                || !current.Destination.Equals(route.Destination)
                || current.ItemId != route.ItemId
                || current.CargoCapacity != route.CargoCapacity
                || current.TransferRateItemsPerSecond != route.TransferRateItemsPerSecond
                || current.OutboundTravelSeconds != route.OutboundTravelSeconds
                || current.ReturnTravelSeconds != route.ReturnTravelSeconds
                || current.PartialLoadDepartureWindowSeconds != route.PartialLoadDepartureWindowSeconds)
            {
                error = $"Unrelated route identity {route.Guid:D} changed during deletion.";
                return false;
            }
        }

        foreach (var truck in before.Trucks)
        {
            if (deletedTrucks.Contains(truck.Guid))
            {
                continue;
            }

            var current = after.Trucks.Find(candidate => candidate.Guid == truck.Guid);
            if (current is null
                || current.RouteGuid != truck.RouteGuid
                || current.State != truck.State
                || current.CargoItemId != truck.CargoItemId
                || current.CargoCount != truck.CargoCount
                || current.RemainingTravelSeconds != truck.RemainingTravelSeconds
                || current.LoadingWindowProgress != truck.LoadingWindowProgress
                || current.BlockingReason != truck.BlockingReason)
            {
                error = $"Unrelated truck identity {truck.Guid:D} changed during deletion.";
                return false;
            }
        }

        return true;
    }

    private static void VerifyRemainingEntities(
        FactoryWorldSnapshot before,
        FactoryWorldSnapshot after,
        HashSet<Guid> deletedEntities,
        out string error)
    {
        error = string.Empty;
        var currentEntities = GetWorldEntities(after);
        foreach (var entity in GetWorldEntities(before).Values)
        {
            if (deletedEntities.Contains(entity.Guid))
            {
                continue;
            }

            if (!currentEntities.TryGetValue(entity.Guid, out var current)
                || !SameEntityState(entity, current))
            {
                error = $"Unrelated equipment identity {entity.Guid:D} changed during deletion.";
                return;
            }
        }
    }

    private static bool SameFloorState(
        FactoryWorldFloorRecord left,
        FactoryWorldFloorRecord right)
    {
        if (left.BuildingGuid != right.BuildingGuid
            || left.FloorIndex != right.FloorIndex
            || left.Label != right.Label
            || left.ProductionRate != right.ProductionRate
            || left.AccumulatedProduction != right.AccumulatedProduction
            || left.MarkerPosition != right.MarkerPosition
            || left.Entities.Count != right.Entities.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Entities.Count; index++)
        {
            if (!SameEntityState(left.Entities[index], right.Entities[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAppliedTopologies(
        FactoryWorldSnapshot persisted,
        FactoryWorldSnapshot expected,
        IReadOnlyList<uint> buildingIds,
        out string error)
    {
        error = string.Empty;
        foreach (var buildingId in buildingIds)
        {
            var persistedBuilding = persisted.Buildings.Find(
                building => building.LegacyBuildingId == buildingId);
            var expectedBuilding = expected.Buildings.Find(
                building => building.LegacyBuildingId == buildingId);
            if (persistedBuilding is null
                || expectedBuilding is null
                || persistedBuilding.Guid != expectedBuilding.Guid
                || persistedBuilding.AnchorCell != expectedBuilding.AnchorCell
                || persistedBuilding.FootprintSize != expectedBuilding.FootprintSize
                || persistedBuilding.StoryCount != expectedBuilding.StoryCount)
            {
                error = $"Applied topology for building {buildingId} did not survive reload.";
                return false;
            }
        }

        return true;
    }

    private static bool HasAppliedDeletions(
        FactoryWorldSnapshot persisted,
        FactoryBuildingTopologyApplyResult result,
        out string error)
    {
        error = string.Empty;
        foreach (var guid in result.DeletedBuildingGuids)
        {
            if (persisted.Buildings.Exists(building => building.Guid == guid))
            {
                error = $"Deleted building {guid:D} survived reload.";
                return false;
            }
        }

        foreach (var guid in result.DeletedFloorGuids)
        {
            if (persisted.Floors.Exists(floor => floor.Guid == guid))
            {
                error = $"Deleted floor {guid:D} survived reload.";
                return false;
            }
        }

        if (result.DeletedEntityGuids.Exists(guid => GetWorldEntities(persisted).ContainsKey(guid))
            || result.DeletedConnectionGuids.Exists(guid => persisted.Connections.Exists(item => item.Guid == guid))
            || result.DeletedRouteGuids.Exists(guid => persisted.Routes.Exists(item => item.Guid == guid))
            || result.DeletedTruckGuids.Exists(guid => persisted.Trucks.Exists(item => item.Guid == guid)))
        {
            error = "One or more deleted building dependencies survived reload.";
            return false;
        }

        return true;
    }

    public static string GetRecordsFingerprint(IEnumerable<BuildingRecord> records)
    {
        var ordered = new List<BuildingRecord>();
        if (records is not null)
        {
            foreach (var record in records)
            {
                if (record is not null)
                {
                    ordered.Add(record.Clone());
                }
            }
        }

        ordered.Sort((left, right) => left.BuildingInstanceId.CompareTo(right.BuildingInstanceId));
        var builder = new StringBuilder();
        foreach (var record in ordered)
        {
            builder.Append(record.BuildingInstanceId).Append('|')
                .Append(record.AnchorCell.x).Append(',').Append(record.AnchorCell.y).Append(',').Append(record.AnchorCell.z).Append('|')
                .Append(record.FootprintSize.x).Append(',').Append(record.FootprintSize.y).Append('|')
                .Append(record.StoryCount).Append('|');
            foreach (var door in record.Doors)
            {
                builder.Append(door?.WallId).Append('@').Append(door?.NormalizedOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            }

            builder.Append('\n');
        }

        return HashFingerprint(builder.ToString());
    }

    public static string GetSnapshotFingerprint(FactoryWorldSnapshot snapshot)
    {
        if (snapshot is null)
        {
            return HashFingerprint("<null>");
        }

        snapshot = snapshot.Clone();
        snapshot.Buildings.Sort((left, right) => left.Guid.CompareTo(right.Guid));
        snapshot.Floors.Sort((left, right) => left.Guid.CompareTo(right.Guid));
        snapshot.Connections.Sort((left, right) => left.Guid.CompareTo(right.Guid));
        snapshot.Routes.Sort((left, right) => left.Guid.CompareTo(right.Guid));
        snapshot.Trucks.Sort((left, right) => left.Guid.CompareTo(right.Guid));
        snapshot.MigrationMappings.Sort((left, right) => left.Guid.CompareTo(right.Guid));
        foreach (var floor in snapshot.Floors)
        {
            var entities = new List<FactoryWorldEntityRecord>(floor.Entities);
            entities.Sort((left, right) => left.Guid.CompareTo(right.Guid));
            floor.SetEntities(entities);
        }

        var builder = new StringBuilder();
        builder.Append(snapshot.SchemaVersion).Append('|');
        foreach (var building in snapshot.Buildings.FindAll(item => item is not null))
        {
            builder.Append("B|").Append(building.Guid.ToString("D")).Append('|')
                .Append(building.LegacyBuildingId).Append('|').Append(building.DefinitionId).Append('|')
                .Append(building.AnchorCell.x).Append(',').Append(building.AnchorCell.y).Append(',').Append(building.AnchorCell.z).Append('|')
                .Append(building.FootprintSize.x).Append(',').Append(building.FootprintSize.y).Append('|')
                .Append(building.StoryCount).Append('|').Append(building.InteriorOnly).Append('|');
            foreach (var door in building.Doors)
            {
                builder.Append(door?.WallId).Append('@')
                    .Append(door?.NormalizedOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            }

            builder.Append('\n');
        }

        foreach (var floor in snapshot.Floors.FindAll(item => item is not null))
        {
            builder.Append("F|").Append(floor.Guid.ToString("D")).Append('|').Append(floor.BuildingGuid.ToString("D"))
                .Append('|').Append(floor.FloorIndex).Append('|').Append(floor.Label).Append('|')
                .Append(floor.ProductionRate.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(floor.AccumulatedProduction.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(floor.MarkerPosition.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(floor.MarkerPosition.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            foreach (var entity in floor.Entities)
            {
                builder.Append("E|").Append(entity.Guid.ToString("D")).Append('|').Append(entity.FloorGuid.ToString("D"))
                    .Append('|').Append(entity.LegacyEntityId).Append('|').Append(entity.DefinitionId).Append('|')
                    .Append(entity.LocalPosition.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(entity.LocalPosition.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                    .Append(entity.RotationZ.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                    .Append(entity.Footprint.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(entity.Footprint.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                    .Append(entity.CycleRate.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                    .Append(entity.CycleProgress.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                    .Append(entity.ProducedCount).Append('|').Append(entity.OutputCount).Append('|')
                    .Append(entity.InputCount).Append('|').Append(entity.IsNaiEntity).Append('|')
                    .Append(Convert.ToBase64String(entity.State)).Append('\n');
            }
        }

        foreach (var connection in snapshot.Connections)
        {
            builder.Append("C|").Append(connection.Guid.ToString("D")).Append('|')
                .Append(connection.Source.BuildingGuid.ToString("D")).Append('|')
                .Append(connection.Source.FloorGuid.ToString("D")).Append('|')
                .Append(connection.Source.EntityGuid.ToString("D")).Append('|')
                .Append(connection.Destination.BuildingGuid.ToString("D")).Append('|')
                .Append(connection.Destination.FloorGuid.ToString("D")).Append('|')
                .Append(connection.Destination.EntityGuid.ToString("D")).Append('\n');
        }

        foreach (var route in snapshot.Routes)
        {
            builder.Append("R|").Append(route.Guid.ToString("D")).Append('|').Append(route.TruckGuid.ToString("D"))
                .Append('|').Append(route.Source.BuildingGuid.ToString("D")).Append('|')
                .Append(route.Source.FloorGuid.ToString("D")).Append('|').Append(route.Source.EntityGuid.ToString("D"))
                .Append('|').Append(route.Destination.BuildingGuid.ToString("D")).Append('|')
                .Append(route.Destination.FloorGuid.ToString("D")).Append('|').Append(route.Destination.EntityGuid.ToString("D"))
                .Append('|').Append(route.ItemId).Append('|').Append(route.CargoCapacity).Append('|')
                .Append(route.TransferRateItemsPerSecond.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(route.OutboundTravelSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(route.ReturnTravelSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(route.PartialLoadDepartureWindowSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }

        foreach (var truck in snapshot.Trucks)
        {
            builder.Append("T|").Append(truck.Guid.ToString("D")).Append('|').Append(truck.RouteGuid.ToString("D"))
                .Append('|').Append(truck.State).Append('|').Append(truck.CargoItemId).Append('|').Append(truck.CargoCount).Append('|')
                .Append(truck.RemainingTravelSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(truck.LoadingWindowProgress.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(truck.BlockingReason).Append('\n');
        }

        foreach (var mapping in snapshot.MigrationMappings)
        {
            builder.Append("M|").Append(mapping.Kind).Append('|').Append(mapping.LegacyKey).Append('|')
                .Append(mapping.Guid.ToString("D")).Append('\n');
        }

        return HashFingerprint(builder.ToString());
    }

    private static string HashFingerprint(string value)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var byteValue in bytes)
        {
            builder.Append(byteValue.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static Dictionary<Guid, FactoryWorldEntityRecord> GetWorldEntities(
        FactoryWorldSnapshot snapshot)
    {
        var result = new Dictionary<Guid, FactoryWorldEntityRecord>();
        foreach (var floor in snapshot.Floors)
        {
            foreach (var entity in floor.Entities)
            {
                if (entity is not null)
                {
                    result.Add(entity.Guid, entity);
                }
            }
        }

        return result;
    }

    private static bool SameEntityState(
        FactoryWorldEntityRecord left,
        FactoryWorldEntityRecord right)
    {
        var leftState = left.State;
        var rightState = right.State;
        if (left.Guid != right.Guid
            || left.DefinitionId != right.DefinitionId
            || left.RotationZ != right.RotationZ
            || left.Footprint != right.Footprint
            || left.CycleRate != right.CycleRate
            || left.CycleProgress != right.CycleProgress
            || left.ProducedCount != right.ProducedCount
            || left.OutputCount != right.OutputCount
            || left.InputCount != right.InputCount
            || left.IsNaiEntity != right.IsNaiEntity
            || leftState.Length != rightState.Length)
        {
            return false;
        }

        for (var index = 0; index < leftState.Length; index++)
        {
            if (leftState[index] != rightState[index])
            {
                return false;
            }
        }

        return true;
    }

    private static void PopulatePreservationCounts(
        FactoryWorldSnapshot before,
        FactoryWorldSnapshot after,
        FactoryBuildingTopologyApplyResult result)
    {
        result.PreservedBuildingCount = CountShared(
            before.Buildings,
            after.Buildings,
            building => building.Guid);
        result.PreservedFloorCount = CountShared(
            before.Floors,
            after.Floors,
            floor => floor.Guid);
        result.PreservedEntityCount = CountShared(
            GetWorldEntities(before).Values,
            GetWorldEntities(after).Values,
            entity => entity.Guid);
        result.PreservedConnectionCount = CountShared(
            before.Connections,
            after.Connections,
            connection => connection.Guid);
        result.PreservedRouteCount = CountShared(
            before.Routes,
            after.Routes,
            route => route.Guid);
        result.PreservedTruckCount = CountShared(
            before.Trucks,
            after.Trucks,
            truck => truck.Guid);
    }

    private static int CountShared<T>(
        IEnumerable<T> before,
        IEnumerable<T> after,
        Func<T, Guid> getGuid)
    {
        var afterGuids = new HashSet<Guid>();
        foreach (var item in after)
        {
            afterGuids.Add(getGuid(item));
        }

        var count = 0;
        foreach (var item in before)
        {
            if (afterGuids.Contains(getGuid(item)))
            {
                count++;
            }
        }

        return count;
    }

    private static FactoryWorldBuildingRecord FindWorldBuilding(
        FactoryWorldSnapshot snapshot,
        uint buildingId)
    {
        return snapshot.Buildings.Find(
            building => building.LegacyBuildingId == buildingId)!;
    }

    private static BuildingRecord ToBuildingRecord(
        FactoryWorldBuildingRecord building)
    {
        return new BuildingRecord(
            building.LegacyBuildingId,
            building.AnchorCell,
            building.FootprintSize,
            building.StoryCount,
            building.Doors);
    }

    private static Vector2Int GetInteriorSize(
        FactoryWorldBuildingRecord building)
    {
        return building.InteriorOnly
            ? building.FootprintSize
            : BuildingFootprint.GetUsableInteriorSize(building.FootprintSize);
    }

    private static Vector2Int GetInteriorSize(
        BuildingRecord record,
        bool interiorOnly)
    {
        return interiorOnly
            ? record.FootprintSize
            : BuildingFootprint.GetUsableInteriorSize(record.FootprintSize);
    }

    private static FactoryWorldFloorRecord CreateDefaultWorldFloor(
        uint buildingId,
        Guid buildingGuid,
        int floorIndex,
        Vector2Int footprintSize)
    {
        var legacyFloor = OutsideTestFloorRecord.CreateDefault(buildingId, floorIndex);
        var interiorSize = BuildingFootprint.GetUsableInteriorSize(footprintSize);
        var floor = new FactoryWorldFloorRecord(
            FactoryGuidMigration.ForFloor(buildingId, floorIndex),
            buildingGuid,
            floorIndex,
            legacyFloor.Label,
            legacyFloor.ProductionRate,
            legacyFloor.AccumulatedProduction,
            OutsideTestFloorRecord.ClampMarkerPosition(
                legacyFloor.MarkerPosition,
                interiorSize));
        var entities = new List<FactoryWorldEntityRecord>();
        foreach (var entity in legacyFloor.Entities)
        {
            entities.Add(ToWorldEntity(
                entity,
                floor.Guid,
                buildingId,
                floorIndex));
        }

        floor.SetEntities(entities);
        ReconcileNewFloorEntities(floor, interiorSize);
        return floor;
    }

    private static void ReconcileNewFloorEntities(
        FactoryWorldFloorRecord floor,
        Vector2Int interiorSize)
    {
        var occupiedCells = new HashSet<Vector2Int>();
        foreach (var entity in floor.Entities)
        {
            if (entity is null)
            {
                continue;
            }

            var cell = Vector2Int.FloorToInt(entity.LocalPosition);
            if (BuildingFootprint.IsUsableInteriorPosition(entity.LocalPosition, interiorSize)
                && occupiedCells.Add(cell))
            {
                continue;
            }

            var placed = false;
            for (var y = 0; y < interiorSize.y && !placed; y++)
            {
                for (var x = 0; x < interiorSize.x; x++)
                {
                    var candidate = new Vector2Int(x, y);
                    if (occupiedCells.Add(candidate))
                    {
                        entity.SetLocalPosition((Vector2)candidate + Vector2.one * 0.5f);
                        placed = true;
                        break;
                    }
                }
            }
        }
    }

    private static FactoryWorldEntityRecord ToWorldEntity(
        FactoryEntityRecord entity,
        Guid floorGuid,
        uint buildingId,
        int floorIndex)
    {
        return new FactoryWorldEntityRecord(
            FactoryGuidMigration.ForEntity(buildingId, floorIndex, entity.EntityId),
            floorGuid,
            entity.DefinitionId,
            entity.LogicalPosition,
            entity.IsDock ? FactoryDock.DirectionToRotation(entity.DockDirection) : 0f,
            Vector2.one,
            entity.GetConveyorState(),
            entity.EntityId,
            false,
            entity.CycleRate,
            entity.CycleProgress,
            entity.ProducedCount,
            entity.OutputCount,
            entity.InputCount);
    }

    private static FactoryWorldEntityRecord CloneWorldEntity(
        FactoryWorldEntityRecord entity,
        Guid floorGuid,
        Vector2 position)
    {
        return new FactoryWorldEntityRecord(
            entity.Guid,
            floorGuid,
            entity.DefinitionId,
            position,
            entity.RotationZ,
            entity.Footprint,
            entity.State,
            entity.LegacyEntityId,
            entity.IsNaiEntity,
            entity.CycleRate,
            entity.CycleProgress,
            entity.ProducedCount,
            entity.OutputCount,
            entity.InputCount);
    }

    private static void RemoveWorldEntity(
        FactoryWorldFloorRecord floor,
        Guid entityGuid)
    {
        var retained = new List<FactoryWorldEntityRecord>();
        foreach (var entity in floor.Entities)
        {
            if (entity is not null && entity.Guid != entityGuid)
            {
                retained.Add(entity);
            }
        }

        floor.SetEntities(retained);
    }

    private static int CompareWorldEntities(
        FactoryWorldEntityRecord left,
        FactoryWorldEntityRecord right)
    {
        var legacyIdComparison = left.LegacyEntityId.CompareTo(right.LegacyEntityId);
        return legacyIdComparison != 0
            ? legacyIdComparison
            : left.Guid.CompareTo(right.Guid);
    }

    private static int ComparePlacementRequests(
        EntityPlacementRequest left,
        EntityPlacementRequest right)
    {
        var floorComparison = left.SourceFloorIndex.CompareTo(right.SourceFloorIndex);
        if (floorComparison != 0)
        {
            return floorComparison;
        }

        return CompareWorldEntities(left.Entity, right.Entity);
    }

    private static bool AddPlanError(
        FactoryBuildingTopologyPlan plan,
        string message,
        out string error)
    {
        error = message ?? "Topology plan validation failed.";
        plan.ValidationErrorsInternal.Add(error);
        return false;
    }

    private static void AddChangeError(
        FactoryBuildingTopologyPlan plan,
        FactoryBuildingTopologyChange change,
        string message)
    {
        var error = string.IsNullOrWhiteSpace(message)
            ? $"Building {change.BuildingInstanceId} topology could not be applied."
            : message;
        change.ValidationErrors.Add(error);
        plan.ValidationErrorsInternal.Add(error);
    }

    private static string NormalizeDatabasePath(string databasePath)
    {
        return string.IsNullOrWhiteSpace(databasePath)
            ? string.Empty
            : Path.GetFullPath(databasePath.Trim());
    }

    private sealed class FloorPlacementState
    {
        public FloorPlacementState(FactoryWorldFloorRecord newFloor)
        {
            Floor = newFloor;
        }

        public FactoryWorldFloorRecord Floor { get; }
        public HashSet<Vector2Int> OccupiedCells { get; } = new();
        public HashSet<uint> EntityIds { get; } = new();
    }

    private sealed class EntityPlacementRequest
    {
        public EntityPlacementRequest(
            FloorPlacementState newSource,
            FactoryWorldEntityRecord newEntity,
            int newSourceFloorIndex,
            FactoryWorldFloorRecord newRemovedFloor = null)
        {
            Source = newSource;
            Entity = newEntity;
            SourceFloorIndex = newSourceFloorIndex;
            RemovedFloor = newRemovedFloor!;
        }

        public FloorPlacementState Source { get; }
        public FactoryWorldEntityRecord Entity { get; }
        public int SourceFloorIndex { get; }
        public FactoryWorldFloorRecord RemovedFloor { get; }
    }

    public static bool TryUpdateBuildingRecord(
        FactoryWorldState state,
        BuildingRecord record,
        float doorCornerExclusionDistance,
        out string error,
        out FactoryBuildingEditResult result)
    {
        error = string.Empty;
        result = new FactoryBuildingEditResult();
        if (state is null)
        {
            error = "Factory world state is required.";
            return false;
        }

        if (record is null)
        {
            error = "Building record is required.";
            return false;
        }

        var otherRecords = new List<BuildingRecord>();
        foreach (var existingRecord in state.BuildingRecords)
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

        if (!state.TryGetBuildingRecord(record.BuildingInstanceId, out var previousRecord))
        {
            state.ApplyBuildingRecord(record);
            result.Changed = true;
            return true;
        }

        var retainedFloors = new List<OutsideTestFloorRecord>();
        var affectedFloors = new List<OutsideTestFloorRecord>();
        foreach (var floor in state.FloorStates)
        {
            if (floor.BuildingInstanceId != record.BuildingInstanceId)
            {
                continue;
            }

            if (floor.FloorIndex < record.StoryCount)
            {
                retainedFloors.Add(floor);
            }
            else
            {
                affectedFloors.Add(floor);
            }
        }

        if (affectedFloors.Count == 0)
        {
            state.ApplyBuildingRecord(record);
            result.Changed = true;
            return true;
        }

        retainedFloors.Sort((left, right) => left.FloorIndex.CompareTo(right.FloorIndex));
        affectedFloors.Sort((left, right) => left.FloorIndex.CompareTo(right.FloorIndex));
        var interiorSize = state.GetInteriorSize(record);
        var occupiedCells = new Dictionary<int, HashSet<Vector2Int>>();
        var entityIds = new Dictionary<int, HashSet<uint>>();
        foreach (var floor in retainedFloors)
        {
            occupiedCells[floor.FloorIndex] = new HashSet<Vector2Int>();
            entityIds[floor.FloorIndex] = new HashSet<uint>();
            foreach (var entity in floor.Entities)
            {
                if (entity is null)
                {
                    continue;
                }

                entityIds[floor.FloorIndex].Add(entity.EntityId);
                if (BuildingFootprint.IsUsableInteriorPosition(entity.LogicalPosition, interiorSize))
                {
                    occupiedCells[floor.FloorIndex].Add(Vector2Int.FloorToInt(entity.LogicalPosition));
                }
            }
        }

        var plans = new List<RelocationPlan>();
        foreach (var floor in affectedFloors)
        {
            var entities = new List<FactoryEntityRecord>(floor.Entities);
            entities.Sort((left, right) => left.EntityId.CompareTo(right.EntityId));
            foreach (var entity in entities)
            {
                if (entity is null)
                {
                    continue;
                }

                if (!TryFindTarget(
                        entity,
                        floor.FloorIndex,
                        retainedFloors,
                        interiorSize,
                        occupiedCells,
                        entityIds,
                        out var targetFloor,
                        out var targetPosition))
                {
                    result.RecoveryEntityIdsInternal.Add(entity.EntityId);
                    error = $"Building {record.BuildingInstanceId} cannot retain entity {entity.EntityId} while reducing its story count.";
                    return false;
                }

                occupiedCells[targetFloor.FloorIndex].Add(Vector2Int.FloorToInt(targetPosition));
                entityIds[targetFloor.FloorIndex].Add(entity.EntityId);
                plans.Add(new RelocationPlan(floor, targetFloor, entity, targetPosition));
            }
        }

        var applied = new List<RelocationPlan>();
        try
        {
            foreach (var plan in plans)
            {
                if (!plan.Source.RemoveEntity(plan.Entity.EntityId))
                {
                    throw new InvalidOperationException($"Entity {plan.Entity.EntityId} disappeared during building edit planning.");
                }

                plan.Entity.SetLogicalPosition(plan.TargetPosition);
                plan.Target.AddEntity(plan.Entity);
                state.RebindEntityEndpoint(
                    new FactoryEntityEndpoint(
                        record.BuildingInstanceId,
                        plan.Source.FloorIndex,
                        plan.Entity.EntityId),
                    new FactoryEntityEndpoint(
                        record.BuildingInstanceId,
                        plan.Target.FloorIndex,
                        plan.Entity.EntityId));
                result.RelocationsInternal.Add(new FactoryBuildingEntityRelocation
                {
                    EntityId = plan.Entity.EntityId,
                    FromFloorIndex = plan.Source.FloorIndex,
                    ToFloorIndex = plan.Target.FloorIndex,
                    FromPosition = plan.OriginalPosition,
                    ToPosition = plan.TargetPosition
                });
                applied.Add(plan);
            }

            state.ApplyBuildingRecord(record);
            state.ResumeRecoveryRoutes();
            result.Changed = true;
            return true;
        }
        catch (Exception exception)
        {
            for (var index = applied.Count - 1; index >= 0; index--)
            {
                var plan = applied[index];
                plan.Target.RemoveEntity(plan.Entity.EntityId);
                plan.Entity.SetLogicalPosition(plan.OriginalPosition);
                plan.Source.AddEntity(plan.Entity);
                state.RebindEntityEndpoint(
                    new FactoryEntityEndpoint(
                        record.BuildingInstanceId,
                        plan.Target.FloorIndex,
                        plan.Entity.EntityId),
                    new FactoryEntityEndpoint(
                        record.BuildingInstanceId,
                        plan.Source.FloorIndex,
                        plan.Entity.EntityId));
            }

            result.RelocationsInternal.Clear();
            state.ApplyBuildingRecord(previousRecord);
            error = exception.Message;
            return false;
        }
    }

    private static bool TryFindTarget(
        FactoryEntityRecord entity,
        int sourceFloorIndex,
        IReadOnlyList<OutsideTestFloorRecord> retainedFloors,
        Vector2Int interiorSize,
        IReadOnlyDictionary<int, HashSet<Vector2Int>> occupiedCells,
        IReadOnlyDictionary<int, HashSet<uint>> entityIds,
        out OutsideTestFloorRecord targetFloor,
        out Vector2 targetPosition)
    {
        targetFloor = null!;
        targetPosition = default;
        var found = false;
        var bestFloorDistance = int.MaxValue;
        var bestDistance = float.PositiveInfinity;
        var bestCell = Vector2Int.zero;
        foreach (var floor in retainedFloors)
        {
            if (entityIds[floor.FloorIndex].Contains(entity.EntityId))
            {
                continue;
            }

            for (var y = 0; y < interiorSize.y; y++)
            {
                for (var x = 0; x < interiorSize.x; x++)
                {
                    var candidate = new Vector2Int(x, y);
                    if (occupiedCells[floor.FloorIndex].Contains(candidate))
                    {
                        continue;
                    }

                    var floorDistance = Mathf.Abs(floor.FloorIndex - sourceFloorIndex);
                    var center = (Vector2)candidate + Vector2.one * 0.5f;
                    var distance = (center - entity.LogicalPosition).sqrMagnitude;
                    if (found
                        && !IsBetterTarget(
                            floorDistance,
                            distance,
                            floor.FloorIndex,
                            candidate,
                            bestFloorDistance,
                            bestDistance,
                            targetFloor.FloorIndex,
                            bestCell))
                    {
                        continue;
                    }

                    found = true;
                    bestFloorDistance = floorDistance;
                    bestDistance = distance;
                    bestCell = candidate;
                    targetFloor = floor;
                }
            }
        }

        if (found)
        {
            targetPosition = (Vector2)bestCell + Vector2.one * 0.5f;
        }

        return found;
    }

    private static bool IsBetterTarget(
        int floorDistance,
        float distance,
        int floorIndex,
        Vector2Int cell,
        int bestFloorDistance,
        float bestDistance,
        int bestFloorIndex,
        Vector2Int bestCell)
    {
        if (floorDistance != bestFloorDistance)
        {
            return floorDistance < bestFloorDistance;
        }

        if (!Mathf.Approximately(distance, bestDistance))
        {
            return distance < bestDistance;
        }

        if (floorIndex != bestFloorIndex)
        {
            return floorIndex < bestFloorIndex;
        }

        return cell.y < bestCell.y
            || (cell.y == bestCell.y && cell.x < bestCell.x);
    }

    private sealed class RelocationPlan
    {
        public RelocationPlan(
            OutsideTestFloorRecord newSource,
            OutsideTestFloorRecord newTarget,
            FactoryEntityRecord newEntity,
            Vector2 newTargetPosition)
        {
            Source = newSource;
            Target = newTarget;
            Entity = newEntity;
            TargetPosition = newTargetPosition;
            OriginalPosition = newEntity.LogicalPosition;
        }

        public OutsideTestFloorRecord Source { get; }
        public OutsideTestFloorRecord Target { get; }
        public FactoryEntityRecord Entity { get; }
        public Vector2 OriginalPosition { get; }
        public Vector2 TargetPosition { get; }
    }
}
