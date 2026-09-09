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
