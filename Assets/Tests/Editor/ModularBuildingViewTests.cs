// Verifies that arbitrary rectangular sizes produce modular geometry and boundary colliders.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class ModularBuildingViewTests
{
    [Test]
    public void ConfigureBuildsOneRoofPerCellAndOneWallPerBoundaryEdge()
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
                false);

            var generatedRoot = buildingObject.transform.Find("Modular Generated");
            var meshFilters = generatedRoot.GetComponentsInChildren<MeshFilter>(true);
            var lineRenderers = generatedRoot.GetComponentsInChildren<LineRenderer>(true);
            var wallSegments = generatedRoot.GetComponentsInChildren<DirectionalWallSegmentRenderer>(true);
            var boundaryCollider = generatedRoot.Find("Boundary Collision").GetComponent<EdgeCollider2D>();
            var interiorCollider = buildingObject.GetComponent<PolygonCollider2D>();

            Assert.That(generatedRoot.childCount, Is.EqualTo(24));
            Assert.That(meshFilters, Has.Length.EqualTo(16));
            Assert.That(lineRenderers, Has.Length.EqualTo(1));
            Assert.That(wallSegments, Has.Length.EqualTo(10));
            Assert.That(
                System.Array.FindAll(
                    wallSegments,
                    segment => segment.Direction == GridEdgeDirection.South),
                Has.Length.EqualTo(3));
            Assert.That(
                System.Array.FindAll(
                    wallSegments,
                    segment => segment.Direction == GridEdgeDirection.North),
                Has.Length.EqualTo(3));
            Assert.That(
                System.Array.FindAll(
                    wallSegments,
                    segment => segment.Direction == GridEdgeDirection.West),
                Has.Length.EqualTo(2));
            Assert.That(
                System.Array.FindAll(
                    wallSegments,
                    segment => segment.Direction == GridEdgeDirection.East),
                Has.Length.EqualTo(2));
            Assert.That(boundaryCollider.points, Has.Length.EqualTo(5));
            Assert.That(interiorCollider.isTrigger, Is.True);
            Assert.That(interiorCollider.GetPath(0), Has.Length.EqualTo(5));

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
}
