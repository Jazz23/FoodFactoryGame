// Verifies standalone directional walls share runtime and preview geometry without preview physics.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class DirectionalWallSegmentTests
{
    [Test]
    public void PreviewAndRuntimeUseTheSameDirectionalMesh()
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
                GridEdgeDirection.East);
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

            var runtimeSegment = runtimeObject.GetComponentInChildren<DirectionalWallSegmentRenderer>();
            var previewSegment = previewObject.GetComponentInChildren<DirectionalWallSegmentRenderer>();
            var runtimeMesh = runtimeSegment.GetComponent<MeshFilter>().sharedMesh;
            var previewMesh = previewSegment.GetComponent<MeshFilter>().sharedMesh;

            Assert.That(runtimeSegment.Direction, Is.EqualTo(GridEdgeDirection.East));
            Assert.That(runtimeSegment.Edge, Is.EqualTo(previewSegment.Edge));
            Assert.That(runtimeMesh.vertices, Is.EqualTo(previewMesh.vertices));
            Assert.That(runtimeMesh.triangles, Is.EqualTo(previewMesh.triangles));
            Assert.That(runtimeSegment.IsDegenerate, Is.False);
            Assert.That(runtimeSegment.GetComponent<EdgeCollider2D>().enabled, Is.True);
            Assert.That(previewSegment.GetComponent<EdgeCollider2D>(), Is.Null);
            Assert.That(runtimeObject.GetComponent<ScenePortal>(), Is.Null);
            Assert.That(runtimeObject.GetComponent<PolygonCollider2D>(), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(runtimeObject);
            Object.DestroyImmediate(previewObject);
            Object.DestroyImmediate(gridObject);
        }
    }
}
