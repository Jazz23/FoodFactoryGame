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
                new Vector2(3f, 1f),
                GridEdgeDirection.West,
                3,
                2);

            Assert.That(portal.BuildingInstanceId, Is.EqualTo(7u));
            Assert.That(portal.BuildingSize, Is.EqualTo(new Vector2Int(4, 5)));
            Assert.That(portal.InteriorDoorDirection, Is.EqualTo(GridEdgeDirection.West));
            Assert.That(portal.BuildingStoryCount, Is.EqualTo(3));
            Assert.That(portal.FloorIndex, Is.EqualTo(2));
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

    [Test]
    public void InsideFactoryControllerUsesTheEnteredDoorPositionForItsExit()
    {
        var gridObject = new GameObject("Inside Factory Grid");
        var exitObject = new GameObject("Exit Portal");
        try
        {
            var sceneGrid = gridObject.AddComponent<SceneGrid>();
            var serializedGrid = new SerializedObject(sceneGrid);
            serializedGrid.FindProperty("projection").enumValueIndex = (int)GridProjection.Orthogonal;
            serializedGrid.ApplyModifiedPropertiesWithoutUndo();

            gridObject.AddComponent<EdgeCollider2D>();
            gridObject.AddComponent<IndoorGrid>();
            var exitPortal = exitObject.AddComponent<ScenePortal>();
            var controller = gridObject.AddComponent<InsideFactoryController>();
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("exitPortal").objectReferenceValue = exitPortal;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            controller.Configure(new Vector2Int(5, 4), new Vector2(2.25f, 0.5f));

            Assert.That(exitPortal.InteractionLogicalPosition, Is.EqualTo(new Vector2(2.25f, 0.5f)));
            Assert.That(exitPortal.BuildingInstanceId, Is.EqualTo(0u));
        }
        finally
        {
            Object.DestroyImmediate(gridObject);
            Object.DestroyImmediate(exitObject);
        }
    }

    [Test]
    public void InsideFactoryControllerCreatesAnExitForEveryEnteredDoor()
    {
        var gridObject = new GameObject("Inside Factory Grid");
        var exitObject = new GameObject("Exit Portal");
        try
        {
            var sceneGrid = gridObject.AddComponent<SceneGrid>();
            var serializedGrid = new SerializedObject(sceneGrid);
            serializedGrid.FindProperty("projection").enumValueIndex = (int)GridProjection.Orthogonal;
            serializedGrid.ApplyModifiedPropertiesWithoutUndo();

            gridObject.AddComponent<EdgeCollider2D>();
            gridObject.AddComponent<IndoorGrid>();
            var exitPortal = exitObject.AddComponent<ScenePortal>();
            var controller = gridObject.AddComponent<InsideFactoryController>();
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("exitPortal").objectReferenceValue = exitPortal;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            var interiorPositions = new[]
            {
                new Vector2(2.25f, 0.5f),
                new Vector2(0.5f, 2.75f)
            };
            var exteriorPositions = new[]
            {
                new Vector2(2.25f, -0.25f),
                new Vector2(-0.25f, 2.75f)
            };
            var interiorDirections = new[]
            {
                GridEdgeDirection.South,
                GridEdgeDirection.West
            };
            controller.Configure(
                new Vector2Int(5, 4),
                interiorPositions[0],
                interiorPositions,
                exteriorPositions,
                interiorDirections);

            var additionalExit = gridObject.transform.Find("Exit Portal 1");
            Assert.That(additionalExit, Is.Not.Null);
            var additionalPortal = additionalExit.GetComponent<ScenePortal>();
            Assert.That(additionalPortal.InteractionLogicalPosition, Is.EqualTo(interiorPositions[1]));
            Assert.That(
                additionalPortal.ExteriorArrivalLogicalPosition,
                Is.EqualTo(exteriorPositions[1]));
            Assert.That(
                additionalPortal.InteriorDoorDirection,
                Is.EqualTo(interiorDirections[1]));
            Assert.That(additionalPortal.HasExteriorArrivalLogicalPosition, Is.True);
            Assert.That(additionalExit.GetComponent<SpriteRenderer>(), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(gridObject);
            Object.DestroyImmediate(exitObject);
        }
    }

    [Test]
    public void InsideFactoryControllerDisablesExteriorExitOnUpperFloors()
    {
        var gridObject = new GameObject("Inside Factory Grid");
        var exitObject = new GameObject("Exit Portal");
        try
        {
            var sceneGrid = gridObject.AddComponent<SceneGrid>();
            var serializedGrid = new SerializedObject(sceneGrid);
            serializedGrid.FindProperty("projection").enumValueIndex = (int)GridProjection.Orthogonal;
            serializedGrid.ApplyModifiedPropertiesWithoutUndo();

            gridObject.AddComponent<EdgeCollider2D>();
            gridObject.AddComponent<IndoorGrid>();
            var exitPortal = exitObject.AddComponent<ScenePortal>();
            var controller = gridObject.AddComponent<InsideFactoryController>();
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("exitPortal").objectReferenceValue = exitPortal;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            controller.Configure(
                new Vector2Int(5, 4),
                new Vector2(2.25f, 0.5f),
                new[] { new Vector2(2.25f, 0.5f) },
                new Vector2[0],
                new[] { GridEdgeDirection.South },
                7,
                3,
                1);

            var elevator = gridObject.GetComponent<InsideFactoryElevator>();
            Assert.That(exitObject.activeSelf, Is.False);
            Assert.That(controller.BuildingInstanceId, Is.EqualTo(7u));
            Assert.That(controller.StoryCount, Is.EqualTo(3));
            Assert.That(controller.CurrentFloor, Is.EqualTo(1));
            Assert.That(elevator.CanOpenPrompt, Is.True);
            Assert.That(elevator.CanGoUp, Is.True);
            Assert.That(elevator.CanGoDown, Is.True);
            Assert.That(elevator.IsFloorAvailable(-1), Is.False);
            Assert.That(elevator.IsFloorAvailable(3), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(gridObject);
            Object.DestroyImmediate(exitObject);
        }
    }

    [Test]
    public void ElevatorInteractionPositionIsCenteredAtTheNorthInteriorEdge()
    {
        Assert.That(
            InsideFactoryElevator.GetInteractionLogicalPosition(new Vector2Int(6, 6)),
            Is.EqualTo(new Vector2(3f, 5.5f)));
    }

    [Test]
    public void InsideFactoryDoorRotationFollowsItsWallDirection()
    {
        Assert.That(
            InsideFactoryVisuals.GetDoorRotation(GridEdgeDirection.South),
            Is.EqualTo(0f));
        Assert.That(
            InsideFactoryVisuals.GetDoorRotation(GridEdgeDirection.West),
            Is.EqualTo(90f));
        Assert.That(
            InsideFactoryVisuals.GetDoorRotation(GridEdgeDirection.East),
            Is.EqualTo(-90f));
        Assert.That(
            InsideFactoryVisuals.GetDoorRotation(GridEdgeDirection.North),
            Is.EqualTo(180f));
    }
}
