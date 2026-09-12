// Exposes deterministic factory authoring and persistence workflows through Unity Pipeline commands.
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
public sealed class FactoryPipelineIssue
{
    public string code = string.Empty;
    public uint buildingId;
    public int floorIndex = -1;
    public float[] expected = null!;
    public float[] actual = null!;
}

[Serializable]
public sealed class FactoryRebuildOutsideShellsResult
{
    public bool ok;
    public string scene = string.Empty;
    public int layoutCount;
    public List<uint> rebuiltBuildingIds = new();
    public List<uint> unchangedBuildingIds = new();
    public bool saved;
    public List<FactoryPipelineIssue> issues = new();
}

[Serializable]
public sealed class FactoryAuthoringValidationResult
{
    public bool ok;
    public string scene = string.Empty;
    public int buildingsChecked;
    public int floorScenesChecked;
    public List<FactoryPipelineIssue> issues = new();
}

[Serializable]
public sealed class FactoryRebuildAndValidateResult
{
    public bool ok;
    public bool changed;
    public int rebuilt;
    public int validated;
    public bool saved;
    public List<FactoryPipelineIssue> issues = new();
}

[Serializable]
public sealed class FactoryReconcileSaveResult
{
    public bool ok;
    public bool changed;
    public int buildings;
    public List<FactoryPipelineRelocation> relocated = new();
    public List<uint> recoveryEntityIds = new();
    public int identityChanges;
    public int dataLoss;
    public bool saved;
}

[Serializable]
public sealed class FactoryInspectSaveResult
{
    public bool ok;
    public int schemaVersion;
    public int buildingCount;
    public int floorCount;
    public int entityCount;
    public int connectionCount;
    public int routeCount;
    public int truckCount;
    public int recoveryCount;
    public List<FactoryBuildingInspection> buildings = new();
    public List<FactoryFloorInspection> floors = new();
    public List<FactoryConnectionInspection> connections = new();
    public List<FactoryRouteInspection> routes = new();
    public List<FactoryTruckInspection> trucks = new();
}

[Serializable]
public sealed class FactoryBuildingInspection
{
    public string guid = string.Empty;
    public uint legacyBuildingId;
    public int[] anchor = null!;
    public int[] footprint = null!;
    public int[] usableInterior = null!;
    public int storyCount;
    public bool interiorOnly;
}

[Serializable]
public sealed class FactoryFloorInspection
{
    public string guid = string.Empty;
    public string buildingGuid = string.Empty;
    public int floorIndex;
    public int entityCount;
    public int recoveryCount;
}

[Serializable]
public sealed class FactoryConnectionInspection
{
    public string guid = string.Empty;
    public string sourceEntityGuid = string.Empty;
    public string destinationEntityGuid = string.Empty;
}

[Serializable]
public sealed class FactoryRouteInspection
{
    public string guid = string.Empty;
    public string truckGuid = string.Empty;
    public string sourceEntityGuid = string.Empty;
    public string destinationEntityGuid = string.Empty;
}

[Serializable]
public sealed class FactoryTruckInspection
{
    public string guid = string.Empty;
    public string routeGuid = string.Empty;
    public string state = string.Empty;
    public int cargoCount;
}

public static class FactoryPipelineCommands
{
    public const string OutsideTestScenePath = "Assets/Scenes/OutsideTest.unity";
    public const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
    public const string InsideFactoryTemplatePath = "Assets/Scenes/insidefactory0.unity";

    [CliCommand(
        "factory_rebuild_outside_shells",
        "Rebuild stale OutsideTest building shells and save only when changed.",
        MainThreadRequired = true)]
    public static FactoryRebuildOutsideShellsResult RebuildOutsideShells(
        [CliArg("dry_run", "Preview stale shells without changing the scene.")] bool dryRun = false,
        [CliArg("confirm", "Apply stale shell rebuilds and save the scene.")] bool confirm = false)
    {
        return FactoryAuthoringPipelineService.RebuildOutsideShells(dryRun, confirm);
    }

