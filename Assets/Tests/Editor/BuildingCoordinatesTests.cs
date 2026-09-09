// Verifies pure building coordinate conversions, floor elevation, and door mapping adapters.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class BuildingCoordinatesTests
{
    [Test]
    public void PlanarConversionsRoundTripFractionalPositionsOutsideTheFootprint()
    {
        var anchors = new[]
        {
            new Vector3Int(7, -4, 0),
            new Vector3Int(-11, 9, 123)
        };
        var localPositions = new[]
        {
            new Vector2(0.25f, -0.75f),
            new Vector2(-100.5f, 75.25f)
        };

        foreach (var anchor in anchors)
        {
            foreach (var localPosition in localPositions)
            {
                var exteriorPosition = BuildingCoordinates.LocalToExteriorLogical(
                    anchor,
                    localPosition);
                var roundTrip = BuildingCoordinates.ExteriorLogicalToLocal(
                    anchor,
                    exteriorPosition);

                AssertVectorApproximately(localPosition, roundTrip);
            }
        }
    }

    [Test]
    public void PlanarConversionsIgnoreAnchorElevationAndPreserveLogicalDistances()
    {
        var localStart = new Vector2(-2.5f, 4.25f);
        var localEnd = new Vector2(3.75f, -1.5f);
        var anchor = new Vector3Int(-8, 13, 42);
        var alternateElevationAnchor = new Vector3Int(anchor.x, anchor.y, -420);
        var exteriorStart = BuildingCoordinates.LocalToExteriorLogical(anchor, localStart);
        var exteriorEnd = BuildingCoordinates.LocalToExteriorLogical(
            alternateElevationAnchor,
            localEnd);

        AssertVectorApproximately(
            BuildingCoordinates.LocalToExteriorLogical(anchor, localStart),
            BuildingCoordinates.LocalToExteriorLogical(alternateElevationAnchor, localStart));
        AssertVectorApproximately(localEnd, BuildingCoordinates.ExteriorLogicalToLocal(
            anchor,
            exteriorEnd));
        AssertVectorApproximately(localEnd - localStart, exteriorEnd - exteriorStart);
    }

    [TestCase(0, 2f, 0f)]
    [TestCase(2, 1.25f, 2.5f)]
    [TestCase(-2, 3f, 0f)]
    [TestCase(3, 0f, 0f)]
    [TestCase(3, -1.5f, 0f)]
    public void FloorElevationMatchesTheExistingNonNegativeRule(
        int floorIndex,
        float storyHeight,
        float expectedElevation)
    {
        Assert.That(
            BuildingCoordinates.GetFloorElevation(floorIndex, storyHeight),
            Is.EqualTo(expectedElevation).Within(0.0001f));
        Assert.That(
            TestBuildingCreator.GetStoryBaseHeight(storyHeight, floorIndex),
            Is.EqualTo(expectedElevation).Within(0.0001f));
    }

    [Test]
    public void MappingOverloadsMatchForEveryExteriorSideAndMultipleDoorOffsets()
    {
        var anchor = new Vector3Int(8, -6, 37);
        var size = new Vector2Int(5, 4);
        var layoutObject = new GameObject("Coordinate Mapping Layout");
        try
        {
            var layout = layoutObject.AddComponent<TestBuildingLayout>();
            layout.Configure(anchor, size);
            var spans = new List<TestBuildingCreator.ExteriorWallSpan>();
            TestBuildingCreator.GetExteriorWallSpans(anchor, size, spans);

            foreach (var direction in new[]
            {
                GridEdgeDirection.South,
                GridEdgeDirection.West,
                GridEdgeDirection.North,
                GridEdgeDirection.East
            })
            {
                var wall = spans.Find(span => span.Direction == direction && !span.IsCorner);
                foreach (var doorOffset in new[] { 0.2f, 0.8f })
                {
                    Assert.That(
                        TestBuildingInteriorMapping.TryGetMapping(
                            anchor,
                            size,
                            wall,
                            doorOffset,
                            out var directExteriorDoor,
                            out var directExteriorArrival,
                            out var directInteriorArrival,
                            out var directNormalizedPosition),
                        Is.True);
                    Assert.That(
                        TestBuildingInteriorMapping.TryGetMapping(
                            layout,
                            wall,
                            doorOffset,
                            out var layoutExteriorDoor,
                            out var layoutExteriorArrival,
                            out var layoutInteriorArrival,
                            out var layoutNormalizedPosition),
                        Is.True);

                    AssertVectorApproximately(directExteriorDoor, layoutExteriorDoor);
                    AssertVectorApproximately(directExteriorArrival, layoutExteriorArrival);
                    AssertVectorApproximately(directInteriorArrival, layoutInteriorArrival);
                    Assert.That(
                        layoutNormalizedPosition,
                        Is.EqualTo(directNormalizedPosition).Within(0.0001f));
                    Assert.That(directNormalizedPosition, Is.InRange(0f, 1f));
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(layoutObject);
        }
    }

    private static void AssertVectorApproximately(Vector2 expected, Vector2 actual)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
    }
}
