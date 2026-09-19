// Verifies deterministic, disposable 3D OutsideTest proxy derivation from logical state.
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using NotAI;
using UnityEngine;

public sealed class Factory3DOutsideTestProxyTests
{
    private GameObject gridObject = null!;
    private GameObject proxyRoot = null!;
    private SceneGrid grid = null!;

    [SetUp]
    public void SetUp()
    {
        gridObject = new GameObject("3D Proxy Test Grid");
        grid = gridObject.AddComponent<SceneGrid>();
        proxyRoot = new GameObject("3D Proxy Test Root");
    }

    [TearDown]
    public void TearDown()
    {
        if (proxyRoot is not null && proxyRoot)
        {
            UnityEngine.Object.DestroyImmediate(proxyRoot);
        }

        if (gridObject is not null && gridObject)
        {
            UnityEngine.Object.DestroyImmediate(gridObject);
        }
    }

    [Test]
    public void ReconcileUsesStableBuildingAndFloorNamesWithoutDuplicatesOrDrift()
    {
        var records = new[]
        {
            CreateRecord(20u, new Vector3Int(10, 0, 0), new Vector2Int(5, 4), 1),
            CreateRecord(7u, new Vector3Int(0, 0, 0), new Vector2Int(5, 4), 2)
        };
        var assembler = new Factory3DOutsideTestProxyAssembler();
        var settings = CreateSettings(2f, 2f);

        var first = assembler.Reconcile(
            proxyRoot.transform,
            grid,
            records,
            Array.Empty<OutsideTestFloorRecord>(),
            settings);
        var building = proxyRoot.transform.Find("Building 7");
        var floor = building!.Find("Floor 1");
        var position = floor!.position;

        var second = assembler.Reconcile(
            proxyRoot.transform,
            grid,
            records,
            Array.Empty<OutsideTestFloorRecord>(),
            settings);

        Assert.That(first.BuildingCount, Is.EqualTo(2));
        Assert.That(first.FloorCount, Is.EqualTo(3));
        Assert.That(second.BuildingCount, Is.EqualTo(2));
        Assert.That(second.FloorCount, Is.EqualTo(3));
        Assert.That(proxyRoot.transform.childCount, Is.EqualTo(2));
        Assert.That(proxyRoot.transform.Find("Building 7"), Is.SameAs(building));
        Assert.That(proxyRoot.transform.Find("Building 20"), Is.Not.Null);
        Assert.That(building!.Find("Floor 0"), Is.Not.Null);
        Assert.That(building.Find("Floor 1"), Is.SameAs(floor));
        Assert.That(floor.position, Is.EqualTo(position));
    }