    [CliCommand(
        "factory_validate_authoring",
        "Validate deterministic OutsideTest authoring and floor-scene conformance.",
        MainThreadRequired = true)]
    public static FactoryAuthoringValidationResult ValidateAuthoring()
    {
        return FactoryAuthoringPipelineService.ValidateAuthoring();
    }

    [CliCommand(
        "factory_reconcile_save",
        "Reconcile persisted factory entity positions without changing identity or state.",
        MainThreadRequired = true)]
    public static FactoryReconcileSaveResult ReconcileSave(
        [CliArg("path", "Factory database path; defaults to the application database.")] string path = "",
        [CliArg("dry_run", "Preview reconciliation without saving.")] bool dryRun = true,
        [CliArg("confirm", "Save the reconciled database.")] bool confirm = false)
    {
        var databasePath = FactoryPipelinePersistenceService.ResolveDatabasePath(path);
        var snapshot = new FactoryWorldSqliteStore(databasePath).Load();
        var reconciliation = FactoryWorldReconciliationService.Reconcile(snapshot);
        var result = new FactoryReconcileSaveResult
        {
            ok = reconciliation.identityChanges == 0
                && reconciliation.dataLoss == 0,
            changed = reconciliation.changed,
            buildings = snapshot.Buildings.Count,
            relocated = reconciliation.relocated,
            recoveryEntityIds = reconciliation.recoveryEntityIds,
            identityChanges = reconciliation.identityChanges,
            dataLoss = reconciliation.dataLoss
        };

        if (result.ok && reconciliation.changed && confirm && !dryRun)
        {
            new FactoryWorldSqliteStore(databasePath).Save(snapshot);
            result.saved = true;
        }

        return result;
    }

    [CliCommand(
        "factory_inspect_save",
        "Read-only summary of a persisted schema-9 factory database.",
        MainThreadRequired = true)]
    public static FactoryInspectSaveResult InspectSave(
        [CliArg("path", "Factory database path; defaults to the application database.")] string path = "")
    {
        var databasePath = FactoryPipelinePersistenceService.ResolveDatabasePath(path);
        var snapshot = new FactoryWorldSqliteStore(databasePath).Load();
        return FactoryPipelinePersistenceService.Inspect(snapshot);
    }

    [CliCommand(
        "factory_rebuild_and_validate",
        "Rebuild stale OutsideTest shells once, save once, and validate the persisted scene.",
        MainThreadRequired = true)]
    public static FactoryRebuildAndValidateResult RebuildAndValidate(
        [CliArg("dry_run", "Preview stale shell rebuilds without changing the scene.")] bool dryRun = false,
        [CliArg("confirm", "Apply stale shell rebuilds and save the scene.")] bool confirm = false)
    {
        return FactoryAuthoringPipelineService.RebuildAndValidate(dryRun, confirm);
    }
}

