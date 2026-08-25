using NUnit.Framework;
using UnityEngine;

public sealed class SceneGridTests
{
    [Test]
    public void DimetricProjectionUsesTheExistingTwoToOneBasis()
    {
        var xAxis = SceneGrid.Project(GridProjection.Dimetric, new Vector2(1f, 0f));
        var yAxis = SceneGrid.Project(GridProjection.Dimetric, new Vector2(0f, 1f));

        Assert.That(xAxis, Is.EqualTo(new Vector2(1f, 0.5f)));
        Assert.That(yAxis, Is.EqualTo(new Vector2(-1f, 0.5f)));
    }

    [Test]
    public void DimetricProjectionRoundTripsLogicalCoordinates()
    {
        var logicalPosition = new Vector2(1.5f, -0.5f);
        var projectedPosition = SceneGrid.Project(GridProjection.Dimetric, logicalPosition);

        Assert.That(
            SceneGrid.Unproject(GridProjection.Dimetric, projectedPosition),
            Is.EqualTo(logicalPosition));
    }

    [Test]
    public void OrthogonalProjectionPreservesCellSpacing()
    {
        var logicalPosition = new Vector2(0.5f, 1.5f);

        Assert.That(
            SceneGrid.Project(GridProjection.Orthogonal, logicalPosition),
            Is.EqualTo(logicalPosition));
        Assert.That(
            SceneGrid.Unproject(GridProjection.Orthogonal, logicalPosition),
            Is.EqualTo(logicalPosition));
    }
}
