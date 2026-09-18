// Captures isolated route snapshots, proxy identities, and camera evidence for lifecycle review.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class Factory3DRouteVisualEvidenceTests
{
    [UnityTest]
    public IEnumerator CapturesRouteLifecycleEvidenceToIsolatedArtifacts()
    {
        var artifactDirectory = Path.Combine(
            Directory.GetParent(Application.dataPath)!.FullName,
            "TestResults",
            "Factory3DRoute",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactDirectory);

        var savePath = Factory3DRouteFixture.CreateIsolatedSavePath();
        var gridObject = new GameObject("Route Evidence Grid");
        var proxyObject = new GameObject("Route Evidence Proxy Root");
        var cameraObject = new GameObject("Route Evidence Camera");
        var grid = gridObject.AddComponent<SceneGrid>();
        var view = proxyObject.AddComponent<Factory3DRouteProxyView>();
        var camera = cameraObject.AddComponent<Camera>();
        ConfigureCamera(camera);

        var state = Factory3DRouteFixture.CreateState();
        var tick = 0;
        try
        {
            AdvanceUntil(
                state,
                grid,
                view,
                ref tick,
                snapshot => GetEntity(snapshot, Factory3DRouteFixture.LowerBeltEntityId).BeltOccupants.Count > 0,
                0);
            yield return null;
            var sourceStageSavePath = PersistArtifactSave(
                state,
                savePath,
                artifactDirectory,
                "01-floor-a-source-and-elevator-entry");
            WriteArtifact(
                artifactDirectory,
                "01-floor-a-source-and-elevator-entry",
                view,
                camera,
                tick,
                sourceStageSavePath,
                0);

            AdvanceUntil(
                state,
                grid,
                view,
                ref tick,
                snapshot => GetEntity(snapshot, Factory3DRouteFixture.TopElevatorEntityId).OutputCount > 0,
                -1);
            yield return null;
            var transferStageSavePath = PersistArtifactSave(
                state,
                savePath,
                artifactDirectory,
                "02-elevator-transfer");
            WriteArtifact(
                artifactDirectory,
                "02-elevator-transfer",
                view,
                camera,
                tick,
                transferStageSavePath,
                -1);

            AdvanceUntil(
                state,
                grid,
                view,
                ref tick,
                snapshot => GetEntity(snapshot, Factory3DRouteFixture.StorageEntityId).StorageContents > 0,
                1);
            yield return null;
            var storageStageSavePath = PersistArtifactSave(
                state,
                savePath,
                artifactDirectory,
                "03-floor-b-elevator-exit-and-storage");
            WriteArtifact(
                artifactDirectory,
                "03-floor-b-elevator-exit-and-storage",
                view,
                camera,
                tick,
                storageStageSavePath,
                1);

            var floorSelectionSavePath = PersistArtifactSave(
                state,
                savePath,
                artifactDirectory,
                "04-floor-hiding-and-reselection");
            view.SetActiveFloor(0);
            yield return null;
            var hiddenFloorProxyCount = proxyObject.transform.childCount;
            var hiddenFloorScreenshotPath = Path.Combine(
                artifactDirectory,
                "04-floor-hiding.png");
            CaptureCamera(camera, hiddenFloorScreenshotPath);
            var hiddenSnapshotPath = Path.Combine(
                artifactDirectory,
                "04-floor-hiding.snapshot.json");
            var hiddenHierarchyPath = Path.Combine(
                artifactDirectory,
                "04-floor-hiding.hierarchy.json");
            WriteSnapshotJson(hiddenSnapshotPath, view.Snapshot);
            WriteHierarchyJson(hiddenHierarchyPath, view.transform, 0);

            view.SetActiveFloor(1);
            yield return null;
            var reselectedFloorProxyCount = proxyObject.transform.childCount;
            var reselectedScreenshotPath = Path.Combine(
                artifactDirectory,
                "04-floor-reselection.png");
            CaptureCamera(camera, reselectedScreenshotPath);
            var reselectedSnapshotPath = Path.Combine(
                artifactDirectory,
                "04-floor-reselection.snapshot.json");
            var reselectedHierarchyPath = Path.Combine(
                artifactDirectory,
                "04-floor-reselection.hierarchy.json");
            WriteSnapshotJson(reselectedSnapshotPath, view.Snapshot);
            WriteHierarchyJson(reselectedHierarchyPath, view.transform, 1);
            WriteJson(
                Path.Combine(artifactDirectory, "04-floor-hiding-and-reselection.json"),
                new FloorSelectionEvidence
                {
                    stage = "04-floor-hiding-and-reselection",
                    snapshotPath = reselectedSnapshotPath,
                    hierarchyPath = reselectedHierarchyPath,
                    screenshotPath = reselectedScreenshotPath,
                    isolatedSavePath = floorSelectionSavePath,
                    activeFloorIndex = 1,
                    proxyChildCount = reselectedFloorProxyCount,
                    hiddenFloor = 0,
                    reselectedFloor = 1,
                    hiddenFloorProxyCount = hiddenFloorProxyCount,
                    reselectedFloorProxyCount = reselectedFloorProxyCount,
                    hiddenScreenshotPath = hiddenFloorScreenshotPath,
                    reselectedScreenshotPath = reselectedScreenshotPath,
                    hiddenSnapshotPath = hiddenSnapshotPath,
                    reselectedSnapshotPath = reselectedSnapshotPath,
                    hiddenHierarchyPath = hiddenHierarchyPath,
                    reselectedHierarchyPath = reselectedHierarchyPath,
                    simulationTick = tick,
                });

            Assert.That(Factory3DRouteFixture.SaveToIsolatedPath(state, savePath), Is.True);
            var restored = new FactoryWorldState(Factory3DRouteFixture.BuildingId);
            Assert.That(restored.LoadFromFile(savePath), Is.True);
            var restoredSavePath = Path.Combine(artifactDirectory, "05-restored-route-state.db");
            Assert.That(restored.SaveToFile(restoredSavePath), Is.True);
            view.Rebuild(restored, grid, 3f, -1);
            yield return null;
            WriteArtifact(
                artifactDirectory,
                "05-save-reload-restoration",
                view,
                camera,
                tick,
                restoredSavePath,
                -1);

            Debug.Log($"Factory 3D route visual evidence written to {artifactDirectory}");
        }
        finally
        {
            DestroyObject(cameraObject);
            DestroyObject(proxyObject);
            DestroyObject(gridObject);
            DeleteSaveFiles(savePath);
        }
    }

    private static void AdvanceUntil(
        FactoryWorldState state,
        SceneGrid grid,
        Factory3DRouteProxyView view,
        ref int tick,
        Func<Factory3DRouteSnapshot, bool> predicate,
        int activeFloorIndex)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            view.Rebuild(state, grid, 3f, activeFloorIndex);
            if (predicate(view.Snapshot))
            {
                return;
            }

            Factory3DRouteFixture.AdvanceTick(state);
            tick++;
        }

        Assert.Fail("The route evidence fixture did not reach the requested stage within 100 ticks.");
    }

    private static void WriteArtifact(
        string artifactDirectory,
        string stage,
        Factory3DRouteProxyView view,
        Camera camera,
        int simulationTick,
        string isolatedSavePath,
        int activeFloorIndex)
    {
        var snapshotPath = Path.Combine(artifactDirectory, stage + ".snapshot.json");
        var hierarchyPath = Path.Combine(artifactDirectory, stage + ".hierarchy.json");
        var screenshotPath = Path.Combine(artifactDirectory, stage + ".png");
        WriteSnapshotJson(snapshotPath, view.Snapshot);
        WriteHierarchyJson(hierarchyPath, view.transform, activeFloorIndex);
        CaptureCamera(camera, screenshotPath);
        WriteJson(
            Path.Combine(artifactDirectory, stage + ".json"),
            new RouteEvidence
            {
                stage = stage,
                snapshotPath = snapshotPath,
                hierarchyPath = hierarchyPath,
                screenshotPath = screenshotPath,
                isolatedSavePath = isolatedSavePath,
                simulationTick = simulationTick,
                activeFloorIndex = activeFloorIndex,
                 proxyChildCount = view.transform.childCount
             });
    }

    private static string PersistArtifactSave(
        FactoryWorldState state,
        string temporarySavePath,
        string artifactDirectory,
        string stage)
    {
        Assert.That(Factory3DRouteFixture.SaveToIsolatedPath(state, temporarySavePath), Is.True);
        var artifactSavePath = Path.Combine(artifactDirectory, stage + ".db");
        File.Copy(temporarySavePath, artifactSavePath, true);
        return artifactSavePath;
    }

    private static void WriteSnapshotJson(string path, Factory3DRouteSnapshot snapshot)
    {
        var document = new SnapshotDocument
        {
            buildings = snapshot.Buildings.Select(building => building.Id.CanonicalId).ToArray(),
            floors = snapshot.Floors.Select(floor => floor.Id.CanonicalId).ToArray(),
            connections = snapshot.Connections
                .Select(connection => connection.Source.CanonicalId + "->" + connection.Destination.CanonicalId)
                .ToArray(),
            entities = snapshot.Entities.Select(entity => new EntityDocument
            {
                id = entity.Id.CanonicalId,
                definitionId = entity.DefinitionId,
                routeRole = entity.RouteRole.ToString(),
                inputCount = entity.InputCount,
                outputCount = entity.OutputCount,
                storageContents = entity.StorageContents,
                queueCount = entity.BeltOccupants.Count,
                elevatorPhase = entity.ElevatorPhase.ToString(),
                worldPosition = new[] { entity.WorldPosition.x, entity.WorldPosition.y, entity.WorldPosition.z }
            }).ToArray()
        };
        WriteJson(path, document);
    }

    private static void WriteHierarchyJson(string path, Transform root, int activeFloorIndex)
    {
        var identities = new List<IdentityDocument>();
        foreach (var identity in root.GetComponentsInChildren<Factory3DRouteProxyIdentity>(true))
        {
            identities.Add(new IdentityDocument
            {
                canonicalId = identity.CanonicalId,
                kind = identity.Kind.ToString(),
                definitionId = identity.DefinitionId,
                entityId = identity.EntityId.HasValue ? identity.EntityId.Value.CanonicalId : string.Empty,
                sourceEntityId = identity.SourceEntityId.HasValue ? identity.SourceEntityId.Value.CanonicalId : string.Empty,
                destinationEntityId = identity.DestinationEntityId.HasValue ? identity.DestinationEntityId.Value.CanonicalId : string.Empty
            });
        }

        WriteJson(
            path,
            new HierarchyDocument
            {
                rootName = root.name,
                activeFloorIndex = activeFloorIndex,
                childNames = Enumerable.Range(0, root.childCount)
                    .Select(index => root.GetChild(index).name)
                    .ToArray(),
                identities = identities.ToArray()
            });
    }

    private static void CaptureCamera(Camera camera, string path)
    {
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(640, 360, TextureFormat.RGBA32, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, 640f, 360f), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void ConfigureCamera(Camera camera)
    {
        camera.orthographic = true;
        camera.orthographicSize = 8f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.04f, 0.06f, 0.09f, 1f);
        camera.transform.position = new Vector3(5f, 10f, -9f);
        camera.transform.LookAt(new Vector3(4f, 1.5f, 3f));
    }

    private static Factory3DRouteEntitySnapshot GetEntity(
        Factory3DRouteSnapshot snapshot,
        uint entityId)
    {
        return snapshot.Entities.Single(entity => entity.EntityId == entityId);
    }

    private static void WriteJson(string path, object value)
    {
        File.WriteAllText(path, JsonUtility.ToJson(value, true));
    }

    private static void DeleteSaveFiles(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }

    private static void DestroyObject(GameObject target)
    {
        if (target is not null && target)
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    [Serializable]
    private class RouteEvidence
    {
        public string stage = string.Empty;
        public string snapshotPath = string.Empty;
        public string hierarchyPath = string.Empty;
        public string screenshotPath = string.Empty;
        public string isolatedSavePath = string.Empty;
        public int simulationTick;
        public int activeFloorIndex;
        public int proxyChildCount;
    }

    [Serializable]
    private sealed class FloorSelectionEvidence
    {
        public string stage = string.Empty;
        public string snapshotPath = string.Empty;
        public string hierarchyPath = string.Empty;
        public string screenshotPath = string.Empty;
        public string isolatedSavePath = string.Empty;
        public int simulationTick;
        public int activeFloorIndex;
        public int proxyChildCount;
        public int hiddenFloor;
        public int reselectedFloor;
        public int hiddenFloorProxyCount;
        public int reselectedFloorProxyCount;
        public string hiddenScreenshotPath = string.Empty;
        public string reselectedScreenshotPath = string.Empty;
        public string hiddenSnapshotPath = string.Empty;
        public string reselectedSnapshotPath = string.Empty;
        public string hiddenHierarchyPath = string.Empty;
        public string reselectedHierarchyPath = string.Empty;
    }

    [Serializable]
    private sealed class SnapshotDocument
    {
        public string[] buildings = Array.Empty<string>();
        public string[] floors = Array.Empty<string>();
        public string[] connections = Array.Empty<string>();
        public EntityDocument[] entities = Array.Empty<EntityDocument>();
    }

    [Serializable]
    private sealed class EntityDocument
    {
        public string id = string.Empty;
        public string definitionId = string.Empty;
        public string routeRole = string.Empty;
        public int inputCount;
        public int outputCount;
        public int storageContents;
        public int queueCount;
        public string elevatorPhase = string.Empty;
        public float[] worldPosition = Array.Empty<float>();
    }

    [Serializable]
    private sealed class HierarchyDocument
    {
        public string rootName = string.Empty;
        public int activeFloorIndex;
        public string[] childNames = Array.Empty<string>();
        public IdentityDocument[] identities = Array.Empty<IdentityDocument>();
    }

    [Serializable]
    private sealed class IdentityDocument
    {
        public string canonicalId = string.Empty;
        public string kind = string.Empty;
        public string definitionId = string.Empty;
        public string entityId = string.Empty;
        public string sourceEntityId = string.Empty;
        public string destinationEntityId = string.Empty;
    }
}
