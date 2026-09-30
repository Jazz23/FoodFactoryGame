// Verifies the one placement rule (decision 0031) on a real generated layout in an isolated folder: the starting site stands at
// the scene origin exactly as SiteGridSpace always put it, every other lot's grid centre stands at its map position relative to
// the starting lot's at its building's elevation difference, site cells round-trip through the scene, scene points convert
// back to map metres and to the lot they stand on, and dev layouts (no placement) keep every site at the origin.
using System;
using System.IO;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.World;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class SitePlacementTests
    {
        private string _directory;
        private WorldLayout _layout;
        private SitePlacement _placement;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactorySitePlacementTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _layout = WorldGeneration.PrepareLayout(Path.Combine(_directory, SessionOptions.WorldFileName),
                Path.Combine(_directory, SessionOptions.LegacyWorldFileName), DevWorld.WorldId, "piece-two").Layout;
            _placement = SitePlacement.For(_layout);
            Assert.That(_placement, Is.Not.Null, "A format 3 layout has a placement.");
        }

        [TearDown]
        public void TearDown()
        {
            SitePlacement.Use(null);
            GoodsSnapshotStore.Release(Path.Combine(_directory, SessionOptions.WorldFileName));
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private WorldLot Start => _layout.Lots.Single(x => x.BuildingId == _layout.StartRestaurantId);

        private float Elevation(WorldLot lot) => _layout.Buildings.Single(x => x.Id == lot.BuildingId).ElevationCm / 100f;

        // A lot other than the start, far enough away and at a different elevation to exercise both parts of the shift.
        private WorldLot Other => _layout.Lots.Where(x => x.Id != Start.Id)
            .OrderByDescending(x => Mathf.Abs(Elevation(x) - Elevation(Start))).ThenBy(x => x.Id, StringComparer.Ordinal).First();

        [Test]
        public void TheStartingSiteMatchesTodaysGrid()
        {
            var grid = new SiteLayout { SiteId = Start.SiteId, Width = Start.Width, Depth = Start.Depth };
            var before = SiteGridSpace.FootprintCenter(grid, 3, 1, 2, 1, 1);
            SitePlacement.Use(_placement);
            Assert.That(_placement.SiteOrigin(Start.SiteId), Is.EqualTo(Vector3.zero));
            Assert.That(SiteGridSpace.FootprintCenter(grid, 3, 1, 2, 1, 1), Is.EqualTo(before), "The starting site does not move.");
            Assert.That(_placement.ToScene(Start.X + Start.Width / 2f, Start.Z + Start.Depth / 2f, Elevation(Start)).magnitude, Is.LessThan(1e-3f));
        }

        [Test]
        public void AnotherLotStandsWhereTheMapPutsItsBuilding()
        {
            var lot = Other;
            var origin = _placement.SiteOrigin(lot.SiteId);
            var expected = new Vector3(lot.X + lot.Width / 2f - (Start.X + Start.Width / 2f), Elevation(lot) - Elevation(Start),
                lot.Z + lot.Depth / 2f - (Start.Z + Start.Depth / 2f));
            Assert.That(Vector3.Distance(origin, expected), Is.LessThan(1e-3f), $"{origin} against {expected}.");
            Assert.That(Mathf.Abs(origin.y), Is.GreaterThan(0f), "The chosen lot really differs in elevation from the start.");

            // Site cell (x, z) is map cell (lot.X + x, lot.Z + z) (decision 0028): its centre in the scene is that map point.
            SitePlacement.Use(_placement);
            var grid = new SiteLayout { SiteId = lot.SiteId, Width = lot.Width, Depth = lot.Depth };
            var cell = SiteGridSpace.FootprintCenter(grid, 2, 1, 1, 1);
            var map = _placement.ToScene(lot.X + 2.5f, lot.Z + 1.5f, Elevation(lot));
            Assert.That(Vector3.Distance(cell, map), Is.LessThan(1e-3f), $"{cell} against the map's {map}.");
        }

        [Test]
        public void CellsRoundTripThroughTheScene()
        {
            SitePlacement.Use(_placement);
            var lot = Other;
            var grid = new SiteLayout { SiteId = lot.SiteId, Width = lot.Width, Depth = lot.Depth };
            for (var x = 0; x < lot.Width; x += 3)
            for (var z = 0; z < lot.Depth; z += 3)
            {
                var point = SiteGridSpace.FootprintCenter(grid, x, z, 1, 1, 2);
                Assert.That(SiteGridSpace.AnchorAt(grid, point, 1, 1), Is.EqualTo((x, z)));
                Assert.That(SiteGridSpace.LevelAt(grid, point.y + 0.05f), Is.EqualTo(2));
                Assert.That(_placement.SiteAt(point), Is.EqualTo(lot.SiteId));
                var mapPoint = _placement.ToMap(point);
                Assert.That((Mathf.FloorToInt(mapPoint.x), Mathf.FloorToInt(mapPoint.y)), Is.EqualTo((lot.X + x, lot.Z + z)));
            }
        }

        [Test]
        public void DistanceToALotIsZeroInsideAndGrowsOutside()
        {
            var lot = Other;
            Assert.That(SitePlacement.Distance(lot, new Vector2(lot.X + 0.5f, lot.Z + 0.5f)), Is.Zero);
            Assert.That(SitePlacement.Distance(lot, new Vector2(lot.X - 3f, lot.Z - 4f)), Is.EqualTo(5f).Within(1e-4f));
            Assert.That(_placement.SiteAt(_placement.ToScene(lot.X - 50f, lot.Z - 50f)), Is.Not.EqualTo(lot.SiteId));
        }

        [Test]
        public void DevLayoutsKeepEverySiteAtTheOrigin()
        {
            Assert.That(SitePlacement.For(null), Is.Null);
            SitePlacement.Use(null);
            var grid = new SiteLayout { SiteId = DevWorld.SiteId, Width = 40, Depth = 40 };
            Assert.That(SiteGridSpace.Origin(grid), Is.EqualTo(Vector3.zero));
            Assert.That(SiteGridSpace.FootprintCenter(grid, 20, 20, 1, 1), Is.EqualTo(new Vector3(0.5f, 0f, 0.5f)));
        }
    }
}
