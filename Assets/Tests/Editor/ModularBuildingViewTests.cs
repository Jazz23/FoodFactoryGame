// Verifies that arbitrary rectangular sizes produce modular geometry and boundary colliders.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

public sealed class ModularBuildingViewTests
{
    [Test]
    public void ConfigureBuildsContinuousSurfacesAndWallRunsWithAnEntranceOpening()
    {
        var gridObject = new GameObject("Grid");
        var grid = gridObject.AddComponent<Grid>();
        grid.cellSize = new Vector3(2f, 1f, 0f);
        grid.cellLayout = GridLayout.CellLayout.Isometric;
        var tilemapObject = new GameObject("Ground");
        tilemapObject.transform.SetParent(gridObject.transform, false);
        var ground = tilemapObject.AddComponent<Tilemap>();
        var buildingObject = new GameObject("Building");
        try
        {
            var modularView = buildingObject.AddComponent<ModularBuildingView>();
            var serializedView = new SerializedObject(modularView);
            serializedView.FindProperty("style").objectReferenceValue = AssetDatabase.LoadAssetAtPath<BuildingVisualStyle>(
                "Assets/Buildings/DefaultBuildingVisualStyle.asset");
            serializedView.ApplyModifiedPropertiesWithoutUndo();
            modularView.Configure(
                new Vector3Int(2, 3),
                new Vector2Int(3, 2),
                ground,
                Vector2Int.zero,
                true);

            var generatedRoot = buildingObject.transform.Find("Modular Generated");
            var meshFilters = generatedRoot.GetComponentsInChildren<MeshFilter>(true);
            var lineRenderers = generatedRoot.GetComponentsInChildren<LineRenderer>(true);
            var wallSegments = generatedRoot.GetComponentsInChildren<DirectionalWallSegmentRenderer>(true);
            var wallColliders = generatedRoot.GetComponentsInChildren<PolygonCollider2D>(true);
            var interiorCollider = buildingObject.GetComponent<PolygonCollider2D>();
            var entrance = generatedRoot.Find("Entrance").GetComponent<SpriteRenderer>();
            var roof = generatedRoot.Find("Roof").GetComponent<MeshFilter>().sharedMesh;
            var sortingGroup = generatedRoot.GetComponent<SortingGroup>();

            Assert.That(generatedRoot.childCount, Is.EqualTo(10));
            Assert.That(meshFilters, Has.Length.EqualTo(6));
            Assert.That(lineRenderers, Has.Length.EqualTo(2));
            Assert.That(wallSegments, Has.Length.EqualTo(4));
            Assert.That(
                System.Array.FindAll(
                    wallSegments,
                    segment => segment.Direction == GridEdgeDirection.South),
                Has.Length.EqualTo(1));
            Assert.That(
                System.Array.FindAll(
                    wallSegments,
                    segment => segment.Direction == GridEdgeDirection.North),
                Has.Length.EqualTo(1));
            Assert.That(
                System.Array.FindAll(
                    wallSegments,
                    segment => segment.Direction == GridEdgeDirection.West),
                Has.Length.EqualTo(1));
            Assert.That(
                System.Array.FindAll(
                    wallSegments,
                    segment => segment.Direction == GridEdgeDirection.East),
                Has.Length.EqualTo(1));
            Assert.That(wallColliders, Has.Length.EqualTo(4));
            Assert.That(generatedRoot.GetComponentsInChildren<EdgeCollider2D>(true), Is.Empty);
            Assert.That(generatedRoot.Find("Entrance"), Is.Not.Null);
            Assert.That(generatedRoot.Find("Entrance Outline"), Is.Not.Null);
            Assert.That(generatedRoot.Find("Floor"), Is.Not.Null);
            Assert.That(generatedRoot.Find("Roof"), Is.Not.Null);
            Assert.That(generatedRoot.Find("Roof Accent"), Is.Not.Null);
            Assert.That(roof.vertexCount, Is.EqualTo(4));
            Assert.That(sortingGroup, Is.Not.Null);
            Assert.That(
                GetVisibleSpriteHeight(entrance),
                Is.EqualTo(modularView.Style.EntranceHeight).Within(0.0001f));
            Assert.That(interiorCollider.isTrigger, Is.True);
            Assert.That(interiorCollider.GetPath(0), Has.Length.EqualTo(5));

            var southWall = System.Array.Find(
                wallSegments,
                segment => segment.Direction == GridEdgeDirection.South);
            var entranceEdge = BuildingFootprint.GetSouthEntranceEdge(
                new Vector3Int(2, 3),
                new Vector2Int(3, 2),
                Vector2Int.zero);
            Assert.That(
                Vector3.Distance(
                    southWall.WorldStart,
                    ground.CellToWorld(entranceEdge.EndCorner)),
                Is.LessThan(0.0001f));
            Assert.That(southWall.HasStartCap, Is.True);
            Assert.That(southWall.HasEndCap, Is.True);

            foreach (var wallSegment in wallSegments)
            {
                var wallCollider = wallSegment.GetComponent<PolygonCollider2D>();
                var worldFootprint = wallSegment.GetWorldFootprint();
                var colliderPath = wallCollider.GetPath(0);
                Assert.That(wallSegment.ThicknessInCells, Is.EqualTo(0.5f));
                Assert.That(wallCollider.enabled, Is.True);
                Assert.That(colliderPath, Has.Length.EqualTo(worldFootprint.Length));
                for (var index = 0; index < worldFootprint.Length; index++)
                {
                    var expected = wallCollider.transform.InverseTransformPoint(worldFootprint[index]);
                    Assert.That(Vector2.Distance(colliderPath[index], expected), Is.LessThan(0.0001f));
                }
            }

            foreach (var meshFilter in meshFilters)
            {
                Assert.That(FirstTriangleCrossZ(meshFilter.sharedMesh), Is.GreaterThan(0f));
            }
        }
        finally
        {
            Object.DestroyImmediate(buildingObject);
            Object.DestroyImmediate(gridObject);
        }
    }

    private static float FirstTriangleCrossZ(Mesh mesh)
    {
        var vertices = mesh.vertices;
        var triangles = mesh.triangles;
        var first = vertices[triangles[0]];
        var second = vertices[triangles[1]];
        var third = vertices[triangles[2]];
        return (second.x - first.x) * (third.y - first.y)
            - (second.y - first.y) * (third.x - first.x);
    }

    private static float GetVisibleSpriteHeight(SpriteRenderer renderer)
    {
        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;
        foreach (var vertex in renderer.sprite.vertices)
        {
            minimum = Mathf.Min(minimum, vertex.y);
            maximum = Mathf.Max(maximum, vertex.y);
        }

        return (maximum - minimum) * renderer.transform.lossyScale.y;
    }
}
