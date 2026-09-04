// Verifies generated grid roof slabs sit at the configured wall height without collision.
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GridRoofTests
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
    public void RoofMeshKeepsTopAtConfiguredHeightAndLipBelowIt()
    {
        var roof = CreateRoof(new Vector2(-0.75f, -0.75f), new Vector2(1.75f, 1.75f), 2f, 0.1f);
        var mesh = roof.GetComponent<MeshFilter>().sharedMesh;
        var expectedStart = SceneGrid.Project(GridProjection.Dimetric, new Vector2(-0.75f, -0.75f));
        var expectedEnd = SceneGrid.Project(GridProjection.Dimetric, new Vector2(0f, -0.75f));

        Assert.That(mesh, Is.Not.Null);
        Assert.That(mesh.vertexCount, Is.EqualTo(52));
        Assert.That(mesh.triangles, Has.Length.EqualTo(78));
        AssertVector(mesh.vertices[0], new Vector3(expectedStart.x, expectedStart.y + 1.9f, 0f));
        AssertVector(mesh.vertices[1], new Vector3(expectedEnd.x, expectedEnd.y + 1.9f, 0f));
        AssertVector(mesh.vertices[2], new Vector3(expectedEnd.x, expectedEnd.y + 2f, 0f));
        AssertVector(mesh.vertices[48], new Vector3(expectedStart.x, expectedStart.y + 2f, 0f));
    }

    [Test]
    public void RoofDoesNotAddACollider()
    {
        var roof = CreateRoof(new Vector2(-0.75f, -0.75f), new Vector2(1.75f, 1.75f), 2f, 0.1f);

        Assert.That(roof.GetComponent<Collider2D>(), Is.Null);
        Assert.That(roof.GetComponent<Collider>(), Is.Null);
    }

    [Test]
    public void RoofUsesSplitSurfacesPerSideAndOneTopSurface()
    {
        var roof = CreateRoof(new Vector2(-0.75f, -0.75f), new Vector2(1.75f, 1.75f), 2f, 0.1f);

        Assert.That(roof.transform.childCount, Is.EqualTo(13));
        Assert.That(roof.GetComponent<MeshRenderer>().enabled, Is.False);
        Assert.That(roof.GetComponentsInChildren<DepthOcclusionSurface>(true), Has.Length.EqualTo(13));

        for (var index = 0; index < 4; index++)
        {
            var sideCount = 0;
            foreach (Transform child in roof.transform)
            {
                if (!child.name.Contains($" Side {index} Segment "))
                {
                    continue;
                }

                sideCount++;
                Assert.That(child.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(4));
                Assert.That(child.GetComponent<MeshFilter>().sharedMesh.triangles, Has.Length.EqualTo(6));
            }

            Assert.That(sideCount, Is.EqualTo(3));
        }

        var top = roof.transform.Find("Roof Top");
        Assert.That(top.name, Does.EndWith(" Top"));
        Assert.That(top.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(4));
        Assert.That(top.GetComponent<MeshFilter>().sharedMesh.triangles, Has.Length.EqualTo(6));
    }

    [Test]
    public void SlabSurfacesFollowTheirLogicalDepthAndSortAfterTheMatchingWall()
    {
        var logicalMin = new Vector2(-0.75f, -0.75f);
        var logicalMax = new Vector2(1.75f, 1.75f);
        var roof = CreateRoof(logicalMin, logicalMax, 2f, 0.1f);
        var side0 = roof.transform.Find("Roof Side 0 Segment 0").GetComponent<MeshRenderer>();
        var side1 = roof.transform.Find("Roof Side 1 Segment 0").GetComponent<MeshRenderer>();
        var top = roof.transform.Find("Roof Top").GetComponent<MeshRenderer>();
        var expectedSide0 = GridWall.GetSurfaceSortingOrder(
            new Vector2(logicalMin.x, logicalMin.y),
            new Vector2(0f, logicalMin.y)) + 1;
        var expectedTop = GridWall.GetTopSortingOrderAtDepth(
            logicalMin.x + logicalMin.y
            - WallCellGeometry.ThicknessInCells * 0.5f) + 1;

        Assert.That(side0.sortingOrder, Is.EqualTo(expectedSide0));
        Assert.That(side0.sortingOrder, Is.GreaterThan(side1.sortingOrder));
        Assert.That(top.sortingOrder, Is.EqualTo(expectedTop));
    }

    [Test]
    public void TopSurfaceSortsAfterAWallAtTheSharedInnerCorner()
    {
        var logicalMin = new Vector2(4.75f, -2.25f);
        var logicalMax = new Vector2(6.25f, 1.25f);
        var roof = CreateRoof(logicalMin, logicalMax, 6f, 0.1f, 4f);
        var roofTop = roof.transform.Find("Roof Top").GetComponent<MeshRenderer>();
        var wall = CreateWall(GridWall.WallKind.Vertical, new Vector2Int(6, -2), 4f);
        var wallTop = GetSurfaceRenderer(wall, "Top");

        Assert.That(roofTop.sortingOrder, Is.GreaterThan(wallTop.sortingOrder));
        Assert.That(
            roofTop.sortingOrder,
            Is.EqualTo(GridWall.GetTopSortingOrderAtDepth(
                logicalMin.x + logicalMin.y
                - WallCellGeometry.ThicknessInCells * 0.5f,
                4f) + 1));
    }

    [Test]
    public void StorySlabSurfacesSortAfterTheLowerStory()
    {
        var lowerRoof = CreateRoof(
            new Vector2(-0.75f, -0.75f),
            new Vector2(1.75f, 1.75f),
            2f,
            0.1f,
            0f);
        var upperRoof = CreateRoof(
            new Vector2(-0.75f, -0.75f),
            new Vector2(1.75f, 1.75f),
            4f,
            0.1f,
            2f);

        Assert.That(
            upperRoof.transform.Find("Roof Side 0 Segment 0").GetComponent<MeshRenderer>().sortingOrder,
            Is.EqualTo(lowerRoof.transform.Find("Roof Side 0 Segment 0").GetComponent<MeshRenderer>().sortingOrder + 40));
        Assert.That(
            upperRoof.transform.Find("Roof Top").GetComponent<MeshRenderer>().sortingOrder,
            Is.EqualTo(lowerRoof.transform.Find("Roof Top").GetComponent<MeshRenderer>().sortingOrder + 40));
    }

    private static void AssertVector(Vector3 actual, Vector3 expected)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
        Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
    }

    private GridRoof CreateRoof(
        Vector2 logicalMin,
        Vector2 logicalMax,
        float topHeight,
        float thickness,
        float baseHeight = 0f)
    {
        var roofObject = new GameObject("Roof");
        SceneManager.MoveGameObjectToScene(roofObject, scene);
        var roof = roofObject.AddComponent<GridRoof>();
        var serializedObject = new SerializedObject(roof);
        serializedObject.FindProperty("logicalMin").vector2Value = logicalMin;
        serializedObject.FindProperty("logicalMax").vector2Value = logicalMax;
        serializedObject.FindProperty("topHeight").floatValue = topHeight;
        serializedObject.FindProperty("thickness").floatValue = thickness;
        serializedObject.FindProperty("baseHeight").floatValue = baseHeight;
        serializedObject.ApplyModifiedProperties();
        roof.enabled = false;
        roof.enabled = true;
        return roof;
    }

    private GridWall CreateWall(
        GridWall.WallKind kind,
        Vector2Int cell,
        float baseHeight)
    {
        var wallObject = new GameObject("Wall");
        SceneManager.MoveGameObjectToScene(wallObject, scene);
        var wall = wallObject.AddComponent<GridWall>();
        var serializedObject = new SerializedObject(wall);
        serializedObject.FindProperty("kind").intValue = (int)kind;
        serializedObject.FindProperty("cell").vector2IntValue = cell;
        serializedObject.FindProperty("baseHeight").floatValue = baseHeight;
        serializedObject.ApplyModifiedProperties();
        wall.enabled = false;
        wall.enabled = true;
        return wall;
    }

    private static MeshRenderer GetSurfaceRenderer(GridWall wall, string surfaceSuffix)
    {
        return System.Array.Find(
            wall.GetComponentsInChildren<MeshRenderer>(),
            renderer => renderer.gameObject.name.EndsWith($" {surfaceSuffix}"))!;
    }
}