public static class FactoryPipelinePersistenceService
{
    public static string ResolveDatabasePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return FactoryWorldPaths.GetDefaultDatabasePath();
        }

        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(FactoryWorldPaths.GetProjectRoot(), path);
    }

    public static FactoryInspectSaveResult Inspect(FactoryWorldSnapshot snapshot)
    {
        var result = new FactoryInspectSaveResult
        {
            ok = true,
            schemaVersion = snapshot.SchemaVersion,
            buildingCount = snapshot.Buildings.Count,
            floorCount = snapshot.Floors.Count,
            entityCount = snapshot.Floors.Sum(floor => floor.Entities.Count),
            connectionCount = snapshot.Connections.Count,
            routeCount = snapshot.Routes.Count,
            truckCount = snapshot.Trucks.Count
        };
        var buildings = new List<FactoryWorldBuildingRecord>(snapshot.Buildings);
        buildings.Sort((left, right) =>
        {
            var legacyIdComparison = left.LegacyBuildingId.CompareTo(right.LegacyBuildingId);
            return legacyIdComparison != 0
                ? legacyIdComparison
                : left.Guid.CompareTo(right.Guid);
        });
        foreach (var building in buildings)
        {
            var usableInterior = building.InteriorOnly
                ? building.FootprintSize
                : BuildingFootprint.GetUsableInteriorSize(building.FootprintSize);
            result.buildings.Add(new FactoryBuildingInspection
            {
                guid = building.Guid.ToString("D"),
                legacyBuildingId = building.LegacyBuildingId,
                anchor = new[]
                {
                    building.AnchorCell.x,
                    building.AnchorCell.y,
                    building.AnchorCell.z
                },
                footprint = new[]
                {
                    building.FootprintSize.x,
                    building.FootprintSize.y
                },
                usableInterior = new[] { usableInterior.x, usableInterior.y },
                storyCount = building.StoryCount,
                interiorOnly = building.InteriorOnly
            });
        }

        var floors = new List<FactoryWorldFloorRecord>(snapshot.Floors);
        floors.Sort((left, right) =>
        {
            var buildingComparison = left.BuildingGuid.CompareTo(right.BuildingGuid);
            return buildingComparison != 0
                ? buildingComparison
                : left.FloorIndex.CompareTo(right.FloorIndex);
        });
        foreach (var floor in floors)
        {
            var building = snapshot.Buildings.Find(
                candidate => candidate.Guid == floor.BuildingGuid);
            var recoveryCount = building is null
                ? floor.Entities.Count
                : FactoryWorldReconciliationService.CountRecoveryEntities(
                    building,
                    floor);
            result.recoveryCount += recoveryCount;
            result.floors.Add(new FactoryFloorInspection
            {
                guid = floor.Guid.ToString("D"),
                buildingGuid = floor.BuildingGuid.ToString("D"),
                floorIndex = floor.FloorIndex,
                entityCount = floor.Entities.Count,
                recoveryCount = recoveryCount
            });
        }

        foreach (var connection in snapshot.Connections)
        {
            result.connections.Add(new FactoryConnectionInspection
            {
                guid = connection.Guid.ToString("D"),
                sourceEntityGuid = connection.Source.EntityGuid.ToString("D"),
                destinationEntityGuid = connection.Destination.EntityGuid.ToString("D")
            });
        }

        foreach (var route in snapshot.Routes)
        {
            result.routes.Add(new FactoryRouteInspection
            {
                guid = route.Guid.ToString("D"),
                truckGuid = route.TruckGuid.ToString("D"),
                sourceEntityGuid = route.Source.EntityGuid.ToString("D"),
                destinationEntityGuid = route.Destination.EntityGuid.ToString("D")
            });
        }

        foreach (var truck in snapshot.Trucks)
        {
            result.trucks.Add(new FactoryTruckInspection
            {
                guid = truck.Guid.ToString("D"),
                routeGuid = truck.RouteGuid.ToString("D"),
                state = truck.State.ToString(),
                cargoCount = truck.CargoCount
            });
        }

        return result;
    }
}

public static class FactoryAuthoringPipelineService
{
    public static FactoryRebuildOutsideShellsResult RebuildOutsideShells(
        bool dryRun,
        bool confirm)
    {
        return WithOutsideTestScene(
            scene => RebuildOutsideShellsInScene(scene, dryRun, confirm));
    }

    public static FactoryAuthoringValidationResult ValidateAuthoring()
    {
        return WithOutsideTestScene(ValidateAuthoringInScenes);
    }

    public static FactoryRebuildAndValidateResult RebuildAndValidate(
        bool dryRun,
        bool confirm)
    {
        return WithOutsideTestScene(
            scene =>
            {
                var rebuild = RebuildOutsideShellsInScene(scene, dryRun, confirm);
                var validation = ValidateAuthoringInScenes(scene);
                var result = new FactoryRebuildAndValidateResult
                {
                    ok = rebuild.ok && validation.ok,
                    changed = rebuild.rebuiltBuildingIds.Count > 0,
                    rebuilt = rebuild.rebuiltBuildingIds.Count,
                    validated = validation.buildingsChecked,
                    saved = rebuild.saved
                };
                result.issues.AddRange(rebuild.issues);
                result.issues.AddRange(validation.issues);
                return result;
            });
    }

