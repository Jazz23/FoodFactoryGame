// Verifies deterministic PNG comparison and ROI evidence reporting in isolated editor tests.
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class VisualRegressionEvidenceTests
{
    private string temporaryDirectory;

    [SetUp]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "FoodFactoryVisualRegressionTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    [Test]
    public void IdenticalImagesPassAndWriteReport()
    {
        var baseline = WritePng("baseline.png", 4, 3, new Color32(10, 20, 30, 255));
        var after = WritePng("after.png", 4, 3, new Color32(10, 20, 30, 255));
        var reportPath = Path.Combine(temporaryDirectory, "comparison.json");

        var report = VisualRegressionEvidence.ComparePngs(
            baseline,
            after,
            new RectInt(0, 0, 4, 3),
            0,
            reportPath);

        Assert.That(report.passed, Is.True);
        Assert.That(report.totalPixels, Is.EqualTo(12));
        Assert.That(report.differingPixels, Is.EqualTo(0));
        Assert.That(report.maxChannelDelta, Is.EqualTo(0));
        Assert.That(File.Exists(reportPath), Is.True);
        Assert.That(File.ReadAllText(reportPath), Does.Contain("\"totalPixels\": 12"));
    }

    [Test]
    public void ChangedPixelInsideRoiFails()
    {
        var baseline = WritePng("baseline.png", 4, 3, new Color32(10, 20, 30, 255));
        var after = WritePng("after.png", 4, 3, new Color32(10, 20, 30, 255), (1, 1, new Color32(200, 20, 30, 255)));

        var report = VisualRegressionEvidence.ComparePngs(
            baseline,
            after,
            new RectInt(1, 1, 1, 1),
            0,
            Path.Combine(temporaryDirectory, "comparison.json"));

        Assert.That(report.passed, Is.False);
        Assert.That(report.totalPixels, Is.EqualTo(1));
        Assert.That(report.differingPixels, Is.EqualTo(1));
        Assert.That(report.maxChannelDelta, Is.EqualTo(190));
    }

    [Test]
    public void ChangedPixelOutsideRoiIsIgnored()
    {
        var baseline = WritePng("baseline.png", 4, 3, new Color32(10, 20, 30, 255));
        var after = WritePng("after.png", 4, 3, new Color32(10, 20, 30, 255), (3, 2, new Color32(200, 20, 30, 255)));

        var report = VisualRegressionEvidence.ComparePngs(
            baseline,
            after,
            new RectInt(0, 0, 2, 2),
            0,
            Path.Combine(temporaryDirectory, "comparison.json"));

        Assert.That(report.passed, Is.True);
        Assert.That(report.totalPixels, Is.EqualTo(4));
        Assert.That(report.differingPixels, Is.EqualTo(0));
    }

    [Test]
    public void DimensionMismatchFailsAndWritesDimensions()
    {
        var baseline = WritePng("baseline.png", 4, 3, new Color32(10, 20, 30, 255));
        var after = WritePng("after.png", 5, 3, new Color32(10, 20, 30, 255));
        var reportPath = Path.Combine(temporaryDirectory, "comparison.json");

        var report = VisualRegressionEvidence.ComparePngs(
            baseline,
            after,
            new RectInt(0, 0, 4, 3),
            0,
            reportPath);

        Assert.That(report.passed, Is.False);
        Assert.That(report.dimensionsMatch, Is.False);
        Assert.That(report.baselineWidth, Is.EqualTo(4));
        Assert.That(report.afterWidth, Is.EqualTo(5));
        Assert.That(File.Exists(reportPath), Is.True);
    }

    [Test]
    public void RoiIsClampedAndReportedInJson()
    {
        var baseline = WritePng("baseline.png", 4, 3, new Color32(10, 20, 30, 255));
        var after = WritePng("after.png", 4, 3, new Color32(10, 20, 30, 255));
        var reportPath = Path.Combine(temporaryDirectory, "comparison.json");

        var report = VisualRegressionEvidence.ComparePngs(
            baseline,
            after,
            new RectInt(-1, -1, 3, 3),
            0,
            reportPath);

        Assert.That(report.passed, Is.True);
        Assert.That(report.roiWasClamped, Is.True);
        Assert.That(report.roiValid, Is.True);
        Assert.That(report.clampedRoi, Is.EqualTo(new RectInt(0, 0, 2, 2)));
        Assert.That(report.totalPixels, Is.EqualTo(4));
        Assert.That(File.ReadAllText(reportPath), Does.Contain("requestedRoi"));
        Assert.That(File.ReadAllText(reportPath), Does.Contain("clampedRoi"));
    }

    private string WritePng(string fileName, int width, int height, Color32 fill, params (int x, int y, Color32 color)[] changes)
    {
        var pixels = new Color32[width * height];
        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index] = fill;
        }

        foreach (var change in changes)
        {
            pixels[change.y * width + change.x] = change.color;
        }

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            var path = Path.Combine(temporaryDirectory, fileName);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            return path;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
