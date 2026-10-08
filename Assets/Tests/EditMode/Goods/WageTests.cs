// Verifies hiring, firing and wages (decision 0039): a hire stands beside the player on their site under the site's floor-area
// cap and replays without a second hire; wages are charged once per game hour in hire order, an employee the company cannot pay
// goes unpaid (newest first) and is paid again for one hour once cash covers it, never back pay; firing is refused while the
// hands hold anything and takes the record, grant and hands; wage state survives save and load, and a v18 save does not charge
// for hours already on its clock. Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class WageTests
    {
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.db");

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryWageTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: a 20x20 site "shop" with a 12x12 shell at (0, 0) (100 interior cells, so a cap of 4 employees), its
        // company with the given cash, and a player "boss" standing there with a 10-slot inventory holding 3 dough.
        private static GoodsWorld CreateWorld(long cash)
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = GoodsWorld.InventoryLocationId("boss"), SiteId = "shop", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new GoodsLot
            {
                Id = "dough", ItemId = "dough", OwnerId = "shop", LocationId = GoodsWorld.InventoryLocationId("boss"), Quantity = 3,
                SpoilAfterSeconds = 100000
            });
            world.Bootstrap(new SiteLayout { SiteId = "shop", Width = 20, Depth = 20 });
            world.Bootstrap(new GoodsBuilding
            {
                Id = "shell", SiteId = "shop", CellX = 0, CellZ = 0, Width = 12, Depth = 12, Doors = new List<GridCell> { new() { X = 5, Z = 0 } }
            });
            world.Bootstrap(new GoodsCompany { Id = "company", Cash = cash, SiteIds = new List<string> { "shop" } });
            world.Grant("boss", "shop");
            return world;
        }

        private static string Hire(GoodsWorld world, string requestId)
        {
            var outcome = world.Hire("boss", requestId, 1f, 0f, 2f, 90f);
            Assert.That(outcome.Accepted, Is.True, outcome.Reason);
            return outcome.EquipmentId;
        }

        private static long Cash(GoodsWorld world) => world.Snapshot().Companies.Single().Cash;

        private static bool Unpaid(GoodsWorld world, string id) => world.Snapshot().Employees.Single(x => x.Id == id).Unpaid;

        [Test]
        public void HireStandsBesideThePlayerOnTheirSiteAndReplaysWithoutASecondHire()
        {
            var world = CreateWorld(0);
            var id = Hire(world, "h1");
            var employee = world.Snapshot().Employees.Single();
            Assert.That((employee.Id, employee.SiteId, employee.Name, employee.X, employee.Z, employee.Yaw),
                Is.EqualTo(("employee-1", "shop", "Employee 1", 1f, 2f, 90f)));
            Assert.That(world.CanView(id, "shop"), Is.True, "A hire gets its site grant.");
            Assert.That(world.Snapshot().Locations.Single(x => x.Id == GoodsWorld.InventoryLocationId(id)).Capacity,
                Is.EqualTo(GoodsWorld.HiredHandSlots));
            var replay = world.Hire("boss", "h1", 5f, 0f, 5f, 0f);
            Assert.That((replay.Accepted, replay.EquipmentId), Is.EqualTo((true, id)));
            Assert.That(world.Snapshot().Employees.Count, Is.EqualTo(1), "A retried request never hires twice.");
            Assert.That(Hire(world, "h2"), Is.EqualTo("employee-2"));
        }

        [Test]
        public void HireIsRefusedOverTheCapOffTheSiteAndWithoutACompany()
        {
            var world = CreateWorld(0);
            for (var index = 0; index < 4; index++) Hire(world, "h" + index);
            Assert.That(world.Hire("boss", "h-full", 0f, 0f, 0f, 0f).Reason, Is.EqualTo("employee-cap"));
            Assert.That(world.Hire("stranger", "h-x", 0f, 0f, 0f, 0f).Reason, Is.EqualTo("forbidden"), "No inventory on any site.");

            var free = new GoodsWorld("test-world");
            free.Bootstrap(new GoodsLocation { Id = GoodsWorld.InventoryLocationId("boss"), SiteId = "lot", Kind = "carried", Capacity = 1 });
            free.Grant("boss", "lot");
            Assert.That(free.Hire("boss", "h", 0f, 0f, 0f, 0f).Reason, Is.EqualTo("no-company"));
        }

        [Test]
        public void CapCountsInteriorCellsOverEveryStoreyAndIsAtLeastOne()
        {
            var state = new GoodsSnapshot();
            Assert.That(GoodsWorld.EmployeeCap(state, "shop"), Is.EqualTo(1), "No buildings still allows one.");
            state.Buildings.Add(new GoodsBuilding { Id = "a", SiteId = "shop", Width = 12, Depth = 12 });
            Assert.That(GoodsWorld.EmployeeCap(state, "shop"), Is.EqualTo(4), "10 x 10 interior cells.");
            state.Buildings.Add(new GoodsBuilding { Id = "b", SiteId = "shop", CellX = 13, Width = 7, Depth = 7, Floors = 2 });
            Assert.That(GoodsWorld.EmployeeCap(state, "shop"), Is.EqualTo(6), "100 + 2 storeys of 25 cells.");
            Assert.That(GoodsWorld.EmployeeCap(state, "elsewhere"), Is.EqualTo(1));
        }

        [Test]
        public void WagesAreChargedEachGameHourAndTheNewestGoesUnpaidFirst()
        {
            var world = CreateWorld(3500);
            var first = Hire(world, "h1");
            var second = Hire(world, "h2");
            world.Advance(GoodsWorld.GameHourSeconds - 1);
            Assert.That(Cash(world), Is.EqualTo(3500), "Nothing is charged before the hour ends.");
            world.Advance(1);
            Assert.That(Cash(world), Is.EqualTo(1500), "Two wages at the first hour.");
            world.Advance(GoodsWorld.GameHourSeconds);
            Assert.That(Cash(world), Is.EqualTo(500), "Only the first could be paid.");
            Assert.That((Unpaid(world, first), Unpaid(world, second)), Is.EqualTo((false, true)));
            Assert.That(world.IsUnpaid(second), Is.True);
            Assert.That(world.View("boss", "shop").CompanyWageCentsPerHour, Is.EqualTo(2 * GoodsWorld.WageCentsPerHour));

            world.Advance(GoodsWorld.GameHourSeconds);
            Assert.That(Cash(world), Is.EqualTo(500), "Cash never goes below zero.");
            Assert.That((Unpaid(world, first), Unpaid(world, second)), Is.EqualTo((true, true)));
        }

        [Test]
        public void AnUnpaidEmployeeIsPaidOneHourOnceCashCoversItWithoutBackPay()
        {
            var world = CreateWorld(0);
            var id = Hire(world, "h1");
            world.Advance(GoodsWorld.GameHourSeconds * 3);
            Assert.That(Unpaid(world, id), Is.True);
            Assert.That(world.AdjustCashDurably("company", 2500, PathForSave), Is.Null);
            world.Advance(1);
            Assert.That((Unpaid(world, id), Cash(world)), Is.EqualTo((false, 1500L)), "One hour, not the three missed.");
            world.Advance(GoodsWorld.GameHourSeconds);
            Assert.That(Cash(world), Is.EqualTo(500), "The next hour is charged as usual.");
        }

        [Test]
        public void FiringIsRefusedWhileHoldingAndTakesTheRecordGrantAndHands()
        {
            var world = CreateWorld(0);
            var id = Hire(world, "h1");
            var hands = GoodsWorld.InventoryLocationId(id);
            Assert.That(world.Transfer("boss", new TransferIntent { RequestId = "t1", LotId = "dough", DestinationId = hands, Quantity = 3 }).Accepted, Is.True);
            Assert.That(world.Fire("boss", "f1", id).Reason, Is.EqualTo("holding"));
            Assert.That(world.Fire("stranger", "f2", id).Reason, Is.EqualTo("forbidden"));
            var lot = world.Snapshot().Lots.Single(x => x.LocationId == hands);
            Assert.That(world.Transfer("boss", new TransferIntent
            {
                RequestId = "t2", LotId = lot.Id, DestinationId = GoodsWorld.InventoryLocationId("boss"), Quantity = 3
            }).Accepted, Is.True, "A player takes goods out of an employee's hands.");

            var fired = world.Fire("boss", "f3", id);
            Assert.That((fired.Accepted, fired.EquipmentId), Is.EqualTo((true, id)), fired.Reason);
            var state = world.Snapshot();
            Assert.That(state.Employees, Is.Empty);
            Assert.That(state.Grants.Any(x => x.PlayerId == id), Is.False);
            Assert.That(state.Locations.Any(x => x.Id == hands), Is.False);
            Assert.That(state.Lots.Where(x => x.ItemId == "dough").Sum(x => x.Quantity), Is.EqualTo(3), "No goods were lost.");
            Assert.That(world.Fire("boss", "f3", id).Accepted, Is.True, "A replay returns the first outcome.");
            Assert.That(Hire(world, "h2"), Is.EqualTo("employee-2"), "A fired employee's ID is never handed out again.");
        }

        [Test]
        public void WageStateSurvivesSaveAndLoad()
        {
            var world = CreateWorld(1500);
            var id = Hire(world, "h1");
            Assert.That(world.HireDurably("boss", "h2", 0f, 0f, 0f, 0f, PathForSave).Accepted, Is.True);
            Assert.That(world.TryAdvanceDurably(GoodsWorld.GameHourSeconds, PathForSave), Is.True);
            var loaded = GoodsSnapshotStore.Load(PathForSave);
            var state = loaded.Snapshot();
            Assert.That((state.WagesPaidHour, state.NextEmployeeNumber, state.Companies.Single().Cash), Is.EqualTo((1L, 2L, 500L)));
            Assert.That(state.Employees.Select(x => x.Unpaid), Is.EqualTo(new[] { false, true }), "Only the first could be paid.");
            Assert.That(state.Employees[0].Id, Is.EqualTo(id));
            loaded.Advance(1);
            Assert.That((loaded.Snapshot().Companies.Single().Cash, loaded.IsUnpaid(id)), Is.EqualTo((500L, false)),
                "Recovery does not charge the hour again.");
        }

        [Test]
        public void SchemaV18RowCountsTheHoursOnItsClockAsPaid()
        {
            var legacy = CreateWorld(20000);
            Hire(legacy, "h1");
            legacy.Advance(GoodsWorld.GameHourSeconds * 10 + 5);
            var before = Cash(legacy);
            GoodsSnapshotStore.Save(legacy, PathForSave);
            var v18 = JsonUtility.ToJson(legacy.Snapshot()).Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":18");
            v18 = Regex.Replace(v18, ",\"(WagesPaidHour|NextEmployeeNumber|CompanyWageCentsPerHour)\":\\d+", "").Replace(",\"Unpaid\":false", "");
            Assert.That(v18, Does.Not.Contain("WagesPaidHour").And.Not.Contain("Unpaid"));
            SnapshotDatabase.WritePayload(PathForSave, v18);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(18), "The row looks like a real v18 commit.");

            var loaded = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(loaded.Snapshot().SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(loaded.Snapshot().WagesPaidHour, Is.EqualTo(10));
            loaded.Advance(1);
            Assert.That(loaded.Snapshot().Companies.Single().Cash, Is.EqualTo(before), "No back pay for hours before wages existed.");
        }
    }
}