    [Test]
    public void ReconcileCreatesAndReusesBoundedGroundPresentationThenCleansIt()
    {
        SetLogicalBounds(grid, new Vector2Int(-2, -1), new Vector2Int(4, 3));
        var shader = Shader.Find("Standard");
        Assert.That(shader, Is.Not.Null);
        var sourceMaterial = new Material(shader!);
        var sourceColor = new Color(0.9f, 0.1f, 0.1f, 1f);
        sourceMaterial.color = sourceColor;
        try
        {
            var settings = new Factory3DOutsideTestProxySettings(
                3f,
                3f,
                0.1f,
                0.25f,
                0.7f,
                TestBuildingCreator.DefaultDoorCornerExclusionDistance,
                sourceMaterial);
            var assembler = new Factory3DOutsideTestProxyAssembler();
            assembler.Reconcile(
                proxyRoot.transform,
                grid,
                Array.Empty<BuildingRecord>(),
                Array.Empty<OutsideTestFloorRecord>(),
                settings);

            var groundRoot = proxyRoot.transform.Find(Factory3DOutsideTestProxyAssembler.GroundPresentationRootName);
            var plane = groundRoot!.Find(Factory3DOutsideTestProxyAssembler.GroundPlaneName);
            var logicalGrid = groundRoot.Find(Factory3DOutsideTestProxyAssembler.LogicalGridName);
            var planeMesh = plane!.GetComponent<MeshFilter>()!.sharedMesh;
            var gridMesh = logicalGrid!.GetComponent<MeshFilter>()!.sharedMesh;
            var groundMaterial = plane.GetComponent<MeshRenderer>()!.sharedMaterial;
            Assert.That(groundRoot, Is.Not.Null);
            Assert.That(planeMesh, Is.Not.Null);
            Assert.That(gridMesh, Is.Not.Null);
            Assert.That(gridMesh!.vertexCount, Is.EqualTo((4 + 1 + 3 + 1) * 4));
            Assert.That(planeMesh!.bounds.min.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(planeMesh.bounds.max.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(groundMaterial, Is.Not.SameAs(sourceMaterial));
            Assert.That(sourceMaterial.color.r, Is.EqualTo(sourceColor.r).Within(0.0001f));
            Assert.That(sourceMaterial.color.g, Is.EqualTo(sourceColor.g).Within(0.0001f));
            Assert.That(sourceMaterial.color.b, Is.EqualTo(sourceColor.b).Within(0.0001f));
            Assert.That(sourceMaterial.color.a, Is.EqualTo(sourceColor.a).Within(0.0001f));
            Assert.That(groundRoot.GetComponentsInChildren<Collider>(true), Is.Empty);

            assembler.Reconcile(
                proxyRoot.transform,
                grid,
                Array.Empty<BuildingRecord>(),
                Array.Empty<OutsideTestFloorRecord>(),
                settings);

            Assert.That(
                proxyRoot.transform.Find(Factory3DOutsideTestProxyAssembler.GroundPresentationRootName),
                Is.SameAs(groundRoot));
            Assert.That(
                plane.GetComponent<MeshFilter>()!.sharedMesh,
                Is.SameAs(planeMesh));
            Assert.That(
                logicalGrid.GetComponent<MeshFilter>()!.sharedMesh,
                Is.SameAs(gridMesh));

            assembler.Clear(proxyRoot.transform);
            Assert.That(
                proxyRoot.transform.Find(Factory3DOutsideTestProxyAssembler.GroundPresentationRootName),
                Is.Null);
            Assert.That(planeMesh == null, Is.True);
            Assert.That(gridMesh == null, Is.True);
            Assert.That(groundMaterial == null, Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceMaterial);
        }
    }

    [Test]
    public void ReconcileOmitsGroundPresentationWhenLogicalBoundsAreAbsent()
    {
        new Factory3DOutsideTestProxyAssembler().Reconcile(
            proxyRoot.transform,
            grid,
            Array.Empty<BuildingRecord>(),
            Array.Empty<OutsideTestFloorRecord>(),
            CreateSettings(3f, 2f));

        Assert.That(
            proxyRoot.transform.Find(Factory3DOutsideTestProxyAssembler.GroundPresentationRootName),
            Is.Null);
    }

    [Test]
    public void ReconcilePlacesEveryFloorAtDerivedStoryElevation()
    {
        var record = CreateRecord(
            12u,
            new Vector3Int(3, 4, 0),
            new Vector2Int(6, 5),
            3);
        var assembler = new Factory3DOutsideTestProxyAssembler();
        var settings = CreateSettings(3f, 2f);
        var result = assembler.Reconcile(
            proxyRoot.transform,
            grid,
            new[] { record },
            Array.Empty<OutsideTestFloorRecord>(),
            settings);

        var expectedPlacements = new List<TestBuildingCreator.WallPlacement>();
        TestBuildingCreator.GetWallPlacements(
            record.AnchorCell,
            record.AnchorCell + new Vector3Int(
                record.FootprintSize.x - 1,
                record.FootprintSize.y - 1),
            expectedPlacements);
        Assert.That(result.FloorCount, Is.EqualTo(3));
        Assert.That(result.WallCount, Is.EqualTo(expectedPlacements.Count * 3));
        Assert.That(result.WallSegmentCount, Is.GreaterThan(result.WallCount));

        var building = proxyRoot.transform.Find("Building 12");
        for (var floorIndex = 0; floorIndex < 3; floorIndex++)
        {
            var floor = building!.Find($"Floor {floorIndex}");
            var wall = FindFirstChildStartingWith(floor!, "Wall ");
            var segment = wall!.GetChild(0);
            Assert.That(
                segment.position.y,
                Is.EqualTo(floorIndex * settings.StoryHeight + settings.WallHeight * 0.5f)
                    .Within(0.0001f));
        }
    }

    [Test]
    public void ReconcilePlacesBuildingDoorsOnlyOnGroundFloor()
    {
        var record = CreateRecord(
            21u,
            new Vector3Int(2, 3, 0),
            new Vector2Int(6, 5),
            3);
        var assembler = new Factory3DOutsideTestProxyAssembler();
        var result = assembler.Reconcile(
            proxyRoot.transform,
            grid,
            new[] { record },
            Array.Empty<OutsideTestFloorRecord>(),
            CreateSettings(3f, 2f));

        var doorName = $"Door {record.Doors[0].WallId} {record.Doors[0].NormalizedOffset:0.######}";
        var building = proxyRoot.transform.Find("Building 21");
        Assert.That(result.DoorCount, Is.EqualTo(1));
        Assert.That(building!.Find($"Floor 0/{doorName}"), Is.Not.Null);
        Assert.That(building.Find($"Floor 1/{doorName}"), Is.Null);
        Assert.That(building.Find($"Floor 2/{doorName}"), Is.Null);
    }

    [Test]
    public void StateAwareReconcileKeepsBuildingLevelDoorsOnGroundFloorOnly()
    {
        var record = CreateRecord(
            26u,
            new Vector3Int(2, 3, 0),
            new Vector2Int(6, 5),
            3);
        var state = new FactoryWorldState(record.BuildingInstanceId);
        Assert.That(state.TryRegisterBuilding(record, out var registrationError), Is.True, registrationError);
        var assembler = new Factory3DOutsideTestProxyAssembler();

        var result = assembler.Reconcile(
            proxyRoot.transform,
            grid,
            state,
            CreateSettings(3f, 2f));

        var doorName = $"Door {record.Doors[0].WallId} {record.Doors[0].NormalizedOffset:0.######}";
        var building = proxyRoot.transform.Find("Building 26");
        Assert.That(result.DoorCount, Is.EqualTo(1));
        Assert.That(building!.Find($"Floor 0/{doorName}"), Is.Not.Null);
        Assert.That(building.Find($"Floor 1/{doorName}"), Is.Null);
        Assert.That(building.Find($"Floor 2/{doorName}"), Is.Null);
    }

    [Test]
    public void ReconcileBuildsSlabWithCorrectWindingAndElevation()
    {
        var record = new BuildingRecord(
            22u,
            new Vector3Int(1, 2, 0),
            new Vector2Int(5, 4),
            2,
            Array.Empty<BuildingRecord.DoorPlacement>());
        var settings = CreateSettings(3f, 2f);
        var assembler = new Factory3DOutsideTestProxyAssembler();
        assembler.Reconcile(
            proxyRoot.transform,
            grid,
            new[] { record },
            Array.Empty<OutsideTestFloorRecord>(),
            settings);

        var slab = proxyRoot.transform.Find("Building 22/Floor 1/Floor Slab");
        var mesh = slab!.GetComponent<MeshFilter>()!.sharedMesh;
        Assert.That(mesh.vertexCount, Is.EqualTo(8));
        Assert.That(mesh.triangles, Has.Length.EqualTo(36));
        Assert.That(mesh.bounds.min.y, Is.EqualTo(settings.StoryHeight - settings.FloorSlabThickness).Within(0.0001f));
        Assert.That(mesh.bounds.max.y, Is.EqualTo(settings.StoryHeight).Within(0.0001f));

        var topNormals = GetHorizontalFaceNormals(mesh, settings.StoryHeight);
        var bottomNormals = GetHorizontalFaceNormals(
            mesh,
            settings.StoryHeight - settings.FloorSlabThickness);
        Assert.That(topNormals, Has.Count.EqualTo(2));
        Assert.That(bottomNormals, Has.Count.EqualTo(2));
        foreach (var normal in topNormals)
        {
            Assert.That(Vector3.Dot(normal, Vector3.up), Is.GreaterThan(0.999f));
        }

        foreach (var normal in bottomNormals)
        {
            Assert.That(Vector3.Dot(normal, Vector3.down), Is.GreaterThan(0.999f));
        }

        var triangles = mesh.triangles;
        var vertices = mesh.vertices;
        for (var index = 0; index < triangles.Length; index += 3)
        {
            var first = vertices[triangles[index]];
            var second = vertices[triangles[index + 1]];
            var third = vertices[triangles[index + 2]];
            Assert.That(
                Vector3.Cross(second - first, third - first).sqrMagnitude,
                Is.GreaterThan(0.000001f));
        }
    }

    [Test]
    public void ReconcileAddsOnlyTheTopStoryRoofAtWallHeight()
    {
        var record = new BuildingRecord(
            27u,
            Vector3Int.zero,
            new Vector2Int(5, 4),
            2,
            Array.Empty<BuildingRecord.DoorPlacement>());
        var settings = CreateSettings(3f, 2f);
        var assembler = new Factory3DOutsideTestProxyAssembler();
        assembler.Reconcile(
            proxyRoot.transform,
            grid,
            new[] { record },
            Array.Empty<OutsideTestFloorRecord>(),
            settings);

        Assert.That(proxyRoot.transform.Find("Building 27/Floor 0/Roof"), Is.Null);
        var roof = proxyRoot.transform.Find("Building 27/Floor 1/Roof");
        var mesh = roof!.GetComponent<MeshFilter>()!.sharedMesh;
        Assert.That(mesh.bounds.min.y, Is.EqualTo(settings.StoryHeight + settings.WallHeight - settings.RoofThickness).Within(0.0001f));
        Assert.That(mesh.bounds.max.y, Is.EqualTo(settings.StoryHeight + settings.WallHeight).Within(0.0001f));
    }

    [Test]
    public void ReconcilePreservesTheDedicatedRouteProxyRoot()
    {
        var routeRoot = new GameObject(Factory3DOutsideTestProxyView.RouteProxyRootName);
        routeRoot.transform.SetParent(proxyRoot.transform, false);
        var record = new BuildingRecord(
            29u,
            Vector3Int.zero,
            new Vector2Int(5, 4),
            1,
            Array.Empty<BuildingRecord.DoorPlacement>());
        var assembler = new Factory3DOutsideTestProxyAssembler();
        assembler.Reconcile(
            proxyRoot.transform,
            grid,
            new[] { record },
            Array.Empty<OutsideTestFloorRecord>(),
            CreateSettings(3f, 2f));

        Assert.That(proxyRoot.transform.Find(Factory3DOutsideTestProxyView.RouteProxyRootName), Is.SameAs(routeRoot.transform));
        assembler.Clear(proxyRoot.transform);
        Assert.That(proxyRoot.transform.Find(Factory3DOutsideTestProxyView.RouteProxyRootName), Is.SameAs(routeRoot.transform));
    }

    [Test]
    public void ProxyViewCanIsolateAnActiveFloorAndHideItsRoof()
    {
        var view = proxyRoot.AddComponent<Factory3DOutsideTestProxyView>();
        var record = new BuildingRecord(
            28u,
            Vector3Int.zero,
            new Vector2Int(5, 4),
            2,
            Array.Empty<BuildingRecord.DoorPlacement>());
        view.Rebuild(
            grid,
            new[] { record },
            Array.Empty<OutsideTestFloorRecord>(),
            3f,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance);

        view.SetActiveFloor(1);
        Assert.That(view.ActiveFloor, Is.EqualTo(1));
        Assert.That(proxyRoot.transform.Find("Building 28/Floor 0")!.gameObject.activeSelf, Is.False);
        Assert.That(proxyRoot.transform.Find("Building 28/Floor 1")!.gameObject.activeSelf, Is.True);
        Assert.That(
            FindFirstChildStartingWith(proxyRoot.transform.Find("Building 28/Floor 0")!, "Wall ").gameObject.activeSelf,
            Is.False);
        Assert.That(
            FindFirstChildStartingWith(proxyRoot.transform.Find("Building 28/Floor 1")!, "Wall ").gameObject.activeSelf,
            Is.True);
        Assert.That(proxyRoot.transform.Find("Building 28/Floor 0/Floor Slab")!.gameObject.activeSelf, Is.False);
        Assert.That(proxyRoot.transform.Find("Building 28/Floor 1/Floor Slab")!.gameObject.activeSelf, Is.True);
        Assert.That(proxyRoot.transform.Find("Building 28/Floor 1/Roof")!.gameObject.activeSelf, Is.False);

        view.ClearActiveFloor();
        Assert.That(view.ActiveFloor, Is.EqualTo(-1));
        Assert.That(proxyRoot.transform.Find("Building 28/Floor 0")!.gameObject.activeSelf, Is.True);
        Assert.That(
            FindFirstChildStartingWith(proxyRoot.transform.Find("Building 28/Floor 0")!, "Wall ").gameObject.activeSelf,
            Is.True);
        Assert.That(proxyRoot.transform.Find("Building 28/Floor 0/Floor Slab")!.gameObject.activeSelf, Is.False);
        Assert.That(proxyRoot.transform.Find("Building 28/Floor 1/Floor Slab")!.gameObject.activeSelf, Is.False);
        Assert.That(proxyRoot.transform.Find("Building 28/Floor 1/Roof")!.gameObject.activeSelf, Is.True);
    }

    [Test]
    public void BridgeControlledExteriorModeSuppressesRoofsWhileShowingWallsAndKeepingSlabsHidden()
    {
        var view = proxyRoot.AddComponent<Factory3DOutsideTestProxyView>();
        var record = new BuildingRecord(
            30u,
            Vector3Int.zero,
            new Vector2Int(5, 4),
            2,
            Array.Empty<BuildingRecord.DoorPlacement>());
        view.Rebuild(
            grid,
            new[] { record },
            Array.Empty<OutsideTestFloorRecord>(),
            3f,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance);

        var floor0 = proxyRoot.transform.Find("Building 30/Floor 0")!;
        var floor1 = proxyRoot.transform.Find("Building 30/Floor 1")!;
        var roof1 = floor1.Find("Roof")!;
        var wall0 = FindFirstChildStartingWith(
            floor0,
            "Wall ");
        var wall1 = FindFirstChildStartingWith(
            floor1,
            "Wall ");
        view.SetExteriorOcclusionSurfacesVisible(false);

        Assert.That(view.ExteriorOcclusionSurfacesVisible, Is.False);
        Assert.That(floor0.gameObject.activeSelf, Is.True);
        Assert.That(floor1.gameObject.activeSelf, Is.True);
        Assert.That(roof1.gameObject.activeSelf, Is.False);
        Assert.That(wall0.gameObject.activeSelf, Is.True);
        Assert.That(wall1.gameObject.activeSelf, Is.True);
        Assert.That(
            floor0.Find("Floor Slab")!.gameObject.activeSelf,
            Is.False);
        Assert.That(
            floor1.Find("Floor Slab")!.gameObject.activeSelf,
            Is.False);

        view.SetActiveFloor(1);
        Assert.That(view.ActiveFloor, Is.EqualTo(1));
        Assert.That(floor0.gameObject.activeSelf, Is.False);
        Assert.That(floor1.gameObject.activeSelf, Is.True);
        Assert.That(wall0.gameObject.activeSelf, Is.False);
        Assert.That(wall1.gameObject.activeSelf, Is.True);
        Assert.That(floor0.Find("Floor Slab")!.gameObject.activeSelf, Is.False);
        Assert.That(floor1.Find("Floor Slab")!.gameObject.activeSelf, Is.True);
        Assert.That(roof1.gameObject.activeSelf, Is.False);

        view.ClearActiveFloor();
        Assert.That(view.ActiveFloor, Is.EqualTo(-1));
        Assert.That(floor0.gameObject.activeSelf, Is.True);
        Assert.That(floor1.gameObject.activeSelf, Is.True);
        Assert.That(wall0.gameObject.activeSelf, Is.True);
        Assert.That(wall1.gameObject.activeSelf, Is.True);
        Assert.That(roof1.gameObject.activeSelf, Is.False);
        Assert.That(floor0.Find("Floor Slab")!.gameObject.activeSelf, Is.False);
        Assert.That(floor1.Find("Floor Slab")!.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void StateAwareRebuildForwardsConfiguredMaterialToProxyGeometry()
    {
        var record = new BuildingRecord(
            32u,
            Vector3Int.zero,
            new Vector2Int(5, 4),
            1,
            Array.Empty<BuildingRecord.DoorPlacement>());
        var state = new FactoryWorldState(record.BuildingInstanceId);
        Assert.That(state.TryRegisterBuilding(record, out var error), Is.True, error);
        var shader = Shader.Find("Standard");
        Assert.That(shader, Is.Not.Null);
        var material = new Material(shader!);
        try
        {
            var view = proxyRoot.AddComponent<Factory3DOutsideTestProxyView>();
            view.Rebuild(
                grid,
                state,
                3f,
                TestBuildingCreator.DefaultDoorCornerExclusionDistance,
                material);

            var wall = FindFirstChildStartingWith(
                proxyRoot.transform.Find("Building 32/Floor 0")!,
                "Wall ");
            var renderers = wall.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Assert.That(renderers[0].sharedMaterial, Is.SameAs(material));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    [Test]
    public void ReconcilePreservesInteriorOnlyFootprintAndEquipmentCoordinates()
    {
        var state = new FactoryWorldState(23u);
        var interiorSize = new Vector2Int(6, 5);
        Assert.That(
            state.TryRegisterBuilding(23u, 2, interiorSize, out var registrationError),
            Is.True,
            registrationError);
        Assert.That(
            state.TryAddTestMachine(
                23u,
                0,
                new Vector2(0.5f, 0.5f),
                out var entityId,
                out var entityError),
            Is.True,
            entityError);

        var assembler = new Factory3DOutsideTestProxyAssembler();
        var settings = CreateSettings(3f, 2f);
        var result = assembler.Reconcile(
            proxyRoot.transform,
            grid,
            state,
            settings);

        var floor = proxyRoot.transform.Find("Building 23/Floor 0");
        var slabMesh = floor!.Find("Floor Slab")!.GetComponent<MeshFilter>()!.sharedMesh;
        var equipment = floor.Find($"Equipment {entityId}");
        var adapter = grid.CreateSpatialAdapter();
        var expected = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(23u, 0, new Vector2(0.5f, 0.5f)),
            0f);
        var expectedFootprintSize = Get3DFootprintSize(
            grid,
            Vector3Int.zero,
            interiorSize);
        Assert.That(result.FloorCount, Is.EqualTo(2));
        Assert.That(result.EquipmentCount, Is.EqualTo(3));
        Assert.That(slabMesh.bounds.size.x, Is.EqualTo(expectedFootprintSize.x).Within(0.0001f));
        Assert.That(slabMesh.bounds.size.z, Is.EqualTo(expectedFootprintSize.y).Within(0.0001f));
        Assert.That(equipment, Is.Not.Null);
        Assert.That(equipment!.position.x, Is.EqualTo(expected.x).Within(0.0001f));
        Assert.That(equipment.position.z, Is.EqualTo(expected.z).Within(0.0001f));
    }

    [Test]
    public void ReconcileReusesUnchangedSlabMeshAndCleansItWhenCleared()
    {
        var record = new BuildingRecord(
            24u,
            new Vector3Int(0, 0, 0),
            new Vector2Int(5, 4),
            1,
            Array.Empty<BuildingRecord.DoorPlacement>());
        var assembler = new Factory3DOutsideTestProxyAssembler();
        var settings = CreateSettings(2f, 2f);
        assembler.Reconcile(
            proxyRoot.transform,
            grid,
            new[] { record },
            Array.Empty<OutsideTestFloorRecord>(),
            settings);
        var slab = proxyRoot.transform.Find("Building 24/Floor 0/Floor Slab");
        var firstMesh = slab!.GetComponent<MeshFilter>()!.sharedMesh;

        assembler.Reconcile(
            proxyRoot.transform,
            grid,
            new[] { record },
            Array.Empty<OutsideTestFloorRecord>(),
            settings);
        var secondMesh = slab.GetComponent<MeshFilter>()!.sharedMesh;

        Assert.That(secondMesh, Is.SameAs(firstMesh));
        assembler.Clear(proxyRoot.transform);
        Assert.That(firstMesh == null, Is.True);
        Assert.That(proxyRoot.transform.childCount, Is.EqualTo(0));
    }

    [Test]
    public void ReconcileUsesInsetRoofFootprintWithoutChangingFloorSlabFootprint()
    {
        var record = new BuildingRecord(
            26u,
            new Vector3Int(2, 3, 0),
            new Vector2Int(5, 4),
            1,
            Array.Empty<BuildingRecord.DoorPlacement>());
        var assembler = new Factory3DOutsideTestProxyAssembler();
        var settings = CreateSettings(3f, 2f);
        assembler.Reconcile(
            proxyRoot.transform,
            grid,
            new[] { record },
            Array.Empty<OutsideTestFloorRecord>(),
            settings);

        var floor = proxyRoot.transform.Find("Building 26/Floor 0")!;
        var slabMesh = floor.Find("Floor Slab")!.GetComponent<MeshFilter>()!.sharedMesh;
        var roofMesh = floor.Find("Roof")!.GetComponent<MeshFilter>()!.sharedMesh;
        Assert.That(roofMesh.bounds.size.x, Is.LessThan(slabMesh.bounds.size.x));
        Assert.That(roofMesh.bounds.size.z, Is.LessThan(slabMesh.bounds.size.z));
    }

    [Test]
    public void ProxyViewLifecycleKeepsDerivedObjectsDisposableAndReadOnly()
    {
        var view = proxyRoot.AddComponent<Factory3DOutsideTestProxyView>();
        var record = new BuildingRecord(
            25u,
            Vector3Int.zero,
            new Vector2Int(5, 4),
            1,
            Array.Empty<BuildingRecord.DoorPlacement>());
        var result = view.Rebuild(
            grid,
            new[] { record },
            Array.Empty<OutsideTestFloorRecord>(),
            2f,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance);

        Assert.That(result.BuildingCount, Is.EqualTo(1));
        Assert.That(proxyRoot.GetComponent<TestBuildingLayout>(), Is.Null);
        Assert.That(proxyRoot.transform.childCount, Is.GreaterThan(0));
        view.Clear();
        Assert.That(proxyRoot.transform.childCount, Is.EqualTo(0));
        UnityEngine.Object.DestroyImmediate(view);
        Assert.That(proxyRoot.transform.childCount, Is.EqualTo(0));
    }

    [Test]
    public void ReconcileDerivesTranslatedDoorsAndInteriorEquipmentPositions()
    {
        var record = CreateRecord(
            31u,
            new Vector3Int(5, 6, 0),
            new Vector2Int(6, 5),
            2);
        var state = new FactoryWorldState(record.BuildingInstanceId);
        Assert.That(state.TryRegisterBuilding(record, out var registrationError), Is.True, registrationError);
        Assert.That(
            state.TryAddTestEntity(
                record.BuildingInstanceId,
                0,
                FactoryEntityDefinitions.TestMachineDefinitionId,
                new Vector2(1.5f, 2.5f),
                out var entityId,
                out var entityError),
            Is.True,
            entityError);

        var assembler = new Factory3DOutsideTestProxyAssembler();
        var settings = CreateSettings(3f, 2f);
        var result = assembler.Reconcile(
            proxyRoot.transform,
            grid,
            state.BuildingRecords,
            state.FloorStates,
            settings);

        Assert.That(result.DoorCount, Is.EqualTo(1));
        Assert.That(result.EquipmentCount, Is.EqualTo(3));
        var building = proxyRoot.transform.Find("Building 31");
        var floor = building!.Find("Floor 0");
        var door = floor!.Find($"Door {record.Doors[0].WallId} {record.Doors[0].NormalizedOffset:0.######}");
        Assert.That(door, Is.Not.Null);
        Assert.That(
            building.Find($"Floor 1/Door {record.Doors[0].WallId} {record.Doors[0].NormalizedOffset:0.######}"),
            Is.Null);
        var spans = new List<TestBuildingCreator.ExteriorWallSpan>();
        TestBuildingCreator.GetExteriorWallSpans(
            record.AnchorCell,
            record.FootprintSize,
            spans);
        Assert.That(
            TryGetSpan(spans, record.Doors[0].WallId, out var wall),
            Is.True);
        var doorLogical = Vector2.Lerp(
            wall.LogicalStart,
            wall.LogicalEnd,
            record.Doors[0].NormalizedOffset);
        var adapter = grid.CreateSpatialAdapter();
        var expectedDoor = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(record.BuildingInstanceId, 0, doorLogical),
            0f);
        Assert.That(door!.position.x, Is.EqualTo(expectedDoor.x).Within(0.0001f));
        Assert.That(door.position.z, Is.EqualTo(expectedDoor.z).Within(0.0001f));

        var equipment = floor.Find($"Equipment {entityId}");
        Assert.That(equipment, Is.Not.Null);
        Assert.That(
            state.TryGetFloorState(record.BuildingInstanceId, 0, out var floorState),
            Is.True);
        Assert.That(floorState!.TryGetEntity(entityId, out var entity), Is.True);
        var exteriorLogical = BuildingCoordinates.LocalToExteriorLogical(
            record.AnchorCell,
            BuildingCoordinates.InteriorLocalToExteriorLocal(entity!.LogicalPosition));
        var expectedEquipment = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(record.BuildingInstanceId, 0, exteriorLogical),
            0f);
        Assert.That(equipment!.position.x, Is.EqualTo(expectedEquipment.x).Within(0.0001f));
        Assert.That(equipment.position.z, Is.EqualTo(expectedEquipment.z).Within(0.0001f));
    }

    [Test]
    public void ReconcileRemovesStaleBuildingsFloorsDoorsAndEquipment()
    {
        var firstRecord = CreateRecord(
            41u,
            new Vector3Int(0, 0, 0),
            new Vector2Int(6, 5),
            2);
        var secondRecord = CreateRecord(
            42u,
            new Vector3Int(10, 0, 0),
            new Vector2Int(6, 5),
            1);
        var state = new FactoryWorldState(firstRecord.BuildingInstanceId);
        Assert.That(state.TryRegisterBuilding(firstRecord, out var firstError), Is.True, firstError);
        Assert.That(state.TryRegisterBuilding(secondRecord, out var secondError), Is.True, secondError);
        Assert.That(
            state.TryAddTestEntity(
                firstRecord.BuildingInstanceId,
                1,
                FactoryEntityDefinitions.TestStorageDefinitionId,
                new Vector2(1.5f, 1.5f),
                out _,
                out var entityError),
            Is.True,
            entityError);

        var assembler = new Factory3DOutsideTestProxyAssembler();
        var settings = CreateSettings(2f, 2f);
        assembler.Reconcile(
            proxyRoot.transform,
            grid,
            new[] { firstRecord, secondRecord },
            state.FloorStates,
            settings);

        var reducedRecord = CreateRecord(
            41u,
            firstRecord.AnchorCell,
            firstRecord.FootprintSize,
            1);
        assembler.Reconcile(
            proxyRoot.transform,
            grid,
            new[] { reducedRecord },
            Array.Empty<OutsideTestFloorRecord>(),
            settings);

        Assert.That(proxyRoot.transform.childCount, Is.EqualTo(1));
        Assert.That(proxyRoot.transform.Find("Building 42"), Is.Null);
        var remainingBuilding = proxyRoot.transform.Find("Building 41");
        Assert.That(remainingBuilding!.Find("Floor 1"), Is.Null);
        Assert.That(remainingBuilding.Find("Floor 0/Equipment 1"), Is.Null);
        Assert.That(remainingBuilding.GetComponentsInChildren<MeshRenderer>(true), Is.Not.Empty);
    }

    [Test]
    public void ReconcileIsReadOnlyAndDoesNotCreateAnAuthorityComponent()
    {
        var record = CreateRecord(
            51u,
            new Vector3Int(2, 3, 0),
            new Vector2Int(6, 5),
            2);
        var state = new FactoryWorldState(record.BuildingInstanceId);
        Assert.That(state.TryRegisterBuilding(record, out var error), Is.True, error);
        Assert.That(
            state.TryAddTestMachine(
                record.BuildingInstanceId,
                0,
                new Vector2(1.5f, 1.5f),
                out var entityId,
                out var entityError),
            Is.True,
            entityError);
        var beforeRecord = record.Clone();
        Assert.That(state.TryGetFloorState(record.BuildingInstanceId, 0, out var beforeFloor), Is.True);
        Assert.That(beforeFloor!.TryGetEntity(entityId, out var beforeEntity), Is.True);
        var beforeEntitySnapshot = beforeEntity!.Clone();

        new Factory3DOutsideTestProxyAssembler().Reconcile(
            proxyRoot.transform,
            grid,
            state.BuildingRecords,
            state.FloorStates,
            CreateSettings(2f, 2f));

        Assert.That(record.HasSameTopology(beforeRecord), Is.True);
        Assert.That(state.TryGetFloorState(record.BuildingInstanceId, 0, out var afterFloor), Is.True);
        Assert.That(afterFloor!.TryGetEntity(entityId, out var afterEntity), Is.True);
        Assert.That(afterEntity!.LogicalPosition, Is.EqualTo(beforeEntitySnapshot.LogicalPosition));
        Assert.That(afterEntity.OutputCount, Is.EqualTo(beforeEntitySnapshot.OutputCount));
        Assert.That(proxyRoot.GetComponent<TestBuildingLayout>(), Is.Null);
        Assert.That(proxyRoot.GetComponent<Factory3DOutsideTestProxyView>(), Is.Null);
    }

    [Test]
    public void FacadeInteriorSemanticsFeedProxyWithoutChangingAuthoritativeState()
    {
        var managerObject = new GameObject("3D Proxy Test Authority");
        try
        {
            var manager = managerObject.AddComponent<NAIStateManager>();
            var shellRecord = new BuildingRecord(
                61u,
                new Vector3Int(10, 0, 0),
                new Vector2Int(5, 4),
                1,
                Array.Empty<BuildingRecord.DoorPlacement>());
            var interiorSize = new Vector2Int(6, 5);
            Assert.That(manager.TryRegisterBuilding(shellRecord, out var shellError), Is.True, shellError);
            var factoryStateField = typeof(NAIStateManager).GetField(
                "factoryState",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(factoryStateField, Is.Not.Null);
            var factoryState = (FactoryWorldState)factoryStateField!.GetValue(manager)!;
            Assert.That(
                factoryState.TryRegisterBuilding(62u, 1, interiorSize, out var interiorError),
                Is.True,
                interiorError);
            Assert.That(
                manager.TryAddTestMachine(
                    62u,
                    0,
                    new Vector2(0.5f, 0.5f),
                    out var entityId,
                    out var entityError),
                Is.True,
                entityError);

            Assert.That(
                manager.TryGetOutsideTestBuildingInteriorSemantics(
                    shellRecord.BuildingInstanceId,
                    out var shellIsInteriorOnly,
                    out var shellUsableSize),
                Is.True);
            Assert.That(shellIsInteriorOnly, Is.False);
            Assert.That(shellUsableSize, Is.EqualTo(new Vector2Int(3, 2)));
            Assert.That(
                manager.TryGetOutsideTestBuildingInteriorSemantics(
                    62u,
                    out var interiorIsInteriorOnly,
                    out var reportedInteriorSize),
                Is.True);
            Assert.That(interiorIsInteriorOnly, Is.True);
            Assert.That(reportedInteriorSize, Is.EqualTo(interiorSize));
            Assert.That(
                manager.TryGetOutsideTestBuildingInteriorSemantics(
                    999u,
                    out var missingIsInteriorOnly,
                    out var missingSize),
                Is.False);
            Assert.That(missingIsInteriorOnly, Is.False);
            Assert.That(missingSize, Is.EqualTo(Vector2Int.zero));

            Assert.That(manager.TryGetBuildingRecord(62u, out var beforeRecord), Is.True);
            Assert.That(manager.TryGetFloorState(62u, 0, out var beforeFloor), Is.True);
            Assert.That(beforeFloor!.TryGetEntity(entityId, out var beforeEntity), Is.True);
            var beforeEntityPosition = beforeEntity!.LogicalPosition;

            var sources = new List<Factory3DOutsideTestProxyBuildingSource>();
            foreach (var record in manager.BuildingRecords)
            {
                Assert.That(
                    manager.TryGetOutsideTestBuildingInteriorSemantics(
                        record.BuildingInstanceId,
                        out var isInteriorOnly,
                        out var usableInteriorSize),
                    Is.True);
                sources.Add(new Factory3DOutsideTestProxyBuildingSource(
                    record.Clone(),
                    isInteriorOnly,
                    usableInteriorSize));
            }

            var result = new Factory3DOutsideTestProxyAssembler().Reconcile(
                proxyRoot.transform,
                grid,
                sources,
                manager.FloorStates,
                CreateSettings(3f, 2f));
            var floor = proxyRoot.transform.Find("Building 62/Floor 0");
            var slabMesh = floor!.Find("Floor Slab")!.GetComponent<MeshFilter>()!.sharedMesh;
            var equipment = floor.Find($"Equipment {entityId}");
            var adapter = grid.CreateSpatialAdapter();
            var expectedEquipment = adapter.LogicalToWorld3D(
                new FactoryLogicalLocation(62u, 0, beforeEntityPosition),
                0f);
        var expectedFootprintSize = Get3DFootprintSize(
                grid,
                Vector3Int.zero,
                interiorSize);

            Assert.That(result.BuildingCount, Is.EqualTo(2));
            Assert.That(result.EquipmentCount, Is.EqualTo(3));
            Assert.That(slabMesh.bounds.size.x, Is.EqualTo(expectedFootprintSize.x).Within(0.0001f));
            Assert.That(slabMesh.bounds.size.z, Is.EqualTo(expectedFootprintSize.y).Within(0.0001f));
            Assert.That(equipment, Is.Not.Null);
            Assert.That(equipment!.position.x, Is.EqualTo(expectedEquipment.x).Within(0.0001f));
            Assert.That(equipment.position.z, Is.EqualTo(expectedEquipment.z).Within(0.0001f));
            Assert.That(manager.TryGetBuildingRecord(62u, out var afterRecord), Is.True);
            Assert.That(afterRecord!.HasSameTopology(beforeRecord), Is.True);
            Assert.That(manager.TryGetFloorState(62u, 0, out var afterFloor), Is.True);
            Assert.That(afterFloor!.TryGetEntity(entityId, out var afterEntity), Is.True);
            Assert.That(afterEntity!.LogicalPosition, Is.EqualTo(beforeEntityPosition));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(managerObject);
        }
    }

    private static BuildingRecord CreateRecord(
        uint buildingId,
        Vector3Int anchor,
        Vector2Int footprint,
        int storyCount)
    {
        Assert.That(
            TestBuildingCreator.TryGetDefaultEntrance(
                anchor,
                footprint,
                out var door,
                out var error),
            Is.True,
            error);
        return new BuildingRecord(
            buildingId,
            anchor,
            footprint,
            storyCount,
            new[] { door });
    }

    private static Factory3DOutsideTestProxySettings CreateSettings(
        float storyHeight,
        float wallHeight)
    {
        return new Factory3DOutsideTestProxySettings(
            storyHeight,
            wallHeight,
            0.1f,
            0.25f,
            0.7f,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance);
    }

    private static void SetLogicalBounds(
        SceneGrid sourceGrid,
        Vector2Int minimum,
        Vector2Int size)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var hasBoundsField = typeof(SceneGrid).GetField("hasLogicalBounds", flags);
        var minimumField = typeof(SceneGrid).GetField("logicalBoundsMin", flags);
        var sizeField = typeof(SceneGrid).GetField("logicalBoundsSize", flags);
        Assert.That(hasBoundsField, Is.Not.Null);
        Assert.That(minimumField, Is.Not.Null);
        Assert.That(sizeField, Is.Not.Null);
        hasBoundsField!.SetValue(sourceGrid, true);
        minimumField!.SetValue(sourceGrid, minimum);
        sizeField!.SetValue(sourceGrid, size);
    }

    private static Transform FindFirstChildStartingWith(Transform parent, string prefix)
    {
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (child.name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null!;
    }

    private static List<Vector3> GetHorizontalFaceNormals(Mesh mesh, float y)
    {
        var result = new List<Vector3>();
        var vertices = mesh.vertices;
        var triangles = mesh.triangles;
        for (var index = 0; index < triangles.Length; index += 3)
        {
            var first = vertices[triangles[index]];
            var second = vertices[triangles[index + 1]];
            var third = vertices[triangles[index + 2]];
            if (!Mathf.Approximately(first.y, y)
                || !Mathf.Approximately(second.y, y)
                || !Mathf.Approximately(third.y, y))
            {
                continue;
            }

            var normal = Vector3.Cross(second - first, third - first);
            if (normal.sqrMagnitude > 0.000001f)
            {
                result.Add(normal.normalized);
            }
        }

        return result;
    }

    private static Vector2 Get3DFootprintSize(
        SceneGrid sourceGrid,
        Vector3Int anchor,
        Vector2Int size)
    {
        var adapter = sourceGrid.CreateSpatialAdapter();
        var corners = new[]
        {
            adapter.LogicalToWorld3DGround(new Vector2(anchor.x, anchor.y)),
            adapter.LogicalToWorld3DGround(new Vector2(anchor.x + size.x, anchor.y)),
            adapter.LogicalToWorld3DGround(new Vector2(anchor.x + size.x, anchor.y + size.y)),
            adapter.LogicalToWorld3DGround(new Vector2(anchor.x, anchor.y + size.y))
        };
        var minimum = corners[0];
        var maximum = corners[0];
        foreach (var corner in corners)
        {
            minimum = Vector2.Min(minimum, corner);
            maximum = Vector2.Max(maximum, corner);
        }

        return maximum - minimum;
    }

    private static bool TryGetSpan(
        IReadOnlyList<TestBuildingCreator.ExteriorWallSpan> spans,
        string wallId,
        out TestBuildingCreator.ExteriorWallSpan result)
    {
        foreach (var span in spans)
        {
            if (span.StableId == wallId)
            {
                result = span;
                return true;
            }
        }

        result = default;
        return false;
    }
}
