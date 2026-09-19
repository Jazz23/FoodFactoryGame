// Verifies logical grid projections, presentation scaling, and scene-grid lookup behavior.
using UnityEditor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SceneGridTests
{
    private Scene scene;

    [SetUp]
    public void SetUp()
    {
        scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [Test]
    public void DimetricProjectionUsesTheExistingTwoToOneBasis()
    {
        var xAxis = SceneGrid.Project(GridProjection.Dimetric, new Vector2(1f, 0f));
        var yAxis = SceneGrid.Project(GridProjection.Dimetric, new Vector2(0f, 1f));

        Assert.That(xAxis, Is.EqualTo(new Vector2(1f, 0.5f)));
        Assert.That(yAxis, Is.EqualTo(new Vector2(-1f, 0.5f)));
    }

    [Test]
    public void DimetricProjectionRoundTripsLogicalCoordinates()
    {
        var logicalPosition = new Vector2(1.5f, -0.5f);
        var projectedPosition = SceneGrid.Project(GridProjection.Dimetric, logicalPosition);

        Assert.That(
            SceneGrid.Unproject(GridProjection.Dimetric, projectedPosition),
            Is.EqualTo(logicalPosition));
    }

    [Test]
    public void OrthogonalProjectionPreservesCellSpacing()
    {
        var logicalPosition = new Vector2(0.5f, 1.5f);

        Assert.That(
            SceneGrid.Project(GridProjection.Orthogonal, logicalPosition),
            Is.EqualTo(logicalPosition));
        Assert.That(
            SceneGrid.Unproject(GridProjection.Orthogonal, logicalPosition),
            Is.EqualTo(logicalPosition));
    }

    [TestCase(GridProjection.Dimetric)]
    [TestCase(GridProjection.Orthogonal)]
    public void GridRoundTripsLogicalCoordinatesWithPresentationScale(
        GridProjection projection)
    {
        var gridObject = new GameObject("Scaled Grid");
        SceneManager.MoveGameObjectToScene(gridObject, scene);
        var grid = gridObject.AddComponent<SceneGrid>();
        var serializedGrid = new SerializedObject(grid);
        serializedGrid.FindProperty("projection").enumValueIndex = (int)projection;
        serializedGrid.FindProperty("logicalOrigin").vector2Value = new Vector2(-3.25f, 4.5f);
        serializedGrid.FindProperty("visualOrigin").vector2Value = new Vector2(1.75f, -2.25f);
        serializedGrid.FindProperty("cellSize").floatValue = 2.5f;
        serializedGrid.ApplyModifiedPropertiesWithoutUndo();

        var logicalPosition = new Vector2(6.125f, -7.75f);
        var worldPosition = grid.LogicalToWorld(logicalPosition);
        var roundTrip = grid.WorldToLogical(worldPosition);

        Assert.That(roundTrip.x, Is.EqualTo(logicalPosition.x).Within(0.0001f));
        Assert.That(roundTrip.y, Is.EqualTo(logicalPosition.y).Within(0.0001f));
    }

    [Test]
    public void PresentationScaleChangesRenderedDistanceWithoutChangingLogicalUnits()
    {
        var gridObject = new GameObject("Presentation Scale Grid");
        SceneManager.MoveGameObjectToScene(gridObject, scene);
        var grid = gridObject.AddComponent<SceneGrid>();
        var serializedGrid = new SerializedObject(grid);
        serializedGrid.FindProperty("projection").enumValueIndex = (int)GridProjection.Dimetric;
        serializedGrid.FindProperty("logicalOrigin").vector2Value = new Vector2(2f, -3f);
        serializedGrid.FindProperty("visualOrigin").vector2Value = new Vector2(-4f, 1.5f);
        serializedGrid.FindProperty("cellSize").floatValue = 3f;
        serializedGrid.ApplyModifiedPropertiesWithoutUndo();

        var logicalStart = new Vector2(2.5f, -1.25f);
        var logicalDelta = new Vector2(2f, 1f);
        var renderedDelta = grid.LogicalToWorld(logicalStart + logicalDelta)
            - grid.LogicalToWorld(logicalStart);
        var expectedRenderedDelta = SceneGrid.Project(
            GridProjection.Dimetric,
            logicalDelta) * 3f;

        Assert.That(renderedDelta.x, Is.EqualTo(expectedRenderedDelta.x).Within(0.0001f));
        Assert.That(renderedDelta.y, Is.EqualTo(expectedRenderedDelta.y).Within(0.0001f));
    }

    [Test]
    public void SpatialAdapterPreservesTheCurrent2DProjection()
    {
        var gridObject = new GameObject("Spatial Adapter Grid");
        SceneManager.MoveGameObjectToScene(gridObject, scene);
        var grid = gridObject.AddComponent<SceneGrid>();
        var serializedGrid = new SerializedObject(grid);
        serializedGrid.FindProperty("projection").enumValueIndex = (int)GridProjection.Dimetric;
        serializedGrid.FindProperty("logicalOrigin").vector2Value = new Vector2(-2f, 3f);
        serializedGrid.FindProperty("visualOrigin").vector2Value = new Vector2(4f, -1f);
        serializedGrid.FindProperty("cellSize").floatValue = 1.5f;
        serializedGrid.ApplyModifiedPropertiesWithoutUndo();

        var location = new FactoryLogicalLocation(13, 1, new Vector2(2.5f, -0.5f));
        var adapter = grid.CreateSpatialAdapter();

        Assert.That(
            adapter.LogicalToWorld2D(location),
            Is.EqualTo(grid.LogicalToWorld(location.FloorPosition)));
    }

    [Test]
    public void SpatialAdapterKeeps2DDimetricProjectionSeparateFrom3DGroundMapping()
    {
        var gridObject = new GameObject("Separate 2D and 3D Mapping Grid");
        SceneManager.MoveGameObjectToScene(gridObject, scene);
        var grid = gridObject.AddComponent<SceneGrid>();
        var serializedGrid = new SerializedObject(grid);
        serializedGrid.FindProperty("projection").enumValueIndex = (int)GridProjection.Dimetric;
        serializedGrid.FindProperty("logicalOrigin").vector2Value = new Vector2(-2f, 3f);
        serializedGrid.FindProperty("visualOrigin").vector2Value = new Vector2(4f, -1f);
        serializedGrid.FindProperty("cellSize").floatValue = 1.5f;
        serializedGrid.ApplyModifiedPropertiesWithoutUndo();

        var logical = new Vector2(2.5f, -0.5f);
        var adapter = grid.CreateSpatialAdapter();
        var world3D = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(1u, 0, logical),
            0f);
        var expected3DGround = grid.VisualOrigin
            + (logical - grid.LogicalOrigin) * grid.CellSize;

        Assert.That(grid.LogicalToWorld(logical), Is.EqualTo(new Vector2(16f, -0.25f)));
        Assert.That(world3D.x, Is.EqualTo(expected3DGround.x).Within(0.0001f));
        Assert.That(world3D.z, Is.EqualTo(expected3DGround.y).Within(0.0001f));
        Assert.That(world3D.x, Is.Not.EqualTo(grid.LogicalToWorld(logical).x));
    }

    [Test]
    public void SpatialAdapterKeeps3DLogicalAxesOrthogonal()
    {
        var gridObject = new GameObject("Orthogonal 3D Mapping Grid");
        SceneManager.MoveGameObjectToScene(gridObject, scene);
        var grid = gridObject.AddComponent<SceneGrid>();
        var adapter = grid.CreateSpatialAdapter();
        var origin = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(1u, 0, Vector2.zero),
            0f);
        var xAxis = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(1u, 0, Vector2.right),
            0f) - origin;
        var yAxis = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(1u, 0, Vector2.up),
            0f) - origin;

        Assert.That(Vector3.Dot(xAxis, yAxis), Is.EqualTo(0f).Within(0.0001f));
        Assert.That(xAxis.magnitude, Is.EqualTo(grid.CellSize).Within(0.0001f));
        Assert.That(yAxis.magnitude, Is.EqualTo(grid.CellSize).Within(0.0001f));
    }

    [Test]
    public void SpatialAdapterRoundTripsA3DLocationWithoutPersistingWorldHeight()
    {
        var gridObject = new GameObject("3D Spatial Adapter Grid");
        SceneManager.MoveGameObjectToScene(gridObject, scene);
        var grid = gridObject.AddComponent<SceneGrid>();
        var serializedGrid = new SerializedObject(grid);
        serializedGrid.FindProperty("projection").enumValueIndex = (int)GridProjection.Dimetric;
        serializedGrid.FindProperty("logicalOrigin").vector2Value = new Vector2(1f, -2f);
        serializedGrid.FindProperty("visualOrigin").vector2Value = new Vector2(-3f, 5f);
        serializedGrid.FindProperty("cellSize").floatValue = 2f;
        serializedGrid.ApplyModifiedPropertiesWithoutUndo();

        var location = new FactoryLogicalLocation(21, 2, new Vector2(4.5f, 1.25f));
        var adapter = grid.CreateSpatialAdapter();
        var worldPosition = adapter.LogicalToWorld3D(location, 7.5f);
        var roundTrip = adapter.WorldToLogical3D(
            worldPosition,
            location.BuildingInstanceId,
            location.FloorIndex);

        Assert.That(worldPosition.y, Is.EqualTo(7.5f));
        Assert.That(roundTrip.BuildingInstanceId, Is.EqualTo(location.BuildingInstanceId));
        Assert.That(roundTrip.FloorIndex, Is.EqualTo(location.FloorIndex));
        Assert.That(roundTrip.FloorPosition.x, Is.EqualTo(location.FloorPosition.x).Within(0.0001f));
        Assert.That(roundTrip.FloorPosition.y, Is.EqualTo(location.FloorPosition.y).Within(0.0001f));
    }

    [Test]
    public void TryGetForSceneResolvesItsExactEnabledGrid()
    {
        var gridObject = new GameObject("Grid");
        SceneManager.MoveGameObjectToScene(gridObject, scene);
        var expectedGrid = gridObject.AddComponent<SceneGrid>();

        var resolved = SceneGrid.TryGetForScene(scene, out var actualGrid);

        Assert.That(resolved, Is.True);
        Assert.That(actualGrid, Is.SameAs(expectedGrid));
    }

    [Test]
    public void TryGetForSceneRejectsAnInactiveGrid()
    {
        var gridObject = new GameObject("Grid");
        SceneManager.MoveGameObjectToScene(gridObject, scene);
        gridObject.AddComponent<SceneGrid>();
        gridObject.SetActive(false);

        var resolved = SceneGrid.TryGetForScene(scene, out _);

        Assert.That(resolved, Is.False);
    }

    [Test]
    public void TryGetForSceneRejectsAMissingGrid()
    {
        var resolved = SceneGrid.TryGetForScene(scene, out _);

        Assert.That(resolved, Is.False);
    }
}
