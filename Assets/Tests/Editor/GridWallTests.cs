// Verifies GridWall geometry: piece shapes, thickness, corner L-flushness, and the closed ring on a 3x3 footprint.
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
        var grid = gridObject.AddComponent<SceneGrid>();
        grid.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    [Test]
    public void StraightWallsAreSingleHalfCellThickBars()
    {
        var horizontal = CreateWall(GridWall.WallKind.Horizontal, new Vector2Int(0, 1));
        var vertical = CreateWall(GridWall.WallKind.Vertical, new Vector2Int(1, 0));

        var horizontalRects = horizontal.GetLogicalRects();
        var verticalRects = vertical.GetLogicalRects();

        Assert.That(horizontalRects, Has.Count.EqualTo(1));
        Assert.That(horizontalRects[0].size, Is.EqualTo(new Vector2(1f, 0.5f)));
        Assert.That(verticalRects, Has.Count.EqualTo(1));
        Assert.That(verticalRects[0].size, Is.EqualTo(new Vector2(0.5f, 1f)));
    }

    [Test]
    public void EveryRectIsHalfACellThick()
    {
        foreach (GridWall.WallKind kind in System.Enum.GetValues(typeof(GridWall.WallKind)))
        {
            var wall = CreateWall(kind, new Vector2Int(3, 3));
            foreach (var rect in wall.GetLogicalRects())
            {
                Assert.That(
                    Mathf.Approximately(rect.size.x, 0.5f)
                        || Mathf.Approximately(rect.size.y, 0.5f),
                    Is.True,
                    $"{kind} rect {rect} is not half a cell thick");
            }
        }
    }

    [Test]
    public void CornerPiecesAreLShapedWithoutOverlappingRects()
    {
        foreach (GridWall.WallKind kind in System.Enum.GetValues(typeof(GridWall.WallKind)))
        {
            if (kind is not (GridWall.WallKind.CornerNorthWest
                or GridWall.WallKind.CornerNorthEast
                or GridWall.WallKind.CornerSouthWest
                or GridWall.WallKind.CornerSouthEast))
            {
                continue;
            }

            var wall = CreateWall(kind, new Vector2Int(3, 3));
            var rects = wall.GetLogicalRects();

            Assert.That(rects, Has.Count.EqualTo(2), $"{kind} should be an L of two rects");
            Assert.That(
                OverlapArea(rects[0], rects[1]),
                Is.EqualTo(0f).Within(0.0001f),
                $"{kind} rects overlap");
        }
    }

    [Test]
    public void HorizontalWallFootprintUsesTheSceneGridProjection()
    {
        var wall = CreateWall(GridWall.WallKind.Horizontal, new Vector2Int(0, 1));
        var footprint = wall.GetWorldFootprint();

        Assert.That(footprint, Has.Length.EqualTo(4));
        var expectedBottomLeft = SceneGrid.Project(
            GridProjection.Dimetric,
            new Vector2(-0.5f, 0.75f));
        Assert.That(footprint[0].x, Is.EqualTo(expectedBottomLeft.x).Within(0.0001f));
        Assert.That(footprint[0].y, Is.EqualTo(expectedBottomLeft.y).Within(0.0001f));
        Assert.That(footprint[0].z, Is.EqualTo(0f));
    }

    [Test]
    public void ThreeByThreePerimeterPiecesCoverExactlyTheWallRing()
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
        var placedRects = new List<Rect>();
        foreach (var (cell, kind) in pieces)
        {
            var wall = CreateWall(kind, cell);
            foreach (var rect in wall.GetLogicalRects())
            {
                placedRects.Add(new Rect(
                    cell.x + rect.x,
                    cell.y + rect.y,
                    rect.width,
                    rect.height));
            }
        }

        var expectedBands = new[]
        {
            new Rect(-1.5f, 0.75f, 3f, 0.5f),
            new Rect(-1.5f, -1.25f, 3f, 0.5f),
            new Rect(-1.25f, -0.75f, 0.5f, 1.5f),
            new Rect(0.75f, -0.75f, 0.5f, 1.5f)
        };
        var step = 0.05f;
        for (var x = -1.475f; x <= 1.475f; x += step)
        {
            for (var y = -1.475f; y <= 1.475f; y += step)
            {
                var point = new Vector2(x, y);
                var expected = ContainsAny(expectedBands, point);
                var actual = ContainsAny(placedRects, point);
                Assert.That(
                    actual,
                    Is.EqualTo(expected),
                    $"Coverage mismatch at {point}");
            }
        }
    }

    private static bool ContainsAny(Rect[] rects, Vector2 point)
    {
        foreach (var rect in rects)
        {
            if (rect.xMin < point.x
                && point.x < rect.xMax
                && rect.yMin < point.y
                && point.y < rect.yMax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(List<Rect> rects, Vector2 point)
    {
        return ContainsAny(rects.ToArray(), point);
    }

    private static float OverlapArea(Rect first, Rect second)
    {
        var overlapWidth = Mathf.Min(first.xMax, second.xMax) - Mathf.Max(first.xMin, second.xMin);
        var overlapHeight = Mathf.Min(first.yMax, second.yMax) - Mathf.Max(first.yMin, second.yMin);
        return Mathf.Max(0f, overlapWidth) * Mathf.Max(0f, overlapHeight);
    }

    private GridWall CreateWall(GridWall.WallKind kind, Vector2Int cell)
    {
        var wallObject = new GameObject($"Wall {kind} {cell.x},{cell.y}");
        SceneManager.MoveGameObjectToScene(wallObject, scene);
        var wall = wallObject.AddComponent<GridWall>();
        var serializedObject = new SerializedObject(wall);
        serializedObject.FindProperty("kind").intValue = (int)kind;
        serializedObject.FindProperty("cell").vector2IntValue = new Vector2Int(cell.x, cell.y);
        serializedObject.ApplyModifiedProperties();
        return wall;
    }
}
