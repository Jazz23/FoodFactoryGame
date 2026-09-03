// Verifies the OutsideTest fixture has a FishNet-ready spawn and coherent generated building outputs.
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class OutsideTestConformanceTests
{
    [Test]
    public void OutsideTestContainsActiveCanonicalBuildingsWithMatchingCollision()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/OutsideTest.unity");
        var openedForTest = !scene.isLoaded;
        if (openedForTest)
        {
            scene = EditorSceneManager.OpenScene(
                "Assets/Scenes/OutsideTest.unity",
                OpenSceneMode.Additive);
        }

        try
        {
            var roots = scene.GetRootGameObjects();
            var grids = roots
                .SelectMany(root => root.GetComponentsInChildren<SceneGrid>(true))
                .Where(grid => grid.isActiveAndEnabled)
                .ToArray();
            var creators = roots
                .SelectMany(root => root.GetComponentsInChildren<TestBuildingCreator>(true))
                .ToArray();
            var coordinators = roots
                .SelectMany(root => root.GetComponentsInChildren<DepthOcclusionCoordinator>(true))
                .ToArray();
            var layouts = roots
                .SelectMany(root => root.GetComponentsInChildren<TestBuildingLayout>(true))
                .ToArray();

            Assert.That(grids, Has.Length.EqualTo(1));
            Assert.That(creators, Has.Length.EqualTo(1));
            Assert.That(coordinators, Has.Length.EqualTo(1));
            Assert.That(creators[0].GetComponent<DepthOcclusionCoordinator>(), Is.SameAs(coordinators[0]));
            Assert.That(layouts, Is.Not.Empty);

            foreach (var layout in layouts)
            {
                var visuals = layout.transform.Find(TestBuildingLayout.GeneratedVisualsName);
                var collision = layout.transform.Find(TestBuildingLayout.GeneratedCollisionName);
                var doors = layout.transform.Find(TestBuildingLayout.VisualDoorsName);
                var presentation = layout.GetComponent<TestBuildingPresentation>();
                var expectedPlacements = new System.Collections.Generic.List<TestBuildingCreator.WallPlacement>();
                var secondCorner = layout.AnchorCell + new Vector3Int(
                    layout.Size.x - 1,
                    layout.Size.y - 1);
                TestBuildingCreator.GetWallPlacements(
                    layout.AnchorCell,
                    secondCorner,
                    expectedPlacements);

                Assert.That(visuals, Is.Not.Null);
                Assert.That(collision, Is.Not.Null);
                Assert.That(doors, Is.Not.Null);
                Assert.That(presentation, Is.Not.Null);
                var walls = visuals.GetComponentsInChildren<GridWall>(true);
                var colliders = collision.GetComponentsInChildren<PolygonCollider2D>(true);
                Assert.That(walls, Has.Length.EqualTo(expectedPlacements.Count));
                Assert.That(colliders, Has.Length.EqualTo(walls.Length));
                Assert.That(doors.childCount, Is.EqualTo(layout.HasDoor ? 1 : 0));

                if (layout.HasDoor)
                {
                    var doorSurface = doors.GetChild(0).GetComponent<DepthOcclusionSurface>();
                    Assert.That(doorSurface, Is.Not.Null);
                    Assert.That(doorSurface.IsConfigured, Is.True);
                }

                foreach (var collider in colliders)
                {
                    Assert.That(collider.enabled, Is.True);
                    Assert.That(collider.pathCount, Is.EqualTo(1));
                    Assert.That(collider.GetPath(0), Is.Not.Empty);
                }
            }

            var grid = grids[0];
            var spawnPosition = grid.LogicalToWorld(grid.InitialPlayerLogicalPosition);
            var collidersInScene = layouts
                .SelectMany(layout => layout
                    .transform
                    .Find(TestBuildingLayout.GeneratedCollisionName)
                    .GetComponentsInChildren<PolygonCollider2D>(true));
            Assert.That(
                collidersInScene.Any(collider => collider.OverlapPoint(spawnPosition)),
                Is.False);

            var legacyWalls = roots.FirstOrDefault(root => root.name == "Walls");
            Assert.That(legacyWalls is null || !legacyWalls.activeSelf, Is.True);
            Assert.That(
                roots.First(root => root.name == "Main Camera").activeSelf,
                Is.False);
            Assert.That(
                roots.First(root => root.name == "Global Light 2D").activeSelf,
                Is.False);
        }
        finally
        {
            if (openedForTest)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    [Test]
    public void BootstrapIsFirstAndLoadsOutsideTestForItsNetworkManager()
    {
        var buildScenes = EditorBuildSettings.scenes;
        Assert.That(buildScenes, Is.Not.Empty);
        Assert.That(buildScenes[0].path, Is.EqualTo("Assets/Scenes/Bootstrap.unity"));

        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Bootstrap.unity");
        var openedForTest = !scene.isLoaded;
        if (openedForTest)
        {
            scene = EditorSceneManager.OpenScene(
                "Assets/Scenes/Bootstrap.unity",
                OpenSceneMode.Additive);
        }

        try
        {
            var managers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GameSceneManager>(true))
                .ToArray();
            Assert.That(managers, Has.Length.EqualTo(1));

            var serializedManager = new SerializedObject(managers[0]);
            Assert.That(
                serializedManager.FindProperty("worldSceneName").stringValue,
                Is.EqualTo("OutsideTest"));
        }
        finally
        {
            if (openedForTest)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
