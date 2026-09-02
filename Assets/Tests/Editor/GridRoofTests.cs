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
        var expectedEnd = SceneGrid.Project(GridProjection.Dimetric, new Vector2(1.75f, -0.75f));

        Assert.That(mesh, Is.Not.Null);
        Assert.That(mesh.vertexCount, Is.EqualTo(20));
        Assert.That(mesh.triangles, Has.Length.EqualTo(30));
        AssertVector(mesh.vertices[0], new Vector3(expectedStart.x, expectedStart.y + 1.9f, 0f));
        AssertVector(mesh.vertices[1], new Vector3(expectedEnd.x, expectedEnd.y + 1.9f, 0f));
        AssertVector(mesh.vertices[2], new Vector3(expectedEnd.x, expectedEnd.y + 2f, 0f));
        AssertVector(mesh.vertices[16], new Vector3(expectedStart.x, expectedStart.y + 2f, 0f));
    }

    [Test]
    public void RoofDoesNotAddACollider()
    {
        var roof = CreateRoof(new Vector2(-0.75f, -0.75f), new Vector2(1.75f, 1.75f), 2f, 0.1f);

        Assert.That(roof.GetComponent<Collider2D>(), Is.Null);
        Assert.That(roof.GetComponent<Collider>(), Is.Null);
    }

    private static void AssertVector(Vector3 actual, Vector3 expected)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
        Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
    }

    private GridRoof CreateRoof(Vector2 logicalMin, Vector2 logicalMax, float topHeight, float thickness)
    {
        var roofObject = new GameObject("Roof");
        SceneManager.MoveGameObjectToScene(roofObject, scene);
        var roof = roofObject.AddComponent<GridRoof>();
        var serializedObject = new SerializedObject(roof);
        serializedObject.FindProperty("logicalMin").vector2Value = logicalMin;
        serializedObject.FindProperty("logicalMax").vector2Value = logicalMax;
        serializedObject.FindProperty("topHeight").floatValue = topHeight;
        serializedObject.FindProperty("thickness").floatValue = thickness;
        serializedObject.ApplyModifiedProperties();
        roof.enabled = false;
        roof.enabled = true;
        return roof;
    }
}
