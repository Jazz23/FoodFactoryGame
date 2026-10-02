// Verifies the routes customer figures walk on a drawn site (decision 0036, SiteWalk): from the street they come in through a
// door and never cross a wall, the register's service spot and its queue stand on open cells reached from the lot's edges, and
// a room without a door gives no route (the presenter then falls back to the NavMesh). TEST-ONLY layout; no files are used.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Customers;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class SiteWalkTests
    {
        // TEST-ONLY: a 16x12 site, an 8x6 restaurant at (4,4) with one door at (7,4) in its south wall, and a 2x1 register at
        // (8,7) facing south (rotation 2) toward the door.
        private static GoodsSnapshot Site(bool door = true) => new()
        {
            SiteLayouts = { new SiteLayout { SiteId = "walk", Width = 16, Depth = 12 } },
            Buildings =
            {
                new GoodsBuilding
                {
                    Id = "shop", SiteId = "walk", CellX = 4, CellZ = 4, Width = 8, Depth = 6,
                    Doors = door ? new List<GridCell> { new() { X = 7, Z = 4 } } : new List<GridCell>()
                }
            },
            Equipment =
            {
                new GoodsEquipment
                {
                    Id = "register", Kind = GoodsWorld.CounterKind, SiteId = "walk", State = EquipmentState.Placed, CellX = 8, CellZ = 7,
                    Width = 2, Depth = 1, Rotation = 2, InputCapacity = 1, OutputCapacity = 1
                }
            }
        };

        private static (int X, int Z) CellOf(SiteLayout layout, Vector3 point) => SiteGridSpace.AnchorAt(layout, point, 1, 1);

        [Test]
        public void FromTheStreetAFigureComesInThroughTheDoorAndNeverCrossesAWall()
        {
            var site = Site();
            var layout = site.SiteLayouts.Single();
            var shell = site.Buildings.Single();
            var walk = SiteWalk.For(site, layout);
            var spot = walk.ServiceSpot(site.Equipment.Single());
            Assert.That(spot.HasValue, Is.True);
            Assert.That(CellOf(layout, spot.Value), Is.EqualTo((8, 6)).Or.EqualTo((9, 6)), "The register is served from the side it faces.");

            var street = SiteGridSpace.FootprintCenter(layout, 7, -4, 1, 1);
            var route = walk.Route(street, spot.Value);
            Assert.That(route, Is.Not.Null);
            var cells = new List<(int X, int Z)>();
            var from = street;
            foreach (var point in route)
            {
                for (var step = 0f; step <= 1f; step += 0.02f) cells.Add(CellOf(layout, Vector3.Lerp(from, point, step)));
                from = point;
            }
            Assert.That(cells.Where(c => SiteGrid.IsWall(shell, c.X, c.Z)), Is.Empty, "No step of the route stands on a wall.");
            Assert.That(cells, Does.Contain((7, 4)), "The way in is the door.");
            Assert.That(CellOf(layout, route[route.Length - 1]), Is.EqualTo(CellOf(layout, spot.Value)));
        }

        [Test]
        public void TheQueueStandsOnOpenCellsBackAlongTheWayIn()
        {
            var site = Site();
            var layout = site.SiteLayouts.Single();
            var walk = SiteWalk.For(site, layout);
            var register = site.Equipment.Single();
            var places = Enumerable.Range(-1, 4).Select(rank => CellOf(layout, walk.ServiceSpot(register, rank).Value)).ToList();
            Assert.That(places.Distinct().Count(), Is.EqualTo(places.Count), "Each place in the line is its own cell.");
            Assert.That(places.All(walk.Open), Is.True);
            for (var index = 1; index < places.Count; index++)
                Assert.That(Mathf.Abs(places[index].X - places[index - 1].X) + Mathf.Abs(places[index].Z - places[index - 1].Z), Is.EqualTo(1),
                    "The line is a walk, one cell after another.");
        }

        [Test]
        public void ARoomWithoutADoorHasNoRouteAndNoServiceSpot()
        {
            var site = Site(false);
            var layout = site.SiteLayouts.Single();
            var walk = SiteWalk.For(site, layout);
            Assert.That(walk.ServiceSpot(site.Equipment.Single()), Is.Null);
            Assert.That(walk.Route(SiteGridSpace.FootprintCenter(layout, 7, -4, 1, 1), SiteGridSpace.FootprintCenter(layout, 8, 6, 1, 1)), Is.Null);
        }
    }
}
