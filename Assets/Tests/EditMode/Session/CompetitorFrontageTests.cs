// Verifies the frontage geometry customer figures use at competitors (decision 0033) on every competitor lot of a real generated
// layout in an isolated folder: the door point, apron point and every drawn queue place stand on the lot outside the building,
// the kerb and street points stand off the lot on its street side, and all of them sit at the building's ground floor.
using System;
using System.IO;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Customers;
using FoodFactoryGame.World;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class CompetitorFrontageTests
    {
        private string _directory;
        private WorldLayout _layout;
        private SitePlacement _placement;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryCompetitorFrontageTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _layout = WorldGeneration.PrepareLayout(Path.Combine(_directory, SessionOptions.WorldFileName),
                Path.Combine(_directory, SessionOptions.LegacyWorldFileName), DevWorld.WorldId, "piece-two").Layout;
            _placement = SitePlacement.For(_layout);
        }

        [TearDown]
        public void TearDown()
        {
            GoodsSnapshotStore.Release(Path.Combine(_directory, SessionOptions.WorldFileName));
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void EveryCompetitorsFiguresStandOnItsApronOrItsStreet()
        {
            var buildings = _layout.Buildings.ToDictionary(x => x.Id, StringComparer.Ordinal);
            var lots = _layout.Lots.Where(x => buildings[x.BuildingId].Ownership == Ownership.Competitor).ToList();
            // Restaurants grew on 2026-10-02 (owner request), so fewer fit a city.
            Assert.That(lots.Count, Is.GreaterThan(150), "A generated city has hundreds of competitors.");
            foreach (var lot in lots)
            {
                var building = buildings[lot.BuildingId];
                var frontage = CompetitorFrontage.For(_placement, lot.Id);
                Assert.That(frontage, Is.Not.Null, lot.Id);
                var floor = _placement.SiteOrigin(lot.SiteId).y;

                void OnApron(Vector3 point, string what)
                {
                    var map = _placement.ToMap(point);
                    Assert.That(Inside(map, lot.X, lot.Z, lot.Width, lot.Depth), Is.True, $"{lot.Id}: {what} on the lot");
                    Assert.That(Inside(map, building.X, building.Z, building.Width, building.Depth), Is.False, $"{lot.Id}: {what} outside the building");
                    Assert.That(point.y, Is.EqualTo(floor).Within(1e-3f), $"{lot.Id}: {what} at the ground floor");
                }

                void OnStreet(Vector3 point, string what)
                {
                    Assert.That(Inside(_placement.ToMap(point), lot.X, lot.Z, lot.Width, lot.Depth), Is.False, $"{lot.Id}: {what} off the lot");
                    Assert.That(Vector3.Dot(point - frontage.Door, frontage.Outward), Is.GreaterThan(0f), $"{lot.Id}: {what} on the street side");
                }

                OnApron(frontage.Door, "door");
                OnApron(frontage.Apron, "apron");
                for (var rank = 0; rank < CompetitorFrontage.QueueSpots; rank++) OnApron(frontage.QueueSpot(rank), "queue place " + rank);
                OnStreet(frontage.Kerb, "kerb");
                foreach (var point in frontage.Street) OnStreet(point, "street point");
                Assert.That(frontage.Arrive(frontage.Door).Last(), Is.EqualTo(frontage.Door));
                Assert.That(frontage.Depart(frontage.Street[0]).First(), Is.EqualTo(frontage.Apron));
            }
            Assert.That(CompetitorFrontage.For(_placement, "lot-unknown"), Is.Null);
            Assert.That(CompetitorFrontage.For(null, lots[0].Id), Is.Null);
        }

        private static bool Inside(Vector2 map, int x, int z, int width, int depth) =>
            map.x > x && map.x < x + width && map.y > z && map.y < z + depth;
    }
}