    private static FactoryRebuildOutsideShellsResult RebuildOutsideShellsInScene(
        Scene scene,
        bool dryRun,
        bool confirm)
    {
        var context = ReadOutsideTestContext(scene);
        var result = new FactoryRebuildOutsideShellsResult
        {
            ok = true,
            scene = FactoryPipelineCommands.OutsideTestScenePath,
            layoutCount = context.layouts.Count
        };
        AddContextIssues(context, result.issues);
        if (result.issues.Count > 0)
        {
            result.ok = false;
            return result;
        }

        var records = context.layouts
            .Select(layout => layout.ExportBuildingRecord())
            .ToList();
        if (!BuildingShellValidation.TryValidateRecords(
                records,
                context.creator.DoorCornerExclusionDistance,
                out _))
        {
            result.issues.Add(new FactoryPipelineIssue
            {
                code = "INVALID_BUILDING_RECORD"
            });
            result.ok = false;
            return result;
        }

        var shouldApply = confirm && !dryRun;
        for (var index = 0; index < context.layouts.Count; index++)
        {
            var layout = context.layouts[index];
            var record = records[index];
            if (!BuildingShellAssembler.NeedsRebuild(
                    record,
                    context.creator,
                    layout.transform))
            {
                result.unchangedBuildingIds.Add(record.BuildingInstanceId);
                continue;
            }

            result.rebuiltBuildingIds.Add(record.BuildingInstanceId);
            if (!shouldApply
                || new BuildingShellAssembler().RebuildShell(
                    record,
                    context.creator,
                    layout.transform))
            {
                continue;
            }

            result.issues.Add(new FactoryPipelineIssue
            {
                code = "SHELL_REBUILD_FAILED",
                buildingId = record.BuildingInstanceId
            });
        }

        if (result.issues.Count > 0)
        {
            result.ok = false;
            return result;
        }

        if (shouldApply && result.rebuiltBuildingIds.Count > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                result.issues.Add(new FactoryPipelineIssue
                {
                    code = "SCENE_SAVE_FAILED"
                });
                result.ok = false;
                return result;
            }

            result.saved = true;
        }

