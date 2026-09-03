// Verifies the selected building perimeter and roof bounds used by TestBuildingCreator.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class TestBuildingCreatorTests
{
    [Test]
    public void WallRingMatchesTheSelectedCellsAsItsFootprint()
    {
        var placements = new List<TestBuildingCreator.WallPlacement>();

        TestBuildingCreator.GetWallPlacements(
            new Vector3Int(3, 5),
            new Vector3Int(1, 4),
            placements);

        Assert.That(placements, Has.Count.EqualTo(6));
        Assert.That(
            placements.FindAll(placement => placement.Kind == GridWall.WallKind.Horizontal),
            Has.Count.EqualTo(2));
        Assert.That(
            placements.FindAll(placement => placement.Kind == GridWall.WallKind.Vertical),
            Has.Count.EqualTo(0));
        Assert.That(
            placements.FindAll(placement => placement.Kind is GridWall.WallKind.CornerNorthWest
                or GridWall.WallKind.CornerNorthEast
                or GridWall.WallKind.CornerSouthWest
                or GridWall.WallKind.CornerSouthEast),
            Has.Count.EqualTo(4));
        Assert.That(
            placements.Exists(placement => placement.Kind == GridWall.WallKind.Horizontal
                && placement.Cell == new Vector2Int(2, 4)),
            Is.True);
        Assert.That(
            placements.Exists(placement => placement.Kind == GridWall.WallKind.CornerSouthWest
                && placement.Cell == new Vector2Int(1, 4)),
            Is.True);
    }

    [Test]
    public void RoofBoundsMatchTheSelectedCells()
    {
        var first = new Vector3Int(-2, 4);
        var second = new Vector3Int(1, 5);

        Assert.That(
            TestBuildingCreator.GetRoofLogicalMin(first, second),
            Is.EqualTo(new Vector2(-1.75f, 4.25f)));
        Assert.That(
            TestBuildingCreator.GetRoofLogicalMax(first, second),
            Is.EqualTo(new Vector2(1.75f, 5.75f)));
    }

    [Test]
    public void SingleCellSelectionCreatesFourCornerWallPieces()
    {
        var placements = new List<TestBuildingCreator.WallPlacement>();

        TestBuildingCreator.GetWallPlacements(Vector3Int.zero, Vector3Int.zero, placements);

        Assert.That(placements, Has.Count.EqualTo(4));
        Assert.That(
            placements.FindAll(placement => placement.Kind is GridWall.WallKind.CornerNorthWest
                or GridWall.WallKind.CornerNorthEast
                or GridWall.WallKind.CornerSouthWest
                or GridWall.WallKind.CornerSouthEast),
            Has.Count.EqualTo(4));
        Assert.That(placements.ConvertAll(placement => placement.Cell), Has.All.EqualTo(Vector2Int.zero));
    }

    [Test]
    public void ThreeByThreeSelectionCreatesOnlyItsPerimeterWallPieces()
    {
        var placements = new List<TestBuildingCreator.WallPlacement>();

        TestBuildingCreator.GetWallPlacements(
            Vector3Int.zero,
            new Vector3Int(2, 2),
            placements);

        Assert.That(placements, Has.Count.EqualTo(8));
        foreach (var placement in placements)
        {
            Assert.That(placement.Cell.x, Is.InRange(0, 2));
            Assert.That(placement.Cell.y, Is.InRange(0, 2));
        }
    }

    [Test]
    public void OneCellWideSelectionsDoNotDuplicateStraightWallPieces()
    {
        var placements = new List<TestBuildingCreator.WallPlacement>();

        TestBuildingCreator.GetWallPlacements(
            Vector3Int.zero,
            new Vector3Int(2, 0),
            placements);

        Assert.That(placements, Has.Count.EqualTo(5));
        Assert.That(
            placements.FindAll(placement => placement.Kind == GridWall.WallKind.Horizontal),
            Has.Count.EqualTo(1));
    }

    [Test]
    public void RoofSortingFollowsTheBuildingDepth()
    {
        Assert.That(
            TestBuildingCreator.GetRoofSortingOrder(
                Vector3Int.zero,
                Vector3Int.zero,
                20),
            Is.EqualTo(20));
        Assert.That(
            TestBuildingCreator.GetRoofSortingOrder(
                new Vector3Int(4, -2),
                new Vector3Int(5, 0),
                20),
            Is.EqualTo(0));
        Assert.That(
            TestBuildingCreator.GetRoofSortingOrder(
                new Vector3Int(-4, 2),
                new Vector3Int(-2, 3),
                20),
            Is.EqualTo(40));
    }

    [Test]
    public void RoofClearanceKeepsTheSlabAboveTheWallTop()
    {
        var wallHeight = 2f;
        var roofThickness = 0.1f;

        Assert.That(
            TestBuildingCreator.GetRoofTopHeight(wallHeight, 2f, roofThickness),
            Is.EqualTo(2.1f));
        Assert.That(
            TestBuildingCreator.GetRoofTopHeight(wallHeight, 2.5f, roofThickness),
            Is.EqualTo(2.5f));
    }

    [Test]
    public void ExteriorWallSpansClassifyEveryPerimeterDirection()
    {
        var spans = new List<TestBuildingCreator.ExteriorWallSpan>();

        TestBuildingCreator.GetExteriorWallSpans(
            new Vector3Int(-1, -1),
            new Vector2Int(3, 3),
            spans);

        Assert.That(spans, Has.Count.EqualTo(12));
        Assert.That(
            spans.FindAll(span => span.Direction == GridEdgeDirection.South),
            Has.Count.EqualTo(3));
        Assert.That(
            spans.FindAll(span => span.Direction == GridEdgeDirection.East),
            Has.Count.EqualTo(3));
        Assert.That(
            spans.FindAll(span => span.Direction == GridEdgeDirection.North),
            Has.Count.EqualTo(3));
        Assert.That(
            spans.FindAll(span => span.Direction == GridEdgeDirection.West),
            Has.Count.EqualTo(3));
        Assert.That(
            spans.FindAll(span => !span.IsCorner),
            Has.Count.EqualTo(4));
        Assert.That(
            spans.ConvertAll(span => span.StableId).Distinct().Count(),
            Is.EqualTo(spans.Count));
    }

    [Test]
    public void DoorSelectionUsesTheStableWallId()
    {
        var layoutObject = new GameObject("Layout");
        try
        {
            var layout = layoutObject.AddComponent<TestBuildingLayout>();
            layout.Configure(Vector3Int.zero, new Vector2Int(3, 3));
            var spans = new List<TestBuildingCreator.ExteriorWallSpan>();
            layout.GetExteriorWallSpans(spans);
            var wall = spans.Find(span => !span.IsCorner);

            layout.SetDoor(wall, 0.25f);

            Assert.That(layout.HasDoor, Is.True);
            Assert.That(layout.DoorWallId, Is.EqualTo(wall.StableId));
            Assert.That(layout.DoorOffset, Is.EqualTo(0.25f));
            Assert.That(layout.TryGetDoor(spans, out var resolvedWall), Is.True);
            Assert.That(resolvedWall.StableId, Is.EqualTo(wall.StableId));
        }
        finally
        {
            Object.DestroyImmediate(layoutObject);
        }
    }

    [Test]
    public void SouthDoorMapsToTheBottomInteriorEdgeAtTheSameWallPosition()
    {
        var layoutObject = new GameObject("South Layout");
        try
        {
            var layout = layoutObject.AddComponent<TestBuildingLayout>();
            layout.Configure(new Vector3Int(-1, 5), new Vector2Int(5, 4));
            var spans = new List<TestBuildingCreator.ExteriorWallSpan>();
            layout.GetExteriorWallSpans(spans);
            var wall = spans.Find(span => span.Direction == GridEdgeDirection.South
                && !span.IsCorner
                && span.Cell == new Vector2Int(1, 5));
            layout.SetDoor(wall, 0.25f);

            Assert.That(
                TestBuildingInteriorMapping.TryGetMapping(
                    layout,
                    wall,
                    out var exteriorDoor,
                    out var exteriorArrival,
                    out var interiorArrival,
                    out var normalizedPosition),
                Is.True);
            Assert.That(exteriorDoor, Is.EqualTo(new Vector2(1.25f, 5.5f)));
            Assert.That(normalizedPosition, Is.EqualTo(0.4375f).Within(0.0001f));
            Assert.That(interiorArrival, Is.EqualTo(new Vector2(2.25f, 0.5f)));
            Assert.That(exteriorArrival, Is.EqualTo(new Vector2(1.25f, 4.75f)));
        }
        finally
        {
            Object.DestroyImmediate(layoutObject);
        }
    }

    [Test]
    public void WestDoorMapsToTheLeftInteriorEdgeAtTheSameWallPosition()
    {
        var layoutObject = new GameObject("West Layout");
        try
        {
            var layout = layoutObject.AddComponent<TestBuildingLayout>();
            layout.Configure(new Vector3Int(5, -2), new Vector2Int(3, 5));
            var spans = new List<TestBuildingCreator.ExteriorWallSpan>();
            layout.GetExteriorWallSpans(spans);
            var wall = spans.Find(span => span.Direction == GridEdgeDirection.West
                && !span.IsCorner
                && span.Cell == new Vector2Int(5, 0));
            layout.SetDoor(wall, 0.75f);

            Assert.That(
                TestBuildingInteriorMapping.TryGetMapping(
                    layout,
                    wall,
                    out var exteriorDoor,
                    out var exteriorArrival,
                    out var interiorArrival,
                    out var normalizedPosition),
                Is.True);
            Assert.That(exteriorDoor, Is.EqualTo(new Vector2(5.5f, 0.75f)));
            Assert.That(normalizedPosition, Is.EqualTo(0.5625f).Within(0.0001f));
            Assert.That(interiorArrival, Is.EqualTo(new Vector2(0.5f, 2.75f)));
            Assert.That(exteriorArrival, Is.EqualTo(new Vector2(4.75f, 0.75f)));
        }
        finally
        {
            Object.DestroyImmediate(layoutObject);
        }
    }

    [Test]
    public void EditorBuildingsRequireAtLeastTwoCellsInEachDimension()
    {
        Assert.That(TestBuildingCreator.IsSupportedSize(new Vector2Int(2, 2)), Is.True);
        Assert.That(TestBuildingCreator.IsSupportedSize(new Vector2Int(3, 2)), Is.True);
        Assert.That(TestBuildingCreator.IsSupportedSize(new Vector2Int(2, 3)), Is.True);
        Assert.That(TestBuildingCreator.IsSupportedSize(new Vector2Int(1, 2)), Is.False);
        Assert.That(TestBuildingCreator.IsSupportedSize(new Vector2Int(2, 1)), Is.False);
    }

    [Test]
    public void DepthGeometryRequiresFootprintIntersection()
    {
        var polygon = new List<Vector2>
        {
            new(-1f, 0.5f),
            new(4f, 3f),
            new(-1f, 5.5f),
            new(-6f, 3f)
        };

        Assert.That(
            BuildingDepthGeometry.IntersectsFootprint(
                polygon,
                new Bounds(new Vector3(0.5f, 3f), new Vector3(0.2f, 0.2f, 0.2f))),
            Is.True);
        Assert.That(
            BuildingDepthGeometry.IntersectsFootprint(
                polygon,
                new Bounds(new Vector3(0.5f, 7f), new Vector3(0.2f, 0.2f, 0.2f))),
            Is.False);
    }

    [Test]
    public void DepthGeometryDetectsCrossingPolygonsWithoutContainedVertices()
    {
        var horizontal = new List<Vector2>
        {
            new(-2f, -0.1f),
            new(2f, -0.1f),
            new(2f, 0.1f),
            new(-2f, 0.1f)
        };
        var vertical = new List<Vector2>
        {
            new(-0.1f, -2f),
            new(0.1f, -2f),
            new(0.1f, 2f),
            new(-0.1f, 2f)
        };

        Assert.That(BuildingDepthGeometry.IntersectsPolygon(horizontal, vertical), Is.True);
    }

    [Test]
    public void DepthSortingUsesHysteresisAtTheSurfaceBoundary()
    {
        Assert.That(
            DepthOcclusionCoordinator.ResolveBehind(1.04f, 1f, false, 0.05f),
            Is.False);
        Assert.That(
            DepthOcclusionCoordinator.ResolveBehind(1.06f, 1f, false, 0.05f),
            Is.True);
        Assert.That(
            DepthOcclusionCoordinator.ResolveBehind(0.96f, 1f, true, 0.05f),
            Is.True);
        Assert.That(
            DepthOcclusionCoordinator.ResolveBehind(0.94f, 1f, true, 0.05f),
            Is.False);
    }

    [Test]
    public void DepthSurfaceUsesTheClosestLocalGroundDepth()
    {
        var surfaceObject = new GameObject("Surface");
        surfaceObject.AddComponent<MeshRenderer>();
        var surface = surfaceObject.AddComponent<DepthOcclusionSurface>();
        try
        {
            surface.Configure(
                new List<Vector3>
                {
                    new(0f, 0f),
                    new(2f, 0f),
                    new(2f, 2f),
                    new(0f, 2f)
                },
                new List<Vector3>
                {
                    new(0f, 0f),
                    new(2f, 1f),
                    new(2f, 2f),
                    new(0f, 1f)
                },
                new Vector3(1f, 1.5f),
                Vector2.zero,
                Vector2.one);

            Assert.That(
                surface.GetDepthKey(new Vector2(0.25f, 0.2f)),
                Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(
                surface.GetDepthKey(new Vector2(1.75f, 0.9f)),
                Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(surface.DepthKey, Is.EqualTo(1.5f).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(surfaceObject);
        }
    }

    [Test]
    public void TestBuildingPresentationKeepsBaseDoorAlphaAcrossRefresh()
    {
        var buildingObject = new GameObject("Building");
        var doorObject = new GameObject("Door");
        doorObject.transform.SetParent(buildingObject.transform);
        var doorRenderer = doorObject.AddComponent<SpriteRenderer>();
        doorRenderer.color = new Color(1f, 1f, 1f, 0.75f);
        var presentation = buildingObject.AddComponent<TestBuildingPresentation>();

        try
        {
            presentation.RefreshRenderers();
            presentation.SetOcclusionAlpha(0.2f);
            presentation.RefreshRenderers();
            presentation.SetOcclusionAlpha(1f);

            Assert.That(doorRenderer.color.a, Is.EqualTo(0.75f).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(buildingObject);
        }
    }

    [Test]
    public void TestBuildingPresentationIgnoresDestroyedRenderers()
    {
        var buildingObject = new GameObject("Building");
        var meshObject = new GameObject("Generated Mesh");
        meshObject.transform.SetParent(buildingObject.transform);
        meshObject.AddComponent<MeshRenderer>();
        var presentation = buildingObject.AddComponent<TestBuildingPresentation>();

        try
        {
            presentation.RefreshRenderers();
            Object.DestroyImmediate(meshObject);

            Assert.DoesNotThrow(() => presentation.SetOcclusionAlpha(0.2f));
        }
        finally
        {
            Object.DestroyImmediate(buildingObject);
        }
    }

    [Test]
    public void RearEdgeUsesThePlayerFootprintCenterAtItsLogicalDepth()
    {
        var polygon = new List<Vector2>
        {
            new(-1f, 0.5f),
            new(4f, 3f),
            new(-1f, 5.5f),
            new(-6f, 3f)
        };
        var playerFootprint = new Bounds(
            new Vector3(0.5f, 4.8f),
            new Vector3(0.2f, 0.6f, 0.2f));

        Assert.That(
            BuildingDepthGeometry.TryGetRearEdgeY(
                polygon,
                playerFootprint,
                out var rearEdgeY),
            Is.True);
        Assert.That(rearEdgeY, Is.EqualTo(4.75f).Within(0.0001f));
    }

    [Test]
    public void DoorFacingMirrorsVerticalWallDirections()
    {
        var style = ScriptableObject.CreateInstance<BuildingVisualStyle>();
        try
        {
            Assert.That(style.ShouldFlipEntranceX(GridEdgeDirection.South), Is.False);
            Assert.That(style.ShouldFlipEntranceX(GridEdgeDirection.North), Is.False);
            Assert.That(style.ShouldFlipEntranceX(GridEdgeDirection.East), Is.True);
            Assert.That(style.ShouldFlipEntranceX(GridEdgeDirection.West), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(style);
        }
    }
}
