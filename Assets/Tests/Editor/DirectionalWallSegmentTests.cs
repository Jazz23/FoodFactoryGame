// Verifies centered wall cells share exact runtime, preview, and collision geometry.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class DirectionalWallSegmentTests
{
    [TestCase(WallCellShape.Horizontal)]
    [TestCase(WallCellShape.Vertical)]
    [TestCase(WallCellShape.CornerNorthEast)]
    [TestCase(WallCellShape.CornerSouthEast)]
    [TestCase(WallCellShape.CornerSouthWest)]
    [TestCase(WallCellShape.CornerNorthWest)]
    public void PreviewAndRuntimeUseTheSameCenteredMesh(WallCellShape shape)
    {
        var gridObject = new GameObject("Grid");
        var grid = gridObject.AddComponent<Grid>();
        grid.cellSize = new Vector3(2f, 1f, 0f);
        grid.cellLayout = GridLayout.CellLayout.Isometric;
        var tilemapObject = new GameObject("Ground");
        tilemapObject.transform.SetParent(gridObject.transform, false);
        var ground = tilemapObject.AddComponent<Tilemap>();
        var definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(
            "Assets/Buildings/WallBuilding.asset");
        var runtimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Wall.prefab");
        var previewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Previews/WallPreview.prefab");
        var runtimeObject = Object.Instantiate(runtimePrefab);
        var previewObject = Object.Instantiate(previewPrefab);

        try
        {
            var instance = new BuildingInstance(
                7,
                definition.Id,
                new Vector3Int(3, -2),
                Vector2Int.one,
                1,
                WallCellGeometry.GetPrimaryDirection(shape),
                shape);
            runtimeObject.GetComponent<BuildingVisualView>().Configure(
                instance,
                definition,
                ground,
                BuildingVisualMode.Runtime);
            previewObject.GetComponent<BuildingVisualView>().Configure(
                instance,
                definition,
                ground,
                BuildingVisualMode.Preview);

            var runtimeSegment = runtimeObject.GetComponentInChildren<CenteredWallSegmentRenderer>();
            var previewSegment = previewObject.GetComponentInChildren<CenteredWallSegmentRenderer>();
            var runtimeMesh = runtimeSegment.GetComponent<MeshFilter>().sharedMesh;
            var previewMesh = previewSegment.GetComponent<MeshFilter>().sharedMesh;
            var collider = runtimeSegment.GetComponent<PolygonCollider2D>();
            var worldFootprint = runtimeSegment.GetWorldFootprint();

            Assert.That(runtimeSegment.Shape, Is.EqualTo(shape));
            Assert.That(runtimeMesh.vertices, Is.EqualTo(previewMesh.vertices));
            Assert.That(runtimeMesh.triangles, Is.EqualTo(previewMesh.triangles));
            Assert.That(runtimeMesh.triangles, Is.Not.Empty);
            Assert.That(runtimeSegment.IsDegenerate, Is.False);
            Assert.That(
                Vector3.Distance(runtimeSegment.WorldCenter, ground.GetCellCenterWorld(instance.AnchorCell)),
                Is.LessThan(0.0001f));
            Assert.That(collider.enabled, Is.True);
            Assert.That(collider.GetPath(0), Has.Length.EqualTo(worldFootprint.Length));
            for (var index = 0; index < worldFootprint.Length; index++)
            {
                var expected = collider.transform.InverseTransformPoint(worldFootprint[index]);
                Assert.That(Vector2.Distance(collider.GetPath(0)[index], expected), Is.LessThan(0.0001f));
            }

            Assert.That(previewSegment.GetComponent<Collider2D>(), Is.Null);
            Assert.That(runtimeObject.GetComponent<ScenePortal>(), Is.Null);
            Assert.That(runtimeSegment.GetComponent<EdgeCollider2D>(), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(runtimeObject);
            Object.DestroyImmediate(previewObject);
            Object.DestroyImmediate(gridObject);
        }
    }

    [Test]
    public void StraightWallFootprintsAreExactlyHalfACellThick()
    {
        var horizontal = WallCellGeometry.GetLogicalFootprint(WallCellShape.Horizontal);
        var vertical = WallCellGeometry.GetLogicalFootprint(WallCellShape.Vertical);

        Assert.That(GetRange(horizontal, false), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(GetRange(vertical, true), Is.EqualTo(0.5f).Within(0.0001f));
    }

    private static float GetRange(Vector2[] points, bool useX)
    {
        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;
        foreach (var point in points)
        {
            var value = useX ? point.x : point.y;
            minimum = Mathf.Min(minimum, value);
            maximum = Mathf.Max(maximum, value);
        }

        return maximum - minimum;
    }
}
