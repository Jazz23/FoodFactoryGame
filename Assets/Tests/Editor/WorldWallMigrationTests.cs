// Verifies the authored World scene uses unique centered wall cells and regenerated collision.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class WorldWallMigrationTests
{
    [Test]
    public void WorldContainsOnlyValidNonOverlappingWallCells()
    {
        const string scenePath = "Assets/Scenes/World.unity";
        var scene = SceneManager.GetSceneByPath(scenePath);
        var openedForTest = !scene.isLoaded;
        if (openedForTest)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }

        try
        {
            var buildings = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<PreplacedBuilding>(true))
                .ToArray();
            var walls = buildings
                .Where(building => building.Definition.PlacementKind == BuildingPlacementKind.WallSegment)
                .ToArray();
            var occupancy = new BuildingOccupancy();
            var cells = new List<Vector3Int>();
            var edges = new List<GridEdge>();

            Assert.That(walls, Has.Length.EqualTo(34));
            Assert.That(walls.Select(wall => wall.InstanceId).Distinct().Count(), Is.EqualTo(walls.Length));
            Assert.That(walls.Select(wall => wall.AnchorCell).Distinct().Count(), Is.EqualTo(walls.Length));

            foreach (var building in buildings.OrderBy(building => building.InstanceId))
            {
                var instance = new BuildingInstance(
                    building.InstanceId,
                    building.Definition.Id,
                    building.AnchorCell,
                    building.Size,
                    -1,
                    building.Direction,
                    building.WallShape);
                BuildingPlacementRules.GetReservation(
                    instance,
                    building.Definition,
                    cells,
                    edges);
                Assert.That(
                    occupancy.TryReserve(instance.Id, cells, edges),
                    Is.True,
                    $"{building.name} has conflicting occupancy.");
            }

            foreach (var wall in walls)
            {
                Assert.That(WallCellGeometry.IsValid(wall.WallShape), Is.True);
                var renderer = wall.GetComponentInChildren<CenteredWallSegmentRenderer>(true);
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.AnchorCell, Is.EqualTo(wall.AnchorCell));
                Assert.That(renderer.Shape, Is.EqualTo(wall.WallShape));
                Assert.That(renderer.GetComponent<PolygonCollider2D>().enabled, Is.True);
                Assert.That(wall.GetComponentInChildren<DirectionalWallSegmentRenderer>(true), Is.Null);
            }

            var modularWalls = buildings
                .Where(building => building.Definition.PlacementKind == BuildingPlacementKind.CellArea)
                .SelectMany(building => building.GetComponentsInChildren<DirectionalWallSegmentRenderer>(true))
                .ToArray();
            Assert.That(modularWalls, Has.Length.EqualTo(40));
            Assert.That(
                modularWalls.All(wall => wall.GetComponent<PolygonCollider2D>().enabled),
                Is.True);
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