        return result;
    }

    private static FactoryAuthoringValidationResult ValidateAuthoringInScenes(
        Scene outsideScene)
    {
        var context = ReadOutsideTestContext(outsideScene);
        var result = new FactoryAuthoringValidationResult
        {
            ok = true,
            scene = FactoryPipelineCommands.OutsideTestScenePath,
            buildingsChecked = context.layouts.Count
        };
        AddContextIssues(context, result.issues);
        ValidateOutsideTestShells(context, result);
        ValidateOutsideTestGlobalState(context, result);
        ValidateBootstrapScene(result.issues);
        ValidateInsideFactoryTemplate(result.issues);
        result.ok = result.issues.Count == 0;
        return result;
    }

    private static void ValidateOutsideTestShells(
        OutsideTestContext context,
        FactoryAuthoringValidationResult result)
    {
        var records = new List<BuildingRecord>();
        foreach (var layout in context.layouts)
        {
            records.Add(layout.ExportBuildingRecord());
        }

        if (!BuildingShellValidation.TryValidateRecords(
                records,
                context.creator is null
                    ? TestBuildingCreator.DefaultDoorCornerExclusionDistance
                    : context.creator.DoorCornerExclusionDistance,
                out _))
        {
            result.issues.Add(new FactoryPipelineIssue
            {
                code = "INVALID_BUILDING_RECORD"
            });
        }

        var expectedPlacements = new List<TestBuildingCreator.WallPlacement>();
        foreach (var layout in context.layouts)
        {
            var record = layout.ExportBuildingRecord();
            var buildingId = record.BuildingInstanceId;
            if (record.FootprintSize.x < 3 || record.FootprintSize.y < 3)
            {
                result.issues.Add(new FactoryPipelineIssue
                {
                    code = "MINIMUM_ENTERABLE_FOOTPRINT",
                    buildingId = buildingId
                });
            }

            var visuals = layout.transform.Find(TestBuildingLayout.GeneratedVisualsName);
            var collision = layout.transform.Find(TestBuildingLayout.GeneratedCollisionName);
            var doors = layout.transform.Find(TestBuildingLayout.VisualDoorsName);
            if (visuals is null)
            {
                result.issues.Add(Issue("SHELL_GENERATED_VISUALS_MISSING", buildingId));
            }

            if (collision is null)
            {
                result.issues.Add(Issue("SHELL_GENERATED_COLLISION_MISSING", buildingId));
            }

            if (doors is null)
            {
                result.issues.Add(Issue("SHELL_VISUAL_DOORS_MISSING", buildingId));
            }

            if (layout.GetComponent<TestBuildingPresentation>() is null)
            {
                result.issues.Add(Issue("SHELL_PRESENTATION_MISSING", buildingId));
            }

            if (context.creator is null)
            {
                continue;
            }

            var secondCorner = layout.AnchorCell + new Vector3Int(
                layout.Size.x - 1,
                layout.Size.y - 1);
            TestBuildingCreator.GetWallPlacements(
                layout.AnchorCell,
                secondCorner,
                expectedPlacements);
            var floorSceneCount = layout.StoryCount;
            result.floorScenesChecked += floorSceneCount;
            for (var floorIndex = 0; floorIndex < floorSceneCount; floorIndex++)
            {
                var floorPath = TestBuildingFloorScenes.GetScenePath(
                    buildingId,
                    floorIndex);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(floorPath) is null)
                {
                    result.issues.Add(Issue(
                        "FLOOR_SCENE_MISSING",
                        buildingId,
                        floorIndex));
                }
            }

            if (visuals is null || collision is null || doors is null)
            {
                continue;
            }

            var roofs = visuals.GetComponentsInChildren<GridRoof>(true);
            if (roofs.Length != layout.StoryCount)
            {
                result.issues.Add(Issue(
                    "ROOF_COUNT_MISMATCH",
                    buildingId));
            }

            var expectedRoofMin = TestBuildingCreator.GetRoofLogicalMin(
                layout.AnchorCell,
                secondCorner);
            var expectedRoofMax = TestBuildingCreator.GetRoofLogicalMax(
                layout.AnchorCell,
                secondCorner);
            for (var roofIndex = 0; roofIndex < roofs.Length; roofIndex++)
            {
                var roof = roofs[roofIndex];
                if (roof.LogicalMin != expectedRoofMin)
                {
                    result.issues.Add(new FactoryPipelineIssue
                    {
                        code = "ROOF_BOUNDS_MISMATCH",
                        buildingId = buildingId,
                        floorIndex = roofIndex,
                        expected = ToArray(expectedRoofMin),
                        actual = ToArray(roof.LogicalMin)
                    });
                }

                if (roof.LogicalMax != expectedRoofMax)
                {
                    result.issues.Add(new FactoryPipelineIssue
                    {
                        code = "ROOF_BOUNDS_MISMATCH",
                        buildingId = buildingId,
                        floorIndex = roofIndex,
                        expected = ToArray(expectedRoofMax),
                        actual = ToArray(roof.LogicalMax)
                    });
                }
            }

            var walls = visuals.GetComponentsInChildren<GridWall>(true);
            var colliders = collision.GetComponentsInChildren<PolygonCollider2D>(true);
            if (walls.Length != expectedPlacements.Count * layout.StoryCount)
            {
                result.issues.Add(Issue("WALL_COUNT_MISMATCH", buildingId));
            }

            if (colliders.Length != expectedPlacements.Count)
            {
                result.issues.Add(Issue("COLLIDER_COUNT_MISMATCH", buildingId));
            }

            if (doors.childCount != layout.Doors.Count)
            {
                result.issues.Add(Issue("DOOR_COUNT_MISMATCH", buildingId));
            }

            if (layout.GetComponentsInChildren<OutsideTestFactoryDoor>(true).Length
                != layout.Doors.Count)
            {
                result.issues.Add(Issue("FACTORY_DOOR_COUNT_MISMATCH", buildingId));
            }

            if (layout.GetComponentsInChildren<ScenePortal>(true).Length
                != layout.Doors.Count)
            {
                result.issues.Add(Issue("PORTAL_COUNT_MISMATCH", buildingId));
            }

            for (var doorIndex = 0;
                doorIndex < layout.Doors.Count && doorIndex < doors.childCount;
                doorIndex++)
            {
                var doorObject = doors.GetChild(doorIndex);
                var surface = doorObject.GetComponent<DepthOcclusionSurface>();
                var factoryDoor = doorObject.GetComponent<OutsideTestFactoryDoor>();
                if (surface is null)
                {
                    result.issues.Add(Issue(
                        "DOOR_SURFACE_MISSING",
                        buildingId));
                }
                else if (!surface.IsConfigured)
                {
                    result.issues.Add(Issue(
                        "DOOR_SURFACE_UNCONFIGURED",
                        buildingId));
                }

                if (factoryDoor is null)
                {
                    result.issues.Add(Issue("FACTORY_DOOR_MISSING", buildingId));
                }
                else if (!factoryDoor.Matches(
                             layout.Doors[doorIndex].WallId,
                             layout.Doors[doorIndex].NormalizedOffset))
                {
                    result.issues.Add(Issue("DOOR_ID_MISMATCH", buildingId));
                }
            }

            foreach (var collider in colliders)
            {
                if (!collider.enabled)
                {
                    result.issues.Add(Issue("COLLIDER_DISABLED", buildingId));
                }

                if (collider.pathCount != 1)
                {
                    result.issues.Add(Issue("COLLIDER_PATH_COUNT_MISMATCH", buildingId));
                }
                else if (collider.GetPath(0).Length == 0)
                {
                    result.issues.Add(Issue("COLLIDER_PATH_EMPTY", buildingId));
                }
            }
        }
    }

    private static void ValidateOutsideTestGlobalState(
        OutsideTestContext context,
        FactoryAuthoringValidationResult result)
    {
        if (context.grid is not null && context.layouts.Count > 0)
        {
            var spawnPosition = context.grid.LogicalToWorld(
                context.grid.InitialPlayerLogicalPosition);
            foreach (var layout in context.layouts)
            {
                var collision = layout.transform.Find(
                    TestBuildingLayout.GeneratedCollisionName);
                if (collision is null)
                {
                    continue;
                }

                if (collision.GetComponentsInChildren<PolygonCollider2D>(true)
                    .Any(collider => collider.OverlapPoint(spawnPosition)))
                {
                    result.issues.Add(Issue(
                        "SPAWN_INSIDE_COLLIDER",
                        layout.BuildingInstanceId));
                }
            }
        }

        var roots = context.scene.GetRootGameObjects();
        var legacyWalls = roots.FirstOrDefault(root => root.name == "Walls");
        if (legacyWalls is not null && legacyWalls.activeSelf)
        {
            result.issues.Add(Issue("LEGACY_WALLS_ACTIVE"));
        }

        var mainCamera = roots.FirstOrDefault(root => root.name == "Main Camera");
        if (mainCamera is not null && mainCamera.activeSelf)
        {
            result.issues.Add(Issue("OUTSIDE_MAIN_CAMERA_ACTIVE"));
        }

        var globalLight = roots.FirstOrDefault(root => root.name == "Global Light 2D");
        if (globalLight is not null && globalLight.activeSelf)
        {
            result.issues.Add(Issue("OUTSIDE_GLOBAL_LIGHT_ACTIVE"));
        }
    }

    private static void ValidateBootstrapScene(List<FactoryPipelineIssue> issues)
    {
        var buildScenes = EditorBuildSettings.scenes;
        if (buildScenes.Length == 0
            || buildScenes[0].path != FactoryPipelineCommands.BootstrapScenePath)
        {
            issues.Add(Issue("BOOTSTRAP_NOT_FIRST"));
        }

        var scene = SceneManager.GetSceneByPath(
            FactoryPipelineCommands.BootstrapScenePath);
        var openedForTest = !scene.isLoaded;
        if (openedForTest
            && AssetDatabase.LoadAssetAtPath<SceneAsset>(
                FactoryPipelineCommands.BootstrapScenePath) is not null)
        {
            scene = EditorSceneManager.OpenScene(
                FactoryPipelineCommands.BootstrapScenePath,
                OpenSceneMode.Additive);
        }

        if (!scene.isLoaded)
        {
            issues.Add(Issue("BOOTSTRAP_SCENE_MISSING"));
            return;
        }

        try
        {
            var managers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GameSceneManager>(true))
                .ToArray();
            if (managers.Length != 1)
            {
                issues.Add(Issue("BOOTSTRAP_MANAGER_COUNT"));
                return;
            }

            var serializedManager = new SerializedObject(managers[0]);
            var worldSceneName = serializedManager.FindProperty("worldSceneName");
            if (worldSceneName is null || worldSceneName.stringValue != "OutsideTest")
            {
                issues.Add(Issue("BOOTSTRAP_WORLD_SCENE_MISMATCH"));
            }
        }
        finally
        {
            if (openedForTest && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static void ValidateInsideFactoryTemplate(List<FactoryPipelineIssue> issues)
    {
        var scene = SceneManager.GetSceneByPath(
            FactoryPipelineCommands.InsideFactoryTemplatePath);
        var openedForTest = !scene.isLoaded;
        if (openedForTest
            && AssetDatabase.LoadAssetAtPath<SceneAsset>(
                FactoryPipelineCommands.InsideFactoryTemplatePath) is not null)
        {
            scene = EditorSceneManager.OpenScene(
                FactoryPipelineCommands.InsideFactoryTemplatePath,
                OpenSceneMode.Additive);
        }

        if (!scene.isLoaded)
        {
            issues.Add(Issue("INSIDE_FACTORY_SCENE_MISSING"));
            return;
        }

        try
        {
            var roots = scene.GetRootGameObjects();
            var grids = roots
                .SelectMany(root => root.GetComponentsInChildren<SceneGrid>(true))
                .Where(grid => grid.isActiveAndEnabled)
                .ToArray();
            if (grids.Length != 1)
            {
                issues.Add(Issue("INSIDE_GRID_COUNT"));
            }
            else
            {
                if (grids[0].Projection != GridProjection.Orthogonal)
                {
                    issues.Add(Issue("INSIDE_GRID_PROJECTION"));
                }

                if (grids[0].VerticalMovementMultiplier != 1f)
                {
                    issues.Add(Issue("INSIDE_GRID_VERTICAL_MOVEMENT"));
                }
            }

            var indoorGrids = roots
                .SelectMany(root => root.GetComponentsInChildren<IndoorGrid>(true))
                .Where(grid => grid.isActiveAndEnabled)
                .ToArray();
            if (indoorGrids.Length != 1)
            {
                issues.Add(Issue("INDOOR_GRID_COUNT"));
            }
            else if (indoorGrids[0].Size != new Vector2Int(6, 6))
            {
                issues.Add(Issue("INDOOR_GRID_SIZE"));
            }

            var controllers = roots
                .SelectMany(root => root.GetComponentsInChildren<InsideFactoryController>(true))
                .ToArray();
            if (controllers.Length != 1)
            {
                issues.Add(Issue("INSIDE_CONTROLLER_COUNT"));
            }

            var visuals = roots
                .SelectMany(root => root.GetComponentsInChildren<InsideFactoryVisuals>(true))
                .ToArray();
            if (visuals.Length != 1)
            {
                issues.Add(Issue("INSIDE_VISUALS_COUNT"));
            }
            else
            {
                var serializedVisuals = new SerializedObject(visuals[0]);
                if (serializedVisuals.FindProperty("floorTile").objectReferenceValue is null
                    || serializedVisuals.FindProperty("doorSprite").objectReferenceValue is null
                    || serializedVisuals.FindProperty("material").objectReferenceValue is null)
                {
                    issues.Add(Issue("INSIDE_VISUAL_REFERENCES_MISSING"));
                }
            }

            var elevators = roots
                .SelectMany(root => root.GetComponentsInChildren<InsideFactoryElevator>(true))
                .ToArray();
            if (elevators.Length != 1)
            {
                issues.Add(Issue("INSIDE_ELEVATOR_COUNT"));
            }

            var portals = roots
                .SelectMany(root => root.GetComponentsInChildren<ScenePortal>(true))
                .ToArray();
            if (portals.Length != 1)
            {
                issues.Add(Issue("INSIDE_PORTAL_COUNT"));
            }
            else if (portals[0].GetComponent<SpriteRenderer>())
            {
                issues.Add(Issue("INSIDE_PORTAL_HAS_SPRITE"));
            }
        }
        finally
        {
            if (openedForTest && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static OutsideTestContext ReadOutsideTestContext(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var grids = roots
            .SelectMany(root => root.GetComponentsInChildren<SceneGrid>(true))
            .Where(grid => grid.isActiveAndEnabled)
            .ToList();
        var creators = roots
            .SelectMany(root => root.GetComponentsInChildren<TestBuildingCreator>(true))
            .ToList();
        var coordinators = roots
            .SelectMany(root => root.GetComponentsInChildren<DepthOcclusionCoordinator>(true))
            .ToList();
        var layouts = roots
            .SelectMany(root => root.GetComponentsInChildren<TestBuildingLayout>(true))
            .OrderBy(layout => layout.BuildingInstanceId)
            .ToList();
        return new OutsideTestContext(
            scene,
            grids,
            creators.Count == 1 ? creators[0] : null!,
            coordinators,
            layouts);
    }

    private static void AddContextIssues(
        OutsideTestContext context,
        List<FactoryPipelineIssue> issues)
    {
        if (context.grids.Count != 1)
        {
            issues.Add(Issue("OUTSIDE_GRID_COUNT"));
        }

        if (context.creator is null)
        {
            issues.Add(Issue("OUTSIDE_CREATOR_COUNT"));
        }

        if (context.coordinators.Count != 1)
        {
            issues.Add(Issue("OUTSIDE_COORDINATOR_COUNT"));
        }
        else if (context.creator is not null
                 && context.creator.GetComponent<DepthOcclusionCoordinator>()
                    != context.coordinators[0])
        {
            issues.Add(Issue("OUTSIDE_COORDINATOR_MISMATCH"));
        }

        if (context.layouts.Count == 0)
        {
            issues.Add(Issue("OUTSIDE_LAYOUTS_EMPTY"));
        }
    }

    private static FactoryPipelineIssue Issue(
        string code,
        uint buildingId = 0,
        int floorIndex = -1)
    {
        return new FactoryPipelineIssue
        {
            code = code,
            buildingId = buildingId,
            floorIndex = floorIndex
        };
    }

    private static float[] ToArray(Vector2 value)
    {
        return new[] { value.x, value.y };
    }

    private static T WithOutsideTestScene<T>(Func<Scene, T> operation)
    {
        var previousSetup = EditorSceneManager.GetSceneManagerSetup();
        var scene = SceneManager.GetSceneByPath(
            FactoryPipelineCommands.OutsideTestScenePath);
        var openedForOperation = !scene.isLoaded;
        if (openedForOperation)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    FactoryPipelineCommands.OutsideTestScenePath) is null)
            {
                throw new FileNotFoundException(
                    "The OutsideTest authoring scene does not exist.",
                    FactoryPipelineCommands.OutsideTestScenePath);
            }

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

    private sealed class OutsideTestContext
    {
        public OutsideTestContext(
            Scene newScene,
            List<SceneGrid> newGrids,
            TestBuildingCreator newCreator,
            List<DepthOcclusionCoordinator> newCoordinators,
            List<TestBuildingLayout> newLayouts)
        {
            scene = newScene;
            grids = newGrids;
            grid = grids.Count == 1 ? grids[0] : null!;
            creator = newCreator;
            coordinators = newCoordinators;
            layouts = newLayouts;
        }

        public readonly Scene scene;
        public readonly SceneGrid grid;
        public readonly TestBuildingCreator creator;
        public readonly List<SceneGrid> grids;
        public readonly List<DepthOcclusionCoordinator> coordinators;
        public readonly List<TestBuildingLayout> layouts;
    }
}
