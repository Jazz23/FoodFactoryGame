// Verifies that factory interiors can be configured from the entered world building size.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class InteriorSizingTests
{
    [Test]
    public void ScenePortalStoresConfiguredBuildingSize()
    {
        var portalObject = new GameObject("Portal");
        try
        {
            var portal = portalObject.AddComponent<ScenePortal>();

            portal.ConfigureBuilding(
                7,
                new Vector2Int(4, 5),
                Vector2.one,
                "FactoryInterior",
                new Vector2(2f, 0.5f),
                new Vector2(3f, 1f));

            Assert.That(portal.BuildingInstanceId, Is.EqualTo(7u));
            Assert.That(portal.BuildingSize, Is.EqualTo(new Vector2Int(4, 5)));
        }
        finally
        {
            Object.DestroyImmediate(portalObject);
        }
    }

    [Test]
    public void IndoorGridConfigureSizeRebuildsLinesAndCollisionBounds()
    {
        var gridObject = new GameObject("Indoor Grid");
        try
        {
            var sceneGrid = gridObject.AddComponent<SceneGrid>();
            var serializedGrid = new SerializedObject(sceneGrid);
            serializedGrid.FindProperty("projection").enumValueIndex = (int)GridProjection.Orthogonal;
            serializedGrid.ApplyModifiedPropertiesWithoutUndo();

            var edgeCollider = gridObject.AddComponent<EdgeCollider2D>();
            var indoorGrid = gridObject.AddComponent<IndoorGrid>();

            indoorGrid.ConfigureSize(new Vector2Int(4, 3));

            var generatedRoot = gridObject.transform.Find("Generated Grid Lines");
            Assert.That(indoorGrid.Size, Is.EqualTo(new Vector2Int(4, 3)));
            Assert.That(generatedRoot.childCount, Is.EqualTo(9));
            Assert.That(edgeCollider.points, Is.EqualTo(new[]
            {
                new Vector2(0f, -0.5f),
                new Vector2(4f, -0.5f),
                new Vector2(4f, 3f),
                new Vector2(0f, 3f),
                new Vector2(0f, -0.5f)
            }));
        }
        finally
        {
            Object.DestroyImmediate(gridObject);
        }
    }
}
