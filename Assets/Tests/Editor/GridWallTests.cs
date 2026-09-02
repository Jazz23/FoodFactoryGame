// Verifies centered plane-wall geometry and its closed 3x3 perimeter loop.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GridWallTests
{
    private Scene scene = default;

    [SetUp]
    public void SetUp()
    {
        scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var gridObject = new GameObject("Grid");
        SceneManager.MoveGameObjectToScene(gridObject, scene);
        gridObject.AddComponent<SceneGrid>();
    }

    [Test]
    public void StraightPlanesSpanOneCellThroughTheirCellCenters()
    {
        var horizontal = CreateWall(GridWall.WallKind.Horizontal, new Vector2Int(0, 1));
        var vertical = CreateWall(GridWall.WallKind.Vertical, new Vector2Int(1, 0));
        var horizontalSegments = horizontal.GetLogicalPlaneSegments();
        var verticalSegments = vertical.GetLogicalPlaneSegments();

        Assert.That(horizontalSegments, Has.Count.EqualTo(1));
        AssertSegment(horizontalSegments[0], new Vector2(0f, 1.5f), new Vector2(1f, 1.5f));
        Assert.That(verticalSegments, Has.Count.EqualTo(1));
        AssertSegment(verticalSegments[0], new Vector2(1.5f, 0f), new Vector2(1.5f, 1f));
    }

    [Test]
    public void CornerPlanesFormLShapesAtTheirCellCenters()
    {
        var cases = new (GridWall.WallKind kind, Vector2Int cell, Vector2 firstEnd, Vector2 secondEnd)[]
        {
            (GridWall.WallKind.CornerNorthWest, new Vector2Int(-1, 1), new Vector2(0f, 1.5f), new Vector2(-0.5f, 1f)),
            (GridWall.WallKind.CornerNorthEast, new Vector2Int(1, 1), new Vector2(1f, 1.5f), new Vector2(1.5f, 1f)),
            (GridWall.WallKind.CornerSouthWest, new Vector2Int(-1, -1), new Vector2(0f, -0.5f), new Vector2(-0.5f, 0f)),
            (GridWall.WallKind.CornerSouthEast, new Vector2Int(1, -1), new Vector2(1f, -0.5f), new Vector2(1.5f, 0f))
        };

        foreach (var (kind, cell, firstEnd, secondEnd) in cases)
        {
            var wall = CreateWall(kind, cell);
            var segments = wall.GetLogicalPlaneSegments();
            var center = SceneGrid.CellCenterLogical(cell);

            Assert.That(segments, Has.Count.EqualTo(2));
            AssertSegment(segments[0], center, firstEnd);
            AssertSegment(segments[1], center, secondEnd);
        }
    }

    [Test]
    public void ThickWallsKeepTheirConfiguredHeightAndHalfCellThickness()
    {
        var wall = CreateWall(GridWall.WallKind.Horizontal, Vector2Int.zero);

        Assert.That(wall.WallHeight, Is.EqualTo(1.75f));
        Assert.That(wall.ThicknessInCells, Is.EqualTo(0.5f));
    }

    [Test]
    public void StraightWallFootprintsAreCenteredAndHalfACellThick()
    {
        var horizontal = CreateWall(GridWall.WallKind.Horizontal, new Vector2Int(0, 1));
        var vertical = CreateWall(GridWall.WallKind.Vertical, new Vector2Int(1, 0));

        Assert.That(horizontal.GetLogicalFootprint(), Is.EqualTo(new[]
        {
            new Vector2(0f, 1.25f),
            new Vector2(1f, 1.25f),
            new Vector2(1f, 1.75f),
            new Vector2(0f, 1.75f)
        }));
        Assert.That(vertical.GetLogicalFootprint(), Is.EqualTo(new[]
        {
            new Vector2(1.25f, 0f),
            new Vector2(1.75f, 0f),
            new Vector2(1.75f, 1f),
            new Vector2(1.25f, 1f)
        }));
    }

    [Test]
    public void ThickWallMeshUsesTheSceneGridProjectionAndIncludesOneWallTopCap()
    {
        var wall = CreateWall(GridWall.WallKind.Horizontal, new Vector2Int(0, 1));
        var mesh = wall.GetComponent<MeshFilter>().sharedMesh;
        var expectedStart = SceneGrid.Project(GridProjection.Dimetric, new Vector2(0f, 1.25f));
        var expectedEnd = SceneGrid.Project(GridProjection.Dimetric, new Vector2(1f, 1.25f));

        Assert.That(mesh, Is.Not.Null);
        Assert.That(mesh.vertexCount, Is.EqualTo(12));
        Assert.That(mesh.triangles, Has.Length.EqualTo(18));
        AssertVector(mesh.vertices[0], new Vector3(expectedStart.x, expectedStart.y, 0f));
        AssertVector(mesh.vertices[1], new Vector3(expectedEnd.x, expectedEnd.y, 0f));
        AssertVector(mesh.vertices[2], new Vector3(expectedEnd.x, expectedEnd.y + 1.75f, 0f));
        AssertVector(mesh.vertices[3], new Vector3(expectedStart.x, expectedStart.y + 1.75f, 0f));
        AssertVector(mesh.vertices[8], new Vector3(expectedStart.x, expectedStart.y + 1.75f, 0f));
    }

    [Test]
    public void ThickWallMeshUsesFaceDirectionTonesAndABrighterTopCap()
    {
        var topWall = CreateWall(GridWall.WallKind.Horizontal, new Vector2Int(0, 1));
        var rightWall = CreateWall(GridWall.WallKind.Vertical, new Vector2Int(1, 0));
        var topMesh = topWall.GetComponent<MeshFilter>().sharedMesh;
        var rightMesh = rightWall.GetComponent<MeshFilter>().sharedMesh;

        AssertColor(topMesh.colors[0], new Color(0.45f, 0.52f, 0.58f, 1f));
        AssertColor(topMesh.colors[4], new Color(0.28f, 0.35f, 0.41f, 1f));
        AssertColor(topMesh.colors[8], new Color(0.62f, 0.68f, 0.74f, 1f));
        AssertColor(rightMesh.colors[0], new Color(0.45f, 0.52f, 0.58f, 1f));
        AssertColor(rightMesh.colors[4], new Color(0.28f, 0.35f, 0.41f, 1f));
    }

    [Test]
    public void CornerWallMeshUsesAJoinedLFootprintWithInnerCornerFaces()
    {
        var wall = CreateWall(GridWall.WallKind.CornerNorthWest, new Vector2Int(-1, 1));
        var footprint = wall.GetLogicalFootprint();
        var mesh = wall.GetComponent<MeshFilter>().sharedMesh;

        Assert.That(footprint, Has.Count.EqualTo(6));
        Assert.That(GetPolygonArea(footprint), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(mesh.vertexCount, Is.EqualTo(22));
        Assert.That(mesh.triangles, Has.Length.EqualTo(36));
    }

    [Test]
    public void WallTopSurfaceRendersAfterItsSideSurfaces()
    {
        var wall = CreateWall(GridWall.WallKind.CornerNorthWest, new Vector2Int(-1, 1));
        var topSortingOrder = int.MinValue;
        var sideSortingOrders = new List<int>();

        foreach (var renderer in wall.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.gameObject.name.EndsWith(" Top"))
            {
                topSortingOrder = renderer.sortingOrder;
            }
            else if (renderer.gameObject.name.Contains(" Side "))
            {
                sideSortingOrders.Add(renderer.sortingOrder);
            }
        }

        Assert.That(topSortingOrder, Is.GreaterThan(Mathf.Max(sideSortingOrders.ToArray())));
    }

    [Test]
    public void ThreeByThreePerimeterPiecesFormOneClosedCenteredLoop()
    {
        var pieces = new (Vector2Int cell, GridWall.WallKind kind)[]
        {
            (new Vector2Int(-1, 1), GridWall.WallKind.CornerNorthWest),
            (new Vector2Int(0, 1), GridWall.WallKind.Horizontal),
            (new Vector2Int(1, 1), GridWall.WallKind.CornerNorthEast),
            (new Vector2Int(-1, 0), GridWall.WallKind.Vertical),
            (new Vector2Int(1, 0), GridWall.WallKind.Vertical),
            (new Vector2Int(-1, -1), GridWall.WallKind.CornerSouthWest),
            (new Vector2Int(0, -1), GridWall.WallKind.Horizontal),
            (new Vector2Int(1, -1), GridWall.WallKind.CornerSouthEast)
        };
        var endpointCounts = new Dictionary<Vector2, int>();
        var segmentCount = 0;
        foreach (var (cell, kind) in pieces)
        {
            var wall = CreateWall(kind, cell);
            foreach (var segment in wall.GetLogicalPlaneSegments())
            {
                AddEndpoint(endpointCounts, segment.Start);
                AddEndpoint(endpointCounts, segment.End);
                segmentCount++;
            }
        }

        Assert.That(segmentCount, Is.EqualTo(12));
        Assert.That(endpointCounts, Has.Count.EqualTo(12));
        Assert.That(endpointCounts.ContainsKey(Vector2.zero), Is.False);
        foreach (var pair in endpointCounts)
        {
            Assert.That(pair.Value, Is.EqualTo(2), $"Disconnected loop point at {pair.Key}");
        }
    }

    private static void AddEndpoint(Dictionary<Vector2, int> endpointCounts, Vector2 endpoint)
    {
        endpointCounts.TryGetValue(endpoint, out var count);
        endpointCounts[endpoint] = count + 1;
    }

    private static void AssertSegment(GridWall.PlaneSegment actual, Vector2 expectedStart, Vector2 expectedEnd)
    {
        Assert.That(actual.Start, Is.EqualTo(expectedStart));
        Assert.That(actual.End, Is.EqualTo(expectedEnd));
    }

    private static void AssertVector(Vector3 actual, Vector3 expected)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
        Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
    }

    private static void AssertColor(Color actual, Color expected)
    {
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
    }

    private static float GetPolygonArea(IReadOnlyList<Vector2> points)
    {
        var area = 0f;
        for (var index = 0; index < points.Count; index++)
        {
            var nextIndex = (index + 1) % points.Count;
            area += points[index].x * points[nextIndex].y - points[nextIndex].x * points[index].y;
        }

        return Mathf.Abs(area) * 0.5f;
    }

    private GridWall CreateWall(GridWall.WallKind kind, Vector2Int cell)
    {
        var wallObject = new GameObject($"Wall {kind} {cell.x},{cell.y}");
        SceneManager.MoveGameObjectToScene(wallObject, scene);
        var wall = wallObject.AddComponent<GridWall>();
        var serializedObject = new SerializedObject(wall);
        serializedObject.FindProperty("kind").intValue = (int)kind;
        serializedObject.FindProperty("cell").vector2IntValue = cell;
        serializedObject.ApplyModifiedProperties();
        wall.enabled = false;
        wall.enabled = true;
        return wall;
    }
}
